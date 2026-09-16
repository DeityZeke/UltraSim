using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Game.Replication;
using UltraSim.Game.World;

internal static class Pf5R2R2ScalingAttribution
{
    private const string MeasurementBase =
        "89f26f8bbaa1003b15ddf52d61d6ce2567694b74";
    private const string ReplicationCandidate =
        "992fc541460f18534da9036c73cb711277de11ce";
    private const string V2Artifact =
        "_Archive/_V2/_Benchmarks/results/dense_hotspot_v1_shape_linux_4families_gc_pooled/" +
        "network_bot_harness_20260428_023430.json";
    private const string V2ShardedArtifact =
        "_Archive/_V2/_Benchmarks/results/dense_hotspot_new_runner_4families_60s/" +
        "sharded_20260428_031706/aggregate.json";

    internal static void Run(string[] args)
    {
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        bool matchedOnly = args.Contains("--matched-only", StringComparer.Ordinal);
        bool matchedDetailed = args.Contains("--matched-detailed", StringComparer.Ordinal);
        RemoteChunkSingletonDeltaCompression singletonDeltaCompression =
            ParseSingletonCompression(ReadText(args, "--singleton-compression"));
        int warmup = ReadInt(args, "--warmup", focused ? 8 : 30);
        int ticks = ReadInt(args, "--ticks", focused ? 24 : 270);
        int attributionTicks = ReadInt(args, "--attribution-ticks", focused ? 16 : 90);
        int[] populations = ParsePopulations(ReadText(args, "--populations") ??
            (focused ? "16" : "128,192,256,300,350"));
        if (warmup < 1 || ticks < 10 || attributionTicks < 10 ||
            populations.Any(value => value is < 2 or > 512))
            throw new ArgumentOutOfRangeException(nameof(args));

        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r2-r2-v2-v3-normalized-scaling.json";
        V2HistoricalEvidence historical = ReadHistoricalV2(V2Artifact);
        V2ShardedHistoricalEvidence shardedHistorical =
            ReadShardedHistoricalV2(V2ShardedArtifact);

        Prime(singletonDeltaCompression);
        var rows = new List<Pf5R2R2ScalingRow>(checked(populations.Length * 4));
        var matchedRows = new List<Pf5R2R2ScalingRow>(checked(populations.Length * 3));
        foreach (int population in populations)
        {
            if (!matchedOnly)
            {
                RunRated(Pf5R2R2Shape.V2PopulationV2Activity,
                    P03NetworkWorkload.ChunkV2Shaped(population, population, v2Activity: true));
                RunRated(Pf5R2R2Shape.V2PopulationCurrentActivity,
                    P03NetworkWorkload.ChunkV2Shaped(population, population, v2Activity: false));
                RunRated(Pf5R2R2Shape.ThousandEntitiesV2Activity,
                    P03NetworkWorkload.ChunkV2Shaped(population, 1_000, v2Activity: true));
                RunRated(Pf5R2R2Shape.CurrentProductionDense,
                    P03NetworkWorkload.ChunkProductionDense(population)
                        .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124));
            }

            int lowDirty = Math.Min(20, population);
            int highDirty = Math.Max(lowDirty,
                (int)Math.Round(population * .35, MidpointRounding.AwayFromZero));
            P03NetworkWorkload matchedPopulation =
                P03NetworkWorkload.ChunkMatchedControl(population, population, lowDirty);
            P03NetworkWorkload matchedThousandLow =
                P03NetworkWorkload.ChunkMatchedControl(population, 1_000, lowDirty);
            P03NetworkWorkload matchedThousandHigh =
                P03NetworkWorkload.ChunkMatchedControl(population, 1_000, highDirty);
            ValidateMatchedControls(
                matchedPopulation, matchedThousandLow, matchedThousandHigh);
            RunMatched(Pf5R2R2Shape.MatchedRecipientPopulation, matchedPopulation);
            RunMatched(Pf5R2R2Shape.MatchedThousandLowMutation, matchedThousandLow);
            RunMatched(Pf5R2R2Shape.MatchedThousandHighMutation, matchedThousandHigh);
        }

