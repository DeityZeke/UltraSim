namespace UltraSim.Network;

public readonly record struct ControlPlaneEvent(
    ControlPlaneEventKind Kind,
    ConnectionId ConnectionId,
    SessionId SessionId,
    SessionDisconnectReason DisconnectReason,
    HandshakeRejectReason HandshakeRejectReason,
    long Ticks);
