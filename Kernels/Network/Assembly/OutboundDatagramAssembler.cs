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
            PublishedFrame frame = item.Kind switch
            {
                ScheduledWorkKind.LatestState => SharedFramePublisher.PublishLatestState(item.Sequence, item.ItemKey, item.Payload),
                ScheduledWorkKind.OrderedEvent => SharedFramePublisher.PublishOrderedEvent(item.Sequence, item.Payload),
                _ => throw new InvalidOperationException($"Unsupported scheduled work kind: {item.Kind}."),
            };

            OutboundAssemblyResult singleResult = AssemblePublished(
                connectionId,
                frame,
                destination[count..],
                allowCompression,
                allowSegmentation);
            if (singleResult.Count == 0)
                break;

            count += singleResult.Count;
            compressed += singleResult.CompressedCount;
            encrypted += singleResult.EncryptedCount;
        }

        return new OutboundAssemblyResult(count, compressed, encrypted);
    }

    public OutboundAssemblyResult AssemblePublished(
        ConnectionId connectionId,
        in PublishedFrame frame,
        Span<OutboundDatagram> destination,
        bool allowCompression = true,
        bool allowSegmentation = true)
    {
        if (destination.Length == 0)
            return default;

        var context = new OutboundTransformContext(
            connectionId,
            frame.Plane,
            frame.PacketClass,
            frame.Channel,
            AllowCompression: allowCompression,
            AllowSegmentation: allowSegmentation);

        ImmutablePayloadBuffer transformedBody = frame.Body;
        PacketHeaderFlags transformedFlags = frame.DefaultHeaderFlags;
        for (int transformIndex = 0; transformIndex < _transforms.Length; transformIndex++)
        {
            OutboundTransformResult result = _transforms[transformIndex].Transform(transformedBody, transformedFlags, context);
            transformedBody = result.Payload;
            transformedFlags = result.HeaderFlags;
        }

        ImmutablePayloadBuffer envelope = PacketCodec.EncodeEnvelope(frame.PacketId, transformedBody, transformedFlags);
        destination[0] = new OutboundDatagram(
            connectionId,
            frame.Plane,
            frame.PacketClass,
            frame.Channel,
            envelope);
        return new OutboundAssemblyResult(
            Count: 1,
            CompressedCount: (transformedFlags & PacketHeaderFlags.Compressed) != 0 ? 1 : 0,
            EncryptedCount: (transformedFlags & PacketHeaderFlags.Encrypted) != 0 ? 1 : 0);
    }

    public OutboundAssemblyResult AssembleFanout(
        ReadOnlySpan<ConnectionId> connections,
        in PublishedFrame frame,
        Span<OutboundDatagram> destination,
        bool allowCompression = true,
        bool allowSegmentation = true)
    {
        int count = 0;
        int compressed = 0;
        int encrypted = 0;

        for (int i = 0; i < connections.Length && count < destination.Length; i++)
        {
            OutboundAssemblyResult singleResult = AssemblePublished(
                connections[i],
                frame,
                destination[count..],
                allowCompression,
                allowSegmentation);
            if (singleResult.Count == 0)
                break;

            count += singleResult.Count;
            compressed += singleResult.CompressedCount;
            encrypted += singleResult.EncryptedCount;
        }

        return new OutboundAssemblyResult(count, compressed, encrypted);
    }
}
