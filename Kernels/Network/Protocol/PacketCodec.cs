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
        if (initialCapacity < PacketEnvelopeHeader.SizeInBytes)
            initialCapacity = PacketEnvelopeHeader.SizeInBytes;

        int capacity = initialCapacity;
        while (true)
        {
            byte[] buffer = new byte[capacity];
            try
            {
                var writer = new BitWriter(buffer);
                writer.WriteUInt16(TPacket.PacketId);
                writer.WriteByte((byte)flags);
                writer.WriteUInt16(0); // patched below

                int payloadStart = writer.Position;
                packet.Serialize(ref writer);
                int payloadLength = writer.Position - payloadStart;
                if ((uint)payloadLength > ushort.MaxValue)
                    throw new InvalidOperationException($"Packet 0x{TPacket.PacketId:X4} exceeds ushort payload length.");

                buffer[3] = (byte)(payloadLength & 0xFF);
                buffer[4] = (byte)(payloadLength >> 8);
                Array.Resize(ref buffer, writer.Position);
                return ImmutablePayloadBuffer.FromOwned(buffer);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
            {
                capacity *= 2;
            }
        }
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
