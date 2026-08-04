namespace UltraSim.ECS;

/// <summary>
/// Fixed constraints used to plan one structure-of-arrays chunk.
/// </summary>
public readonly struct ChunkLayoutOptions : IEquatable<ChunkLayoutOptions>
{
    public const int DefaultReservedHeaderBytes = 256;
    public const int DefaultEntityIndexSizeBytes = sizeof(uint);
    public const int DefaultMinimumColumnAlignmentBytes = 64;
    public const int DefaultMaximumRows = ushort.MaxValue;

    public ChunkLayoutOptions(
        int chunkSizeBytes,
        int reservedHeaderBytes = DefaultReservedHeaderBytes,
        int entityIndexSizeBytes = DefaultEntityIndexSizeBytes,
        int minimumColumnAlignmentBytes = DefaultMinimumColumnAlignmentBytes,
        int maximumRows = DefaultMaximumRows)
    {
        if (chunkSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), chunkSizeBytes, "Chunk size must be positive.");
        if (reservedHeaderBytes < 0 || reservedHeaderBytes > chunkSizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedHeaderBytes),
                reservedHeaderBytes,
                "Reserved header bytes must be between zero and the total chunk size.");
        }
        if (!IsPowerOfTwo(entityIndexSizeBytes) || entityIndexSizeBytes > sizeof(ulong))
        {
            throw new ArgumentOutOfRangeException(
                nameof(entityIndexSizeBytes),
                entityIndexSizeBytes,
                "Entity index size must be 1, 2, 4, or 8 bytes.");
        }
        if (!IsPowerOfTwo(minimumColumnAlignmentBytes) || minimumColumnAlignmentBytes > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumColumnAlignmentBytes),
                minimumColumnAlignmentBytes,
                "Minimum column alignment must be a power of two between 1 and 4096 bytes.");
        }
        int minimumChunkAlignment = Math.Max(
            sizeof(ulong),
            Math.Max(entityIndexSizeBytes, minimumColumnAlignmentBytes));
        if ((chunkSizeBytes & (minimumChunkAlignment - 1)) != 0)
        {
            throw new ArgumentException(
                "Chunk size must preserve entity, bitset, and column alignment for consecutive arena chunks.",
                nameof(chunkSizeBytes));
        }
        if (maximumRows <= 0 || maximumRows > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRows),
                maximumRows,
                $"Maximum rows must be between 1 and {ushort.MaxValue}.");
        }

        ChunkSizeBytes = chunkSizeBytes;
        ReservedHeaderBytes = reservedHeaderBytes;
        EntityIndexSizeBytes = entityIndexSizeBytes;
        MinimumColumnAlignmentBytes = minimumColumnAlignmentBytes;
        MaximumRows = maximumRows;
    }

    public int ChunkSizeBytes { get; }

    public int ReservedHeaderBytes { get; }

    public int EntityIndexSizeBytes { get; }

    public int MinimumColumnAlignmentBytes { get; }

    public int MaximumRows { get; }

    public bool IsValid =>
        ChunkSizeBytes > 0 &&
        ReservedHeaderBytes >= 0 &&
        ReservedHeaderBytes <= ChunkSizeBytes &&
        IsPowerOfTwo(EntityIndexSizeBytes) &&
        EntityIndexSizeBytes <= sizeof(ulong) &&
        IsPowerOfTwo(MinimumColumnAlignmentBytes) &&
        MinimumColumnAlignmentBytes <= 4096 &&
        (ChunkSizeBytes & (Math.Max(sizeof(ulong), Math.Max(EntityIndexSizeBytes, MinimumColumnAlignmentBytes)) - 1)) == 0 &&
        MaximumRows > 0 &&
        MaximumRows <= ushort.MaxValue;

    public static ChunkLayoutOptions KiB16 => new(16 * 1024);

    public static ChunkLayoutOptions KiB32 => new(32 * 1024);

    public static ChunkLayoutOptions KiB64 => new(64 * 1024);

    public bool Equals(ChunkLayoutOptions other) =>
        ChunkSizeBytes == other.ChunkSizeBytes &&
        ReservedHeaderBytes == other.ReservedHeaderBytes &&
        EntityIndexSizeBytes == other.EntityIndexSizeBytes &&
        MinimumColumnAlignmentBytes == other.MinimumColumnAlignmentBytes &&
        MaximumRows == other.MaximumRows;

    public override bool Equals(object? obj) => obj is ChunkLayoutOptions other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        ChunkSizeBytes,
        ReservedHeaderBytes,
        EntityIndexSizeBytes,
        MinimumColumnAlignmentBytes,
        MaximumRows);

    public static bool operator ==(ChunkLayoutOptions left, ChunkLayoutOptions right) => left.Equals(right);

    public static bool operator !=(ChunkLayoutOptions left, ChunkLayoutOptions right) => !left.Equals(right);

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
