using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;
using Godot;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Engines.Networking;
using UltraSim.Server.Gameplay;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.UI.Contracts;
using UltraSim.UI.Contracts.Bundled;

namespace Server.Godot
{
    [GlobalClass]
    public partial class ServerRuntimeHostNode : Node
    {
        private const string UltraSimVersion = "0.1.0";
        private const string DiscoverySettingsSection = "discovery";
        private const string DiscoveryOwnershipTokenKey = "ownership_token";

        private readonly IServerLogger _logger = new GodotServerLogger();
        private readonly TcpFramedNetworkTransport.ConnectionEvent[] _connectionEvents =
            new TcpFramedNetworkTransport.ConnectionEvent[16];
        private readonly Dictionary<long, string> _sessionIps = new();
        private readonly List<IServerDiscoveryPublisher> _discoveryPublishers = new();
        private HttpClient? _officialRegistryHttpClient;
        private CancellationTokenSource? _runtimeLoopCancellation;
        private Task? _runtimeLoopTask;
        private ServerAccountPersistenceCoordinator? _accountPersistenceCoordinator;
        private WorldPersistenceCoordinator? _worldPersistenceCoordinator;
        private CancellationTokenSource? _discoveryStatusCancellation;
        private Task? _discoveryStatusTask;
        private TcpListener? _discoveryStatusListener;
        private double _discoveryPublishAccumulatorSeconds;
        private bool _regenerateDiscoveryOwnershipTokenOnStart;
        private string? _discoveryOwnershipTokenStartupSuffix;
        private static readonly JsonSerializerOptions DiscoveryStatusJsonOptions = new(JsonSerializerDefaults.Web);

        // Per-session login challenge state: sessionId → (challenge16, panelInstanceId)
        private readonly Dictionary<long, (byte[] Challenge, UiPanelInstanceId PanelInstanceId)> _loginChallenges = new();
        // Per-session account after successful login (cleared on disconnect)
        private readonly Dictionary<long, AccountModel> _sessionAccounts = [];
        private readonly Dictionary<long, string> _sessionEnterGameDetails = [];
        private readonly Dictionary<long, bool> _sessionSkipDeleteCharacterConfirm = [];
        private const uint ConfirmDisconnectYesAction = SharedUiActionIds.QuitToMainMenu;
        private const uint ConfirmDisconnectNoAction = 2;
        private TcpFramedNetworkTransport? _transport;
        private ServerRuntime? _runtime;
        private double _tickAccumulatorMs;
        private double _statusAccumulatorSeconds;
        private double _metricsAccumulatorSeconds;
        private int _windowFrames;
        private int _windowTicksProcessed;
        private int _windowMaxTicksProcessedPerFrame;
        private int _windowInboundDatagrams;
        private double _windowPumpMsTotal;
        private double _windowPumpMsMax;
        private double _windowTickMsTotal;
        private double _windowTickMsMax;
        private double _windowFlushMsTotal;
        private double _windowFlushMsMax;
        private double _windowConnectionEventMsTotal;
        private double _windowConnectionEventMsMax;
        private int _gc0Baseline = GC.CollectionCount(0);
        private int _gc1Baseline = GC.CollectionCount(1);
        private int _gc2Baseline = GC.CollectionCount(2);
        private long _pingRxBaseline;
        private long _pongTxBaseline;
        private TcpFramedNetworkTransport.TcpUdpStatsSnapshot _transportStatsBaseline;
        private ServerNetworkControlService.TimingSnapshot _networkControlTimingBaseline;
        private ServerRuntime.RuntimeTimingSnapshot _runtimeTimingBaseline;
        private ServerPlayerReplicationService.ReplicationTimingSnapshot _replicationTimingBaseline;
        private ReplicationStatsSnapshot _replicationStatsBaseline;
        private ServerNetworkReplicationBridge.BridgeTimingSnapshot _bridgeTimingBaseline;
        private NetworkReplicationPipeline.BuildTimingSnapshot _pipelineTimingBaseline;
        private ReplicationReplayWindow.ReplayWindowSnapshot _replayWindowBaseline;

        [Export]
        public bool AutoStart { get; set; } = true;

        [Export]
        public string ProfileName { get; set; } = "server_runtime_host";

        [Export]
        public int TickMilliseconds { get; set; } = 16;

        [Export]
        // Under honest high-rate UDP load, pumping inbound once per frame backs up the transport queue.
        public bool DecoupleTransportPumpFromSimulation { get; set; } = false;

        [Export]
        public int MaxInboundDatagramsPerPump { get; set; } = 8192;

        [Export]
        public int MaxCatchUpTicksPerFrame { get; set; } = 512;

        [Export]
        public bool StartTransportOnStart { get; set; } = true;

        [Export]
        public bool StopTransportOnStop { get; set; } = true;

        [Export]
        public bool SaveOnStop { get; set; }

        [Export]
        public float AutoSaveEverySeconds { get; set; }

        [Export]
        public string SaveDirectory { get; set; } = string.Empty;

        [Export]
        public bool PublicVisible { get; set; }

        [Export]
        public string PublicDisplayName { get; set; } = "UltraSim Server";

        [Export]
        public string PublicServerId { get; set; } = string.Empty;

        [Export]
        public string BindHost { get; set; } = "0.0.0.0";

        [Export]
        public int Port { get; set; } = 42042;

        [Export]
        public bool EnableUdp { get; set; }

        [Export]
        public int UdpPort { get; set; }

        [Export]
        public string PublicConnectHint { get; set; } = string.Empty;

        [Export]
        public bool EnableDiscoveryStatusEndpoint { get; set; } = true;

        [Export]
        public string DiscoveryStatusBindHost { get; set; } = "127.0.0.1";

        [Export]
        public int DiscoveryStatusPort { get; set; } = 42043;

        [Export]
        public string PublicStatusEndpointUrl { get; set; } = string.Empty;

        [Export]
        public string PublicProtocolVersion { get; set; } = "dev";

        [Export]
        public string PublicContentVersion { get; set; } = "dev";

        [Export]
        public string PublicRegion { get; set; } = "local";

        [Export]
        public string PublicDescription { get; set; } = "Tracked server host bootstrap.";

        [Export]
        public string PublicPrimaryLanguage { get; set; } = "en";

        [Export]
        public string[] PublicLanguages { get; set; } = new[] { "en" };

        [Export]
        public string PublicWebsiteUrl { get; set; } = string.Empty;

        [Export]
        public string PublicDiscordUrl { get; set; } = string.Empty;

        [Export]
        public int PublicMaxUsers { get; set; }

        [Export]
        public string PublicCompatibilityId { get; set; } = string.Empty;

        [Export]
        public bool PasswordRequired { get; set; }

        [Export]
        public bool WhitelistOnly { get; set; }

        [Export]
        public string[] PublicTags { get; set; } = Array.Empty<string>();

        [Export]
        public string OfficialRegistryBaseUrl { get; set; } = "http://127.0.0.1:5180";

        [Export]
        public bool RegisterWithTrackerOnStartup { get; set; }

        [Export]
        public string DiscoveryPublishToken { get; set; } = string.Empty;

        [Export]
        public string DiscoveryOwnershipToken { get; set; } = string.Empty;

        [Export]
        public bool AutoGenerateDiscoveryOwnershipToken { get; set; } = true;

        [Export]
        public string DiscoveryOwnershipTokenSettingsPath { get; set; } = string.Empty;

        [Export]
        public float DiscoveryPublishEverySeconds { get; set; } = 15f;

        [Export]
        public float StatusLogEverySeconds { get; set; } = 10f;

        [Export]
        public bool EnableStatusLog { get; set; } = false;

        [Export]
        public bool LogConnectionEvents { get; set; }

        [Export]
        public bool EnableDiagnostics { get; set; }

        [Export]
        public bool EnableMetricsLite { get; set; }

        [Export]
        public float DiagnosticsLogEverySeconds { get; set; } = 1f;

        [Export]
        public int ChatMinTicksBetweenMessages { get; set; } = 8;

        [Export]
        public bool EnableChatCommands { get; set; } = true;

        [Export]
        public bool EnableDenseLocalChatRecipientCap { get; set; } = true;

        [Export]
        public int DenseLocalChatCapStartRecipients { get; set; } = 24;

        [Export]
        public int DenseLocalChatCapMaxRecipients { get; set; } = 8;

        [Export]
        public bool EnableDenseLocalChatRateCap { get; set; } = true;

        [Export]
        public int DenseLocalChatRateCapStartRecipients { get; set; } = 24;

        [Export]
        public int DenseLocalChatRateCapMinTicks { get; set; } = 60;

        [Export]
        public bool EnableChatBatching { get; set; } = true;

        [Export]
        public int PlayerSpawnSpacingGrid { get; set; } = 4;

        [Export]
        public int PlayerSpawnWrapCount { get; set; }

        [Export]
        public bool EnablePlayerChunkAoi { get; set; } = true;

        [Export]
        public int PlayerChunkSizeGridUnits { get; set; } = 40;

        [Export]
        public int PlayerChunkAoiRadius { get; set; } = 1;

        [Export]
        public bool EnablePlayerRecipientSelectorFastPath { get; set; } = true;

        [Export]
        public bool EnablePlayerRecipientSelectorChunkGroupCacheKey { get; set; } = true;

        [Export]
        public bool EnablePlayerPriorityLod { get; set; } = true;

        [Export]
        public int PlayerPriorityLodNearCadenceTicks { get; set; } = 1;

        [Export]
        public int PlayerPriorityLodOuterCadenceTicks { get; set; } = 2;

        [Export]
        public int PlayerPriorityLodRing1MaxChunks { get; set; } = 3;

        [Export]
        public int PlayerPriorityLodFarCadenceTicks { get; set; } = 3;

        [Export]
        public bool EnablePlayerDenseObserverCadence { get; set; }

        [Export]
        public int PlayerDenseObserverCadenceStartSubscribers { get; set; } = 64;

        [Export]
        public int PlayerDenseObserverCadenceStepSubscribers { get; set; } = 32;

        [Export]
        public int PlayerDenseObserverCadenceMaxTicks { get; set; } = 4;

        [Export]
        public bool EnablePlayerDenseObserverRecipientCap { get; set; }

        [Export]
        public int PlayerDenseObserverRecipientCapStartSubscribers { get; set; } = 64;

        [Export]
        public int PlayerDenseObserverRecipientCapMaxRecipients { get; set; } = 24;

        [Export]
        public int PlayerDenseObserverRecipientPriorityChunkRadius { get; set; }

        [Export]
        public int PlayerAoiDespawnGraceTicks { get; set; }

        [Export]
        public bool EnablePlayerDualCorrectionStreams { get; set; } = true;

        [Export]
        public bool EnablePlayerCorrectionBatching { get; set; } = true;

        [Export]
        public bool EnablePlayerCorrectionDeadband { get; set; } = true;

        [Export]
        public int PlayerCorrectionDeadbandUnits { get; set; }

        [Export]
        public int PlayerCorrectionMaxSilentTicks { get; set; } = 1;

        [Export]
        public int PlayerReplicationBuildBatchPacketLimit { get; set; } = 512;

        [Export]
        public int MaxReplicationBuildWorkers { get; set; }

        [Export]
        public int PlayerObserverStreamCadenceTicks { get; set; } = 1;

        [Export]
        public bool EnableObserverSpatialFirstBuild { get; set; }

        public ServerRuntime? Runtime => _runtime;

        public override void _Ready()
        {
            ApplyCommandLineOverrides(OS.GetCmdlineUserArgs());
            EnsureDiscoveryOwnershipToken();

            _logger.Info($"UltraSim - Version {UltraSimVersion}");

            try
            {
                EnsureRuntime();
                _logger.Info("Core: Created");
            }
            catch (Exception ex)
            {
                _logger.Error("Core: Created - failed");
                _logger.Error($"  {ex.Message}");
                return;
            }

            Version runtimeVer = System.Environment.Version;
            _logger.Info($"Core: Running on .NET {runtimeVer.Major}.{runtimeVer.Minor}.{runtimeVer.Build}");

            string[] cmdArgs = OS.GetCmdlineUserArgs();
            if (cmdArgs.Length > 0)
                _logger.Info($"Core: Running with arguments: {string.Join(" ", cmdArgs)}");

            int cores = Math.Min(2, System.Environment.ProcessorCount);
            _logger.Info($"Core: Optimizing for {cores} {(cores == 1 ? "processor" : "processors")}");

            if (AutoStart)
                StartRuntime();
        }

        public override void _ExitTree()
        {
            try
            {
                StopRuntime();
            }
            finally
            {
                StopDiscoveryStatusEndpoint();
                _officialRegistryHttpClient?.Dispose();
                _officialRegistryHttpClient = null;
                _accountPersistenceCoordinator = null;
                _worldPersistenceCoordinator = null;
                _transport?.Dispose();
                _transport = null;
                _runtime = null;
            }
        }

        public void StartRuntime()
        {
            EnsureRuntime();
            if (_runtime == null)
                return;

            if (_runtime.State == ServerRuntimeState.Running)
                return;

            _runtime.Start();
            _tickAccumulatorMs = 0d;
            _statusAccumulatorSeconds = 0d;
            _metricsAccumulatorSeconds = 0d;
            StartDiscoveryStatusEndpoint();
            StartRuntimeLoop();
        }

        public void StopRuntime()
        {
            if (_runtime == null)
                return;

            StopRuntimeLoop();
            StopDiscoveryStatusEndpoint();
            _runtime.Stop();
            _tickAccumulatorMs = 0d;
            _statusAccumulatorSeconds = 0d;
            _metricsAccumulatorSeconds = 0d;
        }

        public void RestartRuntime()
        {
            EnsureRuntime();
            if (_runtime == null)
                return;

            StopRuntimeLoop();
            StopDiscoveryStatusEndpoint();
            _runtime.Restart();
            _tickAccumulatorMs = 0d;
            _statusAccumulatorSeconds = 0d;
            _metricsAccumulatorSeconds = 0d;
            StartDiscoveryStatusEndpoint();
            StartRuntimeLoop();
        }

        public bool SaveRuntime()
        {
            if (_runtime == null)
                return false;

            return _runtime.Save();
        }

        public string RegenerateDiscoveryOwnershipToken()
        {
            string token = GenerateDiscoveryOwnershipToken();
            DiscoveryOwnershipToken = token;

            try
            {
                SaveDiscoveryOwnershipToken(token);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Discovery ownership token regenerate save failed - {ex.Message}");
            }

            _logger.Info($"Discovery: Ownership Token {token} (regen)");
            NotifyDiscoveryOwnershipTokenChanged();
            return token;
        }

