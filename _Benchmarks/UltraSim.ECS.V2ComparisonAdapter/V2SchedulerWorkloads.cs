using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal sealed class V2ScheduledChunkWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;
    private readonly ScheduledChunkSystem _system;

    internal V2ScheduledChunkWorkload(ScenarioDescriptor descriptor, bool parallel)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(
            _world, descriptor.Scale, LogicalComponents.Position | LogicalComponents.Velocity);
        _system = new ScheduledChunkSystem(_world, parallel);
        _world.RegisterSystem(_system);
        _world.ConfigurePhaseStaggering(false);
        if (parallel)
        {
            _world.ConfigureComparisonWorkerCount(backgroundWorkers: 3);
            _world.SetSystemExecutionMode(SystemExecutionMode.ParallelBatches);
        }
        _world.Seal();
        _world.Initialize();
        _world.Tick(1f / 60f);
    }

    public ScenarioMeasurement Execute()
    {
        _world.Tick(1f / 60f);
        return new ScenarioMeasurement(_entities.Length, _system.Checksum);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() => _world.Shutdown();

    private sealed class ScheduledChunkSystem : SystemBase
    {
        private readonly bool _parallel;
        private readonly PatchChunkAction<Position, Velocity> _patch;
        private long _checksum;

        internal ScheduledChunkSystem(World world, bool parallel)
        {
            _parallel = parallel;
            _patch = Patch;
            Descriptor = new SystemDescriptor(
                "comparison.scheduler.chunk",
                SystemStage.Movement,
                defaultPeriodTicks: 1,
                defaultStartOffset: 0,
                ComponentMask.FromIds([world.GetComponentId<Position>()]),
                ComponentMask.FromIds([world.GetComponentId<Velocity>()]));
        }

        public override SystemDescriptor Descriptor { get; }
        internal long Checksum => _checksum;

        public override void Update(in SystemContext context)
        {
            _checksum = 0;
            int visited = _parallel
                ? context.PatchChunksParallel(4_096, _patch)
                : context.PatchChunks(_patch);
            if (visited == 0)
                throw new InvalidOperationException("V2 scheduled chunk system visited no rows.");
        }

        private void Patch(ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, Span<Velocity> velocities)
        {
            long checksum = 0;
            for (int row = 0; row < positions.Length; row++)
            {
                Position position = positions[row];
                velocities[row] = new Velocity(position.X * 2 + 3, position.Y * 2 + 5);
                checksum = unchecked(checksum + position.X + position.Y);
            }
            Interlocked.Add(ref _checksum, checksum);
        }
    }
}

internal sealed class V2ScheduledRangeWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly int[] _values;

    internal V2ScheduledRangeWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _values = new int[descriptor.Scale];
        for (int index = 0; index < _values.Length; index++) _values[index] = index;
        _world = V2WorldSupport.CreateWorld();
        _world.RegisterSystem(new ScheduledRangeSystem(_values));
        _world.ConfigurePhaseStaggering(false);
        _world.ConfigureComparisonWorkerCount(backgroundWorkers: 3);
        _world.SetSystemExecutionMode(SystemExecutionMode.ParallelBatches);
        _world.Seal();
        _world.Initialize();
        _world.Tick(1f / 60f);
    }

    public ScenarioMeasurement Execute()
    {
        _world.Tick(1f / 60f);
        return new ScenarioMeasurement(_values.Length, ArrayState.Checksum(_values));
    }

    public CanonicalState CaptureCanonicalState() => ArrayState.Capture(_descriptor, _values);
    public void Dispose() => _world.Shutdown();

    private sealed class ScheduledRangeSystem : SystemBase
    {
        private const int Partitions = 4;
        private readonly int[] _values;
        private readonly Action<int> _execute;

        internal ScheduledRangeSystem(int[] values)
        {
            _values = values;
            _execute = ExecutePartition;
        }

        public override SystemDescriptor Descriptor { get; } = new(
            "comparison.scheduler.range", SystemStage.WorldSim, 1, 0,
            ComponentMask.Empty, ComponentMask.Empty);

        public override void Update(in SystemContext context) => context.ParallelFor(Partitions, _execute);

        private void ExecutePartition(int partition)
        {
            int start = partition * _values.Length / Partitions;
            int end = (partition + 1) * _values.Length / Partitions;
            for (int index = start; index < end; index++)
            {
                int value = _values[index];
                for (int operation = 0; operation < 8; operation++)
                    value = unchecked(value * 1_664_525 + 1_013_904_223);
                _values[index] = value;
            }
        }
    }
}

internal static class ArrayState
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