        int attributionPopulation = focused ? populations[0] :
            populations.Contains(300) ? 300 : populations[^1];
        var attribution = new List<Pf5R2R2ScalingRow>(4);
        if (!matchedOnly)
        {
            RunAttribution(Pf5R2R2Shape.V2PopulationV2Activity,
                P03NetworkWorkload.ChunkV2Shaped(
                    attributionPopulation, attributionPopulation, v2Activity: true));
            RunAttribution(Pf5R2R2Shape.V2PopulationCurrentActivity,
                P03NetworkWorkload.ChunkV2Shaped(
                    attributionPopulation, attributionPopulation, v2Activity: false));
            RunAttribution(Pf5R2R2Shape.ThousandEntitiesV2Activity,
                P03NetworkWorkload.ChunkV2Shaped(
                    attributionPopulation, 1_000, v2Activity: true));
            RunAttribution(Pf5R2R2Shape.CurrentProductionDense,
                P03NetworkWorkload.ChunkProductionDense(attributionPopulation)
                    .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124));
        }

        var report = new Pf5R2R2ScalingReport(
            "ultrasim-concept-demo-pf5-r2-r2-v2-v3-scaling-v2",
            DateTimeOffset.UtcNow,
            MeasurementBase,
            ReadGitSha(),
            ReplicationCandidate,
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            warmup,
            ticks,
            attributionTicks,
            "ChunkPublication; Message129WhenBeneficial; selective message148 LZ4 " +
                "canonical>=384/saving>=32; V3 production cadence 1/2/4; CrossLane cap 2; " +
                "Game manifest v29; transport revision 2; singleton compression " +
                singletonDeltaCompression,
            "RemotePublicationPreparation is the exact opt-in ChunkPublicationStore interval " +
                "which groups current-tick source changes and creates shared prepared " +
                "publications/records. The encompassing ApplyAuthority interval also owns " +
                "ordinary public-world commit/reconciliation and is not charged wholesale " +
                "to the remote budget.",
            "ServerOwnedRemoteReplicationBudget is formed for every tick by summing that " +
                "tick's non-overlapping RemotePublicationPreparation, " +
                "RecipientCommitObservation, and NetworkAndRemoteReplication ticks, then " +
                "calculating distributions over the resulting samples. No phase percentiles " +
                "are added.",
            "The V3 normalized replication fixture applies equivalent authoritative movement " +
                "mutations directly. V2 reapplied held movement intent every server tick, so " +
                "the 35-percent active mutation shape is appropriate even though new inputs " +
                "arrived at 20 Hz. V3 does not emit input-command messages here; modeled input " +
                "counts remain labels, not measured V3 transport work.",
            "V3's current public-world record is one unified latest-state family. The historical " +
                "V2 run independently published movement, visual, bulk, and ordered-event " +
                "families. Exact semantic-family parity is therefore unavailable without " +
                "inventing a non-production V3 protocol.",
            "V2 used a 1 m, 20-column grid. That can place more than V3's bounded 64 public " +
                "records in one 16 m publication chunk and is rejected during baseline. The " +
                "closest admissible V3 fixture uses a 2 m, 20-column grid, retaining dense " +
                "overlap while keeping every publication chunk within its production bound. " +
                "V2's active clients were the first 35% of indices; V3 distributes the same " +
                "active ratio across that grid because concentrating them can create one " +
                "canonical per-chunk delta larger than the production Game-message limit.",
            historical,
            shardedHistorical,
            new V2ContemporaryControl(
                "UnavailableUnchanged",
                "dotnet build _Archive/_V2/_Benchmarks/UltraSim.Network.BotHarness/" +
                    "UltraSim.Network.BotHarness.csproj -c Release --no-restore",
                "The unchanged archived build fails because current repository-wide MSBuild " +
                    "output rules cause archived generated obj trees to be included twice, " +
                    "producing duplicate TargetFramework and assembly attributes. No V2 " +
                    "source or project file was modified to obtain a contemporary number."),
            rows.ToArray(),
            attribution.ToArray(),
            "Matched controls use the same two-metre/20-column positions, V2 AOI, cadence " +
                "1/2/3, a common bounded distributed-prefix mutation placement, and production " +
                "protocol. The population " +
                "pair varies only E=R versus E=1000 at the same 20 common mutations; the " +
                "activity pair varies only 20 versus round(35% of R) common-prefix mutations " +
                "at E=1000.",
            matchedRows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        void RunRated(Pf5R2R2Shape shape, P03NetworkWorkload workload)
        {
            Console.WriteLine($"PF5-R2-R2 {shape} R={workload.Recipients} " +
                $"E={workload.Entities} dirty={workload.DirtyEntitiesPerTick}: rated");
            P03WholeFlowRow flow = RunHarness(
                workload, warmup, ticks, detailed: false, singletonDeltaCompression);
            VerifyServerBudget(flow, ticks);
            rows.Add(new Pf5R2R2ScalingRow(shape, false, flow));
            Console.WriteLine($"  game={flow.Timing.GameScheduleAndCodecQueue.P95:F3}ms " +
                $"server-budget={flow.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                $"whole={flow.Timing.Tick.P95:F3}ms pubs=" +
                $"{flow.ProfileCounters.SuccessfulSemanticPublications:N0}");
        }

        void RunAttribution(Pf5R2R2Shape shape, P03NetworkWorkload workload)
        {
            Console.WriteLine($"PF5-R2-R2 {shape} R={workload.Recipients}: attribution");
            P03WholeFlowRow flow = RunHarness(
                workload, Math.Min(warmup, 12), attributionTicks, detailed: true,
                singletonDeltaCompression);
            VerifyServerBudget(flow, attributionTicks);
            Require(flow.ProfileCounters.ChunkCpu.Enabled,
                "Detailed Chunk attribution was not enabled.");
            Require(flow.ProfileCounters.PreparedPublication.SourcePreparationTicks != 0,
                "Prepared source attribution was not observed.");
            attribution.Add(new Pf5R2R2ScalingRow(shape, true, flow));
        }

        void RunMatched(Pf5R2R2Shape shape, P03NetworkWorkload workload)
        {
            Console.WriteLine($"PF5-R2-R2 {shape} R={workload.Recipients} " +
                $"E={workload.Entities} dirty={workload.DirtyEntitiesPerTick}: matched");
            P03WholeFlowRow flow = RunHarness(
                workload, warmup, ticks, detailed: matchedDetailed,
                singletonDeltaCompression);
            VerifyServerBudget(flow, ticks);
            matchedRows.Add(new Pf5R2R2ScalingRow(shape, matchedDetailed, flow));
            Console.WriteLine($"  game={flow.Timing.GameScheduleAndCodecQueue.P95:F3}ms " +
                $"server-budget={flow.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                $"whole={flow.Timing.Tick.P95:F3}ms pubs=" +
                $"{flow.ProfileCounters.SuccessfulSemanticPublications:N0}");
        }
    }

    private static void ValidateMatchedControls(
        in P03NetworkWorkload population,
        in P03NetworkWorkload thousandLow,
        in P03NetworkWorkload thousandHigh)
    {
        Require(population.Recipients == thousandLow.Recipients &&
                thousandLow.Recipients == thousandHigh.Recipients &&
                population.DirtyEntitiesPerTick == thousandLow.DirtyEntitiesPerTick &&
                thousandLow.Entities == thousandHigh.Entities &&
                population.EntityLayout == thousandLow.EntityLayout &&
                thousandLow.EntityLayout == thousandHigh.EntityLayout &&
                population.MutationPattern == P03MutationPattern.MatchedDistributedPrefix &&
                thousandLow.MutationPattern == P03MutationPattern.MatchedDistributedPrefix &&
                thousandHigh.MutationPattern == P03MutationPattern.MatchedDistributedPrefix &&
                population.EntityInterest == thousandLow.EntityInterest &&
                thousandLow.EntityInterest == thousandHigh.EntityInterest &&
                population.ChunkInterest == thousandLow.ChunkInterest &&
                thousandLow.ChunkInterest == thousandHigh.ChunkInterest &&
                population.ChunkCadence == thousandLow.ChunkCadence &&
                thousandLow.ChunkCadence == thousandHigh.ChunkCadence,
            "PF5-R2-R2 matched-control invariants diverged.");
        Span<int> populationSelection = stackalloc int[population.DirtyEntitiesPerTick];
        Span<int> thousandSelection = stackalloc int[thousandLow.DirtyEntitiesPerTick];
        population.CopyDirtyEntityIndices(17, populationSelection);
        thousandLow.CopyDirtyEntityIndices(17, thousandSelection);
        Require(populationSelection.SequenceEqual(thousandSelection),
            "PF5-R2-R2 population control did not mutate the same common entities.");
        Span<int> highSelection = stackalloc int[thousandHigh.DirtyEntitiesPerTick];
        thousandHigh.CopyDirtyEntityIndices(17, highSelection);
        for (int index = 0; index < populationSelection.Length; index++)
            Require(highSelection.BinarySearch(populationSelection[index]) >= 0,
                "PF5-R2-R2 activity control did not preserve the low-mutation prefix.");
    }

    private static P03WholeFlowRow RunHarness(
        P03NetworkWorkload workload,
        int warmup,
        int ticks,
        bool detailed,
        RemoteChunkSingletonDeltaCompression singletonDeltaCompression)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var harness = new P03WholeFlowHarness(
            workload,
            P03NetworkProfile.ChunkPublication,
            2,
            stageTimingEnabled: true,
            gameDetailedAttribution: detailed,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            singletonDeltaCompression: singletonDeltaCompression,
            historyAudit: false,
            legacyRangeCensus: false,
            authorityThroughNetworkTimingEnabled: true,
            serverOwnedRemoteTimingEnabled: true,
            chunkReadModel: PreparedChunkPublicationReadModel.PreparedLatestState);
        P03WholeFlowRow flow = harness.Run(warmup, ticks);
        PreparedChunkPublicationSnapshot prepared = flow.ProfileCounters.PreparedPublication;
        Require(prepared.Selected && prepared.HistoryDerivedSteadyStateCalls == 0,
            "PF5-R2-R2 did not retain the selected shared prepared-publication path.");
        Require(flow.Converged && flow.Transport.CapacityDrops == 0,
            "PF5-R2-R2 fixture did not converge without capacity loss.");
        return flow;
    }

    private static void VerifyServerBudget(in P03WholeFlowRow flow, int ticks)
    {
        P03WholeFlowTiming timing = flow.Timing;
        Require(timing.RemotePublicationPreparation.Samples == ticks &&
                timing.RecipientCommitObservation.Samples == ticks &&
                timing.NetworkAndRemoteReplication.Samples == ticks &&
                timing.ServerOwnedRemoteReplicationBudget.Samples == ticks,
            "Server-owned timing sample cardinality diverged.");
        double expectedMean = timing.RemotePublicationPreparation.Mean +
            timing.RecipientCommitObservation.Mean +
            timing.NetworkAndRemoteReplication.Mean;
        Require(Math.Abs(expectedMean - timing.ServerOwnedRemoteReplicationBudget.Mean) < .000_001,
            "Server-owned budget was not formed from paired per-tick phase samples.");
        Require(timing.ServerOwnedRemoteReplicationBudget.P95 >=
                timing.NetworkAndRemoteReplication.P95 &&
                timing.ServerOwnedRemoteReplicationBudget.P95 >=
                timing.RemotePublicationPreparation.P95 &&
                timing.ServerOwnedRemoteReplicationBudget.P95 >=
                timing.RecipientCommitObservation.P95,
            "Server-owned p95 does not include every non-overlapping owned phase.");
    }

    private static void Prime(
        RemoteChunkSingletonDeltaCompression singletonDeltaCompression)
    {
        using var harness = new P03WholeFlowHarness(
            P03NetworkWorkload.ChunkV2Shaped(8, 8, v2Activity: true),
            P03NetworkProfile.ChunkPublication,
            2,
            stageTimingEnabled: false,
            gameDetailedAttribution: false,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            singletonDeltaCompression: singletonDeltaCompression,
            legacyRangeCensus: false);
        _ = harness.Run(4, 4);
    }

    private static V2HistoricalEvidence ReadHistoricalV2(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        JsonElement args = root.GetProperty("args");
        var stages = root.GetProperty("stages").EnumerateArray()
            .Select(stage => new V2HistoricalStage(
                stage.GetProperty("target_clients").GetInt32(),
                stage.GetProperty("active_clients").GetInt32(),
                stage.GetProperty("ready_clients").GetInt32(),
                stage.GetProperty("tick_avg_ms").GetDouble(),
                stage.GetProperty("tick_p95_ms").GetDouble(),
                stage.GetProperty("server_pump_avg_ms").GetDouble(),
                stage.GetProperty("server_pump_p95_ms").GetDouble(),
                stage.GetProperty("publish_avg_ms").GetDouble(),
                stage.GetProperty("publish_p95_ms").GetDouble(),
                stage.GetProperty("dispatch_avg_ms").GetDouble(),
                stage.GetProperty("dispatch_p95_ms").GetDouble(),
                stage.GetProperty("async_flush_previous_avg_ms").GetDouble(),
                stage.GetProperty("async_flush_previous_p95_ms").GetDouble(),
                stage.GetProperty("client_pump_avg_ms").GetDouble(),
                stage.GetProperty("client_pump_p95_ms").GetDouble(),
                stage.GetProperty("replication_published_items").GetInt64(),
                stage.GetProperty("replication_datagrams").GetInt64(),
                stage.GetProperty("replication_wire_datagrams").GetInt64(),
                stage.GetProperty("observer_frames_received").GetInt64(),
                stage.GetProperty("observer_events_received").GetInt64()))
            .ToArray();
        return new V2HistoricalEvidence(
            path,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            root.GetProperty("timestamp_utc").GetString() ?? "unknown",
            "One session-owned ECS movement entity per accepted client; no separate ambient " +
                "1,000-entity population is created by the harness.",
            "Main tick includes in-process client work. Async previous flush is separately " +
                "reported and cannot be percentile-added to main tick.",
            args.Clone(),
            stages);
    }

    private static V2ShardedHistoricalEvidence ReadShardedHistoricalV2(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        return new V2ShardedHistoricalEvidence(
            path,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            root.GetProperty("timestamp_utc").GetString() ?? "unknown",
            root.GetProperty("workers").GetInt32(),
            "Dedicated server process plus four client worker processes. Server tick and " +
                "previous background send are independent distributions and are not added.",
            root.GetProperty("stages").Clone());
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? text = ReadText(args, name);
        return text is null ? fallback : int.Parse(text, CultureInfo.InvariantCulture);
    }

    private static string? ReadText(string[] args, string name)
    {
        string prefix = name + "=";
        string? value = args.FirstOrDefault(item =>
            item.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? null : value[prefix.Length..];
    }

    private static int[] ParsePopulations(string text)
    {
        int[] values = text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length == 0 || values.Distinct().Count() != values.Length)
            throw new ArgumentOutOfRangeException(nameof(text));
        return values;
    }

    private static RemoteChunkSingletonDeltaCompression ParseSingletonCompression(string? text) =>
        text?.Trim().ToLowerInvariant() switch
        {
            null or "" or "off" or "disabled" =>
                RemoteChunkSingletonDeltaCompression.Disabled,
            "on" or "selected" or "when-beneficial" =>
                RemoteChunkSingletonDeltaCompression.WhenBeneficial,
            _ => throw new ArgumentException($"Unknown singleton compression '{text}'."),
        };

    private static string ReadGitSha()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            string value = process?.StandardOutput.ReadToEnd().Trim() ?? "unknown";
            process?.WaitForExit();
            return process?.ExitCode == 0 ? value : "unknown";
        }
        catch { return "unknown"; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal enum Pf5R2R2Shape : byte
{
    V2PopulationV2Activity = 1,
    V2PopulationCurrentActivity = 2,
    ThousandEntitiesV2Activity = 3,
    CurrentProductionDense = 4,
    MatchedRecipientPopulation = 5,
    MatchedThousandLowMutation = 6,
    MatchedThousandHighMutation = 7,
}

internal sealed record Pf5R2R2ScalingReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string MeasurementBase,
    string GitSha,
    string ReplicationSemanticCandidate,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessors,
    long StopwatchFrequency,
    int WarmupTicks,
    int MeasuredTicks,
    int AttributionTicks,
    string FrozenPolicy,
    string RemotePublicationPreparationDefinition,
    string ServerOwnedRemoteReplicationBudgetDefinition,
    string InputNormalizationLimitation,
    string SemanticFamilyNormalizationLimitation,
    string SpatialDensityNormalizationLimitation,
    V2HistoricalEvidence HistoricalV2,
    V2ShardedHistoricalEvidence HistoricalV2Sharded,
    V2ContemporaryControl ContemporaryV2,
    Pf5R2R2ScalingRow[] RatedRows,
    Pf5R2R2ScalingRow[] DetailedAttributionRows,
    string MatchedControlDefinition,
    Pf5R2R2ScalingRow[] MatchedControlRows);

