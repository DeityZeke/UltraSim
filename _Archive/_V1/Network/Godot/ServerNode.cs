// Network/Godot/ServerNode.cs
// Godot server node - drag and drop onto a scene to run a network server
// Example implementation showing how to set up a server with the networking library

#nullable enable

using Godot;
using Network.Server.Network;
using Network.Shared.Network;
using Network.Shared.Network.Packets;
using System;
using System.Collections.Generic;

namespace Network.Godot
{
    /// <summary>
    /// Godot Node that manages a network server.
    /// Attach this to an autoload or server scene to run a dedicated server.
    /// </summary>
    public partial class ServerNode : Node
    {
        // ═══════════════════════════════════════════════════════════════════
        //                        EXPORTS
        // ═══════════════════════════════════════════════════════════════════

        [Export]
        public int TcpPort { get; set; } = 2593;

        [Export]
        public int UdpPort { get; set; } = 2594;

        [Export]
        public int MaxConnections { get; set; } = 1000;

        [Export]
        public ushort ProtocolVersion { get; set; } = 1;

        [Export]
        public bool AutoStart { get; set; } = true;

        [Export]
        public bool RequireAuthentication { get; set; } = true;

        // ═══════════════════════════════════════════════════════════════════
        //                        SIGNALS
        // ═══════════════════════════════════════════════════════════════════

        [Signal]
        public delegate void ServerStartedEventHandler();

        [Signal]
        public delegate void ServerStoppedEventHandler();

        [Signal]
        public delegate void ClientConnectedEventHandler(uint sessionId);

        [Signal]
        public delegate void ClientDisconnectedEventHandler(uint sessionId, string reason);

        [Signal]
        public delegate void ClientAuthenticatedEventHandler(uint sessionId, string username);

        // ═══════════════════════════════════════════════════════════════════
        //                        STATE
        // ═══════════════════════════════════════════════════════════════════

        private ConnectionManager? _server;
        private bool _isRunning;

        public bool IsRunning => _isRunning;
        public int ConnectedClients => _server?.ActiveConnections ?? 0;

        // Track authenticated sessions
        private readonly Dictionary<uint, string> _authenticatedSessions = new();

        // ═══════════════════════════════════════════════════════════════════
        //                      LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        public override void _Ready()
        {
            if (AutoStart)
            {
                StartServer();
            }

            GD.Print($"[ServerNode] Ready - AutoStart: {AutoStart}");
        }

        public override void _Process(double delta)
        {
            // Server tick for processing network events and UDP retries
            _server?.Tick();
        }

        public override void _ExitTree()
        {
            StopServer();
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      SERVER CONTROL
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Start the network server.
        /// </summary>
        public void StartServer()
        {
            if (_isRunning)
            {
                GD.PrintErr("[ServerNode] Server is already running");
                return;
            }

            try
            {
                var config = new NetworkConfig
                {
                    TcpPort = TcpPort,
                    UdpPort = UdpPort,
                    MaxConnections = MaxConnections,
                    ProtocolVersion = ProtocolVersion
                };

                _server = new ConnectionManager(config);

                // Register system handlers
                _server.RegisterSystemHandlers();

                // Register game packet handlers
                RegisterPacketHandlers();

                // Wire up events
                _server.OnClientConnected += OnClientConnected;
                _server.OnClientDisconnected += OnClientDisconnected;

                // Start listening
                _server.Start();

                _isRunning = true;
                GD.Print($"[ServerNode] Server started on TCP:{TcpPort} UDP:{UdpPort}");
                EmitSignal(SignalName.ServerStarted);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ServerNode] Failed to start server: {ex.Message}");
                _server?.Dispose();
                _server = null;
            }
        }

