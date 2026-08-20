namespace UltraSim.Network;

public sealed class ChunkAoiSubscriptionMaintainer
{
    private readonly object _sync = new();
    private readonly ChunkAoiPolicy _policy;
    private readonly List<SubscriptionKey> _desiredKeyScratch = new();

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
            _desiredKeyScratch.Clear();
            _policy.CollectKeys(center, _desiredKeyScratch);
            return subscriptions.ReconcileConnection(connectionId, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_desiredKeyScratch));
        }
    }

    public int RemoveConnection(ConnectionId connectionId, ConnectionSubscriptionRegistry subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        return subscriptions.RemoveConnection(connectionId);
    }
}
