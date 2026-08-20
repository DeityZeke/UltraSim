namespace UltraSim.Network
{
    public readonly struct HandshakeHelloPacket : IPacket<HandshakeHelloPacket>
    {
        public static ushort PacketId => 0x0001;
        public static ChannelType Channel => ChannelType.ReliableOrdered;

        public readonly uint ProtocolVersion;
        public readonly ulong SchemaHash;
        public readonly ulong ContentHash;
        public readonly FeatureFlags Features;

        public HandshakeHelloPacket(
            uint protocolVersion,
            ulong schemaHash,
            ulong contentHash,
            FeatureFlags features)
        {
            ProtocolVersion = protocolVersion;
            SchemaHash = schemaHash;
            ContentHash = contentHash;
            Features = features;
        }

        public void Serialize(ref BitWriter writer)
        {
            writer.WriteUInt32(ProtocolVersion);
            writer.WriteUInt64(SchemaHash);
            writer.WriteUInt64(ContentHash);
            writer.WriteUInt32((uint)Features);
        }

        public static HandshakeHelloPacket Deserialize(ref BitReader reader)
        {
            uint protocolVersion = reader.ReadUInt32();
            ulong schemaHash = reader.ReadUInt64();
            ulong contentHash = reader.ReadUInt64();
            FeatureFlags features = (FeatureFlags)reader.ReadUInt32();
            return new HandshakeHelloPacket(protocolVersion, schemaHash, contentHash, features);
        }
    }
}
