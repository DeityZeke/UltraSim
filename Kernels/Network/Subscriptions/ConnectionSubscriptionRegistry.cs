using System.Runtime.InteropServices;

namespace UltraSim.Network;

public sealed class ConnectionSubscriptionRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<SubscriptionKey, List<ConnectionId>> _connectionsByKey = new();
    private readonly Dictionary<ConnectionId, List<SubscriptionKey>> _keysByConnection = new();

    public bool Subscribe(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (!key.IsValid)
            throw new ArgumentOutOfRangeException(nameof(key));

        lock (_sync)
        {
            return Subscribe_NoLock(connectionId, key);
        }
    }

    public bool Unsubscribe(ConnectionId connectionId, SubscriptionKey key)
    {
        lock (_sync)
        {
            return Unsubscribe_NoLock(connectionId, key);
        }
    }

    public int RemoveConnection(ConnectionId connectionId)
    {
        lock (_sync)
        {
            if (!_keysByConnection.TryGetValue(connectionId, out List<SubscriptionKey>? keys))
                return 0;

            int removed = 0;
            while (keys.Count > 0)
            {
                if (Unsubscribe_NoLock(connectionId, keys[^1]))
                    removed++;
            }

            return removed;
        }
    }

    public int CountSubscribers(SubscriptionKey key)
    {
        lock (_sync)
        {
            return _connectionsByKey.TryGetValue(key, out List<ConnectionId>? connections)
                ? connections.Count
                : 0;
        }
    }

    public int CopySubscribers(SubscriptionKey key, Span<ConnectionId> destination)
    {
        lock (_sync)
        {
            if (!_connectionsByKey.TryGetValue(key, out List<ConnectionId>? connections) || destination.Length == 0)
                return 0;

            int count = Math.Min(connections.Count, destination.Length);
            CollectionsMarshal.AsSpan(connections)[..count].CopyTo(destination);
            return count;
        }
    }

    public bool IsSubscribed(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (!key.IsValid)
            throw new ArgumentOutOfRangeException(nameof(key));

        lock (_sync)
        {
            return _keysByConnection.TryGetValue(connectionId, out List<SubscriptionKey>? keys) &&
                   Contains(keys, key);
        }
    }

    public bool HasSubscriptions(ConnectionId connectionId)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        lock (_sync)
        {
            return _keysByConnection.TryGetValue(connectionId, out List<SubscriptionKey>? keys) &&
                   keys.Count != 0;
        }
    }

    public int WriteDatagramsForSubscribers(
        SubscriptionKey key,
        NetworkPlane plane,
        PacketClass packetClass,
        ChannelType channel,
        ImmutablePayloadBuffer payload,
        Span<OutboundDatagram> destination)
    {
        lock (_sync)
        {
            if (!_connectionsByKey.TryGetValue(key, out List<ConnectionId>? connections) || destination.Length == 0)
                return 0;

            int count = Math.Min(connections.Count, destination.Length);
            ReadOnlySpan<ConnectionId> subscribers = CollectionsMarshal.AsSpan(connections)[..count];
            for (int i = 0; i < subscribers.Length; i++)
            {
                destination[i] = new OutboundDatagram(
                    subscribers[i],
                    plane,
                    packetClass,
                    channel,
                    payload);
            }

            return count;
        }
    }

    public AoiSubscriptionUpdateResult ReconcileConnection(ConnectionId connectionId, ReadOnlySpan<SubscriptionKey> desiredKeys)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        lock (_sync)
        {
            if (!_keysByConnection.TryGetValue(connectionId, out List<SubscriptionKey>? currentKeys))
            {
                currentKeys = new List<SubscriptionKey>(desiredKeys.Length);
                _keysByConnection.Add(connectionId, currentKeys);
            }

            int added = 0;
            for (int i = 0; i < desiredKeys.Length; i++)
            {
                SubscriptionKey key = desiredKeys[i];
                if (!key.IsValid)
                    throw new ArgumentOutOfRangeException(nameof(desiredKeys));
                if (Contains(currentKeys, key))
                    continue;
                if (Subscribe_NoLock(connectionId, key))
                    added++;
            }

            int removed = 0;
            for (int i = currentKeys.Count - 1; i >= 0; i--)
            {
                SubscriptionKey key = currentKeys[i];
                if (Contains(desiredKeys, key))
                    continue;
                if (Unsubscribe_NoLock(connectionId, key))
                    removed++;
            }

            return new AoiSubscriptionUpdateResult(
                DesiredScopeCount: desiredKeys.Length,
                AddedCount: added,
                RemovedCount: removed,
                Changed: added > 0 || removed > 0);
        }
    }

    private bool Subscribe_NoLock(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!_connectionsByKey.TryGetValue(key, out List<ConnectionId>? connections))
        {
            connections = new List<ConnectionId>(1);
            _connectionsByKey.Add(key, connections);
        }

        if (Contains(connections, connectionId))
            return false;

        connections.Add(connectionId);

        if (!_keysByConnection.TryGetValue(connectionId, out List<SubscriptionKey>? keys))
        {
            keys = new List<SubscriptionKey>(4);
            _keysByConnection.Add(connectionId, keys);
        }

        keys.Add(key);
        return true;
    }

    private bool Unsubscribe_NoLock(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!_connectionsByKey.TryGetValue(key, out List<ConnectionId>? connections))
            return false;

        int connectionIndex = connections.IndexOf(connectionId);
        if (connectionIndex < 0)
            return false;

        connections.RemoveAt(connectionIndex);
        if (connections.Count == 0)
            _connectionsByKey.Remove(key);

        if (_keysByConnection.TryGetValue(connectionId, out List<SubscriptionKey>? keys))
        {
            int keyIndex = keys.IndexOf(key);
            if (keyIndex >= 0)
                keys.RemoveAt(keyIndex);
            if (keys.Count == 0)
                _keysByConnection.Remove(connectionId);
        }

        return true;
    }

    private static bool Contains(List<ConnectionId> connections, ConnectionId connectionId)
    {
        for (int i = 0; i < connections.Count; i++)
        {
            if (connections[i] == connectionId)
                return true;
        }

        return false;
    }

    private static bool Contains(List<SubscriptionKey> keys, SubscriptionKey key)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == key)
                return true;
        }

        return false;
    }

    private static bool Contains(ReadOnlySpan<SubscriptionKey> keys, SubscriptionKey key)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key)
                return true;
        }

        return false;
    }
}
