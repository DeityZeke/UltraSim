namespace UltraSim.Network;

public readonly record struct ScheduledWorkItem(
    ScheduledWorkKind Kind,
    PacketClass PacketClass,
    ulong Sequence,
    ulong ItemKey,
    SubscriptionKey ScopeKey,
    ImmutablePayloadBuffer Payload);
