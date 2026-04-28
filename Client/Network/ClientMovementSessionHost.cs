using System.Diagnostics;
using UltraSim.Network;

namespace UltraSim.Client;

/// <summary>
/// Narrow live client session host that keeps control-plane handshake and movement traffic
/// on the same transport poll path.
/// </summary>
public sealed class ClientMovementSessionHost
{
    private readonly INetworkTransport _transport;
    private readonly NetworkControlPlaneHost _control;
    private readonly ClientMovementNetworkBridge _movement;
    private readonly ClientObserverStateBridge? _observerState;
    private readonly ClientObserverEventBridge? _observerEvents;
    private readonly ClientChatEventBridge? _chatEvents;
    private readonly InboundDatagram[] _inboundScratch;
    private SessionId _sessionId;

    public ClientMovementSessionHost(
        INetworkTransport transport,
        ConnectionId connectionId,
        HandshakeHelloPacket clientHello,
        ClientInputSequencer<ClientMovementInput>? sequencer = null,
        ClientPredictionStore<ClientMovementState>? predictionStore = null,
        ClientInterpolationBuffer<ClientMovementState>? interpolation = null,
        ClientObserverStateBridge? observerState = null,
        ClientObserverEventBridge? observerEvents = null,
        ClientChatEventBridge? chatEvents = null,
        int maxPollBatch = 64)
    {
        if (maxPollBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPollBatch));

        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _control = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, transport, clientHello: clientHello);
        _movement = new ClientMovementNetworkBridge(transport, connectionId, sequencer, predictionStore, interpolation);
        _observerState = observerState;
        _observerEvents = observerEvents ?? (chatEvents is null
            ? null
            : ClientObserverEventBridge.CreateSpanFastPath(connectionId, (_, payload) => chatEvents.TryProcessEventPayload(payload)));
        if (_observerEvents is not null && chatEvents is not null)
            _observerEvents.ChatEvents = chatEvents;
        _chatEvents = chatEvents;
        _inboundScratch = new InboundDatagram[maxPollBatch];
    }

    public ConnectionId ConnectionId => _movement.ConnectionId;
    public NetworkControlPlaneHost Control => _control;
    public ClientMovementNetworkBridge Movement => _movement;
    public ClientObserverStateBridge? ObserverState => _observerState;
    public ClientObserverEventBridge? ObserverEvents => _observerEvents;
    public ClientChatEventBridge? ChatEvents => _chatEvents;
    public ClientPumpProfileSnapshot LastPumpProfile { get; private set; }
    public SessionId SessionId => _sessionId;
    public bool IsRunning => _transport.IsRunning;

    public bool IsReady
    {
        get
        {
            if (!_sessionId.IsValid)
                return false;

            return _control.Sessions.TryGetSnapshot(_sessionId, out SessionSnapshot snapshot) &&
                   snapshot.State == ConnectionSessionState.Ready;
        }
    }

    public void Start() => _control.Start();

    public void Stop() => _control.Stop();

    public bool SendDisconnectNotice(SessionDisconnectReason reason, long nowTicks)
    {
        return _control.SendClientDisconnectNotice(ConnectionId, reason, nowTicks);
    }

    public SessionId BeginHandshake(long nowTicks)
    {
        _sessionId = _control.BeginClientHandshake(ConnectionId, nowTicks);
        return _sessionId;
    }

    public int Pump(long nowTicks)
    {
        int totalCount = 0;
        long pollElapsedTicks = 0;
        long pollPendingDrainElapsedTicks = 0;
        long pollSocketReceiveElapsedTicks = 0;
        long pollBundleUnpackElapsedTicks = 0;
        long pollBundleIndexElapsedTicks = 0;
        long pollBundleMaterializeElapsedTicks = 0;
        long pollBundleSliceElapsedTicks = 0;
        long pollBundleDatagramCreateElapsedTicks = 0;
        long pollBundleEnqueueElapsedTicks = 0;
        long pollPayloadCopyElapsedTicks = 0;
        long pollEnvelopeParseElapsedTicks = 0;
        long pollAsyncReceiveElapsedTicks = 0;
        int pollAsyncQueuedDatagramCount = 0;
        long controlElapsedTicks = 0;
        long movementElapsedTicks = 0;
        long observerStateElapsedTicks = 0;
        long observerStateDecodeElapsedTicks = 0;
        long observerStateApplyElapsedTicks = 0;
        long observerEventElapsedTicks = 0;
        StateBaselineDescriptor? completedBaseline = null;
        _observerState?.BeginPump();
        _observerEvents?.BeginPump();
        _chatEvents?.BeginPump();

        while (true)
        {
            long pollStart = Stopwatch.GetTimestamp();
            int count = _transport.PollInbound(_inboundScratch);
            pollElapsedTicks += Stopwatch.GetTimestamp() - pollStart;
            if (_transport is UdpSocketNetworkTransport udpTransport)
            {
                TransportPollProfileSnapshot pollProfile = udpTransport.LastPollProfile;
                pollPendingDrainElapsedTicks += pollProfile.PendingDrainElapsedTicks;
                pollSocketReceiveElapsedTicks += pollProfile.SocketReceiveElapsedTicks;
                pollBundleUnpackElapsedTicks += pollProfile.BundleUnpackElapsedTicks;
                pollBundleIndexElapsedTicks += pollProfile.BundleIndexElapsedTicks;
                pollBundleMaterializeElapsedTicks += pollProfile.BundleMaterializeElapsedTicks;
                pollBundleSliceElapsedTicks += pollProfile.BundleSliceElapsedTicks;
                pollBundleDatagramCreateElapsedTicks += pollProfile.BundleDatagramCreateElapsedTicks;
                pollBundleEnqueueElapsedTicks += pollProfile.BundleEnqueueElapsedTicks;
                pollPayloadCopyElapsedTicks += pollProfile.PayloadCopyElapsedTicks;
                pollEnvelopeParseElapsedTicks += pollProfile.EnvelopeParseElapsedTicks;
                pollAsyncReceiveElapsedTicks += pollProfile.AsyncReceiveElapsedTicks;
                pollAsyncQueuedDatagramCount += pollProfile.AsyncQueuedDatagramCount;
            }
            if (count <= 0)
                break;

            ReadOnlySpan<InboundDatagram> inbound = _inboundScratch.AsSpan(0, count);
            long controlStart = Stopwatch.GetTimestamp();
            _control.ProcessInbound(inbound, nowTicks);
            controlElapsedTicks += Stopwatch.GetTimestamp() - controlStart;

            for (int i = 0; i < count; i++)
            {
                ref readonly InboundDatagram datagram = ref inbound[i];
                if (datagram.ConnectionId != ConnectionId || datagram.Plane != NetworkPlane.State)
                    continue;

                long dispatchStart = Stopwatch.GetTimestamp();
                if (_movement.TryProcessEnvelope(datagram.Header, datagram.EnvelopePayloadSpan))
                {
                    movementElapsedTicks += Stopwatch.GetTimestamp() - dispatchStart;
                    continue;
                }

                dispatchStart = Stopwatch.GetTimestamp();
                if (_observerState?.TryProcessEnvelope(datagram.Header, datagram.EnvelopePayloadSpan) == true)
                {
                    observerStateElapsedTicks += Stopwatch.GetTimestamp() - dispatchStart;
                    ClientObserverStateProfileSnapshot observerProfile = _observerState.LastProfile;
                    observerStateDecodeElapsedTicks = observerProfile.DecodeElapsedTicks;
                    observerStateApplyElapsedTicks = observerProfile.ApplyElapsedTicks;
                    if (_observerState.TryDequeueCompletedBaseline(out StateBaselineDescriptor baseline))
                    {
                        _observerEvents?.ResetToSequence(baseline.LastOrderedEventSequence);
                        completedBaseline = baseline;
                    }
                    continue;
                }

                dispatchStart = Stopwatch.GetTimestamp();
                if (_observerEvents?.TryProcessEnvelope(datagram.Header, datagram.EnvelopePayloadSpan) == true)
                {
                    observerEventElapsedTicks += Stopwatch.GetTimestamp() - dispatchStart;
                    continue;
                }
            }

            totalCount += count;
            DisposeInbound(inbound);

            if (count < _inboundScratch.Length)
                break;
        }

        if (_observerEvents is not null &&
            _observerEvents.TryDequeueAckPacket(out ReliableEventAckPacket ackPacket))
        {
            _transport.EnqueueOutbound(new OutboundDatagram(
                ConnectionId,
                ReliableEventAckPacket.Plane,
                ReliableEventAckPacket.PacketClass,
                ReliableEventAckPacket.Channel,
                PacketCodec.Encode(ackPacket)));
        }

        if (completedBaseline.HasValue)
        {
            _transport.EnqueueOutbound(new OutboundDatagram(
                ConnectionId,
                StateBaselineAckPacket.Plane,
                StateBaselineAckPacket.PacketClass,
                StateBaselineAckPacket.Channel,
                PacketCodec.Encode(new StateBaselineAckPacket(completedBaseline.Value))));
        }

        _control.FlushOutbound();
        LastPumpProfile = new ClientPumpProfileSnapshot(
            totalCount,
            pollElapsedTicks,
            pollPendingDrainElapsedTicks,
            pollSocketReceiveElapsedTicks,
            pollBundleUnpackElapsedTicks,
            pollBundleIndexElapsedTicks,
            pollBundleMaterializeElapsedTicks,
            pollBundleSliceElapsedTicks,
            pollBundleDatagramCreateElapsedTicks,
            pollBundleEnqueueElapsedTicks,
            pollPayloadCopyElapsedTicks,
            pollEnvelopeParseElapsedTicks,
            pollAsyncReceiveElapsedTicks,
            pollAsyncQueuedDatagramCount,
            controlElapsedTicks,
            movementElapsedTicks,
            observerStateElapsedTicks,
            observerStateDecodeElapsedTicks,
            observerStateApplyElapsedTicks,
            observerEventElapsedTicks);
        return totalCount;
    }

    private static void DisposeInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
            inbound[i].Dispose();
    }

    public bool TrySendInput(
        in ClientMovementInput input,
        uint clientTick,
        in ClientMovementState predictedState,
        out ClientMovementInputSendResult result)
    {
        if (!IsReady)
        {
            result = default;
            return false;
        }

        bool sent = _movement.TrySendInput(input, clientTick, predictedState, out result);
        if (sent)
            _transport.FlushOutbound();
        return sent;
    }

    public bool TrySendChat(string text)
    {
        if (!IsReady)
            return false;

        _transport.EnqueueOutbound(new OutboundDatagram(
            ConnectionId,
            ChatSubmitPacket.Plane,
            ChatSubmitPacket.PacketClass,
            ChatSubmitPacket.Channel,
            PacketCodec.Encode(new ChatSubmitPacket(text))));
        _transport.FlushOutbound();
        return true;
    }
}
