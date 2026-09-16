using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;

internal static class Pf5R8RequiredFanoutAttribution
{
    private const int Seed = 0x5F80_8008;
    private const string StartingHead =
        "13c1afedae7e18ad736a93e28573bcd96cdb5a9e";
    private const string SemanticCandidate =
        "14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9";

    internal static void Run(string[] args)
    {
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        int matrixWarmup = ReadInt(args, "--matrix-warmup", focused ? 6 : 20);
        int matrixTicks = ReadInt(args, "--matrix-ticks", focused ? 8 : 60);
        int diagnosticTicks = ReadInt(args, "--diagnostic-ticks", focused ? 8 : 30);
        int ratedWarmup = ReadInt(args, "--rated-warmup", focused ? 8 : 60);
        int ratedTicks = ReadInt(args, "--rated-ticks", focused ? 12 : 270);
        int repetitions = ReadInt(args, "--repetitions", focused ? 1 : 3);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r8-required-fanout-attribution.raw.json";
        if (matrixWarmup < 1 || matrixTicks < 8 || diagnosticTicks < 4 ||
            ratedWarmup < 1 || ratedTicks < 8 || repetitions < 1)
            throw new ArgumentOutOfRangeException(nameof(args));

        Prime();
        var fanout = new List<Pf5R8MeasuredRow>();
        var topology = new List<Pf5R8MeasuredRow>();
        var activity = new List<Pf5R8MeasuredRow>();
        var telemetry = new List<Pf5R8TelemetryRow>();
        var proofDebt = new List<Pf5R8ProofDebtRow>();
        var rated = new List<Pf5R8RatedRow>();

        int[] fanoutPopulations = focused ? [1, 8, 16] : [1, 8, 32, 64, 128, 256];
        foreach (int recipients in fanoutPopulations)
        {
            P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(
                recipients, 1_000, 20, 1)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            fanout.Add(RunMeasured("fixed-source-fanout", $"r{recipients}", workload));
        }

        int topologyRecipients = focused ? 16 : 512;
        int topologyDirty = focused ? 6 : 179;
        int[] topologyGroups = focused ? [1, 4, 16] : [1, 8, 64, 512];
        foreach (int groups in topologyGroups)
        {
            P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(
                topologyRecipients, topologyRecipients, topologyDirty, groups)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            topology.Add(RunMeasured("fixed-total-topology",
                $"{groups}x{topologyRecipients / groups}", workload));
        }

        int activityRecipients = focused ? 16 : 128;
        int activityEntities = focused ? 32 : 1_000;
        int[] changed = focused ? [1, 6, 16] : [1, 8, 20, 30, 45, 128];
        foreach (int dirty in changed)
        {
            P03NetworkWorkload baseWorkload = P03NetworkWorkload.ChunkTopology(
                activityRecipients, activityEntities, dirty, 1)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            activity.Add(RunMeasured("fixed-recipient-activity", $"spread-{dirty}",
                baseWorkload));
            if (dirty is 8 or 20 or 30)
                activity.Add(RunMeasured("fixed-recipient-record-shape", $"one-chunk-{dirty}",
                    baseWorkload with
                    {
                        Id = baseWorkload.Id + "-one-chunk",
                        MutationPattern = P03MutationPattern.FixedPrefix,
                    }));
        }

        (string Label, int Dirty)[] telemetryActivities = focused
            ? [("focused", 6)]
            : [("low", 20), ("high", 45)];
        int telemetryRepetitions = focused ? 1 : 3;
        foreach ((string activityLabel, int telemetryDirty) in telemetryActivities)
        for (int repetition = 0; repetition < telemetryRepetitions; repetition++)
        {
            int telemetryRecipients = focused ? 16 : 128;
            P03NetworkWorkload telemetryWorkload = P03NetworkWorkload.ChunkTopology(
                telemetryRecipients, focused ? 32 : 1_000, telemetryDirty, 1)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            GameTelemetryLevel[] order = (repetition & 1) == 0
                ? [GameTelemetryLevel.Off, GameTelemetryLevel.Basic,
                    GameTelemetryLevel.Detailed]
                : [GameTelemetryLevel.Detailed, GameTelemetryLevel.Basic,
                    GameTelemetryLevel.Off];
            foreach (GameTelemetryLevel level in order)
            {
                P03WholeFlowRow row = RunHarness(telemetryWorkload, matrixWarmup,
                    matrixTicks, level, retainRaw: true);
                telemetry.Add(new Pf5R8TelemetryRow(
                    activityLabel, repetition, level.ToString(), row));
            }
        }

        int[] debtDepths = focused ? [1, 4] : [1, 4, 16, 64];
        foreach (bool sameChunk in new[] { false, true })
        foreach (int depth in debtDepths)
        {
            P03NetworkWorkload workload = (sameChunk
                    ? P03NetworkWorkload.ChunkMatchedControl(1, 1_000, 1) with
                    {
                        MutationPattern = P03MutationPattern.FixedPrefix,
                    }
                    : P03NetworkWorkload.ChunkProductionDense(1) with
                    {
                        DirtyEntitiesPerTick = 1,
                        MutationPattern = P03MutationPattern.SingleAcrossChunkStripe,
                    })
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124) with
                {
                    Id = $"proof-debt-{(sameChunk ? "same" : "multi")}-{depth}",
                };
            P03WholeFlowRow row = RunHarness(
                workload,
                Math.Max(matrixWarmup, depth),
                Math.Max(matrixTicks, depth * 2),
                GameTelemetryLevel.Detailed,
                retainRaw: false,
                proofDispatchInterval: depth);
            proofDebt.Add(new Pf5R8ProofDebtRow(
                sameChunk ? "same-chunk" : "multi-chunk",
                depth,
                row.ProfileCounters.Lifecycle.OutstandingHighWater,
                row.ProfileCounters.ChunkCpu.ProofLookupCalls,
                row.ProfileCounters.ChunkCpu.ProofLookupComparisons,
                row.ProfileCounters.ChunkCpu.ProofSubscriptionLookupCalls,
                row.ProfileCounters.ChunkCpu.ProofSubscriptionLookupComparisons,
                row.ProfileCounters.ChunkCpu.ProofObsoleteEntryChecks,
                row.ProfileCounters.ChunkCpu.ProofObsoleteSameChunkChecks,
                row.ProfileCounters.Lifecycle.ProofEntries,
                row.ProfileCounters.Lifecycle.SupersededDebtRetired,
                row.Timing.ServerGameProofApply,
                row.Converged));
        }

