namespace UltraSim.Network;

/// <summary>
/// Immutable outbound payload buffer.
/// Once created, callers can only observe it through read-only memory.
/// </summary>
public sealed class ImmutablePayloadBuffer
{
    private static readonly ImmutablePayloadBuffer s_empty = new(Array.Empty<byte>());
    private readonly byte[] _buffer;

    private ImmutablePayloadBuffer(byte[] buffer)
    {
        _buffer = buffer;
    }

    public static ImmutablePayloadBuffer Empty => s_empty;

    public int Length => _buffer.Length;

    public ReadOnlyMemory<byte> Memory => _buffer;

    public ReadOnlySpan<byte> Span => _buffer;

    public static ImmutablePayloadBuffer CopyFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
            return Empty;

        return new ImmutablePayloadBuffer(source.ToArray());
    }

    public static ImmutablePayloadBuffer FromOwned(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0)
            return Empty;

        return new ImmutablePayloadBuffer(buffer);
    }
}
