using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS.BitmapExperiment;

/// <summary>
/// Fixed-capacity experimental SoA store. Each ulong column is physically
/// contiguous; one shared occupancy word governs 64 logical rows across every
/// column. This deliberately isolates packed relocation versus persistent holes
/// without introducing a different allocator or component representation.
/// </summary>
public sealed unsafe class PrototypeArchetypeStore : IDisposable
{
    private const int RowsPerWord = 64;

    private readonly PrototypeStorageKind _kind;
    private readonly int _capacity;
    private readonly int _paddedCapacity;
    private readonly int _columnCount;
    private readonly int _wordCount;
    private readonly ulong[] _occupancy;
    private readonly int[] _rowEntities;
    private readonly ulong _lastCapacityMask;
    private ulong* _columns;
    private int _count;
    private int _highWaterRows;
    private int _firstNonFullWord;
    private int _bulkWord = -1;
    private int _bulkSearchWord;
    private bool _disposed;
    private long _rowRelocations;
    private long _holesReused;
    private long _compactionPasses;

    public PrototypeArchetypeStore(
        PrototypeStorageKind kind,
        int capacity,
        int columnCount)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (columnCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(columnCount));

        _kind = kind;
        _capacity = capacity;
        _paddedCapacity = checked((capacity + RowsPerWord - 1) & -RowsPerWord);
        _columnCount = columnCount;
        _wordCount = _paddedCapacity / RowsPerWord;
        _occupancy = kind == PrototypeStorageKind.DensePacked
            ? Array.Empty<ulong>()
            : new ulong[_wordCount];
        _rowEntities = new int[capacity];
        int finalRows = capacity - ((_wordCount - 1) * RowsPerWord);
        _lastCapacityMask = finalRows == RowsPerWord
            ? ulong.MaxValue
            : (1UL << finalRows) - 1UL;

