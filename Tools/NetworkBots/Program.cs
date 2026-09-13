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
    $"{options.Checkpoint} BOT RUN endpoint={options.Endpoint} bots={options.BotCount} seed={options.Seed} " +
    $"warmupMs={options.WarmupMilliseconds} durationMs={options.DurationMilliseconds} scenario={options.Scenario} " +
    $"inputMode={options.InputMode} sampleHz={options.InputSampleHz} submitHz={options.InputSubmitHz} " +
    $"frames={options.UnreliableFrameCap}/{options.UnreliableAggregationScope} " +
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
    private readonly BotOptions _options;
    private readonly ContentCache _cache;
    private readonly HttpContentPackageSource _source;
    private readonly ContentPackageSourceSnapshot _lifetimeSourceStart;
    private readonly ContentCacheSnapshot _lifetimeCacheStart;
    private readonly BotClient[] _bots;
    private readonly BotProcessTelemetrySampler _processMetrics = new(
        new CurrentBotProcessSampleSource());
    private readonly BotReconnectIdentityObservation[] _reconnectObservers =
        [new(), new()];
    private readonly long _created = Stopwatch.GetTimestamp();
    private long _lastPump;
    private long _scenarioStarted;
    private long _admissionCompleted;
    private long _scenarioCompleted;
    private long _measurementStarted;
    private long _quiescenceStarted;
    private long _quiescenceCompleted;
    private long _completionBarrierStarted;
    private long _completionBarrierCompleted;
    private long _pumpStartIntervalHighWaterTicks;
    private long _pumpDurationHighWaterTicks;
    private ulong _pumpIterations;
    private readonly BotLatencyHistogram _measuredPumpIntervals = new();
    private readonly BotLatencyHistogram _measuredPumpDurations = new();
    private BotWireMeasurement? _wireMeasurement;
    private BotWireMeasurementSummary? _wireSummary;
    private BotClientTelemetryReport[] _clientTelemetry = [];
    private WorldPublicEntityId _singleHotActor;
    private SpatialChunkCoord _singleHotChunk;
    private bool _measurementActive;
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
        _lifetimeSourceStart = source.GetSnapshot();
        _lifetimeCacheStart = cache.GetSnapshot();
        _bots = new BotClient[options.BotCount];
        for (int index = 0; index < _bots.Length; index++)
            _bots[index] = new BotClient(
                options, index, cache, source,
                BotMonotonicClock.AddMilliseconds(
                    _created, checked((long)index * options.AdmissionStaggerMilliseconds)));
        _lastPump = Stopwatch.GetTimestamp();
    }

    internal BotRunSummary Run()
    {
        long admissionDeadline = BotScenarioContract.AdmissionDeadline(
            _created, _options.BotCount, _options.AdmissionStaggerMilliseconds);
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
                    long startDeadline = BotMonotonicClock.AddMilliseconds(
                        Stopwatch.GetTimestamp(), 120_000);
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
                _admissionCompleted = Stopwatch.GetTimestamp();
                if (_options.IsMixedChaos && !WaitForSwordContenders())
                    return Finish(false,
                        "No bot could locate the sword through its ordinary public world state.");
                _scenarioStarted = Stopwatch.GetTimestamp();
                if (_options.Scenario == "single-hot")
                {
                    // Hold most recipient anchors still and make one ordinary actor the sole hot
                    // source. The middle spawn lies in the central shared-interest region, so
                    // this measures temporal churn on one chunk instead of navigation convergence.
                    // Shared spawns alternate X columns. The adjacent even slot is in the
                    // authored flat test chunk (0,1), avoiding fall/recovery churn while every
                    // other bounded spawn remains interested in that central chunk.
                    int moverIndex = (_bots.Length / 2) & ~1;
                    BotClient mover = _bots[moverIndex];
                    WorldPosition first = mover.CurrentPosition;
                    int edge = GameWorldSpatialProfile.WorldChunkEdgeUnits;
                    if (!WorldPosition.TryFromGlobalMillimeters(
                            checked((long)first.Chunk.X * edge + edge / 2),
                            first.YMillimeters,
                            checked((long)first.Chunk.Z * edge + edge / 2),
                            out WorldPosition target))
                        throw new InvalidOperationException(
                            "The single-hot shared chunk center was unrepresentable.");
                    _singleHotActor = mover.CurrentPublicActor;
                    _singleHotChunk = first.Chunk;
                    foreach (BotClient bot in _bots)
                        bot.SetMeasurementTarget(target, bot.Id == moverIndex);
                }
                foreach (BotClient bot in _bots) bot.StartScenario(_scenarioStarted);
                Console.WriteLine(
                    $"{_options.Checkpoint} BOT SWARM ACTIVE bots={_bots.Length} generation=" +
                    $"{_bots[0].ServerGenerationId} sessions={_bots.Sum(static bot => bot.SessionCount)}");
                break;
            }
            if (Stopwatch.GetTimestamp() >= admissionDeadline)
                return Finish(false, "The bot swarm did not complete ordinary admission in time.");
            Thread.Sleep(1);
        }

        if (_options.WarmupMilliseconds != 0)
        {
            long warmupDeadline = BotMonotonicClock.AddMilliseconds(
                _scenarioStarted, _options.WarmupMilliseconds);
            while (Stopwatch.GetTimestamp() < warmupDeadline)
            {
                PumpOnce(allowNewChaos: !_options.IsMixedChaos);
                if (_bots.Any(static bot => bot.IsFaulted))
                    return Finish(false, "A bot faulted during PF2 warmup/positioning.");
                Thread.Sleep(1);
            }
            _scenarioStarted = Stopwatch.GetTimestamp();
            foreach (BotClient bot in _bots) bot.StartScenario(_scenarioStarted);
        }

        if (_options.MeasurementReadyFile is not null)
        {
            BotFiles.WriteAtomic(_options.MeasurementReadyFile, "ready");
            long measurementReleaseDeadline = BotMonotonicClock.AddMilliseconds(
                Stopwatch.GetTimestamp(), 120_000);
            while (!File.Exists(_options.MeasurementReleaseFile))
            {
                PumpOnce(allowNewChaos: false);
                if (_bots.Any(static bot => bot.IsFaulted))
                    return Finish(false, "A bot faulted at the PF2 measurement barrier.");
                if (Stopwatch.GetTimestamp() >= measurementReleaseDeadline)
                    return Finish(false, "The PF2 measurement release marker did not arrive.");
                Thread.Sleep(1);
            }
            _scenarioStarted = Stopwatch.GetTimestamp();
            foreach (BotClient bot in _bots) bot.StartScenario(_scenarioStarted);
        }

        // A measured window owns its generated/submitted/completed input counts. Drain any
        // command sampled during warmup/positioning before resetting cumulative diagnostics.
        long inputQuiescenceDeadline = BotMonotonicClock.AddMilliseconds(
            Stopwatch.GetTimestamp(), 10_000);
        while (_bots.Any(static bot => bot.HasUnexpectedQueueDebt) &&
               Stopwatch.GetTimestamp() < inputQuiescenceDeadline)
        {
            PumpOnce(allowNewChaos: false);
            if (_bots.Any(static bot => bot.IsFaulted))
                return Finish(false, "A bot faulted while quiescing the PF3 input window.");
            Thread.Sleep(1);
        }
        if (_bots.Any(static bot => bot.HasUnexpectedQueueDebt))
            return Finish(false,
                "Input/session debt did not quiesce before measurement: " +
                string.Join("; ", _bots.Where(static bot => bot.HasUnexpectedQueueDebt)
                    .Take(8).Select(static bot => bot.DescribeUnexpectedQueueDebt())));

        _measurementStarted = Stopwatch.GetTimestamp();
        foreach (BotClient bot in _bots) bot.ResetTelemetryWindow();
        _wireMeasurement = new BotWireMeasurement(
            _bots, _source, _cache, _options.DurationMilliseconds, _measurementStarted);
        _processMetrics.BeginWindow(_measurementStarted);
        _measuredPumpIntervals.Clear();
        _measuredPumpDurations.Clear();
        _measurementActive = true;

        long scenarioDeadline = BotMonotonicClock.AddMilliseconds(
            _measurementStarted, _options.DurationMilliseconds);
        while (Stopwatch.GetTimestamp() < scenarioDeadline)
        {
            PumpOnce();
            if (_bots.Any(static bot => bot.IsFaulted))
                return Finish(false, $"A bot faulted during {_options.Scenario}.");
            Thread.Sleep(1);
        }

        // The wall-clock scenario boundary may bisect an ordinary reliable command or proof,
        // especially once many clients share the central pump. Stop creating work and give that
        // already-accepted debt one bounded opportunity to converge before judging cleanliness.
        _scenarioCompleted = Stopwatch.GetTimestamp();
        _measurementActive = false;
        _processMetrics.EndWindow(_scenarioCompleted);
        _wireSummary = _wireMeasurement.Complete(
            _bots, _source, _cache, _scenarioCompleted);
        if (_options.MeasurementCompleteFile is not null)
            BotFiles.WriteAtomic(_options.MeasurementCompleteFile, "complete");
        _quiescenceStarted = _scenarioCompleted;
        long quiescenceDeadline = BotMonotonicClock.AddMilliseconds(
            _quiescenceStarted, 10_000);
        while (_bots.Any(static bot => bot.HasUnexpectedQueueDebt) &&
               Stopwatch.GetTimestamp() < quiescenceDeadline)
        {
            PumpOnce(allowNewChaos: false);
            if (_bots.Any(static bot => bot.IsFaulted))
                return Finish(false, "A bot faulted while quiescing accepted session work.");
            Thread.Sleep(1);
        }
        _quiescenceCompleted = Stopwatch.GetTimestamp();
        // The measured producer window has closed, but obligations accepted during that window
        // may complete during the bounded quiescence tail. Capture the client/scheduler view only
        // after that tail so window-local generated/submitted/completed edge counts cannot report
        // an impossible partial lifetime. Physical wire accounting remains fenced at the explicit
        // scenario boundary above.
        _clientTelemetry = _bots.Select(static bot => bot.GetTelemetryReport()).ToArray();

        string? coverageFailure = ValidateCoverage();
        if (coverageFailure is not null)
            return Finish(false, coverageFailure);

        _completionBarrierStarted = Stopwatch.GetTimestamp();
        if (_options.ReadyFile is not null)
        {
            BotFiles.WriteAtomic(_options.ReadyFile, "ready");
            if (_options.ReleaseFile is null)
                throw new InvalidOperationException("--ready-file requires --release-file.");
            long releaseDeadline = BotMonotonicClock.AddMilliseconds(
                Stopwatch.GetTimestamp(), 120_000);
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
        _completionBarrierCompleted = Stopwatch.GetTimestamp();

        string? finalHealthFailure = ValidateCurrentHealth();
        if (finalHealthFailure is not null)
            return Finish(false, finalHealthFailure);

        return Finish(true, null);
    }

    private void PumpOnce(bool allowNewChaos = true)
    {
        long now = Stopwatch.GetTimestamp();
        long lag = now - _lastPump;
        if (lag > _pumpStartIntervalHighWaterTicks) _pumpStartIntervalHighWaterTicks = lag;
        _lastPump = now;
        if (_measurementActive) _measuredPumpIntervals.RecordTicks(lag);
        for (int index = 0; index < _bots.Length; index++)
            _bots[index].Pump(now, allowNewChaos);
        TrackReconnectIdentity();
        _activeHighWater = Math.Max(
            _activeHighWater, _bots.Count(static bot => bot.IsActive));
        _admittingHighWater = Math.Max(
            _admittingHighWater, _bots.Count(static bot => bot.IsAdmitting));
        _reconnectingHighWater = Math.Max(
            _reconnectingHighWater, _bots.Count(static bot => bot.IsReconnecting));
        _pumpIterations++;
        long completed = Stopwatch.GetTimestamp();
        _pumpDurationHighWaterTicks = Math.Max(
            _pumpDurationHighWaterTicks, completed - now);
        if (_measurementActive) _measuredPumpDurations.RecordTicks(completed - now);
        if (_measurementActive) _wireMeasurement?.Sample(_bots, completed);
        _processMetrics.Sample(completed);
    }

    private string? ValidateCoverage()
    {
        string? healthFailure = ValidateCurrentHealth();
        if (healthFailure is not null) return healthFailure;
        if (_bots.Any(static bot => !bot.IsActive)) return "Not every bot was active after churn.";
        ulong generation = _bots[0].ServerGenerationId;
        if (generation == 0 || _bots.Any(bot => bot.ServerGenerationId != generation))
            return "Bots did not converge on one authoritative server generation.";
        if (_bots.Length > 1 && (!_bots.Any(static bot => bot.Body == GameGuestBody.HumanFemale) ||
            !_bots.Any(static bot => bot.Body == GameGuestBody.HumanMale)))
            return "The swarm did not exercise mixed X/Y bodies.";
        if (_wireSummary is null || !_wireSummary.PhysicalTransportReconciled ||
            !_wireSummary.LogicalMessageReconciled)
            return "PF2 physical/logical traffic accounting did not reconcile.";
        if (_options.Checkpoint is "PF4A" or "PF4B" or "PF4C" or "PF4D" or "PF4E")
        {
            BotActionOutcomeReport[] outcomes =
                BotRunSummary.CreateActionOutcomes(_bots, _clientTelemetry);
            BotActionOutcomeReport? invalid = null;
            foreach (BotActionOutcomeReport outcome in outcomes)
                if (!outcome.Conserved || !outcome.Terminal)
                {
                    invalid = outcome;
                    break;
                }
            if (invalid is not null)
                return $"PF4A action conservation failed for {invalid.Value.Action}: " +
                    $"unexplained={invalid.Value.Unexplained}, " +
                    $"pendingUnsent={invalid.Value.PendingUnsent}, " +
                    $"pendingCompletion={invalid.Value.PendingCompletion}.";
        }
        if (!_options.IsMixedChaos)
        {
            if (_options.Scenario is "moving" or "crowd" or "single-hot" or "multi-hot" &&
                _bots.Sum(static bot => bot.MovementAccepted) == 0)
                return $"The {_options.Scenario} scenario produced no authoritative movement.";
            if (_options.Scenario == "single-hot")
            {
                BotClient mover = _bots[(_bots.Length / 2) & ~1];
                if (!_singleHotActor.IsValid || mover.CurrentPublicActor != _singleHotActor ||
                    mover.CurrentPosition.Chunk != _singleHotChunk)
                    return "The single-hot source actor did not retain its chunk/lifetime: " +
                        $"expectedActor={_singleHotActor} actualActor={mover.CurrentPublicActor} " +
                        $"expectedChunk={_singleHotChunk} actualPosition={mover.CurrentPosition}.";
                if (_bots.Any(bot => !bot.ContainsPublic(_singleHotActor)))
                    return "A single-hot recipient did not retain the shared hot actor.";
            }
            if (_options.Scenario == "sustained-input")
            {
                ClientWorldInputScheduleSnapshot[] schedules = _bots
                    .Select(static bot => bot.GetTelemetryReport().InputScheduler).ToArray();
                if (_options.InputMode is BotInputMode.Compliant or BotInputMode.HardLimit)
                {
                    if (schedules.Any(static value => value.SamplesObserved == 0 ||
                            value.CommandsSubmitted == 0))
                        return "The sustained-input scenario did not exercise every stock scheduler.";
                    if (schedules.Any(static value => value.CapacityRejections != 0 ||
                        value.EdgeObligationsGenerated != value.EdgeObligationsSubmitted ||
                        value.EdgeObligationsGenerated != value.EdgeObligationsCompleted ||
                        value.ReleaseOrStopGenerated != value.ReleaseOrStopSubmitted ||
                        value.ReleaseOrStopGenerated != value.ReleaseOrStopCompleted))
                        return "A bounded stock input scheduler lost or stranded an action/release edge.";
                    if (_bots.Any(static bot => bot.RateLimitedCommandsObserved != 0))
                        return "A compliant stock scheduler was rate-limited by the authority.";
                }
                if ((_options.InputMode is BotInputMode.OverLimit or BotInputMode.BurstOver) &&
                    !_bots.Any(static bot => bot.RateLimitedCommandsObserved != 0))
                    return "The non-compliant input mode did not produce bounded rate-limit outcomes.";
                if (_options.InputMode == BotInputMode.StaleSession &&
                    _bots.Any(static bot => bot.SuccessfulReconnects != 1 ||
                        bot.StaleSessionReplayAttempts != 1 ||
                        bot.StaleSessionReplaysQueued != 1 ||
                        bot.StaleSessionReplaysFenced != 1))
                    return "An old application/control-session command was not explicitly replayed and fenced.";
            }
            return null;
        }
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
        if (_bots.Sum(static bot => bot.ChunkCrossings) < 2 ||
            _bots.Sum(static bot => bot.DistinctChunksVisited) < 2 ||
            _bots.Sum(static bot => bot.VerticalTransitions) < 2 ||
            !_bots.Any(static bot => bot.SawAirborne) ||
            _bots.Sum(static bot => bot.EdgePressureAttempts) == 0 ||
            _bots.Sum(static bot => bot.EdgeJumpCommandsQueued) == 0 ||
            _bots.Sum(static bot => bot.LandingRecoveries) == 0 ||
            _bots.Sum(static bot => bot.PostRecoveryMovementAccepted) == 0)
            return "Chunk/AOI and edge/fall transition pressure was incomplete.";
        if (_bots.Any(static bot => bot.ChatObservationGaps != 0))
            return "A bot fell behind its bounded Chat observation cursor.";
        if (_bots.Sum(static bot => bot.CancelledCommands) != 0)
            return "The correctness scenario cancelled an outstanding command outcome.";
        if (_reconnectObservers.Any(static observer => !observer.Converged))
            return "Observer clients did not retire the exact old actor identity and acquire its replacement.";
        if (_bots.Any(static bot => bot.HasUnexpectedQueueDebt))
            return "A bot retained unexpected session queue debt at the acceptance boundary.";
        return null;
    }

    private string? ValidateCurrentHealth()
    {
        foreach (BotClient bot in _bots)
        {
            string? failure = bot.GetCurrentHealthFailure();
            if (failure is not null) return $"Bot {bot.Id:D4} is unhealthy: {failure}";
        }
        return null;
    }

    private void TrackReconnectIdentity()
    {
        if (!_options.IsMixedChaos) return;
        BotClient subject = _bots[^1];
        WorldPublicEntityId oldIdentity = subject.PriorPublicActor.IsValid
            ? subject.PriorPublicActor
            : subject.CurrentPublicActor;
        if (!oldIdentity.IsValid) return;
        for (int index = 0; index < _reconnectObservers.Length; index++)
        {
            BotClient observer = _bots[index];
            if (subject.SuccessfulReconnects == 0)
                _reconnectObservers[index].ObserveBefore(
                    observer.ContainsPublic(oldIdentity) &&
                    observer.ContainsPublicPresentation(
                        oldIdentity, $"PF1 Bot {subject.Id:D4}"));
            else
                _reconnectObservers[index].ObserveAfter(
                    observer.ContainsPublic(oldIdentity) ||
                    observer.ContainsPublicPresentation(oldIdentity),
                    subject.CurrentPublicActor.IsValid &&
                    subject.CurrentPublicActor != oldIdentity &&
                    observer.ContainsPublic(subject.CurrentPublicActor) &&
                    observer.ContainsPublicPresentation(
                        subject.CurrentPublicActor, $"PF1 Bot {subject.Id:D4}"));
        }
    }

    private bool WaitForSwordContenders()
    {
        long deadline = BotMonotonicClock.AddMilliseconds(Stopwatch.GetTimestamp(), 10_000);
        do
        {
            if (TrySelectSwordContenders()) return true;
            PumpOnce(allowNewChaos: false);
            if (_bots.Any(static bot => bot.IsFaulted)) return false;
            Thread.Sleep(1);
        }
        while (Stopwatch.GetTimestamp() < deadline);
        return TrySelectSwordContenders();
    }

    private bool TrySelectSwordContenders()
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
        if (candidates.Length == 0) return false;
        for (int index = 0; index < Math.Min(2, candidates.Length); index++)
            candidates[index].Bot.SetSwordContender();
        return true;
    }

    private BotRunSummary Finish(bool requestedPass, string? detail)
    {
        long retirementStarted = Stopwatch.GetTimestamp();
        foreach (BotClient bot in _bots) bot.BeginFinalRetirement(retirementStarted);
        long deadline = BotMonotonicClock.AddMilliseconds(retirementStarted, 20_000);
        while (_bots.Any(static bot => !bot.IsRetired) && Stopwatch.GetTimestamp() < deadline)
        {
            PumpOnce(allowNewChaos: false);
            Thread.Sleep(1);
        }
        bool clean = _bots.All(static bot => bot.IsRetired);
        bool pass = requestedPass && clean && _bots.All(static bot => !bot.IsFaulted);
        detail ??= clean ? null : "Not every bot retired cleanly.";
        long completed = Stopwatch.GetTimestamp();
        _processMetrics.Sample(completed, force: true);
        return BotRunSummary.Create(
            _options, _bots, pass, clean, detail, _pumpIterations,
            BotMonotonicClock.TicksToFloatingMilliseconds(_pumpStartIntervalHighWaterTicks),
            BotMonotonicClock.TicksToFloatingMilliseconds(_pumpDurationHighWaterTicks),
            _processMetrics.WorkingSetHighWaterBytes, _processMetrics.CpuMilliseconds,
            _processMetrics.Samples,
            _activeHighWater, _admittingHighWater, _reconnectingHighWater,
            _reconnectObservers.Count(static observer => observer.Converged),
            CreatePhaseDurations(retirementStarted, completed),
            _wireSummary,
            _clientTelemetry,
            BotHttpBodyTrafficReport.Delta(_lifetimeSourceStart, _source.GetSnapshot()),
            BotCacheTrafficReport.Delta(_lifetimeCacheStart, _cache.GetSnapshot()),
            new BotPumpWindowSummary(
                _measuredPumpIntervals.Count,
                _measuredPumpIntervals.Percentile(0.50),
                _measuredPumpIntervals.Percentile(0.95),
                _measuredPumpIntervals.Percentile(0.99),
                _measuredPumpIntervals.MaximumMilliseconds,
                _measuredPumpDurations.Percentile(0.50),
                _measuredPumpDurations.Percentile(0.95),
                _measuredPumpDurations.Percentile(0.99),
                _measuredPumpDurations.MaximumMilliseconds,
                _processMetrics.WindowWorkingSetHighWaterBytes,
                _processMetrics.WindowCpuMilliseconds,
                _processMetrics.WindowSamples,
                _processMetrics.WindowManagedHeapHighWaterBytes,
                _processMetrics.WindowAllocatedBytes,
                _processMetrics.WindowGen0Collections,
                _processMetrics.WindowGen1Collections,
                _processMetrics.WindowGen2Collections,
                _processMetrics.WindowThreadHighWater));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (BotClient bot in _bots) bot.Dispose();
        _processMetrics.Dispose();
    }

    private BotRunPhaseDurations CreatePhaseDurations(long retirementStarted, long completed) => new(
        Elapsed(_created, _admissionCompleted),
        _options.WarmupMilliseconds,
        Elapsed(_scenarioStarted, _scenarioCompleted),
        Elapsed(_quiescenceStarted, _quiescenceCompleted),
        Elapsed(_completionBarrierStarted, _completionBarrierCompleted),
        Elapsed(retirementStarted, completed),
        Elapsed(_created, completed));

    private static long Elapsed(long start, long end) => start == 0 || end < start
        ? 0
        : BotMonotonicClock.TicksToMilliseconds(end - start);
}

