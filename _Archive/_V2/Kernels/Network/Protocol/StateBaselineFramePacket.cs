namespace UltraSim.Network;

public readonly struct StateBaselineFramePacket : IPacket<StateBaselineFramePacket>
{
    public static ushort PacketId => 0x0105;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Bulk;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly uint BaselineId;
    public readonly ulong ItemKey;
    public readonly ulong Sequence;
    public readonly ImmutablePayloadBuffer Payload;

    public StateBaselineFramePacket(uint baselineId, ulong itemKey, ulong sequence, ImmutablePayloadBuffer payload)
    {
        BaselineId = baselineId;
        ItemKey = itemKey;
        Sequence = sequence;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt32(BaselineId);
        writer.WriteUInt64(ItemKey);
        writer.WriteUInt64(Sequence);
        writer.WriteUInt16(checked((ushort)Payload.Length));
        writer.WriteBytes(Payload.Span);
    }

    public static StateBaselineFramePacket Deserialize(ref BitReader reader)
    {
        uint baselineId = reader.ReadUInt32();
        ulong itemKey = reader.ReadUInt64();
        ulong sequence = reader.ReadUInt64();
        ushort payloadLength = reader.ReadUInt16();
        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom(reader.ReadBytes(payloadLength));
        return new StateBaselineFramePacket(baselineId, itemKey, sequence, payload);
    }
}
