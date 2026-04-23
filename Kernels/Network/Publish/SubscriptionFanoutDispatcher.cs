namespace UltraSim.Network;

public sealed class SubscriptionFanoutDispatcher
{
    private const int MaxRealtimeBatchItems = 8;
    private const int MaxRealtimeBatchPayloadBytes = 1200;
    private readonly ConnectionSubscriptionRegistry _subscriptions;
    private readonly OutboundDatagramAssembler _assembler;
    private readonly bool _enableLatestStateBatching;

    public SubscriptionFanoutDispatcher(
        ConnectionSubscriptionRegistry subscriptions,
        OutboundDatagramAssembler assembler,
        bool enableLatestStateBatching = false)
    {
        _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
        _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
        _enableLatestStateBatching = enableLatestStateBatching;
    }

    public SubscriptionFanoutDispatchResult Dispatch(
        ReadOnlySpan<ScopedPublishedFrame> frames,
        Span<ConnectionId> subscriberScratch,
        Span<OutboundDatagram> destination,
        bool allowCompression = true,
        bool allowSegmentation = true)
    {
        if (!_assembler.HasTransforms)
            return DispatchWithoutTransforms(frames, destination);

        int frameCount = 0;
        int subscriberDeliveries = 0;
        int datagramCount = 0;
        int compressedCount = 0;
        int encryptedCount = 0;

        for (int i = 0; i < frames.Length && datagramCount < destination.Length; i++)
        {
            ScopedPublishedFrame scopedFrame = frames[i];
            int subscriberCount = _subscriptions.CopySubscribers(scopedFrame.ScopeKey, subscriberScratch);
            if (subscriberCount == 0)
                continue;

            frameCount++;
            subscriberDeliveries += subscriberCount;

            OutboundAssemblyResult assembly = _assembler.AssembleFanout(
                subscriberScratch[..subscriberCount],
                scopedFrame.Frame,
                destination[datagramCount..],
                allowCompression,
                allowSegmentation);

            datagramCount += assembly.Count;
            compressedCount += assembly.CompressedCount;
            encryptedCount += assembly.EncryptedCount;
        }

        return new SubscriptionFanoutDispatchResult(
            FrameCount: frameCount,
            SubscriberDeliveries: subscriberDeliveries,
            DatagramCount: datagramCount,
            CompressedCount: compressedCount,
            EncryptedCount: encryptedCount);
    }

    public SubscriptionFanoutDispatchResult DispatchScheduledWork(
        ReadOnlySpan<ScheduledWorkItem> items,
        Span<ConnectionId> subscriberScratch,
        Span<OutboundDatagram> destination,
        bool allowCompression = true,
        bool allowSegmentation = true)
    {
        if (!_assembler.HasTransforms)
            return DispatchScheduledWorkWithoutTransforms(items, destination);

        int frameCount = 0;
        int subscriberDeliveries = 0;
        int datagramCount = 0;
        int compressedCount = 0;
        int encryptedCount = 0;

        for (int i = 0; i < items.Length && datagramCount < destination.Length; i++)
        {
            ScheduledWorkItem item = items[i];
            SubscriptionKey scopeKey = item.ScopeKey;
            if (!scopeKey.IsValid)
                throw new ArgumentOutOfRangeException(nameof(items));

            int subscriberCount = _subscriptions.CopySubscribers(scopeKey, subscriberScratch);
            if (subscriberCount == 0)
                continue;

            PublishedFrame frame = SharedFramePublisher.Publish(item);
            frameCount++;
            subscriberDeliveries += subscriberCount;

            OutboundAssemblyResult assembly = _assembler.AssembleFanout(
                subscriberScratch[..subscriberCount],
                frame,
                destination[datagramCount..],
                allowCompression,
                allowSegmentation);

            datagramCount += assembly.Count;
            compressedCount += assembly.CompressedCount;
            encryptedCount += assembly.EncryptedCount;
        }

        return new SubscriptionFanoutDispatchResult(
            FrameCount: frameCount,
            SubscriberDeliveries: subscriberDeliveries,
            DatagramCount: datagramCount,
            CompressedCount: compressedCount,
            EncryptedCount: encryptedCount);
    }

    private SubscriptionFanoutDispatchResult DispatchWithoutTransforms(
        ReadOnlySpan<ScopedPublishedFrame> frames,
        Span<OutboundDatagram> destination)
    {
        int frameCount = 0;
        int subscriberDeliveries = 0;
        int datagramCount = 0;
        int compressedCount = 0;
        int encryptedCount = 0;

        for (int i = 0; i < frames.Length && datagramCount < destination.Length; i++)
        {
            ScopedPublishedFrame scopedFrame = frames[i];
            PublishedFrame frame = scopedFrame.Frame;
            ImmutablePayloadBuffer envelope = PacketCodec.EncodeEnvelope(frame.PacketId, frame.Body, frame.DefaultHeaderFlags);
            int written = _subscriptions.WriteDatagramsForSubscribers(
                scopedFrame.ScopeKey,
                frame.Plane,
                frame.PacketClass,
                frame.Channel,
                envelope,
                destination[datagramCount..]);
            if (written == 0)
                continue;

            frameCount++;
            subscriberDeliveries += written;
            datagramCount += written;
            if ((frame.DefaultHeaderFlags & PacketHeaderFlags.Compressed) != 0)
                compressedCount += written;
            if ((frame.DefaultHeaderFlags & PacketHeaderFlags.Encrypted) != 0)
                encryptedCount += written;
        }

        return new SubscriptionFanoutDispatchResult(
            FrameCount: frameCount,
            SubscriberDeliveries: subscriberDeliveries,
            DatagramCount: datagramCount,
            CompressedCount: compressedCount,
            EncryptedCount: encryptedCount);
    }

