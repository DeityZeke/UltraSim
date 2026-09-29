using System.Collections.ObjectModel;
using System.Threading;

namespace UltraSim.Framework.Definitions;

/// <summary>Cold diagnostic identifying the exact contributed meaning and its owner.</summary>
public readonly record struct DefinitionContribution(
    StableDefinitionId Definition, DefinitionFacetId Facet, DefinitionPackageId Package);
public readonly record struct DefinitionTagContribution(
    DefinitionTagId Tag, StableDefinitionId Definition, DefinitionPackageId Package, bool Applied);
public readonly record struct DefinitionAliasContribution(
    StableDefinitionId OldId, StableDefinitionId Canonical, DefinitionPackageId Package);

/// <summary>Cold, bounded authoring surface shared by built-in and compiled packages.</summary>
public sealed class DefinitionComposer
{
    public const int MaximumDefinitions = 4096;
    public const int MaximumFacetFamilies = 64;
    public const int MaximumFacetsPerDefinition = 32;
    public const int MaximumTags = 256;
    public const int MaximumTagMemberships = 8192;

    private sealed class AuthoredDefinition(DefinitionPackageId owner)
    {
        public DefinitionPackageId Owner = owner;
        public Dictionary<DefinitionFacetId, (DefinitionPackageId Package, object Value)> Facets = new();
        public HashSet<DefinitionCapabilityId> RequiredCapabilities = new();
    }
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<StableDefinitionId, AuthoredDefinition> _definitions = new();
    private readonly Dictionary<DefinitionFacetId, DefinitionCatalog.FamilyIdentity> _families = new();
    private readonly Dictionary<StableDefinitionId, (StableDefinitionId Canonical, DefinitionPackageId Package)> _aliases = new();
    private readonly Dictionary<DefinitionTagId, HashSet<(StableDefinitionId Definition, DefinitionPackageId Package, bool Required)>> _tags = new();
    private bool _frozen;
    private int _membershipCount;
    private static long _nextGeneration;

    private void EnsureOpen()
    {
        if (_frozen || Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Definition composition requires its unfrozen owner thread.");
    }

    public DefinitionBuilder Define(DefinitionPackageId package, StableDefinitionId id)
    {
        EnsureOpen();
        if (!package.IsValid || !id.IsValid || _definitions.Count == MaximumDefinitions ||
            !_definitions.TryAdd(id, new(package)))
            throw new InvalidOperationException($"Definition {id} is invalid, duplicated or exceeds capacity (package {package}).");
        return new(this, id);
    }

    /// <summary>Another package may contribute a distinct facet without owning the Definition.</summary>
    public DefinitionBuilder Extend(StableDefinitionId id)
    {
        EnsureOpen();
        if (!_definitions.ContainsKey(id)) throw new InvalidOperationException($"Definition {id} is not declared.");
        return new(this, id);
    }

    public void RegisterFacet<T>(DefinitionFacetFamily<T> family) where T : struct
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(family);
        if (_families.Count == MaximumFacetFamilies ||
            !_families.TryAdd(family.Id, new(typeof(T), family.ContractsOwner, family.RuntimeCapability, family)))
            throw new InvalidOperationException($"Facet {family.Id.Value} is duplicated or exceeds capacity.");
    }

