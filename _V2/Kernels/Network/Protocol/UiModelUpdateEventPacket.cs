namespace UltraSim.Network;

public readonly record struct UiModelUpdateEventPacket(byte[] DocumentBytes) : IPacket<UiModelUpdateEventPacket>
{
    public static ushort PacketId => 0x0403;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        ReadOnlySpan<byte> bytes = DocumentBytes ?? [];
        writer.WriteUInt16(checked((ushort)bytes.Length));
        writer.WriteBytes(bytes);
    }

    public static UiModelUpdateEventPacket Deserialize(ref BitReader reader)
    {
        ushort length = reader.ReadUInt16();
        return new UiModelUpdateEventPacket(reader.ReadBytes(length).ToArray());
    }
}
