namespace UltraSim.ECS;

/// <summary>Cold reflection-free binding of stable component schemas to codecs and migrations.</summary>
public sealed class PersistenceCatalogBuilder
{
    private readonly ComponentSchema _schema;
    private readonly IBoundPersistenceCodec?[] _current;
    private readonly Dictionary<(uint Id, ushort Version), IBoundPersistenceCodec> _historical = new();
    private readonly Dictionary<uint, uint> _aliases = new();
    private readonly HashSet<uint> _drops = new();
    private readonly List<ShapeMigrationBinding> _shapeMigrations = new();
    private readonly Dictionary<ComponentId, IPersistenceDerivedReconstructor> _derived = new();
    private bool _built;

    public PersistenceCatalogBuilder(ComponentSchema schema)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _current = new IBoundPersistenceCodec?[schema.ComponentCount];
    }

    public PersistenceCatalogBuilder Register<T>(PersistenceSchemaFingerprint fingerprint, IPersistenceComponentCodec<T> codec)
        where T : unmanaged, IComponent<T>
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(codec);
        if (!_schema.TryGetComponentId<T>(out ComponentId id)) throw new ArgumentException("Codec type is absent from the schema.");
        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(id);
        if (metadata.PersistenceMode != ComponentPersistenceMode.Value)
            throw new ArgumentException("Only value-persisted components accept a payload codec.");
        if (_current[id.Value] is not null) throw new ArgumentException($"Component persistence ID {metadata.PersistenceId} already has a current codec.");
        _current[id.Value] = new BoundPersistenceCodec<T>(metadata, fingerprint, codec);
        return this;
    }

    public PersistenceCatalogBuilder RegisterHistoricalReader<T>(ushort savedVersion, PersistenceSchemaFingerprint fingerprint,
        IPersistenceComponentReader<T> reader)
        where T : unmanaged, IComponent<T>
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(reader);
        if (savedVersion == 0) throw new ArgumentOutOfRangeException(nameof(savedVersion));
        if (!_schema.TryGetComponentId<T>(out ComponentId id)) throw new ArgumentException("Reader type is absent from the schema.");
        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(id);
        var key = (metadata.PersistenceId, savedVersion);
        if (!_historical.TryAdd(key, new BoundHistoricalReader<T>(metadata, savedVersion, reader.EncodedSize, fingerprint, reader)))
            throw new ArgumentException("A direct historical reader is already registered for this version.");
        return this;
    }

    public PersistenceCatalogBuilder AddAlias(uint savedPersistenceId, uint currentPersistenceId)
    {
        EnsureMutable();
        if (savedPersistenceId == 0 || currentPersistenceId == 0 || !_schema.TryGetComponentIdByPersistenceId(currentPersistenceId, out _))
            throw new ArgumentException("Alias IDs must be nonzero and the destination must exist in the current schema.");
        if (savedPersistenceId == currentPersistenceId ||
            _schema.TryGetComponentIdByPersistenceId(savedPersistenceId, out _) ||
            _drops.Contains(savedPersistenceId) || _aliases.ContainsValue(savedPersistenceId))
            throw new ArgumentException("Alias sources must be distinct historical IDs that are neither current, dropped, nor alias destinations.");
        if (!_aliases.TryAdd(savedPersistenceId, currentPersistenceId)) throw new ArgumentException("Duplicate persistence alias.");
        return this;
    }

    public PersistenceCatalogBuilder Drop(uint savedPersistenceId)
    {
        EnsureMutable();
        if (savedPersistenceId == 0 || _schema.TryGetComponentIdByPersistenceId(savedPersistenceId, out _) ||
            _aliases.ContainsKey(savedPersistenceId) || _aliases.ContainsValue(savedPersistenceId) || !_drops.Add(savedPersistenceId))
            throw new ArgumentException("Drop declarations must be unique historical IDs and cannot contradict current or alias identities.");
        return this;
    }

    public PersistenceCatalogBuilder RegisterShapeMigration(ReadOnlySpan<uint> savedPersistenceIds,
        ReadOnlySpan<ComponentId> currentComponents, IPersistenceShapeMigrator migrator)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(migrator);
        if (savedPersistenceIds.IsEmpty || currentComponents.IsEmpty) throw new ArgumentException("Shape migrations require nonempty source and destination shapes.");
        uint[] saved = savedPersistenceIds.ToArray(); Array.Sort(saved);
        ComponentId[] current = currentComponents.ToArray(); Array.Sort(current, static (left, right) => left.Value.CompareTo(right.Value));
        if (saved[0] == 0 || saved.Distinct().Count() != saved.Length || current.Any(static id => !id.IsValid) || current.Distinct().Count() != current.Length)
            throw new ArgumentException("Shape migration identities must be valid and unique.");
        foreach (ComponentId id in current) _ = _schema.GetMetadata(id);
        if (_shapeMigrations.Any(binding => binding.SavedIds.AsSpan().SequenceEqual(saved)))
            throw new ArgumentException("A shape migration is already registered for this source shape.");
        _shapeMigrations.Add(new ShapeMigrationBinding { SavedIds = saved, CurrentComponents = current, Migrator = migrator });
        return this;
    }

    public PersistenceCatalogBuilder RegisterDerived<T>(IPersistenceDerivedReconstructor reconstructor)
        where T : unmanaged, IComponent<T>
    {
        EnsureMutable(); ArgumentNullException.ThrowIfNull(reconstructor);
        if (!_schema.TryGetComponentId<T>(out ComponentId id) || _schema.GetMetadata(id).PersistenceMode != ComponentPersistenceMode.Derived)
            throw new ArgumentException("Derived reconstructors require a derived component in this schema.");
        if (!_derived.TryAdd(id, reconstructor)) throw new ArgumentException("Duplicate derived reconstructor.");
        return this;
    }

    public PersistenceCatalog Build()
    {
        EnsureMutable();
        _built = true;
        return new PersistenceCatalog(_schema, _current, _historical, _aliases, _drops, _shapeMigrations, _derived);
    }

    private void EnsureMutable()
    {
        if (_built) throw new InvalidOperationException("The persistence catalog builder is already sealed.");
    }
}

