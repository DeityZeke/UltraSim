namespace UltraSim.Network;

public readonly record struct UiActionRequestPacket(byte[] DocumentBytes) : IPacket<UiActionRequestPacket>
{
    public static ushort PacketId => 0x0401;
    public static NetworkPlane Plane => NetworkPlane.Input;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        ReadOnlySpan<byte> bytes = DocumentBytes ?? [];
        writer.WriteUInt16(checked((ushort)bytes.Length));
        writer.WriteBytes(bytes);
    }

    public static UiActionRequestPacket Deserialize(ref BitReader reader)
    {
        ushort length = reader.ReadUInt16();
        return new UiActionRequestPacket(reader.ReadBytes(length).ToArray());
    }
}
