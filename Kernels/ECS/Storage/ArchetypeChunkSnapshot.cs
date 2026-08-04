namespace UltraSim.ECS;

/// <summary>Cold occupancy and fragmentation facts for one fixed archetype store.</summary>
public readonly struct ArchetypeChunkSnapshot
{
    internal ArchetypeChunkSnapshot(
        int entityCount,
        int rowCapacity,
        int maximumChunks,
        int acquiredChunkCount,
        int fullChunkCount,
        int partialChunkCount,
        int emptyChunkCount,
        int chunkRecordSizeBytes,
        bool isDisposed)
    {
        EntityCount = entityCount;
        RowCapacity = rowCapacity;
        MaximumChunks = maximumChunks;
        AcquiredChunkCount = acquiredChunkCount;
        FullChunkCount = fullChunkCount;
        PartialChunkCount = partialChunkCount;
        EmptyChunkCount = emptyChunkCount;
        ChunkRecordSizeBytes = chunkRecordSizeBytes;
        IsDisposed = isDisposed;
    }

    public int EntityCount { get; }
    public int RowCapacity { get; }
    public int MaximumChunks { get; }
    public int AcquiredChunkCount { get; }
    public int FullChunkCount { get; }
    public int PartialChunkCount { get; }
    public int EmptyChunkCount { get; }
    public int ChunkRecordSizeBytes { get; }
    public long ChunkMetadataBytes => (long)MaximumChunks * ChunkRecordSizeBytes;
    public int UnallocatedChunkCount => MaximumChunks - AcquiredChunkCount;
    public int OccupiedChunkCount => FullChunkCount + PartialChunkCount;
    public long MaximumRows => (long)MaximumChunks * RowCapacity;
    public long AcquiredRows => (long)AcquiredChunkCount * RowCapacity;
    public long UnusedAcquiredRows => AcquiredRows - EntityCount;
    public bool IsDisposed { get; }
}
