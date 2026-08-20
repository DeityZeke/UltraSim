namespace UltraSim.Network;

public readonly record struct ConnectionRecoveryPlanResult(
    ConnectionRecoveryMode Mode,
    int ScheduledWorkCount,
    int BaselineDatagramCount,
    ConnectionScheduleState NextState,
    StateBaselineDescriptor BaselineDescriptor,
    bool OrderedEventRecoveryRequired);
