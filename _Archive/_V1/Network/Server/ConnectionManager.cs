// Server/Network/ConnectionManager.cs
// Central coordinator for all server networking

using System.Collections.Concurrent;
using Network.Shared.Network;
using Network.Shared.Network.Packets;

namespace Network.Server.Network
{
    /// <summary>
    /// Manages all network connections and coordinates packet handling.
    /// Main entry point for server networking.
    /// </summary>
    public sealed class ConnectionManager : IDisposable
    {
        private readonly TcpServer _tcpServer;
        private readonly UdpServer _udpServer;
        private readonly PacketRegistry<ServerSession> _packetRegistry;

        // Active sessions
        private readonly ConcurrentDictionary<uint, ServerSession> _sessions = new();

        // Configuration
        private readonly NetworkConfig _config;

        // Events
        public event Action<ServerSession>? OnClientConnected;
        public event Action<ServerSession, string>? OnClientDisconnected;

        // Metrics
        public int ActiveConnections => _sessions.Count;
        public int MaxConnections => _config.MaxConnections;
        public IEnumerable<ServerSession> Sessions => _sessions.Values;

        public ConnectionManager(NetworkConfig? config = null)
        {
            _config = config ?? NetworkConfig.Default;

            // Create servers
            _udpServer = new UdpServer(_config.UdpPort);
            _tcpServer = new TcpServer(_config.TcpPort, _udpServer, _config.MaxConnections);

            // Create packet registry
            _packetRegistry = new PacketRegistry<ServerSession>();

            // Wire up events
            _tcpServer.OnSessionCreated += OnSessionCreated;

            Console.WriteLine($"[ConnectionManager] Initialized - TCP:{_config.TcpPort} UDP:{_config.UdpPort}");
        }

        /// <summary>
        /// Start the network servers.
        /// </summary>
        public void Start()
        {
            _udpServer.Start();
            _tcpServer.Start();
            Console.WriteLine("[ConnectionManager] Network servers started");
        }

        /// <summary>
        /// Stop the network servers.
        /// </summary>
        public void Stop()
        {
            _tcpServer.Stop();
            _udpServer.Stop();

            // Disconnect all sessions
            foreach (var session in _sessions.Values)
            {
                session.Disconnect("Server shutdown");
            }

            _sessions.Clear();
            Console.WriteLine("[ConnectionManager] Network servers stopped");
        }

        /// <summary>
        /// Register a packet handler.
        /// </summary>
        public void RegisterHandler<TPacket>(PacketHandler<ServerSession, TPacket> handler)
            where TPacket : struct, IPacket<TPacket>
        {
            _packetRegistry.Register(handler);
        }

        /// <summary>
        /// Register all default system handlers.
        /// </summary>
        public void RegisterSystemHandlers()
        {
            // Handshake
            RegisterHandler<ClientHello>(OnClientHello);
            RegisterHandler<Ping>(OnPing);
            RegisterHandler<Disconnect>(OnDisconnect);
        }

        /// <summary>
        /// Process network tick. Call from game loop.
        /// </summary>
        public void Tick()
        {
            var now = DateTime.UtcNow;
            var toRemove = new List<uint>();

            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;

                // Check for timeout
                var tcpIdle = now - session.LastTcpActivity;
                if (tcpIdle.TotalMilliseconds > _config.ConnectionTimeoutMs)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Process UDP retries
                session.ProcessUdpRetries();
            }

            // Remove timed out sessions
            foreach (var id in toRemove)
            {
                if (_sessions.TryRemove(id, out var session))
                {
                    session.Disconnect("Timeout");
                }
            }
        }

        /// <summary>
        /// Get a session by ID.
        /// </summary>
        public ServerSession? GetSession(uint sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// Broadcast a packet to all connected sessions.
        /// </summary>
        public void Broadcast<TPacket>(in TPacket packet) where TPacket : struct, IPacket
        {
            foreach (var session in _sessions.Values)
            {
                if (session.State != SessionState.Disconnected)
                {
                    session.Send(in packet);
                }
            }
        }

        /// <summary>
        /// Broadcast a packet to sessions matching a predicate.
        /// </summary>
        public void Broadcast<TPacket>(in TPacket packet, Func<ServerSession, bool> predicate)
            where TPacket : struct, IPacket
        {
            foreach (var session in _sessions.Values)
            {
                if (session.State != SessionState.Disconnected && predicate(session))
                {
                    session.Send(in packet);
                }
            }
        }

        /// <summary>
        /// Get all active sessions.
        /// </summary>
        public IEnumerable<ServerSession> GetAllSessions()
        {
            return _sessions.Values.Where(s => s.State != SessionState.Disconnected);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      EVENT HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnSessionCreated(ServerSession session)
        {
            _sessions[session.SessionId] = session;

            // Wire up packet handling
            session.OnPacketReceived += OnPacketReceived;
            session.OnDisconnected += OnSessionDisconnected;

            OnClientConnected?.Invoke(session);
        }

        private void OnSessionDisconnected(ServerSession session, string reason)
        {
            _sessions.TryRemove(session.SessionId, out _);
            OnClientDisconnected?.Invoke(session, reason);
        }

        private void OnPacketReceived(ServerSession session, ChannelType channel, ushort packetId, ReadOnlySpan<byte> payload)
        {
            if (!_packetRegistry.Dispatch(session, packetId, payload))
            {
                Console.WriteLine($"[ConnectionManager] Unknown packet 0x{packetId:X4} from session {session.SessionId}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                    SYSTEM PACKET HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnClientHello(ServerSession session, in ClientHello packet)
        {
            Console.WriteLine($"[ConnectionManager] ClientHello from session {session.SessionId}: " +
                $"Protocol={packet.ProtocolVersion}, Version={packet.ClientVersion}");

            // Check protocol version
            if (packet.ProtocolVersion != _config.ProtocolVersion)
            {
                session.Send(new ConnectionRejected(
                    RejectReason.VersionMismatch,
                    $"Expected protocol version {_config.ProtocolVersion}, got {packet.ProtocolVersion}"
                ));
                session.Disconnect("Version mismatch");
                return;
            }

            // Send response
            session.Send(new ServerHello(
                session.SessionId,
                session.UdpHandshakeToken,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _config.UdpEnabled,
                (ushort)_config.UdpPort
            ));
        }

        private void OnPing(ServerSession session, in Ping packet)
        {
            session.Send(new Pong(
                packet.Sequence,
                packet.Timestamp,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }

        private void OnDisconnect(ServerSession session, in Disconnect packet)
        {
            Console.WriteLine($"[ConnectionManager] Client disconnect: {packet.Reason} - {packet.Message}");
            session.Disconnect($"Client: {packet.Reason}");
        }

        public void Dispose()
        {
            Stop();
            _tcpServer.Dispose();
            _udpServer.Dispose();
        }
    }

    /// <summary>
    /// Network configuration.
    /// </summary>
    public sealed class NetworkConfig
    {
        public int TcpPort { get; init; } = NetworkConstants.DefaultTcpPort;
        public int UdpPort { get; init; } = NetworkConstants.DefaultUdpPort;
        public int MaxConnections { get; init; } = NetworkConstants.MaxConnections;
        public int ConnectionTimeoutMs { get; init; } = NetworkConstants.ConnectionTimeoutMs;
        public ushort ProtocolVersion { get; init; } = 1;
        public bool UdpEnabled { get; init; } = true;

        public static NetworkConfig Default { get; } = new();
    }
}
