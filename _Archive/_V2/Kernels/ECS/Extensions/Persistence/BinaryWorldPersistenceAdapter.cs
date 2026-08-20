namespace UltraSim.ECS.Persistence;

/// <summary>
/// File/stream-backed world persistence adapter built on the in-memory entity snapshot model.
/// </summary>
public sealed class BinaryWorldPersistenceAdapter : IEcsPersistenceAdapter
{
    private readonly PersistenceTypeRegistry _registry;

    public BinaryWorldPersistenceAdapter(PersistenceTypeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void Save(World world, IDataWriter writer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(writer);

        if (writer is not BinaryDataWriter binaryWriter)
            throw new InvalidOperationException("BinaryWorldPersistenceAdapter requires BinaryDataWriter.");

        EnsureRegistryReady(world.Schema);
        EntityPersistenceSnapshot snapshot = world.CapturePersistenceSnapshot(_registry);
        BinaryEntityPersistenceSerializer.WriteSnapshot(binaryWriter, snapshot);
        binaryWriter.Flush();
    }

    public void Load(World world, IDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(reader);

        if (reader is not BinaryDataReader binaryReader)
            throw new InvalidOperationException("BinaryWorldPersistenceAdapter requires BinaryDataReader.");

        EnsureRegistryReady(world.Schema);
        EntityPersistenceSnapshot snapshot = BinaryEntityPersistenceSerializer.ReadSnapshot(binaryReader, world.Schema, _registry);
        _ = world.RestorePersistenceSnapshot(snapshot);
    }

    private void EnsureRegistryReady(EcsSchema schema)
    {
        if (_registry.IsSealed)
            return;

        _registry.RegisterSchemaComponents(schema);
        _registry.Seal();
    }
}
