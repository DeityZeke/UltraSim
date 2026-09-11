using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Client.StandardUi;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;
using UltraSim.UI;

BotOptions options;
try
{
    options = BotOptions.Parse(args);
}
catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"UltraSim bot configuration error: {exception.Message}");
    return 2;
}

Console.WriteLine(
    $"PF1 BOT RUN endpoint={options.Endpoint} bots={options.BotCount} seed={options.Seed} " +
    $"durationMs={options.DurationMilliseconds} scenario={options.Scenario} " +
    $"cache={options.CacheRoot} network=ClientOnly");

Directory.CreateDirectory(options.CacheRoot);
string contentCacheRoot = Path.Combine(options.CacheRoot, "content");
if (!ContentCache.TryOpen(contentCacheRoot, out ContentCache? cache, out ContentCacheError cacheError))
{
    Console.Error.WriteLine($"PF1 bot content cache could not open: {cacheError}.");
    return 3;
}

using (cache)
using (var source = new HttpContentPackageSource(new ContentPackageSourceLimits(
    maximumArchiveBytes: 768L * 1024 * 1024,
    requestTimeoutMilliseconds: 30_000,
    allowInsecureServerHostedHttp: true,
    allowPrivateNetworkTargets: true,
    maximumConcurrentRequests: 4)))
using (var harness = new BotHarness(options, cache!, source))
{
    try
    {
        BotRunSummary summary = harness.Run();
        if (options.SummaryFile is not null)
            BotFiles.WriteAtomic(
                options.SummaryFile,
                JsonSerializer.Serialize(summary, BotFiles.JsonOptions));
        Console.WriteLine(summary.ToEvidenceLine());
        return summary.Passed ? 0 : 1;
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        Console.Error.WriteLine($"PF1 BOT HARNESS FAILED: {exception}");
        return 1;
    }
}

