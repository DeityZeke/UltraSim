namespace UltraSim.Network;

public sealed class NetworkControlPlaneHost
{
    private readonly INetworkTransport _transport;
    private readonly PacketRegistry<DispatchContext> _registry;
    private readonly InboundDatagram[] _inboundScratch;
    private readonly NetworkControlPlaneRole _role;
    private readonly HandshakePolicy _handshakePolicy;
    private readonly HandshakeHelloPacket _clientHello;
    private readonly SessionAdmissionPolicy _sessionPolicy;
    private readonly List<PongProbePacket> _receivedPongs = new();
    private readonly Queue<ControlPlaneEvent> _events = new();
    private int _inboundDatagrams;
    private int _decodeFailures;
    private int _dispatchMisses;
    private int _handshakeStarts;
    private int _handshakeAccepts;
    private int _handshakeRejects;
    private int _admissionRejects;
    private int _pingProbesSent;
    private int _pingProbesReceived;
    private int _pongProbesSent;
    private int _pongProbesReceived;

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
        SessionAdmissionPolicy sessionPolicy = default,
        int maxPollBatch = 64)
    {
        if (maxPollBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPollBatch));

        _role = role;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _handshakePolicy = handshakePolicy;
        _clientHello = clientHello;
        _sessionPolicy = sessionPolicy.Equals(default(SessionAdmissionPolicy))
            ? SessionAdmissionPolicy.Default
            : sessionPolicy;
        Sessions = sessions ?? new NetworkSessionManager(_sessionPolicy);
        _registry = new PacketRegistry<DispatchContext>();
        _inboundScratch = new InboundDatagram[maxPollBatch];

        if (_role == NetworkControlPlaneRole.Server)
        {
            _registry.Register<HandshakeHelloPacket>(static (DispatchContext context, in HandshakeHelloPacket packet) =>
                context.Host.OnHandshakeHello(context.ConnectionId, context.NowTicks, packet));
            _registry.Register<PingProbePacket>(static (DispatchContext context, in PingProbePacket packet) =>
                context.Host.OnPingProbe(context.ConnectionId, context.NowTicks, packet));
        }
        else
        {
            _registry.Register<HandshakeResultPacket>(static (DispatchContext context, in HandshakeResultPacket packet) =>
                context.Host.OnHandshakeResult(context.ConnectionId, context.NowTicks, packet));
            _registry.Register<PongProbePacket>(static (DispatchContext context, in PongProbePacket packet) =>
                context.Host.OnPongProbe(context.ConnectionId, context.NowTicks, packet));
        }

        _registry.Seal();
    }

    public NetworkSessionManager Sessions { get; }

    public IReadOnlyList<PongProbePacket> ReceivedPongs => _receivedPongs;

    public bool IsRunning => _transport.IsRunning;

    public SessionMetricsSnapshot GetSessionMetricsSnapshot() => Sessions.GetMetricsSnapshot();

    public ControlPlaneMetricsSnapshot GetMetricsSnapshot() => new(
        InboundDatagrams: _inboundDatagrams,
        DecodeFailures: _decodeFailures,
        DispatchMisses: _dispatchMisses,
        HandshakeStarts: _handshakeStarts,
        HandshakeAccepts: _handshakeAccepts,
        HandshakeRejects: _handshakeRejects,
        AdmissionRejects: _admissionRejects,
        PingProbesSent: _pingProbesSent,
        PingProbesReceived: _pingProbesReceived,
        PongProbesSent: _pongProbesSent,
        PongProbesReceived: _pongProbesReceived);

    public void Start() => _transport.Start();

    public void Stop() => _transport.Stop();

    public SessionId BeginClientHandshake(ConnectionId connectionId, long nowTicks)
    {
        if (_role != NetworkControlPlaneRole.Client)
            throw new InvalidOperationException("Only client hosts can begin handshakes.");

        SessionId sessionId = Sessions.CreateSession(connectionId, nowTicks);
        Sessions.TryMarkHandshaking(sessionId, nowTicks);
        _handshakeStarts++;
        _events.Enqueue(new ControlPlaneEvent(
            ControlPlaneEventKind.HandshakeStarted,
            connectionId,
            sessionId,
            SessionDisconnectReason.None,
            HandshakeRejectReason.None,
            nowTicks));
        SendPacket(connectionId, _clientHello, PacketHeaderFlags.HighPriorityHint);
        _transport.FlushOutbound();
        return sessionId;
    }

    public int Pump(long nowTicks)
    {
        Sessions.SweepTimeouts(nowTicks);
        int total = PollAndProcessInbound(nowTicks);
        _transport.FlushOutbound();
        return total;
    }

    public int ProcessInbound(ReadOnlySpan<InboundDatagram> inbound, long nowTicks)
    {
        Sessions.SweepTimeouts(nowTicks);
        int processed = 0;
        for (int i = 0; i < inbound.Length; i++)
        {
            InboundDatagram datagram = inbound[i];
            if (datagram.Plane != NetworkPlane.Control)
                continue;

            processed++;
            _inboundDatagrams++;
            if (!PacketCodec.TryDecodeEnvelope(datagram.Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload))
            {
                _decodeFailures++;
                continue;
            }

            if (!_registry.TryDispatch(new DispatchContext(this, datagram.ConnectionId, nowTicks), header, payload))
                _dispatchMisses++;
        }

        return processed;
    }

    public void FlushOutbound() => _transport.FlushOutbound();

    public bool SendPingProbe(ConnectionId connectionId, ushort sequence, uint clientTick)
    {
        if (_role != NetworkControlPlaneRole.Client)
            return false;
        if (!Sessions.TryGetSessionIdByConnection(connectionId, out SessionId sessionId))
            return false;
        if (!Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) || snapshot.State != ConnectionSessionState.Ready)
            return false;

        _pingProbesSent++;
        SendPacket(connectionId, new PingProbePacket(sequence, clientTick), PacketHeaderFlags.HighPriorityHint);
        _transport.FlushOutbound();
        return true;
    }

    public int DrainEvents(Span<ControlPlaneEvent> destination)
    {
        int count = 0;
        while (count < destination.Length && _events.Count > 0)
            destination[count++] = _events.Dequeue();
        return count;
    }

    public int DrainReceivedPongs(Span<PongProbePacket> destination)
    {
        int count = Math.Min(destination.Length, _receivedPongs.Count);
        for (int i = 0; i < count; i++)
            destination[i] = _receivedPongs[i];

        if (count > 0)
            _receivedPongs.RemoveRange(0, count);

        return count;
    }

    private void OnHandshakeHello(ConnectionId connectionId, long nowTicks, in HandshakeHelloPacket packet)
    {
        if (!Sessions.TryGetSessionIdByConnection(connectionId, out SessionId sessionId))
        {
            if (!Sessions.TryCreateSession(connectionId, nowTicks, out sessionId, out SessionDisconnectReason rejectReason))
            {
                HandshakeRejectReason handshakeRejectReason = ToHandshakeRejectReason(rejectReason);
                _handshakeRejects++;
                _admissionRejects++;
                _events.Enqueue(new ControlPlaneEvent(
                    ControlPlaneEventKind.SessionAdmissionRejected,
                    connectionId,
                    SessionId.Invalid,
                    rejectReason,
                    handshakeRejectReason,
                    nowTicks));
                SendPacket(connectionId, new HandshakeResultPacket(false, handshakeRejectReason), PacketHeaderFlags.HighPriorityHint);
                return;
            }
        }

        Sessions.TryMarkHandshaking(sessionId, nowTicks);
        HandshakeValidationResult result = HandshakeGate.Validate(packet, _handshakePolicy);
        if (result.Accepted)
        {
            Sessions.TryMarkAuthenticated(sessionId, nowTicks);
            Sessions.TryMarkReady(sessionId, nowTicks);
            _handshakeAccepts++;
            _events.Enqueue(new ControlPlaneEvent(
                ControlPlaneEventKind.HandshakeAccepted,
                connectionId,
                sessionId,
                SessionDisconnectReason.None,
                HandshakeRejectReason.None,
                nowTicks));
            SendPacket(connectionId, new HandshakeResultPacket(true), PacketHeaderFlags.HighPriorityHint);
            return;
        }

        Sessions.TryBeginDisconnect(sessionId, SessionDisconnectReason.Rejected, nowTicks);
        Sessions.TryCompleteDisconnect(sessionId, nowTicks);
        _handshakeRejects++;
        _events.Enqueue(new ControlPlaneEvent(
            ControlPlaneEventKind.HandshakeRejected,
            connectionId,
            sessionId,
            SessionDisconnectReason.Rejected,
            result.RejectReason,
            nowTicks));
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
            _handshakeAccepts++;
            _events.Enqueue(new ControlPlaneEvent(
                ControlPlaneEventKind.HandshakeAccepted,
                connectionId,
                sessionId,
                SessionDisconnectReason.None,
                HandshakeRejectReason.None,
                nowTicks));
            return;
        }

        Sessions.TryBeginDisconnect(sessionId, SessionDisconnectReason.Rejected, nowTicks);
        Sessions.TryCompleteDisconnect(sessionId, nowTicks);
        _handshakeRejects++;
        _events.Enqueue(new ControlPlaneEvent(
            ControlPlaneEventKind.HandshakeRejected,
            connectionId,
            sessionId,
            SessionDisconnectReason.Rejected,
            packet.RejectReason,
            nowTicks));
    }

    private void OnPingProbe(ConnectionId connectionId, long nowTicks, in PingProbePacket packet)
    {
        if (!Sessions.TryGetSessionIdByConnection(connectionId, out SessionId sessionId))
            return;
        if (!Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) || snapshot.State != ConnectionSessionState.Ready)
            return;

        Sessions.TryTouchActivity(sessionId, nowTicks);
        _pingProbesReceived++;
        _pongProbesSent++;
        SendPacket(connectionId, new PongProbePacket(packet.Sequence, packet.ClientTick, unchecked((uint)nowTicks)), PacketHeaderFlags.HighPriorityHint);
    }

    private void OnPongProbe(ConnectionId connectionId, long nowTicks, in PongProbePacket packet)
    {
        if (!Sessions.TryGetSessionIdByConnection(connectionId, out SessionId sessionId))
            return;
        Sessions.TryTouchActivity(sessionId, nowTicks);
        _pongProbesReceived++;
        _events.Enqueue(new ControlPlaneEvent(
            ControlPlaneEventKind.PongReceived,
            connectionId,
            sessionId,
            SessionDisconnectReason.None,
            HandshakeRejectReason.None,
            nowTicks));
        _receivedPongs.Add(packet);
    }

    private static HandshakeRejectReason ToHandshakeRejectReason(SessionDisconnectReason reason)
    {
        return reason switch
        {
            SessionDisconnectReason.DuplicateSession => HandshakeRejectReason.DuplicateSession,
            SessionDisconnectReason.ServerBusy => HandshakeRejectReason.ServerBusy,
            _ => HandshakeRejectReason.None,
        };
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

    private int PollAndProcessInbound(long nowTicks)
    {
        int total = 0;
        while (true)
        {
            int count = _transport.PollInbound(_inboundScratch);
            if (count == 0)
                break;

            total += ProcessInbound(_inboundScratch.AsSpan(0, count), nowTicks);
            if (count < _inboundScratch.Length)
                break;
        }

        return total;
    }
}
