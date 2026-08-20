namespace UltraSim.Client;

public readonly record struct ClientObserverStateProfileSnapshot(
    long DecodeElapsedTicks,
    long ApplyElapsedTicks,
    int ReceivedFrameCount,
    int AppliedFrameCount,
    int ReceivedBaselineFrameCount,
    int AppliedBaselineFrameCount,
    int ReceivedDeltaFrameCount,
    int AppliedDeltaFrameCount);
