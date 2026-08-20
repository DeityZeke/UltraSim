using UltraSim.Network;
using UltraSim.Client.Engines.Networking;
using UltraSim.Client.Runtime;
using UltraSim.UI.Contracts;
using Godot;

namespace Client.Godot
{
    public sealed class NetworkClientSessionDriver : IClientSessionDriver
    {
        private readonly string _host;
        private readonly int _port;
        private GodotClientSession? _session;
        private NetworkSessionManager? _sessions;
        private TcpFramedNetworkTransport? _transport;
        private GodotClientNetworkRuntime? _runtime;
        private NetworkInboundCommandPipeline<EntityFloat3InboundMessage>? _inboundPipeline;
        private readonly TcpFramedNetworkTransport.ConnectionEvent[] _connectionEvents =
            new TcpFramedNetworkTransport.ConnectionEvent[8];
        private bool _connected;
        private bool _hasConnectionError;
        private string _statusText = "idle";
        private NetworkDriverConnectionState _connectionState = NetworkDriverConnectionState.Idle;

        public NetworkClientSessionDriver(string host, int port)
        {
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            _port = port <= 0 ? 7777 : port;
        }

        public string Host => _host;
        public int Port => _port;
        public bool IsConnected => _connected;
        public float LastPingMs => _runtime?.LastPingMs ?? 0.0f;
        public float SmoothedPingMs => _runtime?.SmoothedPingMs ?? 0.0f;
        public bool HasConnectionError => _hasConnectionError;
        public string StatusText => _statusText;
        public NetworkDriverConnectionState ConnectionState => _connectionState;
        public System.Collections.Generic.IReadOnlyList<string> ChatFeedLines => _runtime?.ChatFeedLines ?? System.Array.Empty<string>();

        public void Initialize(GodotClientSession session)
        {
            _session = session;
            _session.CharacterWorld.ClearAllActors();
            _sessions = new NetworkSessionManager();
            _transport = new TcpFramedNetworkTransport(
                TcpFramedNetworkTransport.TransportRole.Client,
                host: _host,
                port: _port,
                clientConnectionId: 1,
                enableUdp: false);
            _runtime = new GodotClientNetworkRuntime(session, _transport, () => _transport.ClientServerAssignedConnectionId);
            if (_session.MenuCanvasManager != null)
            {
                _session.MenuCanvasManager.PanelAckSubmitted += OnPanelAckSubmitted;
                _session.MenuCanvasManager.PanelResponseSubmitted += OnPanelResponseSubmitted;
                _session.MenuCanvasManager.LoginRequestSender = OnLoginRequestSender;
            }
            _session.OnLoginResponse = OnLoginResponse;
            _inboundPipeline = new NetworkInboundCommandPipeline<EntityFloat3InboundMessage>(
                _transport,
                sessionResolver: _sessions.TryGetSessionIdByConnection,
                decoder: EntityFloat3InboundMessageCodec.DecodeDatagram,
                queueThreadSafe: false,
                statsThreadSafe: false,
                sessionCacheValidationInterval: 32);
            _transport.Start();
            _connected = false;
            _hasConnectionError = false;
            _statusText = $"connecting {_host}:{_port}";
            _connectionState = NetworkDriverConnectionState.Connecting;
            _runtime.Reset();
            _session.Logger.Info($"network driver started target={_host}:{_port}");
        }

