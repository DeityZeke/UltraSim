namespace UltraSim.ECS;

/// <summary>
/// Allocation-free deterministic planner for fixed-size structure-of-arrays chunks.
/// </summary>
public static class ChunkLayoutPlanner
{
    private const int EnableWordBytes = sizeof(ulong);
    private const int EnableWordBits = EnableWordBytes * 8;

    internal static int CalculateEnableBitsetBytes(int rows, int enableableComponentCount)
    {
        if (rows < 0)
            throw new ArgumentOutOfRangeException(nameof(rows));
        if (enableableComponentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(enableableComponentCount));

        checked
        {
            int words = (rows + (EnableWordBits - 1)) / EnableWordBits;
            return words * EnableWordBytes * enableableComponentCount;
        }
    }

    public static ChunkLayoutPlan Plan(
        ComponentSchema schema,
        ReadOnlySpan<ComponentId> sortedSignature,
        ChunkLayoutOptions options,
        Span<ComponentColumnLayout> columns)
    {
        if (!TryPlan(schema, sortedSignature, options, columns, out ChunkLayoutPlan plan))
        {
            throw new InvalidOperationException(
                "The configured chunk cannot hold one row of the requested archetype signature.");
        }

        return plan;
    }

    public static bool TryPlan(
        ComponentSchema schema,
        ReadOnlySpan<ComponentId> sortedSignature,
        ChunkLayoutOptions options,
        Span<ComponentColumnLayout> columns,
        out ChunkLayoutPlan plan)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!options.IsValid)
            throw new ArgumentException("Chunk layout options are invalid or default initialized.", nameof(options));
        if (columns.Length < sortedSignature.Length)
        {
            throw new ArgumentException(
                "The output span must contain at least one entry per signature component.",
                nameof(columns));
        }

        Span<ComponentColumnLayout> physicalColumns = columns[..sortedSignature.Length];
        InitializeAndValidateColumns(schema, sortedSignature, options, physicalColumns);
        SortPhysicalColumns(physicalColumns);

        long fixedRowBytes = options.EntityIndexSizeBytes;
        for (int i = 0; i < physicalColumns.Length; i++)
            fixedRowBytes = checked(fixedRowBytes + physicalColumns[i].ElementSizeBytes);

        int low = 1;
        int high = (int)Math.Min(options.MaximumRows, options.ChunkSizeBytes / fixedRowBytes);
        int best = 0;
        while (low <= high)
        {
            int candidate = low + ((high - low) >> 1);
            if (TryCalculateFootprint(options, physicalColumns, candidate, writePlacement: false, out Footprint footprint) &&
                footprint.BytesUsed <= options.ChunkSizeBytes)
            {
                best = candidate;
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }

        if (best == 0 ||
            !TryCalculateFootprint(options, physicalColumns, best, writePlacement: true, out Footprint selected))
        {
            plan = default;
            return false;
        }

        int dataColumnCount = 0;
        int tagCount = 0;
        int enableableCount = 0;
        int requiredChunkAlignment = Math.Max(
            EnableWordBytes,
            Math.Max(options.EntityIndexSizeBytes, options.MinimumColumnAlignmentBytes));
        for (int i = 0; i < physicalColumns.Length; i++)
        {
            if (physicalColumns[i].HasData)
                dataColumnCount++;
            else
                tagCount++;
            if (physicalColumns[i].HasEnableBits)
                enableableCount++;
            requiredChunkAlignment = Math.Max(
                requiredChunkAlignment,
                physicalColumns[i].EffectiveAlignmentBytes);
        }

        plan = new ChunkLayoutPlan(
            options,
            best,
            physicalColumns.Length,
            dataColumnCount,
            tagCount,
            enableableCount,
            requiredChunkAlignment,
            selected.EntityIndexOffsetBytes,
            selected.EntityIndexDataBytes,
            selected.ComponentDataBytes,
            selected.EnableBitsetBytes,
            selected.AlignmentPaddingBytes,
            checked(options.ChunkSizeBytes - selected.BytesUsed),
            selected.BytesUsed);
        return true;
    }

    internal static long CalculateRequiredBytes(
        ChunkLayoutOptions options,
        Span<ComponentColumnLayout> physicalColumns,
        int rows)
    {
        if (!options.IsValid)
            throw new ArgumentException("Chunk layout options are invalid or default initialized.", nameof(options));
        if (rows <= 0 || rows > options.MaximumRows)
            throw new ArgumentOutOfRangeException(nameof(rows));

        return TryCalculateFootprint(options, physicalColumns, rows, writePlacement: false, out Footprint footprint)
            ? footprint.BytesUsed
            : long.MaxValue;
    }

    private static void InitializeAndValidateColumns(
        ComponentSchema schema,
        ReadOnlySpan<ComponentId> sortedSignature,
        ChunkLayoutOptions options,
        Span<ComponentColumnLayout> columns)
    {
        ComponentId previous = ComponentId.Invalid;
        for (int i = 0; i < sortedSignature.Length; i++)
        {
            ComponentId componentId = sortedSignature[i];
            if (!componentId.IsValid)
                throw new ArgumentException("An archetype signature cannot contain an invalid component ID.", nameof(sortedSignature));
            if (i != 0 && componentId.Value <= previous.Value)
            {
                throw new ArgumentException(
                    "Archetype signatures must contain unique component IDs in ascending order.",
                    nameof(sortedSignature));
            }

            ref readonly ComponentMetadata metadata = ref schema.GetMetadata(componentId);
            int effectiveAlignment = Math.Max(options.MinimumColumnAlignmentBytes, metadata.Alignment);
            if ((options.ChunkSizeBytes & (effectiveAlignment - 1)) != 0)
            {
                throw new ArgumentException(
                    $"Chunk size must be a multiple of component {componentId} effective alignment {effectiveAlignment}.",
                    nameof(options));
            }
            columns[i] = new ComponentColumnLayout(
                componentId,
                metadata.Flags,
                metadata.Size,
                effectiveAlignment);
            previous = componentId;
        }
    }

    private static void SortPhysicalColumns(Span<ComponentColumnLayout> columns)
    {
        if (columns.Length > 16)
        {
            HeapSortPhysicalColumns(columns);
            return;
        }

        for (int i = 1; i < columns.Length; i++)
        {
            ComponentColumnLayout value = columns[i];
            int destination = i;
            while (destination > 0 && ComparePhysicalOrder(value, columns[destination - 1]) < 0)
            {
                columns[destination] = columns[destination - 1];
                destination--;
            }

            columns[destination] = value;
        }
    }

    private static void HeapSortPhysicalColumns(Span<ComponentColumnLayout> columns)
    {
        for (int root = (columns.Length >> 1) - 1; root >= 0; root--)
            SiftDown(columns, root, columns.Length);

        for (int end = columns.Length - 1; end > 0; end--)
        {
            (columns[0], columns[end]) = (columns[end], columns[0]);
            SiftDown(columns, 0, end);
        }
    }

    private static void SiftDown(Span<ComponentColumnLayout> columns, int root, int length)
    {
        while (true)
        {
            int child = checked((root << 1) + 1);
            if (child >= length)
                return;
            if (child + 1 < length && ComparePhysicalOrder(columns[child], columns[child + 1]) < 0)
                child++;
            if (ComparePhysicalOrder(columns[root], columns[child]) >= 0)
                return;

            (columns[root], columns[child]) = (columns[child], columns[root]);
            root = child;
        }
    }

    private static int ComparePhysicalOrder(ComponentColumnLayout left, ComponentColumnLayout right)
    {
        int dataOrder = right.HasData.CompareTo(left.HasData);
        if (dataOrder != 0)
            return dataOrder;

        int alignmentOrder = right.EffectiveAlignmentBytes.CompareTo(left.EffectiveAlignmentBytes);
        if (alignmentOrder != 0)
            return alignmentOrder;

        int sizeOrder = right.ElementSizeBytes.CompareTo(left.ElementSizeBytes);
        return sizeOrder != 0 ? sizeOrder : left.ComponentId.CompareTo(right.ComponentId);
    }

    private static bool TryCalculateFootprint(
        ChunkLayoutOptions options,
        Span<ComponentColumnLayout> physicalColumns,
        int rows,
        bool writePlacement,
        out Footprint footprint)
    {
        try
        {
            checked
            {
                long cursor = options.ReservedHeaderBytes;
                long alignmentPadding = 0;
                int entityAlignment = Math.Max(
                    options.MinimumColumnAlignmentBytes,
                    options.EntityIndexSizeBytes);
                Align(ref cursor, entityAlignment, ref alignmentPadding);
                int entityOffset = ToInt32(cursor);
                int entityBytes = ToInt32((long)rows * options.EntityIndexSizeBytes);
                cursor += entityBytes;

                int componentDataBytes = 0;
                for (int i = 0; i < physicalColumns.Length; i++)
                {
                    ComponentColumnLayout column = physicalColumns[i];
                    if (!column.HasData)
                        continue;

                    Align(ref cursor, column.EffectiveAlignmentBytes, ref alignmentPadding);
                    int dataOffset = ToInt32(cursor);
                    int dataBytes = ToInt32((long)rows * column.ElementSizeBytes);
                    cursor += dataBytes;
                    componentDataBytes = checked(componentDataBytes + dataBytes);
                    if (writePlacement)
                    {
                        physicalColumns[i] = column.WithPlacement(
                            dataOffset,
                            dataBytes,
                            ComponentColumnLayout.NoOffset,
                            0);
                    }
                }

                int enableBytesPerComponent = CalculateEnableBitsetBytes(rows, 1);
                int enableBitsetBytes = 0;
                for (int i = 0; i < physicalColumns.Length; i++)
                {
                    ComponentColumnLayout column = physicalColumns[i];
                    if (!column.HasEnableBits)
                        continue;

                    Align(ref cursor, EnableWordBytes, ref alignmentPadding);
                    int enableOffset = ToInt32(cursor);
                    cursor += enableBytesPerComponent;
                    enableBitsetBytes = checked(enableBitsetBytes + enableBytesPerComponent);
                    if (writePlacement)
                    {
                        physicalColumns[i] = column.WithPlacement(
                            column.DataOffsetBytes,
                            column.DataBytes,
                            enableOffset,
                            enableBytesPerComponent);
                    }
                }

                footprint = new Footprint(
                    entityOffset,
                    entityBytes,
                    componentDataBytes,
                    enableBitsetBytes,
                    ToInt32(alignmentPadding),
                    ToInt32(cursor));
                return true;
            }
        }
        catch (OverflowException)
        {
            footprint = default;
            return false;
        }
    }

    private static void Align(ref long cursor, int alignment, ref long alignmentPadding)
    {
        long mask = alignment - 1L;
        long aligned = checked((cursor + mask) & ~mask);
        alignmentPadding = checked(alignmentPadding + (aligned - cursor));
        cursor = aligned;
    }

    private static int ToInt32(long value)
    {
        if ((ulong)value > int.MaxValue)
            throw new OverflowException("A planned chunk offset exceeds the supported 32-bit range.");
        return (int)value;
    }

    private readonly struct Footprint
    {
        internal Footprint(
            int entityIndexOffsetBytes,
            int entityIndexDataBytes,
            int componentDataBytes,
            int enableBitsetBytes,
            int alignmentPaddingBytes,
            int bytesUsed)
        {
            EntityIndexOffsetBytes = entityIndexOffsetBytes;
            EntityIndexDataBytes = entityIndexDataBytes;
            ComponentDataBytes = componentDataBytes;
            EnableBitsetBytes = enableBitsetBytes;
            AlignmentPaddingBytes = alignmentPaddingBytes;
            BytesUsed = bytesUsed;
        }

        internal int EntityIndexOffsetBytes { get; }
        internal int EntityIndexDataBytes { get; }
        internal int ComponentDataBytes { get; }
        internal int EnableBitsetBytes { get; }
        internal int AlignmentPaddingBytes { get; }
        internal int BytesUsed { get; }
    }
}
