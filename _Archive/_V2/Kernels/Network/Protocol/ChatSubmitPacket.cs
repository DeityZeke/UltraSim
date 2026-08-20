namespace UltraSim.Network;

public readonly record struct ChatSubmitPacket(string Text) : IPacket<ChatSubmitPacket>
{
    public static ushort PacketId => 0x0300;
    public static NetworkPlane Plane => NetworkPlane.Input;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteString(Text ?? string.Empty);
    }

    public static ChatSubmitPacket Deserialize(ref BitReader reader)
    {
        return new ChatSubmitPacket(reader.ReadString());
    }
}
