namespace UltraSim.Network;

public static class SharedFramePublisher
{
    public static PublishedFrame PublishLatestState(ulong sequence, ulong itemKey, ImmutablePayloadBuffer payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ImmutablePayloadBuffer body = PacketCodec.EncodePayload(new RealtimeStateFramePacket(sequence, itemKey, payload));
        return new PublishedFrame(
            RealtimeStateFramePacket.PacketId,
            RealtimeStateFramePacket.Plane,
            RealtimeStateFramePacket.PacketClass,
            RealtimeStateFramePacket.Channel,
            PacketHeaderFlags.HighPriorityHint,
            body);
    }

    public static PublishedFrame PublishOrderedEvent(ulong sequence, ImmutablePayloadBuffer payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ImmutablePayloadBuffer body = PacketCodec.EncodePayload(new ReliableEventFramePacket(sequence, payload));
        return new PublishedFrame(
            ReliableEventFramePacket.PacketId,
            ReliableEventFramePacket.Plane,
            ReliableEventFramePacket.PacketClass,
            ReliableEventFramePacket.Channel,
            PacketHeaderFlags.None,
            body);
    }
}
