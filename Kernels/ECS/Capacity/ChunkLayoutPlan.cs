namespace UltraSim.ECS;

/// <summary>
/// Exact fixed-capacity memory accounting for one planned chunk.
/// </summary>
public readonly struct ChunkLayoutPlan : IEquatable<ChunkLayoutPlan>
{
    internal ChunkLayoutPlan(
        ChunkLayoutOptions options,
        int rowCapacity,
        int signatureComponentCount,
        int dataColumnCount,
        int tagCount,
        int enableableCount,
        int requiredChunkAlignmentBytes,
        int entityIndexOffsetBytes,
        int entityIndexDataBytes,
        int componentDataBytes,
        int enableBitsetBytes,
        int alignmentPaddingBytes,
        int tailUnusedBytes,
        int bytesUsed)
    {
        Options = options;
        RowCapacity = rowCapacity;
        SignatureComponentCount = signatureComponentCount;
        DataColumnCount = dataColumnCount;
        TagCount = tagCount;
        EnableableCount = enableableCount;
        RequiredChunkAlignmentBytes = requiredChunkAlignmentBytes;
        EntityIndexOffsetBytes = entityIndexOffsetBytes;
        EntityIndexDataBytes = entityIndexDataBytes;
        ComponentDataBytes = componentDataBytes;
        EnableBitsetBytes = enableBitsetBytes;
        AlignmentPaddingBytes = alignmentPaddingBytes;
        TailUnusedBytes = tailUnusedBytes;
        BytesUsed = bytesUsed;
    }

    public ChunkLayoutOptions Options { get; }

    public int RowCapacity { get; }

    public int SignatureComponentCount { get; }

    public int DataColumnCount { get; }

    public int TagCount { get; }

    public int EnableableCount { get; }

    /// <summary>Required alignment of each chunk base in its backing arena.</summary>
    public int RequiredChunkAlignmentBytes { get; }

    public int EntityIndexOffsetBytes { get; }

    public int EntityIndexDataBytes { get; }

    public int ComponentDataBytes { get; }

    public int EnableBitsetBytes { get; }

    public int AlignmentPaddingBytes { get; }

    /// <summary>Bytes after the final planned bitset through the fixed chunk boundary.</summary>
    public int TailUnusedBytes { get; }

    public int BytesUsed { get; }

    public int FixedRowBytes => checked(Options.EntityIndexSizeBytes + (ComponentDataBytes / RowCapacity));

    public int StructuralWasteBytes => checked(AlignmentPaddingBytes + TailUnusedBytes);

    public bool IsValid => RowCapacity > 0 && BytesUsed <= Options.ChunkSizeBytes;

    public bool Equals(ChunkLayoutPlan other) =>
        Options == other.Options &&
        RowCapacity == other.RowCapacity &&
        SignatureComponentCount == other.SignatureComponentCount &&
        DataColumnCount == other.DataColumnCount &&
        TagCount == other.TagCount &&
        EnableableCount == other.EnableableCount &&
        RequiredChunkAlignmentBytes == other.RequiredChunkAlignmentBytes &&
        EntityIndexOffsetBytes == other.EntityIndexOffsetBytes &&
        EntityIndexDataBytes == other.EntityIndexDataBytes &&
        ComponentDataBytes == other.ComponentDataBytes &&
        EnableBitsetBytes == other.EnableBitsetBytes &&
        AlignmentPaddingBytes == other.AlignmentPaddingBytes &&
        TailUnusedBytes == other.TailUnusedBytes &&
        BytesUsed == other.BytesUsed;

    public override bool Equals(object? obj) => obj is ChunkLayoutPlan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        Options,
        RowCapacity,
        SignatureComponentCount,
        DataColumnCount,
        TagCount,
        EnableableCount,
        BytesUsed,
        TailUnusedBytes);
}
