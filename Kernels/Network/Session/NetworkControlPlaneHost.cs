namespace UltraSim.Network;

public sealed class NetworkControlPlaneHost
{
    private readonly INetworkTransport _transport;
    private readonly PacketRegistry<DispatchContext> _registry;
    private readonly InboundDatagram[] _inboundScratch;
    private readonly NetworkControlPlaneRole _role;
    private readonly HandshakePolicy _handshakePolicy;
    private readonly HandshakeHelloPacket _clientHello;

    private readonly record struct DispatchContext(
        NetworkControlPlaneHost Host,
        ConnectionId ConnectionId,
        long NowTicks);

    public NetworkControlPlaneHost(
        NetworkControlPlaneRole role,
        INetworkTransport transport,
        HandshakePolicy handshakePolicy = default,
        HandshakeHelloPacket clientHello = default,
        NetworkSessionManager? sessions = null,
        int maxPollBatch = 64)
    {
        if (maxPollBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPollBatch));

        _role = role;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _handshakePolicy = handshakePolicy;
        _clientHello = clientHello;
        Sessions = sessions ?? new NetworkSessionManager();
        _registry = new PacketRegistry<DispatchContext>();
        _inboundScratch = new InboundDatagram[maxPollBatch];

        if (_role == NetworkControlPlaneRole.Server)
        {
            _registry.Register<HandshakeHelloPacket>(static (DispatchContext context, in HandshakeHelloPacket packet) =>
                context.Host.OnHandshakeHello(context.ConnectionId, context.NowTicks, packet));
        }
        else
        {
            _registry.Register<HandshakeResultPacket>(static (DispatchContext context, in HandshakeResultPacket packet) =>
                context.Host.OnHandshakeResult(context.ConnectionId, context.NowTicks, packet));
        }

        _registry.Seal();
    }

    public NetworkSessionManager Sessions { get; }

    public bool IsRunning => _transport.IsRunning;

    public void Start() => _transport.Start();

    public void Stop() => _transport.Stop();

    public SessionId BeginClientHandshake(ConnectionId connectionId, long nowTicks)
    {
        if (_role != NetworkControlPlaneRole.Client)
            throw new InvalidOperationException("Only client hosts can begin handshakes.");

        SessionId sessionId = Sessions.CreateSession(connectionId, nowTicks);
        Sessions.TryMarkHandshaking(sessionId, nowTicks);
        SendPacket(connectionId, _clientHello, PacketHeaderFlags.HighPriorityHint);
        return sessionId;
    }

    public int Pump(long nowTicks)
    {
        int total = 0;
        while (true)
        {
            int count = _transport.PollInbound(_inboundScratch);
            if (count == 0)
                break;

            total += count;
            for (int i = 0; i < count; i++)
            {
                InboundDatagram datagram = _inboundScratch[i];
                if (!PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
                    continue;

                _registry.TryDispatch(new DispatchContext(this, datagram.ConnectionId, nowTicks), header, payload);
            }

            if (count < _inboundScratch.Length)
                break;
        }

        _transport.FlushOutbound();
        return total;
    }

    private void OnHandshakeHello(ConnectionId connectionId, long nowTicks, in HandshakeHelloPacket packet)
    {
        if (!Sessions.TryGetSessionIdByConnection(connectionId, out SessionId sessionId))
            sessionId = Sessions.CreateSession(connectionId, nowTicks);

        Sessions.TryMarkHandshaking(sessionId, nowTicks);
        HandshakeValidationResult result = HandshakeGate.Validate(packet, _handshakePolicy);
        if (result.Accepted)
        {
            Sessions.TryMarkAuthenticated(sessionId, nowTicks);
            Sessions.TryMarkReady(sessionId, nowTicks);
            SendPacket(connectionId, new HandshakeResultPacket(true), PacketHeaderFlags.HighPriorityHint);
            return;
        }

        Sessions.TryBeginDisconnect(sessionId, SessionDisconnectReason.Rejected, nowTicks);
        Sessions.TryCompleteDisconnect(sessionId, nowTicks);
        SendPacket(connectionId, new HandshakeResultPacket(false, result.RejectReason, result.MissingFeatures), PacketHeaderFlags.HighPriorityHint);
    }

    private void OnHandshakeResult(ConnectionId connectionId, long nowTicks, in HandshakeResultPacket packet)
    {
        if (!Sessions.TryGetSessionIdByConnection(connectionId, out SessionId sessionId))
            return;

        if (packet.Accepted)
        {
            Sessions.TryMarkAuthenticated(sessionId, nowTicks);
            Sessions.TryMarkReady(sessionId, nowTicks);
            return;
        }

        Sessions.TryBeginDisconnect(sessionId, SessionDisconnectReason.Rejected, nowTicks);
        Sessions.TryCompleteDisconnect(sessionId, nowTicks);
    }

    private void SendPacket<TPacket>(ConnectionId connectionId, in TPacket packet, PacketHeaderFlags flags)
        where TPacket : struct, IPacket<TPacket>
    {
        ImmutablePayloadBuffer buffer = PacketCodec.Encode(packet, flags);
        _transport.EnqueueOutbound(new OutboundDatagram(
            connectionId,
            TPacket.Plane,
            TPacket.PacketClass,
            TPacket.Channel,
            buffer));
    }
}
