namespace UltraSim.Network;

public readonly record struct TransportPollProfileSnapshot(
    long PendingDrainElapsedTicks,
    long SocketReceiveElapsedTicks,
    long BundleUnpackElapsedTicks,
    long PayloadCopyElapsedTicks,
    long EnvelopeParseElapsedTicks,
    int QueuedDatagramCount,
    bool AsyncEnabled,
    int AsyncQueuedDatagramCount,
    long AsyncReceiveElapsedTicks);
