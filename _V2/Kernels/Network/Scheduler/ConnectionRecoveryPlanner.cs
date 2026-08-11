namespace UltraSim.Network;

/// <summary>
/// Turns scheduler recovery signals into an explicit full-baseline resync path.
/// Until deltas exist, recovery means "send a full baseline snapshot and resume
/// incremental scheduling from that snapshot floor."
/// </summary>
public sealed class ConnectionRecoveryPlanner
{
    private readonly LatestStateStream _latestState;
    private readonly OrderedEventStream _orderedEvents;
    private readonly ConnectionStreamScheduler _scheduler;
    private readonly StateBaselineSnapshotBuilder _baselineBuilder;
    private uint _nextBaselineId = 1;

    public ConnectionRecoveryPlanner(
        LatestStateStream latestState,
        OrderedEventStream orderedEvents,
        ConnectionStreamScheduler? scheduler = null,
        StateBaselineSnapshotBuilder? baselineBuilder = null)
    {
        _latestState = latestState ?? throw new ArgumentNullException(nameof(latestState));
        _orderedEvents = orderedEvents ?? throw new ArgumentNullException(nameof(orderedEvents));
        _scheduler = scheduler ?? new ConnectionStreamScheduler(latestState, orderedEvents);
        _baselineBuilder = baselineBuilder ?? new StateBaselineSnapshotBuilder();
    }

    public ConnectionRecoveryPlanResult Plan(
        ConnectionId connectionId,
        ConnectionScheduleState state,
        ConnectionSendBudget budget,
        Span<ScheduledWorkItem> scheduledDestination,
        Span<LatestStateUpdate> snapshotItems,
        Span<OutboundDatagram> baselineDestination)
    {
        ConnectionScheduleResult schedule = _scheduler.Schedule(state, budget, scheduledDestination);
        if (!schedule.OrderedEventRecoveryRequired)
        {
            return new ConnectionRecoveryPlanResult(
                Mode: ConnectionRecoveryMode.Scheduled,
                ScheduledWorkCount: schedule.Count,
                BaselineDatagramCount: 0,
                NextState: schedule.NextState,
                BaselineDescriptor: default,
                OrderedEventRecoveryRequired: false);
        }

        return PlanBaseline(connectionId, snapshotItems, baselineDestination);
    }

    public ConnectionRecoveryPlanResult PlanBaseline(
        ConnectionId connectionId,
        Span<LatestStateUpdate> snapshotItems,
        Span<OutboundDatagram> baselineDestination)
    {
        int itemCount = _latestState.CopySnapshot(snapshotItems);
        int boundedItemCount = Math.Min(itemCount, ushort.MaxValue);
        LatestStateSnapshot latestSnapshot = _latestState.GetSnapshot();
        OrderedEventSnapshot orderedSnapshot = _orderedEvents.GetSnapshot();
        ulong lastOrderedEventSequence = orderedSnapshot.Count == 0 ? 0UL : orderedSnapshot.NewestAvailableSequence;
        StateBaselineDescriptor descriptor = new(
            BaselineId: _nextBaselineId++,
            HighestLatestStateSequence: latestSnapshot.HighestPublishedSequence,
            LastOrderedEventSequence: lastOrderedEventSequence,
            ItemCount: (ushort)boundedItemCount);
        int baselineDatagramCount = _baselineBuilder.Build(
            connectionId,
            descriptor,
            snapshotItems.Slice(0, boundedItemCount),
            baselineDestination);

        return new ConnectionRecoveryPlanResult(
            Mode: ConnectionRecoveryMode.Baseline,
            ScheduledWorkCount: 0,
            BaselineDatagramCount: baselineDatagramCount,
            NextState: new ConnectionScheduleState(
                new LatestStateCursor(descriptor.HighestLatestStateSequence),
                new OrderedEventCursor(descriptor.LastOrderedEventSequence + 1)),
            BaselineDescriptor: descriptor,
            OrderedEventRecoveryRequired: true);
    }
}
