using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal sealed class V3ScheduledChunkWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly Entity[] _entities;
    private readonly ComponentLookup<Position> _positions;
    private readonly ComponentLookup<Velocity> _velocities;
    private readonly ScheduledChunkRuntime _runtime;

    internal V3ScheduledChunkWorkload(ScenarioDescriptor descriptor, bool parallel)
    {
        _descriptor = descriptor;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(
            LogicalComponents.Position | LogicalComponents.Velocity);
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1,
            maximumDependencies: 0,
            maximumWorkItems: 64,
            maximumStructuralSegments: 64,
            backgroundWorkerCount: parallel ? 3 : 0,
            callerParticipates: true,
            structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 16);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale), 1, 3, scheduler: scheduler));
        ArchetypeHandle archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale) + 1);
        _positions = _world.PrepareLookup<Position>();
        _velocities = _world.PrepareLookup<Velocity>();
        ReadWriteJobPlan<Position, Velocity> plan = _world.PrepareReadWriteJob<Position, Velocity>();
        _runtime = new ScheduledChunkRuntime();
        _world.RegisterReadWriteSystem<Position, Velocity, ScheduledChunkRuntime, ScheduledChunkJob>(
            new SystemDefinition(
                "comparison.scheduler.chunk",
                SystemStage.Movement,
                executionPolicy: parallel ? SystemExecutionPolicy.Parallel : SystemExecutionPolicy.Serial,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 4_096),
            plan,
            _runtime);
        _world.Seal();
        _world.Initialize();
        _entities = new Entity[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreate(archetype, out Entity entity) ||
                !_world.TryWrite(entity, _positions, new Position { X = ordinal, Y = ordinal + 1 }) ||
                !_world.TryWrite(entity, _velocities, new Velocity { X = ordinal + 2, Y = ordinal + 3 }))
            {
                throw new InvalidOperationException("V3 scheduler comparison fixture failed to seed an entity.");
            }
            _entities[ordinal] = entity;
        }
        _world.BeginRun();
        _world.Tick(1d / 60d);
    }

    public ScenarioMeasurement Execute()
    {
        _world.Tick(1d / 60d);
        return new ScenarioMeasurement(_entities.Length, _runtime.Checksum);
    }

    public CanonicalState CaptureCanonicalState()
    {
        var builder = new LogicalStateBuilder(
            _descriptor.Id, _descriptor.Scale, _entities.Length, logicalArchetypes: 1);
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryRead(_entities[ordinal], _positions, out Position position) ||
                !_world.TryRead(_entities[ordinal], _velocities, out Velocity velocity))
                throw new InvalidOperationException("V3 scheduler state capture lost an entity.");
            builder.AddEntity(
                ordinal, true, 0, LogicalComponents.Position | LogicalComponents.Velocity,
                position.X, position.Y, velocity.X, velocity.Y);
        }
        builder.AddArchetype(LogicalComponents.Position | LogicalComponents.Velocity, _entities.Length);
        return builder.Finish();
    }

    public void Dispose() => _world.Dispose();

    private sealed class ScheduledChunkRuntime : ISystemRuntime<ScheduledChunkJob>
    {
        internal long Checksum;
        public ScheduledChunkJob Prepare(in SystemPrepareContext context) => default;
        public void Complete(in SystemCompleteContext context, ReadOnlySpan<ScheduledChunkJob> jobs)
        {
            long checksum = 0;
            for (int index = 0; index < jobs.Length; index++)
                checksum = unchecked(checksum + jobs[index].Checksum);
            Checksum = checksum;
        }
    }

    private struct ScheduledChunkJob : IScheduledReadWriteChunkJob<Position, Velocity>
    {
        internal long Checksum;
        public void Execute(in SystemJobContext context, in ReadWriteComponentChunk<Position, Velocity> chunk)
        {
            ReadOnlySpan<Position> positions = chunk.ReadComponents;
            Span<Velocity> velocities = chunk.WriteComponents;
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                Position position = positions[row];
                velocities[row] = new Velocity { X = position.X * 2 + 3, Y = position.Y * 2 + 5 };
                checksum = unchecked(checksum + position.X + position.Y);
            }
            Checksum = checksum;
        }
    }
}

internal sealed class V3ScheduledRangeWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly int[] _values;

    internal V3ScheduledRangeWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _values = new int[descriptor.Scale];
        for (int index = 0; index < _values.Length; index++) _values[index] = index;
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 1, maximumDependencies: 0, maximumWorkItems: 16,
            maximumStructuralSegments: 16, backgroundWorkerCount: 3,
            callerParticipates: true, structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0, maximumPartitionsPerSystem: 4);
        _world = new EcsWorld(V3WorldSupport.Schema, new EcsWorldOptions(
            new EntityCapacity(1), 1, 0, scheduler: scheduler));
        _world.RegisterArchetype([], ChunkLayoutOptions.KiB16, 1);
        _world.RegisterRangeSystem<ScheduledRangeRuntime, ScheduledRangeJob>(
            new SystemDefinition(
                "comparison.scheduler.range", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 1),
            new ScheduledRangeRuntime(_values));
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _world.Tick(1d / 60d);
    }

    public ScenarioMeasurement Execute()
    {
        _world.Tick(1d / 60d);
        return new ScenarioMeasurement(_values.Length, V3ArrayState.Checksum(_values));
    }

    public CanonicalState CaptureCanonicalState() => V3ArrayState.Capture(_descriptor, _values);
    public void Dispose() => _world.Dispose();

    private sealed class ScheduledRangeRuntime : IRangeSystemRuntime<ScheduledRangeJob>
    {
        private readonly int[] _values;
        internal ScheduledRangeRuntime(int[] values) => _values = values;
        public int GetRangeCount(in SystemPrepareContext context) => _values.Length;
        public ScheduledRangeJob Prepare(in SystemPrepareContext context) => new(_values);
    }

    private readonly struct ScheduledRangeJob : IRangeJob
    {
        private readonly int[] _values;
        internal ScheduledRangeJob(int[] values) => _values = values;
        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                int value = _values[index];
                for (int operation = 0; operation < 8; operation++)
                    value = unchecked(value * 1_664_525 + 1_013_904_223);
                _values[index] = value;
            }
        }
    }
}

internal static class V3ArrayState
{
    internal static long Checksum(ReadOnlySpan<int> values)
    {
        long checksum = 0;
        for (int index = 0; index < values.Length; index++)
            checksum = unchecked(checksum * 397 + values[index]);
        return checksum;
    }

    internal static CanonicalState Capture(ScenarioDescriptor descriptor, ReadOnlySpan<int> values)
    {
        var hasher = new CanonicalStateHasher();
        hasher.AddString("UltraSim.ECS.RangeState.v1");
        hasher.AddString(descriptor.Id);
        hasher.AddInt32(values.Length);
        for (int index = 0; index < values.Length; index++) hasher.AddInt32(values[index]);
        return new CanonicalState(hasher.Finish(), 0, 0, 0, 0);
    }
}
