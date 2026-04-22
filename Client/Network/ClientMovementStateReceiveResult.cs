namespace UltraSim.Client;

public readonly record struct ClientMovementStateReceiveResult(
    uint NetworkEntityId,
    uint ServerTick,
    ushort LastProcessedInputSequence,
    int RemovedPendingInputs,
    int RemovedPredictions);
