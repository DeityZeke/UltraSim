namespace UltraSim.Network;

/// <summary>
/// Outbound transport unit owned by exactly one recipient connection.
/// Shared payload fanout is handled before transport by reusing the same immutable payload.
/// </summary>
public readonly record struct OutboundDatagram : IDisposable
{
    public OutboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        ImmutablePayloadBuffer payload)
    {
        ConnectionId = connectionId;
        Plane = plane;
        PacketClass = packetClass;
        Channel = channel;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Payload.AddRef();
    }

    public ConnectionId ConnectionId { get; }
    public NetworkPlane Plane { get; }
    public PacketClass PacketClass { get; }
    public ChannelType Channel { get; }
    public ImmutablePayloadBuffer Payload { get; }

    public void Dispose()
    {
        Payload.Release();
    }
}
