namespace UltraSim.Network
{
    public delegate bool ConnectionSessionResolver(int connectionId, out long sessionId);

    public delegate void InboundCommandDecoder<TCommand>(
        long sessionId,
        in InboundDatagram datagram,
        NetworkInboundCommandQueue<TCommand> queue);

    public delegate void InboundCommandHandler<TCommand>(in QueuedInboundCommand<TCommand> command);

    public delegate void InboundRateLimitStrikeHandler(long sessionId, int strikeCount);
}
