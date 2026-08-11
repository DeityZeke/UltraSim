namespace UltraSim.Network;

public readonly record struct SubscriptionFanoutDispatchResult(
    int FrameCount,
    int SubscriberDeliveries,
    int DatagramCount,
    int CompressedCount,
    int EncryptedCount);
