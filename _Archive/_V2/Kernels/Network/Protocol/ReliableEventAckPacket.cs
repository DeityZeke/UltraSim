namespace UltraSim.Network;

/// <summary>
/// Acknowledges the highest contiguous reliable-event sequence a client has applied.
/// This is intentionally separate from movement/input acks so later snapshot/baseline
/// work can build on an explicit ordered-event delivery seam.
/// </summary>
public readonly struct ReliableEventAckPacket : IPacket<ReliableEventAckPacket>
{
    public static ushort PacketId => 0x0103;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.UnreliableSequenced;

    public readonly ulong Sequence;

    public ReliableEventAckPacket(ulong sequence)
    {
        Sequence = sequence;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt64(Sequence);
    }

    public static ReliableEventAckPacket Deserialize(ref BitReader reader)
    {
        return new ReliableEventAckPacket(reader.ReadUInt64());
    }
}
