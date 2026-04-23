namespace UltraSim.Network;

public readonly record struct LatestStateUpdate(
    ulong ItemKey,
    ulong Sequence,
    SubscriptionKey ScopeKey,
    PacketClass PacketClass,
    ImmutablePayloadBuffer Payload,
    uint DeliveryCadenceTicks = 1)
{
    public LatestStateUpdate(
        ulong itemKey,
        ulong sequence,
        SubscriptionKey scopeKey,
        PacketClass packetClass,
        ImmutablePayloadBuffer payload)
        : this(itemKey, sequence, scopeKey, packetClass, payload, 1)
    {
    }
}
