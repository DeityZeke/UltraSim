namespace UltraSim.Network;

/// <summary>
/// Minimal authoritative movement-state packet for the first client/server movement slice.
/// </summary>
public readonly record struct MovementStatePacket(
    uint NetworkEntityId,
    ulong ServerTick,
    ushort LastProcessedInputSequence,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY) : IPacket<MovementStatePacket>
{
    public static ushort PacketId => 0x0201;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.Realtime;
    public static ChannelType Channel => ChannelType.UnreliableSequenced;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt32(NetworkEntityId);
        writer.WriteUInt64(ServerTick);
        writer.WriteUInt16(LastProcessedInputSequence);
        writer.WriteSingle(PositionX);
        writer.WriteSingle(PositionY);
        writer.WriteSingle(VelocityX);
        writer.WriteSingle(VelocityY);
    }

    public static MovementStatePacket Deserialize(ref BitReader reader)
    {
        return new MovementStatePacket(
            reader.ReadUInt32(),
            reader.ReadUInt64(),
            reader.ReadUInt16(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle());
    }
}
