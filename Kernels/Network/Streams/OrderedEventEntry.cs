namespace UltraSim.Network;

public readonly record struct OrderedEventEntry(
    ulong Sequence,
    ImmutablePayloadBuffer Payload);
