using System.Diagnostics;
using System.Net;
using UltraSim.Network;

namespace UltraSim.Client.Godot;

public sealed class GodotClientNetworkSessionDriver : IDisposable
{
    private readonly Action<string> _statusSink;
    private readonly Action<ChatMessageEventPacket> _chatSink;
    private readonly Action<ClientMovementStateReceiveResult> _ownerMovementSink;
    private readonly ClientLatestStateFrameSpanApplier? _observerStateSink;
    private readonly Action? _observerStateReset;
    private ClientMovementSessionHost? _host;
    private UdpSocketNetworkTransport? _transport;
    private readonly PongProbePacket[] _pongScratch = new PongProbePacket[8];
    private readonly Dictionary<ushort, long> _pingSentAtTicks = new();
    private bool _disposed;
    private uint _clientTick;
    private bool _lastReady;
    private uint _lastOwnerServerTick;
    private ushort _lastOwnerInputSequence;
    private ushort _nextPingSequence;
    private long _nextPingTicks;

    public GodotClientNetworkSessionDriver(
        Action<string> statusSink,
        Action<ChatMessageEventPacket> chatSink,
        Action<ClientMovementStateReceiveResult> ownerMovementSink,
        ClientLatestStateFrameSpanApplier? observerStateSink = null,
        Action? observerStateReset = null)
    {
        _statusSink = statusSink ?? throw new ArgumentNullException(nameof(statusSink));
        _chatSink = chatSink ?? throw new ArgumentNullException(nameof(chatSink));
        _ownerMovementSink = ownerMovementSink ?? throw new ArgumentNullException(nameof(ownerMovementSink));
        _observerStateSink = observerStateSink;
        _observerStateReset = observerStateReset;
    }

    public bool IsRunning => _host?.IsRunning == true;
    public bool IsReady => _host?.IsReady == true;
    public ClientPumpProfileSnapshot LastPumpProfile => _host?.LastPumpProfile ?? default;
    public SessionId SessionId => _host?.SessionId ?? default;
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
            (_, _) => { });
        observerEvents.ChatEvents = chatEvents;

        var host = new ClientMovementSessionHost(
            transport,
            new ConnectionId(1),
            new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression),
            observerState: observerState,
            observerEvents: observerEvents,
            chatEvents: chatEvents);

        transport.Start();
        host.Start();
        host.BeginHandshake(Stopwatch.GetTimestamp());

        _transport = transport;
        _host = host;
        _statusSink($"Connecting to {request.Host}:{request.Port}...");
        error = string.Empty;
        return true;
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
        DrainPongs(nowTicks);
        MaybeSendPingProbe(nowTicks);
        ApplyOwnerMovementIfChanged();

        if (_host.IsReady)
        {
            if (!_lastReady)
            {
                _statusSink("Connected.");
            }

            _lastReady = true;
        }
    }

    public bool TrySendChat(string text)
    {
        return _host?.TrySendChat(text) == true;
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
        _host?.Stop();
        _host = null;
        _transport?.Dispose();
        _transport = null;
        _clientTick = 0;
        _lastReady = false;
        _lastOwnerServerTick = 0;
        _lastOwnerInputSequence = 0;
        _nextPingSequence = 0;
        _nextPingTicks = 0;
        _pingSentAtTicks.Clear();
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

                LastPingRoundTripMilliseconds = (nowTicks - sentTicks) * 1000.0 / Stopwatch.Frequency;
            }

            if (count < _pongScratch.Length)
            {
                return;
            }
        }
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
