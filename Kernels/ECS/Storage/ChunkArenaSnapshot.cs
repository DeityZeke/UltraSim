namespace UltraSim.ECS;

/// <summary>Cold observational counters for one fixed chunk arena.</summary>
public readonly struct ChunkArenaSnapshot
{
    internal ChunkArenaSnapshot(
        ChunkArenaId arenaId,
        ChunkArenaMemoryKind memoryKind,
        ChunkArenaCapacity capacity,
        int aliveCount,
        int retiredCount,
        int highWatermark,
        bool isDisposed,
        long payloadBytesInUse,
        long releasedPayloadBytes)
    {
        ArenaId = arenaId;
        MemoryKind = memoryKind;
        Capacity = capacity;
        AliveCount = aliveCount;
        RetiredCount = retiredCount;
        HighWatermark = highWatermark;
        IsDisposed = isDisposed;
        PayloadBytesInUse = payloadBytesInUse;
        ReleasedPayloadBytes = releasedPayloadBytes;
    }

    public ChunkArenaId ArenaId { get; }
    public ChunkArenaMemoryKind MemoryKind { get; }
    public ChunkArenaCapacity Capacity { get; }
    public int AliveCount { get; }
    public int RetiredCount { get; }
    public int HighWatermark { get; }
    public int AvailableCount => IsDisposed ? 0 : Capacity.MaximumChunks - AliveCount - RetiredCount;
    public bool IsDisposed { get; }
    public long PayloadBytesInUse { get; }
    public long ReleasedPayloadBytes { get; }
}
