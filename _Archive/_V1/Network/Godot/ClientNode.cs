// Network/Godot/ClientNode.cs
// Godot client node - drag and drop onto a scene to run a network client with login
// Example implementation showing how to connect to a server with authentication

#nullable enable

using Godot;
using Network.Client.Network;
using Network.Shared.Network;
using Network.Shared.Network.Packets;
using System;
using System.Threading.Tasks;

namespace Network.Godot
{
    /// <summary>
    /// Godot Node that manages a network client with login functionality.
    /// Attach this to a login scene or autoload.
    /// </summary>
    public partial class ClientNode : Control
    {
        // ═══════════════════════════════════════════════════════════════════
        //                        EXPORTS - CONNECTION
        // ═══════════════════════════════════════════════════════════════════

        [Export]
        public string ServerHost { get; set; } = "localhost";

        [Export]
        public int TcpPort { get; set; } = 2593;

        [Export]
        public int UdpPort { get; set; } = 2594;

        [Export]
        public ushort ProtocolVersion { get; set; } = 1;

        [Export]
        public string ClientVersion { get; set; } = "1.0.0";

        // ═══════════════════════════════════════════════════════════════════
        //                        EXPORTS - UI NODES
        // ═══════════════════════════════════════════════════════════════════

        [Export]
        public NodePath? UsernameInputPath { get; set; }

        [Export]
        public NodePath? PasswordInputPath { get; set; }

        [Export]
        public NodePath? ConnectButtonPath { get; set; }

        [Export]
        public NodePath? StatusLabelPath { get; set; }

        [Export]
        public NodePath? ServerHostInputPath { get; set; }

        // ═══════════════════════════════════════════════════════════════════
        //                        SIGNALS
        // ═══════════════════════════════════════════════════════════════════

        [Signal]
        public delegate void ConnectedEventHandler();

        [Signal]
        public delegate void DisconnectedEventHandler(string reason);

        [Signal]
        public delegate void ConnectionFailedEventHandler(string error);

        [Signal]
        public delegate void LoginSuccessEventHandler(uint playerId, string sessionToken);

        [Signal]
        public delegate void LoginFailedEventHandler(string error) ;

        [Signal]
        public delegate void ChatReceivedEventHandler(string senderName, string message, int chatType);

        [Signal]
        public delegate void SystemMessageReceivedEventHandler(string message, int messageType);

        // ═══════════════════════════════════════════════════════════════════
        //                        STATE
        // ═══════════════════════════════════════════════════════════════════

        private NetworkClient? _client;
        private bool _isConnecting;
        private bool _isAuthenticating;
        private double _pingElapsed;
        private uint _pingSequence;
        private double _authElapsed;

        // UI References
        private LineEdit? _usernameInput;
        private LineEdit? _passwordInput;
        private Button? _connectButton;
        private Label? _statusLabel;
        private LineEdit? _serverHostInput;

        public new bool IsConnected => _client?.IsConnected ?? false;
        public bool IsConnecting => _isConnecting;
        public uint SessionId => _client?.SessionId ?? 0;

        // Authentication state
        private uint _playerId;
        private Guid _sessionToken;
        private string _username = "";

        // ═══════════════════════════════════════════════════════════════════
        //                      LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        public override void _Ready()
        {
            // Get UI node references
            if (UsernameInputPath != null)
                _usernameInput = GetNodeOrNull<LineEdit>(UsernameInputPath);

            if (PasswordInputPath != null)
                _passwordInput = GetNodeOrNull<LineEdit>(PasswordInputPath);

            if (ConnectButtonPath != null)
            {
                _connectButton = GetNodeOrNull<Button>(ConnectButtonPath);
                if (_connectButton != null)
                    _connectButton.Pressed += OnConnectButtonPressed;
            }

            if (StatusLabelPath != null)
                _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);

            if (ServerHostInputPath != null)
                _serverHostInput = GetNodeOrNull<LineEdit>(ServerHostInputPath);

            // Create network client
            _client = new NetworkClient(ProtocolVersion, ClientVersion);

            // Wire up events
            _client.OnConnected += OnConnectedToServer;
            _client.OnDisconnected += OnDisconnectedFromServer;
            _client.OnConnectionFailed += OnConnectionFailedToServer;

            // Register packet handlers
            RegisterPacketHandlers();

            UpdateStatus("Ready to connect");
            GD.Print("[ClientNode] Initialized");
        }

        public override void _ExitTree()
        {
            _client?.Disconnect("Exiting");
            _client?.Dispose();
            _client = null;
        }

