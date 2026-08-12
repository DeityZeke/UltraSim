using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

internal abstract class SystemNode
{
    private SystemSchedule _schedule;
    private long _lastElapsedTicks;
    private long _peakElapsedTicks;
    private double _averageElapsedTicks;
    private ulong _runCount;

    protected SystemNode(SystemId id, SystemDefinition definition)
    {
        Id = id;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _schedule = definition.Schedule;
        Publication = new SchedulerChangePublication();
    }

    internal SystemId Id { get; }
    internal SystemDefinition Definition { get; }
    internal SchedulerChangePublication Publication { get; }
    internal int CompiledWave { get; set; }
    internal int RegistrationOrdinal => Id.Value;
    internal bool HasWrites { get; set; }
    internal virtual bool SupportsParallel => true;
    internal virtual bool IsRangeWork => false;
    internal ulong LastRunTick { get; private set; }
    internal int LastRows { get; private set; }
    internal int LastChunks { get; private set; }
    internal int LastWorkItems { get; private set; }
    internal bool LastParallel { get; private set; }
    internal SystemSchedule Schedule => _schedule;

    internal bool IsDue(ulong tick) =>
        _schedule.Enabled &&
        tick >= unchecked((ulong)_schedule.StartOffset) &&
        ((tick - unchecked((ulong)_schedule.StartOffset)) % unchecked((ulong)_schedule.PeriodTicks)) == 0;

    internal void SetEnabled(bool enabled) =>
        _schedule = new SystemSchedule(enabled, _schedule.PeriodTicks, _schedule.StartOffset);

    internal void SetSchedule(int periodTicks, int startOffset) =>
        _schedule = new SystemSchedule(_schedule.Enabled, periodTicks, startOffset);

    internal void ApplyAutomaticStagger(int startOffset) =>
        _schedule = new SystemSchedule(_schedule.Enabled, _schedule.PeriodTicks, startOffset);

    internal abstract void Initialize(EcsWorld world);
    internal abstract void Shutdown(EcsWorld world);
    internal abstract SchedulerQueryShape GetShape();
    internal abstract void Prepare(ulong tick, double deltaSeconds, bool manual, ChangeVersion candidate);
    internal abstract int BuildPartitions(
        in SchedulerQueryShape shape,
        Span<SchedulerChunkPartition> destination,
        int desiredPartitions);
    internal abstract int ExecutePartition(
        int participant,
        in SchedulerChunkPartition partition,
        in SystemJobContext context);
    internal abstract void Complete(ulong tick, double deltaSeconds, bool parallel, bool manual,
        int rows, int chunks, int workItems);
    internal virtual int GetCompletedRows(in SchedulerQueryShape shape) => shape.Rows;
    internal abstract long ReservedPayloadBytes { get; }
    internal virtual IPersistentSystemState? PersistentState => null;

    internal void RecordRun(ulong tick, long elapsedTicks, bool parallel, int rows, int chunks, int workItems)
    {
        LastRunTick = tick;
        LastParallel = parallel;
        LastRows = rows;
        LastChunks = chunks;
        LastWorkItems = workItems;
        _lastElapsedTicks = elapsedTicks;
        if (elapsedTicks > _peakElapsedTicks)
            _peakElapsedTicks = elapsedTicks;
        _runCount++;
        _averageElapsedTicks += (elapsedTicks - _averageElapsedTicks) / _runCount;
    }

    internal SystemRuntimeSnapshot Snapshot() => new(
        Id,
        Definition.Key,
        Definition.Stage,
        _schedule.Enabled,
        Definition.Manual,
        _schedule.PeriodTicks,
        _schedule.StartOffset,
        LastRunTick,
        _runCount,
        ToMicroseconds(_lastElapsedTicks),
        ToMicroseconds(_averageElapsedTicks),
        ToMicroseconds(_peakElapsedTicks),
        Definition.ExecutionPolicy,
        CompiledWave,
        LastParallel,
        LastRows,
        LastChunks,
        LastWorkItems);

