namespace UltraSim.Network;

public readonly record struct NetworkPacketHeader(
    ushort ProtocolVersion,
    NetworkMessageId MessageId,
    ushort MessageVersion,
    NetworkLane Lane,
    byte Flags,
    SecurityEpoch SecurityEpoch,
    PacketNumber PacketNumber,
    LaneSequence LaneSequence,
    ushort PayloadLength);

/// <summary>Canonical fixed packet framing shared by loopback and production transports.</summary>
public static class NetworkPacketFraming
{
    public const ushort Magic = 0x554E;
    public const int HeaderBytes = 32;

    public static bool TryWriteHeader(ref NetworkPacketWriter writer, in NetworkPacketHeader header)
    {
        if (header.ProtocolVersion == 0 || !header.MessageId.IsValid || header.MessageVersion == 0 ||
            (uint)header.Lane >= NetworkLaneContracts.Count || header.Flags != 0 || !header.SecurityEpoch.IsValid)
            return false;
        NetworkPacketWriter staged = writer;
        if (!staged.TryWriteUInt16(Magic) ||
            !staged.TryWriteUInt16(header.ProtocolVersion) ||
            !staged.TryWriteUInt16(header.MessageId.Value) ||
            !staged.TryWriteUInt16(header.MessageVersion) ||
            !staged.TryWriteByte((byte)header.Lane) ||
            !staged.TryWriteByte(header.Flags) ||
            !staged.TryWriteUInt16(header.PayloadLength) ||
            !staged.TryWriteUInt32(header.SecurityEpoch.Value) ||
            !staged.TryWriteUInt64(header.PacketNumber.Value) ||
            !staged.TryWriteUInt64(header.LaneSequence.Value)) return false;
        writer = staged;
        return true;
    }

    public static bool TryReadHeader(ref NetworkPacketReader reader, out NetworkPacketHeader header)
    {
        NetworkPacketReader staged = reader;
        if (!staged.TryReadUInt16(out ushort magic) || magic != Magic ||
            !staged.TryReadUInt16(out ushort protocol) || protocol == 0 ||
            !staged.TryReadUInt16(out ushort messageId) || messageId == 0 ||
            !staged.TryReadUInt16(out ushort messageVersion) || messageVersion == 0 ||
            !staged.TryReadByte(out byte lane) || lane >= NetworkLaneContracts.Count ||
            !staged.TryReadByte(out byte flags) || flags != 0 ||
            !staged.TryReadUInt16(out ushort payloadLength) ||
            !staged.TryReadUInt32(out uint securityEpoch) || securityEpoch == 0 ||
            !staged.TryReadUInt64(out ulong packetNumber) ||
            !staged.TryReadUInt64(out ulong laneSequence))
        {
            header = default;
            return false;
        }
        header = new(
            protocol, new NetworkMessageId(messageId), messageVersion, (NetworkLane)lane, flags,
            new SecurityEpoch(securityEpoch), new PacketNumber(packetNumber), new LaneSequence(laneSequence),
            payloadLength);
        reader = staged;
        return true;
    }

    public static bool TryReadExactPacket(
        ReadOnlySpan<byte> packet,
        out NetworkPacketHeader header,
        out ReadOnlySpan<byte> payload)
    {
        var reader = new NetworkPacketReader(packet);
        if (!TryReadHeader(ref reader, out header) || header.PayloadLength != reader.Remaining ||
            !reader.TryReadBytes(header.PayloadLength, out payload) || !reader.IsComplete)
        {
            header = default;
            payload = default;
            return false;
        }
        return true;
    }
}
