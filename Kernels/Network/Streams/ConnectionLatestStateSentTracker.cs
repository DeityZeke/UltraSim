namespace UltraSim.Network;

/// <summary>
/// Tracks the latest-state sequence most recently sent to each connection for each stable item identity.
/// This is intentionally send-based, not ack-based: it answers "what did we last send this connection?"
/// which is the simplest useful seam for incremental baseline/delta advancement.
/// </summary>
public sealed class ConnectionLatestStateSentTracker
{
    private readonly Dictionary<ConnectionId, Dictionary<ulong, ulong>> _sequenceByConnection = new();

    public int ConnectionCount => _sequenceByConnection.Count;

    public void MarkSent(ConnectionId connectionId, in LatestStateUpdate update)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (update.ItemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(update));

        Dictionary<ulong, ulong> itemSequences = GetOrCreate(connectionId);
        if (itemSequences.TryGetValue(update.ItemKey, out ulong previous) && update.Sequence <= previous)
            return;

        itemSequences[update.ItemKey] = update.Sequence;
    }

    public int MarkSent(ConnectionId connectionId, ReadOnlySpan<LatestStateUpdate> updates)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        int count = 0;
        for (int i = 0; i < updates.Length; i++)
        {
            MarkSent(connectionId, updates[i]);
            count++;
        }

        return count;
    }

    public bool TryGetLastSentSequence(ConnectionId connectionId, ulong itemKey, out ulong sequence)
    {
        if (_sequenceByConnection.TryGetValue(connectionId, out Dictionary<ulong, ulong>? itemSequences))
            return itemSequences.TryGetValue(itemKey, out sequence);

        sequence = 0;
        return false;
    }

    public bool Remove(ConnectionId connectionId)
    {
        return _sequenceByConnection.Remove(connectionId);
    }

    public void Clear()
    {
        _sequenceByConnection.Clear();
    }

    private Dictionary<ulong, ulong> GetOrCreate(ConnectionId connectionId)
    {
        if (_sequenceByConnection.TryGetValue(connectionId, out Dictionary<ulong, ulong>? itemSequences))
            return itemSequences;

        itemSequences = new Dictionary<ulong, ulong>();
        _sequenceByConnection.Add(connectionId, itemSequences);
        return itemSequences;
    }
}