internal static class BotFiles
{
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static void WriteAtomic(string path, string text)
    {
        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("An output path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = full + $".{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporary, text + Environment.NewLine, new UTF8Encoding(false, true));
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed class BotHarness : IDisposable
{
    private const int PumpTargetMilliseconds = 5;
    private readonly BotOptions _options;
    private readonly ContentCache _cache;
    private readonly HttpContentPackageSource _source;
    private readonly BotClient[] _bots;
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly long _created = Stopwatch.GetTimestamp();
    private long _lastPump;
    private long _scenarioStarted;
    private long _workingSetHighWater;
    private long _pumpLagHighWaterTicks;
    private ulong _pumpIterations;
    private int _activeHighWater;
    private int _admittingHighWater;
    private int _reconnectingHighWater;
    private bool _disposed;

    internal BotHarness(
        BotOptions options,
        ContentCache cache,
        HttpContentPackageSource source)
    {
        _options = options;
        _cache = cache;
        _source = source;
        _bots = new BotClient[options.BotCount];
        for (int index = 0; index < _bots.Length; index++)
            _bots[index] = new BotClient(
                options, index, cache, source,
                checked(_created + ToStopwatchTicks(index * options.AdmissionStaggerMilliseconds)));
        _lastPump = Stopwatch.GetTimestamp();
    }

    internal BotRunSummary Run()
    {
        long admissionDeadline = checked(
            _created + ToStopwatchTicks(Math.Max(90_000, _options.BotCount * 1_000)));
        while (_scenarioStarted == 0)
        {
            PumpOnce();
            if (_bots.Any(static bot => bot.IsFaulted))
                return Finish(false, "A bot faulted during admission.");
            if (_bots.All(static bot => bot.HasReachedActive))
            {
                if (_options.StartReadyFile is not null)
                {
                    BotFiles.WriteAtomic(_options.StartReadyFile, "ready");
                    if (_options.StartReleaseFile is null)
                        throw new InvalidOperationException(
                            "--start-ready-file requires --start-release-file.");
                    long startDeadline = checked(
                        Stopwatch.GetTimestamp() + ToStopwatchTicks(120_000));
                    while (!File.Exists(_options.StartReleaseFile))
                    {
                        PumpOnce(allowNewChaos: false);
                        if (_bots.Any(static bot => bot.IsFaulted))
                            return Finish(false, "A bot faulted at the mixed-client start barrier.");
                        if (Stopwatch.GetTimestamp() >= startDeadline)
                            return Finish(false, "The mixed-client start marker did not arrive.");
                        Thread.Sleep(1);
                    }
                }
                _scenarioStarted = Stopwatch.GetTimestamp();
                SelectSwordContenders();
                foreach (BotClient bot in _bots) bot.StartScenario(_scenarioStarted);
                Console.WriteLine(
                    $"PF1 BOT SWARM ACTIVE bots={_bots.Length} generation=" +
                    $"{_bots[0].ServerGenerationId} sessions={_bots.Sum(static bot => bot.SessionCount)}");
                break;
            }
            if (Stopwatch.GetTimestamp() >= admissionDeadline)
                return Finish(false, "The bot swarm did not complete ordinary admission in time.");
            Thread.Sleep(1);
        }

        long scenarioDeadline = checked(
            _scenarioStarted + ToStopwatchTicks(_options.DurationMilliseconds));
        while (Stopwatch.GetTimestamp() < scenarioDeadline)
        {
            PumpOnce();
            if (_bots.Any(static bot => bot.IsFaulted))
                return Finish(false, "A bot faulted during mixed-chaos.");
            Thread.Sleep(1);
        }

        string? coverageFailure = ValidateCoverage();
        if (coverageFailure is not null)
            return Finish(false, coverageFailure);

        if (_options.ReadyFile is not null)
        {
            BotFiles.WriteAtomic(_options.ReadyFile, "ready");
            if (_options.ReleaseFile is null)
                throw new InvalidOperationException("--ready-file requires --release-file.");
            long releaseDeadline = checked(Stopwatch.GetTimestamp() + ToStopwatchTicks(120_000));
            while (!File.Exists(_options.ReleaseFile))
            {
                PumpOnce(allowNewChaos: false);
                if (_bots.Any(static bot => bot.IsFaulted))
                    return Finish(false, "A bot faulted while holding the mixed-client barrier.");
                if (Stopwatch.GetTimestamp() >= releaseDeadline)
                    return Finish(false, "The mixed-client release marker did not arrive.");
                Thread.Sleep(1);
            }
        }

        return Finish(true, null);
    }

    private void PumpOnce(bool allowNewChaos = true)
    {
        long now = Stopwatch.GetTimestamp();
        long lag = now - _lastPump;
        if (lag > _pumpLagHighWaterTicks) _pumpLagHighWaterTicks = lag;
        _lastPump = now;
        for (int index = 0; index < _bots.Length; index++)
            _bots[index].Pump(now, allowNewChaos);
        _activeHighWater = Math.Max(
            _activeHighWater, _bots.Count(static bot => bot.IsActive));
        _admittingHighWater = Math.Max(
            _admittingHighWater, _bots.Count(static bot => bot.IsAdmitting));
        _reconnectingHighWater = Math.Max(
            _reconnectingHighWater, _bots.Count(static bot => bot.IsReconnecting));
        _pumpIterations++;
        long workingSet = _process.WorkingSet64;
        if (workingSet > _workingSetHighWater) _workingSetHighWater = workingSet;
    }

    private string? ValidateCoverage()
    {
        if (_bots.Any(static bot => !bot.IsActive)) return "Not every bot was active after churn.";
        ulong generation = _bots[0].ServerGenerationId;
        if (generation == 0 || _bots.Any(bot => bot.ServerGenerationId != generation))
            return "Bots did not converge on one authoritative server generation.";
        if (!_bots.Any(static bot => bot.Body == GameGuestBody.HumanFemale) ||
            !_bots.Any(static bot => bot.Body == GameGuestBody.HumanMale))
            return "The swarm did not exercise mixed X/Y bodies.";
        if (_bots.Sum(static bot => bot.MovementAccepted) == 0 ||
            _bots.Sum(static bot => bot.JumpsAccepted) == 0)
            return "Authoritative movement/jump acceptance was not observed.";
        if (_bots.Sum(static bot => bot.LocalChatSubmitted) == 0 ||
            _bots.Sum(static bot => bot.GlobalChatSubmitted) == 0 ||
            _bots.Sum(static bot => bot.LocalChatObserved) == 0 ||
            _bots.Sum(static bot => bot.GlobalChatObserved) == 0)
            return "Local/Global Chat submission and recipient observation did not converge.";
        if (_bots.Sum(static bot => bot.NonsenseInputs) == 0 ||
            _bots.Sum(static bot => bot.NonsenseCommandsCreated) != 0)
            return "Unmapped physical input was not proven inert.";
        if (_bots.Sum(static bot => bot.SwordInteractionAttempts) < 2 ||
            _bots.Sum(static bot => bot.SwordPickupObserved) == 0 ||
            _bots.Sum(static bot => bot.SwordEquipObserved) == 0 ||
            _bots.Sum(static bot => bot.SwordDropObserved) == 0)
            return "Sword contention/pickup/equip/drop did not complete through ordinary semantics.";
        if (_bots.Sum(static bot => bot.SuccessfulReconnects) == 0 ||
            _bots.Sum(static bot => bot.FreshProofLifetimes) <= _bots.Length)
            return "A reconnect with fresh profile/content application proof did not complete.";
        if (_bots.Any(static bot => bot.StaleActorObservedAfterReconnect))
            return "A retired public actor reappeared after reconnect.";
        if (_bots.Sum(static bot => bot.ChunkTransitions) < 2 ||
            _bots.Sum(static bot => bot.VerticalTransitions) < 2 ||
            !_bots.Any(static bot => bot.SawAirborne) ||
            _bots.Sum(static bot => bot.EdgePressureAttempts) == 0 ||
            _bots.Sum(static bot => bot.LandingRecoveries) == 0 ||
            _bots.Sum(static bot => bot.PostRecoveryMovementAccepted) == 0)
            return "Chunk/AOI and edge/fall transition pressure was incomplete.";
        if (_bots.Any(static bot => bot.HasUnexpectedQueueDebt))
            return "A bot retained unexpected session queue debt at the acceptance boundary.";
        return null;
    }

    private void SelectSwordContenders()
    {
        (BotClient Bot, double DistanceSquared)[] candidates = _bots
            .Select(static bot => bot.TryMeasurePublicDistanceSquared(
                    GameWorldPublicPrefabIds.IronSword, out double distanceSquared)
                ? (bot: bot, distanceSquared: distanceSquared)
                : (bot: bot, distanceSquared: double.PositiveInfinity))
            .Where(static candidate => double.IsFinite(candidate.distanceSquared))
            .OrderBy(static candidate => candidate.distanceSquared)
            .ThenBy(static candidate => candidate.bot.Id)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException(
                "No bot could locate the sword through its ordinary public world state.");
        for (int index = 0; index < Math.Min(2, candidates.Length); index++)
            candidates[index].Bot.SetSwordContender();
    }

    private BotRunSummary Finish(bool requestedPass, string? detail)
    {
        long retirementStarted = Stopwatch.GetTimestamp();
        foreach (BotClient bot in _bots) bot.BeginFinalRetirement(retirementStarted);
        long deadline = checked(retirementStarted + ToStopwatchTicks(20_000));
        while (_bots.Any(static bot => !bot.IsRetired) && Stopwatch.GetTimestamp() < deadline)
        {
            PumpOnce(allowNewChaos: false);
            Thread.Sleep(1);
        }
        bool clean = _bots.All(static bot => bot.IsRetired);
        bool pass = requestedPass && clean && _bots.All(static bot => !bot.IsFaulted);
        detail ??= clean ? null : "Not every bot retired cleanly.";
        _process.Refresh();
        return BotRunSummary.Create(
            _options, _bots, pass, clean, detail, _pumpIterations,
            ToMilliseconds(_pumpLagHighWaterTicks), _workingSetHighWater,
            _process.TotalProcessorTime.TotalMilliseconds,
            _activeHighWater, _admittingHighWater, _reconnectingHighWater);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (BotClient bot in _bots) bot.Dispose();
    }

    private static long ToStopwatchTicks(long milliseconds) => checked(
        milliseconds * Stopwatch.Frequency / 1_000);

    private static double ToMilliseconds(long stopwatchTicks) =>
        stopwatchTicks * 1_000d / Stopwatch.Frequency;
}

internal enum BotLifetimeState : byte
{
    Pending = 1,
    Connecting = 2,
    Profile = 3,
    Content = 4,
    Active = 5,
    Draining = 6,
    ReconnectDelay = 7,
    Retired = 8,
    Faulted = 9,
}

internal enum BotCommandKind : byte
{
    None,
    Movement,
    Jump,
    Interaction,
    Primary,
    UiOpen,
}

internal sealed class BotClient : IDisposable
{
    private const int MaximumResidentChunks = 192;
    private readonly BotOptions _options;
    private readonly ContentCache _cache;
    private readonly HttpContentPackageSource _source;
    private readonly string _stateRoot;
    private readonly ClientSemanticChunkInfo[] _chunkScratch =
        new ClientSemanticChunkInfo[MaximumResidentChunks];
    private readonly ClientChatOccurrence[] _chatScratch = new ClientChatOccurrence[256];
    private readonly HashSet<ulong> _observedChatDeliveries = [];
    private readonly HashSet<SpatialChunkCoord> _visitedChunks = [];
    private readonly BotControlDriver _controls = new();
    private DeterministicRandom _random;
    private ClientDirectConnectSession? _connection;
    private RemoteClientProfileClient? _profile;
    private RemotePlayableSession? _session;
    private BotUiRenderer? _renderer;
    private ResolvedClientSettings? _settings;
    private long _startDue;
    private long _scenarioStarted;
    private long _nextChaos;
    private long _nextNavigation;
    private long _nextSwordAttempt;
    private long _drainDeadline;
    private bool _hasReachedActive;
    private bool _guestSubmitted;
    private bool _initialMovement;
    private bool _jumpPressed;
    private bool _jumpReleased;
    private bool _localChat;
    private bool _globalChat;
    private bool _nonsense;
    private bool _inventoryRequested;
    private bool _inventorySelected;
    private bool _inventoryEquipRequested;
    private bool _equippedReselected;
    private bool _primaryRequested;
    private bool _inventoryDropRequested;
    private bool _swordContender;
    private bool _reconnectRequested;
    private bool _finalRetirement;
    private bool _proofCounted;
    private bool _sawAirborne;
    private bool _landedAfterAirborne;
    private bool _disposed;
    private ulong _priorApplicationEpoch;
    private WorldPublicEntityId _priorPublicActor;
    private BotCommandKind _pendingCommand;
    private ulong _pendingCommandSequence;
    private ClientPublicActorVerticalPhase _lastVertical;
    private string? _fault;

    internal BotClient(
        BotOptions options,
        int id,
        ContentCache cache,
        HttpContentPackageSource source,
        long startDue)
    {
        _options = options;
        Id = id;
        Body = (id & 1) == 0 ? GameGuestBody.HumanFemale : GameGuestBody.HumanMale;
        _cache = cache;
        _source = source;
        _startDue = startDue;
        _stateRoot = Path.Combine(options.CacheRoot, "clients", id.ToString("D4"));
        _random = new DeterministicRandom(DeterministicRandom.Derive(options.Seed, id));
    }

    internal int Id { get; }
    internal GameGuestBody Body { get; }
    internal BotLifetimeState State { get; private set; } = BotLifetimeState.Pending;
    internal bool IsFaulted => State == BotLifetimeState.Faulted;
    internal bool IsActive => State == BotLifetimeState.Active;
    internal bool IsAdmitting => State is BotLifetimeState.Pending or
        BotLifetimeState.Connecting or BotLifetimeState.Profile or BotLifetimeState.Content;
    internal bool IsReconnecting => !_finalRetirement && _reconnectRequested &&
        SuccessfulReconnects == 0 && State is BotLifetimeState.Draining or
            BotLifetimeState.ReconnectDelay;
    internal bool IsRetired => State == BotLifetimeState.Retired || State == BotLifetimeState.Faulted;
    internal bool HasReachedActive => _hasReachedActive;
    internal ulong ServerGenerationId { get; private set; }
    internal int SessionCount { get; private set; }
    internal int SuccessfulReconnects { get; private set; }
    internal int FreshProofLifetimes { get; private set; }
    internal int MovementAttempts { get; private set; }
    internal int MovementAccepted { get; private set; }
    internal int MovementRejected { get; private set; }
    internal int JumpAttempts { get; private set; }
    internal int JumpsAccepted { get; private set; }
    internal int JumpsRejected { get; private set; }
    internal int LocalChatSubmitted { get; private set; }
    internal int GlobalChatSubmitted { get; private set; }
    internal int LocalChatObserved { get; private set; }
    internal int GlobalChatObserved { get; private set; }
    internal int NonsenseInputs { get; private set; }
    internal int NonsenseCommandsCreated { get; private set; }
    internal int SwordInteractionAttempts { get; private set; }
    internal int SwordPickupObserved { get; private set; }
    internal int SwordEquipObserved { get; private set; }
    internal int SwordDropObserved { get; private set; }
    internal int InteractionAccepted { get; private set; }
    internal int InteractionRejected { get; private set; }
    internal int CommandsAttempted { get; private set; }
    internal int PrimaryAttempts { get; private set; }
    internal int PrimaryAccepted { get; private set; }
    internal int PrimaryRejected { get; private set; }
    internal int ChunkTransitions { get; private set; }
    internal int VerticalTransitions { get; private set; }
    internal int LandingRecoveries { get; private set; }
    internal int PostRecoveryMovementAccepted { get; private set; }
    internal int EdgePressureAttempts { get; private set; }
    internal bool SawAirborne => _sawAirborne;
    internal bool StaleActorObservedAfterReconnect { get; private set; }
    internal string? Fault => _fault;

    internal bool HasUnexpectedQueueDebt
    {
        get
        {
            if (_session is null || State != BotLifetimeState.Active) return false;
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
            return _pendingCommand != BotCommandKind.None ||
                snapshot.Playable.CommandRetainedLocally ||
                snapshot.Chat.HasPendingProof || snapshot.Content.ProofRetained;
        }
    }

    internal void StartScenario(long timestamp)
    {
        _scenarioStarted = timestamp;
        _nextChaos = checked(timestamp + ToStopwatchTicks(6_000 + _random.NextInt(500)));
        _nextNavigation = timestamp;
        _nextSwordAttempt = checked(timestamp + ToStopwatchTicks(100));
    }

    internal void SetSwordContender() => _swordContender = true;

    internal bool TryMeasurePublicDistanceSquared(uint prefab, out double distanceSquared)
    {
        distanceSquared = 0;
        if (_session is null ||
            !TryFindPublic(prefab, out WorldPublicRecord target))
            return false;
        WorldPosition position = _session.GetSnapshot().Playable.Position;
        double dx = target.Position.GlobalXMillimeters - (double)position.GlobalXMillimeters;
        double dz = target.Position.GlobalZMillimeters - (double)position.GlobalZMillimeters;
        distanceSquared = dx * dx + dz * dz;
        return double.IsFinite(distanceSquared);
    }

    internal void Pump(long now, bool allowNewChaos)
    {
        if (_disposed || State is BotLifetimeState.Retired or BotLifetimeState.Faulted) return;
        try
        {
            if (State is BotLifetimeState.Pending or BotLifetimeState.ReconnectDelay)
            {
                if (now < _startDue) return;
                StartLifetime();
            }

            _connection!.Pump();
            ClientDirectConnectSnapshot connection = _connection.GetSnapshot();
            if (connection.State is ClientDirectConnectState.Failed)
            {
                Fail($"Direct Connect failed: {connection.Failure}: {connection.Detail}");
                return;
            }
            if (State == BotLifetimeState.Draining)
            {
                if (connection.State == ClientDirectConnectState.Closed || now >= _drainDeadline)
                    CompleteDrain(now, connection.State == ClientDirectConnectState.Closed);
                return;
            }
            if (connection.State != ClientDirectConnectState.Established) return;

            if (_profile is null)
            {
                ClientProfileDecisionFileStore store = ProfileStore();
                ClientProfileDecisionLoadResult loaded = store.Load();
                _profile = new RemoteClientProfileClient(
                    _connection.Peer!, connection.ApplicationSessionEpoch,
                    connection.Realm, loaded.Document, store);
                State = BotLifetimeState.Profile;
            }
            if (_session is null)
            {
                _profile.ProcessReceived();
                RemoteClientProfileSnapshot profile = _profile.GetSnapshot();
                if (profile.State == RemoteClientProfileState.Faulted)
                {
                    Fail(profile.Detail ?? "The server profile faulted.");
                    return;
                }
                if (profile.State == RemoteClientProfileState.AwaitingDecision &&
                    !_profile.TryChoose(ClientProfileApplicationMode.UseServerDefaults))
                    return;
                profile = _profile.GetSnapshot();
                if (profile.State != RemoteClientProfileState.Applied) return;
                _settings = ClientSettingsResolver.Resolve(
                    ClientSettingsDocument.Empty,
                    connection.SettingsScope,
                    _profile.Recommendation,
                    profile.Mode);
                _renderer = new BotUiRenderer();
                _session = new RemotePlayableSession(
                    _connection.Peer!,
                    connection.ApplicationSessionEpoch,
                    new RemoteRequiredContentBinding(
                        connection.Realm, _cache, ContentBaseCatalog.Empty, _source),
                    RemoteConceptDemoWorldProfile.CreateRequiredBinding(),
                    _renderer,
                    new ClientChatLimits(256, 256, 128),
                    standardUiDefinitionResolver: ClientStandardUiLibrary.Resolve);
                State = BotLifetimeState.Content;
            }

            _ = _session.ProcessReceived();
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
            if (snapshot.Content.State == RemoteContentClientState.Rejected ||
                snapshot.StructuralWorld.State == RemoteStructuralWorldState.Rejected ||
                snapshot.Playable.State is RemotePlayableClientState.Rejected or
                    RemotePlayableClientState.Faulted)
            {
                Fail(snapshot.Playable.Detail ?? snapshot.Content.Detail ??
                    "The remote playable session rejected the bot.");
                return;
            }
            if (!_guestSubmitted && snapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready)
            {
                string name = $"PF1 Bot {Id:D4}";
                if (_session.TrySubmitGuest(new GameGuestAdmissionRequest(Body, name)))
                    _guestSubmitted = true;
            }
            if (snapshot.Playable.State != RemotePlayableClientState.Active) return;

            if (State != BotLifetimeState.Active)
            {
                State = BotLifetimeState.Active;
                _hasReachedActive = true;
                SessionCount++;
                if (_priorApplicationEpoch != 0)
                {
                    if (_priorApplicationEpoch == snapshot.Playable.ApplicationSessionEpoch)
                    {
                        Fail("Reconnect reused an application-session epoch.");
                        return;
                    }
                    SuccessfulReconnects++;
                }
            }
            ServerGenerationId = snapshot.Playable.ServerGenerationId;
            Observe(snapshot);
            if (!_proofCounted && snapshot.Content.ProofsQueued > 0 &&
                _profile.GetSnapshot().ProofsQueued > 0)
            {
                FreshProofLifetimes++;
                _proofCounted = true;
            }
            if (_scenarioStarted != 0 && allowNewChaos)
                RunScenario(now, snapshot);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(exception.ToString());
        }
    }

    private void StartLifetime()
    {
        Directory.CreateDirectory(_stateRoot);
        ClientServerTrustFileStore store = TrustStore();
        ClientServerTrustLoadResult loaded = store.Load();
        _connection = new ClientDirectConnectSession(loaded.Registry, store);
        if (!_connection.TryStart(_options.Endpoint, _options.Fingerprint))
        {
            ClientDirectConnectSnapshot failed = _connection.GetSnapshot();
            Fail($"Direct Connect start failed: {failed.Failure}: {failed.Detail}");
            return;
        }
        State = BotLifetimeState.Connecting;
        _profile = null;
        _session = null;
        _renderer = null;
        _settings = null;
        _guestSubmitted = false;
        _proofCounted = false;
        _pendingCommand = BotCommandKind.None;
        _pendingCommandSequence = 0;
        _lastVertical = ClientPublicActorVerticalPhase.None;
        _controls.Reset();
    }

    private void Observe(in RemotePlayableSessionSnapshot snapshot)
    {
        RemotePlayableClientSnapshot playable = snapshot.Playable;
        if (_pendingCommand != BotCommandKind.None &&
            playable.LastProcessedCommandSequence >= _pendingCommandSequence)
        {
            RemotePlayableActionStatus result = _pendingCommand switch
            {
                BotCommandKind.Movement => playable.Locomotion,
                BotCommandKind.Jump => playable.Jump,
                BotCommandKind.Interaction => playable.Interaction,
                BotCommandKind.Primary => playable.Primary,
                _ => RemotePlayableActionStatus.Accepted,
            };
            bool accepted = result == RemotePlayableActionStatus.Accepted;
            switch (_pendingCommand)
            {
                case BotCommandKind.Movement when accepted:
                    MovementAccepted++;
                    if (_landedAfterAirborne && PostRecoveryMovementAccepted == 0)
                        PostRecoveryMovementAccepted++;
                    break;
                case BotCommandKind.Movement: MovementRejected++; break;
                case BotCommandKind.Jump when accepted: JumpsAccepted++; break;
                case BotCommandKind.Jump: JumpsRejected++; break;
                case BotCommandKind.Interaction when accepted: InteractionAccepted++; break;
                case BotCommandKind.Interaction: InteractionRejected++; break;
                case BotCommandKind.Primary when accepted: PrimaryAccepted++; break;
                case BotCommandKind.Primary: PrimaryRejected++; break;
            }
            _pendingCommand = BotCommandKind.None;
            _pendingCommandSequence = 0;
        }

        if (_visitedChunks.Add(playable.Position.Chunk) && _visitedChunks.Count > 1)
            ChunkTransitions++;
        if (_session!.PublicPresentationState.TryGet(
                playable.PublicActor, out ClientPublicPresentationMetadata metadata))
        {
            if (_lastVertical != ClientPublicActorVerticalPhase.None &&
                metadata.VerticalPhase != _lastVertical)
            {
                VerticalTransitions++;
                if ((_lastVertical is ClientPublicActorVerticalPhase.Rising or
                        ClientPublicActorVerticalPhase.Falling) &&
                    metadata.VerticalPhase == ClientPublicActorVerticalPhase.Grounded)
                {
                    LandingRecoveries++;
                    _landedAfterAirborne = true;
                }
            }
            if (metadata.VerticalPhase is ClientPublicActorVerticalPhase.Rising or
                    ClientPublicActorVerticalPhase.Falling)
                _sawAirborne = true;
            _lastVertical = metadata.VerticalPhase;
        }

        ClientChatStateSnapshot chat = _session.ChatState.GetSnapshot();
        if (chat.RetainedOccurrences != 0)
        {
            int count = _session.ChatState.CopyRecent(_chatScratch);
            for (int index = 0; index < count; index++)
            {
                ClientChatOccurrence occurrence = _chatScratch[index];
                if (!_observedChatDeliveries.Add(occurrence.DeliveryId) ||
                    !occurrence.Text.StartsWith($"PF1:{_options.Seed}:", StringComparison.Ordinal))
                    continue;
                if (occurrence.IsGlobal) GlobalChatObserved++;
                else if (occurrence.IsSpatial) LocalChatObserved++;
            }
        }

        ObserveInventory();
        if (_priorPublicActor.IsValid && SuccessfulReconnects != 0 &&
            ContainsPublic(_priorPublicActor))
            StaleActorObservedAfterReconnect = true;
    }

    private void RunScenario(long now, in RemotePlayableSessionSnapshot snapshot)
    {
        long elapsed = ToMillisecondsTicks(now - _scenarioStarted);
        if (!_initialMovement && elapsed >= 100 + Id * 20L &&
            (!_swordContender || SwordPickupObserved != 0 || elapsed >= 6_000))
        {
            float x = (Id & 1) == 0 ? 1f : 0.35f;
            float z = (Id & 1) == 0 ? 0.15f : 1f;
            if (TryMovement(x, z, snapshot.Playable.Position, now))
                _initialMovement = true;
        }
        if (!_jumpPressed && elapsed >= 500 && TryPhysicalAction(
                ClientSemanticAction.Jump, GamePlayerActions.Jump,
                BotCommandKind.Jump))
            _jumpPressed = true;
        if (_jumpPressed && !_jumpReleased && elapsed >= 750 &&
            TryCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
                BotCommandKind.None))
            _jumpReleased = true;
        if (!_localChat && elapsed >= 900 && Id == 0 &&
            _session!.Chat.TrySubmit($"PF1:{_options.Seed}:bot={Id}:local", ClientChatSpeechMode.Say))
        {
            _localChat = true;
            LocalChatSubmitted++;
        }
        if (!_globalChat && elapsed >= 1_100 && Id == 0 &&
            _session!.Chat.TrySubmit($"PF1:{_options.Seed}:bot={Id}:global", ClientChatSpeechMode.Global))
        {
            _globalChat = true;
            GlobalChatSubmitted++;
        }
        if (!_nonsense && elapsed >= 1_250)
        {
            if (!ClientPhysicalInput.TryCreate(
                    ClientPhysicalInputKind.Keyboard, "PF1_UNMAPPED", ClientInputModifiers.None,
                    out ClientPhysicalInput nonsense))
                throw new InvalidOperationException("The PF1 inert input was not canonical.");
            ulong before = _session!.Playable.Input.Snapshot().CommandsProduced;
            bool mapped = _settings!.TryResolveInput(in nonsense, out _);
            ulong after = _session.Playable.Input.Snapshot().CommandsProduced;
            NonsenseInputs++;
            if (mapped || after != before) NonsenseCommandsCreated++;
            _nonsense = true;
        }

        if (SwordPickupObserved == 0 && elapsed >= 100 && _swordContender &&
            now >= _nextSwordAttempt &&
            TryFindPublic(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword) &&
            TryInteraction(sword.Id))
        {
            SwordInteractionAttempts++;
            _nextSwordAttempt = checked(now + ToStopwatchTicks(500));
        }
        if (!_inventoryRequested && elapsed >= 700 && _swordContender &&
            TryCommand(new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
                BotCommandKind.UiOpen,
                flags: RemotePlayableCommandFlags.OpenInventoryRequested))
            _inventoryRequested = true;

        DriveInventory(elapsed);

        if (now >= _nextNavigation)
        {
            if (_swordContender && SwordPickupObserved == 0 && elapsed < 6_000)
                DriveTowardInteractable(
                    GameWorldPublicPrefabIds.IronSword, now, snapshot.Playable.Position);
            else if (Id == 4 && elapsed is >= 1_400 and < 6_000)
                DriveTowardInteractable(
                    GameWorldPublicPrefabIds.SkyTelepad, now, snapshot.Playable.Position);
            else if (Id == 5 && elapsed is >= 1_400 and < 8_000)
                DriveTowardInteractable(
                    GameWorldPublicPrefabIds.FarGate, now, snapshot.Playable.Position);
            else if (Id == 6 && elapsed is >= 1_500 and < 6_000 &&
                _pendingCommand == BotCommandKind.None)
                _ = TryMovement(1f, 0.2f, snapshot.Playable.Position, now);
            _nextNavigation = checked(now + ToStopwatchTicks(250));
        }

        if (Id == 4 && elapsed >= 5_000 && EdgePressureAttempts == 0)
        {
            if (TryMovement(1f, 0.7f, snapshot.Playable.Position, now))
            {
                EdgePressureAttempts++;
                _ = TryPhysicalAction(
                    ClientSemanticAction.Jump, GamePlayerActions.Jump, BotCommandKind.Jump);
            }
        }

        if (Id == _options.BotCount - 1 && !_reconnectRequested && elapsed >= 4_500)
        {
            _reconnectRequested = true;
            BeginRetirement(now, reconnect: true);
            return;
        }

        if (elapsed >= 6_000 && now >= _nextChaos && _pendingCommand == BotCommandKind.None)
        {
            RunRandomChaos(now, snapshot.Playable.Position);
            _nextChaos = checked(now + ToStopwatchTicks(250 + _random.NextInt(750)));
        }
    }

    private void RunRandomChaos(long now, in WorldPosition position)
    {
        switch (_random.NextInt(8))
        {
            case 0:
            case 1:
                _ = TryMovement(
                    _random.NextSignedUnit(), _random.NextSignedUnit(), position, now);
                break;
            case 2:
                _ = TryCommand(new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
                    BotCommandKind.Movement);
                break;
            case 3:
                _ = TryPhysicalAction(
                    ClientSemanticAction.Jump, GamePlayerActions.Jump, BotCommandKind.Jump);
                break;
            case 4:
                if (_session!.Chat.TrySubmit(
                        $"PF1:{_options.Seed}:bot={Id}:chaos={_random.NextInt(10_000)}",
                        ClientChatSpeechMode.Global))
                    GlobalChatSubmitted++;
                break;
            case 5:
                if (TryFindPublic(GameWorldPublicPrefabIds.InformationalSign, out WorldPublicRecord sign))
                    _ = TryInteraction(sign.Id);
                break;
            case 6:
                if (TryFindPublic(GameWorldPublicPrefabIds.IronSword, out WorldPublicRecord sword))
                {
                    if (TryInteraction(sword.Id)) SwordInteractionAttempts++;
                }
                break;
            default:
                _ = TryPhysicalAction(
                    ClientSemanticAction.Primary, GamePlayerActions.Primary, BotCommandKind.Primary);
                break;
        }
    }

    private bool TryMovement(float x, float z, in WorldPosition position, long now)
    {
        if (_pendingCommand != BotCommandKind.None) return false;
        GameLocomotionUpdate locomotion = _controls.EvaluateMovement(
            _settings!.ControlGrammar, x, z, position, ToMillisecondsTicks(now));
        return TryCommand(new ClientWorldCommandIntent(
            locomotion, GamePlayerActions.None, true), BotCommandKind.Movement);
    }

    private bool TryPhysicalAction(
        ClientSemanticAction expected,
        GamePlayerActions gameAction,
        BotCommandKind kind)
    {
        if (_pendingCommand != BotCommandKind.None ||
            !_settings!.TryGetBinding(expected, out ClientPhysicalInput physical) ||
            !_settings.TryResolveInput(in physical, out ClientSemanticAction resolved) ||
            resolved != expected)
            return false;
        return TryCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, gameAction, true), kind);
    }

