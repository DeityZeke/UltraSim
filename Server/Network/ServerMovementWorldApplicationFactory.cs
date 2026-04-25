using System.Net;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public static class ServerMovementWorldApplicationFactory
{
    public static IServerApplication Create(ServerMovementWorldApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProfileName))
            throw new ArgumentException("Profile name cannot be empty.", nameof(options));
        if ((uint)options.Port > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options));

        var runtime = new Runtime(options);
        return new ServerApplication(
            new ServerApplicationOptions(options.ProfileName, SaveOnStop: options.SaveOnStop),
            new ServerApplicationHooks
            {
                Start = _ => runtime.Start(),
                Tick = (_, deltaSeconds) => runtime.Tick(deltaSeconds),
                Stop = _ => runtime.Stop(),
                IsAcceptingConnections = _ => runtime.IsAcceptingConnections,
                GetActiveSessions = _ => runtime.ActiveSessions,
                GetSystemRuntimeSnapshots = _ => runtime.GetSystemRuntimeSnapshots(),
                GetPlayerSessions = _ => runtime.GetPlayerSessionsSnapshot(),
            });
    }

    private sealed class Runtime : IDisposable
    {
        private readonly ServerMovementWorldApplicationOptions _options;
        private readonly World _world;
        private readonly ArchetypeSignature _playerSignature;
        private UdpSocketNetworkTransport? _transport;
        private ServerMovementSessionHost? _host;
        private uint _nextNetworkEntityId = 1;
        private uint _serverTick;
        private long _lastPumpElapsedTicks;

        public Runtime(ServerMovementWorldApplicationOptions options)
        {
            _options = options;
            var builder = new EcsSchemaBuilder();
            ComponentId movementId = builder.RegisterComponent<ServerMovementBody>();
            _world = new World(builder.Build());
            _playerSignature = new ArchetypeSignature([movementId]);
        }

        public bool IsAcceptingConnections => _host is not null;

        public int ActiveSessions => _host?.BoundConnectionCount ?? 0;

        public void Start()
        {
            if (_host is not null)
                return;

            var metadata = DefaultPacketMetadata.Create();
            _transport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
                new IPEndPoint(IPAddress.Any, _options.Port),
                metadata,
                EnableAsyncFlush: _options.EnableAsyncFlush));
            _transport.Start();

            var handshakePolicy = new HandshakePolicy(
                ProtocolVersion: 3,
                SchemaHash: 4,
                ContentHash: 5,
                RequiredFeatures: FeatureFlags.SharedPayloadFanout);

            _host = new ServerMovementSessionHost(
                _world,
                _transport,
                ApplyMovementInput,
                ProjectMovementState,
                SpawnSession,
                DespawnSession,
                handshakePolicy: handshakePolicy,
                enableGlobalMovementObserverState: _options.EnableGlobalMovementObserverState);
            _host.Start();
        }

        public void Tick(float deltaSeconds)
        {
            ServerMovementSessionHost? host = _host;
            if (host is null)
                return;

            long start = Stopwatch.GetTimestamp();
            host.Pump(Stopwatch.GetTimestamp(), deltaSeconds, ++_serverTick);
            _lastPumpElapsedTicks = Stopwatch.GetTimestamp() - start;
        }

        public void Stop()
        {
            _host?.Stop();
            _host = null;
            _transport?.Dispose();
            _transport = null;
        }

        public void Dispose()
        {
            Stop();
        }

        public IReadOnlyList<ServerSystemRuntimeSnapshot> GetSystemRuntimeSnapshots()
        {
            double lastMilliseconds = _lastPumpElapsedTicks <= 0
                ? 0d
                : _lastPumpElapsedTicks * 1000d / Stopwatch.Frequency;
            return new[]
            {
                new ServerSystemRuntimeSnapshot(
                    "NetworkSession",
                    "Network",
                    Enabled: _host is not null,
                    Manual: false,
                    PeriodTicks: 1,
                    StartOffset: 0,
                    LastRunTick: _serverTick,
                    UpdateCount: _serverTick,
                    LastUpdateMilliseconds: lastMilliseconds,
                    AverageUpdateMilliseconds: lastMilliseconds,
                    PeakUpdateMilliseconds: lastMilliseconds,
                    MinUpdateMilliseconds: lastMilliseconds,
                    ControlCount: ActiveSessions),
            };
        }

        public IReadOnlyList<ServerPlayerSessionSnapshot> GetPlayerSessionsSnapshot()
        {
            ServerMovementSessionHost? host = _host;
            if (host is null || host.BoundConnectionCount == 0)
                return Array.Empty<ServerPlayerSessionSnapshot>();

            var connections = new ConnectionId[host.BoundConnectionCount];
            int count = host.CopyBoundConnections(connections);
            var players = new ServerPlayerSessionSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                ConnectionId connectionId = connections[i];
                players[i] = new ServerPlayerSessionSnapshot(
                    connectionId.Value.ToString(),
                    $"Player {connectionId.Value}",
                    "local",
                    "Ready");
            }

            return players;
        }

        private void ApplyMovementInput(World world, Entity entity, in MovementInputPacket input, float deltaSeconds)
        {
            if (!world.TryGetComponent(entity, out ServerMovementBody body))
                return;

            float velocityX = input.MoveX / 100f * _options.MovementSpeed;
            float velocityY = input.MoveY / 100f * _options.MovementSpeed;
            body = body with
            {
                VelocityX = velocityX,
                VelocityY = velocityY,
                PositionX = body.PositionX + velocityX * deltaSeconds,
                PositionY = body.PositionY + velocityY * deltaSeconds,
            };
            world.SetComponent(entity, body);
        }

        private static bool ProjectMovementState(
            World world,
            Entity entity,
            uint networkEntityId,
            ushort lastProcessedInputSequence,
            uint serverTick,
            out MovementStatePacket packet)
        {
            if (!world.TryGetComponent(entity, out ServerMovementBody body))
            {
                packet = default;
                return false;
            }

            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        private ServerMovementSessionSpawn SpawnSession(World world, ConnectionId connectionId)
        {
            uint networkEntityId = _nextNetworkEntityId++;
            Entity entity = world.CreateDefault(_playerSignature);
            float spawnIndex = networkEntityId - 1;
            float x = (spawnIndex % 16) * 1.5f;
            float y = MathF.Floor(spawnIndex / 16) * 1.5f;
            world.SetComponent(entity, new ServerMovementBody(x, y, 0f, 0f));
            return new ServerMovementSessionSpawn(entity, networkEntityId);
        }

        private static void DespawnSession(World world, ConnectionId connectionId, Entity entity)
        {
            if (world.Entities.IsAlive(entity))
                world.Destroy(entity);
        }
    }
}
