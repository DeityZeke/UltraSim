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
        RowFilter = ArchetypeQueryRowFilter.Bind(
            storage,
            enabledComponents,
            disabledComponents);
    }

    internal ArchetypeStorage<TArena> Storage { get; }

    internal int ReadColumnIndex { get; }

    internal int WriteColumnIndex { get; }

    private ArchetypeQueryRowFilter RowFilter { get; }

    internal bool HasRowFilters =>
        RowFilter.HasFilters;

    internal ulong GetMatchingRowMask(ReadOnlySpan<byte> chunkBytes, int wordIndex) =>
        RowFilter.GetMatchingRowMask(chunkBytes, wordIndex);
}
