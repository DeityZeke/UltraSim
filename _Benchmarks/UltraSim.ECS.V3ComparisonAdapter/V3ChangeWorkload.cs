using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal sealed class V3ChangeCollectWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly Entity[] _entities;
    private readonly ArchetypeComponentBinding<Position> _positions;
    private readonly ArchetypeComponentBinding<Scalar> _scalars;
    private readonly ArchetypeChangedReadQueryPlan<NativeChunkArena, Position> _plan;
    private readonly int[] _ordinalByIndex;
    private readonly bool[] _changed;
    private ChangeCursor _cursor;
    private readonly int _stride;

    internal V3ChangeCollectWorkload(ScenarioDescriptor descriptor, int stride)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(
            LogicalComponents.Position | LogicalComponents.Scalar);
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
        _scalars = _world.BindComponent<Scalar>(archetype, V3WorldSupport.ScalarId);
        _plan = _world.PrepareChangedRowsQuery<Position>(
            V3WorldSupport.PositionId,
            V3WorldSupport.ScalarId);
        _world.Seal();
        _entities = new Entity[descriptor.Scale];
        _ordinalByIndex = new int[descriptor.Scale];
        _changed = new bool[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreateInitialized(
                archetype,
                _positions,
                new Position { X = ordinal, Y = ordinal + 1 },
                out _entities[ordinal]) ||
                !_world.TrySetComponent(
                    _entities[ordinal],
                    _scalars,
                    new Scalar { Value = ordinal + 5 }))
            {
                throw new InvalidOperationException("V3 changed-row fixture initialization failed.");
            }
            _ordinalByIndex[_entities[ordinal].Index] = ordinal;
        }
        _cursor = _world.CreateChangeCursorFromNow();
        _stride = stride;
    }

    public ScenarioMeasurement Execute()
    {
        int expected = 0;
        for (int ordinal = 0; _stride != 0 && ordinal < _entities.Length; ordinal += _stride)
        {
            if (!_world.TrySetComponent(
                _entities[ordinal],
                _scalars,
                new Scalar { Value = ordinal * 3 + 17 }))
            {
                throw new InvalidOperationException("V3 change write lost an entity.");
            }
            expected++;
        }
        var visitor = new ChangedVisitor(_ordinalByIndex, _changed);
        int visited = _world.VisitChangedReadRows(_plan, ref _cursor, ref visitor);
        if (visited != expected || visitor.Rows != expected)
            throw new InvalidOperationException($"V3 changed-row query visited {visited}; expected {expected}.");
        return new ScenarioMeasurement(_descriptor.OperationsPerIteration, visitor.Checksum);
    }

    public CanonicalState CaptureCanonicalState()
    {
        var builder = new LogicalStateBuilder(
            _descriptor.Id,
            _descriptor.Scale,
            _entities.Length,
            logicalArchetypes: 1,
            changedEntities: _changed.Count(static value => value));
        LogicalComponents components = LogicalComponents.Position | LogicalComponents.Scalar;
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryGetComponent(_entities[ordinal], _positions, out Position position) ||
                !_world.TryGetComponent(_entities[ordinal], _scalars, out Scalar scalar))
            {
                throw new InvalidOperationException("V3 changed-row state capture lost an entity.");
            }
            builder.AddEntity(
                ordinal,
                alive: true,
                lifecycleRevision: 0,
                components,
                position.X,
                position.Y,
                scalar: scalar.Value);
        }
        builder.AddArchetype(components, _entities.Length);
        for (int ordinal = 0; ordinal < _changed.Length; ordinal++)
        {
            if (_changed[ordinal])
                builder.AddChangedEntity(ordinal);
        }
        return builder.Finish();
    }

    public void Dispose() => _world.Dispose();

    private struct ChangedVisitor : IComponentChunkReadVisitor<Position>
    {
        private readonly int[] _ordinalByIndex;
        private readonly bool[] _changed;

        internal ChangedVisitor(int[] ordinalByIndex, bool[] changed)
        {
            _ordinalByIndex = ordinalByIndex;
            _changed = changed;
            Rows = 0;
            Checksum = 0;
        }

        internal int Rows;
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadOnlyComponentChunk<Position> chunk)
        {
            int rows = Rows;
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                int ordinal = _ordinalByIndex[chunk.EntityIndices[row]];
                if (_changed[ordinal])
                    throw new InvalidOperationException("V3 changed-row output duplicated an entity.");
                _changed[ordinal] = true;
                rows++;
                checksum = unchecked(checksum + ordinal);
            }
            Rows = rows;
            Checksum = checksum;
        }
    }
}
