using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity, aligned native chunk slab. Structural acquire/release is single-owner;
/// callers may process disjoint live chunk spans in parallel after a barrier.
/// </summary>
internal sealed unsafe class NativeChunkArena : IDisposable
{
    private readonly ChunkSlotPool _slots;
    private void* _baseAddress;
    private bool _disposed;
    private long _releasedPayloadBytes;

    public NativeChunkArena(ChunkArenaCapacity capacity)
    {
        if (!capacity.IsValid)
            throw new ArgumentException("Chunk arena capacity is invalid or default initialized.", nameof(capacity));

        ulong addressableMaximum = Environment.Is64BitProcess ? (ulong)long.MaxValue : int.MaxValue;
        if ((ulong)capacity.TotalPayloadBytes > addressableMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity.TotalPayloadBytes,
                "Native arena payload exceeds the process addressable range.");
        }

        Capacity = capacity;
        _slots = new ChunkSlotPool(ChunkArenaId.Create(), capacity.MaximumChunks);
        _baseAddress = NativeMemory.AlignedAlloc(
            checked((nuint)capacity.TotalPayloadBytes),
            (nuint)capacity.ChunkAlignmentBytes);
        if (_baseAddress is null)
            throw new OutOfMemoryException($"Unable to reserve {capacity.TotalPayloadBytes} aligned native bytes.");
    }

    ~NativeChunkArena()
    {
        Dispose(disposing: false);
    }

    public ChunkArenaId ArenaId => _slots.ArenaId;

    public ChunkArenaCapacity Capacity { get; }

    public bool IsDisposed => _disposed;

    public nuint BaseAddress
    {
        get
        {
            ThrowIfDisposed();
            return (nuint)_baseAddress;
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
        ChunkArenaMemoryKind.NativeAligned,
        Capacity,
        _slots.AliveCount,
        _slots.RetiredCount,
        _slots.HighWatermark,
        _disposed,
        _disposed ? 0 : Capacity.TotalPayloadBytes,
        _releasedPayloadBytes);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        _ = disposing;
        if (_disposed)
            return;

        if (_baseAddress is not null)
        {
            NativeMemory.AlignedFree(_baseAddress);
            _baseAddress = null;
            _releasedPayloadBytes = Capacity.TotalPayloadBytes;
        }

        _disposed = true;
    }

    private Span<byte> GetChunkBytesUnchecked(uint index)
    {
        nint offset = checked((nint)((long)index * Capacity.ChunkSizeBytes));
        return new Span<byte>((byte*)_baseAddress + offset, Capacity.ChunkSizeBytes);
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
