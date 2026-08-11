using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Stack-only read view over the occupied rows of one component column.
/// The view is valid only for the duration of its synchronous visitor call.
/// </summary>
public readonly ref struct ReadOnlyComponentChunk<T>
    where T : unmanaged, IComponent<T>
{
    private readonly ReadOnlySpan<ulong> _enableWords;

    internal ReadOnlyComponentChunk(
        WorldId worldId,
        ArchetypeId archetypeId,
        uint chunkIndex,
        int rowOffset,
        ReadOnlySpan<uint> entityIndices,
        ReadOnlySpan<T> components,
        ReadOnlySpan<ulong> enableWords)
    {
        System.Diagnostics.Debug.Assert(worldId.IsValid);
        System.Diagnostics.Debug.Assert(archetypeId.IsValid);
        System.Diagnostics.Debug.Assert(rowOffset >= 0);
        System.Diagnostics.Debug.Assert(entityIndices.Length == components.Length);
        WorldId = worldId;
        ArchetypeId = archetypeId;
        ChunkIndex = chunkIndex;
        RowOffset = rowOffset;
        EntityIndices = entityIndices;
        Components = components;
        _enableWords = enableWords;
    }

    internal WorldId WorldId { get; }

    internal ArchetypeId ArchetypeId { get; }

    internal uint ChunkIndex { get; }

    internal int RowOffset { get; }

    public ReadOnlySpan<uint> EntityIndices { get; }

    public ReadOnlySpan<T> Components { get; }

    public int Count => Components.Length;

    public bool IsEnableable => !_enableWords.IsEmpty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(int row)
    {
        if (unchecked((uint)row) >= unchecked((uint)Count))
            throw new ArgumentOutOfRangeException(nameof(row));
        int absoluteRow = RowOffset + row;
        return _enableWords.IsEmpty ||
            (_enableWords[absoluteRow >> 6] & (1UL << (absoluteRow & 63))) != 0;
    }
}