        public override void _Process(double delta)
        {
            // Process UDP retries
            _client?.ProcessUdpRetries();

            // Send keep-alive pings to avoid server idle timeout
            if (_client?.IsConnected == true)
            {
                _pingElapsed += delta;
                if (_pingElapsed >= 5.0) // 5s keepalive interval
                {
                    _pingElapsed = 0;
                    _client.Send(new Ping(_pingSequence++, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                }
            }

            // Authentication timeout (10s)
            if (_isAuthenticating)
            {
                _authElapsed += delta;
                if (_authElapsed >= 10.0)
                {
                    _isAuthenticating = false;
                    GD.PrintErr("[ClientNode] Authentication timed out");
                    UpdateStatus("Authentication timed out", true);
                    Disconnect();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   PACKET HANDLER REGISTRATION
        // ═══════════════════════════════════════════════════════════════════

        private void RegisterPacketHandlers()
        {
            if (_client == null) return;

            // Authentication
            _client.RegisterHandler<LoginResponse>(OnLoginResponse);

            // System
            _client.RegisterHandler<SystemMessage>(OnSystemMessage);

            // Movement
            _client.RegisterHandler<MovementConfirm>(OnMovementConfirm);

            // Chat
            _client.RegisterHandler<ChatReceive>(OnChatReceive);

            // Combat
            _client.RegisterHandler<DamageDealt>(OnDamageDealt);
            _client.RegisterHandler<HealthUpdate>(OnHealthUpdate);

            GD.Print("[ClientNode] Packet handlers registered");
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      UI CALLBACKS
        // ═══════════════════════════════════════════════════════════════════

        private void OnConnectButtonPressed()
        {
            if (IsConnected)
            {
                Disconnect();
            }
            else
            {
                ConnectAndLogin();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      CONNECTION & LOGIN
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Connect to server and attempt login with credentials.
        /// </summary>
        public async void ConnectAndLogin()
        {
            if (_client == null || _isConnecting || IsConnected)
                return;

            // Get credentials from UI
            string username = _usernameInput?.Text?.Trim() ?? "";
            string password = _passwordInput?.Text ?? "";

            if (string.IsNullOrEmpty(username))
            {
                UpdateStatus("Please enter a username", true);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                UpdateStatus("Please enter a password", true);
                return;
            }

            // Get server host from UI if available
            if (_serverHostInput != null && !string.IsNullOrEmpty(_serverHostInput.Text))
            {
                ServerHost = _serverHostInput.Text.Trim();
            }

            _isConnecting = true;
            _username = username;
            UpdateStatus($"Connecting to {ServerHost}:{TcpPort}...");
            SetUIEnabled(false);

            try
            {
                await _client.ConnectAsync(ServerHost, TcpPort, UdpPort);

                // Wait a moment for connection to fully establish
                await Task.Delay(100);

                if (IsConnected)
                {
                    // Send login request
                    _isAuthenticating = true;
                    _authElapsed = 0;
                    UpdateStatus("Authenticating...");

                    _client.Send(new LoginRequest(username, password));
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ClientNode] Connection failed: {ex.Message}");
                UpdateStatus($"Connection failed: {ex.Message}", true);
                SetUIEnabled(true);
                _isConnecting = false;
            }
        }

        /// <summary>
        /// Disconnect from server.
        /// </summary>
        public void Disconnect()
        {
            _client?.Disconnect("User requested");
            UpdateStatus("Disconnected");
            SetUIEnabled(true);

            if (_connectButton != null)
                _connectButton.Text = "Connect";

            _isConnecting = false;
            _isAuthenticating = false;
            _authElapsed = 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   CONNECTION EVENT HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnConnectedToServer()
        {
            CallDeferred(nameof(OnConnectedDeferred));
        }

        private void OnConnectedDeferred()
        {
            GD.Print($"[ClientNode] Connected to server! Session ID: {SessionId}");
            _isConnecting = false;
            EmitSignal(SignalName.Connected);

            // Note: Don't update UI yet - waiting for login response
        }

        private void OnDisconnectedFromServer(string reason)
        {
            CallDeferred(nameof(OnDisconnectedDeferred), reason);
        }

        private void OnDisconnectedDeferred(string reason)
        {
            GD.Print($"[ClientNode] Disconnected: {reason}");
            _isConnecting = false;
            _isAuthenticating = false;
            UpdateStatus($"Disconnected: {reason}", true);
            SetUIEnabled(true);

            if (_connectButton != null)
                _connectButton.Text = "Connect";

            EmitSignal(SignalName.Disconnected, reason);
        }

        private void OnConnectionFailedToServer(string error)
        {
            CallDeferred(nameof(OnConnectionFailedDeferred), error);
        }

        private void OnConnectionFailedDeferred(string error)
        {
            GD.PrintErr($"[ClientNode] Connection failed: {error}");
            _isConnecting = false;
            UpdateStatus($"Connection failed: {error}", true);
            SetUIEnabled(true);

            EmitSignal(SignalName.ConnectionFailed, error);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   PACKET HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnLoginResponse(NetworkClient client, in LoginResponse packet)
        {
            GD.Print($"[ClientNode] LoginResponse received. Success={packet.Success}, PlayerId={packet.PlayerId}");
            CallDeferred(nameof(ProcessLoginResponse), packet.Success, packet.ErrorMessage, packet.SessionToken.ToString(), packet.PlayerId);
        }

        private void ProcessLoginResponse(bool success, string errorMessage, string sessionToken, uint playerId)
        {
            _isAuthenticating = false;

            if (success)
            {
                _playerId = playerId;
                if (!Guid.TryParse(sessionToken, out _sessionToken))
                {
                    _sessionToken = Guid.Empty;
                }

                GD.Print($"[ClientNode] Login successful! Player ID: {playerId}");
                UpdateStatus($"Logged in as {_username}");

                if (_connectButton != null)
                    _connectButton.Text = "Disconnect";

                EmitSignal(SignalName.LoginSuccess, playerId, sessionToken);
            }
            else
            {
                GD.PrintErr($"[ClientNode] Login failed: {errorMessage}");
                UpdateStatus($"Login failed: {errorMessage}", true);
                SetUIEnabled(true);

                // Disconnect after failed login
                _client?.Disconnect("Login failed");

                EmitSignal(SignalName.LoginFailed, errorMessage);
            }
        }

        private void OnSystemMessage(NetworkClient client, in SystemMessage packet)
        {
            CallDeferred(nameof(ProcessSystemMessage), packet.Message, (int)packet.Type);
        }

        private void ProcessSystemMessage(string message, int messageType)
        {
            GD.Print($"[ClientNode] System message ({messageType}): {message}");
            UpdateStatus(message);
            EmitSignal(SignalName.SystemMessageReceived, message, messageType);
        }

        private void OnMovementConfirm(NetworkClient client, in MovementConfirm packet)
        {
            // Movement confirmed by server
            // Game code would update player position here
        }

        private void OnChatReceive(NetworkClient client, in ChatReceive packet)
        {
            CallDeferred(nameof(ProcessChatMessage), packet.SenderName, packet.Message, (int)packet.Type);
        }

        private void ProcessChatMessage(string senderName, string message, int chatType)
        {
            GD.Print($"[ClientNode] Chat ({chatType}) from {senderName}: {message}");
            EmitSignal(SignalName.ChatReceived, senderName, message, chatType);
        }

        private void OnDamageDealt(NetworkClient client, in DamageDealt packet)
        {
            GD.Print($"[ClientNode] Damage: {packet.SourceSerial} hit {packet.TargetSerial} for {packet.Amount}");
        }

        private void OnHealthUpdate(NetworkClient client, in HealthUpdate packet)
        {
            GD.Print($"[ClientNode] Health update: Entity {packet.Serial} = {packet.CurrentHealth}/{packet.MaxHealth}");
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      SENDING PACKETS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Send a chat message.
        /// </summary>
        public void SendChat(string message, ChatType type = ChatType.Say, string? target = null)
        {
            if (!IsConnected || _client == null)
                return;

            _client.Send(new ChatSend(type, message, target));
        }

        /// <summary>
        /// Send movement input.
        /// </summary>
        public void SendMovement(byte direction, bool running, uint sequence, int x, int y, int z)
        {
            if (!IsConnected || _client == null)
                return;

            var flags = running ? MoveFlags.Running : MoveFlags.None;
            _client.Send(new MovementRequest(direction, flags, sequence,
                (ushort)x, (ushort)y, (sbyte)z));
        }

        /// <summary>
        /// Send attack request.
        /// </summary>
        public void SendAttack(uint targetSerial)
        {
            if (!IsConnected || _client == null)
                return;

            _client.Send(new AttackRequest(targetSerial));
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      UI HELPERS
        // ═══════════════════════════════════════════════════════════════════

        private void UpdateStatus(string message, bool isError = false)
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = message;
                _statusLabel.Modulate = isError ? Colors.Red : Colors.White;
            }

            GD.Print($"[ClientNode] Status: {message}");
        }

        private void SetUIEnabled(bool enabled)
        {
            if (_usernameInput != null)
                _usernameInput.Editable = enabled;

            if (_passwordInput != null)
                _passwordInput.Editable = enabled;

            if (_serverHostInput != null)
                _serverHostInput.Editable = enabled;

            if (_connectButton != null)
                _connectButton.Disabled = !enabled && !IsConnected;
        }
    }
}
