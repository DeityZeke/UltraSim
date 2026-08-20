using UltraSim.Network;

namespace UltraSim.Client;

public delegate void ClientLatestStateFrameApplier(ulong itemKey, ulong sequence, ImmutablePayloadBuffer payload);