    private static double ToMicroseconds(double ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
}

internal abstract class ScheduledSystemNode<TSystem, TJob> : SystemNode
    where TSystem : class, ISystemRuntime<TJob>
    where TJob : struct
{
    private readonly TJob[] _participantJobs;
    private readonly bool[] _participantUsed;
    protected readonly TSystem Runtime;

    protected ScheduledSystemNode(SystemId id, SystemDefinition definition, TSystem runtime, int participants)
        : base(id, definition)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _participantJobs = new TJob[participants];
        _participantUsed = new bool[participants];
    }

    internal override void Initialize(EcsWorld world) =>
        Runtime.Initialize(new SystemInitializationContext(world, Id));

    internal override void Shutdown(EcsWorld world) =>
        Runtime.Shutdown(new SystemShutdownContext(world, Id));

    internal override void Prepare(ulong tick, double deltaSeconds, bool manual, ChangeVersion candidate)
    {
        TJob job = Runtime.Prepare(new SystemPrepareContext(tick, deltaSeconds, Id, manual));
        for (int index = 0; index < _participantJobs.Length; index++)
        {
            _participantJobs[index] = job;
            _participantUsed[index] = false;
        }
        Publication.Reset(candidate);
    }

    protected ref TJob GetParticipantJob(int participant)
    {
        _participantUsed[participant] = true;
        return ref _participantJobs[participant];
    }

    internal override IPersistentSystemState? PersistentState => Runtime as IPersistentSystemState;

    internal override long ReservedPayloadBytes => checked(
        (long)_participantJobs.Length * Unsafe.SizeOf<TJob>() +
        _participantUsed.Length * sizeof(bool));

    internal override void Complete(ulong tick, double deltaSeconds, bool parallel, bool manual,
        int rows, int chunks, int workItems)
    {
        int resultCount = 0;
        for (int participant = 0; participant < _participantJobs.Length; participant++)
        {
            if (!_participantUsed[participant])
                continue;
            if (resultCount != participant)
                _participantJobs[resultCount] = _participantJobs[participant];
            resultCount++;
        }

        Runtime.Complete(
            new SystemCompleteContext(tick, deltaSeconds, Id, rows, chunks, workItems, parallel, manual),
            _participantJobs.AsSpan(0, resultCount));
    }
}

internal sealed class RangeSystemNode<TSystem, TJob> : ScheduledSystemNode<TSystem, TJob>
    where TSystem : class, IRangeSystemRuntime<TJob>
    where TJob : struct, IRangeJob
{
    private int _rangeCount;

    internal RangeSystemNode(SystemId id, SystemDefinition definition, TSystem runtime, int participants)
        : base(id, definition, runtime, participants) { }

    internal override bool IsRangeWork => true;

    internal override SchedulerQueryShape GetShape() => new(_rangeCount, _rangeCount == 0 ? 0 : 1);

    internal override void Prepare(ulong tick, double deltaSeconds, bool manual, ChangeVersion candidate)
    {
        base.Prepare(tick, deltaSeconds, manual, candidate);
        _rangeCount = Runtime.GetRangeCount(new SystemPrepareContext(tick, deltaSeconds, Id, manual));
        if (_rangeCount < 0)
            throw new InvalidOperationException("A range system returned a negative range length.");
    }

    internal override int BuildPartitions(
        in SchedulerQueryShape shape,
        Span<SchedulerChunkPartition> destination,
        int desiredPartitions)
    {
        if (_rangeCount == 0)
            return 0;
        if (destination.IsEmpty)
            throw new InvalidOperationException("Scheduler work-item capacity is exhausted.");
        int count = Math.Min(Math.Min(desiredPartitions, destination.Length), _rangeCount);
        int baseCount = _rangeCount / count;
        int remainder = _rangeCount % count;
        int start = 0;
        for (int index = 0; index < count; index++)
        {
            int length = baseCount + (index < remainder ? 1 : 0);
            destination[index] = new SchedulerChunkPartition(-1, start, length, length);
            start += length;
        }
        return count;
    }

    internal override int ExecutePartition(int participant, in SchedulerChunkPartition partition,
        in SystemJobContext context)
    {
        ref TJob job = ref GetParticipantJob(participant);
        job.Execute(context, partition.FirstChunkIndex, partition.ChunkCount);
        return partition.ChunkCount;
    }
}