internal readonly record struct BotRunPhaseDurations(
    long AdmissionMilliseconds,
    long WarmupMilliseconds,
    long ScenarioMilliseconds,
    long QuiescenceMilliseconds,
    long CompletionBarrierMilliseconds,
    long RetirementMilliseconds,
    long TotalMilliseconds);

internal readonly record struct BotPumpWindowSummary(
    ulong Samples,
    double IntervalP50Milliseconds,
    double IntervalP95Milliseconds,
    double IntervalP99Milliseconds,
    double IntervalMaximumMilliseconds,
    double DurationP50Milliseconds,
    double DurationP95Milliseconds,
    double DurationP99Milliseconds,
    double DurationMaximumMilliseconds,
    long WorkingSetHighWaterBytes,
    double CpuMilliseconds,
    int ProcessSamples,
    long ManagedHeapHighWaterBytes,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadHighWater);

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

internal enum BotInputMode : byte
{
    Compliant = 1,
    HardLimit = 2,
    OverLimit = 3,
    BurstWithin = 4,
    BurstOver = 5,
    StaleSession = 6,
}

internal enum BotEdgePressureState : byte
{
    None = 0,
    MovementQueued = 1,
    Complete = 2,
}

internal sealed class BotClient : IDisposable
{
    private const int MaximumResidentChunks = 192;
    private const int MaximumInitialConnectRetries = 8;
    private readonly BotOptions _options;
    private readonly ContentCache _cache;
    private readonly HttpContentPackageSource _source;
    private readonly string _stateRoot;
    private readonly ClientSemanticChunkInfo[] _chunkScratch =
        new ClientSemanticChunkInfo[MaximumResidentChunks];
    private readonly ClientChatOccurrence[] _chatScratch = new ClientChatOccurrence[256];
    private readonly BotChunkObservation _chunkObservation = new(MaximumResidentChunks);
    private readonly BotPendingCommandTracker _pendingCommand = new();
    private readonly BotActionAccounting _actionAccounting = new();
    private readonly BotControlDriver _controls = new();
    private DeterministicRandom _random;
    private BotChatObservationCursor _chatObservation;
    private ClientDirectConnectSession? _connection;
    private RemoteClientProfileClient? _profile;
    private RemotePlayableSession? _session;
    private BotUiRenderer? _renderer;
    private ResolvedClientSettings? _settings;
    private ClientWorldInputScheduler? _inputScheduler;
    private long _startDue;
    private long _scenarioStarted;
    private long _nextChaos;
    private long _nextNavigation;
    private long _nextSwordAttempt;
    private long _nextInputSample;
    private long _drainDeadline;
    private long _lastPump;
    private long _serviceIntervalHighWaterTicks;
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
    private BotEdgePressureState _edgePressure;
    private ulong _priorApplicationEpoch;
    private WorldPublicEntityId _priorPublicActor;
    private WorldPublicEntityId _currentPublicActor;
    private ClientPublicActorVerticalPhase _lastVertical;
    private string? _fault;
    private bool _measurementOriginSet;
    private long _measurementOriginX;
    private long _measurementOriginZ;
    private long _lastMeasurementJumpSecond = -1;
    private bool _hasSharedMeasurementTarget;
    private bool _isSingleHotMover;
    private long _sharedMeasurementTargetX;
    private long _sharedMeasurementTargetZ;
    private UdpPayloadTrafficSnapshot _retiredUdpTraffic;
    private NetworkTrafficSnapshot? _retiredSecureTraffic;
    private bool _trafficCapturedForCurrentLifetime;
    private ulong _rateLimitedCommandsObserved;
    private ulong _lastRateLimitedCommands;
    private GameControlSessionEpoch _priorControlSession;
    private bool _staleReplayQueued;
    private bool _staleReplayFenced;
    private long _staleReplayQueuedAt;
    private ulong _staleReplayNewLifetimeProcessedSequence;
    private readonly byte[] _staleReplayScratch =
        new byte[RemotePlayableCommandMessageCodec.EncodedSize];

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
        SuccessfulReconnects == 0 && State is not (BotLifetimeState.Retired or
            BotLifetimeState.Faulted);
    internal bool IsRetired => State == BotLifetimeState.Retired || State == BotLifetimeState.Faulted;
    internal bool HasReachedActive => _hasReachedActive;
    internal ulong ServerGenerationId { get; private set; }
    internal int SessionCount { get; private set; }
    internal int InitialConnectRetries { get; private set; }
    internal int SuccessfulReconnects { get; private set; }
    internal int FreshProofLifetimes { get; private set; }
    internal ulong RateLimitedCommandsObserved => _rateLimitedCommandsObserved;
    internal int StaleSessionReplayAttempts { get; private set; }
    internal int StaleSessionReplaysQueued { get; private set; }
    internal int StaleSessionReplaysFenced => _staleReplayFenced ? 1 : 0;
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
    internal int SyntheticInputSamples { get; private set; }
    internal int SwordInteractionAttempts { get; private set; }
    internal int SwordPickupObserved { get; private set; }
    internal int SwordEquipObserved { get; private set; }
    internal int SwordDropObserved { get; private set; }
    internal int InteractionAccepted { get; private set; }
    internal int InteractionRejected { get; private set; }
    internal int InteractionAttempts { get; private set; }
    internal int CommandSubmitCalls { get; private set; }
    internal int CommandsQueued { get; private set; }
    internal int PrimaryAttempts { get; private set; }
    internal int PrimaryAccepted { get; private set; }
    internal int PrimaryRejected { get; private set; }
    internal int UiOpenAttempts { get; private set; }
    internal int UiOpenAccepted { get; private set; }
    internal int DistinctChunksVisited => _chunkObservation.DistinctChunks;
    internal int ChunkCrossings => _chunkObservation.Crossings;
    internal bool DistinctChunkCoverageSaturated => _chunkObservation.DistinctCoverageSaturated;
    internal int VerticalTransitions { get; private set; }
    internal int LandingRecoveries { get; private set; }
    internal int PostRecoveryMovementAccepted { get; private set; }
    internal int EdgePressureAttempts { get; private set; }
    internal int EdgeJumpCommandsQueued { get; private set; }
    internal int MovementCancelled { get; private set; }
    internal int JumpCancelled { get; private set; }
    internal int InteractionCancelled { get; private set; }
    internal int PrimaryCancelled { get; private set; }
    internal int UiOpenCancelled { get; private set; }
    internal int CancelledCommands => MovementCancelled + JumpCancelled +
        InteractionCancelled + PrimaryCancelled + UiOpenCancelled;
    internal ulong ChatObservationGaps => _chatObservation.ObservationGaps;
    internal double ServiceIntervalHighWaterMilliseconds =>
        BotMonotonicClock.TicksToFloatingMilliseconds(_serviceIntervalHighWaterTicks);
    internal bool SawAirborne => _sawAirborne;
    internal bool StaleActorObservedAfterReconnect { get; private set; }
    internal WorldPublicEntityId PriorPublicActor => _priorPublicActor;
    internal WorldPublicEntityId CurrentPublicActor => _currentPublicActor;
    internal string? Fault => _fault;
    internal WorldPosition CurrentPosition =>
        _session?.GetSnapshot().Playable.Position ?? default;

