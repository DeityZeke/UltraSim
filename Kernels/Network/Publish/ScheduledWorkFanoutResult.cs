namespace UltraSim.Network;

public readonly record struct ScheduledWorkFanoutResult(
    int ProjectedFrameCount,
    int SubscriberDeliveries,
    int DatagramCount,
    int CompressedCount);
