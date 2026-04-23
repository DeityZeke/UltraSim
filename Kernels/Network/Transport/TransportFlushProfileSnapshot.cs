namespace UltraSim.Network;

public readonly record struct TransportFlushProfileSnapshot(
    long DrainElapsedTicks,
    long BundleElapsedTicks,
    long SendElapsedTicks,
    int ConnectionCount,
    int LogicalDatagramCount,
    int WireDatagramCount,
    int BundledWireDatagramCount,
    int SingleWireDatagramCount,
    bool AsyncEnabled,
    bool AsyncStaged,
    int AsyncPendingBatchCount,
    int AsyncMaxPendingBatchCount,
    int AsyncInlineFallbackCount);
