namespace UltraSim.Network
{
    public readonly struct SessionSnapshot
    {
        public readonly long SessionId;
        public readonly int ConnectionId;
        public readonly SessionState State;
        public readonly SessionDisconnectReason DisconnectReason;
        public readonly long OpaqueGameIdentityKey;
        public readonly long CreatedTicks;
        public readonly long LastActivityTicks;

        public SessionSnapshot(
            long sessionId,
            int connectionId,
            SessionState state,
            SessionDisconnectReason disconnectReason,
            long opaqueGameIdentityKey,
            long createdTicks,
            long lastActivityTicks)
        {
            SessionId = sessionId;
            ConnectionId = connectionId;
            State = state;
            DisconnectReason = disconnectReason;
            OpaqueGameIdentityKey = opaqueGameIdentityKey;
            CreatedTicks = createdTicks;
            LastActivityTicks = lastActivityTicks;
        }
    }
}
