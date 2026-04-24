namespace UltraSim.Network;

public readonly record struct ChatMessageEventPacket(
    ulong MessageId,
    ushort ChannelId,
    ulong SenderEntityId,
    string SenderName,
    string Text,
    uint ServerTick,
    ChatMessageFlags Flags) : IPacket<ChatMessageEventPacket>
{
    public static ushort PacketId => 0x0301;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt64(MessageId);
        writer.WriteUInt16(ChannelId);
        writer.WriteUInt64(SenderEntityId);
        writer.WriteString(SenderName ?? string.Empty);
        writer.WriteString(Text ?? string.Empty);
        writer.WriteUInt32(ServerTick);
        writer.WriteUInt16((ushort)Flags);
    }

    public static ChatMessageEventPacket Deserialize(ref BitReader reader)
    {
        return new ChatMessageEventPacket(
            reader.ReadUInt64(),
            reader.ReadUInt16(),
            reader.ReadUInt64(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadUInt32(),
            (ChatMessageFlags)reader.ReadUInt16());
    }
}
