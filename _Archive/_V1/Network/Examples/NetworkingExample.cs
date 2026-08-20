// Examples/NetworkingExample.cs
// Demonstration of how to use the networking system
#nullable enable

using Network.Shared.Network;
using Network.Shared.Network.Packets;
using Network.Server.Network;
using Network.Client.Network;

namespace Network.Examples
{
    /// <summary>
    /// Example showing how to set up and use the networking system.
    /// </summary>
    public static class NetworkingExample
    {
        // ═══════════════════════════════════════════════════════════════════
        //                      SERVER EXAMPLE
        // ═══════════════════════════════════════════════════════════════════

        public static void RunServerExample()
        {
            Console.WriteLine("=== Server Example ===\n");

            // Create connection manager with default config
            var config = new NetworkConfig
            {
                TcpPort = 2593,
                UdpPort = 2594,
                MaxConnections = 1000,
                ProtocolVersion = 1
            };

            using var server = new ConnectionManager(config);

            // Register system handlers (handshake, ping, disconnect)
            server.RegisterSystemHandlers();

            // Register game-specific handlers
            server.RegisterHandler<MovementRequest>(OnMovementRequest);
            server.RegisterHandler<AttackRequest>(OnAttackRequest);
            server.RegisterHandler<ChatSend>(OnChatSend);

            // Wire up events
            server.OnClientConnected += session =>
            {
                Console.WriteLine($"Client connected: Session {session.SessionId}");
            };

            server.OnClientDisconnected += (session, reason) =>
            {
                Console.WriteLine($"Client disconnected: Session {session.SessionId} - {reason}");
            };

            // Start the server
            server.Start();

            Console.WriteLine("Server running. Press Enter to stop...");
            Console.ReadLine();

            server.Stop();
        }

        // Server packet handlers
        private static void OnMovementRequest(ServerSession session, in MovementRequest packet)
        {
            Console.WriteLine($"[Session {session.SessionId}] Movement: Dir={packet.Direction}, " +
                $"Running={packet.Flags.HasFlag(MoveFlags.Running)}, " +
                $"Pos=({packet.PredictedX}, {packet.PredictedY}, {packet.PredictedZ})");

            // Validate and process movement...
            // For now, just confirm it
            session.Send(new MovementConfirm(
                packet.InputSequence,
                packet.PredictedX,
                packet.PredictedY,
                packet.PredictedZ,
                isCorrection: false
            ));
        }

        private static void OnAttackRequest(ServerSession session, in AttackRequest packet)
        {
            Console.WriteLine($"[Session {session.SessionId}] Attack target: {packet.TargetSerial}");

            // Process attack...
            session.Send(new SwingStart(
                attackerSerial: 1000, // Player's serial
                defenderSerial: packet.TargetSerial
            ));
        }

