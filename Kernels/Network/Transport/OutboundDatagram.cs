namespace UltraSim.Network;

/// <summary>
/// Outbound transport unit owned by exactly one recipient connection.
/// Shared payload fanout is handled before transport by reusing the same immutable payload.
/// </summary>
public readonly record struct OutboundDatagram(
    ConnectionId ConnectionId,
    NetworkPlane Plane,
    PacketClass PacketClass,
    ChannelType Channel,
    ImmutablePayloadBuffer Payload);
