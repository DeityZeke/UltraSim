namespace UltraSim.Network;

public enum ConnectionSessionState : byte
{
    Connecting = 0,
    Handshaking = 1,
    Authenticated = 2,
    Ready = 3,
    Disconnecting = 4,
    Disconnected = 5,
}
