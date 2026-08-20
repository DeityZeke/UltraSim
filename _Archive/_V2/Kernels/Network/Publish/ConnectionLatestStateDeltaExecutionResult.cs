namespace UltraSim.Network;

public readonly record struct ConnectionLatestStateDeltaExecutionResult(
    int DeltaItemCount,
    int DatagramCount,
    uint BaselineId,
    ulong HighestSequence);
