namespace UltraSim.Network;

/// <summary>
/// Filters a latest-state snapshot down to the items that are newer than what a specific
/// connection was last sent. Item identity is stable across scope changes, so this planner
/// keys by ItemKey rather than scope.
/// </summary>
public sealed class ConnectionLatestStateDeltaPlanner
{
    private readonly ConnectionLatestStateSentTracker _sentTracker;

    public ConnectionLatestStateDeltaPlanner(ConnectionLatestStateSentTracker sentTracker)
    {
        _sentTracker = sentTracker ?? throw new ArgumentNullException(nameof(sentTracker));
    }

    public ConnectionLatestStateDeltaPlanResult Plan(
        ConnectionId connectionId,
        ReadOnlySpan<LatestStateUpdate> snapshot,
        Span<LatestStateUpdate> destination)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (destination.Length == 0 || snapshot.Length == 0)
            return default;

        int count = 0;
        ulong highestSequence = 0;
        for (int i = 0; i < snapshot.Length && count < destination.Length; i++)
        {
            LatestStateUpdate update = snapshot[i];
            if (_sentTracker.TryGetLastSentSequence(connectionId, update.ItemKey, out ulong lastSentSequence) &&
                update.Sequence <= lastSentSequence)
            {
                continue;
            }

            destination[count++] = update;
            if (update.Sequence > highestSequence)
                highestSequence = update.Sequence;
        }

        return new ConnectionLatestStateDeltaPlanResult(count, highestSequence);
    }
}
