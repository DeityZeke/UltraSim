namespace UltraSim.Network;

public sealed class ScheduledWorkFanoutBridge
{
    private readonly SubscriptionFanoutDispatcher _dispatcher;

    public ScheduledWorkFanoutBridge(
        ConnectionSubscriptionRegistry subscriptions,
        OutboundDatagramAssembler? assembler = null,
        bool enableLatestStateBatching = false)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        _dispatcher = new SubscriptionFanoutDispatcher(
            subscriptions,
            assembler ?? new OutboundDatagramAssembler(),
            enableLatestStateBatching);
    }

    public ScheduledWorkFanoutResult Dispatch(
        ReadOnlySpan<ScheduledWorkItem> items,
        Span<ConnectionId> subscriberScratch,
        Span<OutboundDatagram> output)
    {
        SubscriptionFanoutDispatchResult dispatch = _dispatcher.DispatchScheduledWork(
            items,
            subscriberScratch,
            output);

        return new ScheduledWorkFanoutResult(
            ProjectedFrameCount: dispatch.FrameCount,
            SubscriberDeliveries: dispatch.SubscriberDeliveries,
            DatagramCount: dispatch.DatagramCount,
            CompressedCount: dispatch.CompressedCount);
    }
}
