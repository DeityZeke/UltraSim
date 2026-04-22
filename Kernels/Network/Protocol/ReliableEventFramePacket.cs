namespace UltraSim.Network;

public readonly struct ReliableEventFramePacket : IPacket<ReliableEventFramePacket>
{
    public static ushort PacketId => 0x0102;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly ulong Sequence;
    public readonly ImmutablePayloadBuffer Payload;

    public ReliableEventFramePacket(ulong sequence, ImmutablePayloadBuffer payload)
    {
        Sequence = sequence;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt64(Sequence);
        writer.WriteUInt16(checked((ushort)Payload.Length));
        writer.WriteBytes(Payload.Span);
    }

    public static ReliableEventFramePacket Deserialize(ref BitReader reader)
    {
        ulong sequence = reader.ReadUInt64();
        ushort payloadLength = reader.ReadUInt16();
        ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom(reader.ReadBytes(payloadLength));
        return new ReliableEventFramePacket(sequence, payload);
    }
}
