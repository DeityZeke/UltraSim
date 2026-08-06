using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Seal-bound direct enable-word offsets shared by generated query routes.</summary>
internal readonly struct ArchetypeQueryRowFilter
{
    private readonly int[] _enabledBitOffsetsBytes;
    private readonly int[] _disabledBitOffsetsBytes;

    private ArchetypeQueryRowFilter(
        int[] enabledBitOffsetsBytes,
        int[] disabledBitOffsetsBytes)
    {
        _enabledBitOffsetsBytes = enabledBitOffsetsBytes;
        _disabledBitOffsetsBytes = disabledBitOffsetsBytes;
    }

    internal bool HasFilters =>
        _enabledBitOffsetsBytes.Length != 0 || _disabledBitOffsetsBytes.Length != 0;

    internal static ArchetypeQueryRowFilter Bind<TArena>(
        ArchetypeStorage<TArena> storage,
        ReadOnlySpan<ComponentId> enabledComponents,
        ReadOnlySpan<ComponentId> disabledComponents)
        where TArena : class, IChunkArenaBackend => new(
            BindEnableOffsets(storage, enabledComponents),
            BindEnableOffsets(storage, disabledComponents));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ulong GetMatchingRowMask(ReadOnlySpan<byte> chunkBytes, int wordIndex)
    {
        System.Diagnostics.Debug.Assert(HasFilters);
        System.Diagnostics.Debug.Assert(wordIndex >= 0);
        ref byte chunkStart = ref MemoryMarshal.GetReference(chunkBytes);
        int wordOffset = wordIndex << 3;
        ulong mask = ulong.MaxValue;
        for (int index = 0; index < _enabledBitOffsetsBytes.Length; index++)
        {
            int offset = _enabledBitOffsetsBytes[index] + wordOffset;
            System.Diagnostics.Debug.Assert((uint)offset <= (uint)(chunkBytes.Length - sizeof(ulong)));
            mask &= Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref chunkStart, offset));
        }
        for (int index = 0; index < _disabledBitOffsetsBytes.Length; index++)
        {
            int offset = _disabledBitOffsetsBytes[index] + wordOffset;
            System.Diagnostics.Debug.Assert((uint)offset <= (uint)(chunkBytes.Length - sizeof(ulong)));
            mask &= ~Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref chunkStart, offset));
        }
        return mask;
    }

    private static int[] BindEnableOffsets<TArena>(
        ArchetypeStorage<TArena> storage,
        ReadOnlySpan<ComponentId> components)
        where TArena : class, IChunkArenaBackend
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
