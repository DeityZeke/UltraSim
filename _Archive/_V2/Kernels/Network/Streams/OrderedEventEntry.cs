namespace UltraSim.Network;

public readonly record struct OrderedEventEntry(
    ulong Sequence,
    SubscriptionKey ScopeKey,
    PacketClass PacketClass,
    ImmutablePayloadBuffer Payload);
