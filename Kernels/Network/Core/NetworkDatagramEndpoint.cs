namespace UltraSim.Network;

/// <summary>
/// Owner-thread datagram seam consumed by the secure protocol machine.
/// </summary>
/// <remarks>
/// The protocol owns plaintext/ciphertext buffers; an endpoint owns byte
/// movement only. Implementations must not retain spans passed to
/// <see cref="TrySend"/>. <see cref="DatagramSendStatus.Accepted"/> means that
/// the endpoint accepted a private copy; queued endpoints may submit that copy
/// to the kernel later. Protocol delivery remains proven only by its ACK rules.
/// </remarks>
internal interface INetworkDatagramEndpoint
{
    DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram);

    bool TryReceive(Span<byte> destination, out int length);
}
