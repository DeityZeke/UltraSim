namespace UltraSim.Network;

public readonly record struct OrderedEventEntry(
    ulong Sequence,
    SubscriptionKey ScopeKey,
    ImmutablePayloadBuffer Payload);
