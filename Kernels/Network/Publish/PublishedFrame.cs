namespace UltraSim.Network;

public readonly record struct PublishedFrame(
    ushort PacketId,
    NetworkPlane Plane,
    PacketClass PacketClass,
    ChannelType Channel,
    PacketHeaderFlags DefaultHeaderFlags,
    ImmutablePayloadBuffer Body);