        /// <summary>
        /// Stop the network server.
        /// </summary>
        public void StopServer()
        {
            if (!_isRunning)
                return;

            _server?.Stop();
            _server?.Dispose();
            _server = null;

            _isRunning = false;
            _authenticatedSessions.Clear();

            GD.Print("[ServerNode] Server stopped");
            EmitSignal(SignalName.ServerStopped);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   PACKET HANDLER REGISTRATION
        // ═══════════════════════════════════════════════════════════════════

        private void RegisterPacketHandlers()
        {
            if (_server == null) return;

            // Authentication
            _server.RegisterHandler<LoginRequest>(OnLoginRequest);

            // Movement
            _server.RegisterHandler<MovementRequest>(OnMovementRequest);
            _server.RegisterHandler<MovementStop>(OnMovementStop);

            // Combat
            _server.RegisterHandler<AttackRequest>(OnAttackRequest);
            _server.RegisterHandler<AttackStop>(OnAttackStop);
            _server.RegisterHandler<UseAbility>(OnUseAbility);

            // Chat
            _server.RegisterHandler<ChatSend>(OnChatSend);

            GD.Print("[ServerNode] Packet handlers registered");
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   CONNECTION HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnClientConnected(ServerSession session)
        {
            GD.Print($"[ServerNode] Client connected: Session {session.SessionId} from {session.TcpEndPoint}");
            CallDeferred(nameof(EmitClientConnected), session.SessionId);
        }

        private void OnClientDisconnected(ServerSession session, string reason)
        {
            _authenticatedSessions.Remove(session.SessionId);
            GD.Print($"[ServerNode] Client disconnected: Session {session.SessionId} - {reason}");
            CallDeferred(nameof(EmitClientDisconnected), session.SessionId, reason);
        }

        private void EmitClientConnected(uint sessionId)
        {
            EmitSignal(SignalName.ClientConnected, sessionId);
        }

        private void EmitClientDisconnected(uint sessionId, string reason)
        {
            EmitSignal(SignalName.ClientDisconnected, sessionId, reason);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   PACKET HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnLoginRequest(ServerSession session, in LoginRequest packet)
        {
            GD.Print($"[ServerNode] Login request from Session {session.SessionId}: Username={packet.Username}");

            if (RequireAuthentication)
            {
                // EXAMPLE: Verify credentials
                // In production, you would check against a database
                bool authenticated = ValidateCredentials(packet.Username, packet.Password);

                if (authenticated)
                {
                    _authenticatedSessions[session.SessionId] = packet.Username;

                    // Send success response
                    session.Send(new LoginResponse(
                        success: true,
                        errorMessage: "",
                        sessionToken: Guid.NewGuid(),
                        playerId: session.SessionId
                    ));

                    GD.Print($"[ServerNode] Session {session.SessionId} authenticated as {packet.Username} (LoginResponse sent)");
                    CallDeferred(nameof(EmitClientAuthenticated), session.SessionId, packet.Username);
                }
                else
                {
                    // Send failure response
                    session.Send(new LoginResponse(
                        success: false,
                        errorMessage: "Invalid username or password",
                        sessionToken: Guid.Empty,
                        playerId: 0
                    ));

                    GD.PrintErr($"[ServerNode] Authentication failed for Session {session.SessionId} (LoginResponse sent)");
                }
            }
            else
            {
                // No authentication required - auto-accept
                _authenticatedSessions[session.SessionId] = packet.Username;
                session.Send(new LoginResponse(
                    success: true,
                    errorMessage: "",
                    sessionToken: Guid.NewGuid(),
                    playerId: session.SessionId
                ));
                GD.Print($"[ServerNode] Session {session.SessionId} auto-authenticated as {packet.Username} (LoginResponse sent)");
            }
        }

        private void EmitClientAuthenticated(uint sessionId, string username)
        {
            EmitSignal(SignalName.ClientAuthenticated, sessionId, username);
        }

        /// <summary>
        /// Example credential validation. Replace with your own authentication logic.
        /// </summary>
        private bool ValidateCredentials(string username, string password)
        {
            // EXAMPLE: For demonstration, accept any non-empty credentials
            // In production: Hash password, check database, verify tokens, etc.
            return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
        }

        private void OnMovementRequest(ServerSession session, in MovementRequest packet)
        {
            // Only process if authenticated
            if (!_authenticatedSessions.ContainsKey(session.SessionId))
                return;

            // EXAMPLE: Validate and process movement
            // In production: Server-side movement validation, anti-cheat, etc.
            GD.Print($"[ServerNode] Movement from {session.SessionId}: Dir={packet.Direction}, Flags={packet.Flags}");

            // Confirm movement
            session.Send(new MovementConfirm(
                packet.InputSequence,
                packet.PredictedX,
                packet.PredictedY,
                packet.PredictedZ,
                isCorrection: false
            ));

            // TODO: Broadcast position to nearby players
        }

        private void OnMovementStop(ServerSession session, in MovementStop packet)
        {
            if (!_authenticatedSessions.ContainsKey(session.SessionId))
                return;

            GD.Print($"[ServerNode] Movement stop from {session.SessionId}");
            // TODO: Process movement stop, broadcast to nearby players
        }

        private void OnAttackRequest(ServerSession session, in AttackRequest packet)
        {
            if (!_authenticatedSessions.ContainsKey(session.SessionId))
                return;

            GD.Print($"[ServerNode] Attack request from {session.SessionId}: Target={packet.TargetSerial}");

            // EXAMPLE: Process combat
            session.Send(new SwingStart(
                attackerSerial: session.SessionId,
                defenderSerial: packet.TargetSerial
            ));

            // TODO: Calculate damage, apply to target, broadcast results
        }

        private void OnAttackStop(ServerSession session, in AttackStop packet)
        {
            if (!_authenticatedSessions.ContainsKey(session.SessionId))
                return;

            GD.Print($"[ServerNode] Attack stop from {session.SessionId}");
        }

        private void OnUseAbility(ServerSession session, in UseAbility packet)
        {
            if (!_authenticatedSessions.ContainsKey(session.SessionId))
                return;

            GD.Print($"[ServerNode] Ability used by {session.SessionId}: Ability={packet.AbilityId}");
            // TODO: Validate ability, check cooldowns, apply effects
        }

        private void OnChatSend(ServerSession session, in ChatSend packet)
        {
            if (!_authenticatedSessions.TryGetValue(session.SessionId, out string? username))
                return;

            GD.Print($"[ServerNode] Chat from {username}: {packet.Message}");

            // Broadcast to all connected clients
            if (_server != null)
            {
                var chatPacket = new ChatReceive(
                    packet.Type,
                    senderSerial: session.SessionId,
                    senderName: username,
                    message: packet.Message,
                    hue: 0x3B2
                );

                _server.Broadcast(chatPacket);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send a system message to a specific session.
        /// </summary>
        public void SendSystemMessage(uint sessionId, string message, SystemMessageType type = SystemMessageType.Info)
        {
            var session = _server?.GetSession(sessionId);
            if (session != null)
            {
                session.Send(new SystemMessage(type, message));
            }
        }

        /// <summary>
        /// Kick a client from the server.
        /// </summary>
        public void KickClient(uint sessionId, string reason)
        {
            var session = _server?.GetSession(sessionId);
            if (session != null)
            {
                session.Send(new Disconnect(DisconnectReason.Kicked, reason));
                session.Disconnect(reason);
            }
        }

        /// <summary>
        /// Broadcast a message to all authenticated clients.
        /// </summary>
        public void BroadcastMessage(string message, SystemMessageType type = SystemMessageType.Info)
        {
            if (_server != null)
            {
                var packet = new SystemMessage(type, message);
                _server.Broadcast(packet);
            }
        }
    }
}