        string[] divergenceNames = focused
            ? ["synchronized", "proof-delay", "combined-divergence"]
            : ["synchronized", "staggered-joins", "cadence-phases", "controlled-loss",
                "controlled-reorder", "proof-delay", "temporary-stalls", "aoi-churn",
                "reconnect", "history-pressure", "combined-divergence"];
        var divergence = new Pf5R1ScenarioReport[divergenceNames.Length];
        for (int index = 0; index < divergence.Length; index++)
            divergence[index] = Pf5R1HistoryAudit.RunPreparedBodyScenarioForPf5R6(
                divergenceNames[index], focused ? 16 : 64, focused ? 40 : 180, Seed);

        int[] boundaryPopulations = focused ? [32] : [256, 350, 400];
        foreach (int population in boundaryPopulations)
            rated.Add(new Pf5R8RatedRow("high-boundary", 0, RunRated(
                population, high: true, ratedWarmup, ratedTicks)));
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            bool highFirst = (repetition & 1) != 0;
            foreach (bool high in highFirst ? new[] { true, false } : new[] { false, true })
                rated.Add(new Pf5R8RatedRow(high ? "r300-high" : "r300-low", repetition,
                    RunRated(focused ? 32 : 300, high, ratedWarmup, ratedTicks)));
        }

        var report = new Pf5R8Report(
            "ultrasim-concept-demo-pf5-r8-required-fanout-v1",
            DateTimeOffset.UtcNow,
            StartingHead,
            ReadGitSha(),
            SemanticCandidate,
            Environment.Version.ToString(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            ReadCpuModel(),
            Stopwatch.Frequency,
            Seed,
            focused,
            matrixWarmup,
            matrixTicks,
            diagnosticTicks,
            ratedWarmup,
            ratedTicks,
            repetitions,
            "Release; one in-process authority plus real secure loopback UDP sessions and clients; " +
                "BackgroundPlatformBatch sender is a bounded background owner",
            "ChunkPublication; shared prepared source truth; body sharing enabled; CurrentBundled; " +
                "singleton compression disabled; selected message-148 compression; cadence 1/2/4; " +
                "CrossLane cap 2; BackgroundPlatformBatch; Game manifest v29; transport revision 2; " +
                "60 Hz authority; product participant cap remains 64",
            "ServerOwnedRemoteReplicationBudget is the paired same-tick sum of " +
                "RemotePublicationPreparation + RecipientCommitObservation + " +
                "NetworkAndRemoteReplication. WholeFixture includes in-process clients. Detailed " +
                "exclusive stages are diagnostic-only and do not define rated capacity.",
            [
                "source mutation != dirty chunk != recipient obligation",
                "due work != admitted delivery; queue refusal creates no admitted debt",
                "logical publication != Game message != Network frame != UDP datagram",
                "sent != known; only a matching application proof advances recipient knowledge",
                "positive latest state may supersede older positive debt; absence remains exact",
            ],
            fanout.ToArray(), topology.ToArray(), activity.ToArray(),
            telemetry.ToArray(), divergence, proofDebt.ToArray(), rated.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        Pf5R8MeasuredRow RunMeasured(
            string matrix,
            string label,
            P03NetworkWorkload workload)
        {
            Console.WriteLine($"PF5-R8 {matrix}/{label}: rated");
            P03WholeFlowRow ratedRow = RunHarness(
                workload, matrixWarmup, matrixTicks, GameTelemetryLevel.Off,
                retainRaw: true);
            Console.WriteLine($"PF5-R8 {matrix}/{label}: detailed");
            P03WholeFlowRow detailedRow = RunHarness(
                workload, Math.Min(matrixWarmup, 8), diagnosticTicks,
                GameTelemetryLevel.Detailed, retainRaw: false);
            return new Pf5R8MeasuredRow(matrix, label, ratedRow, detailedRow,
                BuildExclusiveAttribution(detailedRow));
        }
    }

    private static P03WholeFlowRow RunRated(
        int recipients,
        bool high,
        int warmup,
        int ticks)
    {
        int dirty = high
            ? Math.Max(20, (int)Math.Round(recipients * .35, MidpointRounding.AwayFromZero))
            : 20;
        P03NetworkWorkload workload = P03NetworkWorkload.ChunkMatchedControl(
            recipients, 1_000, dirty)
            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
        return RunHarness(workload, warmup, ticks, GameTelemetryLevel.Off, retainRaw: true);
    }

    private static P03WholeFlowRow RunHarness(
        P03NetworkWorkload workload,
        int warmup,
        int ticks,
        GameTelemetryLevel telemetry,
        bool retainRaw,
        int proofDispatchInterval = 1)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var harness = new P03WholeFlowHarness(
            workload,
            P03NetworkProfile.ChunkPublication,
            2,
            stageTimingEnabled: telemetry == GameTelemetryLevel.Detailed,
            gameDetailedAttribution: false,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
            bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
            historyAudit: false,
            legacyRangeCensus: false,
            authorityThroughNetworkTimingEnabled: true,
            serverOwnedRemoteTimingEnabled: true,
            chunkReadModel: PreparedChunkPublicationReadModel.PreparedLatestState,
            socketIoMode: SharedUdpSocketIoMode.BackgroundPlatformBatch,
            backgroundEgressTimingEnabled: true,
            retainRawSamples: retainRaw,
            gameTelemetryLevel: telemetry,
            serverProofDispatchIntervalTicks: proofDispatchInterval);
        P03WholeFlowRow row = harness.Run(warmup, ticks);
        Require(row.Converged && row.Transport.CapacityDrops == 0,
            $"PF5-R8 row did not converge: {workload.Id}/{telemetry}.");
        Require(row.ProfileCounters.PreparedPublication.HistoryDerivedSteadyStateCalls == 0,
            "PF5-R8 left the selected prepared latest-state path.");
        return row;
    }

    private static Pf5R8ExclusiveAttribution BuildExclusiveAttribution(P03WholeFlowRow row)
    {
        P03ChunkCpuAttribution cpu = row.ProfileCounters.ChunkCpu;
        PreparedChunkPublicationSnapshot source = row.ProfileCounters.PreparedPublication;
        RemoteChunkDeltaBodyStoreSnapshot bodies = row.ProfileCounters.DeltaBodies;
        ulong sourceReadChildren = Sum(
            source.RangeIdentityInspectionTicks,
            source.CoverageValidationTicks,
            source.RecipientRangePreparationTicks,
            bodies.LookupTicks,
            bodies.CopyTicks);
        ulong sourceReadResidual = Sub(cpu.SourceReadTicks, sourceReadChildren);
        ulong bookkeepingExclusive = Sub(
            cpu.OutstandingBookkeepingTicks, cpu.DetailedCensusTicks);
        ulong scheduleAttributed = Sum(
            cpu.SourceReadTicks,
            cpu.DeltaMaterializationTicks,
            cpu.PreparedValidationHashTicks,
            cpu.CanonicalStandaloneEncodeTicks,
            cpu.BundlePreflightTicks,
            cpu.BundleRecordCopyTicks,
            cpu.PacketBoundaryCarryCopyTicks,
            cpu.BundleStagingTicks,
            cpu.BundleEncodeTicks,
            row.ProfileCounters.Compression.CompressionStopwatchTicks,
            cpu.NetworkQueueTicks,
            bookkeepingExclusive,
            cpu.DetailedCensusTicks,
            cpu.BundleClearTicks);
        ulong scheduleResidual = Sub(cpu.ScheduleTotalTicks, scheduleAttributed);
        double networkTicks = MillisecondsToTicks(
            row.Timing.NetworkAndRemoteReplication.Mean *
            row.Timing.NetworkAndRemoteReplication.Samples);
        double protocolTicks = MillisecondsToTicks(row.Timing.Protocol.ServerMilliseconds);
        double remoteResidual = Math.Max(0,
            networkTicks - cpu.ScheduleTotalTicks - cpu.ProofTotalTicks -
            cpu.ReadinessSelectionTicks - protocolTicks);
        return new Pf5R8ExclusiveAttribution(
            cpu.ScheduleTotalTicks,
            cpu.SourceReadTicks,
            source.RangeIdentityInspectionTicks,
            source.CoverageValidationTicks,
            source.RecipientRangePreparationTicks,
            bodies.LookupTicks,
            bodies.CopyTicks,
            sourceReadResidual,
            cpu.DeltaMaterializationTicks,
            cpu.PreparedValidationHashTicks,
            cpu.CanonicalStandaloneEncodeTicks,
            cpu.BundlePreflightTicks,
            cpu.BundleRecordCopyTicks,
            cpu.PacketBoundaryCarryCopyTicks,
            cpu.BundleStagingTicks,
            cpu.BundleEncodeTicks,
            row.ProfileCounters.Compression.CompressionStopwatchTicks,
            cpu.NetworkQueueTicks,
            bookkeepingExclusive,
            cpu.DetailedCensusTicks,
            cpu.BundleClearTicks,
            scheduleAttributed,
            scheduleResidual,
            cpu.ReadinessSelectionTicks,
            cpu.ProofTotalTicks,
            protocolTicks,
            networkTicks,
            remoteResidual,
            "SourceRead contains identity/body/coverage/full-range children; those children are " +
                "not added again to ScheduleAttributed. OutstandingBookkeeping excludes its " +
                "nested DetailedCensus. Protocol timing is a child of the Network parent. " +
                "Residual is measured parent minus non-overlapping accounted children.");
    }

    private static ulong Sum(params ulong[] values)
    {
        ulong result = 0;
        foreach (ulong value in values)
            result = ulong.MaxValue - result < value ? ulong.MaxValue : result + value;
        return result;
    }

    private static ulong Sub(ulong value, ulong amount) => value >= amount ? value - amount : 0;
    private static double MillisecondsToTicks(double value) =>
        value * Stopwatch.Frequency / 1_000d;

    private static void Prime()
    {
        P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(8, 8, 3, 1)
            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
        _ = RunHarness(workload, 3, 3, GameTelemetryLevel.Off, retainRaw: false);
    }

    private static string ReadCpuModel()
    {
        const string path = "/proc/cpuinfo";
        if (!File.Exists(path)) return "unknown";
        foreach (string line in File.ReadLines(path))
        {
            int separator = line.IndexOf(':');
            if (separator > 0 && line[..separator].Trim() == "model name")
                return line[(separator + 1)..].Trim();
        }
        return "unknown";
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? text = ReadText(args, name);
        return text is null ? fallback : int.Parse(text,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadText(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index < 0 || index + 1 >= args.Length ? null : args[index + 1];
    }

    private static string ReadGitSha()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Could not start git.");
        string value = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? value : "unknown";
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}

internal sealed record Pf5R8Report(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string StartingHead,
    string MeasurementHead,
    string SemanticCandidate,
    string Runtime,
    string OperatingSystem,
    string ProcessArchitecture,
    int LogicalProcessors,
    string CpuModel,
    long StopwatchFrequency,
    int Seed,
    bool Focused,
    int MatrixWarmupTicks,
    int MatrixMeasuredTicks,
    int DiagnosticMeasuredTicks,
    int RatedWarmupTicks,
    int RatedMeasuredTicks,
    int RatedRepetitions,
    string ProcessArrangement,
    string ProductionPolicy,
    string TimingBoundary,
    string[] WorkDefinitions,
    Pf5R8MeasuredRow[] FixedSourceFanout,
    Pf5R8MeasuredRow[] FixedTotalTopology,
    Pf5R8MeasuredRow[] FixedRecipientActivity,
    Pf5R8TelemetryRow[] TelemetryControls,
    Pf5R1ScenarioReport[] RecipientDivergence,
    Pf5R8ProofDebtRow[] ProofDebt,
    Pf5R8RatedRow[] RatedAnchors);

internal sealed record Pf5R8MeasuredRow(
    string Matrix,
    string Label,
    P03WholeFlowRow Rated,
    P03WholeFlowRow Detailed,
    Pf5R8ExclusiveAttribution Exclusive);

internal sealed record Pf5R8TelemetryRow(
    string Activity,
    int Repetition,
    string Level,
    P03WholeFlowRow Row);

internal sealed record Pf5R8ProofDebtRow(
    string Shape,
    int DispatchIntervalTicks,
    int OutstandingHighWater,
    ulong DeliveryLookupCalls,
    ulong DeliveryLookupComparisons,
    ulong SubscriptionLookupCalls,
    ulong SubscriptionLookupComparisons,
    ulong ObsoleteEntryChecks,
    ulong ObsoleteSameChunkChecks,
    ulong ProofEntries,
    ulong SupersededDebtRetired,
    P03Metric ProofApply,
    bool Converged);

internal sealed record Pf5R8RatedRow(
    string Curve,
    int Repetition,
    P03WholeFlowRow Row);

internal sealed record Pf5R8ExclusiveAttribution(
    ulong ScheduleParentTicks,
    ulong SourceReadParentTicks,
    ulong RangeIdentityTicks,
    ulong CoverageValidationTicks,
    ulong FullRangePreparationTicks,
    ulong BodyLookupTicks,
    ulong BodyCopyTicks,
    ulong SourceReadResidualTicks,
    ulong ProtocolMaterializationTicks,
    ulong ValidationHashTicks,
    ulong StandaloneEncodeTicks,
    ulong BundlePreflightTicks,
    ulong BundleRecordCopyTicks,
    ulong PacketBoundaryCarryCopyTicks,
    ulong BundleStagingTicks,
    ulong BundleEncodeTicks,
    ulong CompressionTicks,
    ulong NetworkQueueTicks,
    ulong BookkeepingExclusiveTicks,
    ulong DetailedCensusTicks,
    ulong BundleClearTicks,
    ulong ScheduleAttributedTicks,
    ulong ScheduleResidualTicks,
    ulong SchedulerSelectionTicks,
    ulong ProofParentTicks,
    double NetworkProtocolServerTicks,
    double NetworkAndRemoteParentTicks,
    double NetworkAndRemoteResidualTicks,
    string ReconciliationRule);
