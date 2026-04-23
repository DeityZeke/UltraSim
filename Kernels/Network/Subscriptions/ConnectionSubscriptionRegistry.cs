using System.Runtime.InteropServices;

namespace UltraSim.Network;

public sealed class ConnectionSubscriptionRegistry
{
    private sealed class ConnectionList
    {
        public readonly List<ConnectionId> Ordered = new();
        public readonly HashSet<ConnectionId> Membership = new();
    }

    private sealed class SubscriptionList
    {
        public readonly List<SubscriptionKey> Ordered = new();
        public readonly HashSet<SubscriptionKey> Membership = new();
    }

    private readonly object _sync = new();
    private readonly Dictionary<SubscriptionKey, ConnectionList> _connectionsByKey = new();
    private readonly Dictionary<ConnectionId, SubscriptionList> _keysByConnection = new();

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
            if (!_keysByConnection.TryGetValue(connectionId, out SubscriptionList? keys))
                return 0;

            int removed = 0;
            while (keys.Ordered.Count > 0)
            {
                if (Unsubscribe_NoLock(connectionId, keys.Ordered[^1]))
                    removed++;
            }

            return removed;
        }
    }

    public int CountSubscribers(SubscriptionKey key)
    {
        lock (_sync)
        {
            return _connectionsByKey.TryGetValue(key, out ConnectionList? connections)
                ? connections.Ordered.Count
                : 0;
        }
    }

    public int CopySubscribers(SubscriptionKey key, Span<ConnectionId> destination)
    {
        lock (_sync)
        {
            if (!_connectionsByKey.TryGetValue(key, out ConnectionList? connections) || destination.Length == 0)
                return 0;

            int count = Math.Min(connections.Ordered.Count, destination.Length);
            CollectionsMarshal.AsSpan(connections.Ordered)[..count].CopyTo(destination);
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
            return _keysByConnection.TryGetValue(connectionId, out SubscriptionList? keys) &&
                   keys.Membership.Contains(key);
        }
    }

    public bool HasSubscriptions(ConnectionId connectionId)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        lock (_sync)
        {
            return _keysByConnection.TryGetValue(connectionId, out SubscriptionList? keys) &&
                   keys.Ordered.Count != 0;
        }
    }

    public int CopySubscriptions(ConnectionId connectionId, Span<SubscriptionKey> destination)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));

        lock (_sync)
        {
            if (!_keysByConnection.TryGetValue(connectionId, out SubscriptionList? keys) || destination.Length == 0)
                return 0;

            int count = Math.Min(keys.Ordered.Count, destination.Length);
            CollectionsMarshal.AsSpan(keys.Ordered)[..count].CopyTo(destination);
            return count;
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
            if (!_connectionsByKey.TryGetValue(key, out ConnectionList? connections) || destination.Length == 0)
                return 0;

            int count = Math.Min(connections.Ordered.Count, destination.Length);
            ReadOnlySpan<ConnectionId> subscribers = CollectionsMarshal.AsSpan(connections.Ordered)[..count];
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
            if (!_keysByConnection.TryGetValue(connectionId, out SubscriptionList? currentKeys))
            {
                currentKeys = new SubscriptionList();
                _keysByConnection.Add(connectionId, currentKeys);
            }

            int added = 0;
            for (int i = 0; i < desiredKeys.Length; i++)
            {
                SubscriptionKey key = desiredKeys[i];
                if (!key.IsValid)
                    throw new ArgumentOutOfRangeException(nameof(desiredKeys));
                if (currentKeys.Membership.Contains(key))
                    continue;
                if (Subscribe_NoLock(connectionId, key))
                    added++;
            }

            int removed = 0;
            for (int i = currentKeys.Ordered.Count - 1; i >= 0; i--)
            {
                SubscriptionKey key = currentKeys.Ordered[i];
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
        if (!_connectionsByKey.TryGetValue(key, out ConnectionList? connections))
        {
            connections = new ConnectionList();
            _connectionsByKey.Add(key, connections);
        }

        if (!connections.Membership.Add(connectionId))
            return false;

        connections.Ordered.Add(connectionId);

        if (!_keysByConnection.TryGetValue(connectionId, out SubscriptionList? keys))
        {
            keys = new SubscriptionList();
            _keysByConnection.Add(connectionId, keys);
        }

        keys.Membership.Add(key);
        keys.Ordered.Add(key);
        return true;
    }

    private bool Unsubscribe_NoLock(ConnectionId connectionId, SubscriptionKey key)
    {
        if (!_connectionsByKey.TryGetValue(key, out ConnectionList? connections))
            return false;

        if (!connections.Membership.Remove(connectionId))
            return false;

        int connectionIndex = connections.Ordered.IndexOf(connectionId);
        if (connectionIndex >= 0)
            connections.Ordered.RemoveAt(connectionIndex);
        if (connections.Ordered.Count == 0)
            _connectionsByKey.Remove(key);

        if (_keysByConnection.TryGetValue(connectionId, out SubscriptionList? keys))
        {
            keys.Membership.Remove(key);
            int keyIndex = keys.Ordered.IndexOf(key);
            if (keyIndex >= 0)
                keys.Ordered.RemoveAt(keyIndex);
            if (keys.Ordered.Count == 0)
                _keysByConnection.Remove(connectionId);
        }

        return true;
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
