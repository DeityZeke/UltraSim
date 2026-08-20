namespace UltraSim.Network;

/// <summary>
/// Tracks the last acknowledged full baseline per connection.
/// </summary>
public sealed class StateBaselineAckTracker
{
    private readonly Dictionary<ConnectionId, StateBaselineDescriptor> _lastAckByConnection = new();

    public int Count => _lastAckByConnection.Count;

    public bool TryAcknowledge(ConnectionId connectionId, StateBaselineDescriptor descriptor, out StateBaselineDescriptor previous)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        if (_lastAckByConnection.TryGetValue(connectionId, out previous))
        {
            if (descriptor.BaselineId <= previous.BaselineId)
                return false;

            _lastAckByConnection[connectionId] = descriptor;
            return true;
        }

        previous = default;
        _lastAckByConnection.Add(connectionId, descriptor);
        return true;
    }

    public bool TryGetLastAcknowledged(ConnectionId connectionId, out StateBaselineDescriptor descriptor)
    {
        return _lastAckByConnection.TryGetValue(connectionId, out descriptor);
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
