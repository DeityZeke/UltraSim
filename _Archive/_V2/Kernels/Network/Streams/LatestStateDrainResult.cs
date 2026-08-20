namespace UltraSim.Network;

public readonly record struct LatestStateDrainResult(
    int Count,
    LatestStateCursor NextCursor,
    ulong HighestPublishedSequence);
