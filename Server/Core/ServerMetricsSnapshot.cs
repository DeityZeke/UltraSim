namespace UltraSim.Server;

public readonly record struct ServerMetricsSnapshot(
    long TotalTicks,
    long TotalCommands,
    long TotalSaveRequests,
    long TotalCompletedSaves,
    long TotalFaults,
    double MeanTickMilliseconds,
    double MaxTickMilliseconds);
