namespace UltraSim.Network;

public readonly record struct NetworkKernelSnapshot(
    int MaximumConnections,
    int ActiveConnections,
    int PacketCapacity,
    int RentedPackets,
    int InboundQueued,
    int OutboundQueued,
    ulong ProtocolFingerprint,
    long ReservedBytes);

/// <summary>
/// Owns one isolated bounded Network mechanism instance before protocol semantics are attached.
/// </summary>
public sealed class NetworkKernel : IDisposable
{
    private readonly int _ownerThreadId;
    private bool _disposed;

    public NetworkKernel(NetworkKernelLimits limits = default, NetworkProtocolManifest? manifest = null)
    {
        if (limits == default) limits = NetworkKernelLimits.Default;
        limits.Validate();
        manifest ??= CoreNetworkProtocol.Manifest;
        if (manifest.Count > limits.MaximumProtocolMessages)
            throw new ArgumentException("The protocol manifest exceeds configured message capacity.", nameof(manifest));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        Limits = limits;
        Manifest = manifest;
        Connections = new NetworkConnectionTable(limits.MaximumConnections);
        Packets = new PacketBufferPool(limits.PacketBufferCount, limits.PacketBufferBytes);
        Inbound = new ProducerOwnedRing<PacketBufferToken>(limits.InboundRingCapacity, _ownerThreadId, _ownerThreadId);
        Outbound = new ProducerOwnedRing<PacketBufferToken>(limits.OutboundRingCapacity, _ownerThreadId, _ownerThreadId);
    }

    public NetworkKernelLimits Limits { get; }
    public NetworkProtocolManifest Manifest { get; }
    public NetworkConnectionTable Connections { get; }
    public PacketBufferPool Packets { get; }
    public ProducerOwnedRing<PacketBufferToken> Inbound { get; }
    public ProducerOwnedRing<PacketBufferToken> Outbound { get; }

    public NetworkKernelSnapshot GetSnapshot()
    {
        EnsureOperational();
        NetworkConnectionSnapshot connections = Connections.GetSnapshot();
        PacketBufferPoolSnapshot packets = Packets.GetSnapshot();
        return new(
            connections.Capacity,
            connections.Active,
            packets.Capacity,
            packets.Rented,
            Inbound.Count,
            Outbound.Count,
            Manifest.Fingerprint,
            checked(connections.ReservedBytes + packets.ReservedBytes + Inbound.ReservedBytes + Outbound.ReservedBytes));
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        if (Inbound.Count != 0 || Outbound.Count != 0)
            throw new InvalidOperationException("Network rings must be drained before kernel disposal.");
        if (Connections.GetSnapshot().Active != 0)
            throw new InvalidOperationException("Every connection must be closed and released before kernel disposal.");
        Packets.Dispose();
        _disposed = true;
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NetworkKernel));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Network kernel is bound to its creating owner thread.");
    }
}
