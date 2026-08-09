using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal enum V3StructuralOperation
{
    Create,
    Destroy,
    Add,
    Remove,
}

internal sealed class V3ImmediateCreateWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly ArchetypeHandle _archetype;
    private readonly Entity[] _entities;

    internal V3ImmediateCreateWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(LogicalComponents.Position);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            maximumPreparedPlans: 4));
        _archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale));
        _lookups = new V3Lookups(_world);
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _entities = new Entity[descriptor.Scale];
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreate(_archetype, out _entities[ordinal]))
                throw new InvalidOperationException("V3 immediate create exhausted its archetype.");
            checksum = unchecked(checksum + _entities[ordinal].Index);
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState() => V3WorldSupport.CaptureFacadeState(
        _world,
        _descriptor,
        _entities,
        _lookups.Positions,
        _lookups.Velocities,
        _lookups.Healths,
        _lookups.Scalars);

    public void Dispose() => _world.Dispose();
}

internal sealed class V3ImmediateDestroyWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly Entity[] _entities;

    internal V3ImmediateDestroyWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(LogicalComponents.Position);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            maximumPreparedPlans: 4));
        ArchetypeHandle archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale));
        _lookups = new V3Lookups(_world);
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _entities = new Entity[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreate(
                archetype,
                _lookups.Positions,
                new Position { X = ordinal, Y = ordinal + 1 },
                out _entities[ordinal]))
            {
                throw new InvalidOperationException("V3 immediate destroy fixture exhausted its archetype.");
            }
        }
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        for (int index = 0; index < _entities.Length; index++)
        {
            if (!_world.TryDestroy(_entities[index]))
                throw new InvalidOperationException("V3 immediate destroy lost an entity.");
            checksum = unchecked(checksum + _entities[index].Index);
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState() => V3WorldSupport.CaptureFacadeState(
        _world,
        _descriptor,
        _entities,
        _lookups.Positions,
        _lookups.Velocities,
        _lookups.Healths,
        _lookups.Scalars);

    public void Dispose() => _world.Dispose();
}

