namespace UltraSim.Network;

public static class PacketCodec
{
    private const int DefaultInitialCapacity = 256;

    public static ImmutablePayloadBuffer Encode<TPacket>(
        in TPacket packet,
        PacketHeaderFlags flags = PacketHeaderFlags.None,
        int initialCapacity = DefaultInitialCapacity)
        where TPacket : struct, IPacket<TPacket>
    {
        ImmutablePayloadBuffer payload = EncodePayload(packet, initialCapacity);
        return EncodeEnvelope(TPacket.PacketId, payload, flags);
    }

    public static ImmutablePayloadBuffer EncodePayload<TPacket>(
        in TPacket packet,
        int initialCapacity = DefaultInitialCapacity)
        where TPacket : struct, IPacket<TPacket>
    {
        int capacity = Math.Max(1, initialCapacity - PacketEnvelopeHeader.SizeInBytes);
        while (true)
        {
            byte[] buffer = new byte[capacity];
            try
            {
                var writer = new BitWriter(buffer);
                packet.Serialize(ref writer);
                Array.Resize(ref buffer, writer.Position);
                return ImmutablePayloadBuffer.FromOwned(buffer);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
            {
                capacity *= 2;
            }
        }
    }

    public static ImmutablePayloadBuffer EncodeEnvelope(
        ushort packetId,
        ImmutablePayloadBuffer payload,
        PacketHeaderFlags flags = PacketHeaderFlags.None)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if ((uint)payload.Length > ushort.MaxValue)
            throw new InvalidOperationException($"Packet 0x{packetId:X4} exceeds ushort payload length.");

        byte[] buffer = new byte[PacketEnvelopeHeader.SizeInBytes + payload.Length];
        var writer = new BitWriter(buffer);
        writer.WriteUInt16(packetId);
        writer.WriteByte((byte)flags);
        writer.WriteUInt16((ushort)payload.Length);
        writer.WriteBytes(payload.Span);
        return ImmutablePayloadBuffer.FromOwned(buffer);
    }

    public static bool TryDecodeEnvelope(
        ReadOnlySpan<byte> source,
        out PacketEnvelopeHeader header,
        out ReadOnlySpan<byte> payload)
    {
        header = default;
        payload = default;

        if (source.Length < PacketEnvelopeHeader.SizeInBytes)
            return false;

        var reader = new BitReader(source);
        header = PacketEnvelopeHeader.Deserialize(ref reader);
        if (reader.Remaining < header.PayloadLength)
            return false;

        payload = reader.ReadBytes(header.PayloadLength);
        return true;
    }
}
