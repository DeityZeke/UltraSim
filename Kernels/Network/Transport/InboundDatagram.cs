namespace UltraSim.Network;

/// <summary>
/// Inbound transport unit delivered during a poll window.
/// </summary>
public readonly record struct InboundDatagram(
    ConnectionId ConnectionId,
    NetworkPlane Plane,
    PacketClass PacketClass,
    ChannelType Channel,
    ReadOnlyMemory<byte> Payload);
