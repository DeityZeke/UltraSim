// Network/Godot/GodotNetworkManager.cs
// Godot-specific integration for the engine-agnostic Network client

#nullable enable

using Godot;
using Network.Client.Network;
using Network.Shared.Network;
using Network.Shared.Network.Packets;
using System.Threading.Tasks;

namespace Network.Godot
{
    /// <summary>
    /// Godot Node wrapper for NetworkClient.
    /// Attach this to an autoload or main scene.
    /// </summary>
    public partial class GodotNetworkManager : Node
    {
        // ═══════════════════════════════════════════════════════════════════
        //                        EXPORTS
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
        //                        SIGNALS
        // ═══════════════════════════════════════════════════════════════════

        [Signal]
        public delegate void ConnectedEventHandler();

        [Signal]
        public delegate void DisconnectedEventHandler(string reason);

        [Signal]
        public delegate void ConnectionFailedEventHandler(string error);

        [Signal]
        public delegate void MovementConfirmedEventHandler(uint sequence, int x, int y, int z, bool correction);

        [Signal]
        public delegate void ChatReceivedEventHandler(string senderName, string message, int chatType);

        [Signal]
        public delegate void DamageReceivedEventHandler(uint targetSerial, uint sourceSerial, int amount, int damageType);

        [Signal]
        public delegate void EntityPositionUpdatedEventHandler(uint serial, int x, int y, int z, int direction);

        [Signal]
        public delegate void HealthUpdatedEventHandler(uint serial, int current, int max);

        [Signal]
        public delegate void EntityDiedEventHandler(uint serial, uint killerSerial);

        // ═══════════════════════════════════════════════════════════════════
        //                        STATE
        // ═══════════════════════════════════════════════════════════════════

        private NetworkClient? _client;
        private bool _isConnecting;

        public new bool IsConnected => _client?.IsConnected ?? false;  // Hides Godot's base class method intentionally
        public bool IsConnecting => _isConnecting;
        public uint SessionId => _client?.SessionId ?? 0;

        // ═══════════════════════════════════════════════════════════════════
        //                      LIFECYCLE
        // ═══════════════════════════════════════════════════════════════════

        public override void _Ready()
        {
            // Create client
            _client = new NetworkClient(ProtocolVersion, ClientVersion);

            // Wire up events
            _client.OnConnected += () => CallDeferred(nameof(OnConnectedDeferred));
            _client.OnDisconnected += (reason) => CallDeferred(nameof(OnDisconnectedDeferred), reason);
            _client.OnConnectionFailed += (error) => CallDeferred(nameof(OnConnectionFailedDeferred), error);

            // Register packet handlers
            RegisterHandlers();

            GD.Print("[Network] Initialized");
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
        }

        // ═══════════════════════════════════════════════════════════════════
        //                   PACKET HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void RegisterHandlers()
        {
            if (_client == null) return;

            _client.RegisterHandler<MovementConfirm>(OnMovementConfirm);
            _client.RegisterHandler<MovementRejected>(OnMovementRejected);
            _client.RegisterHandler<EntityPosition>(OnEntityPosition);
            _client.RegisterHandler<EntityPositionBatch>(OnEntityPositionBatch);
            _client.RegisterHandler<EntityTeleport>(OnEntityTeleport);
            _client.RegisterHandler<ChatReceive>(OnChatReceive);
            _client.RegisterHandler<SystemMessage>(OnSystemMessage);
            _client.RegisterHandler<DamageDealt>(OnDamageDealt);
            _client.RegisterHandler<DamageNumber>(OnDamageNumber);
            _client.RegisterHandler<HealthUpdate>(OnHealthUpdate);
            _client.RegisterHandler<EntityDeath>(OnEntityDeath);
            _client.RegisterHandler<EntityResurrect>(OnEntityResurrect);
            _client.RegisterHandler<SwingStart>(OnSwingStart);
            _client.RegisterHandler<StatusEffectApplied>(OnStatusEffectApplied);
            _client.RegisterHandler<StatusEffectRemoved>(OnStatusEffectRemoved);
        }

