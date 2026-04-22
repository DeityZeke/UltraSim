namespace UltraSim.Network;

public sealed class ConnectionStreamScheduler
{
    private readonly LatestStateStream _latestState;
    private readonly OrderedEventStream _orderedEvents;

    public ConnectionStreamScheduler(
        LatestStateStream latestState,
        OrderedEventStream orderedEvents)
    {
        _latestState = latestState ?? throw new ArgumentNullException(nameof(latestState));
        _orderedEvents = orderedEvents ?? throw new ArgumentNullException(nameof(orderedEvents));
    }

    public ConnectionScheduleResult Schedule(
        ConnectionScheduleState state,
        ConnectionSendBudget budget,
        Span<ScheduledWorkItem> destination)
    {
        if (destination.Length == 0)
            return new ConnectionScheduleResult(0, state, false, 0, 0, 0, 0);

        var latestBuffer = new LatestStateUpdate[destination.Length];
        LatestStateDrainResult latestResult = _latestState.DrainSince(state.LatestStateCursor, latestBuffer);

        int count = 0;
        int realtimeItems = 0;
        int reliableItems = 0;
        int realtimeBytes = 0;
        int reliableBytes = 0;
        LatestStateCursor latestCursor = state.LatestStateCursor;

        for (int i = 0; i < latestResult.Count && count < destination.Length; i++)
        {
            LatestStateUpdate update = latestBuffer[i];
            int payloadBytes = update.Payload.Length;
            if (WouldExceedBudget(realtimeItems, realtimeBytes, payloadBytes, budget.Realtime))
                break;

            destination[count++] = new ScheduledWorkItem(
                ScheduledWorkKind.LatestState,
                PacketClass.Realtime,
                update.Sequence,
                update.ItemKey,
                update.ScopeKey,
                update.Payload);
            latestCursor = new LatestStateCursor(update.Sequence);
            realtimeItems++;
            realtimeBytes += payloadBytes;
        }

        var eventBuffer = new OrderedEventEntry[Math.Max(0, destination.Length - count)];
        OrderedEventDrainResult eventResult = _orderedEvents.DrainSince(state.OrderedEventCursor, eventBuffer);
        OrderedEventCursor eventCursor = state.OrderedEventCursor;
        bool recoveryRequired = eventResult.Status == OrderedEventDrainStatus.RecoveryRequired;

        if (!recoveryRequired)
        {
            for (int i = 0; i < eventResult.Count && count < destination.Length; i++)
            {
                OrderedEventEntry entry = eventBuffer[i];
                int payloadBytes = entry.Payload.Length;
                if (WouldExceedBudget(reliableItems, reliableBytes, payloadBytes, budget.ReliableEvent))
                    break;

                destination[count++] = new ScheduledWorkItem(
                    ScheduledWorkKind.OrderedEvent,
                    PacketClass.ReliableEvent,
                    entry.Sequence,
                    0,
                    entry.ScopeKey,
                    entry.Payload);
                eventCursor = new OrderedEventCursor(entry.Sequence + 1);
                reliableItems++;
                reliableBytes += payloadBytes;
            }
        }
        else
        {
            eventCursor = eventResult.NextCursor;
        }

        return new ConnectionScheduleResult(
            Count: count,
            NextState: new ConnectionScheduleState(latestCursor, eventCursor),
            OrderedEventRecoveryRequired: recoveryRequired,
            RealtimeItemsScheduled: realtimeItems,
            ReliableEventItemsScheduled: reliableItems,
            RealtimeBytesScheduled: realtimeBytes,
            ReliableEventBytesScheduled: reliableBytes);
    }

    private static bool WouldExceedBudget(int itemCount, int totalBytes, int nextPayloadBytes, PacketClassBudget budget)
    {
        if (itemCount >= budget.MaxItems)
            return true;

        long nextBytes = (long)totalBytes + nextPayloadBytes;
        return nextBytes > budget.MaxPayloadBytes;
    }
}