    internal void With<T>(StableDefinitionId id, DefinitionPackageId package,
        DefinitionFacetFamily<T> family, T value) where T : struct
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(family);
        if (!package.IsValid || !_definitions.TryGetValue(id, out AuthoredDefinition? definition) ||
            !_families.TryGetValue(family.Id, out DefinitionCatalog.FamilyIdentity registered) ||
            registered.ValueType != typeof(T) || registered.ContractsOwner != family.ContractsOwner ||
            registered.RuntimeCapability != family.RuntimeCapability ||
            !ReferenceEquals(registered.Family, family) || family.Validate is not null && !family.Validate(id, value) ||
            definition.Facets.Count == MaximumFacetsPerDefinition)
            throw new InvalidOperationException($"Conflicting facet {family.Id.Value} on {id} from {package}.");
        if (!definition.Facets.TryAdd(family.Id, (package, value)))
            throw new InvalidOperationException($"Facet {family.Id.Value} on {id} from {package} conflicts with {definition.Facets[family.Id].Package}.");
    }

    internal void Require(StableDefinitionId id, DefinitionCapabilityId capability)
    {
        EnsureOpen();
        if (!capability.IsValid || !_definitions[id].RequiredCapabilities.Add(capability) ||
            _definitions[id].RequiredCapabilities.Count > MaximumFacetsPerDefinition)
            throw new InvalidOperationException($"Invalid or duplicate required capability {capability} for {id}.");
    }

    public void ContributeTag(DefinitionPackageId package, DefinitionTagId tag, StableDefinitionId member,
        bool requireDefinition = false)
    {
        EnsureOpen();
        if (!package.IsValid || !tag.IsValid || !member.IsValid || _membershipCount == MaximumTagMemberships)
            throw new InvalidOperationException($"Invalid tag membership {tag.Value}/{member} from {package}.");
        if (!_tags.TryGetValue(tag, out var members))
        {
            if (_tags.Count == MaximumTags) throw new InvalidOperationException("Tag capacity exceeded.");
            _tags.Add(tag, members = new());
        }
        // A second package claiming the same membership is a conflict, not a hidden dependency.
        if (members.Any(m => m.Definition == member) || !members.Add((member, package, requireDefinition)))
            throw new InvalidOperationException($"Duplicate tag membership {tag.Value}/{member} from {package}.");
        _membershipCount++;
    }

    public void Alias(DefinitionPackageId package, StableDefinitionId oldId, StableDefinitionId currentId)
    {
        EnsureOpen();
        if (!package.IsValid || !oldId.IsValid || !currentId.IsValid || oldId == currentId ||
            _aliases.Count == MaximumDefinitions || !_aliases.TryAdd(oldId, (currentId, package)))
            throw new InvalidOperationException($"Conflicting Definition alias {oldId} → {currentId} from {package}.");
    }

    /// <summary>Freeze before authority publication. No authored value or alias is rewritten later.</summary>
    public DefinitionCatalog Freeze(IEnumerable<DefinitionCapabilityId> selectedCapabilities)
    {
        EnsureOpen(); ArgumentNullException.ThrowIfNull(selectedCapabilities);
        var selected = new HashSet<DefinitionCapabilityId>();
        foreach (DefinitionCapabilityId capability in selectedCapabilities)
            if (!capability.IsValid || !selected.Add(capability) || selected.Count > MaximumFacetFamilies)
                throw new InvalidOperationException("Selected capability is invalid, duplicated or exceeds capacity.");
        foreach (var (id, declaration) in _definitions)
            foreach (DefinitionCapabilityId required in declaration.RequiredCapabilities)
                if (!selected.Contains(required))
                    throw new InvalidOperationException($"Definition {id} from {declaration.Owner} requires absent capability {required}.");
        foreach (var (oldId, mapping) in _aliases)
            if (_definitions.ContainsKey(oldId) || _aliases.ContainsKey(mapping.Canonical) ||
                !_definitions.TryGetValue(mapping.Canonical, out AuthoredDefinition? target) || target.Owner != mapping.Package)
                throw new InvalidOperationException($"Definition alias {oldId} → {mapping.Canonical} from {mapping.Package} is ambiguous, chained or not owned by the canonical Definition.");
        long generation = Interlocked.Increment(ref _nextGeneration);
        if (generation <= 0) throw new InvalidOperationException("Definition generation exhausted.");
        StableDefinitionId[] ids = _definitions.Keys.Order().ToArray();
        var handles = new Dictionary<StableDefinitionId, DefinitionHandle>(ids.Length);
        for (int i = 0; i < ids.Length; i++) handles.Add(ids[i], new(i + 1, (ulong)generation));
        var facets = new Dictionary<DefinitionFacetId, object?[]>(_families.Count);
        foreach (DefinitionFacetId family in _families.Keys) facets.Add(family, new object?[ids.Length + 1]);
        var diagnostics = new List<DefinitionContribution>();
        foreach (StableDefinitionId id in ids)
            foreach (var (facetId, contribution) in _definitions[id].Facets.OrderBy(x => x.Key))
            {
                facets[facetId][handles[id].Index] = contribution.Value;
                diagnostics.Add(new(id, facetId, contribution.Package));
            }
        var tags = new Dictionary<DefinitionTagId, bool[]>(_tags.Count);
        var tagDiagnostics = new List<DefinitionTagContribution>(_membershipCount);
        foreach (var (tag, members) in _tags)
        {
            var bits = new bool[ids.Length + 1];
            foreach (var member in members.OrderBy(m => m.Definition))
            {
                bool applied = handles.TryGetValue(member.Definition, out DefinitionHandle handle);
                if (!applied && member.Required)
                    throw new InvalidOperationException($"Required tag membership {tag.Value}/{member.Definition} from {member.Package} has no Definition.");
                if (applied) bits[handle.Index] = true;
                tagDiagnostics.Add(new(tag, member.Definition, member.Package, applied));
            }
            tags.Add(tag, bits);
        }
        _frozen = true;
        return new((ulong)generation, ids, handles, facets, tags,
            new(_families), new(_aliases), selected, diagnostics.AsReadOnly(),
            tagDiagnostics.OrderBy(d => d.Tag).ThenBy(d => d.Definition).ToList().AsReadOnly(),
            _aliases.Select(pair => new DefinitionAliasContribution(pair.Key, pair.Value.Canonical, pair.Value.Package))
                .OrderBy(d => d.OldId).ToList().AsReadOnly());
    }
}

