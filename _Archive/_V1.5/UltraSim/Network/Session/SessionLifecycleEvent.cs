namespace UltraSim.Network
{
    public readonly struct SessionLifecycleEvent
    {
        public readonly SessionLifecycleEventKind Kind;
        public readonly long SessionId;
        public readonly int ConnectionId;
        public readonly SessionState PreviousState;
        public readonly SessionState CurrentState;
        public readonly SessionDisconnectReason DisconnectReason;
        public readonly long OpaqueGameIdentityKey;
        public readonly long TimestampTicks;

        public SessionLifecycleEvent(
            SessionLifecycleEventKind kind,
            long sessionId,
            int connectionId,
            SessionState previousState,
            SessionState currentState,
            SessionDisconnectReason disconnectReason,
            long opaqueGameIdentityKey,
            long timestampTicks)
        {
            Kind = kind;
            SessionId = sessionId;
            ConnectionId = connectionId;
            PreviousState = previousState;
            CurrentState = currentState;
            DisconnectReason = disconnectReason;
            OpaqueGameIdentityKey = opaqueGameIdentityKey;
            TimestampTicks = timestampTicks;
        }
    }
}
