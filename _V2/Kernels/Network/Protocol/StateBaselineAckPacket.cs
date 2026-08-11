namespace UltraSim.Network;

/// <summary>
/// Acknowledges that a full baseline snapshot has been completely applied on the client.
/// </summary>
public readonly struct StateBaselineAckPacket : IPacket<StateBaselineAckPacket>
{
    public static ushort PacketId => 0x0107;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Bulk;
    public static ChannelType Channel => ChannelType.UnreliableSequenced;

    public readonly StateBaselineDescriptor Descriptor;

    public StateBaselineAckPacket(StateBaselineDescriptor descriptor)
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

    public static StateBaselineAckPacket Deserialize(ref BitReader reader)
    {
        return new StateBaselineAckPacket(new StateBaselineDescriptor(
            BaselineId: reader.ReadUInt32(),
            HighestLatestStateSequence: reader.ReadUInt64(),
            LastOrderedEventSequence: reader.ReadUInt64(),
            ItemCount: reader.ReadUInt16()));
    }
}
