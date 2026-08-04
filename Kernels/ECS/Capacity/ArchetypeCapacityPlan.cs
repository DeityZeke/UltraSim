namespace UltraSim.ECS;

/// <summary>
/// Population and memory accounting for one archetype using a fixed chunk layout.
/// </summary>
public readonly struct ArchetypeCapacityPlan : IEquatable<ArchetypeCapacityPlan>
{
    public ArchetypeCapacityPlan(ChunkLayoutPlan layout, int expectedPopulation)
    {
        if (!layout.IsValid)
            throw new ArgumentException("A valid chunk layout is required.", nameof(layout));
        if (expectedPopulation < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPopulation));

        checked
        {
            Layout = layout;
            ExpectedPopulation = expectedPopulation;
            RequiredChunkCount = expectedPopulation == 0
                ? 0
                : ((expectedPopulation - 1) / layout.RowCapacity) + 1;
            ReservedRows = (long)RequiredChunkCount * layout.RowCapacity;
            UnusedRowSlots = ReservedRows - expectedPopulation;
            ReservedChunkBytes = (long)RequiredChunkCount * layout.Options.ChunkSizeBytes;
            ReservedHeaderBytes = (long)RequiredChunkCount * layout.Options.ReservedHeaderBytes;
            ReservedFixedRowBytes = ReservedRows * layout.FixedRowBytes;
            OccupiedFixedRowBytes = (long)expectedPopulation * layout.FixedRowBytes;
            PartialChunkUnusedRowBytes = UnusedRowSlots * layout.FixedRowBytes;
            ReservedEnableBitsetBytes = (long)RequiredChunkCount * layout.EnableBitsetBytes;
            long reservedEnableBits = ReservedEnableBitsetBytes * 8;
            long occupiedEnableBits = (long)expectedPopulation * layout.EnableableCount;
            UnusedEnableBits = reservedEnableBits - occupiedEnableBits;
            AlignmentPaddingBytes = (long)RequiredChunkCount * layout.AlignmentPaddingBytes;
            TailUnusedBytes = (long)RequiredChunkCount * layout.TailUnusedBytes;
        }
    }

    public ChunkLayoutPlan Layout { get; }

    public int ExpectedPopulation { get; }

    public int RequiredChunkCount { get; }

    public long ReservedRows { get; }

    public long UnusedRowSlots { get; }

    public long ReservedChunkBytes { get; }

    public long ReservedHeaderBytes { get; }

    public long ReservedFixedRowBytes { get; }

    public long OccupiedFixedRowBytes { get; }

    public long PartialChunkUnusedRowBytes { get; }

    public long ReservedEnableBitsetBytes { get; }

    /// <summary>
    /// Unused bits in word-rounded per-component bitsets, including empty row slots and
    /// the tail bits in the final 64-bit word of every allocated chunk.
    /// </summary>
    public long UnusedEnableBits { get; }

    public long AlignmentPaddingBytes { get; }

    public long TailUnusedBytes { get; }

    public bool Equals(ArchetypeCapacityPlan other) =>
        Layout.Equals(other.Layout) && ExpectedPopulation == other.ExpectedPopulation;

    public override bool Equals(object? obj) => obj is ArchetypeCapacityPlan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Layout, ExpectedPopulation);
}
