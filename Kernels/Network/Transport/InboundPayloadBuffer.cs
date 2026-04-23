using System.Buffers;
using System.Threading;

namespace UltraSim.Network;

/// <summary>
/// Owned inbound payload buffer. UDP can back this with pooled storage, while loopback/shared
/// paths can wrap existing immutable memory without taking ownership.
/// </summary>
public sealed class InboundPayloadBuffer : IDisposable
{
    private static readonly InboundPayloadBuffer s_empty = new(ReadOnlyMemory<byte>.Empty, null);
    private readonly ReadOnlyMemory<byte> _memory;
    private byte[]? _pooledBuffer;
    private ImmutablePayloadBuffer? _retainedPayload;

    private InboundPayloadBuffer(ReadOnlyMemory<byte> memory, byte[]? pooledBuffer, ImmutablePayloadBuffer? retainedPayload = null)
    {
        _memory = memory;
        _pooledBuffer = pooledBuffer;
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
        return new InboundPayloadBuffer(new ReadOnlyMemory<byte>(buffer, 0, source.Length), buffer);
    }

    public void Dispose()
    {
        byte[]? pooledBuffer = Interlocked.Exchange(ref _pooledBuffer, null);
        if (pooledBuffer is not null)
            ArrayPool<byte>.Shared.Return(pooledBuffer);
        ImmutablePayloadBuffer? retainedPayload = Interlocked.Exchange(ref _retainedPayload, null);
        retainedPayload?.Release();

        GC.SuppressFinalize(this);
    }

    ~InboundPayloadBuffer()
    {
        Dispose();
    }
}