    internal void SetMeasurementTarget(in WorldPosition target, bool isSingleHotMover)
    {
        _sharedMeasurementTargetX = target.GlobalXMillimeters;
        _sharedMeasurementTargetZ = target.GlobalZMillimeters;
        _hasSharedMeasurementTarget = true;
        _isSingleHotMover = isSingleHotMover;
    }

    internal BotActionAccountingSnapshot GetActionAccounting(BotCommandKind kind) =>
        _actionAccounting.Snapshot(kind);

    internal int GetPendingActionCompletion(BotCommandKind kind) =>
        kind != BotCommandKind.Movement && _pendingCommand.Pending.Kind == kind ? 1 : 0;

    internal BotCumulativeTrafficSnapshot GetCumulativeTrafficSnapshot()
    {
        UdpPayloadTrafficSnapshot physical = _retiredUdpTraffic;
        NetworkTrafficSnapshot? secure = _retiredSecureTraffic;
        ConnectionId current = default;
        ulong applicationEpoch = 0;
        if (_connection is not null &&
            _connection.TryGetTrafficSnapshot(out HostedUdpConnectionTrafficSnapshot traffic))
        {
            current = traffic.Connection;
            physical = physical.Add(traffic.UdpPayload);
            if (traffic.SecureProtocol is not null)
                secure = secure is null
                    ? traffic.SecureProtocol
                    : NetworkTrafficSnapshot.Add(secure, traffic.SecureProtocol);
            applicationEpoch = _connection.GetSnapshot().ApplicationSessionEpoch;
        }
        return new(current, applicationEpoch, SessionCount, physical, secure);
    }

    internal BotClientTelemetryReport GetTelemetryReport()
    {
        NetworkServiceTimingSnapshot network = default;
        NetworkProtocolSnapshot protocol = default;
        if (_connection is not null) _ = _connection.TryGetTelemetrySnapshot(out network);
        if (_connection is not null) _ = _connection.TryGetProtocolSnapshot(out protocol);
        return new BotClientTelemetryReport(
            Id,
            _connection?.GetSnapshot().Connection ?? default,
            network,
            protocol,
            _session?.GetTimingSnapshot() ?? default,
            _inputScheduler?.GetSnapshot() ?? default);
    }

    internal void ResetTelemetryWindow()
    {
        if (_connection is null || _session is null || State != BotLifetimeState.Active)
            throw new InvalidOperationException(
                "A bot telemetry window requires one active application lifetime.");
        _connection.ConfigureTelemetry(_options.TelemetryLevel);
        _session.ConfigureTelemetry(_options.TelemetryLevel);
        // Movement is latest-state input on an unreliable lane. Its accounting terminates at
        // scheduler submission, so a lost final warmup sample is neither delivery debt nor a
        // reason to poison the synchronized measurement window.
        _pendingCommand.TryRetireLatestMovement(out _);
        _inputScheduler?.ResetTelemetryWindow();
        _actionAccounting.Reset();
        // This counter intentionally describes the synchronized measurement
        // window, not admission or scenario-positioning work. It is distinct
        // from commands submitted because an input sample may be unmapped or
        // suppressed before it becomes an application command.
        SyntheticInputSamples = 0;
    }

    internal bool HasUnexpectedQueueDebt
    {
        get
        {
            if (_session is null || State != BotLifetimeState.Active) return false;
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
            return _pendingCommand.HasPendingExactCompletion ||
                snapshot.Playable.CommandRetainedLocally ||
                snapshot.Chat.HasPendingProof || snapshot.Content.ProofRetained ||
                (_inputScheduler is not null &&
                    (_inputScheduler.GetSnapshot().ContinuousStatePending ||
                     _inputScheduler.GetSnapshot().PendingObligations != 0 ||
                     _inputScheduler.GetSnapshot().SubmittedCompletionDebt != 0));
        }
    }

    internal string DescribeUnexpectedQueueDebt()
    {
        if (_session is null || State != BotLifetimeState.Active)
            return $"bot={Id}:inactive";
        RemotePlayableSessionSnapshot session = _session.GetSnapshot();
        ClientWorldInputScheduleSnapshot input = _inputScheduler?.GetSnapshot() ?? default;
        return $"bot={Id}:pendingCommand={_pendingCommand.Pending.Kind}/" +
            $"{_pendingCommand.Pending.Sequence},playableRetained=" +
            $"{session.Playable.CommandRetainedLocally},chatProof={session.Chat.HasPendingProof}," +
            $"contentProof={session.Content.ProofRetained},continuous=" +
            $"{input.ContinuousStatePending},obligations={input.PendingObligations}," +
            $"completionDebt={input.SubmittedCompletionDebt},submitted=" +
            $"{input.CommandsSubmitted},edgeCompleted={input.EdgeObligationsCompleted}," +
            $"releaseCompleted={input.ReleaseOrStopCompleted}";
    }

    internal void StartScenario(long timestamp)
    {
        _scenarioStarted = timestamp;
        _nextChaos = BotMonotonicClock.AddMilliseconds(
            timestamp, 6_000 + _random.NextInt(500));
        _nextNavigation = timestamp;
        _nextSwordAttempt = BotMonotonicClock.AddMilliseconds(timestamp, 100);
        _nextInputSample = timestamp;
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
        if (_lastPump != 0)
            _serviceIntervalHighWaterTicks = Math.Max(
                _serviceIntervalHighWaterTicks, now - _lastPump);
        _lastPump = now;
        try
        {
            if (State is BotLifetimeState.Pending or BotLifetimeState.ReconnectDelay)
            {
                if (_finalRetirement)
                {
                    RetireWithoutConnection();
                    return;
                }
                if (now < _startDue) return;
                StartLifetime();
            }

            _connection!.Pump();
            ClientDirectConnectSnapshot connection = _connection.GetSnapshot();
            if (connection.State is ClientDirectConnectState.Failed)
            {
                if (!_hasReachedActive && InitialConnectRetries < MaximumInitialConnectRetries &&
                    connection.Failure is ClientDirectConnectFailure.DiscoveryFailed or
                        ClientDirectConnectFailure.HandshakeFailed)
                {
                    RetryInitialConnect(now);
                    return;
                }
                Fail($"Direct Connect failed: {connection.Failure}: {connection.Detail}");
                return;
            }
            if (BotLifecyclePolicy.IsUnexpectedTransportLoss(State, connection.State))
            {
                Fail(
                    $"Direct Connect transport became {connection.State} outside intentional retirement: " +
                    $"{connection.Failure}: {connection.Detail}");
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
                _session.ConfigureTelemetry(_options.TelemetryLevel);
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
            if (BotLifecyclePolicy.IsApplicationUnhealthy(State, in snapshot))
            {
                Fail(
                    $"The admitted client application became unhealthy: playable={snapshot.Playable.State}, " +
                    $"ui={snapshot.Ui.State}.");
                return;
            }
            if (!_finalRetirement && !_guestSubmitted &&
                snapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready)
            {
                string name = $"PF1 Bot {Id:D4}";
                if (_session.TrySubmitGuest(new GameGuestAdmissionRequest(Body, name)))
                    _guestSubmitted = true;
            }
            if (snapshot.Playable.State != RemotePlayableClientState.Active) return;

            if (_inputScheduler is null)
            {
                RemoteServerPerformanceProfile performance = _session.Performance.Profile;
                int recommended = _options.InputMode == BotInputMode.HardLimit
                    ? performance.MaximumInputHz
                    : _options.InputSubmitHz == 0
                        ? performance.RecommendedInputHz
                        : _options.InputSubmitHz;
                if (_options.InputMode == BotInputMode.Compliant &&
                    recommended > performance.MaximumInputHz)
                {
                    Fail($"Requested stock submit rate {recommended}Hz exceeds the authenticated " +
                        $"server maximum {performance.MaximumInputHz}Hz.");
                    return;
                }
                _inputScheduler = new ClientWorldInputScheduler(new ClientInputSubmissionPolicy(
                    performance.MaximumInputHz,
                    Math.Min(recommended, performance.MaximumInputHz),
                    performance.InputBurstCapacity,
                    performance.InputBurstWindowMilliseconds));
            }
            _inputScheduler.ObserveCompleted(snapshot.Playable.LastProcessedCommandSequence);
            _ = _inputScheduler.TryPump(now, TrySubmitScheduledCommand);

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
            _currentPublicActor = snapshot.Playable.PublicActor;
            Observe(snapshot);
            if (snapshot.Playable.RateLimitedCommands > _lastRateLimitedCommands)
            {
                _rateLimitedCommandsObserved +=
                    snapshot.Playable.RateLimitedCommands - _lastRateLimitedCommands;
                _lastRateLimitedCommands = snapshot.Playable.RateLimitedCommands;
            }
            if (!_proofCounted && snapshot.Content.ProofsQueued > 0 &&
                _profile.GetSnapshot().ProofsQueued > 0 &&
                snapshot.Performance.ProofsQueued > 0)
            {
                FreshProofLifetimes++;
                _proofCounted = true;
            }
            if (_options.InputMode == BotInputMode.StaleSession &&
                DriveStaleSessionReplay(now, in snapshot))
                return;
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
        if (_finalRetirement)
        {
            RetireWithoutConnection();
            return;
        }
        _pendingCommand.TryRetireLatestMovement(out _);
        _pendingCommand.RequireClearForNewLifetime();
        Directory.CreateDirectory(_stateRoot);
        ClientServerTrustFileStore store = TrustStore();
        ClientServerTrustLoadResult loaded = store.Load();
        _connection = new ClientDirectConnectSession(
            loaded.Registry,
            store,
            NetworkProtocolLimits.Default with
            {
                MaximumUnreliableFramesPerDatagram = _options.UnreliableFrameCap,
                UnreliableAggregationScope = _options.UnreliableAggregationScope,
            });
        _connection.ConfigureTelemetry(_options.TelemetryLevel);
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
        _inputScheduler = null;
        _guestSubmitted = false;
        _proofCounted = false;
        _currentPublicActor = default;
        _lastVertical = ClientPublicActorVerticalPhase.None;
        _lastRateLimitedCommands = 0;
        _trafficCapturedForCurrentLifetime = false;
        _controls.Reset();
    }

    private void RetryInitialConnect(long now)
    {
        if (_finalRetirement)
        {
            RetireWithoutConnection();
            return;
        }
        InitialConnectRetries++;
        string? cleanupFailure = DisposeLifetime();
        if (cleanupFailure is not null)
        {
            Fail(cleanupFailure);
            return;
        }
        State = BotLifetimeState.Pending;
        int delayMilliseconds = 500 + ((Id + InitialConnectRetries) & 7) * 100;
        _startDue = BotMonotonicClock.AddMilliseconds(now, delayMilliseconds);
    }

    private void Observe(in RemotePlayableSessionSnapshot snapshot)
    {
        RemotePlayableClientSnapshot playable = snapshot.Playable;
        if (_pendingCommand.TryComplete(
                playable.ApplicationSessionEpoch,
                playable.LastProcessedCommandSequence,
                out BotPendingCommand completed))
        {
            RemotePlayableActionStatus result = completed.Kind switch
            {
                BotCommandKind.Movement => playable.Locomotion,
                BotCommandKind.Jump => playable.Jump,
                BotCommandKind.Interaction => playable.Interaction,
                BotCommandKind.Primary => playable.Primary,
                _ => RemotePlayableActionStatus.Accepted,
            };
            bool accepted = result == RemotePlayableActionStatus.Accepted;
            if (completed.Kind != BotCommandKind.Movement)
                _actionAccounting.Completed(completed.Kind, result);
            switch (completed.Kind)
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
                case BotCommandKind.UiOpen: UiOpenAccepted++; break;
            }
        }

        _chunkObservation.Observe(playable.Position.Chunk);
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
            _chatObservation.Observe(
                playable.ApplicationSessionEpoch,
                _chatScratch.AsSpan(0, count),
                $"PF1:{_options.Seed}:",
                out int local,
                out int global);
            LocalChatObserved += local;
            GlobalChatObserved += global;
        }

        ObserveInventory();
        if (_priorPublicActor.IsValid && SuccessfulReconnects != 0 &&
            ContainsPublic(_priorPublicActor))
            StaleActorObservedAfterReconnect = true;
    }

