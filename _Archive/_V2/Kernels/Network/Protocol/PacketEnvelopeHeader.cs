namespace UltraSim.Network;

/// <summary>
/// Base wire envelope for packet payloads.
/// This stays intentionally lane-neutral. Session and reliable sequencing
/// metadata belong in higher-level control/reliable runtime layers.
/// </summary>
public readonly record struct PacketEnvelopeHeader(
    ushort PacketId,
    PacketHeaderFlags Flags,
    ushort PayloadLength)
{
    public const int SizeInBytes = 5;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt16(PacketId);
        writer.WriteByte((byte)Flags);
        writer.WriteUInt16(PayloadLength);
    }

    public static PacketEnvelopeHeader Deserialize(ref BitReader reader)
    {
        return new PacketEnvelopeHeader(
            reader.ReadUInt16(),
            (PacketHeaderFlags)reader.ReadByte(),
            reader.ReadUInt16());
    }
}