public sealed class DefinitionBuilder
{
    private readonly DefinitionComposer _composer;
    internal DefinitionBuilder(DefinitionComposer composer, StableDefinitionId id)
    { _composer = composer; Id = id; }
    public StableDefinitionId Id { get; }
    public DefinitionBuilder With<T>(DefinitionPackageId contributor, DefinitionFacetFamily<T> family, T value) where T : struct
    { _composer.With(Id, contributor, family, value); return this; }
    public DefinitionBuilder RequireRuntime(DefinitionCapabilityId capability)
    { _composer.Require(Id, capability); return this; }
}

/// <summary>Dense, generation-fenced typed hot lookup; no string, reflection or boxing on reads.</summary>
public sealed class DefinitionFacetTable<T> where T : struct
{
    private readonly T[] _values;
    private readonly bool[] _present;
    private readonly ulong _generation;
    internal DefinitionFacetTable(T[] values, bool[] present, ulong generation, bool active)
    { _values = values; _present = present; _generation = generation; RuntimeActive = active; }
    public bool RuntimeActive { get; }
    public bool TryGet(DefinitionHandle handle, out T value)
    {
        if (handle.Generation == _generation && (uint)handle.Index < (uint)_present.Length && _present[handle.Index])
        { value = _values[handle.Index]; return true; }
        value = default; return false;
    }
}

public sealed class DefinitionTagTable
{
    private readonly bool[] _members;
    private readonly ulong _generation;
    internal DefinitionTagTable(bool[] members, ulong generation)
    { _members = members; _generation = generation; }
    public bool Contains(DefinitionHandle handle) => handle.Generation == _generation &&
        (uint)handle.Index < (uint)_members.Length && _members[handle.Index];
}

