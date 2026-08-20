namespace UltraSim.Network;

public readonly record struct LatestStateSnapshot(
    int ItemCount,
    ulong HighestPublishedSequence);
