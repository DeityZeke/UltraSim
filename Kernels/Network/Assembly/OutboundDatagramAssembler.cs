namespace UltraSim.Network;

public sealed class OutboundDatagramAssembler
{
    private readonly IOutboundTransform[] _transforms;

    public OutboundDatagramAssembler(params IOutboundTransform[]? transforms)
    {
        _transforms = transforms ?? Array.Empty<IOutboundTransform>();
    }

    public OutboundAssemblyResult Assemble(
        ConnectionId connectionId,
        ReadOnlySpan<ScheduledWorkItem> items,
        Span<OutboundDatagram> destination,
        bool allowCompression = true,
        bool allowSegmentation = true)
    {
        int count = 0;
        int compressed = 0;
        int encrypted = 0;

        for (int i = 0; i < items.Length && count < destination.Length; i++)
        {
            ScheduledWorkItem item = items[i];
            PacketHeaderFlags flags = item.PacketClass == PacketClass.Realtime
                ? PacketHeaderFlags.HighPriorityHint
                : PacketHeaderFlags.None;

            ImmutablePayloadBuffer body;
            NetworkPlane plane;
            PacketClass packetClass;
            ChannelType channel;
            switch (item.Kind)
            {
                case ScheduledWorkKind.LatestState:
                    body = PacketCodec.EncodePayload(new RealtimeStateFramePacket(item.Sequence, item.ItemKey, item.Payload));
                    plane = RealtimeStateFramePacket.Plane;
                    packetClass = RealtimeStateFramePacket.PacketClass;
                    channel = RealtimeStateFramePacket.Channel;
                    break;
                case ScheduledWorkKind.OrderedEvent:
                    body = PacketCodec.EncodePayload(new ReliableEventFramePacket(item.Sequence, item.Payload));
                    plane = ReliableEventFramePacket.Plane;
                    packetClass = ReliableEventFramePacket.PacketClass;
                    channel = ReliableEventFramePacket.Channel;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported scheduled work kind: {item.Kind}.");
            }

            var context = new OutboundTransformContext(
                connectionId,
                plane,
                packetClass,
                channel,
                AllowCompression: allowCompression,
                AllowSegmentation: allowSegmentation);

            ImmutablePayloadBuffer transformedBody = body;
            PacketHeaderFlags transformedFlags = flags;
            for (int transformIndex = 0; transformIndex < _transforms.Length; transformIndex++)
            {
                OutboundTransformResult result = _transforms[transformIndex].Transform(transformedBody, transformedFlags, context);
                transformedBody = result.Payload;
                transformedFlags = result.HeaderFlags;
            }

            ImmutablePayloadBuffer envelope = item.Kind switch
            {
                ScheduledWorkKind.LatestState => PacketCodec.EncodeEnvelope(RealtimeStateFramePacket.PacketId, transformedBody, transformedFlags),
                ScheduledWorkKind.OrderedEvent => PacketCodec.EncodeEnvelope(ReliableEventFramePacket.PacketId, transformedBody, transformedFlags),
                _ => throw new InvalidOperationException($"Unsupported scheduled work kind: {item.Kind}."),
            };

            if ((transformedFlags & PacketHeaderFlags.Compressed) != 0)
                compressed++;
            if ((transformedFlags & PacketHeaderFlags.Encrypted) != 0)
                encrypted++;

            destination[count++] = new OutboundDatagram(
                connectionId,
                plane,
                packetClass,
                channel,
                envelope);
        }

        return new OutboundAssemblyResult(count, compressed, encrypted);
    }
}