        nuint bytes = checked((nuint)((long)_paddedCapacity * columnCount * sizeof(ulong)));
        _columns = (ulong*)NativeMemory.AlignedAlloc(bytes, 64);
        if (_columns is null)
            throw new OutOfMemoryException($"Could not allocate {bytes} bytes for bitmap experiment columns.");
        new Span<byte>(_columns, checked((int)bytes)).Clear();
        Reset();
    }

    public PrototypeStorageKind Kind => _kind;

    public int Capacity => _capacity;

    public int ColumnCount => _columnCount;

    public int Count => _count;

    public int HighWaterRows => _kind == PrototypeStorageKind.DensePacked ? _count : _highWaterRows;

    public long PayloadBytes => (long)_paddedCapacity * _columnCount * sizeof(ulong);

    public long OccupancyMetadataBytes => (long)_occupancy.Length * sizeof(ulong);

    public long RowEntityMetadataBytes => (long)_rowEntities.Length * sizeof(int);

    public long RowRelocations => _rowRelocations;

    public long HolesReused => _holesReused;

    public bool IsDense => _kind == PrototypeStorageKind.DensePacked || _count == _highWaterRows;

    public void Reset()
    {
        ThrowIfDisposed();
        Array.Clear(_occupancy);
        Array.Fill(_rowEntities, -1);
        _count = 0;
        _highWaterRows = 0;
        _firstNonFullWord = 0;
        _bulkWord = -1;
        _bulkSearchWord = 0;
        _rowRelocations = 0;
        _holesReused = 0;
        _compactionPasses = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsRow(int row) =>
        _kind == PrototypeStorageKind.DensePacked
            ? unchecked((uint)row) < unchecked((uint)_count)
            : unchecked((uint)row) < unchecked((uint)_highWaterRows) &&
              (_occupancy[row >> 6] & (1UL << (row & 63))) != 0;

    public void BeginBulkAllocation()
    {
        _bulkWord = -1;
        _bulkSearchWord = 0;
    }

    public void EndBulkAllocation() => _bulkWord = -1;

    public bool TryAdd(int entityIndex, ulong seed, bool bulk, out int row)
    {
        ThrowIfDisposed();
        if (_count == _capacity)
        {
            row = -1;
            return false;
        }

        if (_kind == PrototypeStorageKind.DensePacked)
        {
            row = _count;
        }
        else
        {
            row = bulk && UsesTopologyAwareBulkAllocation(_kind)
                ? FindTopologyBulkRow()
                : FindFirstFreeRow();
            if (row < _highWaterRows)
                _holesReused++;
            SetOccupied(row, occupied: true);
            if (row >= _highWaterRows)
                _highWaterRows = row + 1;
        }

        _rowEntities[row] = entityIndex;
        InitializeRow(row, seed);
        _count++;
        return true;
    }

    public bool TryRemove(int row, int expectedEntityIndex, out PrototypeRelocation relocation)
    {
        ThrowIfDisposed();
        if (!ContainsRow(row) || _rowEntities[row] != expectedEntityIndex)
        {
            relocation = default;
            return false;
        }

        if (_kind == PrototypeStorageKind.DensePacked)
        {
            int donorRow = _count - 1;
            int donorEntity = _rowEntities[donorRow];
            bool moved = donorRow != row;
            if (moved)
            {
                CopyRow(donorRow, row);
                _rowEntities[row] = donorEntity;
                _rowRelocations++;
            }
            _rowEntities[donorRow] = -1;
            _count--;
            relocation = moved
                ? new PrototypeRelocation(donorEntity, donorRow, row)
                : default;
            return true;
        }

        SetOccupied(row, occupied: false);
        _rowEntities[row] = -1;
        _count--;
        int word = row >> 6;
        if (word < _firstNonFullWord)
            _firstNonFullWord = word;
        if (row + 1 == _highWaterRows)
            ShrinkHighWater();
        relocation = default;
        return true;
    }

    public int Compact(Span<PrototypeRelocation> relocations)
    {
        ThrowIfDisposed();
        if (_kind == PrototypeStorageKind.DensePacked || _count == _highWaterRows)
            return 0;

        int relocationCount = 0;
        int low = FindFirstFreeRowBefore(_highWaterRows);
        int high = FindPreviousOccupiedRow(_highWaterRows - 1);
        while (low >= 0 && high >= 0 && low < high)
        {
            if (relocationCount == relocations.Length)
                throw new ArgumentException("Relocation scratch is too small.", nameof(relocations));

            int movedEntity = _rowEntities[high];
            CopyRow(high, low);
            _rowEntities[low] = movedEntity;
            _rowEntities[high] = -1;
            SetOccupied(low, occupied: true);
            SetOccupied(high, occupied: false);
            relocations[relocationCount++] = new PrototypeRelocation(movedEntity, high, low);
            _rowRelocations++;

            low = FindFirstFreeRowBefore(high);
            high = FindPreviousOccupiedRow(high - 1);
        }

        _highWaterRows = _count;
        RebuildDensePrefixOccupancy();
        _firstNonFullWord = Math.Min(_count >> 6, _wordCount - 1);
        _bulkWord = -1;
        _bulkSearchWord = 0;
        _compactionPasses++;
        return relocationCount;
    }

    public bool ShouldCompact(double minimumDensity, int maximumPartialWords)
    {
        if (_kind != PrototypeStorageKind.BitmapAdaptive || _count == _highWaterRows)
            return false;
        if (_highWaterRows == 0)
            return false;
        PrototypeTopology topology = GetTopology();
        return (double)_count / _highWaterRows < minimumDensity ||
            topology.PartialWords > maximumPartialWords;
    }

    public ulong Traverse(int queryColumnCount, out int visitedRows) =>
        Traverse(queryColumnCount, mutateFirstColumn: false, out visitedRows);

    public ulong Traverse(int queryColumnCount, bool mutateFirstColumn, out int visitedRows)
    {
        ThrowIfDisposed();
        if (queryColumnCount <= 0 || queryColumnCount > _columnCount)
            throw new ArgumentOutOfRangeException(nameof(queryColumnCount));

        ulong checksum = 0;
        visitedRows = 0;
        if (_kind == PrototypeStorageKind.DensePacked)
        {
            TraverseDenseRange(0, _count, queryColumnCount, mutateFirstColumn, ref checksum);
            visitedRows = _count;
            return checksum;
        }

        int activeWords = (_highWaterRows + 63) >> 6;
        for (int word = 0; word < activeWords; word++)
        {
            ulong mask = _occupancy[word] & GetHighWaterMask(word);
            if (mask == 0)
                continue;
            int wordBase = word << 6;
            ulong fullMask = GetHighWaterMask(word);
            if (mask == fullMask)
            {
                int count = BitOperations.PopCount(fullMask);
                TraverseDenseRange(wordBase, count, queryColumnCount, mutateFirstColumn, ref checksum);
                visitedRows += count;
                continue;
            }

            if (_kind is not PrototypeStorageKind.BitmapPersistent)
            {
                TraverseOccupiedRuns(wordBase, mask, queryColumnCount, mutateFirstColumn, ref checksum, ref visitedRows);
                continue;
            }

            ulong bits = mask;
            while (bits != 0)
            {
                int bit = BitOperations.TrailingZeroCount(bits);
                int row = wordBase + bit;
                TraverseOne(row, queryColumnCount, mutateFirstColumn, ref checksum);
                visitedRows++;
                bits &= bits - 1;
            }
        }
        return checksum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Read(int row, int column)
    {
        if (!ContainsRow(row))
            throw new ArgumentException("The row is not occupied.", nameof(row));
        if (unchecked((uint)column) >= unchecked((uint)_columnCount))
            throw new ArgumentOutOfRangeException(nameof(column));
        return _columns[(column * _paddedCapacity) + row];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(int row, int column, ulong value)
    {
        if (!ContainsRow(row))
            throw new ArgumentException("The row is not occupied.", nameof(row));
        if (unchecked((uint)column) >= unchecked((uint)_columnCount))
            throw new ArgumentOutOfRangeException(nameof(column));
        _columns[(column * _paddedCapacity) + row] = value;
    }

    public int GetEntityIndex(int row)
    {
        if (!ContainsRow(row))
            throw new ArgumentException("The row is not occupied.", nameof(row));
        return _rowEntities[row];
    }

    public void CopyRetainedFrom(
        PrototypeArchetypeStore source,
        int sourceRow,
        int destinationRow,
        int retainedColumns)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.ContainsRow(sourceRow) || !ContainsRow(destinationRow))
            throw new ArgumentException("Retained copy requires occupied source and destination rows.");
        int count = Math.Min(retainedColumns, Math.Min(source._columnCount, _columnCount));
        for (int column = 0; column < count; column++)
        {
            _columns[(column * _paddedCapacity) + destinationRow] =
                source._columns[(column * source._paddedCapacity) + sourceRow];
        }
    }

    public PrototypeTopology GetTopology()
    {
        ThrowIfDisposed();
        if (_kind == PrototypeStorageKind.DensePacked)
        {
            int full = _count >> 6;
            int partial = (_count & 63) == 0 ? 0 : 1;
            return new PrototypeTopology(
                _capacity,
                _count,
                _count,
                0,
                full,
                partial,
                _wordCount - full - partial,
                _rowRelocations,
                _rowRelocations * _columnCount * sizeof(ulong),
                0,
                0);
        }

        int fullWords = 0;
        int partialWords = 0;
        int activeWords = (_highWaterRows + 63) >> 6;
        for (int word = 0; word < activeWords; word++)
        {
            ulong mask = _occupancy[word] & GetHighWaterMask(word);
            if (mask == 0)
                continue;
            if (mask == GetHighWaterMask(word))
                fullWords++;
            else
                partialWords++;
        }
        return new PrototypeTopology(
            _capacity,
            _count,
            _highWaterRows,
            _highWaterRows - _count,
            fullWords,
            partialWords,
            _wordCount - fullWords - partialWords,
            _rowRelocations,
            _rowRelocations * _columnCount * sizeof(ulong),
            _holesReused,
            _compactionPasses);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (_columns is not null)
        {
            NativeMemory.AlignedFree(_columns);
            _columns = null;
        }
        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool UsesTopologyAwareBulkAllocation(PrototypeStorageKind kind) =>
        kind is PrototypeStorageKind.BitmapTopologyAware or PrototypeStorageKind.BitmapAdaptive;

    private int FindFirstFreeRow()
    {
        for (int word = _firstNonFullWord; word < _wordCount; word++)
        {
            ulong validMask = GetCapacityMask(word);
            ulong free = (~_occupancy[word]) & validMask;
            if (free == 0)
                continue;
            _firstNonFullWord = word;
            return (word << 6) + BitOperations.TrailingZeroCount(free);
        }
        return -1;
    }

    private int FindTopologyBulkRow()
    {
        if (_bulkWord >= 0)
        {
            ulong free = (~_occupancy[_bulkWord]) & GetCapacityMask(_bulkWord);
            if (free != 0)
                return (_bulkWord << 6) + BitOperations.TrailingZeroCount(free);
        }

        for (int word = _bulkSearchWord; word < _wordCount; word++)
        {
            if (_occupancy[word] != 0)
                continue;
            _bulkWord = word;
            _bulkSearchWord = word + 1;
            return word << 6;
        }

        _bulkWord = -1;
        return FindFirstFreeRow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetOccupied(int row, bool occupied)
    {
        int word = row >> 6;
        ulong bit = 1UL << (row & 63);
        if (occupied)
            _occupancy[word] |= bit;
        else
            _occupancy[word] &= ~bit;
    }

    private int FindFirstFreeRowBefore(int exclusiveEnd)
    {
        int words = (exclusiveEnd + 63) >> 6;
        for (int word = 0; word < words; word++)
        {
            ulong valid = GetMaskBelow(word, exclusiveEnd);
            ulong free = (~_occupancy[word]) & valid;
            if (free != 0)
                return (word << 6) + BitOperations.TrailingZeroCount(free);
        }
        return -1;
    }

    private int FindPreviousOccupiedRow(int startRow)
    {
        if (startRow < 0)
            return -1;
        int word = startRow >> 6;
        int bit = startRow & 63;
        ulong bits = _occupancy[word] & (bit == 63 ? ulong.MaxValue : (1UL << (bit + 1)) - 1UL);
        while (true)
        {
            if (bits != 0)
                return (word << 6) + (63 - BitOperations.LeadingZeroCount(bits));
            if (--word < 0)
                return -1;
            bits = _occupancy[word];
        }
    }

    private void ShrinkHighWater()
    {
        int previous = FindPreviousOccupiedRow(_highWaterRows - 2);
        _highWaterRows = previous + 1;
    }

    private void RebuildDensePrefixOccupancy()
    {
        Array.Clear(_occupancy);
        int fullWords = _count >> 6;
        for (int word = 0; word < fullWords; word++)
            _occupancy[word] = ulong.MaxValue;
        int remaining = _count & 63;
        if (remaining != 0)
            _occupancy[fullWords] = (1UL << remaining) - 1UL;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetCapacityMask(int word) =>
        word == _wordCount - 1 ? _lastCapacityMask : ulong.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetHighWaterMask(int word) => GetMaskBelow(word, _highWaterRows);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong GetMaskBelow(int word, int exclusiveEnd)
    {
        int wordStart = word << 6;
        int remaining = exclusiveEnd - wordStart;
        if (remaining <= 0)
            return 0;
        if (remaining >= 64)
            return GetCapacityMask(word);
        return (1UL << remaining) - 1UL;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitializeRow(int row, ulong seed)
    {
        for (int column = 0; column < _columnCount; column++)
        {
            _columns[(column * _paddedCapacity) + row] =
                unchecked(seed + ((ulong)(column + 1) * 0x9E37_79B9_7F4A_7C15UL));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyRow(int sourceRow, int destinationRow)
    {
        for (int column = 0; column < _columnCount; column++)
        {
            _columns[(column * _paddedCapacity) + destinationRow] =
                _columns[(column * _paddedCapacity) + sourceRow];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TraverseDenseRange(
        int startRow,
        int count,
        int queryColumnCount,
        bool mutateFirstColumn,
        ref ulong checksum)
    {
        int end = startRow + count;
        for (int row = startRow; row < end; row++)
            TraverseOne(row, queryColumnCount, mutateFirstColumn, ref checksum);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TraverseOne(int row, int queryColumnCount, bool mutateFirstColumn, ref ulong checksum)
    {
        if (mutateFirstColumn)
            _columns[row] = unchecked(_columns[row] + 1UL);
        for (int column = 0; column < queryColumnCount; column++)
            checksum = unchecked(checksum + _columns[(column * _paddedCapacity) + row]);
    }

    private void TraverseOccupiedRuns(
        int wordBase,
        ulong bits,
        int queryColumnCount,
        bool mutateFirstColumn,
        ref ulong checksum,
        ref int visitedRows)
    {
        while (bits != 0)
        {
            int start = BitOperations.TrailingZeroCount(bits);
            ulong shifted = bits >> start;
            int length = shifted == ulong.MaxValue
                ? 64
                : BitOperations.TrailingZeroCount(~shifted);
            int boundedLength = Math.Min(length, 64 - start);
            TraverseDenseRange(wordBase + start, boundedLength, queryColumnCount, mutateFirstColumn, ref checksum);
            visitedRows += boundedLength;
            ulong runMask = boundedLength == 64
                ? ulong.MaxValue
                : ((1UL << boundedLength) - 1UL) << start;
            bits &= ~runMask;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
