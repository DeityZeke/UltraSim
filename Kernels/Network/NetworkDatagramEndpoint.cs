namespace UltraSim.Network;

/// <summary>
/// Owner-thread datagram seam consumed by the secure protocol machine.
/// </summary>
/// <remarks>
/// The protocol owns plaintext/ciphertext buffers; an endpoint owns byte
/// movement only. Implementations must not retain spans passed to
/// <see cref="TrySend"/>.
/// </remarks>
internal interface INetworkDatagramEndpoint
{
    DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram);

    bool TryReceive(Span<byte> destination, out int length);
}
