namespace UltraSim.Network;

public readonly record struct ConnectionLatestStateDeltaPlanResult(
    int Count,
    ulong HighestPlannedSequence);
