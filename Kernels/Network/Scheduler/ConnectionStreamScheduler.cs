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
            return new ConnectionScheduleResult(0, state, false, 0, 0, 0, 0, 0, 0);

        var latestBuffer = new LatestStateUpdate[destination.Length];
        LatestStateDrainResult latestResult = _latestState.DrainSince(state.LatestStateCursor, latestBuffer);

        int count = 0;
        int realtimeItems = 0;
        int reliableItems = 0;
        int bulkItems = 0;
        int realtimeBytes = 0;
        int reliableBytes = 0;
        int bulkBytes = 0;
        LatestStateCursor latestCursor = state.LatestStateCursor;

        for (int i = 0; i < latestResult.Count && count < destination.Length; i++)
        {
            LatestStateUpdate update = latestBuffer[i];
            int payloadBytes = update.Payload.Length;
            if (WouldExceedBudget(update.PacketClass, realtimeItems, reliableItems, bulkItems, realtimeBytes, reliableBytes, bulkBytes, payloadBytes, budget))
                break;

            destination[count++] = new ScheduledWorkItem(
                ScheduledWorkKind.LatestState,
                update.PacketClass,
                update.Sequence,
                update.ItemKey,
                update.ScopeKey,
                update.Payload);
            latestCursor = new LatestStateCursor(update.Sequence);
            Accumulate(update.PacketClass, payloadBytes, ref realtimeItems, ref reliableItems, ref bulkItems, ref realtimeBytes, ref reliableBytes, ref bulkBytes);
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
                if (WouldExceedBudget(entry.PacketClass, realtimeItems, reliableItems, bulkItems, realtimeBytes, reliableBytes, bulkBytes, payloadBytes, budget))
                    break;

                destination[count++] = new ScheduledWorkItem(
                    ScheduledWorkKind.OrderedEvent,
                    entry.PacketClass,
                    entry.Sequence,
                    0,
                    entry.ScopeKey,
                    entry.Payload);
                eventCursor = new OrderedEventCursor(entry.Sequence + 1);
                Accumulate(entry.PacketClass, payloadBytes, ref realtimeItems, ref reliableItems, ref bulkItems, ref realtimeBytes, ref reliableBytes, ref bulkBytes);
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
            BulkItemsScheduled: bulkItems,
            RealtimeBytesScheduled: realtimeBytes,
            ReliableEventBytesScheduled: reliableBytes,
            BulkBytesScheduled: bulkBytes);
    }

    private static bool WouldExceedBudget(
        PacketClass packetClass,
        int realtimeItems,
        int reliableItems,
        int bulkItems,
        int realtimeBytes,
        int reliableBytes,
        int bulkBytes,
        int nextPayloadBytes,
        ConnectionSendBudget budget)
    {
        PacketClassBudget classBudget = packetClass switch
        {
            PacketClass.Realtime => budget.Realtime,
            PacketClass.ReliableEvent => budget.ReliableEvent,
            PacketClass.Bulk => budget.Bulk,
            _ => throw new InvalidOperationException($"Unsupported packet class for stream scheduling: {packetClass}."),
        };

        int itemCount = packetClass switch
        {
            PacketClass.Realtime => realtimeItems,
            PacketClass.ReliableEvent => reliableItems,
            PacketClass.Bulk => bulkItems,
            _ => 0,
        };
        int totalBytes = packetClass switch
        {
            PacketClass.Realtime => realtimeBytes,
            PacketClass.ReliableEvent => reliableBytes,
            PacketClass.Bulk => bulkBytes,
            _ => 0,
        };

        if (itemCount >= classBudget.MaxItems)
            return true;

        long nextBytes = (long)totalBytes + nextPayloadBytes;
        return nextBytes > classBudget.MaxPayloadBytes;
    }

    private static void Accumulate(
        PacketClass packetClass,
        int payloadBytes,
        ref int realtimeItems,
        ref int reliableItems,
        ref int bulkItems,
        ref int realtimeBytes,
        ref int reliableBytes,
        ref int bulkBytes)
    {
        switch (packetClass)
        {
            case PacketClass.Realtime:
                realtimeItems++;
                realtimeBytes += payloadBytes;
                break;
            case PacketClass.ReliableEvent:
                reliableItems++;
                reliableBytes += payloadBytes;
                break;
            case PacketClass.Bulk:
                bulkItems++;
                bulkBytes += payloadBytes;
                break;
            default:
                throw new InvalidOperationException($"Unsupported packet class for stream scheduling: {packetClass}.");
        }
    }
}
