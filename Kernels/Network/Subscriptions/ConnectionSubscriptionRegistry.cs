namespace UltraSim.Network;

public sealed class ConnectionSubscriptionRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<SubscriptionKey, HashSet<ConnectionId>> _connectionsByKey = new();
    private readonly Dictionary<ConnectionId, HashSet<SubscriptionKey>> _keysByConnection = new();

    public bool Subscribe(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (!key.IsValid)
            throw new ArgumentOutOfRangeException(nameof(key));

        lock (_sync)
        {
            if (!_connectionsByKey.TryGetValue(key, out HashSet<ConnectionId>? connections))
            {
                connections = new HashSet<ConnectionId>();
                _connectionsByKey.Add(key, connections);
            }

            if (!_keysByConnection.TryGetValue(connectionId, out HashSet<SubscriptionKey>? keys))
            {
                keys = new HashSet<SubscriptionKey>();
                _keysByConnection.Add(connectionId, keys);
            }

            bool added = connections.Add(connectionId);
            keys.Add(key);
            return added;
        }
    }

    public bool Unsubscribe(ConnectionId connectionId, SubscriptionKey key)
    {
        lock (_sync)
        {
            if (!_connectionsByKey.TryGetValue(key, out HashSet<ConnectionId>? connections))
                return false;

            bool removed = connections.Remove(connectionId);
            if (!removed)
                return false;

            if (connections.Count == 0)
                _connectionsByKey.Remove(key);

            if (_keysByConnection.TryGetValue(connectionId, out HashSet<SubscriptionKey>? keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _keysByConnection.Remove(connectionId);
            }

            return true;
        }
    }

    public int RemoveConnection(ConnectionId connectionId)
    {
        lock (_sync)
        {
            if (!_keysByConnection.TryGetValue(connectionId, out HashSet<SubscriptionKey>? keys))
                return 0;

            SubscriptionKey[] keySnapshot = new SubscriptionKey[keys.Count];
            keys.CopyTo(keySnapshot);
            int removed = 0;
            for (int i = 0; i < keySnapshot.Length; i++)
            {
                if (Unsubscribe_NoLock(connectionId, keySnapshot[i]))
                    removed++;
            }

            return removed;
        }
    }

    public int CountSubscribers(SubscriptionKey key)
    {
        lock (_sync)
        {
            return _connectionsByKey.TryGetValue(key, out HashSet<ConnectionId>? connections)
                ? connections.Count
                : 0;
        }
    }

    public int CopySubscribers(SubscriptionKey key, Span<ConnectionId> destination)
    {
        lock (_sync)
        {
            if (!_connectionsByKey.TryGetValue(key, out HashSet<ConnectionId>? connections) || destination.Length == 0)
                return 0;

            int count = 0;
            foreach (ConnectionId connectionId in connections)
            {
                if (count >= destination.Length)
                    break;
                destination[count++] = connectionId;
            }

            return count;
        }
    }

    private bool Unsubscribe_NoLock(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!_connectionsByKey.TryGetValue(key, out HashSet<ConnectionId>? connections))
            return false;

        bool removed = connections.Remove(connectionId);
        if (!removed)
            return false;

        if (connections.Count == 0)
            _connectionsByKey.Remove(key);

        if (_keysByConnection.TryGetValue(connectionId, out HashSet<SubscriptionKey>? keys))
        {
            keys.Remove(key);
            if (keys.Count == 0)
                _keysByConnection.Remove(connectionId);
        }

        return true;
    }
}
