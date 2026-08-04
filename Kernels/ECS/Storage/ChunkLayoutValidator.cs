namespace UltraSim.ECS;

/// <summary>Cold defensive validation before a caller-provided layout becomes executable storage.</summary>
internal static class ChunkLayoutValidator
{
    internal static ComponentColumnLayout[] CopyAndValidate(
        in ChunkLayoutPlan layout,
        ReadOnlySpan<ComponentColumnLayout> columns)
    {
        if (!layout.IsValid)
            throw new ArgumentException("A valid chunk layout is required.", nameof(layout));
        if (layout.Options.EntityIndexSizeBytes != sizeof(uint))
            throw new ArgumentException("Executable archetype storage requires four-byte local entity indices.", nameof(layout));
        if (!IsPowerOfTwo(layout.RequiredChunkAlignmentBytes))
            throw new ArgumentException("The layout requires an invalid chunk alignment.", nameof(layout));
        if (columns.Length != layout.SignatureComponentCount)
        {
            throw new ArgumentException(
                "The physical column set must exactly match the layout signature count.",
                nameof(columns));
        }

        int expectedEntityBytes = checked(layout.RowCapacity * sizeof(uint));
        if (layout.EntityIndexDataBytes != expectedEntityBytes)
            throw new ArgumentException("Entity-index byte accounting does not match the row capacity.", nameof(layout));
        ValidateRange(
            layout.EntityIndexOffsetBytes,
            layout.EntityIndexDataBytes,
            layout.BytesUsed,
            nameof(layout));
        if (layout.EntityIndexOffsetBytes < layout.Options.ReservedHeaderBytes)
            throw new ArgumentException("The entity-index column overlaps the reserved chunk header.", nameof(layout));

        var copy = columns.ToArray();
        int dataColumnCount = 0;
        int tagCount = 0;
        int enableableCount = 0;
        int dataBytes = 0;
        int enableBytes = 0;
        int expectedEnableBytes = ChunkLayoutPlanner.CalculateEnableBitsetBytes(layout.RowCapacity, 1);

        for (int i = 0; i < copy.Length; i++)
        {
            ComponentColumnLayout column = copy[i];
            if (!column.ComponentId.IsValid)
                throw new ArgumentException("Physical columns cannot contain an invalid component ID.", nameof(columns));
            if (!IsPowerOfTwo(column.EffectiveAlignmentBytes))
                throw new ArgumentException("A physical column has invalid alignment.", nameof(columns));
            if (column.EffectiveAlignmentBytes > layout.RequiredChunkAlignmentBytes ||
                (layout.Options.ChunkSizeBytes & (column.EffectiveAlignmentBytes - 1)) != 0)
            {
                throw new ArgumentException("A physical column exceeds the layout's alignment contract.", nameof(columns));
            }
            bool isTag = (column.Flags & ComponentFlags.Tag) != 0;
            if (isTag == column.HasData)
                throw new ArgumentException("Tag flags and physical data storage disagree.", nameof(columns));

            for (int previous = 0; previous < i; previous++)
            {
                if (copy[previous].ComponentId == column.ComponentId)
                    throw new ArgumentException("Physical columns cannot contain duplicate component IDs.", nameof(columns));
            }

            if (column.HasData)
            {
                dataColumnCount++;
                int expectedDataBytes = checked(layout.RowCapacity * column.ElementSizeBytes);
                if (column.DataOffsetBytes == ComponentColumnLayout.NoOffset ||
                    column.DataBytes != expectedDataBytes ||
                    (column.DataOffsetBytes & (column.EffectiveAlignmentBytes - 1)) != 0)
                {
                    throw new ArgumentException("A physical data-column placement is inconsistent with the layout.", nameof(columns));
                }

                ValidateRange(column.DataOffsetBytes, column.DataBytes, layout.BytesUsed, nameof(columns));
                if (column.DataOffsetBytes < layout.Options.ReservedHeaderBytes)
                    throw new ArgumentException("A data column overlaps the reserved chunk header.", nameof(columns));
                dataBytes = checked(dataBytes + column.DataBytes);
            }
            else
            {
                tagCount++;
                if (column.DataOffsetBytes != ComponentColumnLayout.NoOffset || column.DataBytes != 0)
                    throw new ArgumentException("A tag component cannot reserve data-column bytes.", nameof(columns));
            }

            if (column.HasEnableBits)
            {
                enableableCount++;
                if (column.EnableBitsOffsetBytes == ComponentColumnLayout.NoOffset ||
                    column.EnableBitsBytes != expectedEnableBytes ||
                    (column.EnableBitsOffsetBytes & (sizeof(ulong) - 1)) != 0)
                {
                    throw new ArgumentException("An enable-bit placement is inconsistent with the layout.", nameof(columns));
                }

                ValidateRange(
                    column.EnableBitsOffsetBytes,
                    column.EnableBitsBytes,
                    layout.BytesUsed,
                    nameof(columns));
                if (column.EnableBitsOffsetBytes < layout.Options.ReservedHeaderBytes)
                    throw new ArgumentException("An enable bitset overlaps the reserved chunk header.", nameof(columns));
                enableBytes = checked(enableBytes + column.EnableBitsBytes);
            }
            else if (column.EnableBitsOffsetBytes != ComponentColumnLayout.NoOffset || column.EnableBitsBytes != 0)
            {
                throw new ArgumentException("A non-enableable component cannot reserve enable-bit bytes.", nameof(columns));
            }

            ValidateNoOverlap(layout, copy, i);
        }

        if (dataColumnCount != layout.DataColumnCount ||
            tagCount != layout.TagCount ||
            enableableCount != layout.EnableableCount ||
            dataBytes != layout.ComponentDataBytes ||
            enableBytes != layout.EnableBitsetBytes)
        {
            throw new ArgumentException("Physical column totals do not match the chunk layout.", nameof(columns));
        }

        return copy;
    }

