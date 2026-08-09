using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal enum V2StructuralOperation
{
    Create,
    Destroy,
    Add,
    Remove,
}

internal sealed class V2ImmediateCreateWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly ArchetypeSignature _signature;
    private readonly Entity[] _entities;

    internal V2ImmediateCreateWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _signature = V2WorldSupport.Signature(_world, LogicalComponents.Position);
        _entities = new Entity[descriptor.Scale];
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            _entities[ordinal] = _world.CreateDefault(_signature);
            checksum = unchecked(checksum + _entities[ordinal].Index);
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() { }
}

internal sealed class V2ImmediateDestroyWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;

    internal V2ImmediateDestroyWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(_world, descriptor.Scale, LogicalComponents.Position);
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        for (int index = 0; index < _entities.Length; index++)
        {
            _world.Destroy(_entities[index]);
            checksum = unchecked(checksum + _entities[index].Index);
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() { }
}

internal sealed class V2DeferredStructuralWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly V2StructuralOperation _operation;
    private readonly MeasurementBoundary _boundary;
    private readonly Entity[] _entities;
    private readonly ArchetypeSignature[]? _createSignatures;
    private readonly Entity[]? _targets;
    private bool _recorded;
    private bool _played;

    internal V2DeferredStructuralWorkload(
        ScenarioDescriptor descriptor,
        V2StructuralOperation operation,
        bool sparse = false)
    {
        _descriptor = descriptor;
        _operation = operation;
        _boundary = descriptor.Boundary;
        _world = V2WorldSupport.CreateWorld();
        if (operation == V2StructuralOperation.Create)
        {
            _entities = new Entity[descriptor.Scale];
            ArchetypeSignature signature = V2WorldSupport.Signature(_world, LogicalComponents.Position);
            _createSignatures = Enumerable.Repeat(signature, descriptor.Scale).ToArray();
        }
        else
        {
            LogicalComponents components = operation == V2StructuralOperation.Remove
                ? LogicalComponents.Position | LogicalComponents.Velocity
                : LogicalComponents.Position;
            _entities = V2WorldSupport.Seed(_world, descriptor.Scale, components);
            if (sparse)
            {
                _targets = new Entity[descriptor.Scale / 10];
                for (int index = 0; index < _targets.Length; index++)
                    _targets[index] = _entities[index * 10];
            }
        }
        if (_boundary == MeasurementBoundary.PlaybackOnly)
            Record();
    }

    public ScenarioMeasurement Execute()
    {
        if (_boundary is MeasurementBoundary.RecordOnly or MeasurementBoundary.EndToEnd)
            Record();
        if (_boundary is MeasurementBoundary.PlaybackOnly or MeasurementBoundary.EndToEnd)
            Playback();
        return new ScenarioMeasurement(_descriptor.OperationsPerIteration, unchecked((long)_world.Entities.AliveCount + (int)_operation));
    }

    private void Record()
    {
        if (_recorded)
            throw new InvalidOperationException("V2 structural workload was recorded twice.");
        switch (_operation)
        {
            case V2StructuralOperation.Create:
                _world.EnqueueCreateDefault(_createSignatures!);
                break;
            case V2StructuralOperation.Destroy:
                _world.EnqueueDestroy(_targets ?? _entities);
                break;
            case V2StructuralOperation.Add:
                _world.EnqueueAdd<Velocity>(_entities, default(Velocity));
                break;
            case V2StructuralOperation.Remove:
                _world.EnqueueRemove<Velocity>(_entities);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        _recorded = true;
    }

    private void Playback()
    {
        if (!_recorded || _played)
            throw new InvalidOperationException("V2 structural workload has invalid playback state.");
        _world.ProcessStructuralQueues();
        _played = true;
        if (_operation == V2StructuralOperation.Create)
        {
            Entity[] captured = V2WorldSupport.CapturePositionEntities(_world, _entities.Length);
            captured.CopyTo(_entities, 0);
        }
    }

    public CanonicalState CaptureCanonicalState()
    {
        if (!_played)
            Playback();
        return V2WorldSupport.CaptureState(_world, _descriptor, _entities);
    }

    public void Dispose() { }
}

internal sealed class V2MigrationRoundTripWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;

    internal V2MigrationRoundTripWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(_world, descriptor.Scale, LogicalComponents.Position);
    }

    public ScenarioMeasurement Execute()
    {
        _world.EnqueueAdd<Velocity>(_entities, default(Velocity));
        _world.ProcessStructuralQueues();
        _world.EnqueueRemove<Velocity>(_entities);
        _world.ProcessStructuralQueues();
        return new ScenarioMeasurement(_entities.Length * 2L, _world.Entities.AliveCount);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() { }
}

internal sealed class V2ReuseWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _oldEntities;
    private readonly Entity[] _newEntities;
    private readonly ArchetypeSignature _signature;
    private readonly bool[] _oldSlots;
    private int _reused;

    internal V2ReuseWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _oldEntities = V2WorldSupport.Seed(_world, descriptor.Scale, LogicalComponents.Position);
        _newEntities = new Entity[descriptor.Scale];
        _signature = V2WorldSupport.Signature(_world, LogicalComponents.Position);
        _oldSlots = new bool[descriptor.Scale];
        for (int index = 0; index < _oldEntities.Length; index++)
            _oldSlots[_oldEntities[index].Index] = true;
    }

    public ScenarioMeasurement Execute()
    {
        for (int index = 0; index < _oldEntities.Length; index++)
            _world.Destroy(_oldEntities[index]);
        _reused = 0;
        long checksum = 0;
        for (int index = 0; index < _newEntities.Length; index++)
        {
            Entity entity = _world.CreateDefault(_signature);
            _newEntities[index] = entity;
            if (entity.Index < _oldSlots.Length && _oldSlots[entity.Index])
                _reused++;
            checksum = unchecked(checksum + entity.Index + entity.Version);
        }
        return new ScenarioMeasurement(_newEntities.Length * 2L, checksum);
    }

    public CanonicalState CaptureCanonicalState()
    {
        if (_reused != _oldEntities.Length)
            throw new InvalidOperationException("V2 did not reuse every freed slot.");
        for (int index = 0; index < _oldEntities.Length; index++)
        {
            if (_world.Entities.IsAlive(_oldEntities[index]))
                throw new InvalidOperationException("V2 accepted a stale handle after reuse.");
        }
        return V2WorldSupport.CaptureState(
            _world,
            _descriptor,
            _newEntities,
            lifecycleRevision: 1,
            reusedSlots: _reused);
    }

    public void Dispose() { }
}