    private void RunScenario(long now, in RemotePlayableSessionSnapshot snapshot)
    {
        switch (_options.Scenario)
        {
            case "quiet":
                return;
            case "moving":
                RunMovingMeasurement(now, snapshot.Playable.Position, crowd: false);
                return;
            case "crowd":
                RunMovingMeasurement(now, snapshot.Playable.Position, crowd: true);
                return;
            case "single-hot":
                RunSingleHotChunkMeasurement(now, snapshot.Playable.Position);
                return;
            case "multi-hot":
                RunMovingMeasurement(now, snapshot.Playable.Position, crowd: false);
                return;
            case "mixed-chaos":
                RunMixedChaos(now, snapshot);
                return;
            case "sustained-input":
                RunSustainedInput(now, snapshot.Playable.Position);
                return;
            default:
                throw new InvalidOperationException($"Unknown bot scenario '{_options.Scenario}'.");
        }
    }

    private void RunSustainedInput(long now, in WorldPosition position)
    {
        if (now < _nextInputSample) return;
        int sampleHz = _options.InputSampleHz == 0 ? 120 : _options.InputSampleHz;
        long interval = Math.Max(1, Stopwatch.Frequency / sampleHz);
        _nextInputSample = BotMonotonicClock.AdvanceFixedDeadline(
            _nextInputSample, now, interval);
        SyntheticInputSamples++;

        long elapsedMilliseconds = BotMonotonicClock.TicksToMilliseconds(now - _scenarioStarted);
        long phase = elapsedMilliseconds / 500;
        bool stop = phase % 8 == 7;
        float x = stop ? 0 : phase % 4 switch { 0 => 1f, 1 => .7f, 2 => -1f, _ => -.7f };
        float z = stop ? 0 : phase % 4 switch { 0 => .2f, 1 => 1f, 2 => -.2f, _ => -1f };
        GameLocomotionUpdate locomotion = stop
            ? GameLocomotionUpdate.Stop
            : _controls.EvaluateMovement(
                _settings!.ControlGrammar, x, z, position, elapsedMilliseconds);

        bool jumpEdge = elapsedMilliseconds > 0 &&
            elapsedMilliseconds / 2_000 != (elapsedMilliseconds - Math.Max(1,
                1_000 / sampleHz)) / 2_000;
        GamePlayerActions actions = jumpEdge ? GamePlayerActions.Jump : GamePlayerActions.None;
        var intent = new ClientWorldCommandIntent(locomotion, actions, true);
        MovementAttempts++;
        if (jumpEdge) JumpAttempts++;
        _actionAccounting.Generated(BotCommandKind.Movement);
        if (jumpEdge) _actionAccounting.Generated(BotCommandKind.Jump);

        if (_options.InputMode is BotInputMode.OverLimit or BotInputMode.BurstWithin or
                BotInputMode.BurstOver)
        {
            int withinSecond = checked((int)(elapsedMilliseconds % 1_000));
            RemoteServerPerformanceProfile performance = _session!.Performance.Profile;
            bool shouldSubmit = _options.InputMode switch
            {
                BotInputMode.BurstWithin => withinSecond <
                    Math.Max(1, (int)performance.InputBurstCapacity) * 1_000 / sampleHz,
                BotInputMode.BurstOver => withinSecond <
                    Math.Min(1_000, (performance.InputBurstCapacity + 3) * 1_000 / sampleHz),
                _ => true,
            };
            if (shouldSubmit) _ = TrySubmitNonCompliant(in intent);
            return;
        }

        var context = new ClientWorldCommandContext(
            default, ClientWorldCommandRequestFlags.None, 0, 0,
            (ulong)(jumpEdge ? BotCommandKind.Jump : BotCommandKind.None));
        ClientWorldInputScheduleSnapshot before = _inputScheduler!.GetSnapshot();
        if (_inputScheduler.TryObserve(intent, context, now))
        {
            _actionAccounting.Admitted(BotCommandKind.Movement);
            if (jumpEdge)
            {
                ClientWorldInputScheduleSnapshot observed = _inputScheduler.GetSnapshot();
                if (observed.EdgeObligationsGenerated > before.EdgeObligationsGenerated)
                    _actionAccounting.Admitted(BotCommandKind.Jump);
                else
                    _actionAccounting.NoNewEdge(BotCommandKind.Jump);
            }
            _ = _inputScheduler.TryPump(now, TrySubmitScheduledCommand);
        }
        else
        {
            ClientWorldInputScheduleSnapshot rejected = _inputScheduler.GetSnapshot();
            if (rejected.CapacityRejections > before.CapacityRejections)
            {
                _actionAccounting.CapacityRejected(BotCommandKind.Movement);
                if (jumpEdge) _actionAccounting.CapacityRejected(BotCommandKind.Jump);
            }
            else
            {
                _actionAccounting.UnavailableOrInvalid(BotCommandKind.Movement);
                if (jumpEdge) _actionAccounting.UnavailableOrInvalid(BotCommandKind.Jump);
            }
        }
    }

    private bool DriveStaleSessionReplay(
        long now,
        in RemotePlayableSessionSnapshot snapshot)
    {
        long elapsed = BotMonotonicClock.TicksToMilliseconds(now - _scenarioStarted);
        if (!_reconnectRequested && SuccessfulReconnects == 0 && elapsed >= 2_000)
        {
            if (HasUnexpectedQueueDebt) return true;
            _priorApplicationEpoch = snapshot.Playable.ApplicationSessionEpoch;
            _priorControlSession = snapshot.Playable.ControlSession;
            _priorPublicActor = snapshot.Playable.PublicActor;
            _reconnectRequested = true;
            BeginRetirement(now, reconnect: true);
            return true;
        }
        if (SuccessfulReconnects == 0) return _reconnectRequested;
        if (!_staleReplayQueued)
        {
            StaleSessionReplayAttempts++;
            var oldCommand = new GamePlayerCommand(
                _priorControlSession,
                1,
                GameLocomotionUpdate.CreateDirect(
                    GamePlayerCommand.MaximumAxisMagnitude, 0),
                GamePlayerActions.None,
                GamePlayerActions.None);
            RemotePlayableCommandMessage wire = UltraSimGameProtocol.CreatePlayableCommand(
                _priorApplicationEpoch,
                in oldCommand,
                default,
                RemotePlayableCommandFlags.None,
                0,
                0);
            if (!RemotePlayableCommandMessageCodec.TryWriteExact(_staleReplayScratch, in wire))
                throw new InvalidOperationException(
                    "The stale-session replay command was not canonical.");
            if (_connection!.Peer!.TryQueue(
                    RemotePlayableCommandMessageCodec.Descriptor.Id,
                    _staleReplayScratch) != NetworkQueueStatus.Accepted)
                return true;
            StaleSessionReplaysQueued++;
            _staleReplayQueued = true;
            _staleReplayQueuedAt = now;
            _staleReplayNewLifetimeProcessedSequence =
                snapshot.Playable.LastProcessedCommandSequence;
            return true;
        }
        if (!_staleReplayFenced &&
            now - _staleReplayQueuedAt >= Stopwatch.Frequency / 2)
        {
            if (snapshot.Playable.LastProcessedCommandSequence !=
                _staleReplayNewLifetimeProcessedSequence)
                throw new InvalidOperationException(
                    "An old application/control-session command became valid after reconnect.");
            _staleReplayFenced = true;
        }
        return !_staleReplayFenced;
    }

    private bool TrySubmitNonCompliant(in ClientWorldCommandIntent intent)
    {
        CommandSubmitCalls++;
        if (_session is null || !_session.Playable.TrySubmitCommand(
                intent, default, RemotePlayableCommandFlags.None, 0, 0, out _))
            return false;
        CommandsQueued++;
        return true;
    }

    private void RunMovingMeasurement(long now, in WorldPosition position, bool crowd)
    {
        if (!_measurementOriginSet)
        {
            _measurementOriginSet = true;
            _measurementOriginX = position.GlobalXMillimeters;
            _measurementOriginZ = position.GlobalZMillimeters;
        }
        long elapsed = BotMonotonicClock.TicksToMilliseconds(now - _scenarioStarted);
        long second = elapsed / 1_000;
        if (second != _lastMeasurementJumpSecond &&
            second > 0 && second % (11 + Id % 5) == 0 &&
            !_pendingCommand.HasPendingExactCompletion)
        {
            _lastMeasurementJumpSecond = second;
            _ = TryPhysicalAction(
                ClientSemanticAction.Jump, GamePlayerActions.Jump, BotCommandKind.Jump);
            return;
        }
        if (now < _nextNavigation || _pendingCommand.HasPendingExactCompletion) return;

        double angle;
        if (crowd)
        {
            long dx = _measurementOriginX - position.GlobalXMillimeters;
            long dz = _measurementOriginZ - position.GlobalZMillimeters;
            long distanceSquared = checked(dx * dx + dz * dz);
            if (distanceSquared > 4_000L * 4_000L)
                angle = Math.Atan2(dz, dx);
            else
                angle = ((Id * 37 + second * 73) % 360) * Math.PI / 180d;
            _nextNavigation = BotMonotonicClock.AddMilliseconds(now, 500);
        }
        else
        {
            // Independent radial streams spread clients before/during the measured
            // window while retaining bounded ordinary direction changes.
            angle = ((Id * 137 + (second / 8) * 29) % 360) * Math.PI / 180d;
            _nextNavigation = BotMonotonicClock.AddMilliseconds(now, 2_000);
        }
        _ = TryMovement(
            (float)Math.Cos(angle), (float)Math.Sin(angle), position, now);
    }

