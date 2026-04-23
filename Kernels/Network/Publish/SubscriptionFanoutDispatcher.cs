namespace UltraSim.Network;

public sealed class SubscriptionFanoutDispatcher
{
    private readonly ConnectionSubscriptionRegistry _subscriptions;
    private readonly OutboundDatagramAssembler _assembler;

    public SubscriptionFanoutDispatcher(
        ConnectionSubscriptionRegistry subscriptions,
        OutboundDatagramAssembler assembler)
    {
        _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
        _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
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

            PublishedFrame frame = SharedFramePublisher.Publish(item);
            ImmutablePayloadBuffer envelope = PacketCodec.EncodeEnvelope(frame.PacketId, frame.Body, frame.DefaultHeaderFlags);
            int written = _subscriptions.WriteDatagramsForSubscribers(
                scopeKey,
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
}
