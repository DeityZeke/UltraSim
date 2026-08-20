namespace UltraSim.Network
{
    public enum SessionDisconnectReason : byte
    {
        None = 0,
        Unknown = 1,
        Timeout = 2,
        DuplicateSession = 3,
        ProtocolViolation = 4,
        ServerShutdown = 5,
        Kicked = 6,
        ServerBusy = 7,
        IpRejected = 8,
    }
}
