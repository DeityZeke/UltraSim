namespace UltraSim.Network;

public readonly record struct SessionLifecycleEvent(
    SessionLifecycleEventKind Kind,
    SessionId SessionId,
    ConnectionId ConnectionId,
    ConnectionSessionState PreviousState,
    ConnectionSessionState CurrentState,
    SessionDisconnectReason DisconnectReason,
    long TimestampTicks);
