using System.Diagnostics;
using System.Net;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

public sealed class GodotClientNetworkSessionDriver : IDisposable
{
    private readonly Action<string> _statusSink;
    private readonly Action<ChatMessageEventPacket> _chatSink;
    private readonly Action<EntityPresentationEventPacket>? _presentationSink;
    private readonly Action<ClientMovementStateReceiveResult> _ownerMovementSink;
    private readonly ClientLatestStateFrameSpanApplier? _observerStateSink;
    private readonly Action? _observerStateReset;
    private readonly Action<string>? _sessionFailedSink;
    private readonly Action<UiPanelOpenDocument>? _uiOpenSink;
    private readonly Action<UiModelUpdateDocument>? _uiModelUpdateSink;
    private ClientMovementSessionHost? _host;
    private UdpSocketNetworkTransport? _transport;
    private readonly PongProbePacket[] _pongScratch = new PongProbePacket[8];
    private readonly ControlPlaneEvent[] _controlEventScratch = new ControlPlaneEvent[8];
    private readonly Dictionary<ushort, long> _pingSentAtTicks = new();
    private bool _disposed;
    private uint _clientTick;
    private bool _lastReady;
    private ulong _lastOwnerServerTick;
    private ushort _lastOwnerInputSequence;
    private ushort _nextPingSequence;
    private long _nextPingTicks;
    private long _nextHandshakeRetryTicks;
    private long _connectStartedTicks;
    private long _lastPongTicks;
    private int _handshakeAttempts;
    private bool _sessionFailed;

    public int MaxHandshakeAttempts { get; set; } = 5;
    public double HandshakeRetryIntervalSeconds { get; set; } = 0.5;
    public double ReadyKeepAliveTimeoutSeconds { get; set; } = 5.0;

    public GodotClientNetworkSessionDriver(
        Action<string> statusSink,
        Action<ChatMessageEventPacket> chatSink,
        Action<EntityPresentationEventPacket>? presentationSink,
        Action<ClientMovementStateReceiveResult> ownerMovementSink,
        ClientLatestStateFrameSpanApplier? observerStateSink = null,
        Action? observerStateReset = null,
        Action<string>? sessionFailedSink = null,
        Action<UiPanelOpenDocument>? uiOpenSink = null,
        Action<UiModelUpdateDocument>? uiModelUpdateSink = null)
    {
        _statusSink = statusSink ?? throw new ArgumentNullException(nameof(statusSink));
        _chatSink = chatSink ?? throw new ArgumentNullException(nameof(chatSink));
        _presentationSink = presentationSink;
        _ownerMovementSink = ownerMovementSink ?? throw new ArgumentNullException(nameof(ownerMovementSink));
        _observerStateSink = observerStateSink;
        _observerStateReset = observerStateReset;
        _sessionFailedSink = sessionFailedSink;
        _uiOpenSink = uiOpenSink;
        _uiModelUpdateSink = uiModelUpdateSink;
    }

    public bool IsRunning => _host?.IsRunning == true;
    public bool IsReady => _host?.IsReady == true;
    public ClientPumpProfileSnapshot LastPumpProfile => _host?.LastPumpProfile ?? default;
    public SessionId SessionId => _host?.SessionId ?? default;
    public double LastRawPingRoundTripMilliseconds { get; private set; } = -1.0;
    public double LastPingRoundTripMilliseconds { get; private set; } = -1.0;
    public ClientMovementSessionHost? Host => _host;

