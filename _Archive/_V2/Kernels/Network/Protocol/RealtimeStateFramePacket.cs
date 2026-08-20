namespace UltraSim.Network;

public readonly struct RealtimeStateFramePacket : IPacket<RealtimeStateFramePacket>
{
    public static ushort PacketId => 0x0101;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Realtime;
    public static ChannelType Channel => ChannelType.UnreliableSequenced;

    public readonly ulong Sequence;
    public readonly ulong ItemKey;
    public readonly ImmutablePayloadBuffer Payload;

    public RealtimeStateFramePacket(ulong sequence, ulong itemKey, ImmutablePayloadBuffer payload)
    {
        Sequence = sequence;
        ItemKey = itemKey;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt64(Sequence);
        writer.WriteUInt64(ItemKey);
        writer.WriteUInt16(checked((ushort)Payload.Length));
        writer.WriteBytes(Payload.Span);
    }

    public static RealtimeStateFramePacket Deserialize(ref BitReader reader)
    {
        ulong sequence = reader.ReadUInt64();
        ulong itemKey = reader.ReadUInt64();
        ushort payloadLength = reader.ReadUInt16();
        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom(reader.ReadBytes(payloadLength));
        return new RealtimeStateFramePacket(sequence, itemKey, payload);
    }
}
