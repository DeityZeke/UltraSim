using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

[StructLayout(LayoutKind.Sequential)]
public readonly struct SystemRuntimeSnapshot
{
    internal SystemRuntimeSnapshot(
        SystemId id, string key, SystemStage stage, bool enabled, bool manual,
        int period, int offset, ulong lastRunTick, ulong runCount,
        double lastMicroseconds, double averageMicroseconds, double peakMicroseconds,
        SystemExecutionPolicy policy, int compiledWave, bool lastParallel, int rows, int chunks, int workItems)
    {
        Id = id; Key = key; Stage = stage; Enabled = enabled; Manual = manual;
        PeriodTicks = period; StartOffset = offset; LastRunTick = lastRunTick; RunCount = runCount;
        LastMicroseconds = lastMicroseconds; AverageMicroseconds = averageMicroseconds;
        PeakMicroseconds = peakMicroseconds; ExecutionPolicy = policy; CompiledWave = compiledWave;
        LastExecutionWasParallel = lastParallel;
        LastRows = rows; LastChunks = chunks; LastWorkItems = workItems;
    }

    public SystemId Id { get; }
    public string Key { get; }
    public SystemStage Stage { get; }
    public bool Enabled { get; }
    public bool Manual { get; }
    public int PeriodTicks { get; }
    public int StartOffset { get; }
    public ulong LastRunTick { get; }
    public ulong RunCount { get; }
    public double LastMicroseconds { get; }
    public double AverageMicroseconds { get; }
    public double PeakMicroseconds { get; }
    public SystemExecutionPolicy ExecutionPolicy { get; }
    public int CompiledWave { get; }
    public bool LastExecutionWasParallel { get; }
    public int LastRows { get; }
    public int LastChunks { get; }
    public int LastWorkItems { get; }
}

public readonly struct SchedulerRuntimeSnapshot
{
    internal SchedulerRuntimeSnapshot(int backgroundWorkers, bool callerParticipates,
        long dispatches, long workerWakes, long workerParks, long mergedSegments,
        long mergedCommands, long mergedPayloadBytes, long identityResolutions,
        long reservedPayloadBytes, long workerManagedAllocationBytes,
        long preBarrierTicks, long graphTicks, long structuralMergeTicks, long postBarrierTicks)
    {
        BackgroundWorkerCount = backgroundWorkers; CallerParticipates = callerParticipates;
        DispatchCount = dispatches; WorkerWakeCount = workerWakes; WorkerParkCount = workerParks;
        MergedSegmentCount = mergedSegments; MergedCommandCount = mergedCommands;
        MergedPayloadBytes = mergedPayloadBytes; FullEntityResolutionCount = identityResolutions;
        ReservedPayloadBytes = reservedPayloadBytes;
        WorkerManagedAllocationBytes = workerManagedAllocationBytes;
        PreBarrierTimestampTicks = preBarrierTicks;
        GraphTimestampTicks = graphTicks;
        StructuralMergeTimestampTicks = structuralMergeTicks;
        PostBarrierTimestampTicks = postBarrierTicks;
    }

    public int BackgroundWorkerCount { get; }
    public bool CallerParticipates { get; }
    public int ExecutionConcurrency => BackgroundWorkerCount + (CallerParticipates ? 1 : 0);
    public long DispatchCount { get; }
    public long WorkerWakeCount { get; }
    public long WorkerParkCount { get; }
    public long MergedSegmentCount { get; }
    public long MergedCommandCount { get; }
    public long MergedPayloadBytes { get; }
    public long FullEntityResolutionCount { get; }
    /// <summary>Fixed array/arena payload; excludes CLR headers and OS thread stacks.</summary>
    public long ReservedPayloadBytes { get; }
    public long WorkerManagedAllocationBytes { get; }
    /// <summary>Cumulative profiling time. Zero when scheduler profiling is disabled.</summary>
    public long PreBarrierTimestampTicks { get; }
    /// <summary>Cumulative profiling time spent executing the compiled system graph.</summary>
    public long GraphTimestampTicks { get; }
    /// <summary>Cumulative profiling time spent deterministically merging structural lanes.</summary>
    public long StructuralMergeTimestampTicks { get; }
    /// <summary>Cumulative profiling time spent applying the post-system structural barrier.</summary>
    public long PostBarrierTimestampTicks { get; }
    public double PreBarrierTotalMicroseconds => ToMicroseconds(PreBarrierTimestampTicks);
    public double GraphTotalMicroseconds => ToMicroseconds(GraphTimestampTicks);
    public double StructuralMergeTotalMicroseconds => ToMicroseconds(StructuralMergeTimestampTicks);
    public double PostBarrierTotalMicroseconds => ToMicroseconds(PostBarrierTimestampTicks);

    private static double ToMicroseconds(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
}

public sealed class SystemExecutionException : Exception
{
    internal SystemExecutionException(string message, Exception inner, SystemId systemId,
        string systemKey, SystemStage stage, int logicalWorkOrdinal, int participant)
        : base(message, inner)
    {
        SystemId = systemId; SystemKey = systemKey; Stage = stage;
        LogicalWorkOrdinal = logicalWorkOrdinal; Participant = participant;
    }

    public SystemId SystemId { get; }
    public string SystemKey { get; }
    public SystemStage Stage { get; }
    public int LogicalWorkOrdinal { get; }
    public int Participant { get; }
}
