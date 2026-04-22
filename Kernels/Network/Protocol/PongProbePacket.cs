namespace UltraSim.Network;

public readonly struct PongProbePacket : IPacket<PongProbePacket>
{
    public static ushort PacketId => 0x0011;
    public static NetworkPlane Plane => NetworkPlane.Control;
    public static PacketClass PacketClass => PacketClass.Control;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly ushort Sequence;
    public readonly uint ClientTick;
    public readonly uint ServerTick;

    public PongProbePacket(ushort sequence, uint clientTick, uint serverTick)
    {
        Sequence = sequence;
        ClientTick = clientTick;
        ServerTick = serverTick;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt16(Sequence);
        writer.WriteUInt32(ClientTick);
        writer.WriteUInt32(ServerTick);
    }

    public static PongProbePacket Deserialize(ref BitReader reader)
    {
        return new PongProbePacket(
            reader.ReadUInt16(),
            reader.ReadUInt32(),
            reader.ReadUInt32());
    }
}
