using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>A pre-start validation failure. It never represents a partially published runtime.</summary>
public sealed record RuntimeResolutionError(string Code, RuntimePackageId? Package, string Detail);

/// <summary>An immutable deterministic manifest plan. Its fingerprint does not yet include source bytes.</summary>
public sealed class RuntimeCompositionPlan
{
    public ReadOnlyCollection<RuntimePackageManifest> OrderedPackages { get; }
    public string ManifestFingerprint { get; }

    internal RuntimeCompositionPlan(List<RuntimePackageManifest> ordered, string fingerprint)
    {
        OrderedPackages = ordered.AsReadOnly();
        ManifestFingerprint = fingerprint;
    }
}

/// <summary>Resolves bounded package/capability/order metadata before compiler or authority startup.</summary>
public static class RuntimeCompositionPlanner
{
    public const int MaximumPackages = 128;

    public static bool TryResolve(IEnumerable<RuntimePackageManifest> selected,
        out RuntimeCompositionPlan? plan, out RuntimeResolutionError? error)
    {
        ArgumentNullException.ThrowIfNull(selected);
        plan = null; error = null;
        var packages = new Dictionary<RuntimePackageId, RuntimePackageManifest>();
        foreach (RuntimePackageManifest? item in selected)
        {
            if (item is null) return Fail("null-package", null, "Selected package is null.", out error);
            if (packages.Count == MaximumPackages)
                return Fail("package-capacity", item.Id, "Selected package count exceeds 128.", out error);
            if (!packages.TryAdd(item.Id, item))
                return Fail("duplicate-package", item.Id, "Selected package identity is duplicated.", out error);
        }

        var providers = new Dictionary<PackageCapabilityId, RuntimePackageId>();
        foreach (RuntimePackageManifest item in packages.Values)
            foreach (PackageCapabilityId capability in item.Provides)
                if (!providers.TryAdd(capability, item.Id))
                    return Fail("duplicate-capability", item.Id,
                        $"Capability {capability} is also provided by {providers[capability]}.", out error);

        var serviceProviders = new Dictionary<RuntimeServiceId, RuntimePackageId>();
        foreach (RuntimePackageManifest item in packages.Values)
            foreach (RuntimeServiceId service in item.Services)
                if (!serviceProviders.TryAdd(service, item.Id))
                    return Fail("duplicate-service", item.Id,
                        $"Runtime service {service} is also provided by {serviceProviders[service]}.", out error);

        var edges = packages.Keys.ToDictionary(id => id, _ => new HashSet<RuntimePackageId>());
        foreach (RuntimePackageManifest item in packages.Values)
        {
            foreach (RuntimePackageDependency dependency in item.Dependencies)
            {
                if (!packages.TryGetValue(dependency.Package, out RuntimePackageManifest? provider))
                    return Fail("missing-package", item.Id, $"Required package {dependency.Package} is absent.", out error);
                if (!dependency.Accepts(provider.Version))
                    return Fail("package-version", item.Id,
                        $"Required package {dependency.Package} needs [{dependency.MinimumVersion}, {dependency.MaximumVersionExclusive?.ToString() ?? "unbounded"}).", out error);
                if (!AddEdge(provider, item, edges, out error)) return false;
            }
            foreach (PackageCapabilityId capability in item.Requires)
            {
                if (!providers.TryGetValue(capability, out RuntimePackageId providerId))
                    return Fail("missing-capability", item.Id, $"Required capability {capability} is absent.", out error);
                if (providerId != item.Id && !AddEdge(packages[providerId], item, edges, out error)) return false;
            }
            foreach (PackageCapabilityId capability in item.Optional)
                if (providers.TryGetValue(capability, out RuntimePackageId providerId) && providerId != item.Id &&
                    !AddEdge(packages[providerId], item, edges, out error)) return false;
            foreach (RuntimeServiceId service in item.RequiresServices)
            {
                if (!serviceProviders.TryGetValue(service, out RuntimePackageId providerId))
                    return Fail("missing-service", item.Id, $"Required runtime service {service} is absent.", out error);
                if (providerId != item.Id && !AddEdge(packages[providerId], item, edges, out error)) return false;
            }
            foreach (RuntimeServiceId service in item.OptionalServices)
                if (serviceProviders.TryGetValue(service, out RuntimePackageId providerId) && providerId != item.Id &&
                    !AddEdge(packages[providerId], item, edges, out error)) return false;
            foreach (RuntimePackageId other in item.Before)
                if (packages.TryGetValue(other, out RuntimePackageManifest? target) &&
                    !AddEdge(item, target, edges, out error)) return false;
            foreach (RuntimePackageId other in item.After)
                if (packages.TryGetValue(other, out RuntimePackageManifest? source) &&
                    !AddEdge(source, item, edges, out error)) return false;
            foreach (RuntimePackageId other in item.Conflicts)
                if (packages.ContainsKey(other))
                    return Fail("package-conflict", item.Id, $"Conflicting package {other} is selected.", out error);
        }

        // Compile stages are a first-class ordering contract even without an explicit edge.
        RuntimePackageManifest[] all = packages.Values.ToArray();
        foreach (RuntimePackageManifest earlier in all)
            foreach (RuntimePackageManifest later in all)
                if (earlier.Stage < later.Stage) edges[earlier.Id].Add(later.Id);

        var indegree = packages.Keys.ToDictionary(id => id, _ => 0);
        foreach (HashSet<RuntimePackageId> targets in edges.Values)
            foreach (RuntimePackageId target in targets) indegree[target]++;
        var ready = new SortedSet<RuntimePackageId>(indegree.Where(x => x.Value == 0).Select(x => x.Key));
        var ordered = new List<RuntimePackageManifest>(packages.Count);
        while (ready.Count > 0)
        {
            RuntimePackageId id = ready.Min;
            ready.Remove(id);
            ordered.Add(packages[id]);
            foreach (RuntimePackageId target in edges[id])
                if (--indegree[target] == 0) ready.Add(target);
        }
        if (ordered.Count != packages.Count)
            return Fail("dependency-cycle", null, "Selected package ordering contains a cycle.", out error);
        plan = new RuntimeCompositionPlan(ordered, Fingerprint(packages.Values));
        return true;
    }

