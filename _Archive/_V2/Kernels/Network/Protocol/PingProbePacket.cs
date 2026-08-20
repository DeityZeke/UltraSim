namespace UltraSim.Network;

public readonly struct PingProbePacket : IPacket<PingProbePacket>
{
    public static ushort PacketId => 0x0010;
    public static NetworkPlane Plane => NetworkPlane.Control;
    public static PacketClass PacketClass => PacketClass.Control;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly ushort Sequence;
    public readonly uint ClientTick;

    public PingProbePacket(ushort sequence, uint clientTick)
    {
        Sequence = sequence;
        ClientTick = clientTick;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt16(Sequence);
        writer.WriteUInt32(ClientTick);
    }

    public static PingProbePacket Deserialize(ref BitReader reader)
    {
        return new PingProbePacket(
            reader.ReadUInt16(),
            reader.ReadUInt32());
    }
}
