using System.Buffers;
using System.Threading;

namespace UltraSim.Network;

/// <summary>
/// Owned inbound payload buffer. UDP can back this with pooled storage, while loopback/shared
/// paths can wrap existing immutable memory without taking ownership.
/// </summary>
public sealed class InboundPayloadBuffer : IDisposable
{
    private sealed class PooledLease
    {
        private byte[]? _buffer;
        private int _retainCount;

        public PooledLease(byte[] buffer, int retainCount)
        {
            _buffer = buffer;
            _retainCount = retainCount;
        }

        public void AddRef()
        {
            Interlocked.Increment(ref _retainCount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _retainCount) != 0)
                return;

            byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static readonly InboundPayloadBuffer s_empty = new(ReadOnlyMemory<byte>.Empty, null);
    private readonly ReadOnlyMemory<byte> _memory;
    private PooledLease? _pooledLease;
    private ImmutablePayloadBuffer? _retainedPayload;

    private InboundPayloadBuffer(ReadOnlyMemory<byte> memory, PooledLease? pooledLease, ImmutablePayloadBuffer? retainedPayload = null)
    {
        _memory = memory;
        _pooledLease = pooledLease;
        _retainedPayload = retainedPayload;
    }

    public static InboundPayloadBuffer Empty => s_empty;

    public int Length => _memory.Length;
    public ReadOnlyMemory<byte> Memory => _memory;
    public ReadOnlySpan<byte> Span => _memory.Span;

    public static InboundPayloadBuffer FromShared(ReadOnlyMemory<byte> memory)
    {
        return memory.Length == 0 ? Empty : new InboundPayloadBuffer(memory, null);
    }

    public static InboundPayloadBuffer FromRetained(ImmutablePayloadBuffer payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
            return Empty;

        payload.AddRef();
        return new InboundPayloadBuffer(payload.Memory, null, payload);
    }

    public static InboundPayloadBuffer CopyFromPooled(ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
            return Empty;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(source.Length);
        source.CopyTo(buffer);
        return new InboundPayloadBuffer(new ReadOnlyMemory<byte>(buffer, 0, source.Length), new PooledLease(buffer, retainCount: 1));
    }

    public static InboundPayloadBuffer FromPooled(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)length > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return Empty;
        }

        return new InboundPayloadBuffer(new ReadOnlyMemory<byte>(buffer, 0, length), new PooledLease(buffer, retainCount: 1));
    }

    public static InboundPayloadBuffer Slice(InboundPayloadBuffer owner, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (owner._pooledLease is null)
            throw new InvalidOperationException("Only pooled inbound buffers support zero-copy slicing.");
        if ((uint)offset > (uint)owner._memory.Length || (uint)length > (uint)(owner._memory.Length - offset))
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length == 0)
            return Empty;

        owner._pooledLease.AddRef();
        return new InboundPayloadBuffer(owner._memory.Slice(offset, length), owner._pooledLease);
    }

    public void Dispose()
    {
        PooledLease? pooledLease = Interlocked.Exchange(ref _pooledLease, null);
        pooledLease?.Release();
        ImmutablePayloadBuffer? retainedPayload = Interlocked.Exchange(ref _retainedPayload, null);
        retainedPayload?.Release();
    }
}
