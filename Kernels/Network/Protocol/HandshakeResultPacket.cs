namespace UltraSim.Network;

public readonly struct HandshakeResultPacket : IPacket<HandshakeResultPacket>
{
    public static ushort PacketId => 0x0002;
    public static NetworkPlane Plane => NetworkPlane.Control;
    public static PacketClass PacketClass => PacketClass.Control;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public readonly bool Accepted;
    public readonly HandshakeRejectReason RejectReason;
    public readonly FeatureFlags MissingFeatures;

    public HandshakeResultPacket(
        bool accepted,
        HandshakeRejectReason rejectReason = HandshakeRejectReason.None,
        FeatureFlags missingFeatures = FeatureFlags.None)
    {
        Accepted = accepted;
        RejectReason = rejectReason;
        MissingFeatures = missingFeatures;
    }

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteBool(Accepted);
        writer.WriteByte((byte)RejectReason);
        writer.WriteUInt32((uint)MissingFeatures);
    }

    public static HandshakeResultPacket Deserialize(ref BitReader reader)
    {
        return new HandshakeResultPacket(
            reader.ReadBool(),
            (HandshakeRejectReason)reader.ReadByte(),
            (FeatureFlags)reader.ReadUInt32());
    }
}
