namespace UltraSim.ECS;

/// <summary>Seal-compiled physical route for one changed-read archetype match.</summary>
internal readonly struct ArchetypeChangedReadQueryRoute<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal ArchetypeChangedReadQueryRoute(
        ArchetypeStorage<TArena> storage,
        int readColumnIndex,
        int changedColumnIndex,
        ReadOnlySpan<ComponentId> enabledComponents,
        ReadOnlySpan<ComponentId> disabledComponents)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (unchecked((uint)readColumnIndex) >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(readColumnIndex));
        if (unchecked((uint)changedColumnIndex) >= ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(changedColumnIndex));

        Storage = storage;
        ReadColumnIndex = (ushort)readColumnIndex;
        ChangedColumnIndex = (ushort)changedColumnIndex;
        RowFilter = ArchetypeQueryRowFilter.Bind(
            storage,
            enabledComponents,
            disabledComponents);
    }

    internal ArchetypeStorage<TArena> Storage { get; }

    internal int ReadColumnIndex { get; }

    internal int ChangedColumnIndex { get; }

    private ArchetypeQueryRowFilter RowFilter { get; }

    internal bool HasRowFilters => RowFilter.HasFilters;

    internal ulong GetMatchingRowMask(ReadOnlySpan<byte> chunkBytes, int wordIndex) =>
        RowFilter.GetMatchingRowMask(chunkBytes, wordIndex);
}
