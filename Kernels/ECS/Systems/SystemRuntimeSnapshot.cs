namespace UltraSim.ECS;

public readonly record struct SystemRuntimeSnapshot(
    SystemId Id,
    string Name,
    SystemStage Stage,
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
    IReadOnlyList<SystemControlDescriptor> Controls);
