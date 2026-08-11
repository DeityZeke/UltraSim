namespace UltraSim.Network;

public enum ControlPlaneEventKind : byte
{
    HandshakeStarted = 0,
    HandshakeAccepted = 1,
    HandshakeRejected = 2,
    SessionAdmissionRejected = 3,
    PongReceived = 4,
    ServerDisconnected = 5,
    ClientDisconnected = 6,
}
