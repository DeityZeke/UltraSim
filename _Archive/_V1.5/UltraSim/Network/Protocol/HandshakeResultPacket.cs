namespace UltraSim.Network
{
    public readonly struct HandshakeResultPacket : IPacket<HandshakeResultPacket>
    {
        public static ushort PacketId => 0x0002;
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
            bool accepted = reader.ReadBool();
            HandshakeRejectReason rejectReason = (HandshakeRejectReason)reader.ReadByte();
            FeatureFlags missingFeatures = (FeatureFlags)reader.ReadUInt32();
            return new HandshakeResultPacket(accepted, rejectReason, missingFeatures);
        }
    }
}
