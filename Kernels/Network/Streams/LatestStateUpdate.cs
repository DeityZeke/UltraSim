namespace UltraSim.Network;

public readonly record struct LatestStateUpdate(
    ulong ItemKey,
    ulong Sequence,
    SubscriptionKey ScopeKey,
    ImmutablePayloadBuffer Payload);