    private bool TryInteraction(WorldPublicEntityId target) =>
        TryCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged, GamePlayerActions.Interact, true),
            BotCommandKind.Interaction, target);

    private bool TryCommand(
        in ClientWorldCommandIntent intent,
        BotCommandKind kind,
        WorldPublicEntityId target = default,
        RemotePlayableCommandFlags flags = RemotePlayableCommandFlags.None)
    {
        if (_pendingCommand != BotCommandKind.None || _session is null ||
            !_session.Playable.TrySubmitCommand(in intent, target, flags))
            return false;
        CommandsAttempted++;
        switch (kind)
        {
            case BotCommandKind.Movement: MovementAttempts++; break;
            case BotCommandKind.Jump: JumpAttempts++; break;
            case BotCommandKind.Primary: PrimaryAttempts++; break;
        }
        if (kind != BotCommandKind.None)
        {
            _pendingCommand = kind;
            _pendingCommandSequence = _session.Playable.Input.Snapshot().LastSequence;
        }
        return true;
    }

    private void DriveTowardInteractable(uint prefab, long now, in WorldPosition position)
    {
        if (!TryFindPublic(prefab, out WorldPublicRecord target)) return;
        long dx = target.Position.GlobalXMillimeters - position.GlobalXMillimeters;
        long dz = target.Position.GlobalZMillimeters - position.GlobalZMillimeters;
        long distanceSquared = checked(dx * dx + dz * dz);
        if (distanceSquared <= 1_200L * 1_200L)
        {
            if (_pendingCommand == BotCommandKind.None)
                _ = TryInteraction(target.Id);
            return;
        }
        double length = Math.Sqrt(distanceSquared);
        _ = TryMovement((float)(dx / length), (float)(dz / length), position, now);
    }

    private void DriveInventory(long elapsed)
    {
        if (_session is null || _renderer is null ||
            !_renderer.TryFind("game.inventory.gump", out UiViewInstanceId view,
                out CompiledUiDefinition? definition))
            return;
        UiSlotId items = definition!.Bindings.RequireSlot("items");
        ReadOnlySpan<UiListRow> rows = _session.UiClient.GetRows(view, items);
        UiListRow sword = default;
        bool found = false;
        bool equipped = false;
        for (int index = 0; index < rows.Length; index++)
        {
            ReadOnlySpan<UiValue> values = rows[index].Values;
            if (values.Length == 0 ||
                !string.Equals(values[0].Text, "Iron Sword", StringComparison.Ordinal))
                continue;
            sword = rows[index];
            found = true;
            equipped = values.Length >= 3 &&
                string.Equals(values[2].Text, "Main Hand", StringComparison.Ordinal);
            break;
        }
        if (found && SwordPickupObserved == 0) SwordPickupObserved++;
        if (equipped && SwordEquipObserved == 0) SwordEquipObserved++;
        if (_inventoryEquipRequested && SwordEquipObserved != 0 && !found && SwordDropObserved == 0)
            SwordDropObserved++;
        if (!found) return;

        try
        {
            if (!_inventorySelected)
            {
                _session.UiClient.SubmitAction(
                    view, definition.Bindings.RequireAction("select"), sword.Id);
                _inventorySelected = true;
                return;
            }
            UiActionId equip = definition.Bindings.RequireAction("equip");
            if (!_inventoryEquipRequested &&
                _session.UiClient.GetActionState(view, equip).Enabled)
            {
                _session.UiClient.SubmitAction(view, equip);
                _inventoryEquipRequested = true;
                return;
            }
            if (equipped && _inventoryEquipRequested && !_equippedReselected)
            {
                _session.UiClient.SubmitAction(
                    view, definition.Bindings.RequireAction("select"), sword.Id);
                _equippedReselected = true;
                return;
            }
            if (equipped && !_primaryRequested && elapsed >= 2_600 &&
                TryPhysicalAction(
                    ClientSemanticAction.Primary, GamePlayerActions.Primary,
                    BotCommandKind.Primary))
            {
                _primaryRequested = true;
                return;
            }
            UiActionId drop = definition.Bindings.RequireAction("drop");
            if (equipped && _primaryRequested && PrimaryAccepted != 0 &&
                !_inventoryDropRequested &&
                _session.UiClient.GetActionState(view, drop).Enabled)
            {
                _session.UiClient.SubmitAction(view, drop);
                _inventoryDropRequested = true;
            }
        }
        catch (UiSemanticPublicationException)
        {
            // Ordinary bounded client/server UI backpressure: retry on a later central pump.
        }
    }

    private void ObserveInventory()
    {
        if (!_inventoryDropRequested || SwordDropObserved != 0 ||
            _session is null || _renderer is null ||
            !_renderer.TryFind("game.inventory.gump", out UiViewInstanceId view,
                out CompiledUiDefinition? definition))
            return;
        ReadOnlySpan<UiListRow> rows = _session.UiClient.GetRows(
            view, definition!.Bindings.RequireSlot("items"));
        bool found = false;
        for (int index = 0; index < rows.Length; index++)
        {
            if (rows[index].Values.Length != 0 &&
                string.Equals(rows[index].Values[0].Text, "Iron Sword", StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }
        if (!found) SwordDropObserved++;
    }

    private bool TryFindPublic(uint prefab, out WorldPublicRecord found)
    {
        if (_session is not null &&
            _session.WorldCache.TryCopyResidentInfo(_chunkScratch, out int count, out _))
        {
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!_session.WorldCache.TryGetResidentView(
                        _chunkScratch[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.PrefabId != prefab) continue;
                    found = record;
                    return true;
                }
            }
        }
        found = default;
        return false;
    }

    private bool ContainsPublic(WorldPublicEntityId identity)
    {
        if (_session is null ||
            !_session.WorldCache.TryCopyResidentInfo(_chunkScratch, out int count, out _))
            return false;
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!_session.WorldCache.TryGetResidentView(
                    _chunkScratch[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
                if (record.Id == identity) return true;
        }
        return false;
    }

    internal void BeginFinalRetirement(long now)
    {
        _finalRetirement = true;
        if (State == BotLifetimeState.Active)
            BeginRetirement(now, reconnect: false);
        else if (State is BotLifetimeState.Pending or BotLifetimeState.ReconnectDelay)
        {
            DisposeLifetime();
            State = BotLifetimeState.Retired;
        }
    }

    private void BeginRetirement(long now, bool reconnect)
    {
        if (_session is not null)
        {
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
            _priorApplicationEpoch = snapshot.Playable.ApplicationSessionEpoch;
            _priorPublicActor = snapshot.Playable.PublicActor;
            _ = _session.TryRequestDetach();
        }
        _connection?.BeginDisconnect();
        _finalRetirement |= !reconnect;
        State = BotLifetimeState.Draining;
        _drainDeadline = checked(now + ToStopwatchTicks(10_000));
    }

    private void CompleteDrain(long now, bool cleanTransport)
    {
        bool reconnect = !_finalRetirement && _reconnectRequested && SuccessfulReconnects == 0;
        DisposeLifetime();
        if (!cleanTransport)
        {
            Fail("The bot transport did not drain within its bounded deadline.");
            return;
        }
        if (reconnect)
        {
            State = BotLifetimeState.ReconnectDelay;
            _startDue = checked(now + ToStopwatchTicks(250));
        }
        else State = BotLifetimeState.Retired;
    }

    private void DisposeLifetime()
    {
        _session?.Dispose();
        _connection?.Dispose();
        _session = null;
        _connection = null;
        _profile = null;
        _renderer = null;
        _settings = null;
    }

    private ClientServerTrustFileStore TrustStore() => new(
        Path.Combine(_stateRoot, "trusted-servers.json"));

    private ClientProfileDecisionFileStore ProfileStore() => new(
        Path.Combine(_stateRoot, "server-profiles.json"));

    private void Fail(string detail)
    {
        const int maximumDiagnosticCharacters = 2_048;
        _fault = detail.Length <= maximumDiagnosticCharacters
            ? detail
            : detail[..maximumDiagnosticCharacters];
        State = BotLifetimeState.Faulted;
        if (_options.Verbose)
            Console.Error.WriteLine($"PF1 bot {Id:D4} fault: {_fault}");
        DisposeLifetime();
    }

    public void Dispose()
    {
        if (_disposed) return;
        DisposeLifetime();
        _disposed = true;
    }

    private static long ToStopwatchTicks(long milliseconds) => checked(
        milliseconds * Stopwatch.Frequency / 1_000);

    private static long ToMillisecondsTicks(long stopwatchTicks) => checked(
        stopwatchTicks * 1_000 / Stopwatch.Frequency);
}

internal sealed class BotControlDriver
{
    private readonly OrbitCommandProfile _orbit = new();
    private readonly ThirdPersonDirectProfile _thirdPerson = new();
    private readonly IndependentMoveLookProfile _independent = new();

    internal GameLocomotionUpdate EvaluateMovement(
        ClientControlGrammar grammar,
        float x,
        float z,
        in WorldPosition actor,
        long timestampMilliseconds)
    {
        return grammar switch
        {
            ClientControlGrammar.OrbitCommand => EvaluateOrbit(x, z, actor, timestampMilliseconds),
            ClientControlGrammar.ThirdPersonDirect =>
                _thirdPerson.Evaluate(x, z, ClientHorizontalCameraBasis.WorldAxes),
            ClientControlGrammar.IndependentMoveLook => _independent.Evaluate(
                x, z, GamePlayerActions.None, true, false, default).Command.Locomotion,
            _ => throw new InvalidOperationException($"Unsupported stock control grammar {grammar}."),
        };
    }

    internal void Reset()
    {
        _orbit.Reset();
        _thirdPerson.Reset();
        _independent.Reset();
    }

    private GameLocomotionUpdate EvaluateOrbit(
        float x,
        float z,
        in WorldPosition actor,
        long timestampMilliseconds)
    {
        double length = Math.Sqrt((double)x * x + (double)z * z);
        if (length < 0.001d)
        {
            return _orbit.Evaluate(new ClientOrbitCommandSample(
                GamePlayerActions.None, true,
                actor.GlobalXMillimeters, actor.GlobalZMillimeters,
                false, false, false, true, false, false, default,
                timestampMilliseconds)).Command.Locomotion;
        }
        var point = new ClientControlWorldPoint(
            checked(actor.GlobalXMillimeters + (long)Math.Round(x / length * 10_000d)),
            checked(actor.GlobalZMillimeters + (long)Math.Round(z / length * 10_000d)));
        _ = _orbit.Evaluate(new ClientOrbitCommandSample(
            GamePlayerActions.None, true,
            actor.GlobalXMillimeters, actor.GlobalZMillimeters,
            true, true, false, false, false, true, point,
            timestampMilliseconds));
        return _orbit.Evaluate(new ClientOrbitCommandSample(
            GamePlayerActions.None, true,
            actor.GlobalXMillimeters, actor.GlobalZMillimeters,
            true, false, false, false, false, true, point,
            checked(timestampMilliseconds + OrbitCommandProfile.HoldThresholdMilliseconds)))
            .Command.Locomotion;
    }
}

internal sealed class BotUiRenderer : IUiRenderer
{
    private readonly Dictionary<UiViewInstanceId, BotUiHandle> _views = [];

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        var handle = new BotUiHandle(instance, definition);
        _views.Add(instance, handle);
        return handle;
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    {
        if (handle is not BotUiHandle bot || bot.Definition.Handle != definition.Handle)
            throw new InvalidOperationException("The bot UI renderer received a foreign handle.");
    }

    public void Close(IUiRenderHandle handle)
    {
        if (handle is not BotUiHandle bot || !_views.Remove(bot.Instance))
            throw new InvalidOperationException("The bot UI renderer received an unknown close.");
    }

    internal bool TryFind(
        string definitionKey,
        out UiViewInstanceId instance,
        out CompiledUiDefinition? definition)
    {
        foreach (BotUiHandle view in _views.Values)
        {
            if (!string.Equals(view.Definition.Key.Value, definitionKey, StringComparison.Ordinal))
                continue;
            instance = view.Instance;
            definition = view.Definition;
            return true;
        }
        instance = default;
        definition = null;
        return false;
    }

    private sealed record BotUiHandle(
        UiViewInstanceId Instance,
        CompiledUiDefinition Definition) : IUiRenderHandle;
}

internal struct DeterministicRandom
{
    private ulong _state;

    internal DeterministicRandom(ulong seed) => _state = seed == 0 ? 1 : seed;

    internal static ulong Derive(ulong seed, int botId)
    {
        unchecked
        {
            ulong value = seed ^ ((ulong)(botId + 1) * 0x9E3779B97F4A7C15UL);
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }
    }

    internal int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        ulong value = Next();
        return checked((int)(value % (uint)exclusiveMaximum));
    }

    internal float NextSignedUnit() => (NextInt(20_001) - 10_000) / 10_000f;

    private ulong Next()
    {
        unchecked
        {
            ulong value = _state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            _state = value;
            return value * 0x2545F4914F6CDD1DUL;
        }
    }
}