/// <summary>Immutable result of one cold composition. Stable identity is retained for persistence.</summary>
public sealed class DefinitionCatalog
{
    private readonly StableDefinitionId[] _ids;
    private readonly IReadOnlyDictionary<StableDefinitionId, DefinitionHandle> _handles;
    private readonly IReadOnlyDictionary<DefinitionFacetId, object?[]> _facets;
    private readonly IReadOnlyDictionary<DefinitionTagId, bool[]> _tags;
    private readonly IReadOnlyDictionary<DefinitionFacetId, FamilyIdentity> _families;
    private readonly IReadOnlyDictionary<StableDefinitionId, (StableDefinitionId Canonical, DefinitionPackageId Package)> _aliases;
    private readonly IReadOnlySet<DefinitionCapabilityId> _selected;
    internal DefinitionCatalog(ulong generation, StableDefinitionId[] ids,
        Dictionary<StableDefinitionId, DefinitionHandle> handles,
        Dictionary<DefinitionFacetId, object?[]> facets, Dictionary<DefinitionTagId, bool[]> tags,
        Dictionary<DefinitionFacetId, FamilyIdentity> families,
        Dictionary<StableDefinitionId, (StableDefinitionId Canonical, DefinitionPackageId Package)> aliases,
        HashSet<DefinitionCapabilityId> selected, ReadOnlyCollection<DefinitionContribution> contributions,
        ReadOnlyCollection<DefinitionTagContribution> tagContributions,
        ReadOnlyCollection<DefinitionAliasContribution> aliasContributions)
    { Generation = generation; _ids = ids; _handles = handles; _facets = facets; _tags = tags;
      _families = families; _aliases = aliases; _selected = selected; Contributions = contributions;
      TagContributions = tagContributions; AliasContributions = aliasContributions; }
    public ulong Generation { get; }
    public int Count => _ids.Length;
    public IReadOnlyList<DefinitionContribution> Contributions { get; }
    public IReadOnlyList<DefinitionTagContribution> TagContributions { get; }
    public IReadOnlyList<DefinitionAliasContribution> AliasContributions { get; }
    public bool TryResolve(StableDefinitionId stable, out DefinitionHandle handle)
    {
        if (_aliases.TryGetValue(stable, out var mapping)) stable = mapping.Canonical;
        return _handles.TryGetValue(stable, out handle);
    }
    public bool TryGetStableId(DefinitionHandle handle, out StableDefinitionId id)
    {
        if (handle.Generation == Generation && handle.Index > 0 && handle.Index <= _ids.Length)
        { id = _ids[handle.Index - 1]; return true; }
        id = default; return false;
    }
    /// <summary>Reject old+new or other aliases resolving to one loaded identity.</summary>
    public bool TryResolvePersistedDistinct(IEnumerable<StableDefinitionId> persisted, out DefinitionHandle[] handles)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        var result = new List<DefinitionHandle>();
        var seen = new HashSet<DefinitionHandle>();
        foreach (StableDefinitionId id in persisted)
        {
            if (result.Count == DefinitionComposer.MaximumDefinitions || !TryResolve(id, out var handle) || !seen.Add(handle))
            { handles = []; return false; }
            result.Add(handle);
        }
        handles = result.ToArray(); return true;
    }
    public DefinitionFacetTable<T> Bind<T>(DefinitionFacetFamily<T> family) where T : struct
    {
        ArgumentNullException.ThrowIfNull(family);
        if (!_families.TryGetValue(family.Id, out FamilyIdentity identity) ||
            identity.ValueType != typeof(T) || identity.ContractsOwner != family.ContractsOwner ||
            identity.RuntimeCapability != family.RuntimeCapability || !ReferenceEquals(identity.Family, family))
            throw new InvalidOperationException($"Facet {family.Id.Value} has a different registered contract.");
        object?[] source = _facets[family.Id];
        var values = new T[source.Length]; var present = new bool[source.Length];
        for (int i = 1; i < source.Length; i++)
            if (source[i] is T value) { values[i] = value; present[i] = true; }
        return new(values, present, Generation,
            !family.RuntimeCapability.IsValid || _selected.Contains(family.RuntimeCapability));
    }
    public DefinitionTagTable Bind(DefinitionTagId tag) => _tags.TryGetValue(tag, out bool[]? members) ?
        new(members, Generation) : throw new InvalidOperationException($"Unknown Definition tag {tag.Value}.");

    internal readonly record struct FamilyIdentity(Type ValueType, DefinitionPackageId ContractsOwner,
        DefinitionCapabilityId RuntimeCapability, object Family);
}
