using System.Threading;

namespace UltraSim.Registry;

public sealed class RegistryRuntimeMetrics
{
    private long _registerCalls;
    private long _heartbeatCalls;
    private long _presenceCalls;
    private long _metadataUpdateCalls;
    private long _deleteCalls;
    private long _leasePruneCalls;
    private long _leasePruneRecords;
    private long _pollRounds;
    private long _pollCandidates;
    private long _pollSuccesses;
    private long _pollFailures;
    private long _pollRoundDurationMsTotal;
    private long _lastPollRoundDurationMs;
    private long _lastPollRoundCompletedUtcTicks;
    private long _lifecycleSweeps;
    private long _lifecycleDelists;
    private long _lifecyclePurges;
    private long _lifecycleSweepDurationMsTotal;
    private long _lastLifecycleSweepDurationMs;
    private long _lastLifecycleSweepUtcTicks;
    private long _saveStateCalls;
    private long _saveStateFailures;
    private long _saveStateBytesTotal;
    private long _saveStateDurationMsTotal;
    private long _lastSaveStateBytes;
    private long _lastSaveStateDurationMs;
    private long _lastSaveStateUtcTicks;

    public void RecordRegister() => Interlocked.Increment(ref _registerCalls);
    public void RecordHeartbeat() => Interlocked.Increment(ref _heartbeatCalls);
    public void RecordPresence() => Interlocked.Increment(ref _presenceCalls);
    public void RecordMetadataUpdate() => Interlocked.Increment(ref _metadataUpdateCalls);
    public void RecordDelete() => Interlocked.Increment(ref _deleteCalls);

    public void RecordLeasePrune(int prunedCount)
    {
        Interlocked.Increment(ref _leasePruneCalls);
        Interlocked.Add(ref _leasePruneRecords, prunedCount);
    }

    public void RecordPollRound(int candidateCount, int successCount, int failureCount, long durationMs, DateTimeOffset completedUtc)
    {
        Interlocked.Increment(ref _pollRounds);
        Interlocked.Add(ref _pollCandidates, candidateCount);
        Interlocked.Add(ref _pollSuccesses, successCount);
        Interlocked.Add(ref _pollFailures, failureCount);
        Interlocked.Add(ref _pollRoundDurationMsTotal, durationMs);
        Interlocked.Exchange(ref _lastPollRoundDurationMs, durationMs);
        Interlocked.Exchange(ref _lastPollRoundCompletedUtcTicks, completedUtc.UtcTicks);
    }

    public void RecordLifecycleSweep(int delistedCount, int purgedCount, long durationMs, DateTimeOffset completedUtc)
    {
        Interlocked.Increment(ref _lifecycleSweeps);
        Interlocked.Add(ref _lifecycleDelists, delistedCount);
        Interlocked.Add(ref _lifecyclePurges, purgedCount);
        Interlocked.Add(ref _lifecycleSweepDurationMsTotal, durationMs);
        Interlocked.Exchange(ref _lastLifecycleSweepDurationMs, durationMs);
        Interlocked.Exchange(ref _lastLifecycleSweepUtcTicks, completedUtc.UtcTicks);
    }

    public void RecordSaveState(long durationMs, int bytesWritten, DateTimeOffset completedUtc)
    {
        Interlocked.Increment(ref _saveStateCalls);
        Interlocked.Add(ref _saveStateBytesTotal, bytesWritten);
        Interlocked.Add(ref _saveStateDurationMsTotal, durationMs);
        Interlocked.Exchange(ref _lastSaveStateBytes, bytesWritten);
        Interlocked.Exchange(ref _lastSaveStateDurationMs, durationMs);
        Interlocked.Exchange(ref _lastSaveStateUtcTicks, completedUtc.UtcTicks);
    }

    public void RecordSaveStateFailure()
    {
        Interlocked.Increment(ref _saveStateFailures);
    }

