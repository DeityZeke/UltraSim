using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal sealed class V2PrefabSpawnWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly PrefabDefinition _prefab;
    private readonly Entity[] _entities;

    internal V2PrefabSpawnWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        ComponentId position = _world.GetComponentId<Position>();
        _prefab = PrefabDefinition.CreateResolved(
            new ArchetypeSignature([position]),
            [position],
            static (archetype, slot, columns) =>
                archetype.GetComponentSpanAt<Position>(columns[0])[slot] = new Position(42, 43));
        _entities = new Entity[descriptor.Scale];
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        for (int index = 0; index < _entities.Length; index++)
        {
            Entity entity = _world.CreatePrefab(_prefab);
            _entities[index] = entity;
            checksum = unchecked(checksum + entity.Index);
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState()
    {
        for (int index = 0; index < _entities.Length; index++)
        {
            if (_world.GetComponent<Position>(_entities[index]) != new Position(42, 43))
                throw new InvalidOperationException("V2 prefab default state diverged.");
        }
        return V2WorldSupport.CaptureState(_world, _descriptor, _entities);
    }

    public void Dispose() { }
}