        private void EnsureRuntime()
        {
            if (_runtime != null)
                return;

            _transport = new TcpFramedNetworkTransport(
                TcpFramedNetworkTransport.TransportRole.Server,
                host: string.IsNullOrWhiteSpace(BindHost) ? "0.0.0.0" : BindHost,
                port: Port <= 0 ? 42042 : Port,
                enableUdp: EnableUdp,
                udpPort: UdpPort > 0 ? UdpPort : null);
            _discoveryPublishers.Clear();
            ConfigureDiscoveryPublishers();
            _accountPersistenceCoordinator = CreateAccountPersistenceCoordinator();
            _worldPersistenceCoordinator = CreateWorldPersistenceCoordinator();

            _runtime = new ServerRuntime(
                _transport,
                options: new ServerRuntimeOptions(
                    ProfileName,
                    TickMilliseconds: Math.Max(1, TickMilliseconds),
                    StartTransportOnStart: StartTransportOnStart,
                    StopTransportOnStop: StopTransportOnStop,
                    SaveOnStop: SaveOnStop,
                    AutoSaveEverySeconds: Math.Max(0f, AutoSaveEverySeconds),
                    SaveDirectory: ResolveSaveDirectory(),
                    PublicServerId: string.IsNullOrWhiteSpace(PublicServerId) ? null : PublicServerId.Trim(),
                    PublicDisplayName: string.IsNullOrWhiteSpace(PublicDisplayName) ? ProfileName : PublicDisplayName,
                    PublicConnectHint: ResolvePublicConnectHint(),
                    PublicPrimaryLanguage: string.IsNullOrWhiteSpace(PublicPrimaryLanguage) ? null : PublicPrimaryLanguage.Trim(),
                    PublicLanguages: PublicLanguages.Length == 0 ? null : PublicLanguages,
                    PublicWebsiteUrl: string.IsNullOrWhiteSpace(PublicWebsiteUrl) ? null : PublicWebsiteUrl.Trim(),
                    PublicDiscordUrl: string.IsNullOrWhiteSpace(PublicDiscordUrl) ? null : PublicDiscordUrl.Trim(),
                    PublicMaxUsers: PublicMaxUsers > 0 ? PublicMaxUsers : null,
                    PublicCompatibilityId: string.IsNullOrWhiteSpace(PublicCompatibilityId) ? null : PublicCompatibilityId.Trim(),
                    PublicProtocolVersion: string.IsNullOrWhiteSpace(PublicProtocolVersion) ? null : PublicProtocolVersion,
                    PublicContentVersion: string.IsNullOrWhiteSpace(PublicContentVersion) ? null : PublicContentVersion,
                    PublicStatusEndpointUrl: ResolvePublicStatusEndpointUrl(),
                    PublicOwnershipTokenHash: ComputeOwnershipTokenHashOrNull(),
                    PublicVisible: PublicVisible,
                    PasswordRequired: PasswordRequired,
                    WhitelistOnly: WhitelistOnly,
                    PublicRegion: string.IsNullOrWhiteSpace(PublicRegion) ? null : PublicRegion,
                    PublicDescription: string.IsNullOrWhiteSpace(PublicDescription) ? null : PublicDescription,
                    PublicTags: PublicTags.Length == 0 ? null : PublicTags,
                    ChatMinTicksBetweenMessages: Math.Max(0, ChatMinTicksBetweenMessages),
                    EnableChatCommands: EnableChatCommands,
                    EnableDenseLocalChatRecipientCap: EnableDenseLocalChatRecipientCap,
                    DenseLocalChatCapStartRecipients: Math.Max(1, DenseLocalChatCapStartRecipients),
                    DenseLocalChatCapMaxRecipients: Math.Max(1, DenseLocalChatCapMaxRecipients),
                    EnableDenseLocalChatRateCap: EnableDenseLocalChatRateCap,
                    DenseLocalChatRateCapStartRecipients: Math.Max(1, DenseLocalChatRateCapStartRecipients),
                    DenseLocalChatRateCapMinTicks: Math.Max(1, DenseLocalChatRateCapMinTicks),
                    EnableChatBatching: EnableChatBatching,
                    PlayerSpawnSpacingGrid: Math.Max(0, PlayerSpawnSpacingGrid),
                    PlayerSpawnWrapCount: Math.Max(0, PlayerSpawnWrapCount),
                    EnablePlayerChunkAoi: EnablePlayerChunkAoi,
                    PlayerChunkSizeGridUnits: Math.Max(1, PlayerChunkSizeGridUnits),
                    PlayerChunkAoiRadius: Math.Max(0, PlayerChunkAoiRadius),
                    EnablePlayerRecipientSelectorFastPath: EnablePlayerRecipientSelectorFastPath,
                    EnablePlayerRecipientSelectorChunkGroupCacheKey: EnablePlayerRecipientSelectorChunkGroupCacheKey,
                    EnablePlayerPriorityLod: EnablePlayerPriorityLod,
                    PlayerPriorityLodNearCadenceTicks: Math.Max(1, PlayerPriorityLodNearCadenceTicks),
                    PlayerPriorityLodOuterCadenceTicks: Math.Max(1, PlayerPriorityLodOuterCadenceTicks),
                    PlayerPriorityLodRing1MaxChunks: Math.Max(1, PlayerPriorityLodRing1MaxChunks),
                    PlayerPriorityLodFarCadenceTicks: Math.Max(1, PlayerPriorityLodFarCadenceTicks),
                    EnablePlayerDenseObserverCadence: EnablePlayerDenseObserverCadence,
                    PlayerDenseObserverCadenceStartSubscribers: Math.Max(1, PlayerDenseObserverCadenceStartSubscribers),
                    PlayerDenseObserverCadenceStepSubscribers: Math.Max(1, PlayerDenseObserverCadenceStepSubscribers),
                    PlayerDenseObserverCadenceMaxTicks: Math.Max(1, PlayerDenseObserverCadenceMaxTicks),
                    EnablePlayerDenseObserverRecipientCap: EnablePlayerDenseObserverRecipientCap,
                    PlayerDenseObserverRecipientCapStartSubscribers: Math.Max(1, PlayerDenseObserverRecipientCapStartSubscribers),
                    PlayerDenseObserverRecipientCapMaxRecipients: Math.Max(1, PlayerDenseObserverRecipientCapMaxRecipients),
                    PlayerDenseObserverRecipientPriorityChunkRadius: Math.Max(0, PlayerDenseObserverRecipientPriorityChunkRadius),
                    PlayerAoiDespawnGraceTicks: Math.Max(0, PlayerAoiDespawnGraceTicks),
                    EnablePlayerDualCorrectionStreams: EnablePlayerDualCorrectionStreams,
                    EnablePlayerCorrectionBatching: EnablePlayerCorrectionBatching,
                    EnablePlayerCorrectionDeadband: EnablePlayerCorrectionDeadband,
                    PlayerCorrectionDeadbandUnits: Math.Max(0, PlayerCorrectionDeadbandUnits),
                    PlayerCorrectionMaxSilentTicks: Math.Max(1, PlayerCorrectionMaxSilentTicks),
                    PlayerReplicationBuildBatchPacketLimit: Math.Max(1, PlayerReplicationBuildBatchPacketLimit),
                    MaxReplicationBuildWorkers: MaxReplicationBuildWorkers < -1 ? -1 : MaxReplicationBuildWorkers,
                    PlayerObserverStreamCadenceTicks: Math.Max(1, PlayerObserverStreamCadenceTicks),
                    EnableObserverSpatialFirstBuild: EnableObserverSpatialFirstBuild),
                hooks: CreateRuntimeHooks(),
                discoveryPublishers: _discoveryPublishers);

            _runtime.Events.StateChanged += OnRuntimeStateChanged;
            _runtime.Events.Faulted += OnRuntimeFaulted;
            _runtime.Events.SessionAccepted += OnSessionAccepted;
            _runtime.Events.AccountVerified += OnAccountVerified;
            _runtime.Events.CharacterCreateRequested += OnCharacterCreateRequested;
            _runtime.Events.ReturnToCharacterSelectRequested += OnReturnToCharacterSelectRequested;
            _runtime.Events.SessionReady += OnSessionReady;
            _runtime.Events.ClientDisconnected += OnClientDisconnected;

            // Pre-register schema definitions so NeedDefinition acks can be served on first open.
            _runtime.UiPanels.RegisterDefinition(new UI.LoginPanel().ExtractDefinition().ToWireRecord());
            _runtime.UiPanels.RegisterDefinition(
                new UI.CharacterSelectSchemaPanel(
                    username: string.Empty,
                    usedCharacters: 0,
                    maxCharacters: 1,
                    characters: Array.Empty<CharacterModel>(),
                    selectHandler: static _ => { },
                    deleteHandler: static _ => { },
                    createHandler: static () => { },
                    cancelHandler: static () => { }).ExtractDefinition().ToWireRecord());
            _runtime.UiPanels.RegisterDefinition(
                new UI.CharacterCreateSchemaPanel(
                    createHandler: static (_, _) => { },
                    cancelHandler: static () => { }).ExtractDefinition().ToWireRecord());

            // Wire the packet-bound login handler into the UI inbound datagram handler.
            _runtime.UiInbound.LoginRequestHandler = OnLoginRequest;

        }

        private ServerRuntimeHooks CreateRuntimeHooks()
        {
            ServerAccountPersistenceCoordinator? accountCoordinator = _accountPersistenceCoordinator;
            WorldPersistenceCoordinator? coordinator = _worldPersistenceCoordinator;
            ServerPersistenceCoordinator? persistenceCoordinator =
                accountCoordinator is null || coordinator is null
                    ? null
                    : new ServerPersistenceCoordinator(accountCoordinator, coordinator);
            return new ServerRuntimeHooks
            {
                Load = persistenceCoordinator is null ? null : runtime => persistenceCoordinator.Load(runtime),
                CaptureSaveSnapshot = persistenceCoordinator is null ? null : runtime => persistenceCoordinator.CaptureSnapshot(runtime),
                WriteSaveSnapshot = persistenceCoordinator is null ? null : snapshot => persistenceCoordinator.WriteSnapshot((ServerPersistenceSaveSnapshot)snapshot),
            };
        }

        private ServerAccountPersistenceCoordinator CreateAccountPersistenceCoordinator()
        {
            return new ServerAccountPersistenceCoordinator();
        }

        private WorldPersistenceCoordinator CreateWorldPersistenceCoordinator()
        {
            ServerPersistenceRegistration.EnsureComponentsRegistered();

            var coordinator = new WorldPersistenceCoordinator(
                new WorldPersistenceOptions(
                    SaveDirectory: ResolveSaveDirectory(),
                    RecoveryMode: WorldPersistenceRecoveryMode.Abort,
                    RecoveryInteraction: new GodotWorldPersistenceRecoveryInteraction(_logger)));

            ServerPersistenceRegistration.RegisterPersistentTypes(coordinator);
            return coordinator;
        }

        private string? ResolveSaveDirectory()
            => string.IsNullOrWhiteSpace(SaveDirectory) ? null : SaveDirectory.Trim();

        private sealed class GodotWorldPersistenceRecoveryInteraction : IWorldPersistenceRecoveryInteraction
        {
            private readonly IServerLogger _logger;

            public GodotWorldPersistenceRecoveryInteraction(IServerLogger logger)
            {
                _logger = logger;
            }

            public WorldPersistenceRecoveryMode? ResolveMissingOptionalSections(WorldPersistenceRecoveryRequest request)
            {
                _logger.Warn($"World: Save set is incomplete at '{request.RootPath}'.");

                for (int i = 0; i < request.MissingSystemFiles.Count; i++)
                    _logger.Warn($"World: Missing system persistence file - {request.MissingSystemFiles[i]}");

                for (int i = 0; i < request.MissingEntityFiles.Count; i++)
                    _logger.Warn($"World: Missing entity persistence file - {request.MissingEntityFiles[i]}");

                _logger.Warn($"World: Applying configured recovery mode - {request.ConfiguredMode}");
                return null;
            }
        }

        private void StartDiscoveryStatusEndpoint()
        {
            StopDiscoveryStatusEndpoint();

            if (!IsDiscoveryStatusEndpointEnabled())
                return;

            if (_runtime == null)
                return;

            int port = DiscoveryStatusPort > 0 ? DiscoveryStatusPort : ((Port <= 0 ? 42042 : Port) + 1);
            string bindHost = string.IsNullOrWhiteSpace(DiscoveryStatusBindHost) ? "127.0.0.1" : DiscoveryStatusBindHost.Trim();

            try
            {
                IPAddress bindAddress = ResolveStatusBindAddress(bindHost);
                _discoveryStatusListener = new TcpListener(bindAddress, port);
                _discoveryStatusListener.Start();
                _discoveryStatusCancellation = new CancellationTokenSource();
                CancellationToken cancellationToken = _discoveryStatusCancellation.Token;
                _discoveryStatusTask = Task.Run(() => RunDiscoveryStatusEndpointAsync(_discoveryStatusListener, cancellationToken), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Discovery status endpoint failed to start - {ex.GetType().Name}: {ex.Message}");
                _discoveryStatusListener?.Stop();
                _discoveryStatusListener = null;
                _discoveryStatusCancellation?.Dispose();
                _discoveryStatusCancellation = null;
                _discoveryStatusTask = null;
            }
        }

        private void StopDiscoveryStatusEndpoint()
        {
            CancellationTokenSource? cancellation = _discoveryStatusCancellation;
            Task? endpointTask = _discoveryStatusTask;
            TcpListener? listener = _discoveryStatusListener;
            _discoveryStatusCancellation = null;
            _discoveryStatusTask = null;
            _discoveryStatusListener = null;

            if (cancellation == null && listener == null)
                return;

            try
            {
                cancellation?.Cancel();
            }
            catch
            {
            }

            try
            {
                listener?.Stop();
            }
            catch
            {
            }

            try
            {
                endpointTask?.Wait(500);
            }
            catch
            {
            }
            finally
            {
                cancellation?.Dispose();
            }
        }

        private async Task RunDiscoveryStatusEndpointAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    await HandleDiscoveryStatusClientAsync(client, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    client?.Dispose();
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    client?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    client?.Dispose();
                    _logger.Warn($"Discovery status endpoint request failed - {ex.Message}");
                }
            }
        }

        private async Task HandleDiscoveryStatusClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            await using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
            {
                string? requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                    return;

                string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string method = parts.Length > 0 ? parts[0] : string.Empty;
                string path = parts.Length > 1 ? parts[1] : string.Empty;

                while (!cancellationToken.IsCancellationRequested)
                {
                    string? headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(headerLine))
                        break;
                }

                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
                    !(string.Equals(path, "/discovery/status", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(path, "/discovery/status/", StringComparison.OrdinalIgnoreCase)))
                {
                    await WriteHttpResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (_runtime == null)
                {
                    await WriteHttpResponseAsync(stream, "503 Service Unavailable", "text/plain; charset=utf-8", "Runtime unavailable", cancellationToken).ConfigureAwait(false);
                    return;
                }

                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                ServerDiscoveryStatusInfo statusInfo = _runtime.GetDiscoveryStatusInfo(nowUtc) with
                {
                    LastHeartbeatUtc = nowUtc,
                };

                ServerDiscoveryPollStatusResponse payload = new(
                    _runtime.GetAdvertisedInfo().ServerId,
                    statusInfo,
                    ComputeOwnershipTokenHashOrNull());

                string json = JsonSerializer.Serialize(payload, DiscoveryStatusJsonOptions);
                await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task WriteHttpResponseAsync(
            NetworkStream stream,
            string statusLine,
            string contentType,
            string body,
            CancellationToken cancellationToken)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            string headers =
                $"HTTP/1.1 {statusLine}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static IPAddress ResolveStatusBindAddress(string bindHost)
        {
            if (string.Equals(bindHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
                return IPAddress.Any;

            if (IPAddress.TryParse(bindHost, out IPAddress? parsedAddress))
                return parsedAddress;

            IPHostEntry hostEntry = Dns.GetHostEntry(bindHost);
            for (int i = 0; i < hostEntry.AddressList.Length; i++)
            {
                if (hostEntry.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    return hostEntry.AddressList[i];
            }

            return IPAddress.Loopback;
        }

        private void StartRuntimeLoop()
        {
            StopRuntimeLoop();

            _runtimeLoopCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _runtimeLoopCancellation.Token;
            _runtimeLoopTask = Task.Run(async () =>
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                long previousTicks = stopwatch.ElapsedTicks;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_runtime == null || !_runtime.IsRunning)
                        break;

                    long currentTicks = stopwatch.ElapsedTicks;
                    double deltaSeconds = (currentTicks - previousTicks) / (double)Stopwatch.Frequency;
                    previousTicks = currentTicks;

                    if (deltaSeconds < 0d)
                        deltaSeconds = 0d;
                    else if (deltaSeconds > 0.25d)
                        deltaSeconds = 0.25d;

                    try
                    {
                        StepRuntimeFrame(deltaSeconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Runtime loop frame exception - {ex.GetType().Name}: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, cancellationToken);
        }

        private void StopRuntimeLoop()
        {
            CancellationTokenSource? cancellation = _runtimeLoopCancellation;
            Task? loopTask = _runtimeLoopTask;
            _runtimeLoopCancellation = null;
            _runtimeLoopTask = null;

            if (cancellation == null)
                return;

            try
            {
                cancellation.Cancel();
                loopTask?.Wait(500);
            }
            catch
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void StepRuntimeFrame(double delta)
        {
            if (_runtime == null || !_runtime.IsRunning)
                return;

            double tickMs = Math.Max(1, TickMilliseconds);
            _tickAccumulatorMs += delta * 1000.0;
            long frameNowTicks = unchecked((long)Time.GetTicksUsec());
            _windowFrames++;

            if (DecoupleTransportPumpFromSimulation)
            {
                long start = Stopwatch.GetTimestamp();
                ProcessConnectionEvents(frameNowTicks);
                RecordConnectionEventElapsed(start);

                start = Stopwatch.GetTimestamp();
                _windowInboundDatagrams += _runtime.ProcessInboundTransportDatagrams(frameNowTicks, Math.Max(1, MaxInboundDatagramsPerPump));
                RecordPumpElapsed(start);

                start = Stopwatch.GetTimestamp();
                _runtime.Transport.FlushOutbound();
                RecordFlushElapsed(start);
            }

            int ticksProcessed = 0;
            while (_tickAccumulatorMs >= tickMs && ticksProcessed < Math.Max(1, MaxCatchUpTicksPerFrame))
            {
                long nowTicks = unchecked((long)Time.GetTicksUsec());
                if (!DecoupleTransportPumpFromSimulation)
                {
                    long stageStart = Stopwatch.GetTimestamp();
                    ProcessConnectionEvents(nowTicks);
                    RecordConnectionEventElapsed(stageStart);

                    stageStart = Stopwatch.GetTimestamp();
                    _windowInboundDatagrams += _runtime.ProcessInboundTransportDatagrams(nowTicks, Math.Max(1, MaxInboundDatagramsPerPump));
                    RecordPumpElapsed(stageStart);
                }

                long tickStart = Stopwatch.GetTimestamp();
                _runtime.Tick((float)(tickMs / 1000.0));
                RecordTickElapsed(tickStart);

                long flushStart = Stopwatch.GetTimestamp();
                _runtime.Transport.FlushOutbound();
                RecordFlushElapsed(flushStart);

                _tickAccumulatorMs -= tickMs;
                ticksProcessed++;
            }
            _windowTicksProcessed += ticksProcessed;
            if (ticksProcessed > _windowMaxTicksProcessedPerFrame)
                _windowMaxTicksProcessedPerFrame = ticksProcessed;

            if (StatusLogEverySeconds <= 0f)
            {
                TickDiscoveryPublish(delta);

                if (EnableDiagnostics || EnableMetricsLite)
                {
                    _metricsAccumulatorSeconds += delta;
                    if (_metricsAccumulatorSeconds >= Math.Max(0.1f, DiagnosticsLogEverySeconds))
                    {
                        _metricsAccumulatorSeconds = 0d;
                        LogDiagnosticsSnapshot();
                    }
                }

                return;
            }

            _statusAccumulatorSeconds += delta;
            if (_statusAccumulatorSeconds >= StatusLogEverySeconds)
            {
                _statusAccumulatorSeconds = 0d;
                LogStatusSnapshot();
                if ((EnableDiagnostics || EnableMetricsLite) && _metricsAccumulatorSeconds > 0d)
                {
                    _metricsAccumulatorSeconds = 0d;
                    LogDiagnosticsSnapshot();
                }
            }

            if (EnableDiagnostics || EnableMetricsLite)
            {
                _metricsAccumulatorSeconds += delta;
                if (_metricsAccumulatorSeconds >= Math.Max(0.1f, DiagnosticsLogEverySeconds))
                {
                    _metricsAccumulatorSeconds = 0d;
                    LogDiagnosticsSnapshot();
                }
            }

            TickDiscoveryPublish(delta);
        }

        private void ConfigureDiscoveryPublishers()
        {
            if (!RegisterWithTrackerOnStartup || string.IsNullOrWhiteSpace(OfficialRegistryBaseUrl))
                return;

            if (!PublicVisible)
            {
                _logger.Warn("Discovery registration is enabled, but PublicVisible is false. The server will not publish to the registry.");
                return;
            }

            _officialRegistryHttpClient?.Dispose();
            _officialRegistryHttpClient = new HttpClient
            {
                BaseAddress = new Uri(OfficialRegistryBaseUrl, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(10),
            };

            DiscoveryPublishMode publishMode = string.IsNullOrWhiteSpace(DiscoveryPublishToken)
                ? DiscoveryPublishMode.Unverified
                : DiscoveryPublishMode.Verified;

            _discoveryPublishers.Add(new OfficialRegistryDiscoveryPublisher(
                _officialRegistryHttpClient,
                publishMode,
                DiscoveryPublishToken,
                ComputeOwnershipTokenHashOrNull));
        }

        private void TickDiscoveryPublish(double deltaSeconds)
        {
            if (_runtime == null || !_runtime.IsRunning || _runtime.DiscoveryPublishers.Count == 0)
                return;

            _discoveryPublishAccumulatorSeconds += deltaSeconds;
            if (_discoveryPublishAccumulatorSeconds < Math.Max(1f, DiscoveryPublishEverySeconds))
                return;

            _discoveryPublishAccumulatorSeconds = 0d;
            try
            {
                _runtime.PublishDiscoverySnapshot();
            }
            catch (Exception ex)
            {
                _logger.Warn($"Discovery publish failed - {ex.Message}");
            }
        }

        private void ApplyCommandLineOverrides(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (TryConsumeStringArg(args, ref i, "--profile", out string? profile))
                {
                    if (!string.IsNullOrWhiteSpace(profile))
                        ProfileName = profile.Trim();
                    continue;
                }

                if (TryConsumeStringArg(args, ref i, "--public-display-name", out string? displayName))
                {
                    if (!string.IsNullOrWhiteSpace(displayName))
                        PublicDisplayName = displayName.Trim();
                    continue;
                }

                if (TryConsumeStringArg(args, ref i, "--bind-host", out string? bindHost))
                {
                    if (!string.IsNullOrWhiteSpace(bindHost))
                        BindHost = bindHost.Trim();
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--port", out int port))
                {
                    Port = Math.Max(1, port);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--udp", out bool udpEnabled))
                {
                    EnableUdp = udpEnabled;
                    continue;
                }

                if (string.Equals(arg, "--udp", StringComparison.OrdinalIgnoreCase))
                {
                    EnableUdp = true;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--udp-port", out int udpPort))
                {
                    UdpPort = Math.Max(1, udpPort);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--tick-ms", out int tickMilliseconds))
                {
                    TickMilliseconds = Math.Max(1, tickMilliseconds);
                    continue;
                }

                if (string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase))
                {
                    EnableDiagnostics = true;
                    continue;
                }

                if (string.Equals(arg, "--metrics-lite", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--server-metrics-lite", StringComparison.OrdinalIgnoreCase))
                {
                    EnableMetricsLite = true;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--decouple-network-pump", out bool decoupleNetworkPump))
                {
                    DecoupleTransportPumpFromSimulation = decoupleNetworkPump;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--max-inbound-datagrams", out int maxInboundDatagrams))
                {
                    MaxInboundDatagramsPerPump = Math.Max(1, maxInboundDatagrams);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--max-catchup-ticks", out int maxCatchUpTicks))
                {
                    MaxCatchUpTicksPerFrame = Math.Max(1, maxCatchUpTicks);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--debug", out bool debugEnabled))
                {
                    EnableDiagnostics = debugEnabled;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--log-connection-events", out bool logConnectionEvents))
                {
                    LogConnectionEvents = logConnectionEvents;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--metrics-lite", out bool metricsLite) ||
                    TryConsumeBoolArg(args, ref i, "--server-metrics-lite", out metricsLite))
                {
                    EnableMetricsLite = metricsLite;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--metrics-log-ms", out int metricsLogMs))
                {
                    DiagnosticsLogEverySeconds = Math.Max(0.1f, metricsLogMs / 1000f);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--public-visible", out bool publicVisible))
                {
                    PublicVisible = publicVisible;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--password-required", out bool passwordRequired))
                {
                    PasswordRequired = passwordRequired;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--whitelist-only", out bool whitelistOnly))
                {
                    WhitelistOnly = whitelistOnly;
                    continue;
                }

                if (TryConsumeStringArg(args, ref i, "--public-connect-hint", out string? connectHint))
                {
                    PublicConnectHint = connectHint?.Trim() ?? string.Empty;
                    continue;
                }

                if (string.Equals(arg, "--regenerate-discovery-ownership-token", StringComparison.OrdinalIgnoreCase))
                {
                    _regenerateDiscoveryOwnershipTokenOnStart = true;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--spawn-spacing-grid", out int spawnSpacing))
                {
                    PlayerSpawnSpacingGrid = Math.Max(0, spawnSpacing);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--spawn-wrap-count", out int spawnWrap))
                {
                    PlayerSpawnWrapCount = Math.Max(0, spawnWrap);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--priority-lod", out bool priorityLod))
                {
                    EnablePlayerPriorityLod = priorityLod;
                    continue;
                }

                if (string.Equals(arg, "--priority-lod", StringComparison.OrdinalIgnoreCase))
                {
                    EnablePlayerPriorityLod = true;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--priority-lod-near-cadence", out int lodNearCadence))
                {
                    PlayerPriorityLodNearCadenceTicks = Math.Max(1, lodNearCadence);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--priority-lod-outer-cadence", out int lodOuterCadence))
                {
                    PlayerPriorityLodOuterCadenceTicks = Math.Max(1, lodOuterCadence);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--priority-lod-ring1-max-chunks", out int lodRing1Max))
                {
                    PlayerPriorityLodRing1MaxChunks = Math.Max(1, lodRing1Max);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--priority-lod-far-cadence", out int lodFarCadence))
                {
                    PlayerPriorityLodFarCadenceTicks = Math.Max(1, lodFarCadence);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--recipient-selector", out bool recipientSelector))
                {
                    EnablePlayerRecipientSelectorFastPath = recipientSelector;
                    continue;
                }

                if (string.Equals(arg, "--recipient-selector", StringComparison.OrdinalIgnoreCase))
                {
                    EnablePlayerRecipientSelectorFastPath = true;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--recipient-selector-chunk-key", out bool recipientSelectorChunkKey))
                {
                    EnablePlayerRecipientSelectorChunkGroupCacheKey = recipientSelectorChunkKey;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--dense-observer-cadence", out bool denseObserverCadence))
                {
                    EnablePlayerDenseObserverCadence = denseObserverCadence;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--dense-observer-cap", out bool denseObserverCap))
                {
                    EnablePlayerDenseObserverRecipientCap = denseObserverCap;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-observer-cap-start", out int denseCapStart))
                {
                    PlayerDenseObserverRecipientCapStartSubscribers = Math.Max(1, denseCapStart);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-observer-cap-max", out int denseCapMax))
                {
                    PlayerDenseObserverRecipientCapMaxRecipients = Math.Max(1, denseCapMax);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-observer-cadence-start", out int denseCadenceStart))
                {
                    PlayerDenseObserverCadenceStartSubscribers = Math.Max(1, denseCadenceStart);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-observer-cadence-max", out int denseCadenceMax))
                {
                    PlayerDenseObserverCadenceMaxTicks = Math.Max(1, denseCadenceMax);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--dual-correction-streams", out bool dualCorrectionStreams))
                {
                    EnablePlayerDualCorrectionStreams = dualCorrectionStreams;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--correction-batching", out bool correctionBatching))
                {
                    EnablePlayerCorrectionBatching = correctionBatching;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--correction-deadband", out bool correctionDeadband))
                {
                    EnablePlayerCorrectionDeadband = correctionDeadband;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--correction-deadband-units", out int correctionDeadbandUnits))
                {
                    PlayerCorrectionDeadbandUnits = Math.Max(0, correctionDeadbandUnits);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--correction-max-silent-ticks", out int correctionMaxSilentTicks))
                {
                    PlayerCorrectionMaxSilentTicks = Math.Max(1, correctionMaxSilentTicks);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--repl-batch-limit", out int replicationBatchLimit))
                {
                    PlayerReplicationBuildBatchPacketLimit = Math.Max(1, replicationBatchLimit);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--repl-build-workers", out int replicationBuildWorkers))
                {
                    MaxReplicationBuildWorkers = replicationBuildWorkers < -1 ? -1 : replicationBuildWorkers;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--observer-stream-cadence", out int observerStreamCadenceTicks))
                {
                    PlayerObserverStreamCadenceTicks = Math.Max(1, observerStreamCadenceTicks);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--observer-spatial-first-build", out bool observerSpatialFirstBuild))
                {
                    EnableObserverSpatialFirstBuild = observerSpatialFirstBuild;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--chat-min-ticks", out int chatMinTicks))
                {
                    ChatMinTicksBetweenMessages = Math.Max(0, chatMinTicks);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--chat-commands", out bool chatCommands))
                {
                    EnableChatCommands = chatCommands;
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--dense-local-chat-cap", out bool denseLocalChatCap))
                {
                    EnableDenseLocalChatRecipientCap = denseLocalChatCap;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-local-chat-cap-start", out int denseLocalChatCapStart))
                {
                    DenseLocalChatCapStartRecipients = Math.Max(1, denseLocalChatCapStart);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-local-chat-cap-max", out int denseLocalChatCapMax))
                {
                    DenseLocalChatCapMaxRecipients = Math.Max(1, denseLocalChatCapMax);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--dense-local-chat-rate-cap", out bool denseLocalChatRateCap))
                {
                    EnableDenseLocalChatRateCap = denseLocalChatRateCap;
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-local-chat-rate-start", out int denseLocalChatRateStart))
                {
                    DenseLocalChatRateCapStartRecipients = Math.Max(1, denseLocalChatRateStart);
                    continue;
                }

                if (TryConsumeIntArg(args, ref i, "--dense-local-chat-rate-ticks", out int denseLocalChatRateTicks))
                {
                    DenseLocalChatRateCapMinTicks = Math.Max(1, denseLocalChatRateTicks);
                    continue;
                }

                if (TryConsumeBoolArg(args, ref i, "--chat-batching", out bool chatBatching))
                {
                    EnableChatBatching = chatBatching;
                    continue;
                }
            }
        }

        private static bool TryConsumeStringArg(string[] args, ref int index, string key, out string? value)
        {
            string arg = args[index];
            if (arg.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
            {
                value = arg.Substring(key.Length + 1);
                return true;
            }

            if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                value = args[++index];
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryConsumeIntArg(string[] args, ref int index, string key, out int value)
        {
            if (TryConsumeStringArg(args, ref index, key, out string? raw) && int.TryParse(raw, out value))
                return true;

            value = 0;
            return false;
        }

        private static bool TryConsumeBoolArg(string[] args, ref int index, string key, out bool value)
        {
            string arg = args[index];
            if (arg.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
            {
                string rawValue = arg.Substring(key.Length + 1);
                if (TryParseBoolFlag(rawValue, out value))
                    return true;
            }

            if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < args.Length && TryParseBoolFlag(args[index + 1], out value))
                {
                    index++;
                    return true;
                }

                value = true;
                return true;
            }

            if (TryConsumeStringArg(args, ref index, key, out string? raw) && TryParseBoolFlag(raw, out value))
                return true;

            value = false;
            return false;
        }

        private static bool TryParseBoolFlag(string? raw, out bool value)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                value = false;
                return false;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    value = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        private void ProcessConnectionEvents(long nowTicks)
        {
            if (_runtime == null || _transport == null)
                return;

            while (true)
            {
                int count = _transport.DrainConnectionEvents(_connectionEvents);
                if (count <= 0)
                    return;

                for (int i = 0; i < count; i++)
                {
                    TcpFramedNetworkTransport.ConnectionEvent evt = _connectionEvents[i];
                    switch (evt.Kind)
                    {
                        case TcpFramedNetworkTransport.ConnectionEventKind.Connected:
                        {
                            string remoteIp = evt.Message;
                            long sessionId = _runtime.Sessions.CreateSession(evt.ConnectionId, nowTicks);

                            if (!_runtime.Accounts.IsIpAllowed(remoteIp, out string rejectionReason))
                            {
                                _logger.Info($"Client: {remoteIp}: Connected");
                                _logger.Warn($"Client: {remoteIp}: IP rejected - {rejectionReason}");

                                _runtime.UiPanels.OpenPanel(sessionId, SharedUiPanelIds.MessageBox, category: UiPanelCategory.Menu,
                                    presentationMode: UiPanelPresentationMode.ExclusiveMenuOnly,
                                    definitionRevision: 1,
                                    flags: UiPanelFlags.BringToFront | UiPanelFlags.PersistOnSessionLoss,
                                    payload: MessageBoxPayloadCodec.Encode(new MessageBoxPayload(
                                        "Connection Refused",
                                        rejectionReason,
                                        new[] { new MessageBoxButtonDefinition(0, "OK", IsDefault: true) })));

                                _runtime.Sessions.TryDisconnectConnection(evt.ConnectionId, SessionDisconnectReason.IpRejected, nowTicks);
                                break;
                            }

                            _runtime.Sessions.TryMarkHandshaking(sessionId, nowTicks);
                            _sessionIps[sessionId] = remoteIp;
                            _logger.Info($"Client: {remoteIp}: Connected");
                            _runtime.Events.PublishSessionAccepted(sessionId, remoteIp);
                            break;
                        }

                        case TcpFramedNetworkTransport.ConnectionEventKind.Disconnected:
                            if (_runtime.Sessions.TryGetSessionIdByConnection(evt.ConnectionId, out long disconnectedSessionId))
                            {
                                _runtime.UiPanels.RemoveSessionPanels(disconnectedSessionId);
                                _runtime.Sessions.TryDisconnectConnection(
                                    evt.ConnectionId,
                                    SessionDisconnectReason.Unknown,
                                    nowTicks,
                                    removeDisconnected: true);
                            }
                            break;

                        case TcpFramedNetworkTransport.ConnectionEventKind.ListenFailed:
                        case TcpFramedNetworkTransport.ConnectionEventKind.ListenerRuntimeError:
                            _logger.Error($"Listener: {evt.Kind} - {evt.Message}");
                            StopRuntime();
                            return;
                    }
                }
            }
        }

        private string? ResolvePublicConnectHint()
        {
            if (!string.IsNullOrWhiteSpace(PublicConnectHint))
                return PublicConnectHint;

            int resolvedPort = Port <= 0 ? 42042 : Port;
            return $"127.0.0.1:{resolvedPort}";
        }

        private string? ResolvePublicStatusEndpointUrl()
        {
            if (!IsDiscoveryStatusEndpointEnabled())
                return null;

            if (!string.IsNullOrWhiteSpace(PublicStatusEndpointUrl))
                return PublicStatusEndpointUrl.Trim();

            int resolvedPort = DiscoveryStatusPort > 0 ? DiscoveryStatusPort : ((Port <= 0 ? 42042 : Port) + 1);
            return $"http://127.0.0.1:{resolvedPort}/discovery/status";
        }

        private void EnsureDiscoveryOwnershipToken()
        {
            try
            {
                _discoveryOwnershipTokenStartupSuffix = null;
                string? token = LoadPersistedDiscoveryOwnershipToken();

                if (_regenerateDiscoveryOwnershipTokenOnStart)
                {
                    token = GenerateDiscoveryOwnershipToken();
                    _regenerateDiscoveryOwnershipTokenOnStart = false;
                    _discoveryOwnershipTokenStartupSuffix = "regen";
                }
                else if (string.IsNullOrWhiteSpace(token))
                {
                    string? manualToken = NormalizeOptional(DiscoveryOwnershipToken);
                    if (!string.IsNullOrWhiteSpace(manualToken))
                    {
                        token = manualToken;
                        _discoveryOwnershipTokenStartupSuffix = "cached";
                    }
                    else if (AutoGenerateDiscoveryOwnershipToken)
                    {
                        token = GenerateDiscoveryOwnershipToken();
                        _discoveryOwnershipTokenStartupSuffix = null;
                    }
                }
                else
                {
                    _discoveryOwnershipTokenStartupSuffix = "cached";
                }

                DiscoveryOwnershipToken = token ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(token))
                    SaveDiscoveryOwnershipToken(token);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Discovery ownership token setup failed - {ex.Message}");
            }
        }

        private string? LoadPersistedDiscoveryOwnershipToken()
        {
            ConfigFile config = new();
            if (config.Load(ResolveDiscoveryOwnershipTokenSettingsPath()) != Error.Ok)
                return null;

            object rawValue = config.GetValue(DiscoverySettingsSection, DiscoveryOwnershipTokenKey, string.Empty);
            return NormalizeOptional(rawValue?.ToString());
        }

        private void SaveDiscoveryOwnershipToken(string token)
        {
            ConfigFile config = new();
            string settingsPath = ResolveDiscoveryOwnershipTokenSettingsPath();
            config.Load(settingsPath);
            config.SetValue(DiscoverySettingsSection, DiscoveryOwnershipTokenKey, token);

            Error saveResult = config.Save(settingsPath);
            if (saveResult != Error.Ok)
                throw new InvalidOperationException($"Config save failed with {saveResult}.");
        }

        private string ResolveDiscoveryOwnershipTokenSettingsPath()
        {
            if (!string.IsNullOrWhiteSpace(DiscoveryOwnershipTokenSettingsPath))
                return DiscoveryOwnershipTokenSettingsPath.Trim();

            return $"user://server_discovery_{SanitizeTokenPathSegment(ProfileName)}.cfg";
        }

        private static string SanitizeTokenPathSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "default";

            StringBuilder builder = new(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char next = value[i];
                if (char.IsLetterOrDigit(next))
                {
                    builder.Append(char.ToLowerInvariant(next));
                }
                else if (next == '-' || next == '_')
                {
                    builder.Append(next);
                }
                else
                {
                    builder.Append('-');
                }
            }

            string sanitized = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
        }

        private static string GenerateDiscoveryOwnershipToken()
        {
            byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            return Convert.ToHexString(bytes);
        }

        private void NotifyDiscoveryOwnershipTokenChanged()
        {
            if (_runtime == null || !_runtime.IsRunning)
                return;

            try
            {
                _runtime.PublishDiscoverySnapshot();
                _runtime.SendPresence(ServerDiscoveryPresenceSignal.Online);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Discovery ownership token change notification failed - {ex.Message}");
            }
        }

        private string? ComputeOwnershipTokenHashOrNull()
        {
            if (string.IsNullOrWhiteSpace(DiscoveryOwnershipToken))
                return null;

            byte[] bytes = Encoding.UTF8.GetBytes(DiscoveryOwnershipToken.Trim());
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private bool IsDiscoveryStatusEndpointEnabled() =>
            EnableDiscoveryStatusEndpoint &&
            RegisterWithTrackerOnStartup &&
            !string.IsNullOrWhiteSpace(OfficialRegistryBaseUrl);

        private void LogDiscoveryStartupSummary()
        {
            if (!IsDiscoveryStatusEndpointEnabled())
            {
                _logger.Info("Discovery: Disabled");
                return;
            }

            _logger.Info("Discovery: Enabled");

            if (!string.IsNullOrWhiteSpace(DiscoveryOwnershipToken))
            {
                string suffix = string.IsNullOrWhiteSpace(_discoveryOwnershipTokenStartupSuffix)
                    ? string.Empty
                    : $" ({_discoveryOwnershipTokenStartupSuffix})";
                _logger.Info($"Discovery: Ownership Token {DiscoveryOwnershipToken}{suffix}");
            }

            int port = DiscoveryStatusPort > 0 ? DiscoveryStatusPort : ((Port <= 0 ? 42042 : Port) + 1);
            _logger.Info($"Discovery: Port {port}");
        }

        private void LogStatusSnapshot()
        {
            if (_runtime == null || !EnableStatusLog)
                return;

            ServerRuntimeStatusSnapshot snapshot = _runtime.GetStatusSnapshot();
            TcpFramedNetworkTransport.TcpUdpStatsSnapshot transportStats = _transport?.SnapshotTcpUdpStats() ?? default;
            ServerRuntime.RuntimeTimingSnapshot runtimeTiming = _runtime.GetTimingSnapshot();
            ServerNetworkControlService.TimingSnapshot networkControlTiming = _runtime.NetworkControl.GetTimingSnapshot();
            ServerPlayerReplicationService.ReplicationTimingSnapshot replicationTiming = _runtime.PlayerReplication.GetTimingSnapshot();
            ReplicationStatsSnapshot replicationStats = _runtime.PlayerReplication.GetReplicationStatsSnapshot();
            ServerNetworkReplicationBridge.BridgeTimingSnapshot bridgeTiming = _runtime.PlayerReplication.GetBridgeTimingSnapshot();
            NetworkReplicationPipeline.BuildTimingSnapshot pipelineTiming = _runtime.PlayerReplication.GetPipelineTimingSnapshot();
            ReplicationReplayWindow.ReplayWindowSnapshot replayWindow = _runtime.PlayerReplication.GetReplayWindowSnapshot();
            double windowSeconds = Math.Max(0.0001d, _statusAccumulatorSeconds <= 0d ? Math.Max(0.1f, StatusLogEverySeconds) : _statusAccumulatorSeconds);
            int frames = Math.Max(1, _windowFrames);
            int ticks = Math.Max(1, _windowTicksProcessed);
            double hostTickAvgMs = _windowTickMsTotal / ticks;
            double hostPumpAvgMs = _windowPumpMsTotal / frames;
            double hostFlushAvgMs = _windowFlushMsTotal / Math.Max(1, _windowTicksProcessed + (DecoupleTransportPumpFromSimulation ? _windowFrames : 0));
            double hostConnAvgMs = _windowConnectionEventMsTotal / frames;
            double inboundRate = _windowInboundDatagrams / windowSeconds;
            double serverFlushAvgMs = transportStats.ServerFlushCalls > 0
                ? (transportStats.ServerFlushElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / transportStats.ServerFlushCalls
                : 0.0;
            double serverFlushMaxMs = transportStats.ServerFlushElapsedTicksMax > 0
                ? transportStats.ServerFlushElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double tcpBatchWriteAvgMs = transportStats.TcpBatchWriteCalls > 0
                ? (transportStats.TcpBatchWriteElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / transportStats.TcpBatchWriteCalls
                : 0.0;
            double tcpBatchWriteMaxMs = transportStats.TcpBatchWriteElapsedTicksMax > 0
                ? transportStats.TcpBatchWriteElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double tcpSingleWriteAvgMs = transportStats.TcpSingleWriteCalls > 0
                ? (transportStats.TcpSingleWriteElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / transportStats.TcpSingleWriteCalls
                : 0.0;
            double tcpSingleWriteMaxMs = transportStats.TcpSingleWriteElapsedTicksMax > 0
                ? transportStats.TcpSingleWriteElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double pongQueueAvgMs = transportStats.PongQueueAgeSamples > 0
                ? (transportStats.PongQueueAgeTicksTotal * 1000.0 / Stopwatch.Frequency) / transportStats.PongQueueAgeSamples
                : 0.0;
            double pongQueueMaxMs = transportStats.PongQueueAgeTicksMax > 0
                ? transportStats.PongQueueAgeTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double pingInboundQueueAvgMs = transportStats.PingInboundQueueAgeSamples > 0
                ? (transportStats.PingInboundQueueAgeTicksTotal * 1000.0 / Stopwatch.Frequency) / transportStats.PingInboundQueueAgeSamples
                : 0.0;
            double pingInboundQueueMaxMs = transportStats.PingInboundQueueAgeTicksMax > 0
                ? transportStats.PingInboundQueueAgeTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double worldTickAvgMs = runtimeTiming.WorldTickCalls > 0
                ? (runtimeTiming.WorldTickElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / runtimeTiming.WorldTickCalls
                : 0.0;
            double worldTickMaxMs = runtimeTiming.WorldTickElapsedTicksMax > 0
                ? runtimeTiming.WorldTickElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double worldTickAllocAvgBytes = runtimeTiming.WorldTickCalls > 0
                ? (double)runtimeTiming.WorldTickAllocatedBytesTotal / runtimeTiming.WorldTickCalls
                : 0.0;
            double replicationAvgMs = runtimeTiming.ReplicationCalls > 0
                ? (runtimeTiming.ReplicationElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / runtimeTiming.ReplicationCalls
                : 0.0;
            double replicationMaxMs = runtimeTiming.ReplicationElapsedTicksMax > 0
                ? runtimeTiming.ReplicationElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double replicationAllocAvgBytes = runtimeTiming.ReplicationCalls > 0
                ? (double)runtimeTiming.ReplicationAllocatedBytesTotal / runtimeTiming.ReplicationCalls
                : 0.0;
            double chatFlushAvgMs = runtimeTiming.ChatFlushCalls > 0
                ? (runtimeTiming.ChatFlushElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / runtimeTiming.ChatFlushCalls
                : 0.0;
            double chatFlushMaxMs = runtimeTiming.ChatFlushElapsedTicksMax > 0
                ? runtimeTiming.ChatFlushElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double chatFlushAllocAvgBytes = runtimeTiming.ChatFlushCalls > 0
                ? (double)runtimeTiming.ChatFlushAllocatedBytesTotal / runtimeTiming.ChatFlushCalls
                : 0.0;
            double pongBuildAvgMs = networkControlTiming.PongBuildCalls > 0
                ? (networkControlTiming.PongBuildElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / networkControlTiming.PongBuildCalls
                : 0.0;
            double pongBuildMaxMs = networkControlTiming.PongBuildElapsedTicksMax > 0
                ? networkControlTiming.PongBuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double pingRxPathAvgMs = networkControlTiming.StampedPingRxSamples > 0
                ? (networkControlTiming.StampedPingRxElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / networkControlTiming.StampedPingRxSamples
                : 0.0;
            double pingRxPathMaxMs = networkControlTiming.StampedPingRxElapsedTicksMax > 0
                ? networkControlTiming.StampedPingRxElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double aoiRefreshAvgMs = replicationTiming.AoiRefreshCalls > 0
                ? (replicationTiming.AoiRefreshElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / replicationTiming.AoiRefreshCalls
                : 0.0;
            double aoiRefreshMaxMs = replicationTiming.AoiRefreshElapsedTicksMax > 0
                ? replicationTiming.AoiRefreshElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double bridgeAvgMs = replicationTiming.BridgeCalls > 0
                ? (replicationTiming.BridgeElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / replicationTiming.BridgeCalls
                : 0.0;
            double bridgeMaxMs = replicationTiming.BridgeElapsedTicksMax > 0
                ? replicationTiming.BridgeElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double publishAvgMs = bridgeTiming.PublishCalls > 0
                ? (bridgeTiming.PublishElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / bridgeTiming.PublishCalls
                : 0.0;
            double publishMaxMs = bridgeTiming.PublishElapsedTicksMax > 0
                ? bridgeTiming.PublishElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double buildAvgMs = bridgeTiming.BuildCalls > 0
                ? (bridgeTiming.BuildElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / bridgeTiming.BuildCalls
                : 0.0;
            double buildMaxMs = bridgeTiming.BuildElapsedTicksMax > 0
                ? bridgeTiming.BuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double parallelBuildAvgMs = bridgeTiming.ParallelBuildCalls > 0
                ? (bridgeTiming.ParallelBuildElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / bridgeTiming.ParallelBuildCalls
                : 0.0;
            double parallelBuildMaxMs = bridgeTiming.ParallelBuildElapsedTicksMax > 0
                ? bridgeTiming.ParallelBuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double buildAllocAvgBytes = bridgeTiming.BuildCalls > 0
                ? (double)bridgeTiming.BuildAllocatedBytesTotal / bridgeTiming.BuildCalls
                : 0.0;
            double replayVisitAvgMs = pipelineTiming.ReplayVisitCalls > 0
                ? (pipelineTiming.ReplayVisitElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / pipelineTiming.ReplayVisitCalls
                : 0.0;
            double replayVisitMaxMs = pipelineTiming.ReplayVisitElapsedTicksMax > 0
                ? pipelineTiming.ReplayVisitElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double coalesceCommitAvgMs = pipelineTiming.CoalesceCommitCalls > 0
                ? (pipelineTiming.CoalesceCommitElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / pipelineTiming.CoalesceCommitCalls
                : 0.0;
            double coalesceCommitMaxMs = pipelineTiming.CoalesceCommitElapsedTicksMax > 0
                ? pipelineTiming.CoalesceCommitElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double deferredFlushAvgMs = pipelineTiming.DeferredFlushCalls > 0
                ? (pipelineTiming.DeferredFlushElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / pipelineTiming.DeferredFlushCalls
                : 0.0;
            double deferredFlushMaxMs = pipelineTiming.DeferredFlushElapsedTicksMax > 0
                ? pipelineTiming.DeferredFlushElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double recipientSelectionAvgMs = pipelineTiming.RecipientSelectionCalls > 0
                ? (pipelineTiming.RecipientSelectionElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / pipelineTiming.RecipientSelectionCalls
                : 0.0;
            double recipientSelectionMaxMs = pipelineTiming.RecipientSelectionElapsedTicksMax > 0
                ? pipelineTiming.RecipientSelectionElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double selectedDispatchAvgMs = pipelineTiming.SelectedDispatchCalls > 0
                ? (pipelineTiming.SelectedDispatchElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / pipelineTiming.SelectedDispatchCalls
                : 0.0;
            double selectedDispatchMaxMs = pipelineTiming.SelectedDispatchElapsedTicksMax > 0
                ? pipelineTiming.SelectedDispatchElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double trySendAvgMs = pipelineTiming.TrySendCalls > 0
                ? (pipelineTiming.TrySendElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / pipelineTiming.TrySendCalls
                : 0.0;
            double trySendMaxMs = pipelineTiming.TrySendElapsedTicksMax > 0
                ? pipelineTiming.TrySendElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            long ownerTransformAllocAvgBytes = replicationTiming.OwnerTransformCalls > 0
                ? replicationTiming.OwnerTransformAllocatedBytesTotal / replicationTiming.OwnerTransformCalls
                : 0;
            long observerTransformAllocAvgBytes = replicationTiming.ObserverTransformCalls > 0
                ? replicationTiming.ObserverTransformAllocatedBytesTotal / replicationTiming.ObserverTransformCalls
                : 0;
            long batchFinalizeAllocAvgBytes = replicationTiming.BatchFinalizeCalls > 0
                ? replicationTiming.BatchFinalizeAllocatedBytesTotal / replicationTiming.BatchFinalizeCalls
                : 0;
            double observerChunkBuildAvgMs = replicationTiming.ObserverChunkBuildCalls > 0
                ? (replicationTiming.ObserverChunkBuildElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / replicationTiming.ObserverChunkBuildCalls
                : 0.0;
            double observerChunkBuildMaxMs = replicationTiming.ObserverChunkBuildElapsedTicksMax > 0
                ? replicationTiming.ObserverChunkBuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            long statusGc0Delta = GC.CollectionCount(0) - _gc0Baseline;
            long statusGc1Delta = GC.CollectionCount(1) - _gc1Baseline;
            long statusGc2Delta = GC.CollectionCount(2) - _gc2Baseline;
            _logger.Info(
                $"[ServerRuntime] state={snapshot.State} sessions={snapshot.CurrentSessions} players={_runtime.PlayerWorld.PlayerCount} peak={snapshot.PeakSessions} " +
                $"uptime={snapshot.Uptime:hh\\:mm\\:ss} accepting={snapshot.IsAcceptingConnections} " +
                $"ping_rx={_runtime.NetworkControl.PingProbeRxCount} pong_tx={_runtime.NetworkControl.PongProbeTxCount} " +
                $"chat_rx={_runtime.Chat.ReceivedMessageCount} chat_tx={_runtime.Chat.SentMessageCount} " +
                $"repl_pub={_runtime.PlayerReplication.LastPublishedCandidates} repl_send={_runtime.PlayerReplication.LastSentPackets} " +
                $"udp_assoc={transportStats.UdpAssociatedPeers} udp_tx_dg={transportStats.UdpTxDatagrams} udp_rx_dg={transportStats.UdpRxDatagrams} " +
                $"udp_drop_ep={transportStats.UdpDropsUnknownEndpoint} udp_drop_tok={transportStats.UdpDropsUnknownToken} udp_tx_fail={transportStats.UdpTxFailures} " +
                $"tick_avg_ms={hostTickAvgMs:0.000} tick_max_ms={_windowTickMsMax:0.000} " +
                $"pump_avg_ms={hostPumpAvgMs:0.000} pump_max_ms={_windowPumpMsMax:0.000} " +
                $"host_flush_avg_ms={hostFlushAvgMs:0.000} host_flush_max_ms={_windowFlushMsMax:0.000} " +
                $"conn_avg_ms={hostConnAvgMs:0.000} conn_max_ms={_windowConnectionEventMsMax:0.000} inbound_dg_s={inboundRate:0.0} " +
                $"repl_attempts={replicationStats.SendAttempts} repl_bytes={replicationStats.BytesSent} " +
                $"repl_dirty={replicationStats.DirtyDrained} repl_enc={replicationStats.DeltasEncoded} repl_interest={replicationStats.InterestedSelected} " +
                $"repl_if_calls={replicationStats.InterestFilterCalls} repl_if_lookups={replicationStats.InterestCacheLookups} " +
                $"repl_if_hits={replicationStats.InterestCacheHits} repl_if_miss={replicationStats.InterestCacheMisses} " +
                $"repl_fanout_hit={replicationStats.FanoutReuseHits} repl_fanout_miss={replicationStats.FanoutReuseMisses} " +
                $"repl_def_state={replicationStats.GetDeferredByLane(ReplicationLane.State)} repl_drop_state={replicationStats.GetDroppedByLane(ReplicationLane.State)} " +
                $"repl_def_crit={replicationStats.GetDeferredByLane(ReplicationLane.Critical)} repl_drop_crit={replicationStats.GetDroppedByLane(ReplicationLane.Critical)} " +
                $"gc0_d={statusGc0Delta} gc1_d={statusGc1Delta} gc2_d={statusGc2Delta} " +
                $"build_gc0={bridgeTiming.BuildGc0Collections} build_gc1={bridgeTiming.BuildGc1Collections} build_gc2={bridgeTiming.BuildGc2Collections} " +
                $"owner_rents={replicationTiming.OwnerPayloadRents} owner_rent_bytes={replicationTiming.OwnerPayloadRentBytes} " +
                $"observer_miss={replicationTiming.ObserverPayloadMisses} observer_rent_bytes={replicationTiming.ObserverPayloadRentBytes} " +
                $"observer_pub={replicationTiming.ObserverCandidatesPublished} observer_skip={replicationTiming.ObserverCandidatesCadenceSkipped} observer_cadence={_runtime.Options.PlayerObserverStreamCadenceTicks} observer_spatial_first={_runtime.Options.EnableObserverSpatialFirstBuild} " +
                $"obs_chunk_pub={replicationTiming.ObserverChunkPublishedDeltas} obs_chunk_bytes={replicationTiming.ObserverChunkPublishedBytes} obs_chunk_windows={replicationTiming.ObserverChunkActiveWindows} obs_chunk_deltas={replicationTiming.ObserverChunkActiveDeltas} " +
                $"obs_chunk_sent={replicationTiming.ObserverChunkSentPackets} obs_chunk_conn={replicationTiming.ObserverChunkBuiltConnections} obs_chunk_visit={replicationTiming.ObserverChunkVisitedDeltas} obs_chunk_repl={replicationTiming.ObserverChunkCoalescedReplaced} obs_chunk_drop={replicationTiming.ObserverChunkBudgetDropped} " +
                $"batch_rents={replicationTiming.BatchPayloadRents} batch_rent_bytes={replicationTiming.BatchPayloadRentBytes} " +
                $"owner_alloc_avg_b={ownerTransformAllocAvgBytes} owner_alloc_max_b={replicationTiming.OwnerTransformAllocatedBytesMax} owner_alloc_total_b={replicationTiming.OwnerTransformAllocatedBytesTotal} " +
                $"observer_alloc_avg_b={observerTransformAllocAvgBytes} observer_alloc_max_b={replicationTiming.ObserverTransformAllocatedBytesMax} observer_alloc_total_b={replicationTiming.ObserverTransformAllocatedBytesTotal} " +
                $"batch_finalize_calls={replicationTiming.BatchFinalizeCalls} batch_finalize_alloc_avg_b={batchFinalizeAllocAvgBytes} batch_finalize_alloc_max_b={replicationTiming.BatchFinalizeAllocatedBytesMax} batch_finalize_alloc_total_b={replicationTiming.BatchFinalizeAllocatedBytesTotal} " +
                $"obs_chunk_pub_avg_ms={(replicationTiming.ObserverChunkPublishCalls > 0 ? (replicationTiming.ObserverChunkPublishElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / replicationTiming.ObserverChunkPublishCalls : 0.0):0.000} obs_chunk_pub_max_ms={(replicationTiming.ObserverChunkPublishElapsedTicksMax > 0 ? replicationTiming.ObserverChunkPublishElapsedTicksMax * 1000.0 / Stopwatch.Frequency : 0.0):0.000} " +
                $"obs_chunk_build_avg_ms={observerChunkBuildAvgMs:0.000} obs_chunk_build_max_ms={observerChunkBuildMaxMs:0.000} " +
                $"obs_chunk_max_deltas={replicationTiming.ObserverChunkMaxWindowDeltas} obs_chunk_max_bytes={replicationTiming.ObserverChunkMaxWindowBytes} " +
                $"replay_buckets={replayWindow.OccupiedBucketCount} replay_deltas={replayWindow.TotalDeltaCount} replay_bytes={replayWindow.TotalUsedBytes} " +
                $"replay_bucket_max_deltas={replayWindow.MaxBucketDeltaCount} replay_bucket_max_bytes={replayWindow.MaxBucketUsedBytes} " +
                $"world_avg_ms={worldTickAvgMs:0.000} world_max_ms={worldTickMaxMs:0.000} world_alloc_avg_b={worldTickAllocAvgBytes:0} world_alloc_max_b={runtimeTiming.WorldTickAllocatedBytesMax} " +
                $"repl_avg_ms={replicationAvgMs:0.000} repl_max_ms={replicationMaxMs:0.000} repl_alloc_avg_b={replicationAllocAvgBytes:0} repl_alloc_max_b={runtimeTiming.ReplicationAllocatedBytesMax} " +
                $"pong_build_avg_ms={pongBuildAvgMs:0.000} pong_build_max_ms={pongBuildMaxMs:0.000} " +
                $"ping_rx_path_avg_ms={pingRxPathAvgMs:0.000} ping_rx_path_max_ms={pingRxPathMaxMs:0.000} " +
                $"aoi_avg_ms={aoiRefreshAvgMs:0.000} aoi_max_ms={aoiRefreshMaxMs:0.000} " +
                $"bridge_avg_ms={bridgeAvgMs:0.000} bridge_max_ms={bridgeMaxMs:0.000} " +
                $"publish_avg_ms={publishAvgMs:0.000} publish_max_ms={publishMaxMs:0.000} " +
                $"repl_build_workers={bridgeTiming.BuildWorkerCount} parallel_build_avg_ms={parallelBuildAvgMs:0.000} parallel_build_max_ms={parallelBuildMaxMs:0.000} " +
                $"build_avg_ms={buildAvgMs:0.000} build_max_ms={buildMaxMs:0.000} build_alloc_avg_b={buildAllocAvgBytes:0} build_alloc_max_b={bridgeTiming.BuildAllocatedBytesMax} " +
                $"replay_avg_ms={replayVisitAvgMs:0.000} replay_max_ms={replayVisitMaxMs:0.000} " +
                $"sel_avg_ms={recipientSelectionAvgMs:0.000} sel_max_ms={recipientSelectionMaxMs:0.000} " +
                $"dispatch_avg_ms={selectedDispatchAvgMs:0.000} dispatch_max_ms={selectedDispatchMaxMs:0.000} " +
                $"send_avg_ms={trySendAvgMs:0.000} send_max_ms={trySendMaxMs:0.000} " +
                $"coalesce_avg_ms={coalesceCommitAvgMs:0.000} coalesce_max_ms={coalesceCommitMaxMs:0.000} " +
                $"defer_flush_avg_ms={deferredFlushAvgMs:0.000} defer_flush_max_ms={deferredFlushMaxMs:0.000} " +
                $"chat_avg_ms={chatFlushAvgMs:0.000} chat_max_ms={chatFlushMaxMs:0.000} chat_alloc_avg_b={chatFlushAllocAvgBytes:0} chat_alloc_max_b={runtimeTiming.ChatFlushAllocatedBytesMax} " +
                $"srv_flush_avg_ms={serverFlushAvgMs:0.000} srv_flush_max_ms={serverFlushMaxMs:0.000} " +
                $"tcp_batch_w_avg_ms={tcpBatchWriteAvgMs:0.000} tcp_batch_w_max_ms={tcpBatchWriteMaxMs:0.000} tcp_batch_w_calls={transportStats.TcpBatchWriteCalls} " +
                $"tcp_single_w_avg_ms={tcpSingleWriteAvgMs:0.000} tcp_single_w_max_ms={tcpSingleWriteMaxMs:0.000} tcp_single_w_calls={transportStats.TcpSingleWriteCalls} " +
                $"pong_q_avg_ms={pongQueueAvgMs:0.000} pong_q_max_ms={pongQueueMaxMs:0.000} " +
                $"ping_in_q_avg_ms={pingInboundQueueAvgMs:0.000} ping_in_q_max_ms={pingInboundQueueMaxMs:0.000} " +
                $"inbound_q_depth={transportStats.InboundQueueDepth} inbound_q_max={transportStats.InboundQueueDepthMax} " +
                $"udp_fb_tcp={transportStats.ServerUdpFallbackTcpBatches} udp_fb_noroute={transportStats.ServerUdpFallbackNoRouteBatches} udp_fb_sendfail={transportStats.ServerUdpFallbackSendFailBatches} " +
                $"borrowed={transportStats.BorrowedEnqueueSuccesses}/{transportStats.BorrowedEnqueueAttempts} " +
                $"borrowed_patch={transportStats.BorrowedPatchedEnqueueSuccesses}/{transportStats.BorrowedPatchedEnqueueAttempts} " +
                $"borrowed_multi={transportStats.BorrowedMulticastEnqueueSuccesses}/{transportStats.BorrowedMulticastEnqueueAttempts} " +
                $"copy_calls={transportStats.OutboundCopyCalls} copy_bytes={transportStats.OutboundCopyBytes} outbound_item_miss={transportStats.OutboundItemPoolMisses}");

            _gc0Baseline = GC.CollectionCount(0);
            _gc1Baseline = GC.CollectionCount(1);
            _gc2Baseline = GC.CollectionCount(2);
        }

        private void LogDiagnosticsSnapshot()
        {
            if (_runtime == null)
                return;

            double windowSeconds = Math.Max(0.0001d, _metricsAccumulatorSeconds <= 0d ? Math.Max(0.1d, DiagnosticsLogEverySeconds) : _metricsAccumulatorSeconds);
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long memoryBytes = GC.GetTotalMemory(forceFullCollection: false);
            TcpFramedNetworkTransport.TcpUdpStatsSnapshot transportStats = _transport?.SnapshotTcpUdpStats() ?? default;

            int frames = Math.Max(1, _windowFrames);
            int ticks = Math.Max(1, _windowTicksProcessed);
            double tickAvgMs = _windowTickMsTotal / ticks;
            double pumpAvgMs = _windowPumpMsTotal / frames;
            double flushAvgMs = _windowFlushMsTotal / Math.Max(1, _windowTicksProcessed + (DecoupleTransportPumpFromSimulation ? _windowFrames : 0));
            double connAvgMs = _windowConnectionEventMsTotal / frames;
            double inboundRate = _windowInboundDatagrams / windowSeconds;
            long pingRx = _runtime.NetworkControl.PingProbeRxCount;
            long pongTx = _runtime.NetworkControl.PongProbeTxCount;
            ServerRuntime.RuntimeTimingSnapshot runtimeTiming = _runtime.GetTimingSnapshot();
            ServerNetworkControlService.TimingSnapshot networkControlTiming = _runtime.NetworkControl.GetTimingSnapshot();
            ServerPlayerReplicationService.ReplicationTimingSnapshot replicationTiming = _runtime.PlayerReplication.GetTimingSnapshot();
            ReplicationStatsSnapshot replicationStats = _runtime.PlayerReplication.GetReplicationStatsSnapshot();
            ServerNetworkReplicationBridge.BridgeTimingSnapshot bridgeTiming = _runtime.PlayerReplication.GetBridgeTimingSnapshot();
            NetworkReplicationPipeline.BuildTimingSnapshot pipelineTiming = _runtime.PlayerReplication.GetPipelineTimingSnapshot();
            ReplicationReplayWindow.ReplayWindowSnapshot replayWindow = _runtime.PlayerReplication.GetReplayWindowSnapshot();
            long pingRxDelta = pingRx - _pingRxBaseline;
            long pongTxDelta = pongTx - _pongTxBaseline;
            long udpTxDatagramsDelta = transportStats.UdpTxDatagrams - _transportStatsBaseline.UdpTxDatagrams;
            long udpRxDatagramsDelta = transportStats.UdpRxDatagrams - _transportStatsBaseline.UdpRxDatagrams;
            long udpTxFramesDelta = transportStats.UdpTxFrames - _transportStatsBaseline.UdpTxFrames;
            long udpDropsEndpointDelta = transportStats.UdpDropsUnknownEndpoint - _transportStatsBaseline.UdpDropsUnknownEndpoint;
            long udpDropsTokenDelta = transportStats.UdpDropsUnknownToken - _transportStatsBaseline.UdpDropsUnknownToken;
            long udpTxFailuresDelta = transportStats.UdpTxFailures - _transportStatsBaseline.UdpTxFailures;
            long serverFlushCallsDelta = transportStats.ServerFlushCalls - _transportStatsBaseline.ServerFlushCalls;
            long serverFlushElapsedTicksDelta = transportStats.ServerFlushElapsedTicksTotal - _transportStatsBaseline.ServerFlushElapsedTicksTotal;
            long tcpBatchWriteCallsDelta = transportStats.TcpBatchWriteCalls - _transportStatsBaseline.TcpBatchWriteCalls;
            long tcpBatchWriteElapsedTicksDelta = transportStats.TcpBatchWriteElapsedTicksTotal - _transportStatsBaseline.TcpBatchWriteElapsedTicksTotal;
            long tcpSingleWriteCallsDelta = transportStats.TcpSingleWriteCalls - _transportStatsBaseline.TcpSingleWriteCalls;
            long tcpSingleWriteElapsedTicksDelta = transportStats.TcpSingleWriteElapsedTicksTotal - _transportStatsBaseline.TcpSingleWriteElapsedTicksTotal;
            long serverUdpFallbackTcpDelta = transportStats.ServerUdpFallbackTcpBatches - _transportStatsBaseline.ServerUdpFallbackTcpBatches;
            long serverUdpFallbackNoRouteDelta = transportStats.ServerUdpFallbackNoRouteBatches - _transportStatsBaseline.ServerUdpFallbackNoRouteBatches;
            long serverUdpFallbackSendFailDelta = transportStats.ServerUdpFallbackSendFailBatches - _transportStatsBaseline.ServerUdpFallbackSendFailBatches;
            long borrowedEnqueueAttemptsDelta = transportStats.BorrowedEnqueueAttempts - _transportStatsBaseline.BorrowedEnqueueAttempts;
            long borrowedEnqueueSuccessesDelta = transportStats.BorrowedEnqueueSuccesses - _transportStatsBaseline.BorrowedEnqueueSuccesses;
            long borrowedPatchedAttemptsDelta = transportStats.BorrowedPatchedEnqueueAttempts - _transportStatsBaseline.BorrowedPatchedEnqueueAttempts;
            long borrowedPatchedSuccessesDelta = transportStats.BorrowedPatchedEnqueueSuccesses - _transportStatsBaseline.BorrowedPatchedEnqueueSuccesses;
            long borrowedMulticastAttemptsDelta = transportStats.BorrowedMulticastEnqueueAttempts - _transportStatsBaseline.BorrowedMulticastEnqueueAttempts;
            long borrowedMulticastSuccessesDelta = transportStats.BorrowedMulticastEnqueueSuccesses - _transportStatsBaseline.BorrowedMulticastEnqueueSuccesses;
            long outboundCopyCallsDelta = transportStats.OutboundCopyCalls - _transportStatsBaseline.OutboundCopyCalls;
            long outboundCopyBytesDelta = transportStats.OutboundCopyBytes - _transportStatsBaseline.OutboundCopyBytes;
            long outboundItemPoolMissesDelta = transportStats.OutboundItemPoolMisses - _transportStatsBaseline.OutboundItemPoolMisses;
            long pongQueueAgeSamplesDelta = transportStats.PongQueueAgeSamples - _transportStatsBaseline.PongQueueAgeSamples;
            long pongQueueAgeTicksDelta = transportStats.PongQueueAgeTicksTotal - _transportStatsBaseline.PongQueueAgeTicksTotal;
            long pingInboundQueueAgeSamplesDelta = transportStats.PingInboundQueueAgeSamples - _transportStatsBaseline.PingInboundQueueAgeSamples;
            long pingInboundQueueAgeTicksDelta = transportStats.PingInboundQueueAgeTicksTotal - _transportStatsBaseline.PingInboundQueueAgeTicksTotal;
            long worldTickCallsDelta = runtimeTiming.WorldTickCalls - _runtimeTimingBaseline.WorldTickCalls;
            long worldTickElapsedTicksDelta = runtimeTiming.WorldTickElapsedTicksTotal - _runtimeTimingBaseline.WorldTickElapsedTicksTotal;
            long pongBuildCallsDelta = networkControlTiming.PongBuildCalls - _networkControlTimingBaseline.PongBuildCalls;
            long pongBuildElapsedTicksDelta = networkControlTiming.PongBuildElapsedTicksTotal - _networkControlTimingBaseline.PongBuildElapsedTicksTotal;
            long replicationCallsDelta = runtimeTiming.ReplicationCalls - _runtimeTimingBaseline.ReplicationCalls;
            long replicationElapsedTicksDelta = runtimeTiming.ReplicationElapsedTicksTotal - _runtimeTimingBaseline.ReplicationElapsedTicksTotal;
            long chatFlushCallsDelta = runtimeTiming.ChatFlushCalls - _runtimeTimingBaseline.ChatFlushCalls;
            long chatFlushElapsedTicksDelta = runtimeTiming.ChatFlushElapsedTicksTotal - _runtimeTimingBaseline.ChatFlushElapsedTicksTotal;
            long aoiRefreshCallsDelta = replicationTiming.AoiRefreshCalls - _replicationTimingBaseline.AoiRefreshCalls;
            long aoiRefreshElapsedTicksDelta = replicationTiming.AoiRefreshElapsedTicksTotal - _replicationTimingBaseline.AoiRefreshElapsedTicksTotal;
            long bridgeCallsDelta = replicationTiming.BridgeCalls - _replicationTimingBaseline.BridgeCalls;
            long bridgeElapsedTicksDelta = replicationTiming.BridgeElapsedTicksTotal - _replicationTimingBaseline.BridgeElapsedTicksTotal;
            long replSendAttemptsDelta = replicationStats.SendAttempts - _replicationStatsBaseline.SendAttempts;
            long replBytesSentDelta = replicationStats.BytesSent - _replicationStatsBaseline.BytesSent;
            long replDirtyDrainedDelta = replicationStats.DirtyDrained - _replicationStatsBaseline.DirtyDrained;
            long replDeltasEncodedDelta = replicationStats.DeltasEncoded - _replicationStatsBaseline.DeltasEncoded;
            long replInterestedSelectedDelta = replicationStats.InterestedSelected - _replicationStatsBaseline.InterestedSelected;
            long replInterestFilterCallsDelta = replicationStats.InterestFilterCalls - _replicationStatsBaseline.InterestFilterCalls;
            long replInterestCacheLookupsDelta = replicationStats.InterestCacheLookups - _replicationStatsBaseline.InterestCacheLookups;
            long replInterestCacheHitsDelta = replicationStats.InterestCacheHits - _replicationStatsBaseline.InterestCacheHits;
            long replInterestCacheMissesDelta = replicationStats.InterestCacheMisses - _replicationStatsBaseline.InterestCacheMisses;
            long replFanoutHitDelta = replicationStats.FanoutReuseHits - _replicationStatsBaseline.FanoutReuseHits;
            long replFanoutMissDelta = replicationStats.FanoutReuseMisses - _replicationStatsBaseline.FanoutReuseMisses;
            long replDeferredStateDelta = replicationStats.GetDeferredByLane(ReplicationLane.State) - _replicationStatsBaseline.GetDeferredByLane(ReplicationLane.State);
            long replDroppedStateDelta = replicationStats.GetDroppedByLane(ReplicationLane.State) - _replicationStatsBaseline.GetDroppedByLane(ReplicationLane.State);
            long replDeferredCriticalDelta = replicationStats.GetDeferredByLane(ReplicationLane.Critical) - _replicationStatsBaseline.GetDeferredByLane(ReplicationLane.Critical);
            long replDroppedCriticalDelta = replicationStats.GetDroppedByLane(ReplicationLane.Critical) - _replicationStatsBaseline.GetDroppedByLane(ReplicationLane.Critical);
            long bridgePublishCallsDelta = bridgeTiming.PublishCalls - _bridgeTimingBaseline.PublishCalls;
            long bridgePublishElapsedTicksDelta = bridgeTiming.PublishElapsedTicksTotal - _bridgeTimingBaseline.PublishElapsedTicksTotal;
            long bridgeBuildCallsDelta = bridgeTiming.BuildCalls - _bridgeTimingBaseline.BuildCalls;
            long bridgeBuildElapsedTicksDelta = bridgeTiming.BuildElapsedTicksTotal - _bridgeTimingBaseline.BuildElapsedTicksTotal;
            long bridgeBuildAllocatedBytesDelta = bridgeTiming.BuildAllocatedBytesTotal - _bridgeTimingBaseline.BuildAllocatedBytesTotal;
            long bridgeBuildGc0Delta = bridgeTiming.BuildGc0Collections - _bridgeTimingBaseline.BuildGc0Collections;
            long bridgeBuildGc1Delta = bridgeTiming.BuildGc1Collections - _bridgeTimingBaseline.BuildGc1Collections;
            long bridgeBuildGc2Delta = bridgeTiming.BuildGc2Collections - _bridgeTimingBaseline.BuildGc2Collections;
            long pipelineReplayCallsDelta = pipelineTiming.ReplayVisitCalls - _pipelineTimingBaseline.ReplayVisitCalls;
            long pipelineReplayElapsedTicksDelta = pipelineTiming.ReplayVisitElapsedTicksTotal - _pipelineTimingBaseline.ReplayVisitElapsedTicksTotal;
            long pipelineCoalesceCallsDelta = pipelineTiming.CoalesceCommitCalls - _pipelineTimingBaseline.CoalesceCommitCalls;
            long pipelineCoalesceElapsedTicksDelta = pipelineTiming.CoalesceCommitElapsedTicksTotal - _pipelineTimingBaseline.CoalesceCommitElapsedTicksTotal;
            long pipelineDeferredFlushCallsDelta = pipelineTiming.DeferredFlushCalls - _pipelineTimingBaseline.DeferredFlushCalls;
            long pipelineDeferredFlushElapsedTicksDelta = pipelineTiming.DeferredFlushElapsedTicksTotal - _pipelineTimingBaseline.DeferredFlushElapsedTicksTotal;
            long pipelineRecipientSelectionCallsDelta = pipelineTiming.RecipientSelectionCalls - _pipelineTimingBaseline.RecipientSelectionCalls;
            long pipelineRecipientSelectionElapsedTicksDelta = pipelineTiming.RecipientSelectionElapsedTicksTotal - _pipelineTimingBaseline.RecipientSelectionElapsedTicksTotal;
            long pipelineSelectedDispatchCallsDelta = pipelineTiming.SelectedDispatchCalls - _pipelineTimingBaseline.SelectedDispatchCalls;
            long pipelineSelectedDispatchElapsedTicksDelta = pipelineTiming.SelectedDispatchElapsedTicksTotal - _pipelineTimingBaseline.SelectedDispatchElapsedTicksTotal;
            long pipelineTrySendCallsDelta = pipelineTiming.TrySendCalls - _pipelineTimingBaseline.TrySendCalls;
            long pipelineTrySendElapsedTicksDelta = pipelineTiming.TrySendElapsedTicksTotal - _pipelineTimingBaseline.TrySendElapsedTicksTotal;
            int replayBucketDelta = replayWindow.OccupiedBucketCount - _replayWindowBaseline.OccupiedBucketCount;
            int replayDeltaCountDelta = replayWindow.TotalDeltaCount - _replayWindowBaseline.TotalDeltaCount;
            int replayBytesDelta = replayWindow.TotalUsedBytes - _replayWindowBaseline.TotalUsedBytes;
            double serverFlushAvgMs = serverFlushCallsDelta > 0
                ? ((serverFlushElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / serverFlushCallsDelta
                : 0.0;
            double serverFlushMaxMs = transportStats.ServerFlushElapsedTicksMax > 0
                ? transportStats.ServerFlushElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double tcpBatchWriteAvgMs = tcpBatchWriteCallsDelta > 0
                ? ((tcpBatchWriteElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / tcpBatchWriteCallsDelta
                : 0.0;
            double tcpBatchWriteMaxMs = transportStats.TcpBatchWriteElapsedTicksMax > 0
                ? transportStats.TcpBatchWriteElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double tcpSingleWriteAvgMs = tcpSingleWriteCallsDelta > 0
                ? ((tcpSingleWriteElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / tcpSingleWriteCallsDelta
                : 0.0;
            double tcpSingleWriteMaxMs = transportStats.TcpSingleWriteElapsedTicksMax > 0
                ? transportStats.TcpSingleWriteElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double pongQueueAvgMs = pongQueueAgeSamplesDelta > 0
                ? ((pongQueueAgeTicksDelta * 1000.0) / Stopwatch.Frequency) / pongQueueAgeSamplesDelta
                : 0.0;
            double pongQueueMaxMs = transportStats.PongQueueAgeTicksMax > 0
                ? transportStats.PongQueueAgeTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double pingInboundQueueAvgMs = pingInboundQueueAgeSamplesDelta > 0
                ? ((pingInboundQueueAgeTicksDelta * 1000.0) / Stopwatch.Frequency) / pingInboundQueueAgeSamplesDelta
                : 0.0;
            double pingInboundQueueMaxMs = transportStats.PingInboundQueueAgeTicksMax > 0
                ? transportStats.PingInboundQueueAgeTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double worldTickAvgMs = worldTickCallsDelta > 0
                ? ((worldTickElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / worldTickCallsDelta
                : 0.0;
            double worldTickMaxMs = runtimeTiming.WorldTickElapsedTicksMax > 0
                ? runtimeTiming.WorldTickElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double worldTickAllocAvgBytes = worldTickCallsDelta > 0
                ? (double)(runtimeTiming.WorldTickAllocatedBytesTotal - _runtimeTimingBaseline.WorldTickAllocatedBytesTotal) / worldTickCallsDelta
                : 0.0;
            double replicationAvgMs = replicationCallsDelta > 0
                ? ((replicationElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / replicationCallsDelta
                : 0.0;
            double replicationMaxMs = runtimeTiming.ReplicationElapsedTicksMax > 0
                ? runtimeTiming.ReplicationElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double replicationAllocAvgBytes = replicationCallsDelta > 0
                ? (double)(runtimeTiming.ReplicationAllocatedBytesTotal - _runtimeTimingBaseline.ReplicationAllocatedBytesTotal) / replicationCallsDelta
                : 0.0;
            double chatFlushAvgMs = chatFlushCallsDelta > 0
                ? ((chatFlushElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / chatFlushCallsDelta
                : 0.0;
            double chatFlushMaxMs = runtimeTiming.ChatFlushElapsedTicksMax > 0
                ? runtimeTiming.ChatFlushElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double chatFlushAllocAvgBytes = chatFlushCallsDelta > 0
                ? (double)(runtimeTiming.ChatFlushAllocatedBytesTotal - _runtimeTimingBaseline.ChatFlushAllocatedBytesTotal) / chatFlushCallsDelta
                : 0.0;
            double pongBuildAvgMs = pongBuildCallsDelta > 0
                ? ((pongBuildElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pongBuildCallsDelta
                : 0.0;
            double pongBuildMaxMs = networkControlTiming.PongBuildElapsedTicksMax > 0
                ? networkControlTiming.PongBuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double aoiRefreshAvgMs = aoiRefreshCallsDelta > 0
                ? ((aoiRefreshElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / aoiRefreshCallsDelta
                : 0.0;
            double aoiRefreshMaxMs = replicationTiming.AoiRefreshElapsedTicksMax > 0
                ? replicationTiming.AoiRefreshElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double bridgeAvgMs = bridgeCallsDelta > 0
                ? ((bridgeElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / bridgeCallsDelta
                : 0.0;
            double bridgeMaxMs = replicationTiming.BridgeElapsedTicksMax > 0
                ? replicationTiming.BridgeElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double publishAvgMs = bridgePublishCallsDelta > 0
                ? ((bridgePublishElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / bridgePublishCallsDelta
                : 0.0;
            double publishMaxMs = bridgeTiming.PublishElapsedTicksMax > 0
                ? bridgeTiming.PublishElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double buildAvgMs = bridgeBuildCallsDelta > 0
                ? ((bridgeBuildElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / bridgeBuildCallsDelta
                : 0.0;
            double buildMaxMs = bridgeTiming.BuildElapsedTicksMax > 0
                ? bridgeTiming.BuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            long bridgeParallelBuildCallsDelta = bridgeTiming.ParallelBuildCalls - _bridgeTimingBaseline.ParallelBuildCalls;
            long bridgeParallelBuildElapsedTicksDelta = bridgeTiming.ParallelBuildElapsedTicksTotal - _bridgeTimingBaseline.ParallelBuildElapsedTicksTotal;
            double parallelBuildAvgMs = bridgeParallelBuildCallsDelta > 0
                ? ((bridgeParallelBuildElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / bridgeParallelBuildCallsDelta
                : 0.0;
            double parallelBuildMaxMs = bridgeTiming.ParallelBuildElapsedTicksMax > 0
                ? bridgeTiming.ParallelBuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double buildAllocAvgBytes = bridgeBuildCallsDelta > 0
                ? (double)bridgeBuildAllocatedBytesDelta / bridgeBuildCallsDelta
                : 0.0;
            double replayVisitAvgMs = pipelineReplayCallsDelta > 0
                ? ((pipelineReplayElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pipelineReplayCallsDelta
                : 0.0;
            double replayVisitMaxMs = pipelineTiming.ReplayVisitElapsedTicksMax > 0
                ? pipelineTiming.ReplayVisitElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double coalesceCommitAvgMs = pipelineCoalesceCallsDelta > 0
                ? ((pipelineCoalesceElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pipelineCoalesceCallsDelta
                : 0.0;
            double coalesceCommitMaxMs = pipelineTiming.CoalesceCommitElapsedTicksMax > 0
                ? pipelineTiming.CoalesceCommitElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double deferredFlushAvgMs = pipelineDeferredFlushCallsDelta > 0
                ? ((pipelineDeferredFlushElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pipelineDeferredFlushCallsDelta
                : 0.0;
            double deferredFlushMaxMs = pipelineTiming.DeferredFlushElapsedTicksMax > 0
                ? pipelineTiming.DeferredFlushElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double recipientSelectionAvgMs = pipelineRecipientSelectionCallsDelta > 0
                ? ((pipelineRecipientSelectionElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pipelineRecipientSelectionCallsDelta
                : 0.0;
            double recipientSelectionMaxMs = pipelineTiming.RecipientSelectionElapsedTicksMax > 0
                ? pipelineTiming.RecipientSelectionElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double selectedDispatchAvgMs = pipelineSelectedDispatchCallsDelta > 0
                ? ((pipelineSelectedDispatchElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pipelineSelectedDispatchCallsDelta
                : 0.0;
            double selectedDispatchMaxMs = pipelineTiming.SelectedDispatchElapsedTicksMax > 0
                ? pipelineTiming.SelectedDispatchElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            double trySendAvgMs = pipelineTrySendCallsDelta > 0
                ? ((pipelineTrySendElapsedTicksDelta * 1000.0) / Stopwatch.Frequency) / pipelineTrySendCallsDelta
                : 0.0;
            double trySendMaxMs = pipelineTiming.TrySendElapsedTicksMax > 0
                ? pipelineTiming.TrySendElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;
            long ownerTransformAllocAvgBytes = replicationTiming.OwnerTransformCalls > 0
                ? replicationTiming.OwnerTransformAllocatedBytesTotal / replicationTiming.OwnerTransformCalls
                : 0;
            long observerTransformAllocAvgBytes = replicationTiming.ObserverTransformCalls > 0
                ? replicationTiming.ObserverTransformAllocatedBytesTotal / replicationTiming.ObserverTransformCalls
                : 0;
            long batchFinalizeAllocAvgBytes = replicationTiming.BatchFinalizeCalls > 0
                ? replicationTiming.BatchFinalizeAllocatedBytesTotal / replicationTiming.BatchFinalizeCalls
                : 0;
            double observerChunkBuildAvgMs = replicationTiming.ObserverChunkBuildCalls > 0
                ? (replicationTiming.ObserverChunkBuildElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / replicationTiming.ObserverChunkBuildCalls
                : 0.0;
            double observerChunkBuildMaxMs = replicationTiming.ObserverChunkBuildElapsedTicksMax > 0
                ? replicationTiming.ObserverChunkBuildElapsedTicksMax * 1000.0 / Stopwatch.Frequency
                : 0.0;

            string payload =
                $"frames={_windowFrames}|ticks={_windowTicksProcessed}|ticks_max_frame={_windowMaxTicksProcessedPerFrame}|" +
                $"tick_avg_ms={tickAvgMs:0.000}|tick_max_ms={_windowTickMsMax:0.000}|" +
                $"pump_avg_ms={pumpAvgMs:0.000}|pump_max_ms={_windowPumpMsMax:0.000}|" +
                $"flush_avg_ms={flushAvgMs:0.000}|flush_max_ms={_windowFlushMsMax:0.000}|" +
                $"conn_avg_ms={connAvgMs:0.000}|conn_max_ms={_windowConnectionEventMsMax:0.000}|" +
                $"inbound_dg={_windowInboundDatagrams}|inbound_dg_s={inboundRate:0.0}|" +
                $"mem_mb={(memoryBytes / (1024.0 * 1024.0)):0.0}|gc0={gc0 - _gc0Baseline}|gc1={gc1 - _gc1Baseline}|gc2={gc2 - _gc2Baseline}|" +
                $"ping_rx_d={pingRxDelta}|pong_tx_d={pongTxDelta}|" +
                $"repl_pub={_runtime.PlayerReplication.LastPublishedCandidates}|repl_send={_runtime.PlayerReplication.LastSentPackets}|" +
                $"repl_attempts_d={replSendAttemptsDelta}|repl_bytes_d={replBytesSentDelta}|" +
                $"repl_dirty_d={replDirtyDrainedDelta}|repl_enc_d={replDeltasEncodedDelta}|repl_interest_d={replInterestedSelectedDelta}|" +
                $"repl_if_calls_d={replInterestFilterCallsDelta}|repl_if_lookups_d={replInterestCacheLookupsDelta}|" +
                $"repl_if_hits_d={replInterestCacheHitsDelta}|repl_if_miss_d={replInterestCacheMissesDelta}|" +
                $"repl_fanout_hit_d={replFanoutHitDelta}|repl_fanout_miss_d={replFanoutMissDelta}|" +
                $"repl_def_state_d={replDeferredStateDelta}|repl_drop_state_d={replDroppedStateDelta}|" +
                $"repl_def_crit_d={replDeferredCriticalDelta}|repl_drop_crit_d={replDroppedCriticalDelta}|" +
                $"build_gc0_d={bridgeBuildGc0Delta}|build_gc1_d={bridgeBuildGc1Delta}|build_gc2_d={bridgeBuildGc2Delta}|" +
                $"owner_rents={replicationTiming.OwnerPayloadRents}|owner_rent_bytes={replicationTiming.OwnerPayloadRentBytes}|" +
                $"observer_miss={replicationTiming.ObserverPayloadMisses}|observer_rent_bytes={replicationTiming.ObserverPayloadRentBytes}|" +
                $"observer_pub={replicationTiming.ObserverCandidatesPublished}|observer_skip={replicationTiming.ObserverCandidatesCadenceSkipped}|observer_cadence={_runtime.Options.PlayerObserverStreamCadenceTicks}|observer_spatial_first={_runtime.Options.EnableObserverSpatialFirstBuild}|" +
                $"obs_chunk_pub={replicationTiming.ObserverChunkPublishedDeltas}|obs_chunk_bytes={replicationTiming.ObserverChunkPublishedBytes}|obs_chunk_windows={replicationTiming.ObserverChunkActiveWindows}|obs_chunk_deltas={replicationTiming.ObserverChunkActiveDeltas}|" +
                $"obs_chunk_sent={replicationTiming.ObserverChunkSentPackets}|obs_chunk_conn={replicationTiming.ObserverChunkBuiltConnections}|obs_chunk_visit={replicationTiming.ObserverChunkVisitedDeltas}|obs_chunk_repl={replicationTiming.ObserverChunkCoalescedReplaced}|obs_chunk_drop={replicationTiming.ObserverChunkBudgetDropped}|" +
                $"batch_rents={replicationTiming.BatchPayloadRents}|batch_rent_bytes={replicationTiming.BatchPayloadRentBytes}|" +
                $"owner_alloc_avg_b={ownerTransformAllocAvgBytes}|owner_alloc_max_b={replicationTiming.OwnerTransformAllocatedBytesMax}|owner_alloc_total_b={replicationTiming.OwnerTransformAllocatedBytesTotal}|" +
                $"observer_alloc_avg_b={observerTransformAllocAvgBytes}|observer_alloc_max_b={replicationTiming.ObserverTransformAllocatedBytesMax}|observer_alloc_total_b={replicationTiming.ObserverTransformAllocatedBytesTotal}|" +
                $"batch_finalize_calls={replicationTiming.BatchFinalizeCalls}|batch_finalize_alloc_avg_b={batchFinalizeAllocAvgBytes}|batch_finalize_alloc_max_b={replicationTiming.BatchFinalizeAllocatedBytesMax}|batch_finalize_alloc_total_b={replicationTiming.BatchFinalizeAllocatedBytesTotal}|" +
                $"obs_chunk_pub_avg_ms={(replicationTiming.ObserverChunkPublishCalls > 0 ? (replicationTiming.ObserverChunkPublishElapsedTicksTotal * 1000.0 / Stopwatch.Frequency) / replicationTiming.ObserverChunkPublishCalls : 0.0):0.000}|obs_chunk_pub_max_ms={(replicationTiming.ObserverChunkPublishElapsedTicksMax > 0 ? replicationTiming.ObserverChunkPublishElapsedTicksMax * 1000.0 / Stopwatch.Frequency : 0.0):0.000}|obs_chunk_build_avg_ms={observerChunkBuildAvgMs:0.000}|obs_chunk_build_max_ms={observerChunkBuildMaxMs:0.000}|obs_chunk_max_deltas={replicationTiming.ObserverChunkMaxWindowDeltas}|obs_chunk_max_bytes={replicationTiming.ObserverChunkMaxWindowBytes}|" +
                $"replay_buckets={replayWindow.OccupiedBucketCount}|replay_deltas={replayWindow.TotalDeltaCount}|replay_bytes={replayWindow.TotalUsedBytes}|" +
                $"replay_bucket_max_deltas={replayWindow.MaxBucketDeltaCount}|replay_bucket_max_bytes={replayWindow.MaxBucketUsedBytes}|" +
                $"replay_bucket_d={replayBucketDelta}|replay_deltas_d={replayDeltaCountDelta}|replay_bytes_d={replayBytesDelta}|" +
                $"world_avg_ms={worldTickAvgMs:0.000}|world_max_ms={worldTickMaxMs:0.000}|world_alloc_avg_b={worldTickAllocAvgBytes:0}|world_alloc_max_b={runtimeTiming.WorldTickAllocatedBytesMax}|" +
                $"repl_phase_avg_ms={replicationAvgMs:0.000}|repl_phase_max_ms={replicationMaxMs:0.000}|repl_alloc_avg_b={replicationAllocAvgBytes:0}|repl_alloc_max_b={runtimeTiming.ReplicationAllocatedBytesMax}|" +
                $"pong_build_avg_ms={pongBuildAvgMs:0.000}|pong_build_max_ms={pongBuildMaxMs:0.000}|" +
                $"aoi_avg_ms={aoiRefreshAvgMs:0.000}|aoi_max_ms={aoiRefreshMaxMs:0.000}|" +
                $"bridge_avg_ms={bridgeAvgMs:0.000}|bridge_max_ms={bridgeMaxMs:0.000}|" +
                $"publish_avg_ms={publishAvgMs:0.000}|publish_max_ms={publishMaxMs:0.000}|" +
                $"repl_build_workers={bridgeTiming.BuildWorkerCount}|parallel_build_avg_ms={parallelBuildAvgMs:0.000}|parallel_build_max_ms={parallelBuildMaxMs:0.000}|" +
                $"build_avg_ms={buildAvgMs:0.000}|build_max_ms={buildMaxMs:0.000}|build_alloc_avg_b={buildAllocAvgBytes:0}|build_alloc_max_b={bridgeTiming.BuildAllocatedBytesMax}|" +
                $"replay_avg_ms={replayVisitAvgMs:0.000}|replay_max_ms={replayVisitMaxMs:0.000}|" +
                $"sel_avg_ms={recipientSelectionAvgMs:0.000}|sel_max_ms={recipientSelectionMaxMs:0.000}|" +
                $"dispatch_avg_ms={selectedDispatchAvgMs:0.000}|dispatch_max_ms={selectedDispatchMaxMs:0.000}|" +
                $"send_avg_ms={trySendAvgMs:0.000}|send_max_ms={trySendMaxMs:0.000}|" +
                $"coalesce_avg_ms={coalesceCommitAvgMs:0.000}|coalesce_max_ms={coalesceCommitMaxMs:0.000}|" +
                $"defer_flush_avg_ms={deferredFlushAvgMs:0.000}|defer_flush_max_ms={deferredFlushMaxMs:0.000}|" +
                $"chat_avg_ms={chatFlushAvgMs:0.000}|chat_max_ms={chatFlushMaxMs:0.000}|chat_alloc_avg_b={chatFlushAllocAvgBytes:0}|chat_alloc_max_b={runtimeTiming.ChatFlushAllocatedBytesMax}|" +
                $"udp_assoc={transportStats.UdpAssociatedPeers}|udp_tx_dg_d={udpTxDatagramsDelta}|udp_rx_dg_d={udpRxDatagramsDelta}|" +
                $"udp_tx_frames_d={udpTxFramesDelta}|udp_drop_ep_d={udpDropsEndpointDelta}|udp_drop_tok_d={udpDropsTokenDelta}|udp_tx_fail_d={udpTxFailuresDelta}|" +
                $"srv_flush_calls_d={serverFlushCallsDelta}|srv_flush_avg_ms={serverFlushAvgMs:0.000}|srv_flush_max_ms={serverFlushMaxMs:0.000}|" +
                $"tcp_batch_w_avg_ms={tcpBatchWriteAvgMs:0.000}|tcp_batch_w_max_ms={tcpBatchWriteMaxMs:0.000}|tcp_batch_w_calls_d={tcpBatchWriteCallsDelta}|" +
                $"tcp_single_w_avg_ms={tcpSingleWriteAvgMs:0.000}|tcp_single_w_max_ms={tcpSingleWriteMaxMs:0.000}|tcp_single_w_calls_d={tcpSingleWriteCallsDelta}|" +
                $"pong_q_avg_ms={pongQueueAvgMs:0.000}|pong_q_max_ms={pongQueueMaxMs:0.000}|" +
                $"ping_in_q_avg_ms={pingInboundQueueAvgMs:0.000}|ping_in_q_max_ms={pingInboundQueueMaxMs:0.000}|" +
                $"inbound_q_depth={transportStats.InboundQueueDepth}|inbound_q_max={transportStats.InboundQueueDepthMax}|" +
                $"udp_fb_tcp_d={serverUdpFallbackTcpDelta}|udp_fb_noroute_d={serverUdpFallbackNoRouteDelta}|udp_fb_sendfail_d={serverUdpFallbackSendFailDelta}|" +
                $"borrowed_d={borrowedEnqueueSuccessesDelta}/{borrowedEnqueueAttemptsDelta}|borrowed_patch_d={borrowedPatchedSuccessesDelta}/{borrowedPatchedAttemptsDelta}|" +
                $"borrowed_multi_d={borrowedMulticastSuccessesDelta}/{borrowedMulticastAttemptsDelta}|copy_calls_d={outboundCopyCallsDelta}|copy_bytes_d={outboundCopyBytesDelta}|outbound_item_miss_d={outboundItemPoolMissesDelta}";

            string prefix = EnableDiagnostics ? "[ServerRuntime] DEBUG|" : "[ServerRuntime] METRIC|";
            _logger.Info($"{prefix}{payload}");
            _logger.Info($"[IslandNet] SERVER_METRIC|{payload}");

            _gc0Baseline = gc0;
            _gc1Baseline = gc1;
            _gc2Baseline = gc2;
            _pingRxBaseline = pingRx;
            _pongTxBaseline = pongTx;
            _transportStatsBaseline = transportStats;
            _networkControlTimingBaseline = networkControlTiming;
            _runtimeTimingBaseline = runtimeTiming;
            _replicationTimingBaseline = replicationTiming;
            _replicationStatsBaseline = replicationStats;
            _bridgeTimingBaseline = bridgeTiming;
            _pipelineTimingBaseline = pipelineTiming;
            _replayWindowBaseline = replayWindow;
            ResetDiagnosticsWindow();
        }

        private void ResetDiagnosticsWindow()
        {
            _windowFrames = 0;
            _windowTicksProcessed = 0;
            _windowMaxTicksProcessedPerFrame = 0;
            _windowInboundDatagrams = 0;
            _windowPumpMsTotal = 0d;
            _windowPumpMsMax = 0d;
            _windowTickMsTotal = 0d;
            _windowTickMsMax = 0d;
            _windowFlushMsTotal = 0d;
            _windowFlushMsMax = 0d;
            _windowConnectionEventMsTotal = 0d;
            _windowConnectionEventMsMax = 0d;
        }

        private void RecordPumpElapsed(long startTimestamp)
        {
            RecordElapsed(startTimestamp, ref _windowPumpMsTotal, ref _windowPumpMsMax);
        }

        private void RecordTickElapsed(long startTimestamp)
        {
            RecordElapsed(startTimestamp, ref _windowTickMsTotal, ref _windowTickMsMax);
        }

        private void RecordFlushElapsed(long startTimestamp)
        {
            RecordElapsed(startTimestamp, ref _windowFlushMsTotal, ref _windowFlushMsMax);
        }

        private void RecordConnectionEventElapsed(long startTimestamp)
        {
            RecordElapsed(startTimestamp, ref _windowConnectionEventMsTotal, ref _windowConnectionEventMsMax);
        }

        private static void RecordElapsed(long startTimestamp, ref double totalMs, ref double maxMs)
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            totalMs += elapsedMs;
            if (elapsedMs > maxMs)
                maxMs = elapsedMs;
        }

        private void OnRuntimeStateChanged(ServerRuntimeStateChangedEvent evt)
        {
            switch (evt.CurrentState)
            {
                case ServerRuntimeState.Initialized:
                    _logger.Info("Core: Initializing... - done");
                    SetProcess(false);
                    break;
                case ServerRuntimeState.Loading:
                    _logger.Info("Core: Starting... - done");
                    _logger.Info("Scripts: Compiling C# scripts... - done (0 warnings, 0 errors)");
                    _logger.Info("World: Loading...");
                    SetProcess(false);
                    break;
                case ServerRuntimeState.Running:
                {
                    int entities = _runtime?.PlayerWorld.PlayerCount ?? 0;
                    string noun = entities == 1 ? "entity" : "entities";
                    _logger.Info($"World: Loading... - done (0 items, {entities} {noun})");
                    LogDiscoveryStartupSummary();
                    string host = string.IsNullOrWhiteSpace(BindHost) ? "0.0.0.0" : BindHost;
                    int port = Port <= 0 ? 42042 : Port;
                    _logger.Info($"Listening: {host}:{port}");
                    SetProcess(true);
                    break;
                }
                case ServerRuntimeState.Stopping:
                case ServerRuntimeState.Stopped:
                    SetProcess(false);
                    break;
            }
        }

        private void OnRuntimeFaulted(ServerRuntimeFault fault)
        {
            string phase = fault.State switch
            {
                ServerRuntimeState.Initializing => "Core: Initializing",
                ServerRuntimeState.Starting     => "Core: Starting",
                ServerRuntimeState.Loading      => "World: Loading",
                _                               => $"Core: {fault.State}"
            };
            _logger.Error($"{phase}... failed");
            _logger.Error($"  {fault.Operation}: {fault.Exception.Message}");
            SetProcess(false);
        }

        public bool TrySendEnterGame(long sessionId, string detail = "")
        {
            if (_runtime == null || _transport == null)
                return false;

            if (!_runtime.Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot))
                return false;

            byte[] payload = UiPanelNetworkCodec.EncodeSessionEnterGame(detail);
            _transport.EnqueueOutbound(new OutboundDatagram(
                snapshot.ConnectionId,
                ChannelType.ReliableOrdered,
                payload));
            return true;
        }

        private void OnSessionAccepted(ServerSessionAcceptedEvent evt)
        {
            if (_runtime == null)
                return;

            byte[] challenge = new byte[LoginPacketCodec.ChallengePlainBytes];
            System.Security.Cryptography.RandomNumberGenerator.Fill(challenge);

            UiPanelInstanceId instanceId = _runtime.UiPanels.OpenPanel(
                evt.SessionId,
                new UiPanelTypeId(UI.LoginPanel.TypeId),
                UiPanelCategory.Menu,
                UiPanelPresentationMode.ExclusiveMenuOnly,
                definitionRevision: 1,
                flags: UiPanelFlags.BringToFront | UiPanelFlags.BlocksGameplayInput,
                payload: LoginPacketCodec.EncodeChallengePayload(challenge),
                responseHandler: (sid, response) =>
                {
                    if (response.ActionId == UI.LoginPanel.CancelButton)
                        _runtime.UiPanels.ClosePanel(sid, response.PanelInstanceId);
                });

            _loginChallenges[evt.SessionId] = (challenge, instanceId);
        }

        private void OnLoginRequest(
            long sessionId,
            UiPanelInstanceId instanceId,
            string username,
            byte[] passwordHash,
            byte[] challengeResponse)
        {
            if (_runtime == null)
                return;

            if (!_loginChallenges.TryGetValue(sessionId, out (byte[] Challenge, UiPanelInstanceId PanelId) state) ||
                state.PanelId.Value != instanceId.Value)
                return;

            string ip = _sessionIps.TryGetValue(sessionId, out string? s) ? s : "?";

            if (!_runtime.Accounts.TryLogin(
                username,
                passwordHash,
                challengeResponse,
                state.Challenge,
                ip,
                out AccountModel? account,
                out bool isNewAccount,
                out string errorMessage))
            {
                _logger.Info($"Client: {ip}: Login - Invalid credentials for '{username}': {errorMessage}");

                byte[] newChallenge = new byte[LoginPacketCodec.ChallengePlainBytes];
                System.Security.Cryptography.RandomNumberGenerator.Fill(newChallenge);
                _loginChallenges[sessionId] = (newChallenge, instanceId);

                if (_runtime.Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snap))
                {
                    byte[] responsePacket = LoginPacketCodec.EncodeLoginResponse(
                        instanceId,
                        LoginPacketCodec.LoginResponseResult.InvalidCredentials,
                        newChallenge,
                        errorMessage);
                    _transport?.EnqueueOutbound(new OutboundDatagram(
                        snap.ConnectionId,
                        ChannelType.ReliableOrdered,
                        responsePacket));
                }

                _runtime.UiPanels.OpenMessageBox(
                    sessionId,
                    title: "Login Failed",
                    body: string.IsNullOrWhiteSpace(errorMessage) ? "Invalid username/password." : errorMessage,
                    buttons: new[]
                    {
                        new MessageBoxButtonDefinition(1, "OK", IsDefault: true, IsCancel: true)
                    });
                return;
            }

            _loginChallenges.Remove(sessionId);
            _runtime.UiPanels.ClosePanel(sessionId, instanceId);
            if (isNewAccount)
                _logger.Info($"Client: {ip}: Creating new account '{username}'");
            _logger.Info($"Client: {ip}: Login - Valid credentials for '{username}'");

            if (account is null)
                return;

            _sessionAccounts[sessionId] = account;
            long nowTicks = unchecked((long)Time.GetTicksUsec());
            if (!_runtime.Sessions.TryMarkAuthenticated(sessionId, nowTicks))
            {
                _runtime.Sessions.TryDisconnectSession(sessionId, SessionDisconnectReason.ProtocolViolation, nowTicks, removeDisconnected: true);
                return;
            }

            _runtime.Events.PublishAccountVerified(sessionId, ip, username, account, isNewAccount);
        }

        private void OnAccountVerified(ServerAccountVerifiedEvent evt)
        {
            if (_runtime == null)
                return;

            CharacterModel[] characters = _runtime.Accounts.GetCharactersForAccount(evt.Account.AccountId);
            if (characters.Length == 0)
            {
                _runtime.Events.PublishCharacterCreateRequested(evt.SessionId, evt.Account);
                return;
            }

            OpenCharacterSelectPanel(evt.SessionId, evt.Account, characters);
        }

        private void OnCharacterCreateRequested(ServerCharacterCreateRequestedEvent evt)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.TryGetAccount(evt.Account.AccountId, out AccountModel account))
                return;

            if (account.FirstEmptySlot() < 0)
            {
                _runtime.UiPanels.OpenMessageBox(
                    evt.SessionId,
                    title: "Character Slots Full",
                    body: "You have reached the maximum allotted space for characters on this account. Delete a character before creating a new one.",
                    buttons: new[]
                    {
                        new MessageBoxButtonDefinition(1, "OK", IsDefault: true, IsCancel: true)
                    });
                return;
            }

            _sessionAccounts[evt.SessionId] = account;
            _runtime.UiPanels.CloseAllForSession(evt.SessionId, UiPanelCloseReason.ClosedByServer);
            OpenCharacterCreatePanel(evt.SessionId, account);
        }

        private void OnSessionReady(long sessionId)
        {
            string detail = _sessionEnterGameDetails.TryGetValue(sessionId, out string? value) ? value : string.Empty;
            TrySendEnterGame(sessionId, detail);
        }

        private void OnReturnToCharacterSelectRequested(long sessionId)
        {
            if (_runtime == null)
                return;

            if (!_sessionAccounts.TryGetValue(sessionId, out AccountModel? account) || account is null)
                return;

            long nowTicks = unchecked((long)Time.GetTicksUsec());
            if (!_runtime.TryReturnSessionToAuthenticated(sessionId, nowTicks))
                return;

            _sessionEnterGameDetails.Remove(sessionId);
            _runtime.SetPendingSessionSpawnPrefabId(sessionId, null);
            _runtime.SetPendingSessionSpawnDisplayName(sessionId, null);
            _runtime.SetPendingSessionSpawnCharacter(sessionId, null);
            _runtime.SetPendingSessionSpawnSnapshot(sessionId, null);

            if (!_runtime.Accounts.TryGetAccount(account.AccountId, out AccountModel refreshedAccount))
                return;

            _sessionAccounts[sessionId] = refreshedAccount;
            CharacterModel[] characters = _runtime.Accounts.GetCharactersForAccount(refreshedAccount.AccountId);
            if (characters.Length == 0)
            {
                _runtime.Events.PublishCharacterCreateRequested(sessionId, refreshedAccount);
                return;
            }

            OpenCharacterSelectPanel(sessionId, refreshedAccount, characters);
        }

        private void OpenCharacterSelectPanel(long sessionId, AccountModel account, CharacterModel[] characters)
        {
            if (_runtime == null)
                return;

            byte maxCharacters = (byte)Math.Clamp(account.CharacterSlots.Length, 1, 16);
            var panel = new UI.CharacterSelectSchemaPanel(
                username: account.Username,
                usedCharacters: account.CharacterCount,
                maxCharacters: maxCharacters,
                characters: characters,
                selectHandler: characterId => HandleCharacterSelected(sessionId, account, characterId),
                deleteHandler: characterId => HandleCharacterDeleted(sessionId, account, characterId),
                createHandler: () => PublishCharacterCreateRequested(sessionId, account.AccountId),
                cancelHandler: () => PromptDisconnectFromCharacterSelect(sessionId));

            _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.Replaced);
            _runtime.UiPanels.Open(
                sessionId,
                panel,
                UiPanelCategory.Menu,
                UiPanelPresentationMode.ExclusiveMenuOnly,
                UiPanelFlags.BringToFront | UiPanelFlags.BlocksGameplayInput);
        }

        private void OpenCharacterCreatePanel(long sessionId, AccountModel account)
        {
            if (_runtime == null)
                return;

            var panel = new UI.CharacterCreateSchemaPanel(
                createHandler: (name, archetypeId) => HandleCharacterCreated(sessionId, account, name, archetypeId),
                cancelHandler: () => HandleCharacterCreateCancelled(sessionId, account.AccountId));

            _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.Replaced);
            _runtime.UiPanels.Open(
                sessionId,
                panel,
                UiPanelCategory.Menu,
                UiPanelPresentationMode.ExclusiveMenuOnly,
                UiPanelFlags.BringToFront | UiPanelFlags.BlocksGameplayInput);
        }

        private void HandleCharacterSelected(long sessionId, AccountModel account, ulong characterId)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.TryGetAccount(account.AccountId, out account))
                return;

            if (!account.TryGetSlotIndex(characterId, out _) ||
                !_runtime.Accounts.TryGetCharacter(characterId, out CharacterModel character) ||
                character.AccountId != account.AccountId)
            {
                OpenCharacterSelectPanel(sessionId, account, _runtime.Accounts.GetCharactersForAccount(account.AccountId));
                return;
            }

            _sessionEnterGameDetails[sessionId] = character.Name;
            _runtime.SetPendingSessionSpawnPrefabId(sessionId, character.ArchetypeId);
            _runtime.SetPendingSessionSpawnDisplayName(sessionId, character.Name);
            _runtime.SetPendingSessionSpawnCharacter(sessionId, new PlayerCharacterComponent
            {
                CharacterId = character.CharacterId,
                SlotIndex = character.SlotIndex,
            });
            _runtime.SetPendingSessionSpawnSnapshot(sessionId, new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));
            _runtime.Events.PublishCharacterSelected(sessionId, account, character.CharacterId, character.Name);
            if (_runtime.Sessions.TryMarkReady(sessionId, unchecked((long)Time.GetTicksUsec())))
                _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.ClosedByServer);
        }

        private void HandleCharacterDeleted(long sessionId, AccountModel account, ulong characterId)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.TryGetAccount(account.AccountId, out account))
                return;

            if (_sessionSkipDeleteCharacterConfirm.TryGetValue(sessionId, out bool skipPrompt) && skipPrompt)
            {
                DeleteCharacterInternal(sessionId, account, characterId);
                return;
            }

            if (!_runtime.Accounts.TryGetCharacter(characterId, out CharacterModel character) ||
                character.AccountId != account.AccountId)
            {
                OpenCharacterSelectPanel(sessionId, account, _runtime.Accounts.GetCharactersForAccount(account.AccountId));
                return;
            }

            _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.Replaced);
            _runtime.UiPanels.Open(
                sessionId,
                new UI.CharacterDeleteConfirmSchemaPanel(
                    character.Name,
                    skipPrompt,
                    errorMessage: null,
                    confirmHandler: skipFutureWarning =>
                    {
                        _sessionSkipDeleteCharacterConfirm[sessionId] = skipFutureWarning;
                        if (_runtime.Accounts.TryGetAccount(account.AccountId, out AccountModel refreshed))
                            DeleteCharacterInternal(sessionId, refreshed, characterId);
                    },
                    cancelHandler: () =>
                    {
                        if (_runtime.Accounts.TryGetAccount(account.AccountId, out AccountModel refreshed))
                            OpenCharacterSelectPanel(sessionId, refreshed, _runtime.Accounts.GetCharactersForAccount(refreshed.AccountId));
                    }),
                UiPanelCategory.Menu,
                UiPanelPresentationMode.ExclusiveMenuOnly,
                UiPanelFlags.BringToFront | UiPanelFlags.BlocksGameplayInput);
        }

        private void DeleteCharacterInternal(long sessionId, AccountModel account, ulong characterId)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.DeleteCharacter(account, characterId, out AccountModel? updatedAccount) || updatedAccount is null)
            {
                OpenCharacterSelectPanel(sessionId, account, _runtime.Accounts.GetCharactersForAccount(account.AccountId));
                return;
            }

            _sessionAccounts[sessionId] = updatedAccount;
            CharacterModel[] characters = _runtime.Accounts.GetCharactersForAccount(updatedAccount.AccountId);
            if (characters.Length == 0)
            {
                _runtime.Events.PublishCharacterCreateRequested(sessionId, updatedAccount);
                return;
            }

            OpenCharacterSelectPanel(sessionId, updatedAccount, characters);
        }

        private void HandleCharacterCreated(long sessionId, AccountModel account, string name, ushort archetypeId)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.TryGetAccount(account.AccountId, out account))
                return;

            if (!_runtime.Accounts.TryCreateCharacter(account, name, archetypeId, out AccountModel? updatedAccount, out CharacterModel? character, out string errorMessage) ||
                updatedAccount is null ||
                character is null)
            {
                _runtime.UiPanels.OpenMessageBox(
                    sessionId,
                    title: "Character Create Failed",
                    body: string.IsNullOrWhiteSpace(errorMessage)
                        ? "Character names require at least 3 alphanumeric characters."
                        : errorMessage,
                    buttons: new[]
                    {
                        new MessageBoxButtonDefinition(1, "OK", IsDefault: true, IsCancel: true)
                    },
                    presentationMode: UiPanelPresentationMode.Shared);
                return;
            }

            _sessionAccounts[sessionId] = updatedAccount;
            _sessionEnterGameDetails[sessionId] = character.Name;
            _runtime.SetPendingSessionSpawnPrefabId(sessionId, character.ArchetypeId);
            _runtime.SetPendingSessionSpawnDisplayName(sessionId, character.Name);
            _runtime.SetPendingSessionSpawnCharacter(sessionId, new PlayerCharacterComponent
            {
                CharacterId = character.CharacterId,
                SlotIndex = character.SlotIndex,
            });
            _runtime.SetPendingSessionSpawnSnapshot(sessionId, new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));
            _runtime.Events.PublishCharacterCreated(sessionId, updatedAccount, character.Name, character.ArchetypeId);
            if (_runtime.Sessions.TryMarkReady(sessionId, unchecked((long)Time.GetTicksUsec())))
                _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.ClosedByServer);
        }

        private void PublishCharacterCreateRequested(long sessionId, ulong accountId)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.TryGetAccount(accountId, out AccountModel account))
                return;

            _runtime.Events.PublishCharacterCreateRequested(sessionId, account);
        }

        private void HandleCharacterCreateCancelled(long sessionId, ulong accountId)
        {
            if (_runtime == null)
                return;

            if (!_runtime.Accounts.TryGetAccount(accountId, out AccountModel account))
            {
                _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.Cancelled);
                return;
            }

            _runtime.UiPanels.CloseAllForSession(sessionId, UiPanelCloseReason.Cancelled);
            CharacterModel[] characters = _runtime.Accounts.GetCharactersForAccount(account.AccountId);
            OpenCharacterSelectPanel(sessionId, account, characters);
        }

        private void PromptDisconnectFromCharacterSelect(long sessionId)
        {
            if (_runtime == null)
                return;

            _runtime.UiPanels.OpenMessageBox(
                sessionId,
                title: "Disconnect?",
                body: "Disconnect from this server and leave character select?",
                buttons: new[]
                {
                    new MessageBoxButtonDefinition(ConfirmDisconnectYesAction, "Yes", IsDefault: true),
                    new MessageBoxButtonDefinition(ConfirmDisconnectNoAction, "No", IsCancel: true)
                },
                responseHandler: null,
                closeOnResponse: true);
        }

        private void OnClientDisconnected(long sessionId, SessionDisconnectReason reason)
        {
            string ip = _sessionIps.TryGetValue(sessionId, out string? s) ? s : "?";
            _sessionIps.Remove(sessionId);
            _loginChallenges.Remove(sessionId);
            _sessionAccounts.Remove(sessionId);
            _sessionEnterGameDetails.Remove(sessionId);
            _sessionSkipDeleteCharacterConfirm.Remove(sessionId);
            string reasonText = reason switch
            {
                SessionDisconnectReason.Timeout           => "Disconnected - Inactivity",
                SessionDisconnectReason.ServerShutdown    => "Disconnected - Server shutdown",
                SessionDisconnectReason.Kicked            => "Disconnected - Kicked",
                SessionDisconnectReason.ServerBusy        => "Disconnected - Server busy",
                SessionDisconnectReason.IpRejected        => "Disconnected - IP rejected",
                SessionDisconnectReason.DuplicateSession  => "Disconnected - Duplicate session",
                SessionDisconnectReason.ProtocolViolation => "Disconnected - Protocol violation",
                _                                         => "Disconnected"
            };
            _logger.Info($"Client: {ip}: {reasonText}");
        }
    }
}
