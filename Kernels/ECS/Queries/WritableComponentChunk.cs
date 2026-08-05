using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Stack-only write view over the occupied rows of one component column.
/// The view is valid only for the duration of its synchronous visitor call.
/// </summary>
internal readonly ref struct WritableComponentChunk<T>
    where T : unmanaged, IComponent<T>
{
    private readonly Span<ulong> _enableWords;

    internal WritableComponentChunk(
        WorldId worldId,
        ArchetypeId archetypeId,
        uint chunkIndex,
        ReadOnlySpan<uint> entityIndices,
        Span<T> components,
        Span<ulong> enableWords)
    {
        System.Diagnostics.Debug.Assert(worldId.IsValid);
        System.Diagnostics.Debug.Assert(archetypeId.IsValid);
        System.Diagnostics.Debug.Assert(entityIndices.Length == components.Length);
        WorldId = worldId;
        ArchetypeId = archetypeId;
        ChunkIndex = chunkIndex;
        EntityIndices = entityIndices;
        Components = components;
        _enableWords = enableWords;
    }

    internal WorldId WorldId { get; }

    internal ArchetypeId ArchetypeId { get; }

    internal uint ChunkIndex { get; }

    internal ReadOnlySpan<uint> EntityIndices { get; }

    internal Span<T> Components { get; }

    internal int Count => Components.Length;

    internal bool IsEnableable => !_enableWords.IsEmpty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsEnabled(int row)
    {
        if ((uint)row >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(row));
        return _enableWords.IsEmpty ||
            (_enableWords[row >> 6] & (1UL << (row & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetEnabled(int row, bool enabled)
    {
        if (_enableWords.IsEmpty)
            throw new InvalidOperationException("The component is not enableable.");
        if ((uint)row >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(row));

        ref ulong word = ref _enableWords[row >> 6];
        ulong mask = 1UL << (row & 63);
        word = enabled ? word | mask : word & ~mask;
    }
}
