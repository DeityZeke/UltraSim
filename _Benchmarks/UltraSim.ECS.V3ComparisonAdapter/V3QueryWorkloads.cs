using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal sealed class V3ReadQueryWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly Entity[] _entities;
    private readonly int _componentCount;
    private readonly ArchetypeComponentBinding<Position> _positions;
    private readonly ArchetypeComponentBinding<Velocity> _velocities;
    private readonly ArchetypeReadReadQueryPlan<NativeChunkArena, Position, Velocity>? _twoReadPlan;

    internal V3ReadQueryWorkload(ScenarioDescriptor descriptor, int componentCount)
    {
        _descriptor = descriptor;
        _componentCount = componentCount;
        ComponentSchema schema = V3WorldSupport.Schema;
        LogicalComponents components = componentCount == 1
            ? LogicalComponents.Position
            : LogicalComponents.Position | LogicalComponents.Velocity;
        ComponentId[] signature = V3WorldSupport.Signature(components);
        _world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: componentCount == 1 ? 0 : 1);
        ArchetypeId archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale));
        _positions = _world.BindComponent<Position>(archetype, V3WorldSupport.PositionId);
        if (componentCount == 2)
        {
            _velocities = _world.BindComponent<Velocity>(archetype, V3WorldSupport.VelocityId);
            _twoReadPlan = _world.PrepareReadReadQuery<Position, Velocity>(
                V3WorldSupport.PositionId,
                V3WorldSupport.VelocityId);
        }
        _world.Seal();
        _entities = new Entity[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreateInitialized(
                archetype,
                _positions,
                new Position { X = ordinal, Y = ordinal + 1 },
                out _entities[ordinal]))
            {
                throw new InvalidOperationException("V3 read query fixture exhausted its archetype.");
            }
            if (componentCount == 2 && !_world.TrySetComponent(
                _entities[ordinal],
                _velocities,
                new Velocity { X = ordinal + 2, Y = ordinal + 3 }))
            {
                throw new InvalidOperationException("V3 read query could not initialize Velocity.");
            }
        }
        _ = ExecuteCore();
    }

    public ScenarioMeasurement Execute() => new(_entities.Length, ExecuteCore());

    private long ExecuteCore()
    {
        if (_componentCount == 1)
        {
            var visitor = new PositionVisitor();
            int visited = _world.VisitReadChunks(_positions, ref visitor);
            if (visited != _entities.Length)
                throw new InvalidOperationException("V3 one-column query lost rows.");
            return visitor.Checksum;
        }

        var twoVisitor = new PositionVelocityVisitor();
        int twoVisited = _world.VisitReadReadChunks(_twoReadPlan!, ref twoVisitor);
        if (twoVisited != _entities.Length)
            throw new InvalidOperationException("V3 two-column query lost rows.");
        return twoVisitor.Checksum;
    }

    public CanonicalState CaptureCanonicalState()
    {
        var builder = new LogicalStateBuilder(
            _descriptor.Id,
            _descriptor.Scale,
            _entities.Length,
            logicalArchetypes: 1);
        LogicalComponents components = _componentCount == 1
            ? LogicalComponents.Position
            : LogicalComponents.Position | LogicalComponents.Velocity;
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryGetComponent(_entities[ordinal], _positions, out Position position))
                throw new InvalidOperationException("V3 state capture lost Position.");
            Velocity velocity = default;
            if (_componentCount == 2 && !_world.TryGetComponent(_entities[ordinal], _velocities, out velocity))
                throw new InvalidOperationException("V3 state capture lost Velocity.");
            builder.AddEntity(
                ordinal,
                alive: true,
                lifecycleRevision: 0,
                components,
                position.X,
                position.Y,
                velocity.X,
                velocity.Y);
        }
        builder.AddArchetype(components, _entities.Length);
        return builder.Finish();
    }

    public void Dispose() => _world.Dispose();

    private struct PositionVisitor : IComponentChunkReadVisitor<Position>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadOnlyComponentChunk<Position> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
                checksum = unchecked(checksum + chunk.Components[row].X + chunk.Components[row].Y);
            Checksum = checksum;
        }
    }

    private struct PositionVelocityVisitor : IComponentChunkReadReadVisitor<Position, Velocity>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadReadComponentChunk<Position, Velocity> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                checksum = unchecked(
                    checksum +
                    chunk.ReadComponents1[row].X + chunk.ReadComponents1[row].Y +
                    chunk.ReadComponents2[row].X + chunk.ReadComponents2[row].Y);
            }
            Checksum = checksum;
        }
    }
}

internal sealed class V3WriteQueryWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly Entity[] _entities;
    private readonly ArchetypeComponentBinding<Position> _positions;
    private readonly ArchetypeComponentBinding<Velocity> _velocities;
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, Position, Velocity> _plan;

    internal V3WriteQueryWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(
            LogicalComponents.Position | LogicalComponents.Velocity);
        _world = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 1);
        ArchetypeId archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale));
        _positions = _world.BindComponent<Position>(archetype, V3WorldSupport.PositionId);
        _velocities = _world.BindComponent<Velocity>(archetype, V3WorldSupport.VelocityId);
        _plan = _world.PrepareReadWriteQuery<Position, Velocity>(
            V3WorldSupport.PositionId,
            V3WorldSupport.VelocityId);
        _world.Seal();
        _entities = new Entity[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreateInitialized(
                archetype,
                _positions,
                new Position { X = ordinal, Y = ordinal + 1 },
                out _entities[ordinal]) ||
                !_world.TrySetComponent(
                    _entities[ordinal],
                    _velocities,
                    new Velocity { X = ordinal + 2, Y = ordinal + 3 }))
            {
                throw new InvalidOperationException("V3 write query fixture initialization failed.");
            }
        }
        _ = ExecuteCore();
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            _world.TrySetComponent(
                _entities[ordinal],
                _velocities,
                new Velocity { X = ordinal + 2, Y = ordinal + 3 });
        }
    }

    public ScenarioMeasurement Execute() => new(_entities.Length, ExecuteCore());

    private long ExecuteCore()
    {
        var visitor = new PositionVelocityWriter();
        int visited = _world.VisitReadWriteChunks(_plan, ref visitor);
        if (visited != _entities.Length)
            throw new InvalidOperationException("V3 write query lost rows.");
        return visitor.Checksum;
    }

    public CanonicalState CaptureCanonicalState()
    {
        var builder = new LogicalStateBuilder(
            _descriptor.Id,
            _descriptor.Scale,
            _entities.Length,
            logicalArchetypes: 1);
        LogicalComponents components = LogicalComponents.Position | LogicalComponents.Velocity;
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryGetComponent(_entities[ordinal], _positions, out Position position) ||
                !_world.TryGetComponent(_entities[ordinal], _velocities, out Velocity velocity))
            {
                throw new InvalidOperationException("V3 write-query state capture lost an entity.");
            }
            builder.AddEntity(
                ordinal,
                alive: true,
                lifecycleRevision: 0,
                components,
                position.X,
                position.Y,
                velocity.X,
                velocity.Y);
        }
        builder.AddArchetype(components, _entities.Length);
        return builder.Finish();
    }

    public void Dispose() => _world.Dispose();

    private struct PositionVelocityWriter : IComponentChunkReadWriteVisitor<Position, Velocity>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadWriteComponentChunk<Position, Velocity> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                Position position = chunk.ReadComponents[row];
                chunk.WriteComponents[row] = new Velocity
                {
                    X = position.X * 2,
                    Y = position.Y * 2,
                };
                checksum = unchecked(checksum + position.X + position.Y);
            }
            Checksum = checksum;
        }
    }
}