    private void RunSingleHotChunkMeasurement(long now, in WorldPosition position)
    {
        if (!_hasSharedMeasurementTarget)
            throw new InvalidOperationException("The single-hot scenario has no shared target.");
        if (!_isSingleHotMover) return;
        long elapsed = BotMonotonicClock.TicksToMilliseconds(now - _scenarioStarted);
        long second = elapsed / 1_000;
        if (now < _nextNavigation || _pendingCommand.HasPendingExactCompletion) return;
        int waypoint = checked((int)((second / 2) & 3));
        long targetX = checked(_sharedMeasurementTargetX + waypoint switch
        {
            0 => 750L,
            2 => -750L,
            _ => 0L,
        });
        long targetZ = checked(_sharedMeasurementTargetZ + waypoint switch
        {
            1 => 750L,
            3 => -750L,
            _ => 0L,
        });
        long dx = targetX - position.GlobalXMillimeters;
        long dz = targetZ - position.GlobalZMillimeters;
        long distanceSquared = checked(dx * dx + dz * dz);
        _nextNavigation = BotMonotonicClock.AddMilliseconds(now, 200);
        if (distanceSquared <= 250L * 250L)
        {
            _ = TryMovement(0, 0, position, now);
            return;
        }
        double angle = Math.Atan2(dz, dx);
        _ = TryMovement((float)Math.Cos(angle), (float)Math.Sin(angle), position, now);
    }

