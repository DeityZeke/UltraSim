using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity, densely packed rows for one archetype. Structural methods are
/// single-owner; disjoint occupied chunks may be processed in parallel after a barrier.
/// </summary>
internal sealed class ArchetypeChunkStore<TArena> : IDisposable
    where TArena : class, IChunkArenaBackend
{
    private const int NoChunk = -1;

    private readonly TArena _arena;
    private readonly ChunkLayoutPlan _layout;
    private readonly ComponentColumnLayout[] _columns;
    private readonly ChunkRecord[] _chunks;
    private int _fullHead = NoChunk;
    private int _partialHead = NoChunk;
    private int _emptyHead = NoChunk;
    private int _entityCount;
    private int _acquiredChunkCount;
    private int _fullChunkCount;
    private int _partialChunkCount;
    private int _emptyChunkCount;
    private bool _disposed;

    internal ArchetypeChunkStore(
        TArena arena,
        in ChunkLayoutPlan layout,
        ReadOnlySpan<ComponentColumnLayout> columns)
    {
        ArgumentNullException.ThrowIfNull(arena);
        if (arena.IsDisposed)
            throw new ObjectDisposedException(nameof(arena));
        if (!arena.Capacity.Supports(layout))
            throw new ArgumentException("The chunk arena cannot execute this layout.", nameof(arena));

        ChunkArenaSnapshot arenaSnapshot = arena.GetSnapshot();
        if (arenaSnapshot.AliveCount != 0 ||
            arenaSnapshot.RetiredCount != 0 ||
            arenaSnapshot.HighWatermark != 0)
        {
            throw new ArgumentException(
                "An archetype chunk store requires exclusive ownership of a fresh arena.",
                nameof(arena));
        }

        _layout = layout;
        _columns = ChunkLayoutValidator.CopyAndValidate(layout, columns);
        _arena = arena;
        _chunks = new ChunkRecord[arena.Capacity.MaximumChunks];
        for (int i = 0; i < _chunks.Length; i++)
        {
            _chunks[i].Next = NoChunk;
            _chunks[i].Previous = NoChunk;
        }
    }

    internal TArena Arena => _arena;

    internal ChunkLayoutPlan Layout => _layout;

    internal ReadOnlySpan<ComponentColumnLayout> Columns => _columns;

    internal int Count => _entityCount;

    internal long MaximumRows => (long)_chunks.Length * _layout.RowCapacity;

    internal bool IsDisposed => _disposed;

    internal bool TryAdd(uint entityIndex, out ChunkRow location)
    {
        ThrowIfDisposed();
        int chunkIndex = AcquireDestinationChunk();
        if (chunkIndex == NoChunk)
        {
            location = ChunkRow.Invalid;
            return false;
        }

        ref ChunkRecord chunk = ref _chunks[chunkIndex];
        int row = chunk.RowCount;
        Span<byte> bytes = _arena.GetChunkBytes(chunk.Handle);
        InitializeRows(bytes, row, 1);
        GetEntityIndices(bytes)[row] = entityIndex;
        chunk.RowCount = row + 1;
        _entityCount++;
        location = new ChunkRow((uint)chunkIndex, row);

        if (chunk.RowCount == _layout.RowCapacity)
            MoveChunk(chunkIndex, ChunkListState.Full);
        return true;
    }

    internal int Add(ReadOnlySpan<uint> entityIndices, Span<ChunkRow> locations)
    {
        ThrowIfDisposed();
        if (locations.Length < entityIndices.Length)
            throw new ArgumentException("The location output span is smaller than the entity input span.", nameof(locations));

        int written = 0;
        while (written < entityIndices.Length)
        {
            int chunkIndex = AcquireDestinationChunk();
            if (chunkIndex == NoChunk)
                break;

            ref ChunkRecord chunk = ref _chunks[chunkIndex];
            int startRow = chunk.RowCount;
            int count = Math.Min(_layout.RowCapacity - startRow, entityIndices.Length - written);
            Span<byte> bytes = _arena.GetChunkBytes(chunk.Handle);
            InitializeRows(bytes, startRow, count);
            entityIndices.Slice(written, count).CopyTo(GetEntityIndices(bytes).Slice(startRow, count));
            for (int i = 0; i < count; i++)
                locations[written + i] = new ChunkRow((uint)chunkIndex, startRow + i);

            chunk.RowCount += count;
            _entityCount += count;
            written += count;
            if (chunk.RowCount == _layout.RowCapacity)
                MoveChunk(chunkIndex, ChunkListState.Full);
        }

        return written;
    }

    internal bool TryRemove(
        ChunkRow location,
        uint expectedEntityIndex,
        out ChunkRowRelocation relocation)
    {
        ThrowIfDisposed();
        if (!TryResolve(location, out int targetChunkIndex, out int targetRow))
        {
            relocation = default;
            return false;
        }

        ref ChunkRecord target = ref _chunks[targetChunkIndex];
        Span<byte> targetBytes = _arena.GetChunkBytes(target.Handle);
        if (GetEntityIndices(targetBytes)[targetRow] != expectedEntityIndex)
        {
            relocation = default;
            return false;
        }

        int donorChunkIndex = _partialHead != NoChunk ? _partialHead : _fullHead;
        if (donorChunkIndex == NoChunk)
            throw new InvalidOperationException("An occupied archetype has no donor chunk.");

        ref ChunkRecord donor = ref _chunks[donorChunkIndex];
        int donorRow = donor.RowCount - 1;
        Span<byte> donorBytes = donorChunkIndex == targetChunkIndex
            ? targetBytes
            : _arena.GetChunkBytes(donor.Handle);
        uint donorEntityIndex = GetEntityIndices(donorBytes)[donorRow];
        bool moved = donorChunkIndex != targetChunkIndex || donorRow != targetRow;
        if (moved)
            CopyRow(donorBytes, donorRow, targetBytes, targetRow);

        ClearRow(donorBytes, donorRow);
        int previousDonorCount = donor.RowCount;
        donor.RowCount = previousDonorCount - 1;
        _entityCount--;
        if (donor.RowCount == 0)
            MoveChunk(donorChunkIndex, ChunkListState.Empty);
        else if (previousDonorCount == _layout.RowCapacity)
            MoveChunk(donorChunkIndex, ChunkListState.Partial);

        relocation = moved
            ? new ChunkRowRelocation(
                donorEntityIndex,
                new ChunkRow((uint)donorChunkIndex, donorRow),
                new ChunkRow((uint)targetChunkIndex, targetRow))
            : default;
        return true;
    }

    internal uint GetEntityIndex(ChunkRow location)
    {
        ResolveOrThrow(location, out int chunkIndex, out int row);
        Span<byte> bytes = _arena.GetChunkBytes(_chunks[chunkIndex].Handle);
        return GetEntityIndices(bytes)[row];
    }

    internal bool Contains(ChunkRow location, uint expectedEntityIndex)
    {
        ThrowIfDisposed();
        if (!TryResolve(location, out int chunkIndex, out int row))
            return false;

        Span<byte> bytes = _arena.GetChunkBytes(_chunks[chunkIndex].Handle);
        return GetEntityIndices(bytes)[row] == expectedEntityIndex;
    }

    internal void CopyColumnsTo(
        ChunkRow sourceLocation,
        ArchetypeChunkStore<TArena> destination,
        ChunkRow destinationLocation,
        ReadOnlySpan<ArchetypeColumnTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ResolveOrThrow(sourceLocation, out int sourceChunkIndex, out int sourceRow);
        destination.ResolveOrThrow(
            destinationLocation,
            out int destinationChunkIndex,
            out int destinationRow);
        Span<byte> sourceBytes = _arena.GetChunkBytes(_chunks[sourceChunkIndex].Handle);
        Span<byte> destinationBytes = destination._arena.GetChunkBytes(
            destination._chunks[destinationChunkIndex].Handle);

        for (int i = 0; i < transfers.Length; i++)
        {
            ArchetypeColumnTransfer transfer = transfers[i];
            ref readonly ComponentColumnLayout sourceColumn =
                ref _columns[transfer.SourceColumnIndex];
            ref readonly ComponentColumnLayout destinationColumn =
                ref destination._columns[transfer.DestinationColumnIndex];
            System.Diagnostics.Debug.Assert(sourceColumn.ComponentId == transfer.ComponentId);
            System.Diagnostics.Debug.Assert(destinationColumn.ComponentId == transfer.ComponentId);
            System.Diagnostics.Debug.Assert(sourceColumn.ElementSizeBytes == destinationColumn.ElementSizeBytes);
            System.Diagnostics.Debug.Assert(sourceColumn.HasEnableBits == destinationColumn.HasEnableBits);

            if (sourceColumn.HasData)
            {
                int sourceOffset = checked(
                    sourceColumn.DataOffsetBytes + (sourceRow * sourceColumn.ElementSizeBytes));
                int destinationOffset = checked(
                    destinationColumn.DataOffsetBytes + (destinationRow * destinationColumn.ElementSizeBytes));
                sourceBytes.Slice(sourceOffset, sourceColumn.ElementSizeBytes)
                    .CopyTo(destinationBytes.Slice(destinationOffset, destinationColumn.ElementSizeBytes));
            }
            if (sourceColumn.HasEnableBits)
            {
                bool enabled = EnableBitOperations.Get(sourceBytes, sourceColumn, sourceRow);
                EnableBitOperations.Set(destinationBytes, destinationColumn, destinationRow, enabled);
            }
        }
    }

    internal int GetColumnIndex(ComponentId componentId)
    {
        ThrowIfDisposed();
        for (int i = 0; i < _columns.Length; i++)
        {
            if (_columns[i].ComponentId == componentId)
                return i;
        }

        return -1;
    }

    internal ref T GetComponentRef<T>(ChunkRow location, in ComponentColumnLayout column)
        where T : unmanaged
    {
        int columnIndex = GetOwnedColumnIndex(column);
        return ref GetComponentRef<T>(location, columnIndex);
    }

    internal ref T GetComponentRef<T>(ChunkRow location, int columnIndex)
        where T : unmanaged
    {
        ResolveOrThrow(location, out int chunkIndex, out int row);
        ref readonly ComponentColumnLayout column = ref GetDataColumn(columnIndex);
        Span<T> values = ChunkMemoryAccess.GetColumn<T>(
            _arena.GetChunkBytes(_chunks[chunkIndex].Handle),
            _layout,
            column);
        return ref values[row];
    }

    internal bool IsEnabled(ChunkRow location, in ComponentColumnLayout column)
    {
        int columnIndex = GetOwnedColumnIndex(column);
        return IsEnabled(location, columnIndex);
    }

    internal bool IsEnabled(ChunkRow location, int columnIndex)
    {
        ResolveOrThrow(location, out int chunkIndex, out int row);
        ref readonly ComponentColumnLayout column = ref GetEnableColumn(columnIndex);
        return EnableBitOperations.Get(_arena.GetChunkBytes(_chunks[chunkIndex].Handle), column, row);
    }

    internal void SetEnabled(ChunkRow location, in ComponentColumnLayout column, bool enabled)
    {
        int columnIndex = GetOwnedColumnIndex(column);
        SetEnabled(location, columnIndex, enabled);
    }

    internal void SetEnabled(ChunkRow location, int columnIndex, bool enabled)
    {
        ResolveOrThrow(location, out int chunkIndex, out int row);
        ref readonly ComponentColumnLayout column = ref GetEnableColumn(columnIndex);
        EnableBitOperations.Set(_arena.GetChunkBytes(_chunks[chunkIndex].Handle), column, row, enabled);
    }

    internal int GetRowCount(uint chunkIndex)
    {
        ThrowIfDisposed();
        if (chunkIndex >= (uint)_chunks.Length || _chunks[chunkIndex].State == ChunkListState.Unallocated)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        return _chunks[chunkIndex].RowCount;
    }

    internal ChunkHandle GetChunkHandle(uint chunkIndex)
    {
        ThrowIfDisposed();
        if (chunkIndex >= (uint)_chunks.Length || _chunks[chunkIndex].State == ChunkListState.Unallocated)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        return _chunks[chunkIndex].Handle;
    }

    internal ArchetypeChunkSnapshot GetSnapshot() => new(
        _entityCount,
        _layout.RowCapacity,
        _chunks.Length,
        _acquiredChunkCount,
        _fullChunkCount,
        _partialChunkCount,
        _emptyChunkCount,
        Unsafe.SizeOf<ChunkRecord>(),
        _disposed);

    internal void ValidateInvariants()
    {
        int entities = 0;
        int acquired = 0;
        int full = 0;
        int partial = 0;
        int empty = 0;
        for (int index = 0; index < _chunks.Length; index++)
        {
            ref readonly ChunkRecord chunk = ref _chunks[index];
            switch (chunk.State)
            {
                case ChunkListState.Unallocated:
                    if (chunk.Handle.IsValid || chunk.RowCount != 0)
                        ThrowInvariant("An unallocated chunk contains state.");
                    break;
                case ChunkListState.Empty:
                    acquired++;
                    empty++;
                    if (!chunk.Handle.IsValid || chunk.RowCount != 0)
                        ThrowInvariant("An empty chunk has an invalid handle or nonzero rows.");
                    break;
                case ChunkListState.Partial:
                    acquired++;
                    partial++;
                    entities += chunk.RowCount;
                    if (!chunk.Handle.IsValid || chunk.RowCount <= 0 || chunk.RowCount >= _layout.RowCapacity)
                        ThrowInvariant("A partial chunk has invalid occupancy.");
                    break;
                case ChunkListState.Full:
                    acquired++;
                    full++;
                    entities += chunk.RowCount;
                    if (!chunk.Handle.IsValid || chunk.RowCount != _layout.RowCapacity)
                        ThrowInvariant("A full chunk has invalid occupancy.");
                    break;
                default:
                    ThrowInvariant("A chunk has an unknown list state.");
                    break;
            }
        }

        if (partial > 1 ||
            entities != _entityCount ||
            acquired != _acquiredChunkCount ||
            full != _fullChunkCount ||
            partial != _partialChunkCount ||
            empty != _emptyChunkCount)
        {
            ThrowInvariant("Archetype chunk counters or packed occupancy are inconsistent.");
        }

        ValidateList(_fullHead, ChunkListState.Full, full);
        ValidateList(_partialHead, ChunkListState.Partial, partial);
        ValidateList(_emptyHead, ChunkListState.Empty, empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (int i = 0; i < _chunks.Length; i++)
        {
            ChunkHandle handle = _chunks[i].Handle;
            if (handle.IsValid)
                _arena.TryRelease(handle);
        }

        _arena.Dispose();
        _disposed = true;
    }

    private int AcquireDestinationChunk()
    {
        if (_partialHead != NoChunk)
            return _partialHead;

        int chunkIndex = _emptyHead;
        if (chunkIndex == NoChunk)
        {
            if (!_arena.TryAcquire(out ChunkHandle handle))
                return NoChunk;

            chunkIndex = checked((int)handle.Index);
            ref ChunkRecord acquired = ref _chunks[chunkIndex];
            if (acquired.State != ChunkListState.Unallocated || acquired.Handle.IsValid)
                throw new InvalidOperationException("The exclusive chunk arena returned a duplicate slot.");
            acquired.Handle = handle;
            _acquiredChunkCount++;
            AddToList(chunkIndex, ChunkListState.Empty);
        }

        MoveChunk(chunkIndex, ChunkListState.Partial);
        return chunkIndex;
    }

    private void InitializeRows(Span<byte> chunk, int startRow, int count)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            ComponentColumnLayout column = _columns[i];
            if (column.HasData)
            {
                int offset = checked(column.DataOffsetBytes + (startRow * column.ElementSizeBytes));
                int length = checked(count * column.ElementSizeBytes);
                chunk.Slice(offset, length).Clear();
            }
            if (column.HasEnableBits)
                EnableBitOperations.SetRange(chunk, column, startRow, count, enabled: true);
        }
    }

    private void CopyRow(Span<byte> source, int sourceRow, Span<byte> destination, int destinationRow)
    {
        GetEntityIndices(source)[sourceRow..(sourceRow + 1)]
            .CopyTo(GetEntityIndices(destination)[destinationRow..(destinationRow + 1)]);
        for (int i = 0; i < _columns.Length; i++)
        {
            ComponentColumnLayout column = _columns[i];
            if (column.HasData)
            {
                int sourceOffset = checked(column.DataOffsetBytes + (sourceRow * column.ElementSizeBytes));
                int destinationOffset = checked(column.DataOffsetBytes + (destinationRow * column.ElementSizeBytes));
                source.Slice(sourceOffset, column.ElementSizeBytes)
                    .CopyTo(destination.Slice(destinationOffset, column.ElementSizeBytes));
            }
            if (column.HasEnableBits)
            {
                bool enabled = EnableBitOperations.Get(source, column, sourceRow);
                EnableBitOperations.Set(destination, column, destinationRow, enabled);
            }
        }
    }

    private void ClearRow(Span<byte> chunk, int row)
    {
        GetEntityIndices(chunk)[row] = 0;
        for (int i = 0; i < _columns.Length; i++)
        {
            ComponentColumnLayout column = _columns[i];
            if (column.HasData)
            {
                int offset = checked(column.DataOffsetBytes + (row * column.ElementSizeBytes));
                chunk.Slice(offset, column.ElementSizeBytes).Clear();
            }
            if (column.HasEnableBits)
                EnableBitOperations.Set(chunk, column, row, enabled: false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<uint> GetEntityIndices(Span<byte> chunk) =>
        MemoryMarshal.Cast<byte, uint>(
            chunk.Slice(_layout.EntityIndexOffsetBytes, _layout.EntityIndexDataBytes));

    private bool TryResolve(ChunkRow location, out int chunkIndex, out int row)
    {
        if (!location.IsValid || location.ChunkIndex >= (uint)_chunks.Length)
        {
            chunkIndex = NoChunk;
            row = -1;
            return false;
        }

        chunkIndex = checked((int)location.ChunkIndex);
        row = location.Row;
        ChunkListState state = _chunks[chunkIndex].State;
        return (state == ChunkListState.Partial || state == ChunkListState.Full) &&
            (uint)row < (uint)_chunks[chunkIndex].RowCount;
    }

    private void ResolveOrThrow(ChunkRow location, out int chunkIndex, out int row)
    {
        ThrowIfDisposed();
        if (!TryResolve(location, out chunkIndex, out row))
            throw new ArgumentException("The chunk-row location is stale, empty, or invalid.", nameof(location));
    }

    private ref readonly ComponentColumnLayout GetDataColumn(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)_columns.Length || !_columns[columnIndex].HasData)
            throw new ArgumentOutOfRangeException(nameof(columnIndex), "The index does not identify a data column.");
        return ref _columns[columnIndex];
    }

    private ref readonly ComponentColumnLayout GetEnableColumn(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)_columns.Length || !_columns[columnIndex].HasEnableBits)
            throw new ArgumentOutOfRangeException(nameof(columnIndex), "The index does not identify an enableable column.");
        return ref _columns[columnIndex];
    }

    private int GetOwnedColumnIndex(in ComponentColumnLayout column)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (_columns[i].Equals(column))
                return i;
        }

        throw new ArgumentException("The physical column does not belong to this archetype layout.", nameof(column));
    }

    private void MoveChunk(int chunkIndex, ChunkListState destination)
    {
        RemoveFromList(chunkIndex);
        AddToList(chunkIndex, destination);
    }

    private void AddToList(int chunkIndex, ChunkListState state)
    {
        ref ChunkRecord chunk = ref _chunks[chunkIndex];
        if (chunk.State != ChunkListState.Unallocated)
            throw new InvalidOperationException("A chunk must be detached before entering another state list.");

        ref int head = ref GetHead(state);
        chunk.State = state;
        chunk.Previous = NoChunk;
        chunk.Next = head;
        if (head != NoChunk)
            _chunks[head].Previous = chunkIndex;
        head = chunkIndex;
        IncrementStateCount(state);
    }

    private void RemoveFromList(int chunkIndex)
    {
        ref ChunkRecord chunk = ref _chunks[chunkIndex];
        ChunkListState state = chunk.State;
        if (state == ChunkListState.Unallocated)
            return;

        ref int head = ref GetHead(state);
        if (chunk.Previous == NoChunk)
            head = chunk.Next;
        else
            _chunks[chunk.Previous].Next = chunk.Next;
        if (chunk.Next != NoChunk)
            _chunks[chunk.Next].Previous = chunk.Previous;

        chunk.State = ChunkListState.Unallocated;
        chunk.Next = NoChunk;
        chunk.Previous = NoChunk;
        DecrementStateCount(state);
    }

    private ref int GetHead(ChunkListState state)
    {
        if (state == ChunkListState.Full)
            return ref _fullHead;
        if (state == ChunkListState.Partial)
            return ref _partialHead;
        if (state == ChunkListState.Empty)
            return ref _emptyHead;
        throw new ArgumentOutOfRangeException(nameof(state));
    }

    private void IncrementStateCount(ChunkListState state)
    {
        if (state == ChunkListState.Full)
            _fullChunkCount++;
        else if (state == ChunkListState.Partial)
        {
            if (_partialChunkCount != 0)
                throw new InvalidOperationException("Packed archetype storage cannot contain two partial chunks.");
            _partialChunkCount++;
        }
        else if (state == ChunkListState.Empty)
            _emptyChunkCount++;
    }

    private void DecrementStateCount(ChunkListState state)
    {
        if (state == ChunkListState.Full)
            _fullChunkCount--;
        else if (state == ChunkListState.Partial)
            _partialChunkCount--;
        else if (state == ChunkListState.Empty)
            _emptyChunkCount--;
    }

    private void ValidateList(int head, ChunkListState state, int expectedCount)
    {
        int count = 0;
        int previous = NoChunk;
        int index = head;
        while (index != NoChunk)
        {
            if ((uint)index >= (uint)_chunks.Length || count >= _chunks.Length)
                ThrowInvariant("A chunk list is cyclic or out of range.");
            ref readonly ChunkRecord chunk = ref _chunks[index];
            if (chunk.State != state || chunk.Previous != previous)
                ThrowInvariant("A chunk list has inconsistent state or links.");
            previous = index;
            index = chunk.Next;
            count++;
        }

        if (count != expectedCount)
            ThrowInvariant("A chunk list length does not match its counter.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvariant(string message) => throw new InvalidOperationException(message);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ChunkRecord
    {
        internal ChunkHandle Handle;
        internal int RowCount;
        internal int Next;
        internal int Previous;
        internal ChunkListState State;
    }

    private enum ChunkListState : byte
    {
        Unallocated = 0,
        Empty = 1,
        Partial = 2,
        Full = 3,
    }
}
