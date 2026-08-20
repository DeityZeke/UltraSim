namespace UltraSim.Client;

public readonly record struct ClientMovementCorrectionSnapshot(
    long SampleCount,
    double AveragePositionError,
    double MaxPositionError,
    double AverageVelocityError,
    double MaxVelocityError);