internal sealed class V3DeferredStructuralWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly StructuralCommandBuffer _commands;
    private readonly V3StructuralOperation _operation;
    private readonly MeasurementBoundary _boundary;
    private readonly Entity[] _entities;
    private readonly ArchetypeHandle[]? _createArchetypes;
    private readonly DeferredEntity[]? _deferred;
    private readonly Entity[]? _targets;
    private readonly ArchetypeTransition _transition;
    private bool _recorded;
    private bool _played;

    internal V3DeferredStructuralWorkload(
        ScenarioDescriptor descriptor,
        V3StructuralOperation operation,
        bool sparse = false)
    {
        _descriptor = descriptor;
        _operation = operation;
        _boundary = descriptor.Boundary;
        ComponentSchema schema = V3WorldSupport.Schema;
        LogicalComponents sourceComponents = operation == V3StructuralOperation.Remove
            ? LogicalComponents.Position | LogicalComponents.Velocity
            : LogicalComponents.Position;
        LogicalComponents destinationComponents = operation == V3StructuralOperation.Add
            ? LogicalComponents.Position | LogicalComponents.Velocity
            : LogicalComponents.Position;
        ComponentId[] sourceSignature = V3WorldSupport.Signature(sourceComponents);
        ComponentId[] destinationSignature = V3WorldSupport.Signature(destinationComponents);
        int archetypeCount = operation is V3StructuralOperation.Add or V3StructuralOperation.Remove ? 2 : 1;
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: archetypeCount,
            maximumPreparedPlans: 4,
            maximumPreparedTransitions: archetypeCount == 2 ? 1 : 0,
            maximumStructuralCommands: checked((int)Math.Max(1, descriptor.OperationsPerIteration))));
        ArchetypeHandle source = _world.RegisterArchetype(
            sourceSignature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, sourceSignature, descriptor.Scale));
        ArchetypeHandle destination = source;
        if (archetypeCount == 2)
        {
            destination = _world.RegisterArchetype(
                destinationSignature,
                ChunkLayoutOptions.KiB16,
                V3WorldSupport.MaximumChunks(schema, destinationSignature, descriptor.Scale));
            _transition = _world.PrepareTransition(source, destination);
        }
        _lookups = new V3Lookups(_world);
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _commands = _world.StructuralCommands;
        _entities = new Entity[descriptor.Scale];
        if (operation == V3StructuralOperation.Create)
        {
            _createArchetypes = Enumerable.Repeat(source, descriptor.Scale).ToArray();
            _deferred = new DeferredEntity[descriptor.Scale];
        }
        else
        {
            for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            {
                if (!_world.TryCreate(
                    source,
                    _lookups.Positions,
                    new Position { X = ordinal, Y = ordinal + 1 },
                    out _entities[ordinal]))
                {
                    throw new InvalidOperationException("V3 structural fixture exhausted its source.");
                }
                if (operation == V3StructuralOperation.Remove &&
                    !_world.TryWrite(
                        _entities[ordinal],
                        _lookups.Velocities,
                        new Velocity { X = ordinal + 2, Y = ordinal + 3 }))
                {
                    throw new InvalidOperationException("V3 structural fixture could not initialize Velocity.");
                }
            }
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
        StructuralPlaybackResult result = default;
        if (_boundary is MeasurementBoundary.PlaybackOnly or MeasurementBoundary.EndToEnd)
            result = Playback();
        return new ScenarioMeasurement(
            _descriptor.OperationsPerIteration,
            unchecked((long)result.AppliedCount + result.RejectedCount + (int)_operation));
    }

    private void Record()
    {
        if (_recorded)
            throw new InvalidOperationException("V3 structural workload was recorded twice.");
        switch (_operation)
        {
            case V3StructuralOperation.Create:
                _commands.EnqueueCreateDefault(_createArchetypes!, _deferred!);
                break;
            case V3StructuralOperation.Destroy:
                _commands.EnqueueDestroy(_targets ?? _entities);
                break;
            case V3StructuralOperation.Add:
            case V3StructuralOperation.Remove:
                _commands.EnqueueTransition(_entities, _transition);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        _recorded = true;
    }

    private StructuralPlaybackResult Playback()
    {
        if (!_recorded || _played)
            throw new InvalidOperationException("V3 structural workload has invalid playback state.");
        StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
        if (result.RejectedCount != 0 || result.AppliedCount != _descriptor.OperationsPerIteration)
            throw new InvalidOperationException($"V3 structural playback applied {result.AppliedCount} and rejected {result.RejectedCount}.");
        _played = true;
        if (_operation == V3StructuralOperation.Create)
        {
            for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            {
                if (!_commands.TryResolve(_deferred![ordinal], out _entities[ordinal]))
                    throw new InvalidOperationException("V3 deferred create token did not resolve.");
            }
        }
        return result;
    }

    public CanonicalState CaptureCanonicalState()
    {
        if (!_played)
            _ = Playback();
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

internal sealed class V3MigrationRoundTripWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly StructuralCommandBuffer _commands;
    private readonly ArchetypeTransition _forward;
    private readonly ArchetypeTransition _backward;
    private readonly Entity[] _entities;

    internal V3MigrationRoundTripWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] sourceSignature = V3WorldSupport.Signature(LogicalComponents.Position);
        ComponentId[] destinationSignature = V3WorldSupport.Signature(
            LogicalComponents.Position | LogicalComponents.Velocity);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 2,
            maximumPreparedPlans: 4,
            maximumPreparedTransitions: 2,
            maximumStructuralCommands: descriptor.Scale));
        ArchetypeHandle source = _world.RegisterArchetype(
            sourceSignature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, sourceSignature, descriptor.Scale));
        ArchetypeHandle destination = _world.RegisterArchetype(
            destinationSignature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, destinationSignature, descriptor.Scale));
        _forward = _world.PrepareTransition(source, destination);
        _backward = _world.PrepareTransition(destination, source);
        _lookups = new V3Lookups(_world);
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _commands = _world.StructuralCommands;
        _entities = new Entity[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreate(
                source,
                _lookups.Positions,
                new Position { X = ordinal, Y = ordinal + 1 },
                out _entities[ordinal]))
            {
                throw new InvalidOperationException("V3 migration fixture exhausted its source.");
            }
        }
    }

    public ScenarioMeasurement Execute()
    {
        _commands.EnqueueTransition(_entities, _forward);
        StructuralPlaybackResult forward = _world.PlaybackStructuralCommands();
        _commands.EnqueueTransition(_entities, _backward);
        StructuralPlaybackResult backward = _world.PlaybackStructuralCommands();
        if (forward.AddedCount != _entities.Length || backward.RemovedCount != _entities.Length ||
            forward.RejectedCount != 0 || backward.RejectedCount != 0)
        {
            throw new InvalidOperationException("V3 migration round trip did not apply exactly once.");
        }
        return new ScenarioMeasurement(
            _entities.Length * 2L,
            unchecked((long)forward.AddedCount + backward.RemovedCount));
    }

    public CanonicalState CaptureCanonicalState() => V3WorldSupport.CaptureFacadeState(
        _world,
        _descriptor,
        _entities,
        _lookups.Positions,
        _lookups.Velocities,
        _lookups.Healths,
        _lookups.Scalars);

    public void Dispose() => _world.Dispose();
}

