using System.Collections.ObjectModel;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>Bounded package stage. Contracts must remain usable without a selected Core.</summary>
public enum RuntimePackageStage : byte { Contracts, Core, Scripts }

/// <summary>Diagnostic classification; it does not grant runtime privileges.</summary>
public enum RuntimePackageClass : byte
{
    Runtime, Definition, System, EngineCore, Kernel, Schema, Framework
}

/// <summary>A required half-open version range. An absent maximum leaves the upper bound open.</summary>
public readonly record struct RuntimePackageDependency
{
    public RuntimePackageDependency(RuntimePackageId package, RuntimePackageVersion minimumVersion, RuntimePackageVersion? maximumVersionExclusive = null)
    {
        if (!package.IsValid || !minimumVersion.IsValid || maximumVersionExclusive is { } maximum &&
            (!maximum.IsValid || maximum.CompareTo(minimumVersion) <= 0))
            throw new ArgumentException("Invalid package version range.");
        Package = package;
        MinimumVersion = minimumVersion;
        MaximumVersionExclusive = maximumVersionExclusive;
    }
    public RuntimePackageId Package { get; }
    public RuntimePackageVersion MinimumVersion { get; }
    public RuntimePackageVersion? MaximumVersionExclusive { get; }
    public bool Accepts(RuntimePackageVersion version) => version.CompareTo(MinimumVersion) >= 0 &&
        (MaximumVersionExclusive is not { } maximum || version.CompareTo(maximum) < 0);
}

/// <summary>Immutable selected-package metadata for deterministic pre-authority planning.</summary>
public sealed class RuntimePackageManifest
{
    public const int MaximumRelations = 64;
    public RuntimePackageId Id { get; }
    public RuntimePackageVersion Version { get; }
    public RuntimePackageStage Stage { get; }
    public RuntimePackageClass Class { get; }
    public ReadOnlyCollection<RuntimePackageDependency> Dependencies { get; }
    public ReadOnlyCollection<PackageCapabilityId> Provides { get; }
    public ReadOnlyCollection<PackageCapabilityId> Requires { get; }
    public ReadOnlyCollection<PackageCapabilityId> Optional { get; }
    public ReadOnlyCollection<RuntimeServiceId> Services { get; }
    public ReadOnlyCollection<RuntimeServiceId> RequiresServices { get; }
    public ReadOnlyCollection<RuntimeServiceId> OptionalServices { get; }
    /// <summary>Explicit dynamic entrypoint type. Null means compiled vocabulary only.</summary>
    public string? Entrypoint { get; }
    public ReadOnlyCollection<RuntimePackageId> Before { get; }
    public ReadOnlyCollection<RuntimePackageId> After { get; }
    public ReadOnlyCollection<RuntimePackageId> Conflicts { get; }

    public RuntimePackageManifest(
        RuntimePackageId id, RuntimePackageVersion version, RuntimePackageStage stage, RuntimePackageClass @class,
        IEnumerable<RuntimePackageDependency>? dependencies = null,
        IEnumerable<PackageCapabilityId>? provides = null,
        IEnumerable<PackageCapabilityId>? requires = null,
        IEnumerable<PackageCapabilityId>? optional = null,
        IEnumerable<RuntimePackageId>? before = null,
        IEnumerable<RuntimePackageId>? after = null,
        IEnumerable<RuntimePackageId>? conflicts = null,
        IEnumerable<RuntimeServiceId>? services = null,
        IEnumerable<RuntimeServiceId>? requiresServices = null,
        IEnumerable<RuntimeServiceId>? optionalServices = null,
        string? entrypoint = null)
    {
        if (!id.IsValid || !version.IsValid || !Enum.IsDefined(stage) || !Enum.IsDefined(@class))
            throw new ArgumentException("Package identity, version, stage and class must be valid.");
        Id = id; Version = version; Stage = stage; Class = @class;
        Dependencies = Copy(dependencies, d => d.Package.IsValid && d.MinimumVersion.IsValid &&
            (d.MaximumVersionExclusive is not { } maximum || maximum.IsValid && maximum.CompareTo(d.MinimumVersion) > 0), d => d.Package);
        Provides = Copy(provides, x => x.IsValid, x => x);
        Requires = Copy(requires, x => x.IsValid, x => x);
        Optional = Copy(optional, x => x.IsValid, x => x);
        Services = Copy(services, x => x.IsValid, x => x);
        RequiresServices = Copy(requiresServices, x => x.IsValid, x => x);
        OptionalServices = Copy(optionalServices, x => x.IsValid, x => x);
        if (entrypoint is not null && !ValidEntrypoint(entrypoint))
            throw new ArgumentException("Entrypoint must be one bounded canonical qualified C# type name.", nameof(entrypoint));
        Entrypoint = entrypoint;
        Before = Copy(before, x => x.IsValid && x != id, x => x);
        After = Copy(after, x => x.IsValid && x != id, x => x);
        Conflicts = Copy(conflicts, x => x.IsValid && x != id, x => x);
        if (Dependencies.Any(d => d.Package == id) ||
            Provides.Any(Requires.Contains) || Provides.Any(Optional.Contains) ||
            Requires.Any(Optional.Contains) || Services.Any(RequiresServices.Contains) ||
            Services.Any(OptionalServices.Contains) || RequiresServices.Any(OptionalServices.Contains))
            throw new ArgumentException("Self-dependencies and contradictory capability declarations are invalid.");
    }

    private static bool ValidEntrypoint(string value)
    {
        if (value.Length is < 1 or > 256) return false;
        bool start = true;
        foreach (char c in value)
        {
            if (c == '.') { if (start) return false; start = true; continue; }
            if (start && !(char.IsAsciiLetter(c) || c == '_')) return false;
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
            start = false;
        }
        return !start;
    }

    private static ReadOnlyCollection<T> Copy<T, TKey>(IEnumerable<T>? source, Func<T, bool> valid, Func<T, TKey> key)
        where TKey : notnull
    {
        var values = new List<T>();
        var seen = new HashSet<TKey>();
        if (source is not null)
        {
            foreach (T item in source)
            {
                if (values.Count == MaximumRelations || !valid(item) || !seen.Add(key(item)))
                    throw new ArgumentException("Package relation is invalid, duplicated or exceeds its bound.");
                values.Add(item);
            }
        }
        return values.AsReadOnly();
    }
}