        // Movement handlers
        private void OnMovementConfirm(NetworkClient client, in MovementConfirm packet)
        {
            CallDeferred(nameof(EmitMovementConfirmed),
                packet.LastInputSequence, packet.X, packet.Y, packet.Z, packet.IsCorrection);
        }

        private void OnMovementRejected(NetworkClient client, in MovementRejected packet)
        {
            GD.Print($"[Network] Movement rejected: {packet.Reason}");
            CallDeferred(nameof(EmitMovementConfirmed),
                packet.InputSequence, packet.X, packet.Y, packet.Z, true);
        }

        private void OnEntityPosition(NetworkClient client, in EntityPosition packet)
        {
            CallDeferred(nameof(EmitEntityPosition),
                packet.Serial, packet.X, packet.Y, packet.Z, packet.Direction);
        }

        private void OnEntityPositionBatch(NetworkClient client, in EntityPositionBatch packet)
        {
            for (int i = 0; i < packet.Count; i++)
            {
                var e = packet.Entities[i];
                CallDeferred(nameof(EmitEntityPosition),
                    e.Serial, e.X, e.Y, e.Z, e.Direction);
            }
        }

        private void OnEntityTeleport(NetworkClient client, in EntityTeleport packet)
        {
            GD.Print($"[Network] Teleport: {packet.Serial} to ({packet.X}, {packet.Y}, {packet.Z}) map {packet.MapId}");
            CallDeferred(nameof(EmitEntityPosition),
                packet.Serial, packet.X, packet.Y, packet.Z, packet.Direction);
        }

        // Chat handlers
        private void OnChatReceive(NetworkClient client, in ChatReceive packet)
        {
            CallDeferred(nameof(EmitChatReceived),
                packet.SenderName, packet.Message, (int)packet.Type);
        }

        private void OnSystemMessage(NetworkClient client, in SystemMessage packet)
        {
            GD.Print($"[System] {packet.Type}: {packet.Message}");
            CallDeferred(nameof(EmitChatReceived),
                "System", packet.Message, (int)ChatType.System);
        }

        // Combat handlers
        private void OnDamageDealt(NetworkClient client, in DamageDealt packet)
        {
            CallDeferred(nameof(EmitDamageReceived),
                packet.TargetSerial, packet.SourceSerial, (int)packet.Amount, (int)packet.Type);
        }

        private void OnDamageNumber(NetworkClient client, in DamageNumber packet)
        {
            // Visual only - could emit a signal for floating damage numbers
            GD.Print($"[Combat] Damage number: {packet.Amount} on {packet.TargetSerial}");
        }

        private void OnHealthUpdate(NetworkClient client, in HealthUpdate packet)
        {
            CallDeferred(nameof(EmitHealthUpdated),
                packet.Serial, (int)packet.CurrentHealth, (int)packet.MaxHealth);
        }

        private void OnEntityDeath(NetworkClient client, in EntityDeath packet)
        {
            CallDeferred(nameof(EmitEntityDied), packet.Serial, packet.KillerSerial);
        }

        private void OnEntityResurrect(NetworkClient client, in EntityResurrect packet)
        {
            GD.Print($"[Network] Entity resurrected: {packet.Serial}");
        }

        private void OnSwingStart(NetworkClient client, in SwingStart packet)
        {
            GD.Print($"[Combat] Swing: {packet.AttackerSerial} -> {packet.DefenderSerial}");
        }

        private void OnStatusEffectApplied(NetworkClient client, in StatusEffectApplied packet)
        {
            GD.Print($"[Combat] Effect {packet.EffectId} applied to {packet.Serial}");
        }

        private void OnStatusEffectRemoved(NetworkClient client, in StatusEffectRemoved packet)
        {
            GD.Print($"[Combat] Effect {packet.EffectId} removed from {packet.Serial}");
        }

        // ═══════════════════════════════════════════════════════════════════
        //                  DEFERRED SIGNAL EMITTERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnConnectedDeferred()
        {
            _isConnecting = false;
            EmitSignal(SignalName.Connected);
        }

