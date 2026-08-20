namespace UltraSim.Network
{
    public readonly struct QueuedInboundCommand<TCommand>
    {
        public readonly long SessionId;
        public readonly TCommand Command;

        public QueuedInboundCommand(long sessionId, in TCommand command)
        {
            SessionId = sessionId;
            Command = command;
        }
    }
}
