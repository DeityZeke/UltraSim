namespace UltraSim.Network;

public readonly record struct ChunkSubscriptionScope(
    int RingIndex,
    ChunkCoord Chunk,
    SubscriptionKey Key);