        private static void OnChatSend(ServerSession session, in ChatSend packet)
        {
            Console.WriteLine($"[Session {session.SessionId}] Chat ({packet.Type}): {packet.Message}");

            // Broadcast to nearby players...
            session.Send(new ChatReceive(
                packet.Type,
                senderSerial: 1000,
                senderName: "Player",
                message: packet.Message,
                hue: 0x3B2
            ));
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      CLIENT EXAMPLE
        // ═══════════════════════════════════════════════════════════════════

        public static async Task RunClientExample()
        {
            Console.WriteLine("=== Client Example ===\n");

            using var client = new NetworkClient(protocolVersion: 1, clientVersion: "1.0.0");

            // Register handlers for server packets
            client.RegisterHandler<MovementConfirm>(OnMovementConfirm);
            client.RegisterHandler<SwingStart>(OnSwingStart);
            client.RegisterHandler<ChatReceive>(OnChatReceive);
            client.RegisterHandler<DamageDealt>(OnDamageDealt);
            client.RegisterHandler<HealthUpdate>(OnHealthUpdate);
            client.RegisterHandler<EntityPosition>(OnEntityPosition);
            client.RegisterHandler<SystemMessage>(OnSystemMessage);

            // Wire up events
            client.OnConnected += () =>
            {
                Console.WriteLine($"Connected! Session ID: {client.SessionId}");
            };

            client.OnDisconnected += reason =>
            {
                Console.WriteLine($"Disconnected: {reason}");
            };

            // Connect to server
            try
            {
                await client.ConnectAsync("localhost", 2593, 2594);
                Console.WriteLine("Connection initiated...");

                // Wait for connection to complete
                await Task.Delay(1000);

                if (client.IsConnected)
                {
                    // Send some test packets
                    Console.WriteLine("\nSending test packets...\n");

                    // Movement
                    client.Send(new MovementRequest(
                        direction: 1,
                        flags: MoveFlags.Running,
                        inputSequence: 1,
                        predictedX: 100,
                        predictedY: 200,
                        predictedZ: 0
                    ));

                    // Attack
                    client.Send(new AttackRequest(targetSerial: 5000));

                    // Chat
                    client.Send(new ChatSend(ChatType.Say, "Hello, world!"));

                    // Wait for responses
                    await Task.Delay(2000);
                }

                Console.WriteLine("\nPress Enter to disconnect...");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
            }
        }

        // Client packet handlers
        private static void OnMovementConfirm(NetworkClient client, in MovementConfirm packet)
        {
            Console.WriteLine($"[Movement] Confirmed seq={packet.LastInputSequence}, " +
                $"pos=({packet.X}, {packet.Y}, {packet.Z}), correction={packet.IsCorrection}");
        }

        private static void OnSwingStart(NetworkClient client, in SwingStart packet)
        {
            Console.WriteLine($"[Combat] Swing: {packet.AttackerSerial} -> {packet.DefenderSerial}");
        }

        private static void OnChatReceive(NetworkClient client, in ChatReceive packet)
        {
            Console.WriteLine($"[Chat] {packet.SenderName}: {packet.Message}");
        }

        private static void OnDamageDealt(NetworkClient client, in DamageDealt packet)
        {
            Console.WriteLine($"[Combat] Damage: {packet.SourceSerial} hit {packet.TargetSerial} for {packet.Amount} {packet.Type}");
        }

        private static void OnHealthUpdate(NetworkClient client, in HealthUpdate packet)
        {
            Console.WriteLine($"[Health] Entity {packet.Serial}: {packet.CurrentHealth}/{packet.MaxHealth}");
        }

        private static void OnEntityPosition(NetworkClient client, in EntityPosition packet)
        {
            Console.WriteLine($"[Position] Entity {packet.Serial}: ({packet.X}, {packet.Y}, {packet.Z}) dir={packet.Direction}");
        }

        private static void OnSystemMessage(NetworkClient client, in SystemMessage packet)
        {
            Console.WriteLine($"[System] {packet.Type}: {packet.Message}");
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      GAME LOOP EXAMPLE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Example of integrating networking into a game loop.
        /// </summary>
        public static class GameLoopExample
        {
            private static ConnectionManager? _server;
            private static NetworkClient? _client;
            private static bool _running;

            public static void RunServerLoop()
            {
                _server = new ConnectionManager();
                _server.RegisterSystemHandlers();
                _server.Start();
                _running = true;

                // Main game loop
                var lastTick = DateTime.UtcNow;
                const int TickRateMs = 50; // 20 ticks per second

                while (_running)
                {
                    var now = DateTime.UtcNow;
                    var elapsed = now - lastTick;

                    if (elapsed.TotalMilliseconds >= TickRateMs)
                    {
                        lastTick = now;

                        // Process networking
                        _server.Tick();

                        // Update game state...
                        // ProcessGameLogic();

                        // Send updates to clients...
                        // BroadcastWorldState();
                    }
                    else
                    {
                        // Sleep until next tick
                        int sleepMs = TickRateMs - (int)elapsed.TotalMilliseconds;
                        if (sleepMs > 0)
                            Thread.Sleep(sleepMs);
                    }
                }

                _server.Dispose();
            }

            public static async Task RunClientLoop()
            {
                _client = new NetworkClient();
                await _client.ConnectAsync("localhost", 2593, 2594);
                _running = true;

                // Client game loop
                while (_running && _client.IsConnected)
                {
                    // Process input...
                    // var input = GetPlayerInput();

                    // Send movement if needed...
                    // if (input.HasMovement)
                    //     _client.Send(new MovementRequest(...));

                    // Process UDP retries
                    _client.ProcessUdpRetries();

                    // Render...
                    // RenderFrame();

                    await Task.Delay(16); // ~60 FPS
                }

                _client.Dispose();
            }

            public static void Stop()
            {
                _running = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                    CUSTOM PACKET EXAMPLE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Example of creating a custom packet.
        /// </summary>
        [EstimatedSize(32)]
        [FallbackToTcp]
        public readonly struct CustomSkillUse : IPacket<CustomSkillUse>, IClientPacket
        {
            // Static members - no instance needed, no boxing
            public static ushort PacketId => 0x8001; // Custom range starts at 0x8000
            public static ChannelType Channel => ChannelType.ReliableUnordered;

            // Instance fields
            public readonly ushort SkillId;
            public readonly byte Level;
            public readonly uint TargetSerial;
            public readonly ushort TargetX;
            public readonly ushort TargetY;
            public readonly bool IsCharged;
            public readonly float ChargePercent;

            public CustomSkillUse(ushort skillId, byte level, uint targetSerial,
                ushort targetX, ushort targetY, bool isCharged, float chargePercent)
            {
                SkillId = skillId;
                Level = level;
                TargetSerial = targetSerial;
                TargetX = targetX;
                TargetY = targetY;
                IsCharged = isCharged;
                ChargePercent = chargePercent;
            }

            public void Serialize(ref BitWriter writer)
            {
                writer.WriteUInt16(SkillId);
                writer.WriteByte(Level);
                writer.WriteUInt32(TargetSerial);
                writer.WriteUInt16(TargetX);
                writer.WriteUInt16(TargetY);
                writer.WriteBit(IsCharged);

                // Only write charge percent if charged
                if (IsCharged)
                {
                    // Pack charge percent (0-100%) into 7 bits
                    writer.WriteBits((uint)(ChargePercent * 100), 7);
                }

                writer.AlignToByte();
            }

            public static CustomSkillUse Deserialize(ref BitReader reader)
            {
                var skillId = reader.ReadUInt16();
                var level = reader.ReadByte();
                var target = reader.ReadUInt32();
                var x = reader.ReadUInt16();
                var y = reader.ReadUInt16();
                var charged = reader.ReadBit();
                var percent = charged ? reader.ReadBits(7) / 100f : 0f;
                reader.AlignToByte();

                return new CustomSkillUse(skillId, level, target, x, y, charged, percent);
            }
        }

        /// <summary>
        /// Show how to register and use custom packets.
        /// </summary>
        public static void CustomPacketExample()
        {
            using var server = new ConnectionManager();

            // Register custom packet handler
            server.RegisterHandler<CustomSkillUse>(OnCustomSkillUse);

            static void OnCustomSkillUse(ServerSession session, in CustomSkillUse packet)
            {
                Console.WriteLine($"Custom skill used: {packet.SkillId} level {packet.Level}");
                Console.WriteLine($"  Target: {packet.TargetSerial} at ({packet.TargetX}, {packet.TargetY})");
                Console.WriteLine($"  Charged: {packet.IsCharged} ({packet.ChargePercent:P0})");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //                      ENTRY POINT
        // ═══════════════════════════════════════════════════════════════════

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Network System Example\n");
            Console.WriteLine("1. Run Server");
            Console.WriteLine("2. Run Client");
            Console.WriteLine("3. Show Custom Packet Example");
            Console.Write("\nChoice: ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    RunServerExample();
                    break;
                case "2":
                    await RunClientExample();
                    break;
                case "3":
                    CustomPacketExample();
                    break;
                default:
                    Console.WriteLine("Invalid choice");
                    break;
            }
        }
    }
}
