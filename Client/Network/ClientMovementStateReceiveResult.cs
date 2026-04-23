namespace UltraSim.Client;

public readonly record struct ClientMovementStateReceiveResult(
    uint NetworkEntityId,
    uint ServerTick,
    ushort LastProcessedInputSequence,
    ClientMovementState AuthoritativeState,
    bool HadPredictedState,
    float PositionErrorMagnitude,
    float VelocityErrorMagnitude,
    int RemovedPendingInputs,
    int RemovedPredictions);
