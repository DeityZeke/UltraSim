using System.Buffers;
using System.Threading;

namespace UltraSim.Network;

/// <summary>
/// Immutable outbound payload buffer.
/// Once created, callers can only observe it through read-only memory.
/// </summary>
public sealed class ImmutablePayloadBuffer
{
    private static readonly ImmutablePayloadBuffer s_empty = new(ReadOnlyMemory<byte>.Empty, null);
    private readonly ReadOnlyMemory<byte> _memory;
    private byte[]? _pooledBuffer;
    private int _retainCount;

    private ImmutablePayloadBuffer(ReadOnlyMemory<byte> memory, byte[]? pooledBuffer)
    {
        _memory = memory;
        _pooledBuffer = pooledBuffer;
    }

    public static ImmutablePayloadBuffer Empty => s_empty;

    public int Length => _memory.Length;

    public ReadOnlyMemory<byte> Memory => _memory;

    public ReadOnlySpan<byte> Span => _memory.Span;

    public static ImmutablePayloadBuffer CopyFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
            return Empty;

        return new ImmutablePayloadBuffer(source.ToArray(), null);
    }

    public static ImmutablePayloadBuffer FromOwned(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0)
            return Empty;

        return new ImmutablePayloadBuffer(buffer, null);
    }

    public static ImmutablePayloadBuffer FromPooled(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)length > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return Empty;
        }

        return new ImmutablePayloadBuffer(new ReadOnlyMemory<byte>(buffer, 0, length), buffer);
    }

    public void AddRef()
    {
        if (_pooledBuffer is null)
            return;

        Interlocked.Increment(ref _retainCount);
    }

    public void Release()
    {
        if (_pooledBuffer is null)
            return;
        if (Interlocked.Decrement(ref _retainCount) != 0)
            return;

        byte[]? pooledBuffer = Interlocked.Exchange(ref _pooledBuffer, null);
        if (pooledBuffer is not null)
            ArrayPool<byte>.Shared.Return(pooledBuffer);
    }
}
