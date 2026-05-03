namespace UltraSim.Server;

public readonly record struct ServerSystemRuntimeSnapshot(
    string Name,
    string Stage,
    bool Enabled,
    bool Manual,
    int PeriodTicks,
    int StartOffset,
    uint LastRunTick,
    long UpdateCount,
    double LastUpdateMilliseconds,
    double AverageUpdateMilliseconds,
    double PeakUpdateMilliseconds,
    double MinUpdateMilliseconds,
    int ControlCount,
    string Detail = "");
