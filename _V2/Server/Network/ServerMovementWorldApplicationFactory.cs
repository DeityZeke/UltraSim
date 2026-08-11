using System.Net;
using System.Diagnostics;
using System.Globalization;
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
        if (string.IsNullOrWhiteSpace(options.CommandPrefix))
            throw new ArgumentException("Command prefix cannot be empty.", nameof(options));
        _ = ParseBindAddress(options.BindAddress);
        if ((uint)options.Port > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.MaxClients < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Max clients cannot be negative.");
        if (options.AutoSaveIntervalSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(options), "Autosave interval cannot be negative.");

        var runtime = new Runtime(options);
        return new ServerApplication(
            new ServerApplicationOptions(
                options.ProfileName,
                SaveOnStop: options.SaveOnStop,
                AutoSaveIntervalSeconds: options.AutoSaveIntervalSeconds),
            new ServerApplicationHooks
            {
                Start = app => runtime.Start(app),
                Load = app => runtime.Load(app),
                Tick = (_, deltaSeconds) => runtime.Tick(deltaSeconds),
                Save = app => runtime.Save(app),
                Stop = _ => runtime.Stop(),
                IsAcceptingConnections = _ => runtime.IsAcceptingConnections,
                GetActiveSessions = _ => runtime.ActiveSessions,
                GetSystemRuntimeSnapshots = _ => runtime.GetSystemRuntimeSnapshots(),
                GetPlayerSessions = _ => runtime.GetPlayerSessionsSnapshot(),
                GetAccountSummaries = _ => runtime.GetAccountSummariesSnapshot(),
                KickPlayerSession = (_, sessionId) => runtime.KickPlayerSession(sessionId),
                BanAccount = (_, accountId) => runtime.BanAccount(accountId),
                UnbanAccount = (_, accountId) => runtime.UnbanAccount(accountId),
                SetAccountAccessLevel = (_, accountId, accessLevel) =>
                    runtime.SetAccountAccessLevel(accountId, accessLevel),
                ReplaceAccountAccess = (_, accountId, accessLevel, directGrants, directDenies) =>
                    runtime.ReplaceAccountAccess(accountId, accessLevel, directGrants, directDenies),
                ExecuteCommand = (app, command) => runtime.ExecuteCommand(app, command),
            });
    }

    private static IPAddress ParseBindAddress(string bindAddress)
    {
        string value = bindAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Any;
        }

        if (string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "loopback", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(value, out IPAddress? parsed))
            return parsed;

        throw new ArgumentException($"Bind address '{bindAddress}' is not a valid IP address.", nameof(bindAddress));
    }

    private sealed class Runtime : IDisposable
    {
        private readonly ServerMovementWorldApplicationOptions _options;
        private readonly World _world;
        private readonly ArchetypeSignature _playerSignature;
        private readonly InMemoryAccountRegistry _accounts = new();
        private readonly InMemoryCharacterRegistry _characters = new();
        private readonly AccountCharacterService _characterService;
        private readonly Pbkdf2PasswordHashService _passwords = new();
        private readonly InMemoryLoginRateLimiter _loginRateLimiter = new();
        private readonly InMemoryAccountSessionTracker _accountSessions = new();
        private readonly InMemoryAccountAuditSink _accountAudit = new();
        private readonly AccountAccessAdministrationService _accountAccess;
        private readonly AccountStatusAdministrationService _accountStatus;
        private readonly DefaultCredentialService _credentialService;
        private Action<string>? _operatorLog;
        private const float SprintMovementMultiplier = 1.75f;
        private UdpSocketNetworkTransport? _transport;
        private ServerMovementSessionHost? _host;
        private uint _nextNetworkEntityId = 1;
        private ulong _serverTick;
        private ulong _lastSaveSequence;
        private bool _lastLoadStartedFresh;
        private bool _lastLoadUsedBackup;
        private int _lastPersistenceWarningCount;
        private int _lastPersistenceFailureCount;
        private string _lastPersistencePath = string.Empty;
        private long _lastPersistenceBytes;
        private int _lastPersistenceFileCount;
        private ulong _lastPersistenceRunTick;
        private long _persistenceOperationCount;
        private long _lastPersistenceElapsedTicks;
        private double _totalPersistenceElapsedMilliseconds;
        private double _peakPersistenceElapsedMilliseconds;
        private double _minPersistenceElapsedMilliseconds;
        private long _lastPumpElapsedTicks;

        public Runtime(ServerMovementWorldApplicationOptions options)
        {
            _options = options;
            _characterService = new AccountCharacterService(_accounts, _characters);
            _accountAccess = new AccountAccessAdministrationService(_accounts);
            _accountStatus = new AccountStatusAdministrationService(_accounts);
            _credentialService = new DefaultCredentialService(
                _accounts,
                _passwords,
                _loginRateLimiter,
                _accountSessions,
                _accountAudit,
                autoCreateUnknownAccounts: true,
                maxAutoCreatedAccountsPerRemoteAddress: options.MaxAutoCreatedAccountsPerRemoteAddress,
                operatorEvents: LogAccountOperatorEvent);
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

        public void Start(ServerApplication app)
        {
            _operatorLog = app.Log;
            if (_host is not null)
                return;

            var metadata = DefaultPacketMetadata.Create();
            _transport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
                new IPEndPoint(ParseBindAddress(_options.BindAddress), _options.Port),
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
                MaxActiveSessions: _options.MaxClients,
                AuthTimeoutTicks: SecondsToTicks(_options.SessionAuthTimeoutSeconds),
                KeepAliveTimeoutTicks: SecondsToTicks(_options.SessionKeepAliveTimeoutSeconds));

            _host = new ServerMovementSessionHost(
                _world,
                _transport,
                ApplyMovementInput,
                ProjectMovementState,
                SpawnSession,
                despawnSession: DespawnSession,
                endAccountSession: EndAccountSession,
                recordAccountPlayTime: RecordAccountPlayTime,
                getAccountAccessMetadata: _accounts.TryGetById,
                characterPositionProjector: ProjectCharacterPosition,
                characterPositionApplier: ApplyCharacterPosition,
                selectedCharacterSnapshotWriter: WriteSelectedCharacterSnapshot,
                handshakePolicy: handshakePolicy,
                sessionPolicy: sessionPolicy,
                allocateNetworkEntityId: AllocateNetworkEntityId,
                scriptContext: _options.ScriptContext,
                admissionCredentialValidator: new AccountingAdmissionCredentialValidator(
                    _credentialService,
                    ResolveRemoteAddress,
                    ResolveAccountSessionKey),
                admissionRuntimePolicy: new AccountingAdmissionRuntimePolicy(
                    _characterService,
                    bindCharacter: TryBindSelectedCharacter),
                operatorLog: (connectionId, message) =>
                    _operatorLog?.Invoke($"Client: {ResolveRemoteAddress(connectionId)}: {message}"),
                commandPrefix: _options.CommandPrefix,
                accessRules: _options.AccessRules,
                commandAudit: _options.CommandAudit,
                enableGlobalMovementObserverState: _options.EnableGlobalMovementObserverState);
            _host.Start();
        }

        public void Load(ServerApplication app)
        {
            ServerPersistenceCoordinator coordinator = CreatePersistenceCoordinator();
            long persistenceStart = Stopwatch.GetTimestamp();
            ServerPersistenceLoadResult result = coordinator.Load();
            RecordPersistenceElapsed(Stopwatch.GetTimestamp() - persistenceStart);
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
            RecordPersistenceArtifacts(result.Timing);

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
            long persistenceStart = Stopwatch.GetTimestamp();
            ServerPersistenceSaveResult result = coordinator.Save(DateTimeOffset.UtcNow);
            RecordPersistenceElapsed(Stopwatch.GetTimestamp() - persistenceStart);
            if (!result.Succeeded)
            {
                _lastPersistenceFailureCount = result.Failures.Count == 0 ? 1 : result.Failures.Count;
                _lastPersistencePath = coordinator.CurrentSaveSetRoot;
                RecordPersistenceArtifacts(result.Timing);
                string message = result.Failures.Count == 0
                    ? "Server persistence save failed."
                    : string.Join("; ", result.Failures.Select(static failure => $"{failure.ParticipantName}: {failure.Message}"));
                throw new InvalidOperationException(message);
            }

            _lastSaveSequence = result.Metadata.SaveSequence;
            _lastPersistenceFailureCount = 0;
            _lastPersistencePath = coordinator.CurrentSaveSetRoot;
            RecordPersistenceArtifacts(result.Timing);
            app.Log($"Persistence: Saved save-set sequence {result.Metadata.SaveSequence} in {result.Timing.TotalMilliseconds:F2} ms ({_lastPersistenceBytes} bytes).");
        }

        public void Stop()
        {
            _host?.Stop();
            _host = null;
            _transport?.Dispose();
            _transport = null;
            _operatorLog = null;
        }

        public ServerCommandResult? ExecuteCommand(ServerApplication app, ServerControlCommand command)
        {
            return command.Kind switch
            {
                ServerControlCommandKind.ListBackups => ListBackups(),
                ServerControlCommandKind.RestoreBackup => RestoreBackup(app, command.Argument),
                _ => null,
            };
        }

        private ServerCommandResult ListBackups()
        {
            IReadOnlyList<ServerPersistenceBackupSnapshot> backups = CreatePersistenceCoordinator().ListBackups();
            if (backups.Count == 0)
                return ServerCommandResult.Accepted("No backups found.");

            string summary = string.Join("; ", backups.Select(static backup =>
                $"{backup.DirectoryName} seq={backup.Manifest.SaveSequence.ToString(CultureInfo.InvariantCulture)} saved={backup.Manifest.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}"));
            return ServerCommandResult.Accepted(summary);
        }

        private ServerCommandResult RestoreBackup(ServerApplication app, string? backupDirectoryName)
        {
            if (app.State == ServerApplicationState.Running)
                return ServerCommandResult.Rejected("Stop or shutdown the runtime before restoring a backup.");

            string requestedBackup = backupDirectoryName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestedBackup))
                return ServerCommandResult.Rejected("Backup folder name is required. Run 'backups' first.");

            ServerPersistenceCoordinator coordinator = CreatePersistenceCoordinator();
            long persistenceStart = Stopwatch.GetTimestamp();
            ServerPersistenceRestoreResult restoreResult = coordinator.RestoreBackup(requestedBackup, DateTimeOffset.UtcNow);
            if (!restoreResult.Succeeded)
                return ServerCommandResult.Rejected(BuildPersistenceFailureMessage(restoreResult.Failures));

            ServerPersistenceLoadResult loadResult = coordinator.Load();
            RecordPersistenceElapsed(Stopwatch.GetTimestamp() - persistenceStart);
            if (!loadResult.Succeeded)
            {
                _lastPersistenceFailureCount = loadResult.Failures.Count == 0 ? 1 : loadResult.Failures.Count;
                _lastPersistencePath = coordinator.CurrentSaveSetRoot;
                return ServerCommandResult.Rejected($"Backup restored on disk but failed to load: {BuildPersistenceFailureMessage(loadResult.Failures)}");
            }

            if (loadResult.Manifest is not null)
                _lastSaveSequence = loadResult.Manifest.SaveSequence;
            _lastLoadStartedFresh = loadResult.StartedFresh;
            _lastLoadUsedBackup = false;
            _lastPersistenceWarningCount = restoreResult.Warnings.Count + loadResult.Warnings.Count;
            _lastPersistenceFailureCount = 0;
            _lastPersistencePath = coordinator.CurrentSaveSetRoot;
            RecordPersistenceArtifacts(loadResult.Timing);

            ulong sequence = restoreResult.Manifest?.SaveSequence ?? 0UL;
            string safetyBackup = string.IsNullOrWhiteSpace(restoreResult.SafetyBackupRoot)
                ? "no safety backup"
                : $"safety backup '{Path.GetFileName(restoreResult.SafetyBackupRoot)}'";
            return ServerCommandResult.Accepted(
                $"Restored '{restoreResult.BackupDirectoryName}' to current save-set sequence {sequence.ToString(CultureInfo.InvariantCulture)} ({safetyBackup}).");
        }

        private void LogAccountOperatorEvent(AccountOperatorEvent evt)
        {
            Action<string>? log = _operatorLog;
            if (log is null)
                return;

            string username = string.IsNullOrWhiteSpace(evt.Username)
                ? "<empty>"
                : evt.Username.Trim();
            string remote = string.IsNullOrWhiteSpace(evt.RemoteAddress)
                ? "unknown"
                : evt.RemoteAddress.Trim();

            string message = evt.Kind switch
            {
                AccountOperatorEventKind.AccountCreated =>
                    $"Client: {remote}: Creating new account '{username}'",
                AccountOperatorEventKind.LoginSucceeded =>
                    $"Client: {remote}: Login - Valid credentials for '{username}'",
                AccountOperatorEventKind.LoginFailed =>
                    $"Client: {remote}: Login - Invalid credentials for '{username}': {evt.PublicMessage}",
                _ => $"Client: {remote}: Login - {evt.ResultKind} for '{username}'",
            };

            log(message);
        }

        private string ResolveRemoteAddress(ConnectionId connectionId)
        {
            if (_transport?.TryGetRemoteEndPoint(connectionId, out IPEndPoint? remoteEndPoint) == true &&
                remoteEndPoint is not null)
            {
                return remoteEndPoint.Address.ToString();
            }

            return $"connection:{connectionId.Value}";
        }

        private static string ResolveAccountSessionKey(ConnectionId connectionId)
        {
            return $"connection:{connectionId.Value}";
        }

        private void EndAccountSession(ConnectionId connectionId, ulong accountId)
        {
            _accountSessions.EndSession(accountId, ResolveAccountSessionKey(connectionId));
        }

        private void RecordAccountPlayTime(ulong accountId, TimeSpan elapsed)
        {
            if (accountId == 0 || elapsed <= TimeSpan.Zero)
                return;

            if (_accounts.TryGetById(accountId, out ServerAccountRecord account))
                _accounts.Update(account.WithAdditionalPlayTime(elapsed));
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
                    UpdateCount: _serverTick > long.MaxValue ? long.MaxValue : (long)_serverTick,
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
                    LastRunTick: _lastPersistenceRunTick,
                    UpdateCount: _persistenceOperationCount,
                    LastUpdateMilliseconds: TicksToMilliseconds(_lastPersistenceElapsedTicks),
                    AverageUpdateMilliseconds: _persistenceOperationCount == 0
                        ? 0d
                        : _totalPersistenceElapsedMilliseconds / _persistenceOperationCount,
                    PeakUpdateMilliseconds: _peakPersistenceElapsedMilliseconds,
                    MinUpdateMilliseconds: _persistenceOperationCount == 0 ? 0d : _minPersistenceElapsedMilliseconds,
                    ControlCount: _lastPersistenceWarningCount + _lastPersistenceFailureCount,
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
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            for (int i = 0; i < count; i++)
            {
                ConnectionId connectionId = connections[i];
                bool hasSelected = host.TryGetSelectedCharacter(connectionId, out ServerSelectedCharacterSession selected);
                ServerAccountRecord account = null!;
                bool hasAccount = hasSelected && _accounts.TryGetById(selected.AccountId, out account);
                string remoteAddress = ResolveRemoteAddress(connectionId);
                TimeSpan currentUptime = host.TryGetConnectedAtUtc(connectionId, out DateTimeOffset connectedAtUtc)
                    ? ClampDuration(nowUtc - connectedAtUtc)
                    : TimeSpan.Zero;
                TimeSpan totalPlayTime = hasAccount
                    ? account.TotalPlayTime + currentUptime
                    : currentUptime;
                players[i] = new ServerPlayerSessionSnapshot(
                    connectionId.Value.ToString(),
                    hasSelected ? selected.CharacterName : $"Player {connectionId.Value}",
                    hasSelected ? selected.AccountId.ToString() : "local",
                    hasSelected ? "InWorld" : "Ready",
                    hasSelected ? $"character={selected.CharacterId} prefab={selected.PrefabKey}" : string.Empty,
                    AccountName: hasAccount ? account.Username : string.Empty,
                    RemoteAddress: remoteAddress,
                    IsOnline: true,
                    CurrentUptime: currentUptime,
                    TotalPlayTime: totalPlayTime,
                    AccessLevel: hasAccount ? account.AccessLevel : 0,
                    AccessRuleKeys: hasAccount ? account.AccessRuleKeys : [],
                    DirectPermissionGrants: hasAccount ? account.DirectPermissionGrants : [],
                    DirectPermissionDenies: hasAccount ? account.DirectPermissionDenies : []);
            }

            return players;
        }

        public IReadOnlyList<ServerAccountSummarySnapshot> GetAccountSummariesSnapshot()
        {
            IReadOnlyList<ServerAccountRecord> accounts = _accounts.GetAccountsSnapshot();
            if (accounts.Count == 0)
                return Array.Empty<ServerAccountSummarySnapshot>();

            HashSet<ulong> onlineAccounts = GetOnlineAccountIds();
            Dictionary<ulong, TimeSpan> livePlayTimeByAccount = GetLivePlayTimeByAccount();
            var summaries = new ServerAccountSummarySnapshot[accounts.Count];
            for (int i = 0; i < accounts.Count; i++)
            {
                ServerAccountRecord account = accounts[i];
                livePlayTimeByAccount.TryGetValue(account.AccountId, out TimeSpan livePlayTime);
                summaries[i] = new ServerAccountSummarySnapshot(
                    account.AccountId.ToString(),
                    account.Username,
                    onlineAccounts.Contains(account.AccountId),
                    account.Status == AccountStatus.Banned,
                    $"characters={account.CharacterCount}",
                    AccessLevel: account.AccessLevel,
                    AccessRuleKeys: account.AccessRuleKeys,
                    DirectPermissionGrants: account.DirectPermissionGrants,
                    DirectPermissionDenies: account.DirectPermissionDenies,
                    CreatedUtc: account.CreatedUtc,
                    LastLoginUtc: account.LastLoginUtc == DateTimeOffset.MinValue ? null : account.LastLoginUtc,
                    KnownRemoteAddresses: account.KnownRemoteAddresses,
                    TotalPlayTime: account.TotalPlayTime + livePlayTime);
            }

            return summaries;
        }

        public ServerAccountAccessUpdateResult ReplaceAccountAccess(
            string accountId,
            int accessLevel,
            IReadOnlyList<string> directGrants,
            IReadOnlyList<string> directDenies)
        {
            if (!ulong.TryParse(accountId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedAccountId))
                return ServerAccountAccessUpdateResult.Reject(accountId, "Account id is not valid.");

            AccountAccessUpdateResult result = _accountAccess.ReplaceAccessMetadata(
                parsedAccountId,
                accessLevel: Math.Max(0, accessLevel),
                directPermissionGrants: directGrants,
                directPermissionDenies: directDenies,
                reason: "Godot server shell permissions edit.");
            if (!result.Succeeded || result.Account is null)
                return ServerAccountAccessUpdateResult.Reject(accountId, result.Error);

            _operatorLog?.Invoke($"Accounts: Updated permissions for '{result.Account.Username}' access={result.Account.AccessLevel}.");
            return ServerAccountAccessUpdateResult.Success(accountId, $"Updated permissions for '{result.Account.Username}'.");
        }

        public ServerAccountAccessUpdateResult SetAccountAccessLevel(string accountId, int accessLevel)
        {
            if (!ulong.TryParse(accountId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedAccountId))
                return ServerAccountAccessUpdateResult.Reject(accountId, "Account id is not valid.");

            AccountAccessUpdateResult result = _accountAccess.ReplaceAccessMetadata(
                parsedAccountId,
                accessLevel: Math.Max(0, accessLevel),
                reason: "Godot server shell access level shortcut.");
            if (!result.Succeeded || result.Account is null)
                return ServerAccountAccessUpdateResult.Reject(accountId, result.Error);

            _operatorLog?.Invoke($"Accounts: Set access level for '{result.Account.Username}' to {result.Account.AccessLevel}.");
            return ServerAccountAccessUpdateResult.Success(accountId, $"Set '{result.Account.Username}' access to {AccessLevelName(result.Account.AccessLevel)}.");
        }

        private static string AccessLevelName(int accessLevel)
        {
            return accessLevel switch
            {
                >= 100 => "Developer / Owner",
                >= 80 => "Game Master (GM)",
                >= 60 => "Seer",
                >= 40 => "Councilor",
                _ => "Player"
            };
        }

        public ServerPlayerSessionActionResult KickPlayerSession(string sessionId)
        {
            if (!int.TryParse(sessionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int connectionValue))
                return ServerPlayerSessionActionResult.Reject(sessionId, "Player session id is not valid.");

            var connectionId = new ConnectionId(connectionValue);
            if (!connectionId.IsValid)
                return ServerPlayerSessionActionResult.Reject(sessionId, "Player session id is not valid.");

            ServerMovementSessionHost? host = _host;
            if (host is null)
                return ServerPlayerSessionActionResult.Reject(sessionId, "Server is not accepting player sessions.");

            string playerName = $"session {sessionId}";
            if (host.TryGetSelectedCharacter(connectionId, out ServerSelectedCharacterSession selected))
                playerName = selected.CharacterName;

            if (!host.TryKick(connectionId, Stopwatch.GetTimestamp()))
                return ServerPlayerSessionActionResult.Reject(sessionId, $"Player session '{sessionId}' is not online.");

            _operatorLog?.Invoke($"Players: Kicked '{playerName}' session={sessionId}.");
            return ServerPlayerSessionActionResult.Success(sessionId, $"Kicked '{playerName}'.");
        }

        public ServerAccountActionResult BanAccount(string accountId)
        {
            if (!ulong.TryParse(accountId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedAccountId))
                return ServerAccountActionResult.Reject(accountId, "Account id is not valid.");

            AccountStatusUpdateResult result = _accountStatus.ReplaceStatus(
                parsedAccountId,
                AccountStatus.Banned,
                "Banned from Godot server shell.");
            if (!result.Succeeded || result.Account is null)
                return ServerAccountActionResult.Reject(accountId, result.Error);

            int kicked = KickAccountSessions(parsedAccountId);
            _operatorLog?.Invoke($"Accounts: Banned '{result.Account.Username}' and kicked {kicked} active session(s).");
            string message = kicked == 0
                ? $"Banned '{result.Account.Username}'."
                : $"Banned '{result.Account.Username}' and kicked {kicked} active session(s).";
            return ServerAccountActionResult.Success(accountId, message);
        }

        public ServerAccountActionResult UnbanAccount(string accountId)
        {
            if (!ulong.TryParse(accountId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedAccountId))
                return ServerAccountActionResult.Reject(accountId, "Account id is not valid.");

            AccountStatusUpdateResult result = _accountStatus.ReplaceStatus(
                parsedAccountId,
                AccountStatus.Active,
                "Unbanned from Godot server shell.");
            if (!result.Succeeded || result.Account is null)
                return ServerAccountActionResult.Reject(accountId, result.Error);

            _operatorLog?.Invoke($"Accounts: Unbanned '{result.Account.Username}'.");
            return ServerAccountActionResult.Success(accountId, $"Unbanned '{result.Account.Username}'.");
        }

        private int KickAccountSessions(ulong accountId)
        {
            ServerMovementSessionHost? host = _host;
            if (host is null || host.BoundConnectionCount == 0)
                return 0;

            var connections = new ConnectionId[host.BoundConnectionCount];
            int count = host.CopyBoundConnections(connections);
            int kicked = 0;
            long nowTicks = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                ConnectionId connectionId = connections[i];
                if (!host.TryGetSelectedCharacter(connectionId, out ServerSelectedCharacterSession selected) ||
                    selected.AccountId != accountId)
                {
                    continue;
                }

                if (host.TryKick(connectionId, nowTicks))
                    kicked++;
            }

            return kicked;
        }

        private HashSet<ulong> GetOnlineAccountIds()
        {
            var online = new HashSet<ulong>();
            ServerMovementSessionHost? host = _host;
            if (host is null || host.BoundConnectionCount == 0)
                return online;

            var connections = new ConnectionId[host.BoundConnectionCount];
            int count = host.CopyBoundConnections(connections);
            for (int i = 0; i < count; i++)
            {
                if (host.TryGetSelectedCharacter(connections[i], out ServerSelectedCharacterSession selected))
                    online.Add(selected.AccountId);
            }

            return online;
        }

        private Dictionary<ulong, TimeSpan> GetLivePlayTimeByAccount()
        {
            var livePlayTime = new Dictionary<ulong, TimeSpan>();
            ServerMovementSessionHost? host = _host;
            if (host is null || host.BoundConnectionCount == 0)
                return livePlayTime;

            var connections = new ConnectionId[host.BoundConnectionCount];
            int count = host.CopyBoundConnections(connections);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            for (int i = 0; i < count; i++)
            {
                ConnectionId connectionId = connections[i];
                if (!host.TryGetSelectedCharacter(connectionId, out ServerSelectedCharacterSession selected) ||
                    !host.TryGetConnectedAtUtc(connectionId, out DateTimeOffset connectedAtUtc))
                {
                    continue;
                }

                TimeSpan elapsed = ClampDuration(nowUtc - connectedAtUtc);
                livePlayTime.TryGetValue(selected.AccountId, out TimeSpan current);
                livePlayTime[selected.AccountId] = current + elapsed;
            }

            return livePlayTime;
        }

        private static TimeSpan ClampDuration(TimeSpan value)
        {
            return value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        private void ApplyMovementInput(World world, Entity entity, in MovementInputPacket input, float deltaSeconds)
        {
            if (!world.TryGetComponent(entity, out ServerMovementBody body))
                return;

            float movementSpeed = (input.Flags & MovementInputPacket.SprintFlag) != 0
                ? _options.MovementSpeed * SprintMovementMultiplier
                : _options.MovementSpeed;
            float inputX = Math.Clamp(input.MoveX / 100f, -1f, 1f);
            float inputY = Math.Clamp(input.MoveY / 100f, -1f, 1f);
            float magnitudeSquared = (inputX * inputX) + (inputY * inputY);
            if (magnitudeSquared > 1f)
            {
                float inverseMagnitude = 1f / MathF.Sqrt(magnitudeSquared);
                inputX *= inverseMagnitude;
                inputY *= inverseMagnitude;
            }

            float velocityX = inputX * movementSpeed;
            float velocityY = inputY * movementSpeed;
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
            ulong serverTick,
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
                Path.Combine(AppContext.BaseDirectory, "saves");
            string backupsRoot = _options.PersistenceBackupsRoot ??
                Path.Combine(AppContext.BaseDirectory, "backups");
            return new ServerPersistenceCoordinator(
                root,
                [
                    new AccountingPersistenceParticipant(_accounts, _characters),
                    new EcsWorldPersistenceParticipant(_world)
                ],
                new ServerPersistenceSaveSetOptions(
                    BackupsRootDirectory: backupsRoot,
                    BackupCount: _options.PersistenceBackupCount,
                    BackupCompressor: ServerPersistenceBackupCompressorFactory.Create(
                        _options.PersistenceBackupStorageMode,
                        _options.PersistenceBackupZstdCompressionLevel)),
                initialSaveSequence: _lastSaveSequence);
        }

        private void RecordPersistenceElapsed(long elapsedTicks)
        {
            elapsedTicks = Math.Max(0, elapsedTicks);
            double elapsedMilliseconds = TicksToMilliseconds(elapsedTicks);
            _lastPersistenceElapsedTicks = elapsedTicks;
            _lastPersistenceRunTick = _serverTick;
            _persistenceOperationCount++;
            _totalPersistenceElapsedMilliseconds += elapsedMilliseconds;

            if (_persistenceOperationCount == 1)
            {
                _peakPersistenceElapsedMilliseconds = elapsedMilliseconds;
                _minPersistenceElapsedMilliseconds = elapsedMilliseconds;
                return;
            }

            _peakPersistenceElapsedMilliseconds = Math.Max(_peakPersistenceElapsedMilliseconds, elapsedMilliseconds);
            _minPersistenceElapsedMilliseconds = Math.Min(_minPersistenceElapsedMilliseconds, elapsedMilliseconds);
        }

        private void RecordPersistenceArtifacts(ServerPersistenceTiming timing)
        {
            long bytes = 0;
            int files = 0;
            for (int i = 0; i < timing.Participants.Count; i++)
            {
                ServerPersistenceParticipantTiming participant = timing.Participants[i];
                bytes += participant.SizeBytes;
                files += participant.FileCount;
            }

            _lastPersistenceBytes = bytes;
            _lastPersistenceFileCount = files;
        }

        private static double TicksToMilliseconds(long elapsedTicks)
        {
            return elapsedTicks <= 0 ? 0d : elapsedTicks * 1000d / Stopwatch.Frequency;
        }

        private static string BuildPersistenceFailureMessage(IReadOnlyList<ServerPersistenceSaveFailure> failures)
        {
            return failures.Count == 0
                ? "Persistence operation failed."
                : string.Join("; ", failures.Select(static failure => $"{failure.ParticipantName}: {failure.Message}"));
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
            return $"sequence={_lastSaveSequence} load={mode} warnings={_lastPersistenceWarningCount} failures={_lastPersistenceFailureCount} files={_lastPersistenceFileCount} bytes={_lastPersistenceBytes} path={path}";
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
