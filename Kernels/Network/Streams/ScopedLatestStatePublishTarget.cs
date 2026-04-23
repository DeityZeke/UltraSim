namespace UltraSim.Network;

public readonly record struct ScopedLatestStatePublishTarget(
    SubscriptionKey ScopeKey,
    uint DeliveryCadenceTicks);
