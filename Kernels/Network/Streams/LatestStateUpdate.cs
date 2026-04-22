namespace UltraSim.Network;

public readonly record struct LatestStateUpdate(
    ulong ItemKey,
    ulong Sequence,
    SubscriptionKey ScopeKey,
    PacketClass PacketClass,
    ImmutablePayloadBuffer Payload);
