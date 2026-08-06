using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Optional bounded row-precision refinement for one physical component column.
/// Dense writes retain one version per chunk; sparse writes retain one version per
/// physical row so independent cursors can reconstruct an exact changed-row mask.
/// </summary>
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
        int sourceIndex = GetRowIndex(sourceChunkIndex, sourceRow);
        int destinationIndex = GetRowIndex(destinationChunkIndex, destinationRow);
        ChangeVersion dense = new(_denseVersions[sourceChunkIndex]);
        ChangeVersion sparse = new(_rowVersions[sourceIndex]);
        _rowVersions[destinationIndex] = dense.IsNewerThan(sparse)
            ? dense.Value
            : sparse.Value;
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
