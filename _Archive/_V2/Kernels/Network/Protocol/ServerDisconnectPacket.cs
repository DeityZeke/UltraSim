namespace UltraSim.Network;

public readonly struct ServerDisconnectPacket : IPacket<ServerDisconnectPacket>
{
    public static ushort PacketId => 0x0012;
    public static NetworkPlane Plane => NetworkPlane.Control;
    public static PacketClass PacketClass => PacketClass.Control;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly SessionDisconnectReason Reason;

    public ServerDisconnectPacket(SessionDisconnectReason reason)
    {
        Reason = reason;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteByte((byte)Reason);
    }

    public static ServerDisconnectPacket Deserialize(ref BitReader reader)
    {
        return new ServerDisconnectPacket((SessionDisconnectReason)reader.ReadByte());
    }
}
