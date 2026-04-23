namespace UltraSim.Network;

public readonly record struct ScheduledWorkItem(
    ScheduledWorkKind Kind,
    PacketClass PacketClass,
    ulong Sequence,
    ulong ItemKey,
    SubscriptionKey ScopeKey,
    ImmutablePayloadBuffer Payload,
    uint DeliveryCadenceTicks = 1)
{
    public ScheduledWorkItem(
        ScheduledWorkKind kind,
        PacketClass packetClass,
        ulong sequence,
        ulong itemKey,
        SubscriptionKey scopeKey,
        ImmutablePayloadBuffer payload)
        : this(kind, packetClass, sequence, itemKey, scopeKey, payload, 1)
    {
    }
}
