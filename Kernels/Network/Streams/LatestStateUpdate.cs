namespace UltraSim.Network;

public readonly record struct LatestStateUpdate(
    ulong ItemKey,
    ulong Sequence,
    ImmutablePayloadBuffer Payload);
