using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Optional bounded row-precision refinement for one physical component column.
/// Dense writes initially retain one version per chunk and are materialized into
/// the then-current occupants before occupancy changes. Sparse writes retain one
/// version per physical row so independent cursors can reconstruct an exact
/// logical changed-row mask.
/// </summary>
/// <remarks>
/// Storage is fully reserved when tracking is enabled: one dense stamp per chunk
/// plus one row stamp for every configured row in every configured chunk. "Sparse"
/// describes publication behavior, not allocation density.
/// </remarks>
internal sealed class SparseRowChangeVersions
{
    private readonly ulong[] _denseVersions;
    private readonly ulong[] _rowVersions;
    private readonly int _rowCapacity;

    internal SparseRowChangeVersions(int maximumChunks, int rowCapacity)
    {
        if (maximumChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));
        if (rowCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowCapacity));

        _denseVersions = new ulong[maximumChunks];
        _rowVersions = new ulong[checked(maximumChunks * rowCapacity)];
        _rowCapacity = rowCapacity;
    }

    internal long MetadataBytes => checked(
        ((long)_denseVersions.Length + _rowVersions.Length) * sizeof(ulong));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void StampDense(uint chunkIndex, ChangeVersion version)
    {
        System.Diagnostics.Debug.Assert(chunkIndex < unchecked((uint)_denseVersions.Length));
        System.Diagnostics.Debug.Assert(version.IsValid);
        _denseVersions[chunkIndex] = version.Value;
    }

    internal void StampRange(
        uint chunkIndex,
        int rowOffset,
        int rowCount,
        ChangeVersion version)
    {
        System.Diagnostics.Debug.Assert(chunkIndex < unchecked((uint)_denseVersions.Length));
        System.Diagnostics.Debug.Assert(rowOffset >= 0);
        System.Diagnostics.Debug.Assert(rowCount > 0);
        System.Diagnostics.Debug.Assert(rowOffset + rowCount <= _rowCapacity);
        System.Diagnostics.Debug.Assert(version.IsValid);
        _rowVersions.AsSpan(GetRowIndex(chunkIndex, rowOffset), rowCount).Fill(version.Value);
    }

    /// <summary>
    /// Converts a chunk-wide publication into row-owned history for the logical
    /// occupants that exist immediately before an occupancy change. The dense
    /// refinement can then no longer leak into a later occupant of the same
    /// physical chunk or row.
    /// </summary>
    internal void MaterializeDenseForOccupancyChange(uint chunkIndex, int activeRows)
    {
        System.Diagnostics.Debug.Assert(chunkIndex < unchecked((uint)_denseVersions.Length));
        System.Diagnostics.Debug.Assert(activeRows >= 0 && activeRows <= _rowCapacity);

        ChangeVersion dense = new(_denseVersions[chunkIndex]);
        if (!dense.IsValid)
            return;

        int versionOffset = GetRowIndex(chunkIndex, 0);
        for (int row = 0; row < activeRows; row++)
        {
            ChangeVersion current = new(_rowVersions[versionOffset + row]);
            if (dense.IsNewerThan(current))
                _rowVersions[versionOffset + row] = dense.Value;
        }
        _denseVersions[chunkIndex] = 0;
    }

    /// <summary>
    /// Returns changed physical rows for one 64-row word. A newer dense stamp
    /// makes every active row match without touching the row-version array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal ulong GetChangedRowMask(
        uint chunkIndex,
        int wordIndex,
        int activeRows,
        ChangeVersion baseline)
    {
        System.Diagnostics.Debug.Assert(chunkIndex < unchecked((uint)_denseVersions.Length));
        System.Diagnostics.Debug.Assert(wordIndex >= 0);
        System.Diagnostics.Debug.Assert(activeRows is >= 1 and <= 64);

        ulong activeMask = activeRows == 64
            ? ulong.MaxValue
            : (1UL << activeRows) - 1UL;
        if (new ChangeVersion(_denseVersions[chunkIndex]).IsNewerThan(baseline))
            return activeMask;

        int rowOffset = wordIndex << 6;
        int versionOffset = GetRowIndex(chunkIndex, rowOffset);
        ulong changedMask = 0;
        for (int row = 0; row < activeRows; row++)
        {
            ulong candidate = _rowVersions[versionOffset + row];
            if (new ChangeVersion(candidate).IsNewerThan(baseline))
                changedMask |= 1UL << row;
        }
        return changedMask;
    }

    internal void MoveRow(
        uint sourceChunkIndex,
        int sourceRow,
        uint destinationChunkIndex,
        int destinationRow)
    {
        int destinationIndex = GetRowIndex(destinationChunkIndex, destinationRow);
        _rowVersions[destinationIndex] = GetEffectiveRowVersion(sourceChunkIndex, sourceRow).Value;
    }

    /// <summary>
    /// Returns the newest observation that applies to one row. A dense publication
    /// applies to every row that was active in the source chunk; a sparse publication
    /// applies only to this physical row.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ChangeVersion GetEffectiveRowVersion(uint chunkIndex, int row)
    {
        ChangeVersion dense = new(_denseVersions[chunkIndex]);
        ChangeVersion sparse = new(_rowVersions[GetRowIndex(chunkIndex, row)]);
        return dense.IsNewerThan(sparse) ? dense : sparse;
    }

    /// <summary>
    /// Merges logical row history copied from another archetype. This deliberately
    /// does not stamp the dense-full-chunk version: only the transferred row changed
    /// ownership.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MergeRow(uint chunkIndex, int row, ChangeVersion version)
    {
        if (!version.IsValid)
            return;

        int index = GetRowIndex(chunkIndex, row);
        ChangeVersion current = new(_rowVersions[index]);
        if (version.IsNewerThan(current))
            _rowVersions[index] = version.Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ClearRow(uint chunkIndex, int row) =>
        _rowVersions[GetRowIndex(chunkIndex, row)] = 0;

    internal void ClearRange(uint chunkIndex, int rowOffset, int rowCount) =>
        _rowVersions.AsSpan(GetRowIndex(chunkIndex, rowOffset), rowCount).Clear();

    internal void ClearChunk(uint chunkIndex)
    {
        System.Diagnostics.Debug.Assert(chunkIndex < unchecked((uint)_denseVersions.Length));
        _denseVersions[chunkIndex] = 0;
        _rowVersions.AsSpan(GetRowIndex(chunkIndex, 0), _rowCapacity).Clear();
    }

    internal bool IsChunkClear(uint chunkIndex)
    {
        if (_denseVersions[chunkIndex] != 0)
            return false;
        ReadOnlySpan<ulong> rows = _rowVersions.AsSpan(
            GetRowIndex(chunkIndex, 0),
            _rowCapacity);
        for (int index = 0; index < rows.Length; index++)
        {
            if (rows[index] != 0)
                return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetRowIndex(uint chunkIndex, int row) =>
        unchecked(((int)chunkIndex * _rowCapacity) + row);
}
