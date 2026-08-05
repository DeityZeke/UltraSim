using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Cold-compiled physical route for one archetype matched by a query.</summary>
internal readonly struct ArchetypeReadWriteQueryRoute<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal ArchetypeReadWriteQueryRoute(
        ArchetypeStorage<TArena> storage,
        int readColumnIndex,
        int writeColumnIndex,
        ReadOnlySpan<ComponentId> enabledComponents,
        ReadOnlySpan<ComponentId> disabledComponents)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if ((uint)readColumnIndex >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(readColumnIndex));
        if ((uint)writeColumnIndex >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(writeColumnIndex));
        if (readColumnIndex == writeColumnIndex)
            throw new ArgumentException("Read and write query columns must not alias.");

        Storage = storage;
        ReadColumnIndex = (ushort)readColumnIndex;
        WriteColumnIndex = (ushort)writeColumnIndex;
        EnabledBitOffsetsBytes = BindEnableOffsets(storage, enabledComponents);
        DisabledBitOffsetsBytes = BindEnableOffsets(storage, disabledComponents);
    }

    internal ArchetypeStorage<TArena> Storage { get; }

    internal int ReadColumnIndex { get; }

    internal int WriteColumnIndex { get; }

    private int[] EnabledBitOffsetsBytes { get; }

    private int[] DisabledBitOffsetsBytes { get; }

    internal bool HasRowFilters =>
        EnabledBitOffsetsBytes.Length != 0 || DisabledBitOffsetsBytes.Length != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ulong GetMatchingRowMask(ReadOnlySpan<byte> chunkBytes, int wordIndex)
    {
        System.Diagnostics.Debug.Assert(HasRowFilters);
        System.Diagnostics.Debug.Assert(wordIndex >= 0);
        ref byte chunkStart = ref MemoryMarshal.GetReference(chunkBytes);
        int wordOffset = wordIndex << 3;
        ulong mask = ulong.MaxValue;
        for (int index = 0; index < EnabledBitOffsetsBytes.Length; index++)
        {
            int offset = EnabledBitOffsetsBytes[index] + wordOffset;
            System.Diagnostics.Debug.Assert((uint)offset <= (uint)(chunkBytes.Length - sizeof(ulong)));
            mask &= Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref chunkStart, offset));
        }
        for (int index = 0; index < DisabledBitOffsetsBytes.Length; index++)
        {
            int offset = DisabledBitOffsetsBytes[index] + wordOffset;
            System.Diagnostics.Debug.Assert((uint)offset <= (uint)(chunkBytes.Length - sizeof(ulong)));
            mask &= ~Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref chunkStart, offset));
        }
        return mask;
    }

    private static int[] BindEnableOffsets(
        ArchetypeStorage<TArena> storage,
        ReadOnlySpan<ComponentId> components)
    {
        if (components.IsEmpty)
            return [];

        var offsets = new int[components.Length];
        for (int index = 0; index < components.Length; index++)
        {
            int columnIndex = storage.Store.GetColumnIndex(components[index]);
            if (columnIndex < 0)
                throw new InvalidOperationException("A matched archetype is missing an enable-filter component.");
            ref readonly ComponentColumnLayout column = ref storage.Columns[columnIndex];
            if (!column.HasEnableBits || column.EnableBitsOffsetBytes < 0)
                throw new InvalidOperationException("A query enable filter was not bound to an enable bitset.");
            offsets[index] = column.EnableBitsOffsetBytes;
        }
        return offsets;
    }
}
