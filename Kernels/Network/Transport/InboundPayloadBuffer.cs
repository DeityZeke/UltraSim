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

    private InboundPayloadBuffer(ReadOnlyMemory<byte> memory, byte[]? pooledBuffer)
    {
        _memory = memory;
        _pooledBuffer = pooledBuffer;
    }

    public static InboundPayloadBuffer Empty => s_empty;

    public int Length => _memory.Length;
    public ReadOnlyMemory<byte> Memory => _memory;
    public ReadOnlySpan<byte> Span => _memory.Span;

    public static InboundPayloadBuffer FromShared(ReadOnlyMemory<byte> memory)
    {
        return memory.Length == 0 ? Empty : new InboundPayloadBuffer(memory, null);
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

        GC.SuppressFinalize(this);
    }

    ~InboundPayloadBuffer()
    {
        Dispose();
    }
}