internal sealed record BotRunSummary
{
    public required string Checkpoint { get; init; }
    public required bool Passed { get; init; }
    public required string? Detail { get; init; }
    public required string Endpoint { get; init; }
    public required string Scenario { get; init; }
    public required ulong Seed { get; init; }
    public required int BotCount { get; init; }
    public required int ActiveBots { get; init; }
    public required int AdmittingBots { get; init; }
    public required int ReconnectingBots { get; init; }
    public required int RetiredBots { get; init; }
    public required int ActiveHighWater { get; init; }
    public required int AdmittingHighWater { get; init; }
    public required int ReconnectingHighWater { get; init; }
    public required int FemaleBodies { get; init; }
    public required int MaleBodies { get; init; }
    public required ulong ServerGeneration { get; init; }
    public required int ApplicationSessions { get; init; }
    public required int SuccessfulReconnects { get; init; }
    public required int FreshProofLifetimes { get; init; }
    public required int MovementAttempts { get; init; }
    public required int MovementAccepted { get; init; }
    public required int MovementRejected { get; init; }
    public required int JumpAttempts { get; init; }
    public required int JumpsAccepted { get; init; }
    public required int JumpsRejected { get; init; }
    public required int LocalChatSubmitted { get; init; }
    public required int GlobalChatSubmitted { get; init; }
    public required int LocalChatObserved { get; init; }
    public required int GlobalChatObserved { get; init; }
    public required int NonsenseInputs { get; init; }
    public required int NonsenseCommandsCreated { get; init; }
    public required int SwordInteractionAttempts { get; init; }
    public required int SwordPickupObserved { get; init; }
    public required int SwordEquipObserved { get; init; }
    public required int SwordDropObserved { get; init; }
    public required int InteractionAccepted { get; init; }
    public required int InteractionRejected { get; init; }
    public required int CommandsAttempted { get; init; }
    public required int PrimaryAttempts { get; init; }
    public required int PrimaryAccepted { get; init; }
    public required int PrimaryRejected { get; init; }
    public required int ChunkTransitions { get; init; }
    public required int VerticalTransitions { get; init; }
    public required int LandingRecoveries { get; init; }
    public required int PostRecoveryMovementAccepted { get; init; }
    public required int EdgePressureAttempts { get; init; }
    public required ulong PumpIterations { get; init; }
    public required double PumpLagHighWaterMilliseconds { get; init; }
    public required long WorkingSetHighWaterBytes { get; init; }
    public required double CpuMilliseconds { get; init; }
    public required bool CleanRetirement { get; init; }
    public required int FaultedBots { get; init; }
    public required string[] Faults { get; init; }

