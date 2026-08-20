namespace UltraSim.Network;

public readonly record struct ScopedPublishedFrame(
    SubscriptionKey ScopeKey,
    PublishedFrame Frame);
