namespace UltraSim.Network;

/// <summary>
/// Tracks the highest contiguous reliable-event acknowledgement per connection.
/// This is the first runtime seam for later replay/baseline work; it does not
/// imply retransmit by itself.
/// </summary>
public sealed class ReliableEventAckTracker
{
    private readonly Dictionary<ConnectionId, ulong> _lastAckByConnection = new();

    public int Count => _lastAckByConnection.Count;

    public bool TryAcknowledge(ConnectionId connectionId, ulong sequence, out ulong previousSequence)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        if (_lastAckByConnection.TryGetValue(connectionId, out previousSequence))
        {
            if (sequence <= previousSequence)
                return false;

            _lastAckByConnection[connectionId] = sequence;
            return true;
        }

        previousSequence = 0;
        _lastAckByConnection.Add(connectionId, sequence);
        return true;
    }

    public bool TryGetLastAcknowledged(ConnectionId connectionId, out ulong sequence)
    {
        return _lastAckByConnection.TryGetValue(connectionId, out sequence);
    }

    public bool Remove(ConnectionId connectionId)
    {
        return _lastAckByConnection.Remove(connectionId);
    }

    public void Clear()
    {
        _lastAckByConnection.Clear();
    }
}
