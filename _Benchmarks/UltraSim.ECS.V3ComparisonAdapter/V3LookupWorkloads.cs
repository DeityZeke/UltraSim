using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal readonly struct V3Lookups
{
    internal V3Lookups(EcsWorld world)
    {
        Positions = world.PrepareLookup<Position>();
        Velocities = world.PrepareLookup<Velocity>();
        Healths = world.PrepareLookup<Health>();
        Scalars = world.PrepareLookup<Scalar>();
    }

    internal ComponentLookup<Position> Positions { get; }
    internal ComponentLookup<Velocity> Velocities { get; }
    internal ComponentLookup<Health> Healths { get; }
    internal ComponentLookup<Scalar> Scalars { get; }
}

internal sealed class V3LookupWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly Entity[] _entities;
    private readonly bool _write;

    internal V3LookupWorkload(ScenarioDescriptor descriptor, bool write)
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
                throw new InvalidOperationException("V3 lookup fixture exhausted its archetype.");
            }
        }
        _write = write;
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        if (_write)
        {
            for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            {
                var value = new Position { X = ordinal + 7, Y = ordinal * 2 + 11 };
                if (!_world.TryWrite(_entities[ordinal], _lookups.Positions, value))
                    throw new InvalidOperationException("V3 lookup write lost an entity.");
                checksum = unchecked(checksum + value.X + value.Y);
            }
        }
        else
        {
            for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            {
                if (!_world.TryRead(_entities[ordinal], _lookups.Positions, out Position value))
                    throw new InvalidOperationException("V3 lookup read lost an entity.");
                checksum = unchecked(checksum + value.X + value.Y);
            }
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

internal sealed class V3BoundedLookupWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly V3Lookups _lookups;
    private readonly Entity[] _entities;
    private readonly bool _write;

    internal V3BoundedLookupWorkload(ScenarioDescriptor descriptor, bool write)
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
                throw new InvalidOperationException("V3 bounded lookup fixture exhausted its archetype.");
            }
        }
        _write = write;
    }

    public ScenarioMeasurement Execute()
    {
        var job = new PositionAccessJob(_entities, _write);
        _lookups.Positions.Execute(_world, ref job);
        return new ScenarioMeasurement(_entities.Length, job.Checksum);
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

    private struct PositionAccessJob : IComponentAccessJob<Position>
    {
        private readonly Entity[] _entities;
        private readonly bool _write;

        internal PositionAccessJob(Entity[] entities, bool write)
        {
            _entities = entities;
            _write = write;
            Checksum = 0;
        }

        internal long Checksum;

        public void Execute(in ComponentAccessor<Position> access)
        {
            long checksum = 0;
            if (_write)
            {
                for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
                {
                    var value = new Position { X = ordinal + 7, Y = ordinal * 2 + 11 };
                    if (!access.TryWrite(_entities[ordinal], value))
                        throw new InvalidOperationException("V3 bounded lookup write lost an entity.");
                    checksum = unchecked(checksum + value.X + value.Y);
                }
            }
            else
            {
                for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
                {
                    if (!access.TryRead(_entities[ordinal], out Position value))
                        throw new InvalidOperationException("V3 bounded lookup read lost an entity.");
                    checksum = unchecked(checksum + value.X + value.Y);
                }
            }
            Checksum = checksum;
        }
    }
}
