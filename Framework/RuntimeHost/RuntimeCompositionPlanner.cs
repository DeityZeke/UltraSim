using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>A pre-start validation failure. It never represents a partially published runtime.</summary>
public sealed record RuntimeResolutionError(string Code, RuntimePackageId? Package, string Detail);
public readonly record struct RuntimeSavedPackage(RuntimePackageId Id, RuntimePackageVersion Version);
public readonly record struct RuntimePackageRemapStep(RuntimePackageId SavedId, RuntimePackageId SelectedId);

/// <summary>An immutable deterministic manifest plan. Its fingerprint does not yet include source bytes.</summary>
public sealed class RuntimeCompositionPlan
{
    public ReadOnlyCollection<RuntimePackageManifest> OrderedPackages { get; }
    public string ManifestFingerprint { get; }
    private readonly IReadOnlyDictionary<RuntimePackageId, RuntimePackageId> _aliases;

    internal RuntimeCompositionPlan(List<RuntimePackageManifest> ordered, string fingerprint,
        IReadOnlyDictionary<RuntimePackageId, RuntimePackageId> aliases)
    {
        OrderedPackages = ordered.AsReadOnly();
        ManifestFingerprint = fingerprint;
        _aliases = aliases;
    }
    public RuntimePackageId ResolveId(RuntimePackageId id) => _aliases.TryGetValue(id, out var canonical) ? canonical : id;

    /// <summary>Pre-authority package-ID remap order. Durable artifacts still require exact schema admission.</summary>
    public bool TryPlanSavedPackages(IEnumerable<RuntimeSavedPackage> saved,
        out ReadOnlyCollection<RuntimePackageRemapStep>? steps, out RuntimeResolutionError? error)
    {
        ArgumentNullException.ThrowIfNull(saved);
        steps = null; error = null;
        var seen = new HashSet<RuntimePackageId>();
        var byId = OrderedPackages.ToDictionary(p => p.Id);
        var remaps = new Dictionary<RuntimePackageId, RuntimePackageRemapStep>();
        foreach (RuntimeSavedPackage item in saved)
        {
            if (!item.Id.IsValid || !item.Version.IsValid || seen.Count == RuntimeCompositionPlanner.MaximumPackages)
            { error = new("saved-package-bound", item.Id, "Saved package set is invalid or exceeds its bound."); return false; }
            if (!seen.Add(item.Id))
            { error = new("saved-package-duplicate", item.Id, "Saved package identity is duplicated."); return false; }
            RuntimePackageId target = ResolveId(item.Id);
            if (!byId.TryGetValue(target, out RuntimePackageManifest? selected))
            { error = new("saved-package-missing", item.Id, $"No admitted package or alias for saved {item.Id}."); return false; }
            if (item.Version != selected.Version)
            { error = new("saved-package-version", item.Id, $"Saved {item.Id}@{item.Version} needs an explicit migration to {target}@{selected.Version}."); return false; }
            if (!remaps.TryAdd(target, new(item.Id, target)))
            { error = new("saved-package-ambiguous", item.Id, $"Multiple saved identities target {target}."); return false; }
        }
        steps = OrderedPackages.Where(p => remaps.ContainsKey(p.Id)).Select(p => remaps[p.Id])
            .ToList().AsReadOnly();
        return true;
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
        var aliases = new Dictionary<RuntimePackageId, RuntimePackageId>();
        foreach (RuntimePackageManifest item in packages.Values.OrderBy(p => p.Id))
            foreach (RuntimePackageId alias in item.Aliases)
            {
                if (packages.ContainsKey(alias))
                    return Fail("alias-selected-conflict", item.Id, $"Alias {alias} is also a selected package identity.", out error);
                if (!aliases.TryAdd(alias, item.Id))
                    return Fail("alias-ambiguous", item.Id, $"Alias {alias} has multiple selected owners.", out error);
            }
        // An alias is terminal at a selected identity; chains and cycles are rejected by construction.
        RuntimePackageId Canonical(RuntimePackageId id) => aliases.TryGetValue(id, out var target) ? target : id;

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
                if (!packages.TryGetValue(Canonical(dependency.Package), out RuntimePackageManifest? provider))
                    return Fail("missing-package", item.Id, $"Required package {dependency.Package} has no selected identity or alias.", out error);
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
                if (packages.TryGetValue(Canonical(other), out RuntimePackageManifest? target) &&
                    !AddEdge(item, target, edges, out error)) return false;
            foreach (RuntimePackageId other in item.After)
                if (packages.TryGetValue(Canonical(other), out RuntimePackageManifest? source) &&
                    !AddEdge(source, item, edges, out error)) return false;
            foreach (RuntimePackageId other in item.Conflicts)
                if (packages.ContainsKey(Canonical(other)))
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
        plan = new RuntimeCompositionPlan(ordered, Fingerprint(packages.Values), aliases);
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
        var canonical = new StringBuilder("UltraSim.RuntimeCompositionManifest/v4\n");
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
            Append(canonical, item.Aliases.Order());
            canonical.Append(item.DurableSchema?.ParticipantService.ToString() ?? "*").Append('|')
                .Append(item.DurableSchema?.ArtifactKey ?? "*").Append('|');
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
