namespace UltraSim.ECS;

/// <summary>
/// Immutable inspection snapshot for a registered system, including optional live timing data.
/// </summary>
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
