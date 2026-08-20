using System.Buffers;

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
        int capacity = Math.Max(PacketEnvelopeHeader.SizeInBytes + 1, initialCapacity);
        while (true)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                var payloadWriter = new BitWriter(buffer.AsSpan(PacketEnvelopeHeader.SizeInBytes));
                packet.Serialize(ref payloadWriter);

                var headerWriter = new BitWriter(buffer);
                new PacketEnvelopeHeader(
                    TPacket.PacketId,
                    flags,
                    checked((ushort)payloadWriter.Position))
                    .Serialize(ref headerWriter);

                return ImmutablePayloadBuffer.FromPooled(buffer, PacketEnvelopeHeader.SizeInBytes + payloadWriter.Position);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
            {
                ArrayPool<byte>.Shared.Return(buffer);
                capacity *= 2;
            }
        }
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

        byte[] buffer = ArrayPool<byte>.Shared.Rent(PacketEnvelopeHeader.SizeInBytes + payload.Length);
        var writer = new BitWriter(buffer);
        writer.WriteUInt16(packetId);
        writer.WriteByte((byte)flags);
        writer.WriteUInt16((ushort)payload.Length);
        writer.WriteBytes(payload.Span);
        return ImmutablePayloadBuffer.FromPooled(buffer, writer.Position);
    }

    public static ImmutablePayloadBuffer EncodeRealtimeStateFrameEnvelope(
        ulong sequence,
        ulong itemKey,
        ImmutablePayloadBuffer payload,
        PacketHeaderFlags flags,
        int initialCapacity = DefaultInitialCapacity)
    {
        ArgumentNullException.ThrowIfNull(payload);
        int payloadCapacity = sizeof(ulong) + sizeof(ulong) + sizeof(ushort) + payload.Length;
        int capacity = PacketEnvelopeHeader.SizeInBytes + payloadCapacity;
        while (true)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                var payloadWriter = new BitWriter(buffer.AsSpan(PacketEnvelopeHeader.SizeInBytes));
                payloadWriter.WriteUInt64(sequence);
                payloadWriter.WriteUInt64(itemKey);
                payloadWriter.WriteUInt16(checked((ushort)payload.Length));
                payloadWriter.WriteBytes(payload.Span);

                var headerWriter = new BitWriter(buffer);
                new PacketEnvelopeHeader(
                    RealtimeStateFramePacket.PacketId,
                    flags,
                    checked((ushort)payloadWriter.Position))
                    .Serialize(ref headerWriter);

                return ImmutablePayloadBuffer.FromPooled(buffer, PacketEnvelopeHeader.SizeInBytes + payloadWriter.Position);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
            {
                ArrayPool<byte>.Shared.Return(buffer);
                capacity *= 2;
            }
        }
    }

    public static ImmutablePayloadBuffer EncodeReliableEventFrameEnvelope(
        ulong sequence,
        ImmutablePayloadBuffer payload,
        PacketHeaderFlags flags = PacketHeaderFlags.None,
        int initialCapacity = DefaultInitialCapacity)
    {
        ArgumentNullException.ThrowIfNull(payload);
        int payloadCapacity = sizeof(ulong) + sizeof(ushort) + payload.Length;
        int capacity = PacketEnvelopeHeader.SizeInBytes + payloadCapacity;
        while (true)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                var payloadWriter = new BitWriter(buffer.AsSpan(PacketEnvelopeHeader.SizeInBytes));
                payloadWriter.WriteUInt64(sequence);
                payloadWriter.WriteUInt16(checked((ushort)payload.Length));
                payloadWriter.WriteBytes(payload.Span);

                var headerWriter = new BitWriter(buffer);
                new PacketEnvelopeHeader(
                    ReliableEventFramePacket.PacketId,
                    flags,
                    checked((ushort)payloadWriter.Position))
                    .Serialize(ref headerWriter);

                return ImmutablePayloadBuffer.FromPooled(buffer, PacketEnvelopeHeader.SizeInBytes + payloadWriter.Position);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
            {
                ArrayPool<byte>.Shared.Return(buffer);
                capacity *= 2;
            }
        }
    }

    public static ImmutablePayloadBuffer EncodeRealtimeStateBatchEnvelope(
        ReadOnlySpan<ScheduledWorkItem> items,
        PacketHeaderFlags flags,
        int initialCapacity = DefaultInitialCapacity)
    {
        if (items.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(items));

        int payloadCapacity = sizeof(ushort);
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly ScheduledWorkItem item = ref items[i];
            payloadCapacity += sizeof(ulong) + sizeof(ulong) + sizeof(ushort) + item.Payload.Length;
        }

        int capacity = PacketEnvelopeHeader.SizeInBytes + payloadCapacity;
        while (true)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                var payloadWriter = new BitWriter(buffer.AsSpan(PacketEnvelopeHeader.SizeInBytes));
                payloadWriter.WriteUInt16(checked((ushort)items.Length));
                for (int i = 0; i < items.Length; i++)
                {
                    ref readonly ScheduledWorkItem item = ref items[i];
                    payloadWriter.WriteUInt64(item.Sequence);
                    payloadWriter.WriteUInt64(item.ItemKey);
                    payloadWriter.WriteUInt16(checked((ushort)item.Payload.Length));
                    payloadWriter.WriteBytes(item.Payload.Span);
                }

                var headerWriter = new BitWriter(buffer);
                new PacketEnvelopeHeader(
                    RealtimeStateBatchPacket.PacketId,
                    flags,
                    checked((ushort)payloadWriter.Position))
                    .Serialize(ref headerWriter);

                return ImmutablePayloadBuffer.FromPooled(buffer, PacketEnvelopeHeader.SizeInBytes + payloadWriter.Position);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("BitWriter overflow:", StringComparison.Ordinal))
            {
                ArrayPool<byte>.Shared.Return(buffer);
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
