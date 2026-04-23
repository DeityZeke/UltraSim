namespace UltraSim.Network;

public readonly record struct ReliableEventReplayEntry(
    ulong Sequence,
    long SentTick,
    SubscriptionKey ScopeKey,
    PacketClass PacketClass,
    ImmutablePayloadBuffer Payload);
