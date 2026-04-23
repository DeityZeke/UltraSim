namespace UltraSim.Network;

public readonly record struct RealtimeStateBatchItem(
    ulong Sequence,
    ulong ItemKey,
    ImmutablePayloadBuffer Payload);

public readonly struct RealtimeStateBatchPacket : IPacket<RealtimeStateBatchPacket>
{
    public static ushort PacketId => 0x0109;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Realtime;
    public static ChannelType Channel => ChannelType.UnreliableSequenced;

    public readonly RealtimeStateBatchItem[] Items;

    public RealtimeStateBatchPacket(RealtimeStateBatchItem[] items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt16(checked((ushort)Items.Length));
        for (int i = 0; i < Items.Length; i++)
        {
            RealtimeStateBatchItem item = Items[i];
            writer.WriteUInt64(item.Sequence);
            writer.WriteUInt64(item.ItemKey);
            writer.WriteUInt16(checked((ushort)item.Payload.Length));
            writer.WriteBytes(item.Payload.Span);
        }
    }

    public static RealtimeStateBatchPacket Deserialize(ref BitReader reader)
    {
        ushort count = reader.ReadUInt16();
        var items = new RealtimeStateBatchItem[count];
        for (int i = 0; i < count; i++)
        {
            ulong sequence = reader.ReadUInt64();
            ulong itemKey = reader.ReadUInt64();
            ushort payloadLength = reader.ReadUInt16();
            ImmutablePayloadBuffer payload = ImmutablePayloadBuffer.CopyFrom(reader.ReadBytes(payloadLength));
            items[i] = new RealtimeStateBatchItem(sequence, itemKey, payload);
        }

        return new RealtimeStateBatchPacket(items);
    }
}
