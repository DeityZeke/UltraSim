namespace UltraSim.Network;

public readonly struct HandshakeHelloPacket : IPacket<HandshakeHelloPacket>
{
    public static ushort PacketId => 0x0001;
    public static NetworkPlane Plane => NetworkPlane.Control;
    public static PacketClass PacketClass => PacketClass.Control;
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
        return new HandshakeHelloPacket(
            reader.ReadUInt32(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            (FeatureFlags)reader.ReadUInt32());
    }
}