    public bool TryConnect(ClientDirectConnectRequest request, out string error)
    {
        ThrowIfDisposed();
        Disconnect("Restarting client session.");

        if (!request.TryResolve(out IPEndPoint remoteEndPoint, out error))
        {
            return false;
        }

        var metadata = DefaultPacketMetadata.Create();
        var transport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Any, 0),
            metadata,
            remoteEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false));

        var chatEvents = new ClientChatEventBridge(packet =>
        {
            _chatSink(packet);
        });
        ClientObserverStateBridge? observerState = _observerStateSink is null
            ? null
            : ClientObserverStateBridge.CreateSpanFastPath(
                new ConnectionId(1),
                _observerStateSink,
                _observerStateReset);
        var observerEvents = ClientObserverEventBridge.CreateSpanFastPath(
            new ConnectionId(1),
            (_, payload) => TryHandlePresentationEvent(payload));
        observerEvents.ChatEvents = chatEvents;
        var uiEvents = new ClientUiEventBridge(_uiOpenSink, _uiModelUpdateSink);
        observerEvents.UiEvents = uiEvents;

        var host = new ClientMovementSessionHost(
            transport,
            new ConnectionId(1),
            new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression),
            observerState: observerState,
            observerEvents: observerEvents,
            chatEvents: chatEvents,
            uiEvents: uiEvents);

        transport.Start();
        host.Start();
        long nowTicks = Stopwatch.GetTimestamp();
        host.BeginHandshake(nowTicks);

        _transport = transport;
        _host = host;
        _connectStartedTicks = nowTicks;
        _nextHandshakeRetryTicks = nowTicks + SecondsToTicks(HandshakeRetryIntervalSeconds);
        _handshakeAttempts = 1;
        _sessionFailed = false;
        _lastPongTicks = 0;
        LastRawPingRoundTripMilliseconds = -1.0;
        LastPingRoundTripMilliseconds = -1.0;
        _statusSink($"Connecting to {request.Host}:{request.Port}... attempt 1/{Math.Max(1, MaxHandshakeAttempts)}");
        error = string.Empty;
        return true;
    }

    private void TryHandlePresentationEvent(ReadOnlySpan<byte> payload)
    {
        if (_presentationSink is null ||
            !PacketCodec.TryDecodeEnvelope(payload, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> innerPayload) ||
            header.PacketId != EntityPresentationEventPacket.PacketId)
        {
            return;
        }

        var reader = new BitReader(innerPayload);
        _presentationSink(EntityPresentationEventPacket.Deserialize(ref reader));
    }

    public void Pump()
    {
        if (_host is null)
        {
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();
        _host.Pump(nowTicks);
        _clientTick++;
        DrainControlEvents();
        if (_host is null)
        {
            return;
        }

        DrainPongs(nowTicks);
        MaybeSendPingProbe(nowTicks);
        ApplyOwnerMovementIfChanged();
        CheckConnectionHealth(nowTicks);

        if (_host is null)
        {
            return;
        }

        if (_host.IsReady)
        {
            if (!_lastReady)
            {
                _lastPongTicks = nowTicks;
                _statusSink("Connected.");
            }

            _lastReady = true;
        }
    }

    public bool TrySendChat(string text)
    {
        return _host?.TrySendChat(text) == true;
    }

    public bool TrySendUiAction(UiActionRequestDocument request)
    {
        return _host?.TrySendUiAction(request) == true;
    }

    public bool TrySendLoginCredential(LoginCredentialSubmitPacket packet)
    {
        return _host?.TrySendLoginCredential(packet) == true;
    }

    public bool TrySendMovementInput(ClientMovementInput input, ClientMovementState predictedState)
    {
        if (_host is null || !_host.IsReady)
        {
            return false;
        }

        return _host.TrySendInput(input, _clientTick, predictedState, out _);
    }

    public void Disconnect(string status = "Disconnected.")
    {
        Disconnect(status, notifyServer: true);
    }

    private void Disconnect(string status, bool notifyServer)
    {
        ClientMovementSessionHost? host = _host;
        if (notifyServer && host is not null && host.IsReady)
        {
            host.SendDisconnectNotice(SessionDisconnectReason.Closed, Stopwatch.GetTimestamp());
        }

        host?.Stop();
        _host = null;
        _transport?.Dispose();
        _transport = null;
        _clientTick = 0;
        _lastReady = false;
        _lastOwnerServerTick = 0;
        _lastOwnerInputSequence = 0;
        _nextPingSequence = 0;
        _nextPingTicks = 0;
        _nextHandshakeRetryTicks = 0;
        _connectStartedTicks = 0;
        _lastPongTicks = 0;
        _handshakeAttempts = 0;
        _sessionFailed = false;
        _pingSentAtTicks.Clear();
        LastRawPingRoundTripMilliseconds = -1.0;
        LastPingRoundTripMilliseconds = -1.0;
        _statusSink(status);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Disconnect("Disconnected.");
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GodotClientNetworkSessionDriver));
        }
    }

    private void ApplyOwnerMovementIfChanged()
    {
        if (_host is null)
        {
            return;
        }

        ClientMovementStateReceiveResult result = _host.Movement.LastReceiveResult;
        if (result.NetworkEntityId == 0)
        {
            return;
        }

        if (result.ServerTick == _lastOwnerServerTick &&
            result.LastProcessedInputSequence == _lastOwnerInputSequence)
        {
            return;
        }

        _lastOwnerServerTick = result.ServerTick;
        _lastOwnerInputSequence = result.LastProcessedInputSequence;
        _ownerMovementSink(result);
    }

    private void DrainControlEvents()
    {
        if (_host is null)
        {
            return;
        }

        while (true)
        {
            int count = _host.Control.DrainEvents(_controlEventScratch);
            if (count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                ControlPlaneEvent evt = _controlEventScratch[i];
                if (evt.Kind == ControlPlaneEventKind.ServerDisconnected)
                {
                    FailSession(ToServerDisconnectStatus(evt.DisconnectReason));
                    return;
                }
            }

            if (count < _controlEventScratch.Length)
            {
                return;
            }
        }
    }

    private void DrainPongs(long nowTicks)
    {
        if (_host is null)
        {
            return;
        }

        while (true)
        {
            int count = _host.Control.DrainReceivedPongs(_pongScratch);
            if (count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                ushort sequence = _pongScratch[i].Sequence;
                if (!_pingSentAtTicks.Remove(sequence, out long sentTicks))
                {
                    continue;
                }

                LastRawPingRoundTripMilliseconds = (nowTicks - sentTicks) * 1000.0 / Stopwatch.Frequency;
                LastPingRoundTripMilliseconds = LastPingRoundTripMilliseconds < 0.0
                    ? LastRawPingRoundTripMilliseconds
                    : (LastPingRoundTripMilliseconds * 0.75) + (LastRawPingRoundTripMilliseconds * 0.25);
                _lastPongTicks = nowTicks;
            }

            if (count < _pongScratch.Length)
            {
                return;
            }
        }
    }

    private void CheckConnectionHealth(long nowTicks)
    {
        if (_host is null || _sessionFailed)
        {
            return;
        }

        if (_host.IsReady)
        {
            if (_lastPongTicks > 0 &&
                ReadyKeepAliveTimeoutSeconds > 0.0 &&
                nowTicks - _lastPongTicks >= SecondsToTicks(ReadyKeepAliveTimeoutSeconds))
            {
                FailSession($"Connection lost: no server response for {ReadyKeepAliveTimeoutSeconds:0.#} seconds.");
            }

            return;
        }

        int maxAttempts = Math.Max(1, MaxHandshakeAttempts);
        if (_handshakeAttempts >= maxAttempts)
        {
            FailSession($"Connection failed: no response after {_handshakeAttempts} handshake attempt(s).");
            return;
        }

        if (nowTicks < _nextHandshakeRetryTicks)
        {
            return;
        }

        _handshakeAttempts++;
        if (_host.Control.RetryClientHandshake(_host.ConnectionId, nowTicks))
        {
            _statusSink($"Connecting... attempt {_handshakeAttempts}/{maxAttempts}");
        }

        _nextHandshakeRetryTicks = nowTicks + SecondsToTicks(HandshakeRetryIntervalSeconds);
    }

    private void FailSession(string status)
    {
        if (_sessionFailed)
        {
            return;
        }

        _sessionFailed = true;
        Disconnect(status, notifyServer: false);
        _sessionFailed = true;
        _sessionFailedSink?.Invoke(status);
    }

    private static string ToServerDisconnectStatus(SessionDisconnectReason reason)
    {
        return reason switch
        {
            SessionDisconnectReason.ServerShutdown => "Server is shutting down.",
            SessionDisconnectReason.Kicked => "Disconnected by server.",
            SessionDisconnectReason.Timeout => "Server closed the session after a timeout.",
            SessionDisconnectReason.Rejected => "Server rejected the session.",
            SessionDisconnectReason.ServerBusy => "Server is busy.",
            _ => "Server closed the connection.",
        };
    }

    private static long SecondsToTicks(double seconds)
    {
        if (seconds <= 0.0)
        {
            return 0;
        }

        double ticks = seconds * Stopwatch.Frequency;
        return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
    }

    private void MaybeSendPingProbe(long nowTicks)
    {
        if (_host is null || !_host.IsReady || nowTicks < _nextPingTicks)
        {
            return;
        }

        ushort sequence = _nextPingSequence++;
        _pingSentAtTicks[sequence] = nowTicks;
        _host.Control.SendPingProbe(_host.ConnectionId, sequence, unchecked((uint)nowTicks));
        _host.Control.FlushOutbound();
        _nextPingTicks = nowTicks + Stopwatch.Frequency;

        if (_pingSentAtTicks.Count > 32)
        {
            _pingSentAtTicks.Clear();
        }
    }
}
