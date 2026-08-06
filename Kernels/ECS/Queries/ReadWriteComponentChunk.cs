using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Stack-only two-column view with one read-only component and one writable
/// component. The view is valid only during its synchronous visitor call.
/// </summary>
internal readonly ref struct ReadWriteComponentChunk<TRead, TWrite>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    private readonly ReadOnlySpan<ulong> _readEnableWords;
    private readonly Span<ulong> _writeEnableWords;

    internal ReadWriteComponentChunk(
        WorldId worldId,
        ArchetypeId archetypeId,
        uint chunkIndex,
        int rowOffset,
        ReadOnlySpan<uint> entityIndices,
        ReadOnlySpan<TRead> readComponents,
        Span<TWrite> writeComponents,
        ReadOnlySpan<ulong> readEnableWords,
        Span<ulong> writeEnableWords)
    {
        System.Diagnostics.Debug.Assert(worldId.IsValid);
        System.Diagnostics.Debug.Assert(archetypeId.IsValid);
        System.Diagnostics.Debug.Assert(rowOffset >= 0);
        System.Diagnostics.Debug.Assert(entityIndices.Length == readComponents.Length);
        System.Diagnostics.Debug.Assert(entityIndices.Length == writeComponents.Length);
        WorldId = worldId;
        ArchetypeId = archetypeId;
        ChunkIndex = chunkIndex;
        RowOffset = rowOffset;
        EntityIndices = entityIndices;
        ReadComponents = readComponents;
        WriteComponents = writeComponents;
        _readEnableWords = readEnableWords;
        _writeEnableWords = writeEnableWords;
    }

    internal WorldId WorldId { get; }

    internal ArchetypeId ArchetypeId { get; }

    internal uint ChunkIndex { get; }

    /// <summary>Physical row represented by local row zero in this sliced view.</summary>
    internal int RowOffset { get; }

    internal ReadOnlySpan<uint> EntityIndices { get; }

    internal ReadOnlySpan<TRead> ReadComponents { get; }

    internal Span<TWrite> WriteComponents { get; }

    internal int Count => EntityIndices.Length;

    internal bool IsReadEnableable => !_readEnableWords.IsEmpty;

    internal bool IsWriteEnableable => !_writeEnableWords.IsEmpty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsReadEnabled(int row)
    {
        ValidateRow(row);
        int absoluteRow = RowOffset + row;
        return _readEnableWords.IsEmpty ||
            (_readEnableWords[absoluteRow >> 6] & (1UL << (absoluteRow & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsWriteEnabled(int row)
    {
        ValidateRow(row);
        int absoluteRow = RowOffset + row;
        return _writeEnableWords.IsEmpty ||
            (_writeEnableWords[absoluteRow >> 6] & (1UL << (absoluteRow & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetWriteEnabled(int row, bool enabled)
    {
        if (_writeEnableWords.IsEmpty)
            throw new InvalidOperationException("The writable component is not enableable.");
        ValidateRow(row);
        int absoluteRow = RowOffset + row;
        ref ulong word = ref _writeEnableWords[absoluteRow >> 6];
        ulong mask = 1UL << (absoluteRow & 63);
        word = enabled ? word | mask : word & ~mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateRow(int row)
    {
        if (unchecked((uint)row) >= unchecked((uint)Count))
            throw new ArgumentOutOfRangeException(nameof(row));
    }
}