internal sealed record Pf5R2R2ScalingRow(
    Pf5R2R2Shape Shape,
    bool DetailedAttribution,
    P03WholeFlowRow Flow);

internal sealed record V2HistoricalEvidence(
    string Artifact,
    string Sha256,
    string CapturedAtUtc,
    string EntityOwnership,
    string TimingBoundary,
    JsonElement Arguments,
    V2HistoricalStage[] Stages);

internal sealed record V2HistoricalStage(
    int TargetClients,
    int ActiveClients,
    int ReadyClients,
    double TickMeanMilliseconds,
    double TickP95Milliseconds,
    double ServerPumpMeanMilliseconds,
    double ServerPumpP95Milliseconds,
    double PublishMeanMilliseconds,
    double PublishP95Milliseconds,
    double DispatchMeanMilliseconds,
    double DispatchP95Milliseconds,
    double AsyncPreviousFlushMeanMilliseconds,
    double AsyncPreviousFlushP95Milliseconds,
    double ClientPumpMeanMilliseconds,
    double ClientPumpP95Milliseconds,
    long ReplicationPublishedItems,
    long ReplicationLogicalDatagrams,
    long ReplicationWireDatagrams,
    long ObserverFramesReceived,
    long ObserverEventsReceived);

internal sealed record V2ShardedHistoricalEvidence(
    string Artifact,
    string Sha256,
    string CapturedAtUtc,
    int ClientWorkerProcesses,
    string TimingBoundary,
    JsonElement Stages);

internal sealed record V2ContemporaryControl(
    string Status,
    string Command,
    string Result);
