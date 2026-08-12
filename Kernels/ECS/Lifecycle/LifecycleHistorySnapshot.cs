namespace UltraSim.ECS;

/// <summary>Cold diagnostics for the fixed world-owned lifecycle ring.</summary>
public readonly struct LifecycleHistorySnapshot
{
    internal LifecycleHistorySnapshot(
        int capacity,
        int retainedCount,
        int highWatermark,
        ulong overwrittenCount,
        ulong oldestRetainedSequence,
        ulong nextPublishedSequence,
        long reservedBytes)
    {
        Capacity = capacity;
        RetainedCount = retainedCount;
        HighWatermark = highWatermark;
        OverwrittenCount = overwrittenCount;
        OldestRetainedSequence = oldestRetainedSequence;
        NextPublishedSequence = nextPublishedSequence;
        ReservedBytes = reservedBytes;
    }

    public int Capacity { get; }
    public int RetainedCount { get; }
    public int HighWatermark { get; }
    public ulong OverwrittenCount { get; }
    public ulong OldestRetainedSequence { get; }
    public ulong NextPublishedSequence { get; }
    public long ReservedBytes { get; }
}
