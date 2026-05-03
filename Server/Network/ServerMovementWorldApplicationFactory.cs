using System.Net;
using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Accounting;
using UltraSim.Server.Gameplay;
using UltraSim.Server.Persistence;
using UltraSim.Server.Scripting;

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
                Load = app => runtime.Load(app),
                Tick = (_, deltaSeconds) => runtime.Tick(deltaSeconds),
                Save = app => runtime.Save(app),
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
        private readonly InMemoryAccountRegistry _accounts = new();
        private readonly InMemoryCharacterRegistry _characters = new();
        private readonly AccountCharacterService _characterService;
        private UdpSocketNetworkTransport? _transport;
        private ServerMovementSessionHost? _host;
        private uint _nextNetworkEntityId = 1;
        private uint _serverTick;
        private ulong _lastSaveSequence;
        private bool _lastLoadStartedFresh;
        private bool _lastLoadUsedBackup;
        private int _lastPersistenceWarningCount;
        private int _lastPersistenceFailureCount;
        private string _lastPersistencePath = string.Empty;
        private long _lastPumpElapsedTicks;

        public Runtime(ServerMovementWorldApplicationOptions options)
        {
            _options = options;
            _characterService = new AccountCharacterService(_accounts, _characters);
            var builder = new EcsSchemaBuilder();
            ComponentId movementId = builder.RegisterComponent<ServerMovementBody>();
            PrefabCatalog? scriptPrefabCatalog = null;
            DefaultUltraSimScriptContext? scriptContext = options.ScriptContext;
            if (scriptContext is not null && scriptContext.RegisteredPrefabs.Count > 0)
            {
                ScriptPrefabCatalogBuildResult catalogResult = ScriptPrefabCatalogBuilder.Build(
                    scriptContext.RegisteredArchetypes,
                    scriptContext.RegisteredPrefabs,
                    builder);
                scriptPrefabCatalog = catalogResult.Catalog;
            }

            var services = scriptPrefabCatalog is null
                ? null
                : new EcsWorldServices { PrefabResolver = scriptPrefabCatalog };
            _world = new World(builder.Build(), services);
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

            var sessionPolicy = new SessionAdmissionPolicy(
                DuplicateSessionPolicy.RejectNew,
                MaxActiveSessions: 0,
                AuthTimeoutTicks: SecondsToTicks(_options.SessionAuthTimeoutSeconds),
                KeepAliveTimeoutTicks: SecondsToTicks(_options.SessionKeepAliveTimeoutSeconds));

            _host = new ServerMovementSessionHost(
                _world,
                _transport,
                ApplyMovementInput,
                ProjectMovementState,
                SpawnSession,
                despawnSession: DespawnSession,
                characterPositionProjector: ProjectCharacterPosition,
                characterPositionApplier: ApplyCharacterPosition,
                selectedCharacterSnapshotWriter: WriteSelectedCharacterSnapshot,
                handshakePolicy: handshakePolicy,
                sessionPolicy: sessionPolicy,
                allocateNetworkEntityId: AllocateNetworkEntityId,
                scriptContext: _options.ScriptContext,
                admissionRuntimePolicy: new AccountingAdmissionRuntimePolicy(
                    _characterService,
                    bindCharacter: TryBindSelectedCharacter),
                enableGlobalMovementObserverState: _options.EnableGlobalMovementObserverState);
            _host.Start();
        }

        public void Load(ServerApplication app)
        {
            ServerPersistenceCoordinator coordinator = CreatePersistenceCoordinator();
            ServerPersistenceLoadResult result = coordinator.Load();
            if (!result.Succeeded)
            {
                string message = result.Failures.Count == 0
                    ? "Server persistence load failed."
                    : string.Join("; ", result.Failures.Select(static failure => $"{failure.ParticipantName}: {failure.Message}"));
                throw new InvalidOperationException(message);
            }

            if (result.Manifest is not null)
                _lastSaveSequence = result.Manifest.SaveSequence;
            _lastLoadStartedFresh = result.StartedFresh;
            _lastLoadUsedBackup = result.LoadedBackup;
            _lastPersistenceWarningCount = result.Warnings.Count;
            _lastPersistenceFailureCount = result.Failures.Count;
            _lastPersistencePath = result.SaveSetRoot ?? coordinator.CurrentSaveSetRoot;

            EmitPersistenceLoadLogs(app, result);
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

        public void Save(ServerApplication app)
        {
            ServerPersistenceCoordinator coordinator = CreatePersistenceCoordinator();
            ServerPersistenceSaveResult result = coordinator.Save(DateTimeOffset.UtcNow);
            if (!result.Succeeded)
            {
                _lastPersistenceFailureCount = result.Failures.Count == 0 ? 1 : result.Failures.Count;
                _lastPersistencePath = coordinator.CurrentSaveSetRoot;
                string message = result.Failures.Count == 0
                    ? "Server persistence save failed."
                    : string.Join("; ", result.Failures.Select(static failure => $"{failure.ParticipantName}: {failure.Message}"));
                throw new InvalidOperationException(message);
            }

            _lastSaveSequence = result.Metadata.SaveSequence;
            _lastPersistenceFailureCount = 0;
            _lastPersistencePath = coordinator.CurrentSaveSetRoot;
            app.Log($"Persistence: Saved save-set sequence {result.Metadata.SaveSequence}.");
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
            return
            [
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
                new ServerSystemRuntimeSnapshot(
                    "Persistence",
                    "Persistence",
                    Enabled: true,
                    Manual: true,
                    PeriodTicks: 0,
                    StartOffset: 0,
                    LastRunTick: _serverTick,
                    UpdateCount: _lastSaveSequence > long.MaxValue ? long.MaxValue : (long)_lastSaveSequence,
                    LastUpdateMilliseconds: 0d,
                    AverageUpdateMilliseconds: 0d,
                    PeakUpdateMilliseconds: 0d,
                    MinUpdateMilliseconds: 0d,
                    ControlCount: _lastPersistenceWarningCount,
                    Detail: BuildPersistenceDetail()),
            ];
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
                bool hasSelected = host.TryGetSelectedCharacter(connectionId, out ServerSelectedCharacterSession selected);
                players[i] = new ServerPlayerSessionSnapshot(
                    connectionId.Value.ToString(),
                    hasSelected ? selected.CharacterName : $"Player {connectionId.Value}",
                    hasSelected ? selected.AccountId.ToString() : "local",
                    hasSelected ? "InWorld" : "Ready",
                    hasSelected ? $"character={selected.CharacterId} prefab={selected.PrefabKey}" : string.Empty);
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

        private static bool ProjectCharacterPosition(
            World world,
            Entity entity,
            out CharacterPositionSnapshot position)
        {
            if (!world.TryGetComponent(entity, out ServerMovementBody body))
            {
                position = default;
                return false;
            }

            position = new CharacterPositionSnapshot(body.PositionX, body.PositionY, 0f, 0f);
            return true;
        }

        private static bool ApplyCharacterPosition(
            World world,
            Entity entity,
            CharacterPositionSnapshot position)
        {
            if (!world.TryGetComponent(entity, out ServerMovementBody body))
                return false;

            body = body with
            {
                PositionX = position.X,
                PositionY = position.Y,
                VelocityX = 0f,
                VelocityY = 0f,
            };
            return world.SetComponent(entity, body);
        }

        private ServerMovementSessionSpawn SpawnSession(World world, ConnectionId connectionId)
        {
            uint networkEntityId = AllocateNetworkEntityId(connectionId);
            Entity entity = world.CreateDefault(_playerSignature);
            float spawnIndex = networkEntityId - 1;
            float x = (spawnIndex % 16) * 1.5f;
            float y = MathF.Floor(spawnIndex / 16) * 1.5f;
            world.SetComponent(entity, new ServerMovementBody(x, y, 0f, 0f));
            return new ServerMovementSessionSpawn(entity, networkEntityId);
        }

        private uint AllocateNetworkEntityId(ConnectionId connectionId)
        {
            _ = connectionId;
            return _nextNetworkEntityId++;
        }

        private bool TryBindSelectedCharacter(ConnectionId connectionId, ServerCharacterRecord character)
        {
            return _host?.TryEnterWorld(connectionId, character) ?? false;
        }

        private bool WriteSelectedCharacterSnapshot(
            ConnectionId connectionId,
            ServerSelectedCharacterSession selectedCharacter,
            CharacterPositionSnapshot position,
            DateTimeOffset nowUtc)
        {
            return _characterService.TryUpdateCharacterSnapshot(
                selectedCharacter.CharacterId,
                nowUtc,
                selectedCharacter.PrefabKey,
                position,
                out _);
        }

        private ServerPersistenceCoordinator CreatePersistenceCoordinator()
        {
            string root = _options.PersistenceRoot ??
                Path.Combine(AppContext.BaseDirectory, "saves", _options.ProfileName);
            return new ServerPersistenceCoordinator(
                root,
                [
                    new AccountingPersistenceParticipant(_accounts, _characters),
                    new EcsWorldPersistenceParticipant(_world)
                ],
                initialSaveSequence: _lastSaveSequence);
        }

        private string BuildPersistenceDetail()
        {
            string path = string.IsNullOrWhiteSpace(_lastPersistencePath)
                ? CreatePersistenceCoordinator().CurrentSaveSetRoot
                : _lastPersistencePath;
            string mode = _lastLoadStartedFresh
                ? "fresh"
                : _lastLoadUsedBackup
                    ? "backup"
                    : "current";
            return $"sequence={_lastSaveSequence} load={mode} warnings={_lastPersistenceWarningCount} failures={_lastPersistenceFailureCount} path={path}";
        }

        private static void EmitPersistenceLoadLogs(ServerApplication app, ServerPersistenceLoadResult result)
        {
            for (int i = 0; i < result.Warnings.Count; i++)
                app.Log($"Persistence: Warning: {result.Warnings[i]}");

            if (result.StartedFresh)
            {
                app.Log("Persistence: No save-set found; starting fresh.");
                return;
            }

            if (result.Manifest is null)
                return;

            string source = result.LoadedBackup ? "backup save-set" : "save-set";
            app.Log($"Persistence: Loaded {source} sequence {result.Manifest.SaveSequence}.");
        }

        private static long SecondsToTicks(double seconds)
        {
            if (seconds <= 0.0)
                return 0;

            double ticks = seconds * Stopwatch.Frequency;
            return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
        }

        private static void DespawnSession(World world, ConnectionId connectionId, Entity entity)
        {
            if (world.Entities.IsAlive(entity))
                world.Destroy(entity);
        }
    }
}
