namespace UltraSim.Client;

public enum ClientConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Handshaking = 2,
    Ready = 3,
    Faulted = 4,
}
