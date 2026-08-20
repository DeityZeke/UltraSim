namespace UltraSim.Network;

public readonly record struct TransportPollProfileSnapshot(
    long PendingDrainElapsedTicks,
    long SocketReceiveElapsedTicks,
    long BundleUnpackElapsedTicks,
    long BundleIndexElapsedTicks,
    long BundleMaterializeElapsedTicks,
    long BundleSliceElapsedTicks,
    long BundleDatagramCreateElapsedTicks,
    long BundleEnqueueElapsedTicks,
    long PayloadCopyElapsedTicks,
    long EnvelopeParseElapsedTicks,
    int QueuedDatagramCount,
    bool AsyncEnabled,
    int AsyncQueuedDatagramCount,
    long AsyncReceiveElapsedTicks);