internal sealed class V3ReuseWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly ArchetypeHandle _archetype;
    private readonly Entity[] _oldEntities;
    private readonly Entity[] _newEntities;
    private readonly bool[] _oldSlots;
    private int _reused;

    internal V3ReuseWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(LogicalComponents.Position);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            maximumPreparedPlans: 4));
        _archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale));
        _lookups = new V3Lookups(_world);
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _oldEntities = new Entity[descriptor.Scale];
        _newEntities = new Entity[descriptor.Scale];
        _oldSlots = new bool[descriptor.Scale];
        for (int ordinal = 0; ordinal < _oldEntities.Length; ordinal++)
        {
            if (!_world.TryCreate(
                _archetype,
                _lookups.Positions,
                new Position { X = ordinal, Y = ordinal + 1 },
                out _oldEntities[ordinal]))
            {
                throw new InvalidOperationException("V3 reuse fixture exhausted its archetype.");
            }
            _oldSlots[_oldEntities[ordinal].Index] = true;
        }
    }

    public ScenarioMeasurement Execute()
    {
        for (int index = 0; index < _oldEntities.Length; index++)
        {
            if (!_world.TryDestroy(_oldEntities[index]))
                throw new InvalidOperationException("V3 immediate destroy lost an entity.");
        }
        _reused = 0;
        long checksum = 0;
        for (int index = 0; index < _newEntities.Length; index++)
        {
            if (!_world.TryCreate(_archetype, out Entity entity))
                throw new InvalidOperationException("V3 replacement create exhausted its archetype.");
            _newEntities[index] = entity;
            if (entity.Index < _oldSlots.Length && _oldSlots[entity.Index])
                _reused++;
            checksum = unchecked(checksum + entity.Index + entity.Generation);
        }
        return new ScenarioMeasurement(_newEntities.Length * 2L, checksum);
    }

    public CanonicalState CaptureCanonicalState()
    {
        if (_reused != _oldEntities.Length)
            throw new InvalidOperationException("V3 did not reuse every freed slot.");
        for (int index = 0; index < _oldEntities.Length; index++)
        {
            if (_world.IsAlive(_oldEntities[index]))
                throw new InvalidOperationException("V3 accepted a stale handle after reuse.");
        }
        return V3WorldSupport.CaptureFacadeState(
            _world,
            _descriptor,
            _newEntities,
            _lookups.Positions,
            _lookups.Velocities,
            _lookups.Healths,
            _lookups.Scalars,
            lifecycleRevision: 1,
            reusedSlots: _reused);
    }

    public void Dispose() => _world.Dispose();
}
