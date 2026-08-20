using System.Diagnostics;

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
        long scheduleStart = Stopwatch.GetTimestamp();
        ConnectionScheduleResult schedule = _scheduler.Schedule(state, budget, scheduledWork);
        long scheduleEnd = Stopwatch.GetTimestamp();
        ScheduledWorkFanoutResult fanout = _fanout.Dispatch(
            scheduledWork[..schedule.Count],
            subscriberScratch,
            output);
        long fanoutEnd = Stopwatch.GetTimestamp();
        return new ConnectionScheduledFanoutResult(
            schedule,
            fanout,
            scheduleEnd - scheduleStart,
            fanoutEnd - scheduleEnd);
    }
}
