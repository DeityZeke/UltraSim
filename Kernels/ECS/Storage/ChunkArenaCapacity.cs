namespace UltraSim.ECS;

/// <summary>Fixed geometry and checked payload reservation for a chunk arena.</summary>
public readonly struct ChunkArenaCapacity : IEquatable<ChunkArenaCapacity>
{
    public ChunkArenaCapacity(int chunkSizeBytes, int chunkAlignmentBytes, int maximumChunks)
    {
        if (chunkSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes));
        if (!IsPowerOfTwo(chunkAlignmentBytes) || chunkAlignmentBytes > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkAlignmentBytes),
                chunkAlignmentBytes,
                "Chunk alignment must be a power of two between 1 and 4096 bytes.");
        }
        if (chunkSizeBytes < chunkAlignmentBytes || (chunkSizeBytes & (chunkAlignmentBytes - 1)) != 0)
        {
            throw new ArgumentException(
                "Chunk size must be at least one alignment unit and an exact multiple of alignment.",
                nameof(chunkSizeBytes));
        }
        if (maximumChunks <= 0 || maximumChunks > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumChunks),
                maximumChunks,
                $"Maximum chunks must be between 1 and {Array.MaxLength}.");
        }

        ChunkSizeBytes = chunkSizeBytes;
        ChunkAlignmentBytes = chunkAlignmentBytes;
        MaximumChunks = maximumChunks;
        TotalPayloadBytes = checked((long)chunkSizeBytes * maximumChunks);
    }

    public int ChunkSizeBytes { get; }

    public int ChunkAlignmentBytes { get; }

    public int MaximumChunks { get; }

    public long TotalPayloadBytes { get; }

    public long EstimatedSlotMetadataBytes => checked((long)MaximumChunks * 9);

    public bool IsValid =>
        ChunkSizeBytes > 0 &&
        IsPowerOfTwo(ChunkAlignmentBytes) &&
        ChunkAlignmentBytes <= 4096 &&
        ChunkSizeBytes >= ChunkAlignmentBytes &&
        (ChunkSizeBytes & (ChunkAlignmentBytes - 1)) == 0 &&
        MaximumChunks > 0 &&
        MaximumChunks <= Array.MaxLength &&
        TotalPayloadBytes == (long)ChunkSizeBytes * MaximumChunks;

    public static ChunkArenaCapacity FromLayout(ChunkLayoutPlan layout, int maximumChunks)
    {
        if (!layout.IsValid)
            throw new ArgumentException("A valid chunk layout is required.", nameof(layout));
        return new ChunkArenaCapacity(
            layout.Options.ChunkSizeBytes,
            layout.RequiredChunkAlignmentBytes,
            maximumChunks);
    }

    public bool Supports(ChunkLayoutPlan layout) =>
        layout.IsValid &&
        layout.Options.ChunkSizeBytes == ChunkSizeBytes &&
        layout.RequiredChunkAlignmentBytes <= ChunkAlignmentBytes;

    public bool Equals(ChunkArenaCapacity other) =>
        ChunkSizeBytes == other.ChunkSizeBytes &&
        ChunkAlignmentBytes == other.ChunkAlignmentBytes &&
        MaximumChunks == other.MaximumChunks;

    public override bool Equals(object? obj) => obj is ChunkArenaCapacity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ChunkSizeBytes, ChunkAlignmentBytes, MaximumChunks);

    public static bool operator ==(ChunkArenaCapacity left, ChunkArenaCapacity right) => left.Equals(right);

    public static bool operator !=(ChunkArenaCapacity left, ChunkArenaCapacity right) => !left.Equals(right);

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
