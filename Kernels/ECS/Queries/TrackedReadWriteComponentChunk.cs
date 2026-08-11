using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Stack-only conditional-write view for the handwritten 1R1W family.</summary>
public readonly ref struct TrackedReadWriteComponentChunk<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    private readonly ReadOnlySpan<ulong> _readEnableWords;

    internal TrackedReadWriteComponentChunk(
        WorldId worldId,
        ArchetypeId archetypeId,
        uint chunkIndex,
        int rowOffset,
        ReadOnlySpan<uint> entityIndices,
        ReadOnlySpan<TRead> readComponents,
        TrackedWritableComponentChunk<TWrite> write,
        ReadOnlySpan<ulong> readEnableWords)
    {
        System.Diagnostics.Debug.Assert(worldId.IsValid);
        System.Diagnostics.Debug.Assert(archetypeId.IsValid);
        System.Diagnostics.Debug.Assert(rowOffset >= 0);
        System.Diagnostics.Debug.Assert(entityIndices.Length == readComponents.Length);
        System.Diagnostics.Debug.Assert(entityIndices.Length == write.Count);
        WorldId = worldId;
        ArchetypeId = archetypeId;
        ChunkIndex = chunkIndex;
        RowOffset = rowOffset;
        EntityIndices = entityIndices;
        ReadComponents = readComponents;
        Write = write;
        _readEnableWords = readEnableWords;
    }

    internal WorldId WorldId { get; }
    internal ArchetypeId ArchetypeId { get; }
    internal uint ChunkIndex { get; }
    internal int RowOffset { get; }
    public ReadOnlySpan<uint> EntityIndices { get; }
    public ReadOnlySpan<TRead> ReadComponents { get; }
    public TrackedWritableComponentChunk<TWrite> Write { get; }
    public int Count => EntityIndices.Length;
    public bool IsReadEnableable => !_readEnableWords.IsEmpty;
    public bool IsWriteEnableable => Write.IsEnableable;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsReadEnabled(int row)
    {
        if (unchecked((uint)row) >= unchecked((uint)Count))
            throw new ArgumentOutOfRangeException(nameof(row));
        int absoluteRow = RowOffset + row;
        return _readEnableWords.IsEmpty ||
            (_readEnableWords[absoluteRow >> 6] & (1UL << (absoluteRow & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWriteEnabled(int row) => Write.IsEnabled(row);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetWriteEnabledTracked(int row, bool enabled) => Write.SetEnabledTracked(row, enabled);
}
