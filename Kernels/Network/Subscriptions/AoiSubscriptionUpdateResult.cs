namespace UltraSim.Network;

public readonly record struct AoiSubscriptionUpdateResult(
    int DesiredScopeCount,
    int AddedCount,
    int RemovedCount,
    bool Changed);
