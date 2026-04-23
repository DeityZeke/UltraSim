namespace UltraSim.Network;

public readonly struct StateBaselineBeginPacket : IPacket<StateBaselineBeginPacket>
{
    public static ushort PacketId => 0x0104;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Bulk;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly StateBaselineDescriptor Descriptor;

    public StateBaselineBeginPacket(StateBaselineDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt32(Descriptor.BaselineId);
        writer.WriteUInt64(Descriptor.HighestLatestStateSequence);
        writer.WriteUInt64(Descriptor.LastOrderedEventSequence);
        writer.WriteUInt16(Descriptor.ItemCount);
    }

    public static StateBaselineBeginPacket Deserialize(ref BitReader reader)
    {
        return new StateBaselineBeginPacket(new StateBaselineDescriptor(
            BaselineId: reader.ReadUInt32(),
            HighestLatestStateSequence: reader.ReadUInt64(),
            LastOrderedEventSequence: reader.ReadUInt64(),
            ItemCount: reader.ReadUInt16()));
    }
}
