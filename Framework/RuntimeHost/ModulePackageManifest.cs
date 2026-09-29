using System.Collections.ObjectModel;

namespace UltraSim.Framework.RuntimeHost;

/// <summary>Bounded package stage. Contracts must remain usable without a selected Core.</summary>
public enum ModulePackageStage : byte { Contracts, Core, Scripts }

/// <summary>Diagnostic classification; it does not grant runtime privileges.</summary>
public enum ModulePackageClass : byte
{
    RuntimeExtension, Definition, System, Engine, KernelExtension, SchemaExtension
}

/// <summary>A required package relationship. Optional capability references use capability IDs instead.</summary>
public readonly record struct ModuleDependency(ModulePackageId Package, ModuleVersion MinimumVersion);

/// <summary>Immutable selected-package metadata for deterministic pre-authority planning.</summary>
public sealed class ModulePackageManifest
{
    public const int MaximumRelations = 64;
    public ModulePackageId Id { get; }
    public ModuleVersion Version { get; }
    public ModulePackageStage Stage { get; }
    public ModulePackageClass Class { get; }
    public ReadOnlyCollection<ModuleDependency> Dependencies { get; }
    public ReadOnlyCollection<ModuleCapabilityId> Provides { get; }
    public ReadOnlyCollection<ModuleCapabilityId> Requires { get; }
    public ReadOnlyCollection<ModuleCapabilityId> Optional { get; }
    public ReadOnlyCollection<ModulePackageId> Before { get; }
    public ReadOnlyCollection<ModulePackageId> After { get; }
    public ReadOnlyCollection<ModulePackageId> Conflicts { get; }

    public ModulePackageManifest(
        ModulePackageId id, ModuleVersion version, ModulePackageStage stage, ModulePackageClass @class,
        IEnumerable<ModuleDependency>? dependencies = null,
        IEnumerable<ModuleCapabilityId>? provides = null,
        IEnumerable<ModuleCapabilityId>? requires = null,
        IEnumerable<ModuleCapabilityId>? optional = null,
        IEnumerable<ModulePackageId>? before = null,
        IEnumerable<ModulePackageId>? after = null,
        IEnumerable<ModulePackageId>? conflicts = null)
    {
        if (!id.IsValid || !version.IsValid || !Enum.IsDefined(stage) || !Enum.IsDefined(@class))
            throw new ArgumentException("Package identity, version, stage and class must be valid.");
        Id = id; Version = version; Stage = stage; Class = @class;
        Dependencies = Copy(dependencies, d => d.Package.IsValid && d.MinimumVersion.IsValid, d => d.Package);
        Provides = Copy(provides, x => x.IsValid, x => x);
        Requires = Copy(requires, x => x.IsValid, x => x);
        Optional = Copy(optional, x => x.IsValid, x => x);
        Before = Copy(before, x => x.IsValid && x != id, x => x);
        After = Copy(after, x => x.IsValid && x != id, x => x);
        Conflicts = Copy(conflicts, x => x.IsValid && x != id, x => x);
        if (Dependencies.Any(d => d.Package == id) ||
            Provides.Any(Requires.Contains) || Provides.Any(Optional.Contains) ||
            Requires.Any(Optional.Contains))
            throw new ArgumentException("Self-dependencies and contradictory capability declarations are invalid.");
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
