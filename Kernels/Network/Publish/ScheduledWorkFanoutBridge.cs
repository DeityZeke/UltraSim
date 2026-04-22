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
        Span<ConnectionId> subscriberScratch,
        Span<OutboundDatagram> output)
    {
        SubscriptionFanoutDispatchResult dispatch = _dispatcher.DispatchScheduledWork(
            items,
            scopeKeys,
            subscriberScratch,
            output);

        return new ScheduledWorkFanoutResult(
            ProjectedFrameCount: dispatch.FrameCount,
            SubscriberDeliveries: dispatch.SubscriberDeliveries,
            DatagramCount: dispatch.DatagramCount,
            CompressedCount: dispatch.CompressedCount);
    }
}
