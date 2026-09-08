namespace UltraSim.Network;

/// <summary>Names the current exclusive owner of a packet-buffer token.</summary>
public enum PacketBufferOwner : byte
{
    Free = 0,
    Application = 1,
    Assembly = 2,
    Transport = 3,
    Receive = 4,
    Decode = 5,
}

public readonly record struct PacketBufferPoolSnapshot(
    int Capacity,
    int Available,
    int Rented,
    int Retired,
    long ReservedBytes);

/// <summary>Fixed packet/slab pool with generational tokens and explicit ownership transfer.</summary>
public sealed class PacketBufferPool : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly int _bufferBytes;
    private readonly byte[] _storage;
    private readonly uint[] _generations;
    private readonly PacketBufferOwner[] _owners;
    private readonly int[] _free;
    private int _freeCount;
    private int _retired;
    private bool _disposed;

    public PacketBufferPool(int bufferCount, int bufferBytes)
    {
        if (bufferCount <= 0) throw new ArgumentOutOfRangeException(nameof(bufferCount));
        if (bufferBytes is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(bufferBytes));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _bufferBytes = bufferBytes;
        _storage = new byte[checked(bufferCount * bufferBytes)];
        _generations = new uint[bufferCount];
        _owners = new PacketBufferOwner[bufferCount];
        _free = new int[bufferCount];
        for (int index = 0; index < bufferCount; index++) _free[index] = bufferCount - 1 - index;
        _freeCount = bufferCount;
    }

    public int BufferBytes => _bufferBytes;

    public bool TryRent(PacketBufferOwner owner, out PacketBufferToken token)
    {
        EnsureOperational();
        ValidateLiveOwner(owner);
        while (_freeCount != 0)
        {
            int slot = _free[--_freeCount];
            uint generation = _generations[slot];
            if (generation == uint.MaxValue)
            {
                _retired++;
                continue;
            }
            generation++;
            _generations[slot] = generation;
            _owners[slot] = owner;
            token = new PacketBufferToken((uint)slot, generation);
            GetStorageSpan(slot).Clear();
            return true;
        }
        token = default;
        return false;
    }

    public Span<byte> GetWritableSpan(PacketBufferToken token, PacketBufferOwner owner)
    {
        EnsureOperational();
        int slot = ValidateToken(token, owner);
        return GetStorageSpan(slot);
    }

    public ReadOnlySpan<byte> GetReadOnlySpan(PacketBufferToken token, PacketBufferOwner owner, int length)
    {
        EnsureOperational();
        int slot = ValidateToken(token, owner);
        if ((uint)length > (uint)_bufferBytes) throw new ArgumentOutOfRangeException(nameof(length));
        return GetStorageSpan(slot)[..length];
    }

    public bool TryTransfer(PacketBufferToken token, PacketBufferOwner expectedOwner, PacketBufferOwner newOwner)
    {
        EnsureOperational();
        ValidateLiveOwner(expectedOwner);
        ValidateLiveOwner(newOwner);
        if (!TryValidateToken(token, expectedOwner, out int slot)) return false;
        _owners[slot] = newOwner;
        return true;
    }

    public bool TryReturn(PacketBufferToken token, PacketBufferOwner owner)
    {
        EnsureOperational();
        ValidateLiveOwner(owner);
        if (!TryValidateToken(token, owner, out int slot)) return false;
        GetStorageSpan(slot).Clear();
        _owners[slot] = PacketBufferOwner.Free;
        _free[_freeCount++] = slot;
        return true;
    }

    public PacketBufferPoolSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        int rented = 0;
        foreach (PacketBufferOwner owner in _owners)
            if (owner != PacketBufferOwner.Free) rented++;
        return new(
            _owners.Length,
            _freeCount,
            rented,
            _retired,
            checked((long)_storage.Length + (long)_generations.Length * sizeof(uint) +
                _owners.Length + (long)_free.Length * sizeof(int)));
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        int outstanding = 0;
        foreach (PacketBufferOwner owner in _owners)
            if (owner != PacketBufferOwner.Free) outstanding++;
        if (outstanding != 0)
            throw new InvalidOperationException($"Cannot dispose packet pool with {outstanding} outstanding token(s).");
        _disposed = true;
        Array.Clear(_storage);
    }

    private Span<byte> GetStorageSpan(int slot) => _storage.AsSpan(slot * _bufferBytes, _bufferBytes);

    private int ValidateToken(PacketBufferToken token, PacketBufferOwner owner)
    {
        ValidateLiveOwner(owner);
        if (!TryValidateToken(token, owner, out int slot))
            throw new InvalidOperationException("The packet token is stale, foreign, returned, or owned by another stage.");
        return slot;
    }

    private bool TryValidateToken(PacketBufferToken token, PacketBufferOwner owner, out int slot)
    {
        if (!token.IsValid || token.Slot >= (uint)_owners.Length) { slot = -1; return false; }
        slot = (int)token.Slot;
        return
            _generations[slot] == token.Generation && _owners[slot] == owner;
    }

    private static void ValidateLiveOwner(PacketBufferOwner owner)
    {
        if (owner is <= PacketBufferOwner.Free or > PacketBufferOwner.Decode)
            throw new ArgumentOutOfRangeException(nameof(owner));
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(PacketBufferPool));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The packet pool is bound to its creating owner thread.");
    }
}
