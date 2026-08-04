namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity pinned-managed chunk slab used as the managed storage comparison candidate.
/// </summary>
internal sealed unsafe class PinnedChunkArena : IChunkArenaBackend
{
    private readonly ChunkSlotPool _slots;
    private byte[]? _storage;
    private int _baseOffset;
    private nuint _baseAddress;
    private bool _disposed;
    private long _releasedPayloadBytes;

    public PinnedChunkArena(ChunkArenaCapacity capacity)
    {
        if (!capacity.IsValid)
            throw new ArgumentException("Chunk arena capacity is invalid or default initialized.", nameof(capacity));

        long allocationBytes = checked(capacity.TotalPayloadBytes + capacity.ChunkAlignmentBytes - 1L);
        if (allocationBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                allocationBytes,
                "Pinned arena payload plus alignment padding exceeds the runtime array limit.");
        }

        Capacity = capacity;
        _slots = new ChunkSlotPool(ChunkArenaId.Create(), capacity.MaximumChunks);
        _storage = GC.AllocateUninitializedArray<byte>((int)allocationBytes, pinned: true);
        fixed (byte* storage = _storage)
        {
            nuint address = (nuint)storage;
            nuint mask = (nuint)capacity.ChunkAlignmentBytes - 1;
            nuint aligned = (address + mask) & ~mask;
            _baseOffset = checked((int)(aligned - address));
            _baseAddress = aligned;
        }
    }

    public ChunkArenaId ArenaId => _slots.ArenaId;

    public ChunkArenaCapacity Capacity { get; }

    public bool IsDisposed => _disposed;

    public nuint BaseAddress
    {
        get
        {
            ThrowIfDisposed();
            return _baseAddress;
        }
    }

    public ChunkHandle Acquire()
    {
        ThrowIfDisposed();
        ChunkHandle handle = _slots.Acquire();
        GetChunkBytesUnchecked(handle.Index).Clear();
        return handle;
    }

    public bool TryAcquire(out ChunkHandle handle)
    {
        ThrowIfDisposed();
        if (!_slots.TryAcquire(out handle))
            return false;

        GetChunkBytesUnchecked(handle.Index).Clear();
        return true;
    }

    public bool TryRelease(ChunkHandle handle)
    {
        ThrowIfDisposed();
        return _slots.TryRelease(handle);
    }

    public bool IsAlive(ChunkHandle handle)
    {
        ThrowIfDisposed();
        return _slots.IsAlive(handle);
    }

    public Span<byte> GetChunkBytes(ChunkHandle handle)
    {
        ThrowIfDisposed();
        _slots.ValidateAlive(handle);
        return GetChunkBytesUnchecked(handle.Index);
    }

    public Span<uint> GetEntityIndices(ChunkHandle handle, in ChunkLayoutPlan plan)
    {
        ValidatePlan(plan);
        return ChunkMemoryAccess.GetEntityIndices(GetChunkBytes(handle), plan);
    }

    public Span<T> GetColumn<T>(
        ChunkHandle handle,
        in ChunkLayoutPlan plan,
        in ComponentColumnLayout column)
        where T : unmanaged
    {
        ValidatePlan(plan);
        return ChunkMemoryAccess.GetColumn<T>(GetChunkBytes(handle), plan, column);
    }

    public Span<ulong> GetEnableWords(
        ChunkHandle handle,
        in ChunkLayoutPlan plan,
        in ComponentColumnLayout column)
    {
        ValidatePlan(plan);
        return ChunkMemoryAccess.GetEnableWords(GetChunkBytes(handle), plan, column);
    }

    public void ClearChunk(ChunkHandle handle) => GetChunkBytes(handle).Clear();

    public ChunkArenaSnapshot GetSnapshot() => new(
        ArenaId,
        ChunkArenaMemoryKind.PinnedManaged,
        Capacity,
        _slots.AliveCount,
        _slots.RetiredCount,
        _slots.HighWatermark,
        _disposed,
        _disposed ? 0 : Capacity.TotalPayloadBytes,
        _releasedPayloadBytes);

    public void Dispose()
    {
        if (_disposed)
            return;

        _storage = null;
        _baseOffset = 0;
        _baseAddress = 0;
        _releasedPayloadBytes = Capacity.TotalPayloadBytes;
        _disposed = true;
    }

    private Span<byte> GetChunkBytesUnchecked(uint index)
    {
        byte[] storage = _storage ?? throw new ObjectDisposedException(nameof(PinnedChunkArena));
        int offset = checked(_baseOffset + ((int)index * Capacity.ChunkSizeBytes));
        return storage.AsSpan(offset, Capacity.ChunkSizeBytes);
    }

    private void ValidatePlan(in ChunkLayoutPlan plan)
    {
        ThrowIfDisposed();
        if (!Capacity.Supports(plan))
            throw new ArgumentException("Chunk layout is incompatible with this arena's size or alignment.", nameof(plan));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
