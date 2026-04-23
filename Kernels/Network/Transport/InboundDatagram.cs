namespace UltraSim.Network;

/// <summary>
/// Inbound transport unit delivered during a poll window.
/// </summary>
public readonly record struct InboundDatagram : IDisposable
{
    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        ReadOnlyMemory<byte> payload)
        : this(connectionId, plane, packetClass, channel, InboundPayloadBuffer.FromShared(payload))
    {
    }

    public InboundDatagram(
        ConnectionId connectionId,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        InboundPayloadBuffer payload)
    {
        ConnectionId = connectionId;
        Plane = plane;
        PacketClass = packetClass;
        Channel = channel;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public ConnectionId ConnectionId { get; }
    public NetworkPlane Plane { get; }
    public PacketClass PacketClass { get; }
    public ChannelType Channel { get; }
    public InboundPayloadBuffer Payload { get; }

    public void Dispose()
    {
        Payload.Dispose();
    }
}
