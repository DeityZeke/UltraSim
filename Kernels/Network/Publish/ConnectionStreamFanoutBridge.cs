namespace UltraSim.Network;

public sealed class ConnectionStreamFanoutBridge
{
    private readonly ConnectionStreamScheduler _scheduler;
    private readonly ScheduledWorkFanoutBridge _fanout;

    public ConnectionStreamFanoutBridge(
        ConnectionStreamScheduler scheduler,
        ScheduledWorkFanoutBridge fanout)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _fanout = fanout ?? throw new ArgumentNullException(nameof(fanout));
    }

    public ConnectionScheduledFanoutResult ScheduleAndDispatch(
        ConnectionScheduleState state,
        ConnectionSendBudget budget,
        Span<ScheduledWorkItem> scheduledWork,
        Span<ConnectionId> subscriberScratch,
        Span<OutboundDatagram> output)
    {
        ConnectionScheduleResult schedule = _scheduler.Schedule(state, budget, scheduledWork);
        ScheduledWorkFanoutResult fanout = _fanout.Dispatch(
            scheduledWork[..schedule.Count],
            subscriberScratch,
            output);
        return new ConnectionScheduledFanoutResult(schedule, fanout);
    }
}
