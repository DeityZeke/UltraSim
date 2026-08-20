namespace UltraSim.Network;

public readonly struct StateBaselineCompletePacket : IPacket<StateBaselineCompletePacket>
{
    public static ushort PacketId => 0x0106;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Bulk;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly uint BaselineId;

    public StateBaselineCompletePacket(uint baselineId)
    {
        BaselineId = baselineId;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt32(BaselineId);
    }

    public static StateBaselineCompletePacket Deserialize(ref BitReader reader)
    {
        return new StateBaselineCompletePacket(reader.ReadUInt32());
    }
}
