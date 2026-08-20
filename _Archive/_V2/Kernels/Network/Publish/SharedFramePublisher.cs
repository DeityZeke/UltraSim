namespace UltraSim.Network;

public static class SharedFramePublisher
{
    public static PublishedFrame Publish(in ScheduledWorkItem item)
    {
        return item.Kind switch
        {
            ScheduledWorkKind.LatestState => PublishLatestState(item.Sequence, item.ItemKey, item.Payload, item.PacketClass),
            ScheduledWorkKind.OrderedEvent => PublishOrderedEvent(item.Sequence, item.Payload, item.PacketClass),
            _ => throw new InvalidOperationException($"Unsupported scheduled work kind: {item.Kind}."),
        };
    }

    public static PublishedFrame PublishLatestState(ulong sequence, ulong itemKey, ImmutablePayloadBuffer payload, PacketClass packetClass = PacketClass.Realtime)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Latest-state frames only support Realtime or Bulk packet classes.");

        ImmutablePayloadBuffer body = PacketCodec.EncodePayload(new RealtimeStateFramePacket(sequence, itemKey, payload));
        return new PublishedFrame(
            RealtimeStateFramePacket.PacketId,
            RealtimeStateFramePacket.Plane,
            packetClass,
            packetClass == PacketClass.Bulk ? ChannelType.ReliableOrdered : RealtimeStateFramePacket.Channel,
            packetClass == PacketClass.Bulk ? PacketHeaderFlags.None : PacketHeaderFlags.HighPriorityHint,
            body);
    }

    public static PublishedFrame PublishOrderedEvent(ulong sequence, ImmutablePayloadBuffer payload, PacketClass packetClass = PacketClass.ReliableEvent)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (packetClass is not PacketClass.ReliableEvent and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Ordered-event frames only support ReliableEvent or Bulk packet classes.");

        ImmutablePayloadBuffer body = PacketCodec.EncodePayload(new ReliableEventFramePacket(sequence, payload));
        return new PublishedFrame(
            ReliableEventFramePacket.PacketId,
            ReliableEventFramePacket.Plane,
            packetClass,
            ReliableEventFramePacket.Channel,
            PacketHeaderFlags.None,
            body);
    }
}
