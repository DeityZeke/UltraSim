namespace UltraSim.Network;

public enum UiDocumentChunkKind : byte
{
    PanelOpen = 1,
    ModelUpdate = 2,
}

public readonly record struct UiDocumentChunkEventPacket(
    UiDocumentChunkKind Kind,
    string TransferId,
    ushort ChunkIndex,
    ushort ChunkCount,
    byte[] ChunkBytes) : IPacket<UiDocumentChunkEventPacket>
{
    public static ushort PacketId => 0x0404;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        ReadOnlySpan<byte> bytes = ChunkBytes ?? [];
        writer.WriteByte((byte)Kind);
        writer.WriteString(TransferId ?? string.Empty);
        writer.WriteUInt16(ChunkIndex);
        writer.WriteUInt16(ChunkCount);
        writer.WriteUInt16(checked((ushort)bytes.Length));
        writer.WriteBytes(bytes);
    }

    public static UiDocumentChunkEventPacket Deserialize(ref BitReader reader)
    {
        var kind = (UiDocumentChunkKind)reader.ReadByte();
        string transferId = reader.ReadString();
        ushort chunkIndex = reader.ReadUInt16();
        ushort chunkCount = reader.ReadUInt16();
        ushort length = reader.ReadUInt16();
        return new UiDocumentChunkEventPacket(
            kind,
            transferId,
            chunkIndex,
            chunkCount,
            reader.ReadBytes(length).ToArray());
    }
}
