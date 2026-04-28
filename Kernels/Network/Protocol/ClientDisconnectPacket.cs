namespace UltraSim.Network;

public readonly struct ClientDisconnectPacket : IPacket<ClientDisconnectPacket>
{
    public static ushort PacketId => 0x0013;
    public static NetworkPlane Plane => NetworkPlane.Control;
    public static PacketClass PacketClass => PacketClass.Control;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly SessionDisconnectReason Reason;

    public ClientDisconnectPacket(SessionDisconnectReason reason)
    {
        Reason = reason;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteByte((byte)Reason);
    }

    public static ClientDisconnectPacket Deserialize(ref BitReader reader)
    {
        return new ClientDisconnectPacket((SessionDisconnectReason)reader.ReadByte());
    }
}
