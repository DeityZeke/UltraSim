using System.Runtime.CompilerServices;

namespace UltraSim.ECS.BitmapExperiment;

/// <summary>
/// Minimal fixed-capacity, single-owner entity/location shell shared by every
/// prototype policy. Structural or query calls must not overlap across threads;
/// concurrency belongs above this mechanism experiment at a deterministic barrier.
/// </summary>
public sealed class PrototypeWorld : IDisposable
{
    private const byte Dead = 0;
    private const byte Alive = 1;

    private readonly PrototypeArchetypeStore[] _stores;
    private readonly uint[] _generations;
    private readonly byte[] _states;
    private readonly byte[] _storeIds;
    private readonly int[] _rows;
    private readonly int[] _free;
    private readonly PrototypeRelocation[] _relocationScratch;
    private int _nextUnused;
    private int _freeCount;
    private int _aliveCount;
    private bool _disposed;

    public PrototypeWorld(
        PrototypeStorageKind kind,
        int entityCapacity,
        int storeCapacity,
        params int[] storeColumnCounts)
    {
        if (entityCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(entityCapacity));
        if (storeColumnCounts is null || storeColumnCounts.Length == 0 || storeColumnCounts.Length > byte.MaxValue)
            throw new ArgumentException("At least one store is required.", nameof(storeColumnCounts));

        _stores = new PrototypeArchetypeStore[storeColumnCounts.Length];
        for (int index = 0; index < _stores.Length; index++)
            _stores[index] = new PrototypeArchetypeStore(kind, storeCapacity, storeColumnCounts[index]);
        _generations = new uint[entityCapacity];
        _states = new byte[entityCapacity];
        _storeIds = new byte[entityCapacity];
        _rows = new int[entityCapacity];
        _free = new int[entityCapacity];
        _relocationScratch = new PrototypeRelocation[storeCapacity];
        Reset();
    }

    public PrototypeStorageKind Kind => _stores[0].Kind;

    public int AliveCount => _aliveCount;

    public int StoreCount => _stores.Length;

    public long PayloadBytes => _stores.Sum(static store => store.PayloadBytes);

    public long OccupancyMetadataBytes => _stores.Sum(static store => store.OccupancyMetadataBytes);

    public long RowEntityMetadataBytes => _stores.Sum(static store => store.RowEntityMetadataBytes);

    public PrototypeArchetypeStore GetStore(int storeId) => _stores[storeId];

    public void Reset()
    {
        ThrowIfDisposed();
        foreach (PrototypeArchetypeStore store in _stores)
            store.Reset();
        Array.Clear(_generations);
        Array.Clear(_states);
        Array.Clear(_storeIds);
        Array.Fill(_rows, -1);
        _nextUnused = 0;
        _freeCount = 0;
        _aliveCount = 0;
    }

    public bool TryCreate(int storeId, ulong seed, out PrototypeEntity entity, bool bulk = false)
    {
        ThrowIfDisposed();
        if (unchecked((uint)storeId) >= unchecked((uint)_stores.Length))
            throw new ArgumentOutOfRangeException(nameof(storeId));
        if (_freeCount == 0 && _nextUnused == _states.Length)
        {
            entity = PrototypeEntity.Invalid;
            return false;
        }

        bool reusedFreeSlot = _freeCount != 0;
        int index = reusedFreeSlot ? _free[--_freeCount] : _nextUnused++;
        uint previousGeneration = _generations[index];
        uint generation = _generations[index];
        if (generation == 0)
            generation = _generations[index] = 1;
        if (!_stores[storeId].TryAdd(index, seed, bulk, out int row))
        {
            _generations[index] = previousGeneration;
            if (reusedFreeSlot)
                _free[_freeCount++] = index;
            else
                _nextUnused--;
            entity = PrototypeEntity.Invalid;
            return false;
        }

        _states[index] = Alive;
        _storeIds[index] = checked((byte)storeId);
        _rows[index] = row;
        _aliveCount++;
        entity = new PrototypeEntity(index, generation);
        return true;
    }

    public bool TryDestroy(PrototypeEntity entity)
    {
        ThrowIfDisposed();
        if (!TryResolve(entity, out int storeId, out int row))
            return false;
        if (!_stores[storeId].TryRemove(row, entity.Index, out PrototypeRelocation relocation))
            throw new InvalidOperationException("A validated prototype location was rejected by its store.");
        ApplyRelocation(storeId, relocation);
        _states[entity.Index] = Dead;
        _rows[entity.Index] = -1;
        uint nextGeneration = unchecked(_generations[entity.Index] + 1U);
        _generations[entity.Index] = nextGeneration == 0 ? 1U : nextGeneration;
        _free[_freeCount++] = entity.Index;
        _aliveCount--;
        return true;
    }