    private void RunMixedChaos(long now, in RemotePlayableSessionSnapshot snapshot)
    {
        long elapsed = BotMonotonicClock.TicksToMilliseconds(now - _scenarioStarted);
        if (Id == _options.BotCount - 1 && !_reconnectRequested && elapsed >= 4_500)
            _reconnectRequested = true;
        if (_reconnectRequested && SuccessfulReconnects == 0)
        {
            if (HasUnexpectedQueueDebt) return;
            BeginRetirement(now, reconnect: true);
            return;
        }
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
            SyntheticInputSamples++;
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
            _nextSwordAttempt = BotMonotonicClock.AddMilliseconds(now, 500);
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
                !_pendingCommand.HasPendingExactCompletion)
                _ = TryMovement(1f, 0.2f, snapshot.Playable.Position, now);
            _nextNavigation = BotMonotonicClock.AddMilliseconds(now, 250);
        }

        if (Id == 4 && elapsed >= 5_000 && _edgePressure == BotEdgePressureState.None)
        {
            if (TryMovement(1f, 0.7f, snapshot.Playable.Position, now))
            {
                EdgePressureAttempts++;
                _edgePressure = BotEdgePressureState.MovementQueued;
            }
        }
        else if (_edgePressure == BotEdgePressureState.MovementQueued &&
            !_pendingCommand.HasPendingExactCompletion &&
            TryPhysicalAction(
                ClientSemanticAction.Jump, GamePlayerActions.Jump, BotCommandKind.Jump))
        {
            EdgeJumpCommandsQueued++;
            _edgePressure = BotEdgePressureState.Complete;
        }

        if (elapsed >= 6_000 && now >= _nextChaos &&
            !_pendingCommand.HasPendingExactCompletion)
        {
            RunRandomChaos(now, snapshot.Playable.Position);
            _nextChaos = BotMonotonicClock.AddMilliseconds(
                now, 250 + _random.NextInt(750));
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
        if (_pendingCommand.HasPendingExactCompletion) return false;
        SyntheticInputSamples++;
        long relativeTimestamp = _scenarioStarted == 0
            ? 0
            : BotMonotonicClock.TicksToMilliseconds(now - _scenarioStarted);
        GameLocomotionUpdate locomotion = _controls.EvaluateMovement(
            _settings!.ControlGrammar, x, z, position, relativeTimestamp);
        return TryCommand(new ClientWorldCommandIntent(
            locomotion, GamePlayerActions.None, true), BotCommandKind.Movement);
    }

    private bool TryPhysicalAction(
        ClientSemanticAction expected,
        GamePlayerActions gameAction,
        BotCommandKind kind)
    {
        if (_pendingCommand.HasPendingExactCompletion ||
            !_settings!.TryGetBinding(expected, out ClientPhysicalInput physical))
            return false;
        SyntheticInputSamples++;
        if (!_settings.TryResolveInput(in physical, out ClientSemanticAction resolved) ||
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
        CommandSubmitCalls++;
        _actionAccounting.Generated(kind);
        if (_pendingCommand.HasPendingExactCompletion ||
            _session is null || _inputScheduler is null)
        {
            _actionAccounting.UnavailableOrInvalid(kind);
            return false;
        }
        if (kind != BotCommandKind.Movement)
            _pendingCommand.TryRetireLatestMovement(out _);
        var context = new ClientWorldCommandContext(
            target,
            (ClientWorldCommandRequestFlags)(byte)flags,
            0,
            0,
            (ulong)kind);
        long now = Stopwatch.GetTimestamp();
        ClientWorldInputScheduleSnapshot before = _inputScheduler.GetSnapshot();
        if (!_inputScheduler.TryObserve(intent, context, now))
        {
            ClientWorldInputScheduleSnapshot rejected = _inputScheduler.GetSnapshot();
            if (rejected.CapacityRejections > before.CapacityRejections)
                _actionAccounting.CapacityRejected(kind);
            else
                _actionAccounting.UnavailableOrInvalid(kind);
            return false;
        }
        ClientWorldInputScheduleSnapshot observed = _inputScheduler.GetSnapshot();
        // Retain the historical PF1-PF3 counter meaning: a valid source sample was observed,
        // even when a held action produced no new rising edge. PF4A's ActionOutcomes is the
        // authoritative stage-conserving schema.
        switch (kind)
        {
            case BotCommandKind.Movement: MovementAttempts++; break;
            case BotCommandKind.Jump: JumpAttempts++; break;
            case BotCommandKind.Interaction: InteractionAttempts++; break;
            case BotCommandKind.Primary: PrimaryAttempts++; break;
            case BotCommandKind.UiOpen: UiOpenAttempts++; break;
        }
        bool admitted = kind == BotCommandKind.Movement
            ? intent.Locomotion.Kind != GameLocomotionUpdateKind.Unchanged
            : kind != BotCommandKind.None &&
                observed.EdgeObligationsGenerated > before.EdgeObligationsGenerated;
        if (admitted) _actionAccounting.Admitted(kind);
        else _actionAccounting.NoNewEdge(kind);
        if ((kind is BotCommandKind.Jump or BotCommandKind.Interaction or
                BotCommandKind.Primary) &&
            intent.HeldActions != GamePlayerActions.None)
        {
            // These bot operations model a bounded physical press, not a key that remains
            // held forever. Retain the matching release behind the action edge before
            // pumping either obligation. Otherwise a later attempt observes no new rising
            // edge and can be reported as attempted even though no Game command exists.
            var release = new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged, GamePlayerActions.None, intent.HasFocus);
            if (!_inputScheduler.TryObserve(release, default, now))
                throw new InvalidOperationException(
                    "The bot input scheduler could not retain a momentary action release.");
        }
        _ = _inputScheduler.TryPump(now, TrySubmitScheduledCommand);
        return kind == BotCommandKind.None || admitted;
    }

    private bool TrySubmitScheduledCommand(
        in ClientWorldScheduledCommand scheduled,
        out ulong sequence)
    {
        sequence = 0;
        if (_session is null || !_session.Playable.TrySubmitCommand(
                scheduled.Intent,
                scheduled.Context.Target,
                (RemotePlayableCommandFlags)(byte)scheduled.Context.Flags,
                scheduled.Context.FaceXMillimeters,
                scheduled.Context.FaceZMillimeters,
                out sequence))
            return false;
        CommandsQueued++;
        BotCommandKind kind = Enum.IsDefined((BotCommandKind)scheduled.Context.DiagnosticTag)
            ? (BotCommandKind)scheduled.Context.DiagnosticTag
            : BotCommandKind.None;
        if (scheduled.Intent.Locomotion.Kind != GameLocomotionUpdateKind.Unchanged)
            _actionAccounting.Submitted(
                BotCommandKind.Movement,
                completionTracked: false);
        if (kind is not (BotCommandKind.None or BotCommandKind.Movement))
            _actionAccounting.Submitted(kind, completionTracked: true);
        if (kind != BotCommandKind.None)
        {
            ulong epoch = _session.Playable.GetSnapshot().ApplicationSessionEpoch;
            bool retained = kind == BotCommandKind.Movement
                ? _pendingCommand.TryStartOrSupersedeLatestMovement(sequence, epoch)
                : _pendingCommand.TryStart(kind, sequence, epoch);
            if (!retained)
                throw new InvalidOperationException(
                    "The queued bot command could not enter outcome accounting.");
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
            if (!_pendingCommand.HasPendingExactCompletion)
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

    internal bool ContainsPublic(WorldPublicEntityId identity)
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

    internal bool ContainsPublicPresentation(
        WorldPublicEntityId identity,
        string? expectedDisplayLabel = null)
    {
        if (_session is null ||
            !_session.PublicPresentationState.TryGet(
                identity, out ClientPublicPresentationMetadata metadata))
            return false;
        return expectedDisplayLabel is null || string.Equals(
            metadata.DisplayLabel, expectedDisplayLabel, StringComparison.Ordinal);
    }

    internal string? GetCurrentHealthFailure()
    {
        if (State != BotLifetimeState.Active) return null;
        if (_connection is null || _session is null)
            return "The active lifetime has no transport/application composition.";
        ClientDirectConnectSnapshot connection;
        RemotePlayableSessionSnapshot application;
        try
        {
            connection = _connection.GetSnapshot();
            application = _session.GetSnapshot();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"Current health could not be read: {exception.GetType().Name}: {exception.Message}";
        }
        if (connection.State != ClientDirectConnectState.Established)
            return $"The active transport is {connection.State}: {connection.Detail}";
        if (!application.SemanticConsumersActive ||
            application.Playable.State != RemotePlayableClientState.Active ||
            application.Ui.State != RemoteClientApplicationState.Active)
            return $"The active application is unhealthy: semantic={application.SemanticConsumersActive}, " +
                $"playable={application.Playable.State}, ui={application.Ui.State}.";
        return null;
    }

    internal void BeginFinalRetirement(long now)
    {
        if (_disposed || State is BotLifetimeState.Retired or BotLifetimeState.Faulted) return;
        try
        {
            BeginFinalRetirementCore(now);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail($"Final retirement failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void BeginFinalRetirementCore(long now)
    {
        _finalRetirement = true;
        _reconnectRequested = false;
        switch (BotLifecyclePolicy.FinalRetirement(State))
        {
            case BotFinalRetirementAction.None:
                return;
            case BotFinalRetirementAction.RetireWithoutConnection:
                RetireWithoutConnection();
                return;
            case BotFinalRetirementAction.BeginOrContinueDrain:
                if (State == BotLifetimeState.Draining) return;
                CancelPendingCommand();
                BeginRetirement(now, reconnect: false);
                return;
            default:
                throw new InvalidOperationException("Unknown final-retirement action.");
        }
    }

    private void RetireWithoutConnection()
    {
        CancelPendingCommand();
        string? cleanupFailure = DisposeLifetime();
        if (cleanupFailure is not null) Fail(cleanupFailure);
        else State = BotLifetimeState.Retired;
    }

    private void BeginRetirement(long now, bool reconnect)
    {
        if (_session is not null)
        {
            RemotePlayableSessionSnapshot snapshot = _session.GetSnapshot();
            if (reconnect)
            {
                _priorApplicationEpoch = snapshot.Playable.ApplicationSessionEpoch;
                _priorControlSession = snapshot.Playable.ControlSession;
                _priorPublicActor = snapshot.Playable.PublicActor;
            }
            if (_connection?.GetSnapshot().State == ClientDirectConnectState.Established &&
                snapshot.Playable.State == RemotePlayableClientState.Active)
                _ = _session.TryRequestDetach();
        }
        _finalRetirement |= !reconnect;
        State = BotLifetimeState.Draining;
        _drainDeadline = BotMonotonicClock.AddMilliseconds(now, 10_000);
        if (_connection is null)
        {
            CompleteDrain(now, cleanTransport: true);
            return;
        }
        _connection.BeginDisconnect();
        if (_connection.GetSnapshot().State == ClientDirectConnectState.Closed)
            CompleteDrain(now, cleanTransport: true);
    }

    private void CompleteDrain(long now, bool cleanTransport)
    {
        bool reconnect = !_finalRetirement && _reconnectRequested && SuccessfulReconnects == 0;
        string? cleanupFailure = DisposeLifetime();
        if (cleanupFailure is not null)
        {
            Fail(cleanupFailure);
            return;
        }
        if (!cleanTransport)
        {
            Fail("The bot transport did not drain within its bounded deadline.");
            return;
        }
        if (reconnect)
        {
            State = BotLifetimeState.ReconnectDelay;
            _startDue = BotMonotonicClock.AddMilliseconds(now, 250);
        }
        else State = BotLifetimeState.Retired;
    }

    private string? DisposeLifetime()
    {
        string? failure = null;
        CaptureRetiredTraffic();
        try { _session?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = $"Application cleanup failed: {exception.GetType().Name}: {exception.Message}";
        }
        try { _connection?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string connectionFailure =
                $"Transport cleanup failed: {exception.GetType().Name}: {exception.Message}";
            failure = failure is null ? connectionFailure : $"{failure}; {connectionFailure}";
        }
        _session = null;
        _connection = null;
        _profile = null;
        _renderer = null;
        _settings = null;
        _inputScheduler = null;
        _currentPublicActor = default;
        return failure;
    }

    private void CaptureRetiredTraffic()
    {
        if (_trafficCapturedForCurrentLifetime || _connection is null) return;
        if (_connection.TryGetTrafficSnapshot(out HostedUdpConnectionTrafficSnapshot traffic))
        {
            _retiredUdpTraffic = _retiredUdpTraffic.Add(traffic.UdpPayload);
            if (traffic.SecureProtocol is not null)
                _retiredSecureTraffic = _retiredSecureTraffic is null
                    ? traffic.SecureProtocol
                    : NetworkTrafficSnapshot.Add(_retiredSecureTraffic, traffic.SecureProtocol);
        }
        _trafficCapturedForCurrentLifetime = true;
    }

    private void CancelPendingCommand()
    {
        if (_pendingCommand.TryRetireLatestMovement(out _)) return;
        if (!_pendingCommand.TryCancel(out BotPendingCommand cancelled)) return;
        _actionAccounting.RetiredCompletionDebt(cancelled.Kind);
        switch (cancelled.Kind)
        {
            case BotCommandKind.Movement: MovementCancelled++; break;
            case BotCommandKind.Jump: JumpCancelled++; break;
            case BotCommandKind.Interaction: InteractionCancelled++; break;
            case BotCommandKind.Primary: PrimaryCancelled++; break;
            case BotCommandKind.UiOpen: UiOpenCancelled++; break;
        }
    }

    private ClientServerTrustFileStore TrustStore() => new(
        Path.Combine(_stateRoot, "trusted-servers.json"));

    private ClientProfileDecisionFileStore ProfileStore() => new(
        Path.Combine(_stateRoot, "server-profiles.json"));

    private void Fail(string detail)
    {
        const int maximumDiagnosticCharacters = 2_048;
        State = BotLifetimeState.Faulted;
        CancelPendingCommand();
        string? cleanupFailure = DisposeLifetime();
        string combined = cleanupFailure is null ? detail : $"{detail}; {cleanupFailure}";
        _fault = combined.Length <= maximumDiagnosticCharacters
            ? combined
            : combined[..maximumDiagnosticCharacters];
        if (_options.Verbose)
            Console.Error.WriteLine($"PF1 bot {Id:D4} fault: {_fault}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelPendingCommand();
        _ = DisposeLifetime();
        _disposed = true;
    }
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

internal sealed record BotClientTelemetryReport(
    int BotId,
    ConnectionId Connection,
    NetworkServiceTimingSnapshot Network,
    NetworkProtocolSnapshot Protocol,
    RemotePlayableSessionTimingSnapshot Client,
    ClientWorldInputScheduleSnapshot InputScheduler);

internal readonly record struct BotPerClientRateDistribution(
    double AggregateHz,
    double AveragePerClientHz,
    double P50PerClientHz,
    double P95PerClientHz,
    double MaximumPerClientHz,
    int MaximumClientId);

internal readonly record struct BotInputRateReport(
    double MeasuredSeconds,
    BotPerClientRateDistribution Samples,
    BotPerClientRateDistribution SubmissionOpportunities,
    BotPerClientRateDistribution CommandsSubmitted);

internal readonly record struct BotAcrossClientTimingDistribution(
    int Clients,
    double P50OfPerClientP50Microseconds,
    double P95OfPerClientP50Microseconds,
    double WorstClientP50Microseconds,
    int WorstClientP50Id,
    double P50OfPerClientP95Microseconds,
    double P95OfPerClientP95Microseconds,
    double WorstClientP95Microseconds,
    int WorstClientP95Id,
    double P50OfPerClientP99Microseconds,
    double P95OfPerClientP99Microseconds,
    double WorstClientP99Microseconds,
    int WorstClientP99Id,
    double AbsoluteMaximumMicroseconds,
    int AbsoluteMaximumClientId);

internal readonly record struct BotAcrossClientTimingReport(
    BotAcrossClientTimingDistribution CommandToOwnerState,
    BotAcrossClientTimingDistribution OwnerStateFreshness,
    BotAcrossClientTimingDistribution ActionEdgeSubmissionDelay,
    BotAcrossClientTimingDistribution NetworkServiceInterval);

internal readonly record struct BotActionOutcomeReport(
    string Action,
    string Model,
    ulong Generated,
    ulong SchedulerAdmitted,
    ulong SchedulerCapacityRejected,
    ulong NotAdmittedNoNewEdge,
    ulong NotAdmittedUnavailableOrInvalid,
    ulong CoalescedBeforeSubmit,
    ulong Submitted,
    ulong SemanticallyCompleted,
    ulong Accepted,
    ulong Rejected,
    ulong RateLimited,
    ulong CompletionNotTrackedLatestState,
    ulong CancelledOrRetiredCompletionDebt,
    ulong PendingUnsent,
    ulong PendingCompletion,
    ulong Unexplained,
    bool Conserved,
    bool Terminal);

internal sealed record BotRunSummary
{
    public required int SchemaVersion { get; init; }
    public required string Checkpoint { get; init; }
    public required bool Passed { get; init; }
    public required string? Detail { get; init; }
    public required string Endpoint { get; init; }
    public required string Scenario { get; init; }
    public required string InputMode { get; init; }
    public required int ConfiguredInputSampleHz { get; init; }
    public required int ConfiguredInputSubmitHz { get; init; }
    public required int UnreliableFrameCap { get; init; }
    public required string UnreliableAggregationScope { get; init; }
    public required ulong Seed { get; init; }
    public required string HarnessBuildModule { get; init; }
    public required string Runtime { get; init; }
    public required string OperatingSystem { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required int ProcessorCount { get; init; }
    public required string CacheMode { get; init; }
    public required int WarmupMilliseconds { get; init; }
    public required int RequestedDurationMilliseconds { get; init; }
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
    public required int InitialConnectRetries { get; init; }
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
    public required int SyntheticInputSamples { get; init; }
    public required double SyntheticInputSamplesPerSecond { get; init; }
    public required int SwordInteractionAttempts { get; init; }
    public required int SwordPickupObserved { get; init; }
    public required int SwordEquipObserved { get; init; }
    public required int SwordDropObserved { get; init; }
    public required int InteractionCommandsQueued { get; init; }
    public required int InteractionAccepted { get; init; }
    public required int InteractionRejected { get; init; }
    public required int CommandSubmitCalls { get; init; }
    public required int CommandsQueued { get; init; }
    public required ulong ServerRateLimitedCommandsObserved { get; init; }
    public required int StaleSessionReplayAttempts { get; init; }
    public required int StaleSessionReplaysQueued { get; init; }
    public required int StaleSessionReplaysFenced { get; init; }
    public required ulong SchedulerSamplesObserved { get; init; }
    public required ulong SchedulerCommandsSubmitted { get; init; }
    public required ulong SchedulerContinuousCoalesced { get; init; }
    public required ulong SchedulerActionEdgesGenerated { get; init; }
    public required ulong SchedulerActionEdgesSubmitted { get; init; }
    public required ulong SchedulerActionEdgesCompleted { get; init; }
    public required ulong SchedulerReleaseOrStopGenerated { get; init; }
    public required ulong SchedulerReleaseOrStopSubmitted { get; init; }
    public required ulong SchedulerReleaseOrStopCompleted { get; init; }
    public required ulong SchedulerCapacityRejections { get; init; }
    public required int PrimaryAttempts { get; init; }
    public required int PrimaryAccepted { get; init; }
    public required int PrimaryRejected { get; init; }
    public required int UiOpenCommandsQueued { get; init; }
    public required int UiOpenAccepted { get; init; }
    public required int CancelledCommands { get; init; }
    public required int MovementCancelled { get; init; }
    public required int JumpCancelled { get; init; }
    public required int InteractionCancelled { get; init; }
    public required int PrimaryCancelled { get; init; }
    public required int UiOpenCancelled { get; init; }
    public required int DistinctChunksVisited { get; init; }
    public required int ChunkCrossings { get; init; }
    public required bool DistinctChunkCoverageSaturated { get; init; }
    public required int VerticalTransitions { get; init; }
    public required int LandingRecoveries { get; init; }
    public required int PostRecoveryMovementAccepted { get; init; }
    public required int EdgePressureAttempts { get; init; }
    public required int EdgeJumpCommandsQueued { get; init; }
    public required ulong ChatObservationGaps { get; init; }
    public required int ReconnectIdentityObservers { get; init; }
    public required int ReconnectIdentityObserversConverged { get; init; }
    public required ulong PumpIterations { get; init; }
    public required double PumpStartIntervalHighWaterMilliseconds { get; init; }
    public required double PumpDurationHighWaterMilliseconds { get; init; }
    public required double BotServiceIntervalHighWaterMilliseconds { get; init; }
    public required long WorkingSetHighWaterBytes { get; init; }
    public required int ProcessMetricSamples { get; init; }
    public required double CpuMilliseconds { get; init; }
    public required long MeasuredManagedHeapHighWaterBytes { get; init; }
    public required long MeasuredAllocatedBytes { get; init; }
    public required int MeasuredGen0Collections { get; init; }
    public required int MeasuredGen1Collections { get; init; }
    public required int MeasuredGen2Collections { get; init; }
    public required int MeasuredThreadHighWater { get; init; }
    public required BotRunPhaseDurations PhaseDurations { get; init; }
    public required BotWireMeasurementSummary? Wire { get; init; }
    public required string TelemetryLevel { get; init; }
    public required BotClientTelemetryReport[] ClientTelemetry { get; init; }
    public required BotInputRateReport InputRates { get; init; }
    public required BotAcrossClientTimingReport AcrossClientTiming { get; init; }
    public required BotActionOutcomeReport[] ActionOutcomes { get; init; }
    public required BotHttpBodyTrafficReport LifetimeHttpBody { get; init; }
    public required BotCacheTrafficReport LifetimeContentCache { get; init; }
    public required BotPumpWindowSummary MeasuredPump { get; init; }
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
        double pumpStartIntervalHighWaterMilliseconds,
        double pumpDurationHighWaterMilliseconds,
        long workingSetHighWaterBytes,
        double cpuMilliseconds,
        int processMetricSamples,
        int activeHighWater,
        int admittingHighWater,
        int reconnectingHighWater,
        int reconnectIdentityObserversConverged,
        BotRunPhaseDurations phaseDurations,
        BotWireMeasurementSummary? wire,
        BotClientTelemetryReport[] clientTelemetry,
        BotHttpBodyTrafficReport lifetimeHttpBody,
        BotCacheTrafficReport lifetimeContentCache,
        BotPumpWindowSummary measuredPump) => new()
    {
        // Preserve the already accepted PF1 evidence schema. PF2 adds the
        // synchronized warmup/measurement window and first-class wire report.
        SchemaVersion = options.Checkpoint switch
        {
            "PF1" => 2,
            "PF2" => 3,
            "PF2.5" => 4,
            "PF3" => 5,
            "PF4A" => 6,
            "PF4B" => 7,
            "PF4C" => 8,
            "PF4D" => 9,
            _ => 10,
        },
        Checkpoint = options.Checkpoint,
        Passed = passed,
        Detail = detail,
        Endpoint = options.Endpoint,
        Scenario = options.Scenario,
        InputMode = options.InputMode.ToString(),
        ConfiguredInputSampleHz = options.InputSampleHz,
        ConfiguredInputSubmitHz = options.InputSubmitHz,
        UnreliableFrameCap = options.UnreliableFrameCap,
        UnreliableAggregationScope = options.UnreliableAggregationScope.ToString(),
        Seed = options.Seed,
        HarnessBuildModule = BotRuntimeIdentity.BuildModule,
        Runtime = BotRuntimeIdentity.Runtime,
        OperatingSystem = BotRuntimeIdentity.OperatingSystem,
        ProcessArchitecture = BotRuntimeIdentity.Architecture,
        ProcessorCount = Environment.ProcessorCount,
        CacheMode = "shared-persistent-cas",
        WarmupMilliseconds = options.WarmupMilliseconds,
        RequestedDurationMilliseconds = options.DurationMilliseconds,
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
        InitialConnectRetries = bots.Sum(static bot => bot.InitialConnectRetries),
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
        SyntheticInputSamples = bots.Sum(static bot => bot.SyntheticInputSamples),
        SyntheticInputSamplesPerSecond = options.DurationMilliseconds <= 0
            ? 0d
            : bots.Sum(static bot => bot.SyntheticInputSamples) * 1_000d /
                options.DurationMilliseconds,
        SwordInteractionAttempts = bots.Sum(static bot => bot.SwordInteractionAttempts),
        SwordPickupObserved = bots.Sum(static bot => bot.SwordPickupObserved),
        SwordEquipObserved = bots.Sum(static bot => bot.SwordEquipObserved),
        SwordDropObserved = bots.Sum(static bot => bot.SwordDropObserved),
        InteractionCommandsQueued = bots.Sum(static bot => bot.InteractionAttempts),
        InteractionAccepted = bots.Sum(static bot => bot.InteractionAccepted),
        InteractionRejected = bots.Sum(static bot => bot.InteractionRejected),
        CommandSubmitCalls = bots.Sum(static bot => bot.CommandSubmitCalls),
        CommandsQueued = bots.Sum(static bot => bot.CommandsQueued),
        ServerRateLimitedCommandsObserved = bots.Aggregate(
            0UL, static (total, bot) => SaturatingAdd(total, bot.RateLimitedCommandsObserved)),
        StaleSessionReplayAttempts = bots.Sum(static bot => bot.StaleSessionReplayAttempts),
        StaleSessionReplaysQueued = bots.Sum(static bot => bot.StaleSessionReplaysQueued),
        StaleSessionReplaysFenced = bots.Sum(static bot => bot.StaleSessionReplaysFenced),
        SchedulerSamplesObserved = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.SamplesObserved)),
        SchedulerCommandsSubmitted = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.CommandsSubmitted)),
        SchedulerContinuousCoalesced = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.ContinuousCommandsCoalesced)),
        SchedulerActionEdgesGenerated = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.EdgeObligationsGenerated)),
        SchedulerActionEdgesSubmitted = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.EdgeObligationsSubmitted)),
        SchedulerActionEdgesCompleted = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.EdgeObligationsCompleted)),
        SchedulerReleaseOrStopGenerated = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.ReleaseOrStopGenerated)),
        SchedulerReleaseOrStopSubmitted = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.ReleaseOrStopSubmitted)),
        SchedulerReleaseOrStopCompleted = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.ReleaseOrStopCompleted)),
        SchedulerCapacityRejections = clientTelemetry.Aggregate(
            0UL, static (total, client) => SaturatingAdd(
                total, client.InputScheduler.CapacityRejections)),
        PrimaryAttempts = bots.Sum(static bot => bot.PrimaryAttempts),
        PrimaryAccepted = bots.Sum(static bot => bot.PrimaryAccepted),
        PrimaryRejected = bots.Sum(static bot => bot.PrimaryRejected),
        UiOpenCommandsQueued = bots.Sum(static bot => bot.UiOpenAttempts),
        UiOpenAccepted = bots.Sum(static bot => bot.UiOpenAccepted),
        CancelledCommands = bots.Sum(static bot => bot.CancelledCommands),
        MovementCancelled = bots.Sum(static bot => bot.MovementCancelled),
        JumpCancelled = bots.Sum(static bot => bot.JumpCancelled),
        InteractionCancelled = bots.Sum(static bot => bot.InteractionCancelled),
        PrimaryCancelled = bots.Sum(static bot => bot.PrimaryCancelled),
        UiOpenCancelled = bots.Sum(static bot => bot.UiOpenCancelled),
        DistinctChunksVisited = bots.Sum(static bot => bot.DistinctChunksVisited),
        ChunkCrossings = bots.Sum(static bot => bot.ChunkCrossings),
        DistinctChunkCoverageSaturated = bots.Any(static bot => bot.DistinctChunkCoverageSaturated),
        VerticalTransitions = bots.Sum(static bot => bot.VerticalTransitions),
        LandingRecoveries = bots.Sum(static bot => bot.LandingRecoveries),
        PostRecoveryMovementAccepted = bots.Sum(static bot => bot.PostRecoveryMovementAccepted),
        EdgePressureAttempts = bots.Sum(static bot => bot.EdgePressureAttempts),
        EdgeJumpCommandsQueued = bots.Sum(static bot => bot.EdgeJumpCommandsQueued),
        ChatObservationGaps = bots.Aggregate(
            0UL,
            static (total, bot) => ulong.MaxValue - total < bot.ChatObservationGaps
                ? ulong.MaxValue
                : total + bot.ChatObservationGaps),
        ReconnectIdentityObservers = 2,
        ReconnectIdentityObserversConverged = reconnectIdentityObserversConverged,
        PumpIterations = pumpIterations,
        PumpStartIntervalHighWaterMilliseconds = pumpStartIntervalHighWaterMilliseconds,
        PumpDurationHighWaterMilliseconds = pumpDurationHighWaterMilliseconds,
        BotServiceIntervalHighWaterMilliseconds = bots.Max(
            static bot => bot.ServiceIntervalHighWaterMilliseconds),
        WorkingSetHighWaterBytes = workingSetHighWaterBytes,
        ProcessMetricSamples = processMetricSamples,
        CpuMilliseconds = cpuMilliseconds,
        MeasuredManagedHeapHighWaterBytes = measuredPump.ManagedHeapHighWaterBytes,
        MeasuredAllocatedBytes = measuredPump.AllocatedBytes,
        MeasuredGen0Collections = measuredPump.Gen0Collections,
        MeasuredGen1Collections = measuredPump.Gen1Collections,
        MeasuredGen2Collections = measuredPump.Gen2Collections,
        MeasuredThreadHighWater = measuredPump.ThreadHighWater,
        PhaseDurations = phaseDurations,
        Wire = wire,
        TelemetryLevel = options.TelemetryLevel.ToString(),
        ClientTelemetry = clientTelemetry,
        InputRates = CreateInputRates(options, clientTelemetry),
        AcrossClientTiming = CreateAcrossClientTiming(clientTelemetry),
        ActionOutcomes = CreateActionOutcomes(bots, clientTelemetry),
        LifetimeHttpBody = lifetimeHttpBody,
        LifetimeContentCache = lifetimeContentCache,
        MeasuredPump = measuredPump,
        CleanRetirement = cleanRetirement,
        FaultedBots = bots.Count(static bot => bot.IsFaulted),
        Faults = bots.Where(static bot => bot.Fault is not null)
            .Select(bot => $"bot={bot.Id}:{bot.Fault}").ToArray(),
    };

    internal static BotActionOutcomeReport[] CreateActionOutcomes(
        BotClient[] bots,
        BotClientTelemetryReport[] clientTelemetry)
    {
        BotActionOutcomeReport[] reports =
        [
            Action(BotCommandKind.Movement, bots, clientTelemetry),
            Action(BotCommandKind.Jump, bots, clientTelemetry),
            Action(BotCommandKind.Interaction, bots, clientTelemetry),
            Action(BotCommandKind.Primary, bots, clientTelemetry),
            Action(BotCommandKind.UiOpen, bots, clientTelemetry),
            Release(clientTelemetry),
        ];
        return reports;
    }

    private static BotActionOutcomeReport Action(
        BotCommandKind kind,
        BotClient[] bots,
        BotClientTelemetryReport[] clientTelemetry)
    {
        BotActionAccountingSnapshot[] values = bots
            .Select(bot => bot.GetActionAccounting(kind)).ToArray();
        ulong generated = Sum(values, static value => value.Generated);
        ulong admitted = Sum(values, static value => value.SchedulerAdmitted);
        ulong capacity = Sum(values, static value => value.SchedulerCapacityRejected);
        ulong noEdge = Sum(values, static value => value.NotAdmittedNoNewEdge);
        ulong unavailable = Sum(values, static value => value.NotAdmittedUnavailableOrInvalid);
        ulong submitted = Sum(values, static value => value.Submitted);
        ulong completed = Sum(values, static value => value.SemanticallyCompleted);
        ulong accepted = Sum(values, static value => value.Accepted);
        ulong rejected = Sum(values, static value => value.Rejected);
        ulong rateLimited = Sum(values, static value => value.RateLimited);
        ulong untracked = Sum(values, static value => value.CompletionNotTrackedLatestState);
        ulong retired = Sum(values, static value => value.RetiredCompletionDebt);
        ulong pendingCompletion = checked((ulong)bots.Sum(bot => bot.GetPendingActionCompletion(kind)));
        ulong pendingUnsent = kind == BotCommandKind.Movement
            ? checked((ulong)clientTelemetry.Count(static value =>
                value.InputScheduler.ContinuousStatePending))
            : 0;
        ulong coalesced = kind == BotCommandKind.Movement &&
            admitted >= submitted + pendingUnsent
                ? admitted - submitted - pendingUnsent
                : 0;
        ulong generatedRight = SaturatingAdd(
            SaturatingAdd(admitted, capacity), SaturatingAdd(noEdge, unavailable));
        ulong admittedRight = SaturatingAdd(
            SaturatingAdd(submitted, coalesced), pendingUnsent);
        ulong submittedRight = SaturatingAdd(
            SaturatingAdd(completed, untracked), SaturatingAdd(retired, pendingCompletion));
        ulong completedRight = SaturatingAdd(accepted, SaturatingAdd(rejected, rateLimited));
        ulong unexplained = Distance(generated, generatedRight);
        unexplained = SaturatingAdd(unexplained, Distance(admitted, admittedRight));
        unexplained = SaturatingAdd(unexplained, Distance(submitted, submittedRight));
        unexplained = SaturatingAdd(unexplained, Distance(completed, completedRight));
        bool terminal = pendingUnsent == 0 && pendingCompletion == 0;
        return new BotActionOutcomeReport(
            kind.ToString(),
            kind == BotCommandKind.Movement
                ? nameof(BotActionAccountingModel.LatestStateToSubmission)
                : nameof(BotActionAccountingModel.EdgeToAuthorityCompletion),
            generated, admitted, capacity, noEdge, unavailable, coalesced,
            submitted, completed, accepted, rejected, rateLimited, untracked, retired,
            pendingUnsent, pendingCompletion, unexplained, unexplained == 0, terminal);
    }

    private static BotActionOutcomeReport Release(
        BotClientTelemetryReport[] clients)
    {
        ulong generated = clients.Aggregate(0UL, static (total, client) =>
            SaturatingAdd(total, client.InputScheduler.ReleaseOrStopGenerated));
        ulong submitted = clients.Aggregate(0UL, static (total, client) =>
            SaturatingAdd(total, client.InputScheduler.ReleaseOrStopSubmitted));
        ulong completed = clients.Aggregate(0UL, static (total, client) =>
            SaturatingAdd(total, client.InputScheduler.ReleaseOrStopCompleted));
        ulong pendingUnsent = generated >= submitted ? generated - submitted : 0;
        ulong pendingCompletion = submitted >= completed ? submitted - completed : 0;
        ulong unexplained = Distance(generated, SaturatingAdd(submitted, pendingUnsent));
        unexplained = SaturatingAdd(
            unexplained, Distance(submitted, SaturatingAdd(completed, pendingCompletion)));
        bool terminal = pendingUnsent == 0 && pendingCompletion == 0;
        return new BotActionOutcomeReport(
            "ReleaseOrStop", "SchedulerCompletion", generated, generated, 0, 0, 0, 0,
            submitted, completed, completed, 0, 0, 0, 0,
            pendingUnsent, pendingCompletion, unexplained, unexplained == 0, terminal);
    }

    private static ulong Sum(
        BotActionAccountingSnapshot[] values,
        Func<BotActionAccountingSnapshot, ulong> selector) =>
        values.Aggregate(0UL, (total, value) => SaturatingAdd(total, selector(value)));

    private static ulong Distance(ulong left, ulong right) =>
        left >= right ? left - right : right - left;

    private static BotInputRateReport CreateInputRates(
        BotOptions options,
        BotClientTelemetryReport[] clients)
    {
        double seconds = Math.Max(.001, options.DurationMilliseconds / 1_000d);
        return new BotInputRateReport(
            seconds,
            Rates(clients, seconds, static value => value.InputScheduler.SamplesObserved),
            Rates(clients, seconds,
                static value => value.InputScheduler.SubmissionOpportunities),
            Rates(clients, seconds,
                static value => value.InputScheduler.CommandsSubmitted));
    }

    private static BotPerClientRateDistribution Rates(
        BotClientTelemetryReport[] clients,
        double seconds,
        Func<BotClientTelemetryReport, ulong> selector)
    {
        if (clients.Length == 0) return default;
        (int Id, double Value)[] values = clients
            .Select(value => (value.BotId, selector(value) / seconds))
            .OrderBy(static value => value.Item2)
            .ToArray();
        double aggregate = values.Sum(static value => value.Value);
        return new BotPerClientRateDistribution(
            aggregate,
            aggregate / values.Length,
            Percentile(values, .50),
            Percentile(values, .95),
            values[^1].Value,
            values[^1].Id);
    }

    private static BotAcrossClientTimingReport CreateAcrossClientTiming(
        BotClientTelemetryReport[] clients) => new(
        Across(clients, static value => new TimingTriplet(
            value.Client.Playable.CommandToOwnerState.WholeWindowP50Microseconds,
            value.Client.Playable.CommandToOwnerState.WholeWindowP95Microseconds,
            value.Client.Playable.CommandToOwnerState.WholeWindowP99Microseconds,
            value.Client.Playable.CommandToOwnerState.MaximumMicroseconds)),
        Across(clients, static value => new TimingTriplet(
            value.Client.Playable.OwnerStateApplyIntervals.WholeWindowP50Microseconds,
            value.Client.Playable.OwnerStateApplyIntervals.WholeWindowP95Microseconds,
            value.Client.Playable.OwnerStateApplyIntervals.WholeWindowP99Microseconds,
            value.Client.Playable.OwnerStateApplyIntervals.MaximumMicroseconds)),
        Across(clients, static value => new TimingTriplet(
            TicksToMicroseconds(value.InputScheduler.EdgeSubmissionDelay.P50StopwatchTicks),
            TicksToMicroseconds(value.InputScheduler.EdgeSubmissionDelay.P95StopwatchTicks),
            TicksToMicroseconds(value.InputScheduler.EdgeSubmissionDelay.P99StopwatchTicks),
            TicksToMicroseconds(value.InputScheduler.EdgeSubmissionDelay.MaximumStopwatchTicks))),
        Across(clients, static value => new TimingTriplet(
            NetworkTimingDistribution.ToMicroseconds(
                value.Network.PumpIntervals.P50StopwatchTicks),
            NetworkTimingDistribution.ToMicroseconds(
                value.Network.PumpIntervals.P95StopwatchTicks),
            NetworkTimingDistribution.ToMicroseconds(
                value.Network.PumpIntervals.P99StopwatchTicks),
            NetworkTimingDistribution.ToMicroseconds(
                value.Network.PumpIntervals.MaximumStopwatchTicks))));

    private static BotAcrossClientTimingDistribution Across(
        BotClientTelemetryReport[] clients,
        Func<BotClientTelemetryReport, TimingTriplet> selector)
    {
        if (clients.Length == 0) return default;
        (int Id, TimingTriplet Timing)[] values = clients
            .Select(value => (value.BotId, selector(value))).ToArray();
        (int Id, double Value)[] p50 = values
            .Select(static value => (value.Id, value.Timing.P50))
            .OrderBy(static value => value.Item2).ToArray();
        (int Id, double Value)[] p95 = values
            .Select(static value => (value.Id, value.Timing.P95))
            .OrderBy(static value => value.Item2).ToArray();
        (int Id, double Value)[] p99 = values
            .Select(static value => (value.Id, value.Timing.P99))
            .OrderBy(static value => value.Item2).ToArray();
        (int Id, double Value)[] maxima = values
            .Select(static value => (value.Id, value.Timing.Maximum))
            .OrderBy(static value => value.Item2).ToArray();
        return new BotAcrossClientTimingDistribution(
            values.Length,
            Percentile(p50, .50), Percentile(p50, .95), p50[^1].Value, p50[^1].Id,
            Percentile(p95, .50), Percentile(p95, .95), p95[^1].Value, p95[^1].Id,
            Percentile(p99, .50), Percentile(p99, .95), p99[^1].Value, p99[^1].Id,
            maxima[^1].Value, maxima[^1].Id);
    }

    private static double Percentile((int Id, double Value)[] values, double percentile) =>
        values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1,
            0, values.Length - 1)].Value;

    private static double TicksToMicroseconds(ulong ticks) =>
        ticks * 1_000_000d / Stopwatch.Frequency;

    private readonly record struct TimingTriplet(
        double P50,
        double P95,
        double P99,
        double Maximum);

    internal string ToEvidenceLine() =>
        $"{Checkpoint} BOT HARNESS {(Passed ? "PASS" : "FAIL")} schema={SchemaVersion} " +
        $"bots={BotCount} seed={Seed} requestedMs={RequestedDurationMilliseconds} " +
        $"states={ActiveBots}/{AdmittingBots}/{ReconnectingBots}/{RetiredBots} " +
        $"stateHighWater={ActiveHighWater}/{AdmittingHighWater}/{ReconnectingHighWater} " +
        $"generation={ServerGeneration} sessions={ApplicationSessions} reconnects={SuccessfulReconnects} " +
        $"initialRetries={InitialConnectRetries} proofLifetimes={FreshProofLifetimes} " +
        $"movement={MovementAccepted}/{MovementRejected}/" +
        $"{MovementAttempts} jumps={JumpsAccepted}/{JumpsRejected}/{JumpAttempts} " +
        $"primary={PrimaryAccepted}/{PrimaryRejected}/{PrimaryAttempts} " +
        $"chat={LocalChatSubmitted}+{GlobalChatSubmitted}/" +
        $"observed={LocalChatObserved}+{GlobalChatObserved} commands={CommandsQueued} " +
        $"input={InputMode}:{ConfiguredInputSampleHz}/{ConfiguredInputSubmitHz}Hz " +
        $"frames={UnreliableFrameCap}/{UnreliableAggregationScope} " +
        $"scheduler={SchedulerSamplesObserved}/{SchedulerCommandsSubmitted}/" +
        $"coalesced={SchedulerContinuousCoalesced} edges={SchedulerActionEdgesCompleted}/" +
        $"{SchedulerActionEdgesGenerated} releases={SchedulerReleaseOrStopCompleted}/" +
        $"{SchedulerReleaseOrStopGenerated} rateLimited={ServerRateLimitedCommandsObserved} " +
        $"actionConservation={ActionOutcomes.Count(static value => value.Conserved && value.Terminal)}/" +
        $"{ActionOutcomes.Length} unexplained={ActionOutcomes.Aggregate(0UL, static (total, value) => SaturatingAdd(total, value.Unexplained))} " +
        $"staleReplay={StaleSessionReplaysFenced}/{StaleSessionReplaysQueued} " +
        $"nonsense={NonsenseInputs}/commands={NonsenseCommandsCreated} sword={SwordPickupObserved}/" +
        $"{SwordEquipObserved}/{SwordDropObserved} interactions={InteractionAccepted}/" +
        $"{InteractionRejected} chunks={DistinctChunksVisited} crossings={ChunkCrossings} " +
        $"vertical={VerticalTransitions} " +
        $"landings={LandingRecoveries} recoveryMoves={PostRecoveryMovementAccepted} " +
        $"edgeJumps={EdgeJumpCommandsQueued} reconnectObservers=" +
        $"{ReconnectIdentityObserversConverged}/{ReconnectIdentityObservers} " +
        $"pump={PumpIterations} pumpIntervalMs={PumpStartIntervalHighWaterMilliseconds:0.###} " +
        $"pumpDurationMs={PumpDurationHighWaterMilliseconds:0.###} " +
        $"botServiceMs={BotServiceIntervalHighWaterMilliseconds:0.###} " +
        $"workingSet={WorkingSetHighWaterBytes} cpuMs={CpuMilliseconds:0.###} " +
        $"udp={Wire?.ClientToServerUdpPayloadBytes ?? 0}/" +
        $"{Wire?.ServerToClientUdpPayloadBytes ?? 0} " +
        $"retired={CleanRetirement} detail={Detail ?? "none"}";

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
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
    string? MeasurementReadyFile,
    string? MeasurementReleaseFile,
    string? MeasurementCompleteFile,
    int AdmissionStaggerMilliseconds,
    bool Verbose,
    int WarmupMilliseconds,
    NetworkTelemetryLevel TelemetryLevel = NetworkTelemetryLevel.Off,
    int InputSampleHz = 0,
    int InputSubmitHz = 0,
    BotInputMode InputMode = BotInputMode.Compliant,
    int UnreliableFrameCap = 2,
    NetworkUnreliableAggregationScope UnreliableAggregationScope =
        NetworkUnreliableAggregationScope.CrossLane,
    string? CheckpointOverride = null)
{
    internal bool IsMixedChaos => string.Equals(Scenario, "mixed-chaos", StringComparison.Ordinal);
    internal string Checkpoint => CheckpointOverride ?? (Scenario == "sustained-input" ? "PF3" :
        TelemetryLevel == NetworkTelemetryLevel.Detailed
        ? "PF2.5"
        : IsMixedChaos && WarmupMilliseconds == 0 ? "PF1" : "PF2");

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
        string? measurementReady = null;
        string? measurementRelease = null;
        string? measurementComplete = null;
        int bots = 0;
        ulong seed = 0;
        int duration = 0;
        int stagger = 100;
        int warmup = -1;
        bool verbose = false;
        string scenario = "mixed-chaos";
        NetworkTelemetryLevel telemetryLevel = NetworkTelemetryLevel.Off;
        int inputSampleHz = 0;
        int inputSubmitHz = 0;
        BotInputMode inputMode = BotInputMode.Compliant;
        int unreliableFrameCap = NetworkProtocolLimits.Default.MaximumUnreliableFramesPerDatagram;
        NetworkUnreliableAggregationScope unreliableAggregationScope =
            NetworkUnreliableAggregationScope.CrossLane;
        string? checkpointOverride = null;
        foreach (string argument in arguments)
        {
            if (argument == "--verbose") { verbose = true; continue; }
            if (TryValue(argument, "--endpoint=", out string? value)) endpoint = value;
            else if (TryValue(argument, "--fingerprint=", out value)) fingerprint = value;
            else if (TryValue(argument, "--bots=", out value) && int.TryParse(value, out int parsedBots)) bots = parsedBots;
            else if (TryValue(argument, "--seed=", out value) && ulong.TryParse(value, out ulong parsedSeed)) seed = parsedSeed;
            else if (TryValue(argument, "--duration=", out value)) duration = ParseDuration(value!);
            else if (TryValue(argument, "--warmup=", out value)) warmup = ParseDuration(value!);
            else if (TryValue(argument, "--scenario=", out value)) scenario = value!;
            else if (TryValue(argument, "--cache=", out value)) cache = Path.GetFullPath(value!);
            else if (TryValue(argument, "--start-ready-file=", out value)) startReady = Path.GetFullPath(value!);
            else if (TryValue(argument, "--start-release-file=", out value)) startRelease = Path.GetFullPath(value!);
            else if (TryValue(argument, "--ready-file=", out value)) ready = Path.GetFullPath(value!);
            else if (TryValue(argument, "--release-file=", out value)) release = Path.GetFullPath(value!);
            else if (TryValue(argument, "--summary-file=", out value)) summary = Path.GetFullPath(value!);
            else if (TryValue(argument, "--measurement-ready-file=", out value)) measurementReady = Path.GetFullPath(value!);
            else if (TryValue(argument, "--measurement-release-file=", out value)) measurementRelease = Path.GetFullPath(value!);
            else if (TryValue(argument, "--measurement-complete-file=", out value)) measurementComplete = Path.GetFullPath(value!);
            else if (TryValue(argument, "--admission-stagger-ms=", out value) && int.TryParse(value, out int parsedStagger)) stagger = parsedStagger;
            else if (TryValue(argument, "--telemetry=", out value) &&
                Enum.TryParse(value, ignoreCase: true, out NetworkTelemetryLevel parsedTelemetry))
                telemetryLevel = parsedTelemetry;
            else if (TryValue(argument, "--input-sample-hz=", out value) &&
                int.TryParse(value, out int parsedSampleHz)) inputSampleHz = parsedSampleHz;
            else if (TryValue(argument, "--input-submit-hz=", out value) &&
                int.TryParse(value, out int parsedSubmitHz)) inputSubmitHz = parsedSubmitHz;
            else if (TryValue(argument, "--input-mode=", out value) &&
                Enum.TryParse(value!.Replace("-", string.Empty), ignoreCase: true,
                    out BotInputMode parsedInputMode)) inputMode = parsedInputMode;
            else if (TryValue(argument, "--unreliable-frame-cap=", out value) &&
                int.TryParse(value, out int parsedFrameCap) &&
                parsedFrameCap is 1 or 2 or 4 or 8)
                unreliableFrameCap = parsedFrameCap;
            else if (TryValue(argument, "--unreliable-aggregation-scope=", out value))
                unreliableAggregationScope = value switch
                {
                    "same-lane" => NetworkUnreliableAggregationScope.SameLane,
                    "cross-lane" => NetworkUnreliableAggregationScope.CrossLane,
                    _ => throw new ArgumentException(
                        "--unreliable-aggregation-scope must be same-lane or cross-lane."),
                };
            else if (TryValue(argument, "--checkpoint=", out value) &&
                value is "PF2" or "PF2.5" or "PF3" or "PF4A" or "PF4B" or "PF4C" or "PF4D" or "PF4E")
                checkpointOverride = value;
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
        if (scenario is not ("mixed-chaos" or "quiet" or "moving" or "crowd" or
                "single-hot" or "multi-hot" or "sustained-input"))
            throw new ArgumentException(
                "--scenario must be mixed-chaos, quiet, moving, crowd, single-hot, " +
                "multi-hot, or sustained-input.");
        if (inputSampleHz is < 0 or > 1_000)
            throw new ArgumentException("--input-sample-hz must be between 1 and 1000 when set.");
        if (inputSubmitHz is < 0 or > 1_000)
            throw new ArgumentException("--input-submit-hz must be between 1 and 1000 when set.");
        if (scenario == "sustained-input" && inputSampleHz == 0) inputSampleHz = 120;
        if (scenario != "sustained-input" &&
            (inputSampleHz != 0 || inputSubmitHz != 0 || inputMode != BotInputMode.Compliant))
            throw new ArgumentException(
                "input sample/submit/mode options require --scenario=sustained-input.");
        if (cache is null) throw new ArgumentException("--cache is required.");
        if ((startReady is null) != (startRelease is null))
            throw new ArgumentException(
                "--start-ready-file and --start-release-file must be supplied together.");
        if ((ready is null) != (release is null))
            throw new ArgumentException("--ready-file and --release-file must be supplied together.");
        if ((measurementReady is null) != (measurementRelease is null))
            throw new ArgumentException(
                "--measurement-ready-file and --measurement-release-file must be supplied together.");
        if (measurementComplete is not null && measurementReady is null)
            throw new ArgumentException(
                "--measurement-complete-file requires the measurement ready/release pair.");
        if (stagger is < 0 or > 5_000)
            throw new ArgumentException("--admission-stagger-ms must be between 0 and 5000.");
        warmup = warmup < 0 ? (scenario == "mixed-chaos" ? 0 : 60_000) : warmup;
        if (warmup is < 0 or > 3_600_000)
            throw new ArgumentException("--warmup must be between zero and one hour.");
        if (scenario == "mixed-chaos") BotScenarioContract.ValidateMixedChaos(bots, duration);
        return new BotOptions(endpoint!, fingerprint!, bots, seed, duration, scenario,
            cache, startReady, startRelease, ready, release, summary,
            measurementReady, measurementRelease, measurementComplete,
            stagger, verbose, warmup, telemetryLevel,
            inputSampleHz, inputSubmitHz, inputMode,
            unreliableFrameCap, unreliableAggregationScope, checkpointOverride);
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