        public void Tick(float deltaSeconds)
        {
            if (_session == null || _transport == null || _sessions == null || _inboundPipeline == null)
                return;

            int count = _transport.DrainConnectionEvents(_connectionEvents);
            long nowTicks = _session.Clock.GetMonotonicTicksUsec();
            for (int i = 0; i < count; i++)
            {
                TcpFramedNetworkTransport.ConnectionEvent evt = _connectionEvents[i];
                switch (evt.Kind)
                {
                    case TcpFramedNetworkTransport.ConnectionEventKind.Connected:
                        long sessionId = _sessions.CreateSession(evt.ConnectionId, nowTicks);
                        _connected = true;
                        _hasConnectionError = false;
                        _statusText = $"connected {_host}:{_port}";
                        _connectionState = NetworkDriverConnectionState.Connected;
                        _runtime?.OnConnected(nowTicks);
                        _session.Logger.Info($"network connected connection={evt.ConnectionId} session={sessionId}");
                        break;
                    case TcpFramedNetworkTransport.ConnectionEventKind.Disconnected:
                        _connected = false;
                        _hasConnectionError = true;
                        _statusText = $"disconnected: {evt.Message}";
                        _connectionState = NetworkDriverConnectionState.Disconnected;
                        _runtime?.Reset();
                        _session.CharacterWorld.ClearAllActors();
                        _sessions.TryDisconnectConnection(evt.ConnectionId, SessionDisconnectReason.Unknown, nowTicks);
                        _session.Logger.Warn($"network disconnected connection={evt.ConnectionId} reason={evt.Message}");
                        break;
                    case TcpFramedNetworkTransport.ConnectionEventKind.ConnectFailed:
                        _connected = false;
                        _hasConnectionError = true;
                        _statusText = $"connect failed: {evt.Message}";
                        _connectionState = NetworkDriverConnectionState.Error;
                        _runtime?.Reset();
                        _session.CharacterWorld.ClearAllActors();
                        _session.Logger.Warn($"network connect_failed connection={evt.ConnectionId} reason={evt.Message}");
                        break;
                    case TcpFramedNetworkTransport.ConnectionEventKind.ListenFailed:
                    case TcpFramedNetworkTransport.ConnectionEventKind.ListenerRuntimeError:
                        _connected = false;
                        _hasConnectionError = true;
                        _statusText = $"transport error: {evt.Message}";
                        _connectionState = NetworkDriverConnectionState.Error;
                        _runtime?.Reset();
                        _session.CharacterWorld.ClearAllActors();
                        _session.Logger.Warn($"network transport_event kind={evt.Kind} connection={evt.ConnectionId} reason={evt.Message}");
                        break;
                }
            }

            _inboundPipeline.PollAndDecodeInbound();
            _inboundPipeline.ApplyPreBarrier(HandleInboundMessage);
            _runtime?.Tick(_connected, deltaSeconds);
            _inboundPipeline.FlushOutbound();
        }

        public void Shutdown()
        {
            _inboundPipeline = null;
            _runtime?.Reset();
            _runtime = null;
            _connected = false;
            _hasConnectionError = false;
            _statusText = "idle";
            _connectionState = NetworkDriverConnectionState.Idle;
            _session?.CharacterWorld.ClearAllActors();

            if (_session?.MenuCanvasManager != null)
            {
                _session.MenuCanvasManager.PanelAckSubmitted -= OnPanelAckSubmitted;
                _session.MenuCanvasManager.PanelResponseSubmitted -= OnPanelResponseSubmitted;
                _session.MenuCanvasManager.LoginRequestSender = null;
            }
            if (_session != null)
                _session.OnLoginResponse = null;

            if (_transport != null)
            {
                _transport.Dispose();
                _transport = null;
            }

            if (_sessions != null && _session != null)
            {
                _sessions.DisconnectAll(SessionDisconnectReason.Unknown, _session.Clock.GetMonotonicTicksUsec());
                _sessions = null;
            }

            if (_session != null)
                _session.Logger.Info("network driver shutdown");

            _session = null;
        }

        public bool TrySendChatText(string rawText) =>
            _connected && _runtime != null && _runtime.TrySendChatText(rawText);

        public bool TryRequestReturnToCharacterSelect()
        {
            if (!_connected || _runtime == null)
                return false;

            if (!_runtime.TrySendReturnToCharacterSelect())
                return false;

            _runtime.EnterPreGameState();
            return true;
        }

        private void HandleInboundMessage(in QueuedInboundCommand<EntityFloat3InboundMessage> command)
        {
            if (_runtime == null)
                return;

            _runtime.HandleInboundMessage(in command.Command);
        }

        private void OnPanelAckSubmitted(UiPanelAck ack)
        {
            _runtime?.TrySendUiPanelAck(ack);
        }

        private void OnPanelResponseSubmitted(UiPanelResponse response)
        {
            _runtime?.TrySendUiPanelResponse(response);
        }

        private void OnLoginRequestSender(
            UiPanelInstanceId instanceId,
            byte[] usernameUtf8,
            byte[] passwordHash,
            byte[] challengeResponse)
        {
            _runtime?.TrySendLoginRequest(instanceId, usernameUtf8, passwordHash, challengeResponse);
        }

        private void OnLoginResponse(UltraSim.Client.Engines.Networking.LoginResponsePayload payload)
        {
            _session?.MenuCanvasManager?.OnLoginResponse(payload);
        }
    }
}
