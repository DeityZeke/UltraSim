namespace UltraSim.Network;

public sealed class ChunkAoiSubscriptionMaintainer
{
    private readonly object _sync = new();
    private readonly ChunkAoiPolicy _policy;
    private readonly Dictionary<ConnectionId, HashSet<SubscriptionKey>> _scopeKeysByConnection = new();
    private readonly HashSet<SubscriptionKey> _desiredKeyScratch = new();
    private readonly List<SubscriptionKey> _currentKeyScratch = new();

    public ChunkAoiSubscriptionMaintainer(ChunkAoiPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public AoiSubscriptionUpdateResult UpdateConnection(
        ConnectionId connectionId,
        in WorldPosition2 center,
        ConnectionSubscriptionRegistry subscriptions)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        ArgumentNullException.ThrowIfNull(subscriptions);

        lock (_sync)
        {
            if (!_scopeKeysByConnection.TryGetValue(connectionId, out HashSet<SubscriptionKey>? current))
            {
                current = new HashSet<SubscriptionKey>();
                _scopeKeysByConnection.Add(connectionId, current);
            }

            _desiredKeyScratch.Clear();
            _policy.CollectKeys(center, _desiredKeyScratch);

            int added = 0;
            foreach (SubscriptionKey key in _desiredKeyScratch)
            {
                if (current.Contains(key))
                    continue;

                subscriptions.Subscribe(connectionId, key);
                added++;
            }

            int removed = 0;
            _currentKeyScratch.Clear();
            foreach (SubscriptionKey key in current)
                _currentKeyScratch.Add(key);
            for (int i = 0; i < _currentKeyScratch.Count; i++)
            {
                SubscriptionKey key = _currentKeyScratch[i];
                if (_desiredKeyScratch.Contains(key))
                    continue;

                subscriptions.Unsubscribe(connectionId, key);
                removed++;
            }

            current.Clear();
            foreach (SubscriptionKey key in _desiredKeyScratch)
                current.Add(key);

            return new AoiSubscriptionUpdateResult(
                DesiredScopeCount: _desiredKeyScratch.Count,
                AddedCount: added,
                RemovedCount: removed,
                Changed: added > 0 || removed > 0);
        }
    }

    public int RemoveConnection(ConnectionId connectionId, ConnectionSubscriptionRegistry subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        lock (_sync)
        {
            _scopeKeysByConnection.Remove(connectionId);
            return subscriptions.RemoveConnection(connectionId);
        }
    }
}
