namespace UltraSim.ECS;

/// <summary>Seal-compiled physical route for one changed-read archetype match.</summary>
internal readonly struct ArchetypeChangedReadQueryRoute<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal ArchetypeChangedReadQueryRoute(
        ArchetypeStorage<TArena> storage,
        int readColumnIndex,
        int changedColumnIndex,
        SparseRowChangeVersions? sparseRowChangeVersions,
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
        SparseRowChangeVersions = sparseRowChangeVersions;
        RowFilter = ArchetypeQueryRowFilter.Bind(
            storage,
            enabledComponents,
            disabledComponents);
    }

    internal ArchetypeStorage<TArena> Storage { get; }

    internal int ReadColumnIndex { get; }

    internal int ChangedColumnIndex { get; }

    private SparseRowChangeVersions? SparseRowChangeVersions { get; }

    internal bool HasSparseRowChangeVersions => SparseRowChangeVersions is not null;

    private ArchetypeQueryRowFilter RowFilter { get; }

    internal bool HasRowFilters => RowFilter.HasFilters;

    internal ulong GetMatchingRowMask(ReadOnlySpan<byte> chunkBytes, int wordIndex) =>
        RowFilter.GetMatchingRowMask(chunkBytes, wordIndex);

    internal ulong GetChangedRowMask(
        uint chunkIndex,
        int wordIndex,
        int activeRows,
        ChangeVersion baseline)
    {
        SparseRowChangeVersions sparse = SparseRowChangeVersions ??
            throw new InvalidOperationException(
                "The changed-read route was not compiled for exact row tracking.");
        return Storage.Store.GetBoundChangedRowMask(
            sparse,
            chunkIndex,
            wordIndex,
            activeRows,
            baseline);
    }
}