    private SubscriptionFanoutDispatchResult DispatchScheduledWorkWithoutTransforms(
        ReadOnlySpan<ScheduledWorkItem> items,
        Span<OutboundDatagram> destination)
    {
        int frameCount = 0;
        int subscriberDeliveries = 0;
        int datagramCount = 0;
        int compressedCount = 0;
        int encryptedCount = 0;

        for (int i = 0; i < items.Length && datagramCount < destination.Length; i++)
        {
            ScheduledWorkItem item = items[i];
            SubscriptionKey scopeKey = item.ScopeKey;
            if (!scopeKey.IsValid)
                throw new ArgumentOutOfRangeException(nameof(items));

            if (_enableLatestStateBatching &&
                item.Kind == ScheduledWorkKind.LatestState &&
                item.PacketClass == PacketClass.Realtime)
            {
                int batchCount = CountRealtimeBatch(items[i..]);
                if (batchCount > 1)
                {
                    ImmutablePayloadBuffer batchEnvelope = PacketCodec.EncodeRealtimeStateBatchEnvelope(
                        items.Slice(i, batchCount),
                        PacketHeaderFlags.HighPriorityHint);
                    int writtenBatch = _subscriptions.WriteDatagramsForSubscribers(
                        scopeKey,
                        RealtimeStateBatchPacket.Plane,
                        PacketClass.Realtime,
                        RealtimeStateBatchPacket.Channel,
                        batchEnvelope,
                        destination[datagramCount..]);
                    if (writtenBatch != 0)
                    {
                        frameCount++;
                        subscriberDeliveries += writtenBatch;
                        datagramCount += writtenBatch;
                    }

                    i += batchCount - 1;
                    continue;
                }
            }

            NetworkPlane plane;
            PacketClass packetClass = item.PacketClass;
            ChannelType channel;
            ImmutablePayloadBuffer envelope;
            switch (item.Kind)
            {
                case ScheduledWorkKind.LatestState:
                {
                    PacketHeaderFlags flags = item.PacketClass == PacketClass.Bulk
                        ? PacketHeaderFlags.None
                        : PacketHeaderFlags.HighPriorityHint;
                    plane = RealtimeStateFramePacket.Plane;
                    channel = item.PacketClass == PacketClass.Bulk
                        ? ChannelType.ReliableOrdered
                        : RealtimeStateFramePacket.Channel;
                    envelope = PacketCodec.EncodeRealtimeStateFrameEnvelope(item.Sequence, item.ItemKey, item.Payload, flags);
                    break;
                }
                case ScheduledWorkKind.OrderedEvent:
                    plane = ReliableEventFramePacket.Plane;
                    channel = ReliableEventFramePacket.Channel;
                    envelope = PacketCodec.EncodeReliableEventFrameEnvelope(item.Sequence, item.Payload);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported scheduled work kind: {item.Kind}.");
            }

            int written = _subscriptions.WriteDatagramsForSubscribers(
                scopeKey,
                plane,
                packetClass,
                channel,
                envelope,
                destination[datagramCount..]);
            if (written == 0)
                continue;

            frameCount++;
            subscriberDeliveries += written;
            datagramCount += written;
        }

        return new SubscriptionFanoutDispatchResult(
            FrameCount: frameCount,
            SubscriberDeliveries: subscriberDeliveries,
            DatagramCount: datagramCount,
            CompressedCount: compressedCount,
            EncryptedCount: encryptedCount);
    }

    private static int CountRealtimeBatch(ReadOnlySpan<ScheduledWorkItem> items)
    {
        ref readonly ScheduledWorkItem first = ref items[0];
        SubscriptionKey scopeKey = first.ScopeKey;
        int count = 0;
        int payloadBytes = sizeof(ushort);

        for (int i = 0; i < items.Length && count < MaxRealtimeBatchItems; i++)
        {
            ref readonly ScheduledWorkItem item = ref items[i];
            if (item.Kind != ScheduledWorkKind.LatestState ||
                item.PacketClass != PacketClass.Realtime ||
                item.ScopeKey != scopeKey)
                break;

            int nextPayloadBytes = payloadBytes + sizeof(ulong) + sizeof(ulong) + sizeof(ushort) + item.Payload.Length;
            if (count > 0 && nextPayloadBytes > MaxRealtimeBatchPayloadBytes)
                break;

            payloadBytes = nextPayloadBytes;
            count++;
        }

        return count;
    }
}
