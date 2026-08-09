using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal sealed class V2ChangeCollectWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;
    private readonly Entity[] _dirty;
    private readonly int[] _ordinalByIndex;
    private readonly bool[] _changed;
    private readonly int _stride;

    internal V2ChangeCollectWorkload(ScenarioDescriptor descriptor, int stride)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(
            _world,
            descriptor.Scale,
            LogicalComponents.Position | LogicalComponents.Scalar);
        _dirty = new Entity[descriptor.Scale];
        uint maximumIndex = _entities.Max(static entity => entity.Index);
        _ordinalByIndex = Enumerable.Repeat(-1, checked((int)maximumIndex + 1)).ToArray();
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            _ordinalByIndex[_entities[ordinal].Index] = ordinal;
        _changed = new bool[descriptor.Scale];
        _stride = stride;
    }

    public ScenarioMeasurement Execute()
    {
        int expected = 0;
        for (int ordinal = 0; _stride != 0 && ordinal < _entities.Length; ordinal += _stride)
        {
            if (!_world.SetComponent(_entities[ordinal], new Scalar(ordinal * 3 + 17)))
                throw new InvalidOperationException("V2 change write lost an entity.");
            expected++;
        }
        int drained = _world.DrainDirtyTo<Scalar>(_dirty);
        if (drained != expected)
            throw new InvalidOperationException($"V2 drained {drained} Scalar entities; expected {expected}.");
        long checksum = 0;
        for (int index = 0; index < drained; index++)
        {
            int ordinal = _ordinalByIndex[_dirty[index].Index];
            if (ordinal < 0 || _changed[ordinal])
                throw new InvalidOperationException("V2 dirty output was invalid or duplicated.");
            _changed[ordinal] = true;
            checksum = unchecked(checksum + ordinal);
        }
        return new ScenarioMeasurement(_descriptor.OperationsPerIteration, checksum);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities, changed: _changed);

    public void Dispose() { }
}