    internal static BotRunSummary Create(
        BotOptions options,
        BotClient[] bots,
        bool passed,
        bool cleanRetirement,
        string? detail,
        ulong pumpIterations,
        double pumpLagHighWaterMilliseconds,
        long workingSetHighWaterBytes,
        double cpuMilliseconds,
        int activeHighWater,
        int admittingHighWater,
        int reconnectingHighWater) => new()
    {
        Checkpoint = "PF1",
        Passed = passed,
        Detail = detail,
        Endpoint = options.Endpoint,
        Scenario = options.Scenario,
        Seed = options.Seed,
        BotCount = bots.Length,
        ActiveBots = bots.Count(static bot => bot.IsActive),
        AdmittingBots = bots.Count(static bot => bot.IsAdmitting),
        ReconnectingBots = bots.Count(static bot => bot.IsReconnecting),
        RetiredBots = bots.Count(static bot => bot.IsRetired),
        ActiveHighWater = activeHighWater,
        AdmittingHighWater = admittingHighWater,
        ReconnectingHighWater = reconnectingHighWater,
        FemaleBodies = bots.Count(static bot => bot.Body == GameGuestBody.HumanFemale),
        MaleBodies = bots.Count(static bot => bot.Body == GameGuestBody.HumanMale),
        ServerGeneration = bots.Select(static bot => bot.ServerGenerationId).FirstOrDefault(value => value != 0),
        ApplicationSessions = bots.Sum(static bot => bot.SessionCount),
        SuccessfulReconnects = bots.Sum(static bot => bot.SuccessfulReconnects),
        FreshProofLifetimes = bots.Sum(static bot => bot.FreshProofLifetimes),
        MovementAttempts = bots.Sum(static bot => bot.MovementAttempts),
        MovementAccepted = bots.Sum(static bot => bot.MovementAccepted),
        MovementRejected = bots.Sum(static bot => bot.MovementRejected),
        JumpAttempts = bots.Sum(static bot => bot.JumpAttempts),
        JumpsAccepted = bots.Sum(static bot => bot.JumpsAccepted),
        JumpsRejected = bots.Sum(static bot => bot.JumpsRejected),
        LocalChatSubmitted = bots.Sum(static bot => bot.LocalChatSubmitted),
        GlobalChatSubmitted = bots.Sum(static bot => bot.GlobalChatSubmitted),
        LocalChatObserved = bots.Sum(static bot => bot.LocalChatObserved),
        GlobalChatObserved = bots.Sum(static bot => bot.GlobalChatObserved),
        NonsenseInputs = bots.Sum(static bot => bot.NonsenseInputs),
        NonsenseCommandsCreated = bots.Sum(static bot => bot.NonsenseCommandsCreated),
        SwordInteractionAttempts = bots.Sum(static bot => bot.SwordInteractionAttempts),
        SwordPickupObserved = bots.Sum(static bot => bot.SwordPickupObserved),
        SwordEquipObserved = bots.Sum(static bot => bot.SwordEquipObserved),
        SwordDropObserved = bots.Sum(static bot => bot.SwordDropObserved),
        InteractionAccepted = bots.Sum(static bot => bot.InteractionAccepted),
        InteractionRejected = bots.Sum(static bot => bot.InteractionRejected),
        CommandsAttempted = bots.Sum(static bot => bot.CommandsAttempted),
        PrimaryAttempts = bots.Sum(static bot => bot.PrimaryAttempts),
        PrimaryAccepted = bots.Sum(static bot => bot.PrimaryAccepted),
        PrimaryRejected = bots.Sum(static bot => bot.PrimaryRejected),
        ChunkTransitions = bots.Sum(static bot => bot.ChunkTransitions),
        VerticalTransitions = bots.Sum(static bot => bot.VerticalTransitions),
        LandingRecoveries = bots.Sum(static bot => bot.LandingRecoveries),
        PostRecoveryMovementAccepted = bots.Sum(static bot => bot.PostRecoveryMovementAccepted),
        EdgePressureAttempts = bots.Sum(static bot => bot.EdgePressureAttempts),
        PumpIterations = pumpIterations,
        PumpLagHighWaterMilliseconds = pumpLagHighWaterMilliseconds,
        WorkingSetHighWaterBytes = workingSetHighWaterBytes,
        CpuMilliseconds = cpuMilliseconds,
        CleanRetirement = cleanRetirement,
        FaultedBots = bots.Count(static bot => bot.IsFaulted),
        Faults = bots.Where(static bot => bot.Fault is not null)
            .Select(bot => $"bot={bot.Id}:{bot.Fault}").ToArray(),
    };

