using UltraSim.Network;

namespace UltraSim.Client;

public delegate void ClientReliableEventFrameApplier(
    ulong sequence,
    ImmutablePayloadBuffer payload);
