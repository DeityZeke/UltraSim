namespace UltraSim.ECS.Persistence;

/// <summary>
/// Instance-owned registry of types that participate in persistence.
/// </summary>
public sealed class PersistenceTypeRegistry
{
    private readonly Dictionary<uint, PersistenceTypeRecord> _byId = new();
    private readonly Dictionary<Type, PersistenceTypeRecord> _byType = new();
    private bool _sealed;
    private PersistenceTypeRecord[]? _sealedRecords;

    public bool IsSealed => _sealed;
    public int Count => _byId.Count;

    public PersistenceTypeRecord Register<T>(PersistenceKind kind)
        => Register(typeof(T), kind);

    public PersistenceTypeRecord Register(Type type, PersistenceKind kind)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_sealed)
            throw new InvalidOperationException("PersistenceTypeRegistry is sealed.");

        var attribute = (PersistenceIdAttribute?)Attribute.GetCustomAttribute(type, typeof(PersistenceIdAttribute), inherit: false);

        if (attribute is null)
            throw new InvalidOperationException(
                $"Type '{type.FullName}' is missing [PersistenceId(...)] and cannot participate in persistence.");

        if (_byType.TryGetValue(type, out PersistenceTypeRecord existing))
        {
            if (existing.Kind != kind)
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' is already registered as '{existing.Kind}' and cannot also be '{kind}'.");

            return existing;
        }

        if (_byId.TryGetValue(attribute.Id, out PersistenceTypeRecord collision))
            throw new InvalidOperationException(
                $"Duplicate persistence id {attribute.Id}: '{collision.ClrType.FullName}' and '{type.FullName}'.");

        var record = new PersistenceTypeRecord(attribute.Id, kind, type);
        _byId.Add(attribute.Id, record);
        _byType.Add(type, record);
        return record;
    }

    /// <summary>
    /// Registers every serializable component currently present in the supplied schema.
    /// </summary>
    public void RegisterSchemaComponents(EcsSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        IReadOnlyList<ComponentTypeInfo> components = schema.Components;
        for (int i = 0; i < components.Count; i++)
        {
            Type type = components[i].ClrType;
            if (!PersistenceSignatureBuilder.IsSerializableComponentType(type))
                continue;

            Register(type, PersistenceKind.Component);
        }
    }

    public bool TryGetById(uint id, out PersistenceTypeRecord record)
        => _byId.TryGetValue(id, out record);

    public bool TryGetByType(Type type, out PersistenceTypeRecord record)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _byType.TryGetValue(type, out record);
    }

    public bool TryGetByType<T>(out PersistenceTypeRecord record)
        => _byType.TryGetValue(typeof(T), out record);

    public ReadOnlySpan<PersistenceTypeRecord> GetAllSorted()
    {
        if (!_sealed)
            throw new InvalidOperationException("PersistenceTypeRegistry must be sealed before reading its sorted snapshot.");

        return _sealedRecords.AsSpan();
    }

    public void Seal()
    {
        if (_sealed)
            return;

        var records = new PersistenceTypeRecord[_byId.Count];
        int write = 0;
        foreach ((_, PersistenceTypeRecord record) in _byId)
            records[write++] = record;

        Array.Sort(records, static (a, b) => a.PersistenceId.CompareTo(b.PersistenceId));
        _sealedRecords = records;
        _sealed = true;
    }
}