    internal string ToEvidenceLine() =>
        $"PF1 BOT HARNESS {(Passed ? "PASS" : "FAIL")} bots={BotCount} seed={Seed} " +
        $"states={ActiveBots}/{AdmittingBots}/{ReconnectingBots}/{RetiredBots} " +
        $"stateHighWater={ActiveHighWater}/{AdmittingHighWater}/{ReconnectingHighWater} " +
        $"generation={ServerGeneration} sessions={ApplicationSessions} reconnects={SuccessfulReconnects} " +
        $"proofLifetimes={FreshProofLifetimes} movement={MovementAccepted}/{MovementRejected}/" +
        $"{MovementAttempts} jumps={JumpsAccepted}/{JumpsRejected}/{JumpAttempts} " +
        $"primary={PrimaryAccepted}/{PrimaryRejected}/{PrimaryAttempts} " +
        $"chat={LocalChatSubmitted}+{GlobalChatSubmitted}/" +
        $"observed={LocalChatObserved}+{GlobalChatObserved} commands={CommandsAttempted} " +
        $"nonsense={NonsenseInputs}/commands={NonsenseCommandsCreated} sword={SwordPickupObserved}/" +
        $"{SwordEquipObserved}/{SwordDropObserved} interactions={InteractionAccepted}/" +
        $"{InteractionRejected} chunks={ChunkTransitions} vertical={VerticalTransitions} " +
        $"landings={LandingRecoveries} recoveryMoves={PostRecoveryMovementAccepted} " +
        $"pump={PumpIterations} lagMs={PumpLagHighWaterMilliseconds:0.###} " +
        $"workingSet={WorkingSetHighWaterBytes} cpuMs={CpuMilliseconds:0.###} " +
        $"retired={CleanRetirement} detail={Detail ?? "none"}";
}

