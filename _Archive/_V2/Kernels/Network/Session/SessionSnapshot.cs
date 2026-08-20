namespace UltraSim.Network;

public readonly record struct SessionSnapshot(
    SessionId SessionId,
    ConnectionId ConnectionId,
    ConnectionSessionState State,
    SessionDisconnectReason DisconnectReason,
    long CreatedTicks,
    long LastActivityTicks);
