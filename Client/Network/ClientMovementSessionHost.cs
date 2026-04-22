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
    private readonly InboundDatagram[] _inboundScratch;
    private SessionId _sessionId;

    public ClientMovementSessionHost(
        INetworkTransport transport,
        ConnectionId connectionId,
        HandshakeHelloPacket clientHello,
        ClientInputSequencer<ClientMovementInput>? sequencer = null,
        ClientPredictionStore<ClientMovementState>? predictionStore = null,
        ClientInterpolationBuffer<ClientMovementState>? interpolation = null,
        int maxPollBatch = 64)
    {
        if (maxPollBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPollBatch));

        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _control = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, transport, clientHello: clientHello);
        _movement = new ClientMovementNetworkBridge(transport, connectionId, sequencer, predictionStore, interpolation);
        _inboundScratch = new InboundDatagram[maxPollBatch];
    }

    public ConnectionId ConnectionId => _movement.ConnectionId;
    public NetworkControlPlaneHost Control => _control;
    public ClientMovementNetworkBridge Movement => _movement;
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

    public SessionId BeginHandshake(long nowTicks)
    {
        _sessionId = _control.BeginClientHandshake(ConnectionId, nowTicks);
        return _sessionId;
    }

    public int Pump(long nowTicks)
    {
        int totalCount = 0;
        while (true)
        {
            int count = _transport.PollInbound(_inboundScratch);
            if (count <= 0)
                break;

            ReadOnlySpan<InboundDatagram> inbound = _inboundScratch.AsSpan(0, count);
            _control.ProcessInbound(inbound, nowTicks);
            _movement.ProcessInbound(inbound);
            totalCount += count;

            if (count < _inboundScratch.Length)
                break;
        }

        _control.FlushOutbound();
        return totalCount;
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
}
