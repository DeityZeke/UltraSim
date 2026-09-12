using System.Diagnostics;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;

var tests = new (string Name, Action Body)[]
{
    ("UnexpectedTransportAndUiLossFailActiveLifetime", UnexpectedTransportAndUiLossFailActiveLifetime),
    ("FinalRetirementCoversEveryLifecycleState", FinalRetirementCoversEveryLifecycleState),
    ("JoiningBotRetiresWithoutLateAdmission", JoiningBotRetiresWithoutLateAdmission),
    ("ProcessTelemetryRefreshesAtBoundedCadence", ProcessTelemetryRefreshesAtBoundedCadence),
    ("StopwatchConversionAvoidsIntermediateOverflow", StopwatchConversionAvoidsIntermediateOverflow),
    ("FixedDeadlineRetainsProducerCadencePhase", FixedDeadlineRetainsProducerCadencePhase),
    ("AdmissionDeadlineIncludesLastScheduledStart", AdmissionDeadlineIncludesLastScheduledStart),
    ("MixedChaosRejectsContradictoryConfiguration", MixedChaosRejectsContradictoryConfiguration),
    ("PendingCommandCannotDisappearAcrossLifetime", PendingCommandCannotDisappearAcrossLifetime),
    ("ChatObserverUsesOneBoundedSequenceCursor", ChatObserverUsesOneBoundedSequenceCursor),
    ("ChunkCoverageDistinguishesCrossingsFromDistinctChunks", ChunkCoverageDistinguishesCrossingsFromDistinctChunks),
    ("ReconnectObserverRejectsEqualCountGhostIdentity", ReconnectObserverRejectsEqualCountGhostIdentity),
    ("MeasurementCompleteBarrierRequiresSynchronizedWindow", MeasurementCompleteBarrierRequiresSynchronizedWindow),
    ("SustainedInputOptionsAreExplicitAndBounded", SustainedInputOptionsAreExplicitAndBounded),
    ("PF4AActionStagesRetainNoEdgeAndAuthorityOutcomes", ActionStagesRetainNoEdgeAndAuthorityOutcomes),
    ("PF4ACheckpointSelectsConservationSchema", PF4ACheckpointSelectsConservationSchema),
    ("PF4BCheckpointAndHotChunkScenariosAreExplicit", PF4BCheckpointAndHotChunkScenariosAreExplicit),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Network bot harness tests passed.");

static void UnexpectedTransportAndUiLossFailActiveLifetime()
{
    True(BotLifecyclePolicy.IsUnexpectedTransportLoss(
        BotLifetimeState.Active, ClientDirectConnectState.Draining));
    True(BotLifecyclePolicy.IsUnexpectedTransportLoss(
        BotLifetimeState.Active, ClientDirectConnectState.Closed));
    True(BotLifecyclePolicy.IsUnexpectedTransportLoss(
        BotLifetimeState.Content, ClientDirectConnectState.Closed));
    False(BotLifecyclePolicy.IsUnexpectedTransportLoss(
        BotLifetimeState.Draining, ClientDirectConnectState.Closed));
    False(BotLifecyclePolicy.IsUnexpectedTransportLoss(
        BotLifetimeState.Active, ClientDirectConnectState.Established));

    RemotePlayableSessionSnapshot healthy = default;
    healthy = healthy with
    {
        SemanticConsumersActive = true,
        Playable = healthy.Playable with { State = RemotePlayableClientState.Active },
        Ui = healthy.Ui with { State = RemoteClientApplicationState.Active },
    };
    False(BotLifecyclePolicy.IsApplicationUnhealthy(BotLifetimeState.Active, in healthy));
    RemotePlayableSessionSnapshot detached = healthy with
    {
        Ui = healthy.Ui with { State = RemoteClientApplicationState.Detached },
    };
    True(BotLifecyclePolicy.IsApplicationUnhealthy(BotLifetimeState.Active, in detached));
    RemotePlayableSessionSnapshot fullyDetached = detached with
    {
        SemanticConsumersActive = false,
        Playable = detached.Playable with { State = RemotePlayableClientState.AwaitingAdmission },
    };
    True(BotLifecyclePolicy.IsApplicationUnhealthy(BotLifetimeState.Active, in fullyDetached));
    False(BotLifecyclePolicy.IsApplicationUnhealthy(BotLifetimeState.Content, in fullyDetached));
}

static void MeasurementCompleteBarrierRequiresSynchronizedWindow()
{
    string[] common =
    [
        "--endpoint=127.0.0.1:7777",
        $"--fingerprint={new string('1', 64)}",
        "--bots=1",
        "--seed=1",
        "--duration=2s",
        "--scenario=quiet",
        "--cache=/tmp/ultrasim-bot-measurement-barrier",
    ];
    Throws<ArgumentException>(() => BotOptions.Parse(
        [.. common, "--measurement-complete-file=/tmp/complete"]));
    BotOptions accepted = BotOptions.Parse(
    [
        .. common,
        "--measurement-ready-file=/tmp/ready",
        "--measurement-release-file=/tmp/release",
        "--measurement-complete-file=/tmp/complete",
    ]);
    Equal(Path.GetFullPath("/tmp/complete"), accepted.MeasurementCompleteFile);
}

static void SustainedInputOptionsAreExplicitAndBounded()
{
    string[] common =
    [
        "--endpoint=127.0.0.1:7777",
        $"--fingerprint={new string('1', 64)}",
        "--bots=2",
        "--seed=3",
        "--duration=8s",
        "--scenario=sustained-input",
        "--cache=/tmp/ultrasim-bot-pf3-options",
    ];
    BotOptions stock = BotOptions.Parse(
    [
        .. common,
        "--input-sample-hz=120",
        "--input-submit-hz=30",
        "--input-mode=compliant",
    ]);
    Equal("PF3", stock.Checkpoint);
    Equal(120, stock.InputSampleHz);
    Equal(30, stock.InputSubmitHz);
    Equal(BotInputMode.Compliant, stock.InputMode);

    BotOptions abusive = BotOptions.Parse(
    [
        .. common,
        "--input-sample-hz=240",
        "--input-mode=over-limit",
    ]);
    Equal(BotInputMode.OverLimit, abusive.InputMode);
    Throws<ArgumentException>(() => BotOptions.Parse(
    [
        "--endpoint=127.0.0.1:7777",
        $"--fingerprint={new string('1', 64)}",
        "--bots=1", "--seed=1", "--duration=2s", "--scenario=quiet",
        "--cache=/tmp/ultrasim-bot-pf3-options", "--input-sample-hz=60",
    ]));
    Throws<ArgumentException>(() => BotOptions.Parse(
    [
        .. common,
        "--input-sample-hz=1001",
    ]));
}

static void ActionStagesRetainNoEdgeAndAuthorityOutcomes()
{
    var scheduler = new ClientWorldInputScheduler(
        new ClientInputSubmissionPolicy(60, 30, 10, 100), initialTimestamp: 1);
    var heldJump = new ClientWorldCommandIntent(
        GameLocomotionUpdate.Unchanged, GamePlayerActions.Jump, true);
    True(scheduler.TryObserve(heldJump, default, 1));
    Equal((ulong)1, scheduler.GetSnapshot().EdgeObligationsGenerated);
    True(scheduler.TryObserve(heldJump, default, 2));
    Equal((ulong)1, scheduler.GetSnapshot().EdgeObligationsGenerated);

    var accounting = new BotActionAccounting();
    accounting.Generated(BotCommandKind.Jump);
    accounting.NoNewEdge(BotCommandKind.Jump);
    accounting.Generated(BotCommandKind.Jump);
    accounting.Admitted(BotCommandKind.Jump);
    accounting.Submitted(BotCommandKind.Jump, completionTracked: true);
    accounting.Completed(BotCommandKind.Jump, RemotePlayableActionStatus.NotGrounded);
    BotActionAccountingSnapshot jump = accounting.Snapshot(BotCommandKind.Jump);
    Equal((ulong)2, jump.Generated);
    Equal((ulong)1, jump.NotAdmittedNoNewEdge);
    Equal((ulong)1, jump.SchedulerAdmitted);
    Equal((ulong)1, jump.Submitted);
    Equal((ulong)1, jump.SemanticallyCompleted);
    Equal((ulong)1, jump.Rejected);

    accounting.Generated(BotCommandKind.Movement);
    accounting.Admitted(BotCommandKind.Movement);
    accounting.Submitted(BotCommandKind.Movement, completionTracked: false);
    BotActionAccountingSnapshot movement = accounting.Snapshot(BotCommandKind.Movement);
    Equal(BotActionAccountingModel.LatestStateToSubmission, movement.Model);
    Equal((ulong)1, movement.CompletionNotTrackedLatestState);

    accounting.Reset();
    Equal((ulong)0, accounting.Snapshot(BotCommandKind.Jump).Generated);
}

static void PF4ACheckpointSelectsConservationSchema()
{
    BotOptions options = BotOptions.Parse(
    [
        "--endpoint=127.0.0.1:7777",
        $"--fingerprint={new string('1', 64)}",
        "--bots=1",
        "--seed=4",
        "--duration=2s",
        "--scenario=quiet",
        "--cache=/tmp/ultrasim-bot-pf4a-options",
        "--checkpoint=PF4A",
    ]);
    Equal("PF4A", options.Checkpoint);
}

static void PF4BCheckpointAndHotChunkScenariosAreExplicit()
{
    foreach (string scenario in new[] { "single-hot", "multi-hot" })
    {
        BotOptions options = BotOptions.Parse(
        [
            "--endpoint=127.0.0.1:7777",
            $"--fingerprint={new string('1', 64)}",
            "--bots=8",
            "--seed=400504",
            "--duration=2s",
            $"--scenario={scenario}",
            "--cache=/tmp/ultrasim-bot-pf4b-options",
            "--checkpoint=PF4B",
        ]);
        Equal("PF4B", options.Checkpoint);
        Equal(scenario, options.Scenario);
    }
}

static void FinalRetirementCoversEveryLifecycleState()
{
    Equal(BotFinalRetirementAction.RetireWithoutConnection,
        BotLifecyclePolicy.FinalRetirement(BotLifetimeState.Pending));
    Equal(BotFinalRetirementAction.RetireWithoutConnection,
        BotLifecyclePolicy.FinalRetirement(BotLifetimeState.ReconnectDelay));
    foreach (BotLifetimeState state in new[]
             {
                 BotLifetimeState.Connecting,
                 BotLifetimeState.Profile,
                 BotLifetimeState.Content,
                 BotLifetimeState.Active,
                 BotLifetimeState.Draining,
             })
        Equal(BotFinalRetirementAction.BeginOrContinueDrain,
            BotLifecyclePolicy.FinalRetirement(state));
    Equal(BotFinalRetirementAction.None,
        BotLifecyclePolicy.FinalRetirement(BotLifetimeState.Retired));
    Equal(BotFinalRetirementAction.None,
        BotLifecyclePolicy.FinalRetirement(BotLifetimeState.Faulted));
}

static void JoiningBotRetiresWithoutLateAdmission()
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-network-bot-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scope);
    try
    {
        True(ContentCache.TryOpen(
            Path.Combine(scope, "content"), out ContentCache? cache, out ContentCacheError error));
        Equal(ContentCacheError.None, error);
        using (cache)
        using (var source = new HttpContentPackageSource(new ContentPackageSourceLimits(
                   maximumArchiveBytes: 1_024 * 1_024,
                   requestTimeoutMilliseconds: 1_000,
                   allowInsecureServerHostedHttp: true,
                   allowPrivateNetworkTargets: true,
                   maximumConcurrentRequests: 1)))
        using (var bot = new BotClient(
                   new BotOptions(
                       "127.0.0.1:65534",
                       new string('1', 64),
                       8,
                       1,
                       10_000,
                       "mixed-chaos",
                       scope,
                       null, null, null, null, null, null, null, null,
                       0,
                       false,
                       0),
                   0,
                   cache!,
                   source,
                   Stopwatch.GetTimestamp()))
        {
            bot.Pump(Stopwatch.GetTimestamp(), allowNewChaos: false);
            Equal(BotLifetimeState.Connecting, bot.State);
            bot.BeginFinalRetirement(Stopwatch.GetTimestamp());
            for (int iteration = 0; iteration < 100 && !bot.IsRetired; iteration++)
                bot.Pump(Stopwatch.GetTimestamp(), allowNewChaos: false);
            True(bot.IsRetired);
            False(bot.IsActive);
            Equal(0, bot.SessionCount);
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void ProcessTelemetryRefreshesAtBoundedCadence()
{
    var source = new FakeProcessSource(
        new BotProcessSample(10, 1),
        new BotProcessSample(40, 4),
        new BotProcessSample(20, 5));
    using var sampler = new BotProcessTelemetrySampler(source, intervalMilliseconds: 250);
    long start = 10_000;
    sampler.Sample(start);
    sampler.Sample(BotMonotonicClock.AddMilliseconds(start, 100));
    Equal(1, source.Reads);
    sampler.Sample(BotMonotonicClock.AddMilliseconds(start, 250));
    Equal(2, source.Reads);
    Equal(40L, sampler.WorkingSetHighWaterBytes);
    sampler.Sample(BotMonotonicClock.AddMilliseconds(start, 251), force: true);
    Equal(3, source.Reads);
    Equal(5d, sampler.CpuMilliseconds);
}

static void StopwatchConversionAvoidsIntermediateOverflow()
{
    const long frequency = 1_000_000_000;
    const long days = 200;
    long milliseconds = checked(days * 24L * 60 * 60 * 1_000);
    long ticks = checked(days * 24L * 60 * 60 * frequency);
    Equal(milliseconds, BotMonotonicClock.TicksToMilliseconds(ticks, frequency));
    Equal(frequency, BotMonotonicClock.MillisecondsToTicks(1_000, frequency));
    Equal(0L, BotMonotonicClock.TicksToMilliseconds(0, frequency));
}

static void FixedDeadlineRetainsProducerCadencePhase()
{
    long deadline = 1_000;
    const long interval = 100;
    deadline = BotMonotonicClock.AdvanceFixedDeadline(deadline, 1_001, interval);
    Equal(1_100L, deadline);
    deadline = BotMonotonicClock.AdvanceFixedDeadline(deadline, 1_102, interval);
    Equal(1_200L, deadline);
    deadline = BotMonotonicClock.AdvanceFixedDeadline(deadline, 1_451, interval);
    Equal(1_500L, deadline);
    Equal(1_500L, BotMonotonicClock.AdvanceFixedDeadline(deadline, 1_499, interval));
    Throws<ArgumentOutOfRangeException>(() =>
        BotMonotonicClock.AdvanceFixedDeadline(deadline, 1_500, 0));
}

static void AdmissionDeadlineIncludesLastScheduledStart()
{
    const long created = 123_456;
    long actual = BotScenarioContract.AdmissionDeadline(created, 50, 5_000);
    long expected = BotMonotonicClock.AddMilliseconds(
        created,
        49L * 5_000 + BotScenarioContract.MaximumInitialAdmissionMilliseconds);
    Equal(expected, actual);
}

static void MixedChaosRejectsContradictoryConfiguration()
{
    Throws<ArgumentException>(() => BotScenarioContract.ValidateMixedChaos(4, 14_000));
    Throws<ArgumentException>(() => BotScenarioContract.ValidateMixedChaos(8, 2_000));
    BotScenarioContract.ValidateMixedChaos(8, 14_000);
}

static void PendingCommandCannotDisappearAcrossLifetime()
{
    var tracker = new BotPendingCommandTracker();
    True(tracker.TryStart(BotCommandKind.Movement, 7, 100));
    False(tracker.TryComplete(100, 6, out _));
    Throws<InvalidOperationException>(tracker.RequireClearForNewLifetime);
    True(tracker.TryComplete(100, 7, out BotPendingCommand completed));
    Equal(BotCommandKind.Movement, completed.Kind);
    tracker.RequireClearForNewLifetime();

    True(tracker.TryStart(BotCommandKind.Jump, 8, 100));
    True(tracker.TryCancel(out BotPendingCommand cancelled));
    Equal((ulong)100, cancelled.ApplicationSessionEpoch);
    tracker.RequireClearForNewLifetime();
    False(tracker.TryComplete(101, 8, out _));
}

static void ChatObserverUsesOneBoundedSequenceCursor()
{
    var cursor = new BotChatObservationCursor();
    var rows = new ClientChatOccurrence[256];
    int local = 0;
    int global = 0;
    for (int batch = 0; batch < 8; batch++)
    {
        for (int index = 0; index < rows.Length; index++)
        {
            ulong sequence = checked((ulong)(batch * rows.Length + index + 1));
            rows[index] = Chat(11, sequence,
                index % 2 == 0 ? ClientChatOccurrenceKind.GlobalSpeech :
                    ClientChatOccurrenceKind.SpatialSpeech,
                $"PF1:7:{sequence}");
        }
        cursor.Observe(11, rows, "PF1:7:", out int observedLocal, out int observedGlobal);
        local += observedLocal;
        global += observedGlobal;
    }
    Equal(1, cursor.RetainedCursorEntries);
    Equal(1_024, local);
    Equal(1_024, global);
    cursor.Observe(11, rows, "PF1:7:", out int duplicateLocal, out int duplicateGlobal);
    Equal(0, duplicateLocal + duplicateGlobal);

    ClientChatOccurrence[] later = [Chat(11, 2_100, ClientChatOccurrenceKind.GlobalSpeech, "PF1:7:gap")];
    cursor.Observe(11, later, "PF1:7:", out _, out int afterGap);
    Equal(1, afterGap);
    True(cursor.ObservationGaps > 0);
    cursor.Observe(12,
        [Chat(11, 1, ClientChatOccurrenceKind.GlobalSpeech, "PF1:7:new")],
        "PF1:7:", out _, out int afterReconnect);
    Equal(1, afterReconnect);
    Equal(1, cursor.RetainedCursorEntries);
}

static void ChunkCoverageDistinguishesCrossingsFromDistinctChunks()
{
    var observation = new BotChunkObservation(2);
    var a = new SpatialChunkCoord(0, 0);
    var b = new SpatialChunkCoord(1, 0);
    observation.Observe(a);
    observation.Observe(b);
    observation.Observe(a);
    Equal(2, observation.DistinctChunks);
    Equal(2, observation.Crossings);
}

static void ReconnectObserverRejectsEqualCountGhostIdentity()
{
    var observation = new BotReconnectIdentityObservation();
    observation.ObserveBefore(oldPresent: true);
    observation.ObserveAfter(oldPresent: true, replacementPresent: false);
    False(observation.Converged);
    True(observation.StaleOldIdentityObserved);

    var healthy = new BotReconnectIdentityObservation();
    healthy.ObserveBefore(oldPresent: true);
    healthy.ObserveAfter(oldPresent: false, replacementPresent: true);
    True(healthy.Converged);
}

static ClientChatOccurrence Chat(
    ulong epoch,
    ulong sequence,
    ClientChatOccurrenceKind kind,
    string text) => new(
        ServerGenerationId: 1,
        DeliveryId: checked(epoch * 10_000 + sequence),
        RecipientSequence: sequence,
        SpeechFactId: sequence,
        SimulationTick: sequence,
        Kind: kind,
        SpeechMode: kind == ClientChatOccurrenceKind.GlobalSpeech
            ? ClientChatSpeechMode.Global
            : ClientChatSpeechMode.Say,
        PublicAnchor: kind == ClientChatOccurrenceKind.SpatialSpeech
            ? new WorldPublicEntityId(1, 1)
            : default,
        DisplayLabel: "Bot",
        Text: text);

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; actual {actual}.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

internal sealed class FakeProcessSource(params BotProcessSample[] samples) : IBotProcessSampleSource
{
    private int _next;
    internal int Reads { get; private set; }

    public BotProcessSample ReadFresh()
    {
        Reads++;
        return samples[Math.Min(_next++, samples.Length - 1)];
    }

    public void Dispose() { }
}