internal readonly record struct BotOptions(
    string Endpoint,
    string Fingerprint,
    int BotCount,
    ulong Seed,
    int DurationMilliseconds,
    string Scenario,
    string CacheRoot,
    string? StartReadyFile,
    string? StartReleaseFile,
    string? ReadyFile,
    string? ReleaseFile,
    string? SummaryFile,
    int AdmissionStaggerMilliseconds,
    bool Verbose)
{
    internal static BotOptions Parse(string[] arguments)
    {
        string? endpoint = null;
        string? fingerprint = null;
        string? cache = null;
        string? startReady = null;
        string? startRelease = null;
        string? ready = null;
        string? release = null;
        string? summary = null;
        int bots = 0;
        ulong seed = 0;
        int duration = 0;
        int stagger = 100;
        bool verbose = false;
        string scenario = "mixed-chaos";
        foreach (string argument in arguments)
        {
            if (argument == "--verbose") { verbose = true; continue; }
            if (TryValue(argument, "--endpoint=", out string? value)) endpoint = value;
            else if (TryValue(argument, "--fingerprint=", out value)) fingerprint = value;
            else if (TryValue(argument, "--bots=", out value) && int.TryParse(value, out int parsedBots)) bots = parsedBots;
            else if (TryValue(argument, "--seed=", out value) && ulong.TryParse(value, out ulong parsedSeed)) seed = parsedSeed;
            else if (TryValue(argument, "--duration=", out value)) duration = ParseDuration(value!);
            else if (TryValue(argument, "--scenario=", out value)) scenario = value!;
            else if (TryValue(argument, "--cache=", out value)) cache = Path.GetFullPath(value!);
            else if (TryValue(argument, "--start-ready-file=", out value)) startReady = Path.GetFullPath(value!);
            else if (TryValue(argument, "--start-release-file=", out value)) startRelease = Path.GetFullPath(value!);
            else if (TryValue(argument, "--ready-file=", out value)) ready = Path.GetFullPath(value!);
            else if (TryValue(argument, "--release-file=", out value)) release = Path.GetFullPath(value!);
            else if (TryValue(argument, "--summary-file=", out value)) summary = Path.GetFullPath(value!);
            else if (TryValue(argument, "--admission-stagger-ms=", out value) && int.TryParse(value, out int parsedStagger)) stagger = parsedStagger;
            else throw new ArgumentException($"Unknown or malformed option '{argument}'.");
        }
        if (!ClientDirectConnectEndpoint.TryParse(endpoint, out _))
            throw new ArgumentException("--endpoint must name one IPv4 Direct Connect endpoint.");
        if (!ClientDeploymentKeyFingerprint.TryParse(fingerprint, out _))
            throw new ArgumentException("--fingerprint must contain one SHA-256 deployment-key fingerprint.");
        if (bots is <= 0 or > 4_096)
            throw new ArgumentException("--bots must be between 1 and 4096.");
        if (seed == 0) throw new ArgumentException("--seed must be a nonzero UInt64.");
        if (duration is < 2_000 or > 86_400_000)
            throw new ArgumentException("--duration must be between 2 seconds and 24 hours.");
        if (!string.Equals(scenario, "mixed-chaos", StringComparison.Ordinal))
            throw new ArgumentException("--scenario currently supports only mixed-chaos.");
        if (cache is null) throw new ArgumentException("--cache is required.");
        if ((startReady is null) != (startRelease is null))
            throw new ArgumentException(
                "--start-ready-file and --start-release-file must be supplied together.");
        if ((ready is null) != (release is null))
            throw new ArgumentException("--ready-file and --release-file must be supplied together.");
        if (stagger is < 0 or > 5_000)
            throw new ArgumentException("--admission-stagger-ms must be between 0 and 5000.");
        return new BotOptions(endpoint!, fingerprint!, bots, seed, duration, scenario,
            cache, startReady, startRelease, ready, release, summary, stagger, verbose);
    }

    private static bool TryValue(string argument, string prefix, out string? value)
    {
        if (!argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = null;
            return false;
        }
        value = argument[prefix.Length..];
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{prefix[..^1]} cannot be empty.");
        return true;
    }

    private static int ParseDuration(string value)
    {
        if (value.EndsWith("ms", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value.AsSpan(0, value.Length - 2), out int milliseconds))
            return milliseconds;
        if (value.EndsWith('s') &&
            double.TryParse(value.AsSpan(0, value.Length - 1),
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            return checked((int)Math.Round(seconds * 1_000d));
        if (TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                out TimeSpan duration))
            return checked((int)duration.TotalMilliseconds);
        throw new FormatException("--duration must be a TimeSpan, seconds (12s), or milliseconds (12000ms).");
    }
}