public sealed class PersistenceCatalog
{
    private readonly IBoundPersistenceCodec?[] _current;
    private readonly Dictionary<(uint Id, ushort Version), IBoundPersistenceCodec> _historical;
    private readonly Dictionary<uint, uint> _aliases;
    private readonly HashSet<uint> _drops;
    private readonly ShapeMigrationBinding[] _shapeMigrations;
    private readonly Dictionary<ComponentId, IPersistenceDerivedReconstructor> _derived;
    internal PersistenceCatalog(ComponentSchema schema, IBoundPersistenceCodec?[] current,
        Dictionary<(uint Id, ushort Version), IBoundPersistenceCodec> historical,
        Dictionary<uint, uint> aliases, HashSet<uint> drops, List<ShapeMigrationBinding> shapeMigrations,
        Dictionary<ComponentId, IPersistenceDerivedReconstructor> derived)
    {
        Schema = schema;
        _current = (IBoundPersistenceCodec?[])current.Clone();
        _historical = new(historical);
        _aliases = new(aliases);
        _drops = new(drops);
        _shapeMigrations = shapeMigrations.ToArray();
        _derived = new(derived);
    }
    public ComponentSchema Schema { get; }
    internal IBoundPersistenceCodec GetCurrent(ComponentId id) => _current[id.Value] ??
        throw new PersistenceCompatibilityException($"Component '{Schema.GetCanonicalKey(id)}' has no generated or custom current persistence codec.");
    internal bool TryResolveSavedId(uint savedId, out ComponentId currentId, out bool explicitDrop)
    {
        if (_aliases.TryGetValue(savedId, out uint alias)) savedId = alias;
        explicitDrop = _drops.Contains(savedId);
        return Schema.TryGetComponentIdByPersistenceId(savedId, out currentId);
    }
    internal bool TryGetReader(uint currentPersistenceId, ushort savedVersion, out IBoundPersistenceCodec reader)
    {
        if (Schema.TryGetComponentIdByPersistenceId(currentPersistenceId, out ComponentId id))
        {
            IBoundPersistenceCodec? current = _current[id.Value];
            if (current is not null && current.CurrentVersion == savedVersion) { reader = current; return true; }
        }
        return _historical.TryGetValue((currentPersistenceId, savedVersion), out reader!);
    }
    internal bool TryGetShapeMigration(ReadOnlySpan<uint> savedIds, out ShapeMigrationBinding binding)
    {
        foreach (ShapeMigrationBinding candidate in _shapeMigrations)
        {
            if (candidate.SavedIds.AsSpan().SequenceEqual(savedIds)) { binding = candidate; return true; }
        }
        binding = null!; return false;
    }
    internal bool TryGetDerived(ComponentId id, out IPersistenceDerivedReconstructor reconstructor) =>
        _derived.TryGetValue(id, out reconstructor!);
}

public class PersistenceException : Exception
{
    public PersistenceException(string message) : base(message) { }
    public PersistenceException(string message, Exception inner) : base(message, inner) { }
}
public sealed class PersistenceValidationException : PersistenceException
{
    public PersistenceValidationException(string message) : base(message) { }
}
public sealed class PersistenceCompatibilityException : PersistenceException
{
    public PersistenceCompatibilityException(string message) : base(message) { }
}