    private static void ValidateNoOverlap(
        in ChunkLayoutPlan layout,
        ComponentColumnLayout[] columns,
        int index)
    {
        ComponentColumnLayout current = columns[index];
        if (current.HasData && RangesOverlap(
            current.DataOffsetBytes,
            current.DataBytes,
            layout.EntityIndexOffsetBytes,
            layout.EntityIndexDataBytes))
        {
            throw new ArgumentException("A data column overlaps the entity-index column.", nameof(columns));
        }
        if (current.HasEnableBits && RangesOverlap(
            current.EnableBitsOffsetBytes,
            current.EnableBitsBytes,
            layout.EntityIndexOffsetBytes,
            layout.EntityIndexDataBytes))
        {
            throw new ArgumentException("An enable bitset overlaps the entity-index column.", nameof(columns));
        }
        if (current.HasData && current.HasEnableBits && RangesOverlap(
            current.DataOffsetBytes,
            current.DataBytes,
            current.EnableBitsOffsetBytes,
            current.EnableBitsBytes))
        {
            throw new ArgumentException("A component data column overlaps its enable bitset.", nameof(columns));
        }

        for (int previousIndex = 0; previousIndex < index; previousIndex++)
        {
            ComponentColumnLayout previous = columns[previousIndex];
            if (current.HasData && previous.HasData && RangesOverlap(
                current.DataOffsetBytes,
                current.DataBytes,
                previous.DataOffsetBytes,
                previous.DataBytes))
            {
                throw new ArgumentException("Two component data columns overlap.", nameof(columns));
            }
            if (current.HasData && previous.HasEnableBits && RangesOverlap(
                current.DataOffsetBytes,
                current.DataBytes,
                previous.EnableBitsOffsetBytes,
                previous.EnableBitsBytes))
            {
                throw new ArgumentException("A data column overlaps another component's enable bitset.", nameof(columns));
            }
            if (current.HasEnableBits && previous.HasData && RangesOverlap(
                current.EnableBitsOffsetBytes,
                current.EnableBitsBytes,
                previous.DataOffsetBytes,
                previous.DataBytes))
            {
                throw new ArgumentException("An enable bitset overlaps another component's data column.", nameof(columns));
            }
            if (current.HasEnableBits && previous.HasEnableBits && RangesOverlap(
                current.EnableBitsOffsetBytes,
                current.EnableBitsBytes,
                previous.EnableBitsOffsetBytes,
                previous.EnableBitsBytes))
            {
                throw new ArgumentException("Two component enable bitsets overlap.", nameof(columns));
            }
        }
    }

    private static bool RangesOverlap(int leftOffset, int leftLength, int rightOffset, int rightLength) =>
        leftOffset < rightOffset + rightLength && rightOffset < leftOffset + leftLength;

    private static void ValidateRange(int offset, int length, int maximum, string parameterName)
    {
        if (offset < 0 || length < 0 || offset > maximum - length)
            throw new ArgumentOutOfRangeException(parameterName, "A planned chunk range exceeds the chunk boundary.");
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
