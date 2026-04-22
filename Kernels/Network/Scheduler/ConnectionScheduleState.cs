namespace UltraSim.Network;

public readonly record struct ConnectionScheduleState(
    LatestStateCursor LatestStateCursor,
    OrderedEventCursor OrderedEventCursor);
