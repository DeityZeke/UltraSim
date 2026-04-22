namespace UltraSim.Network;

public readonly record struct ConnectionScheduleResult(
    int Count,
    ConnectionScheduleState NextState,
    bool OrderedEventRecoveryRequired,
    int RealtimeItemsScheduled,
    int ReliableEventItemsScheduled,
    int RealtimeBytesScheduled,
    int ReliableEventBytesScheduled);
