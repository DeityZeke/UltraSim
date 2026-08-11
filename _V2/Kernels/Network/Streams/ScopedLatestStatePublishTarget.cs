namespace UltraSim.Network;

public readonly record struct ScopedLatestStatePublishTarget(
    SubscriptionKey ScopeKey,
    uint DeliveryCadenceTicks,
    uint PublishTick = 0)
{
    public ScopedLatestStatePublishTarget(
        SubscriptionKey scopeKey,
        uint deliveryCadenceTicks)
        : this(scopeKey, deliveryCadenceTicks, 0)
    {
    }
}
