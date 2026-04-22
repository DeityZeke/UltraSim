namespace UltraSim.Network;

public sealed class ScheduledWorkFanoutBridge
{
    private readonly SubscriptionFanoutDispatcher _dispatcher;

    public ScheduledWorkFanoutBridge(ConnectionSubscriptionRegistry subscriptions, OutboundDatagramAssembler? assembler = null)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        _dispatcher = new SubscriptionFanoutDispatcher(subscriptions, assembler ?? new OutboundDatagramAssembler());
    }

    public ScheduledWorkFanoutResult Dispatch(
        ReadOnlySpan<ScheduledWorkItem> items,
        ReadOnlySpan<SubscriptionKey> scopeKeys,
        Span<ScopedPublishedFrame> projectedFrames,
        Span<ConnectionId> subscriberScratch,
        Span<OutboundDatagram> output)
    {
        int projectedCount = ScheduledWorkPublishedFrameProjector.Project(items, scopeKeys, projectedFrames);
        SubscriptionFanoutDispatchResult dispatch = _dispatcher.Dispatch(
            projectedFrames[..projectedCount],
            subscriberScratch,
            output);

        return new ScheduledWorkFanoutResult(
            ProjectedFrameCount: projectedCount,
            SubscriberDeliveries: dispatch.SubscriberDeliveries,
            DatagramCount: dispatch.DatagramCount,
            CompressedCount: dispatch.CompressedCount);
    }
}
