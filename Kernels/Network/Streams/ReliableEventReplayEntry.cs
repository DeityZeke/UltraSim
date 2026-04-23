namespace UltraSim.Network;

public readonly record struct ReliableEventReplayEntry(
    ulong Sequence,
    SubscriptionKey ScopeKey,
    PacketClass PacketClass,
    ImmutablePayloadBuffer Payload);