        private void OnDisconnectedDeferred(string reason)
        {
            _isConnecting = false;
            EmitSignal(SignalName.Disconnected, reason);
        }

        private void OnConnectionFailedDeferred(string error)
        {
            _isConnecting = false;
            EmitSignal(SignalName.ConnectionFailed, error);
        }

        private void EmitMovementConfirmed(uint seq, int x, int y, int z, bool correction)
        {
            EmitSignal(SignalName.MovementConfirmed, seq, x, y, z, correction);
        }

        private void EmitEntityPosition(uint serial, int x, int y, int z, int dir)
        {
            EmitSignal(SignalName.EntityPositionUpdated, serial, x, y, z, dir);
        }

        private void EmitChatReceived(string sender, string msg, int type)
        {
            EmitSignal(SignalName.ChatReceived, sender, msg, type);
        }

        private void EmitDamageReceived(uint target, uint source, int amount, int type)
        {
            EmitSignal(SignalName.DamageReceived, target, source, amount, type);
        }

        private void EmitHealthUpdated(uint serial, int current, int max)
        {
            EmitSignal(SignalName.HealthUpdated, serial, current, max);
        }

        private void EmitEntityDied(uint serial, uint killer)
        {
            EmitSignal(SignalName.EntityDied, serial, killer);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Connect to server.
        /// </summary>
        public async void Connect()
        {
            if (_client == null || _isConnecting || IsConnected)
                return;

            _isConnecting = true;
            GD.Print($"[Network] Connecting to {ServerHost}:{TcpPort}...");

            try
            {
                await _client.ConnectAsync(ServerHost, TcpPort, UdpPort);
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[Network] Connection failed: {ex.Message}");
                _isConnecting = false;
                EmitSignal(SignalName.ConnectionFailed, ex.Message);
            }
        }

        /// <summary>
        /// Disconnect from server.
        /// </summary>
        public void Disconnect()
        {
            _client?.Disconnect("User requested");
        }

        /// <summary>
        /// Send movement input.
        /// </summary>
        public void SendMovement(byte direction, bool running, uint sequence, int x, int y, int z)
        {
            if (!IsConnected) return;

            var flags = running ? MoveFlags.Running : MoveFlags.None;
            _client!.Send(new MovementRequest(direction, flags, sequence,
                (ushort)x, (ushort)y, (sbyte)z));
        }

        /// <summary>
        /// Send stop movement.
        /// </summary>
        public void SendMovementStop(uint sequence, int x, int y, int z)
        {
            if (!IsConnected) return;

            _client!.Send(new MovementStop(sequence, (ushort)x, (ushort)y, (sbyte)z));
        }

        /// <summary>
        /// Send attack request.
        /// </summary>
        public void SendAttack(uint targetSerial)
        {
            if (!IsConnected) return;

            _client!.Send(new AttackRequest(targetSerial));
        }

        /// <summary>
        /// Send stop attack.
        /// </summary>
        public void SendAttackStop()
        {
            if (!IsConnected) return;

            _client!.Send(new AttackStop());
        }

        /// <summary>
        /// Send ability use.
        /// </summary>
        public void SendUseAbility(int abilityId, uint targetSerial = 0, int targetX = 0, int targetY = 0, int targetZ = 0)
        {
            if (!IsConnected) return;

            _client!.Send(new UseAbility((ushort)abilityId, targetSerial,
                (ushort)targetX, (ushort)targetY, (sbyte)targetZ));
        }

        /// <summary>
        /// Send chat message.
        /// </summary>
        public void SendChat(string message, int chatType = 0, string? target = null)
        {
            if (!IsConnected) return;

            _client!.Send(new ChatSend((ChatType)chatType, message, target));
        }

        /// <summary>
        /// Send ping for latency measurement.
        /// </summary>
        public void SendPing()
        {
            if (!IsConnected) return;

            _client!.Send(new Ping(0, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
    }
}
