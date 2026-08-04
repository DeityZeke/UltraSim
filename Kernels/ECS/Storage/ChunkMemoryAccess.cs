using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Shared allocation-free typed access validation for concrete arena implementations.</summary>
internal static class ChunkMemoryAccess
{
    internal static Span<uint> GetEntityIndices(Span<byte> chunk, in ChunkLayoutPlan plan)
    {
        if (plan.Options.EntityIndexSizeBytes != sizeof(uint))
        {
            throw new InvalidOperationException(
                "Typed uint entity-index access requires a four-byte entity index layout.");
        }

        int expectedBytes = checked(plan.RowCapacity * sizeof(uint));
        if (plan.EntityIndexDataBytes != expectedBytes)
            throw new ArgumentException("Entity-index byte accounting does not match row capacity.", nameof(plan));
        ValidateRange(plan.EntityIndexOffsetBytes, plan.EntityIndexDataBytes, chunk.Length, "entity index");
        return MemoryMarshal.Cast<byte, uint>(
            chunk.Slice(plan.EntityIndexOffsetBytes, plan.EntityIndexDataBytes));
    }

    internal static Span<T> GetColumn<T>(
        Span<byte> chunk,
        in ChunkLayoutPlan plan,
        in ComponentColumnLayout column)
        where T : unmanaged
    {
        if (!column.HasData || column.DataOffsetBytes == ComponentColumnLayout.NoOffset)
            throw new ArgumentException("Tag components do not have data columns.", nameof(column));

        int elementSize = Unsafe.SizeOf<T>();
        if (elementSize != column.ElementSizeBytes)
        {
            throw new ArgumentException(
                $"Requested element size {elementSize} does not match planned size {column.ElementSizeBytes}.",
                nameof(column));
        }
        if ((column.DataOffsetBytes & (column.EffectiveAlignmentBytes - 1)) != 0)
            throw new ArgumentException("Planned data-column offset is misaligned.", nameof(column));

        int expectedBytes = checked(plan.RowCapacity * elementSize);
        if (column.DataBytes != expectedBytes)
            throw new ArgumentException("Data-column byte accounting does not match row capacity.", nameof(column));
        ValidateRange(column.DataOffsetBytes, column.DataBytes, chunk.Length, "component data");
        return MemoryMarshal.Cast<byte, T>(chunk.Slice(column.DataOffsetBytes, column.DataBytes));
    }

    internal static Span<ulong> GetEnableWords(
        Span<byte> chunk,
        in ChunkLayoutPlan plan,
        in ComponentColumnLayout column)
    {
        if (!column.HasEnableBits || column.EnableBitsOffsetBytes == ComponentColumnLayout.NoOffset)
            throw new ArgumentException("The component does not have an enable bitset.", nameof(column));
        if ((column.EnableBitsOffsetBytes & (sizeof(ulong) - 1)) != 0)
            throw new ArgumentException("Planned enable-bitset offset is misaligned.", nameof(column));

        int expectedBytes = ChunkLayoutPlanner.CalculateEnableBitsetBytes(plan.RowCapacity, 1);
        if (column.EnableBitsBytes != expectedBytes)
            throw new ArgumentException("Enable-bitset bytes do not match row capacity.", nameof(column));
        ValidateRange(column.EnableBitsOffsetBytes, column.EnableBitsBytes, chunk.Length, "enable bitset");
        return MemoryMarshal.Cast<byte, ulong>(
            chunk.Slice(column.EnableBitsOffsetBytes, column.EnableBitsBytes));
    }

    private static void ValidateRange(int offset, int length, int chunkLength, string name)
    {
        if (offset < 0 || length < 0 || offset > chunkLength - length)
        {
            throw new ArgumentOutOfRangeException(
                name,
                $"Planned {name} range [{offset}, {offset + (long)length}) exceeds chunk length {chunkLength}.");
        }
    }
}