    public bool TryTransition(
        PrototypeEntity entity,
        int destinationStoreId,
        int retainedColumns,
        bool bulk)
    {
        ThrowIfDisposed();
        if (!TryResolve(entity, out int sourceStoreId, out int sourceRow) ||
            sourceStoreId == destinationStoreId ||
            unchecked((uint)destinationStoreId) >= unchecked((uint)_stores.Length))
        {
            return false;
        }

        PrototypeArchetypeStore source = _stores[sourceStoreId];
        PrototypeArchetypeStore destination = _stores[destinationStoreId];
        if (!destination.TryAdd(entity.Index, seed: 0, bulk, out int destinationRow))
            return false;
        destination.CopyRetainedFrom(source, sourceRow, destinationRow, retainedColumns);
        _storeIds[entity.Index] = checked((byte)destinationStoreId);
        _rows[entity.Index] = destinationRow;
        if (!source.TryRemove(sourceRow, entity.Index, out PrototypeRelocation relocation))
            throw new InvalidOperationException("A transition source rejected a validated entity.");
        ApplyRelocation(sourceStoreId, relocation);
        return true;
    }

    public void BeginBulk(int storeId) => _stores[storeId].BeginBulkAllocation();

    public void EndBulk(int storeId) => _stores[storeId].EndBulkAllocation();

    public int EndStructuralBarrier(double adaptiveMinimumDensity = 0.90, int adaptiveMaximumPartialWords = 8)
    {
        ThrowIfDisposed();
        int totalRelocations = 0;
        for (int storeId = 0; storeId < _stores.Length; storeId++)
        {
            PrototypeArchetypeStore store = _stores[storeId];
            bool compact = store.Kind == PrototypeStorageKind.BitmapBarrierLocal ||
                store.ShouldCompact(adaptiveMinimumDensity, adaptiveMaximumPartialWords);
            if (!compact)
                continue;
            int count = store.Compact(_relocationScratch);
            for (int index = 0; index < count; index++)
                ApplyRelocation(storeId, _relocationScratch[index]);
            totalRelocations += count;
        }
        return totalRelocations;
    }

    public bool IsAlive(PrototypeEntity entity) =>
        entity.IsValid &&
        unchecked((uint)entity.Index) < unchecked((uint)_nextUnused) &&
        _states[entity.Index] == Alive &&
        _generations[entity.Index] == entity.Generation;

    public bool TryRead(PrototypeEntity entity, int column, out ulong value)
    {
        if (!TryResolve(entity, out int storeId, out int row) ||
            unchecked((uint)column) >= unchecked((uint)_stores[storeId].ColumnCount))
        {
            value = 0;
            return false;
        }
        value = _stores[storeId].Read(row, column);
        return true;
    }

    public bool TryWrite(PrototypeEntity entity, int column, ulong value)
    {
        if (!TryResolve(entity, out int storeId, out int row) ||
            unchecked((uint)column) >= unchecked((uint)_stores[storeId].ColumnCount))
        {
            return false;
        }
        _stores[storeId].Write(row, column, value);
        return true;
    }

    public ulong Traverse(int storeId, int queryColumns, out int visitedRows) =>
        _stores[storeId].Traverse(queryColumns, out visitedRows);

    public ulong TraverseAndMutate(int storeId, int queryColumns, out int visitedRows) =>
        _stores[storeId].Traverse(queryColumns, mutateFirstColumn: true, out visitedRows);

    public PrototypeTopology GetTopology(int storeId) => _stores[storeId].GetTopology();

    public ulong CaptureCanonicalHash()
    {
        ThrowIfDisposed();
        ulong hash = 1469598103934665603UL;
        for (int index = 0; index < _nextUnused; index++)
        {
            hash = Mix(hash, unchecked((ulong)(uint)index));
            hash = Mix(hash, _generations[index]);
            hash = Mix(hash, _states[index]);
            if (_states[index] != Alive)
                continue;
            int storeId = _storeIds[index];
            int row = _rows[index];
            hash = Mix(hash, unchecked((ulong)(uint)storeId));
            PrototypeArchetypeStore store = _stores[storeId];
            for (int column = 0; column < store.ColumnCount; column++)
                hash = Mix(hash, store.Read(row, column));
        }
        return Mix(hash, unchecked((ulong)(uint)_aliveCount));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        foreach (PrototypeArchetypeStore store in _stores)
            store.Dispose();
        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolve(PrototypeEntity entity, out int storeId, out int row)
    {
        if (!IsAlive(entity))
        {
            storeId = -1;
            row = -1;
            return false;
        }
        storeId = _storeIds[entity.Index];
        row = _rows[entity.Index];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyRelocation(int storeId, in PrototypeRelocation relocation)
    {
        if (!relocation.HasValue)
            return;
        if (_states[relocation.EntityIndex] != Alive ||
            _storeIds[relocation.EntityIndex] != storeId ||
            _rows[relocation.EntityIndex] != relocation.SourceRow)
        {
            throw new InvalidOperationException("Prototype donor relocation does not match entity location state.");
        }
        _rows[relocation.EntityIndex] = relocation.DestinationRow;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
