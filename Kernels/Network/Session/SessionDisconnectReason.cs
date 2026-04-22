namespace UltraSim.Network;

public enum SessionDisconnectReason : byte
{
    None = 0,
    Rejected = 1,
    Timeout = 2,
    Closed = 3,
    Unknown = 4,
}