    private static bool AddEdge(RuntimePackageManifest source, RuntimePackageManifest target,
        Dictionary<RuntimePackageId, HashSet<RuntimePackageId>> edges, out RuntimeResolutionError? error)
    {
        error = null;
        if (source.Stage > target.Stage)
            return Fail("backward-stage-dependency", target.Id,
                $"{target.Stage} package cannot depend on later {source.Stage} package {source.Id}.", out error);
        if (source.Id == target.Id)
            return Fail("self-order", target.Id, "Package order cannot reference itself.", out error);
        edges[source.Id].Add(target.Id);
        return true;
    }

    private static bool Fail(string code, RuntimePackageId? package, string detail,
        out RuntimeResolutionError? error)
    {
        error = new RuntimeResolutionError(code, package, detail);
        return false;
    }

    private static string Fingerprint(IEnumerable<RuntimePackageManifest> packages)
    {
        var canonical = new StringBuilder("UltraSim.RuntimeCompositionManifest/v3\n");
        foreach (RuntimePackageManifest item in packages.OrderBy(x => x.Id))
        {
            canonical.Append(item.Id.Value).Append('|').Append(item.Version).Append('|')
                .Append((byte)item.Stage).Append('|').Append((byte)item.Class).Append('|');
            Append(canonical, item.Dependencies.OrderBy(x => x.Package).Select(x => $"{x.Package}@{x.MinimumVersion}..{x.MaximumVersionExclusive?.ToString() ?? "*"}"));
            Append(canonical, item.Provides.Order());
            Append(canonical, item.Requires.Order());
            Append(canonical, item.Optional.Order());
            Append(canonical, item.Services.Order());
            Append(canonical, item.RequiresServices.Order());
            Append(canonical, item.OptionalServices.Order());
            canonical.Append(item.Entrypoint ?? "*").Append('|');
            Append(canonical, item.Before.Order());
            Append(canonical, item.After.Order());
            Append(canonical, item.Conflicts.Order());
            canonical.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static void Append<T>(StringBuilder destination, IEnumerable<T> values)
    {
        foreach (T value in values) destination.Append(value).Append(',');
        destination.Append('|');
    }
}