    public RegistryRuntimeMetricsSnapshot GetSnapshot()
    {
        return new RegistryRuntimeMetricsSnapshot(
            RegisterCalls: Interlocked.Read(ref _registerCalls),
            HeartbeatCalls: Interlocked.Read(ref _heartbeatCalls),
            PresenceCalls: Interlocked.Read(ref _presenceCalls),
            MetadataUpdateCalls: Interlocked.Read(ref _metadataUpdateCalls),
            DeleteCalls: Interlocked.Read(ref _deleteCalls),
            LeasePruneCalls: Interlocked.Read(ref _leasePruneCalls),
            LeasePrunedRecords: Interlocked.Read(ref _leasePruneRecords),
            PollRounds: Interlocked.Read(ref _pollRounds),
            PollCandidates: Interlocked.Read(ref _pollCandidates),
            PollSuccesses: Interlocked.Read(ref _pollSuccesses),
            PollFailures: Interlocked.Read(ref _pollFailures),
            PollRoundDurationMsTotal: Interlocked.Read(ref _pollRoundDurationMsTotal),
            LastPollRoundDurationMs: Interlocked.Read(ref _lastPollRoundDurationMs),
            LastPollRoundCompletedUtc: ReadTimestamp(_lastPollRoundCompletedUtcTicks),
            LifecycleSweeps: Interlocked.Read(ref _lifecycleSweeps),
            LifecycleDelists: Interlocked.Read(ref _lifecycleDelists),
            LifecyclePurges: Interlocked.Read(ref _lifecyclePurges),
            LifecycleSweepDurationMsTotal: Interlocked.Read(ref _lifecycleSweepDurationMsTotal),
            LastLifecycleSweepDurationMs: Interlocked.Read(ref _lastLifecycleSweepDurationMs),
            LastLifecycleSweepUtc: ReadTimestamp(_lastLifecycleSweepUtcTicks),
            SaveStateCalls: Interlocked.Read(ref _saveStateCalls),
            SaveStateFailures: Interlocked.Read(ref _saveStateFailures),
            SaveStateBytesTotal: Interlocked.Read(ref _saveStateBytesTotal),
            SaveStateDurationMsTotal: Interlocked.Read(ref _saveStateDurationMsTotal),
            LastSaveStateBytes: Interlocked.Read(ref _lastSaveStateBytes),
            LastSaveStateDurationMs: Interlocked.Read(ref _lastSaveStateDurationMs),
            LastSaveStateUtc: ReadTimestamp(_lastSaveStateUtcTicks));
    }

    private static DateTimeOffset? ReadTimestamp(long utcTicks)
    {
        return utcTicks <= 0
            ? null
            : new DateTimeOffset(utcTicks, TimeSpan.Zero);
    }
}

public sealed record RegistryRuntimeMetricsSnapshot(
    long RegisterCalls,
    long HeartbeatCalls,
    long PresenceCalls,
    long MetadataUpdateCalls,
    long DeleteCalls,
    long LeasePruneCalls,
    long LeasePrunedRecords,
    long PollRounds,
    long PollCandidates,
    long PollSuccesses,
    long PollFailures,
    long PollRoundDurationMsTotal,
    long LastPollRoundDurationMs,
    DateTimeOffset? LastPollRoundCompletedUtc,
    long LifecycleSweeps,
    long LifecycleDelists,
    long LifecyclePurges,
    long LifecycleSweepDurationMsTotal,
    long LastLifecycleSweepDurationMs,
    DateTimeOffset? LastLifecycleSweepUtc,
    long SaveStateCalls,
    long SaveStateFailures,
    long SaveStateBytesTotal,
    long SaveStateDurationMsTotal,
    long LastSaveStateBytes,
    long LastSaveStateDurationMs,
    DateTimeOffset? LastSaveStateUtc);

public sealed record RegistryStoreRuntimeSnapshot(
    int TotalRecords,
    int ActiveRecords,
    int DelistedRecords,
    int DisabledRecords,
    int OnlineRecords,
    int StaleRecords,
    int OfflineRecords,
    int PendingRecords,
    int ActiveLeases,
    int PollCandidates,
    int VisibleRows,
    bool PersistenceEnabled,
    bool PendingPersistence);

public sealed record RegistryAggregateMetricsSnapshot(
    int CurrentPlayersTotal,
    int TotalVotes,
    int ServersWithHistory,
    int HistoryBucketCount,
    DateTimeOffset? HistoryWindowStartUtc,
    DateTimeOffset? HistoryWindowEndUtc,
    double AveragePlayersRetained,
    int PeakPlayersRetained,
    double OnlineRatioRetained,
    double OfflineRatioRetained,
    int ServersWithLatency,
    double AverageLatencyMs,
    int MaxLatencyMs,
    int PeakPlayers24Hours,
    double AveragePlayers7Days,
    double AveragePlayers30Days,
    double OnlineRatio7Days,
    double OnlineRatio30Days,
    int AllTimePeakPlayers,
    DateTimeOffset? AllTimePeakPlayersUtc,
    IReadOnlyList<RegistryAggregateHistoryPoint>? RecentAggregatePoints = null);

public sealed record RegistryAggregateHistoryPoint(
    DateTimeOffset CapturedAtUtc,
    int CurrentPlayersTotal,
    double OnlineRatio = 0,
    double OfflineRatio = 0,
    int SampleCount = 0);

public sealed record RegistryRuntimeSnapshot(
    DateTimeOffset CapturedAtUtc,
    RegistryStoreRuntimeSnapshot Store,
    RegistryRuntimeMetricsSnapshot Metrics,
    RegistryAggregateMetricsSnapshot Aggregates);
