namespace UltraSim.Network;

/// <summary>
/// Minimal client-to-server movement input packet for the first real movement vertical slice.
/// </summary>
public readonly record struct MovementInputPacket(
    ushort InputSequence,
    uint ClientTick,
    short MoveX,
    short MoveY,
    byte Flags) : IPacket<MovementInputPacket>
{
    public const byte SprintFlag = 1 << 0;
    public const byte JumpFlag = 1 << 1;

    public static ushort PacketId => 0x0200;
    public static NetworkPlane Plane => NetworkPlane.Input;
    public static PacketClass PacketClass => PacketClass.Realtime;
    public static ChannelType Channel => ChannelType.UnreliableSequenced;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt16(InputSequence);
        writer.WriteUInt32(ClientTick);
        writer.WriteInt16(MoveX);
        writer.WriteInt16(MoveY);
        writer.WriteByte(Flags);
    }

    public static MovementInputPacket Deserialize(ref BitReader reader)
    {
        return new MovementInputPacket(
            reader.ReadUInt16(),
            reader.ReadUInt32(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadByte());
    }
}
