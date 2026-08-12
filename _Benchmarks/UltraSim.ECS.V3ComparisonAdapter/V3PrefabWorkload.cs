using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal sealed class V3PrefabSpawnWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly PrefabHandle _prefab;
    private readonly Entity[] _entities;

    internal V3PrefabSpawnWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(LogicalComponents.Position);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            maximumPreparedPlans: 4,
            lifecycleRecordCapacity: 1));
        var content = new ContentCatalogBuilder(schema);
        content.Define(new PrefabKey("comparison/position-42"))
            .With(new Position { X = 42, Y = 43 });
        CompiledPrefabCatalog catalog = content.Compile(
            _world,
            new ContentCompilationOptions(
                ChunkLayoutOptions.KiB16,
                V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale)));
        _prefab = catalog.GetHandle(new PrefabKey("comparison/position-42"));
        _lookups = new V3Lookups(_world);
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _entities = new Entity[descriptor.Scale];
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        for (int index = 0; index < _entities.Length; index++)
        {
            Entity entity = _world.Instantiate(_prefab);
            _entities[index] = entity;
            checksum = unchecked(checksum + entity.Index);
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState()
    {
        for (int index = 0; index < _entities.Length; index++)
        {
            if (!_world.TryRead(_entities[index], _lookups.Positions, out Position value) ||
                value.X != 42 || value.Y != 43)
            {
                throw new InvalidOperationException("V3 prefab default state diverged.");
            }
        }
        return V3WorldSupport.CaptureFacadeState(
            _world,
            _descriptor,
            _entities,
            _lookups.Positions,
            _lookups.Velocities,
            _lookups.Healths,
            _lookups.Scalars);
    }

    public void Dispose() => _world.Dispose();
}
