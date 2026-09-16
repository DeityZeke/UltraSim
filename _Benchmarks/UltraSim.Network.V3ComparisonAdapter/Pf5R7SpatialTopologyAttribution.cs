using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Game.Replication;
using UltraSim.Game.World;

internal static class Pf5R7SpatialTopologyAttribution
{
    private const string StartingHead =
        "a6dfccc04c9253df266dd4fb582aa23a12fe8eae";
    private const string CorrectnessCandidate =
        "ddf454b36e387f84ebfeae70a665acf765d76402";
    private const string V2DenseArtifact =
        "_Archive/_V2/_Benchmarks/results/dense_hotspot_new_runner_4families_60s/" +
        "sharded_20260428_031706/aggregate.json";

    internal static void Run(string[] args)
    {
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        int warmup = ReadInt(args, "--warmup", focused ? 8 : 30);
        int ticks = ReadInt(args, "--ticks", focused ? 12 : 270);
        int censusTicks = ReadInt(args, "--census-ticks", focused ? 8 : 30);
        if (warmup < 1 || ticks < 8 || censusTicks < 4)
            throw new ArgumentOutOfRangeException(nameof(args));
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r7-spatial-topology-attribution.json";

        Prime();
        var topologyRows = new List<Pf5R7TopologyRow>();
        var v2ComparisonRows = new List<Pf5R7V2ComparisonRow>();
        var wholeServerRows = new List<Pf5R7WholeServerRow>();
        var authorityOnlyRows = new List<Pf5R7AuthorityOnlyRow>();
        var physicsRows = new List<Pf5R7PhysicsRow>();
        if (focused)
        {
            RunTopology(16, 16, 6, 1, "focused-dense");
            RunTopology(16, 16, 6, 4, "focused-medium");
            RunTopology(16, 16, 6, 16, "focused-sparse");
            RunTopology(16, 16, 6, 4, "focused-extent", inactiveStoredChunks: 64);
            RunAuthorityOnly(16);
            RunWholeServerMatrix(16);
            RunCollisionPhysics(16);
        }
        else
        {
            foreach (int groups in new[] { 1, 8, 64, 512 })
                RunTopology(512, 512, 179, groups, $"fixed-total-{groups}x{512 / groups}");

            foreach (int population in new[] { 256, 300, 350 })
            {
                RunTopology(population, population,
                    Math.Max(1, (int)Math.Round(population * .35)), 1, "dense");
                RunTopology(population, population,
                    Math.Max(1, (int)Math.Round(population * .35)),
                    Math.Min(8, population), "medium");
                RunTopology(population, population,
                    Math.Max(1, (int)Math.Round(population * .35)), population, "sparse");
            }

            foreach (int population in new[] { 384, 400, 450, 512 })
                RunTopology(population, 1_000, 20, 1, "low-change-dense");

            // This smaller synthetic control keeps recipient count, activity, AOI radii, and
            // per-recipient subscription geometry fixed while changing only how many separated
            // topology groups own the source truth. Total memberships remain approximately
            // matched (ordinary edge rounding is retained); this is characterization, not a
            // production-capacity row.
            RunTopology(64, 64, 20, 1, "controlled-fanout-1x64");
            RunTopology(64, 64, 20, 8, "controlled-fanout-8x8");
            RunTopology(64, 64, 20, 64, "controlled-fanout-64x1");
            RunTopology(64, 64, 20, 8, "extent-small");
            RunTopology(64, 64, 20, 8, "extent-large", inactiveStoredChunks: 4_096);
            foreach (int population in new[] { 256, 300, 350 })
            {
                RunV2Comparison(population, sparse: false);
                RunV2Comparison(population, sparse: true);
            }
            RunAuthorityOnly(200);
            RunWholeServerMatrix(200);
            RunCollisionPhysics(200);
            RunAuthorityOnly(350);
            RunWholeServerMatrix(350);
            RunCollisionPhysics(350);
        }

        V2DenseHistory v2 = ReadV2DenseHistory(V2DenseArtifact);
        var report = new Pf5R7SpatialReport(
            "ultrasim-concept-demo-pf5-r7-spatial-topology-v1",
            DateTimeOffset.UtcNow,
            StartingHead,
            ReadGitSha(),
            CorrectnessCandidate,
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            warmup,
            ticks,
            censusTicks,
            "BackgroundPlatformBatch; CurrentBundled; PF5-R6 body sharing enabled; " +
                "singleton compression disabled; Immediate/Near/Far 1/2/4; CrossLane cap 2; " +
                "Game manifest v29; transport revision 2",
            "In-process secure-UDP architecture harness. Server-owned phase clocks exclude the " +
                "measured client receive/apply slices, but process CPU/RSS include both sides and " +
                "must not be compared directly with the sharded V2 server process.",
            "R200/R350 whole-server rows compose the real current Game authority with the current " +
                "secure-UDP ChunkPublication path in one same-tick harness. The product admission " +
                "cap remains 64: these are benchmark-capacity participants, not admitted product " +
                "CCU. Game movement drives the accepted input/locomotion/physics path while a " +
                "deterministic public-world mirror supplies the replication fixture; actor " +
                "positions are not handed directly between those two bounded fixtures. Chat timing " +
                "uses the real GameChatService authority path but does not include remote Chat " +
                "encoding/delivery in this composed benchmark.",
            "Topology groups use ordinary production AOI and are separated by 192 metres. Each " +
                "group stores at most 20 public records per 16 m publication chunk. A group is a " +
                "spatial region/chunk-group, not necessarily one physical chunk.",
            "The controlled-fanout R64 rows use the same production AOI radius and approximately " +
                "matched total memberships while varying 1/8/64 source groups. Ordinary chunk-edge " +
                "rounding is retained, so exact membership and dirty-chunk fanout are reported and " +
                "the rows are synthetic attribution rather than product-capacity evidence.",
            "Rated rows run with detailed Game attribution off. Each row has a separate short " +
                "diagnostic census using identical semantic work; only its topology/body counters " +
                "are copied into the rated row.",
            "No archived sparse V2 fixture was found. The V2-shaped sparse rows are explicit " +
                "current-V3 reconstructions and are not historical V2 measurements.",
            v2,
            topologyRows.ToArray(),
            v2ComparisonRows.ToArray(),
            wholeServerRows.ToArray(),
            authorityOnlyRows.ToArray(),
            physicsRows.ToArray(),
            BuildInventory());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));

        return;

        void RunTopology(
            int recipients,
            int entities,
            int dirty,
            int groups,
            string label,
            int inactiveStoredChunks = 0)
        {
            P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(
                recipients, entities, dirty, groups, inactiveStoredChunks)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            Console.WriteLine($"PF5-R7 {label}: R={recipients} E={entities} " +
                $"groups={groups} dirty={dirty} inactive={inactiveStoredChunks}");
            P03WholeFlowRow rated = RunHarness(workload, warmup, ticks, detailed: false);
            P03WholeFlowRow census = RunHarness(
                workload, Math.Min(warmup, 8), censusTicks, detailed: true);
            Require(rated.ProfileCounters.PreparedPublication.HistoryDerivedSteadyStateCalls == 0,
                "PF5-R7 left the selected prepared latest-state path.");
            topologyRows.Add(new Pf5R7TopologyRow(
                label,
                groups,
                inactiveStoredChunks,
                rated,
                census.ProfileCounters));
            Console.WriteLine($"  remote p95=" +
                $"{rated.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                $"whole-flow p95={rated.Timing.Tick.P95:F3}ms " +
                $"memberships={census.ProfileCounters.CurrentMemberships:N0} " +
                $"bodies={census.ProfileCounters.DeltaBodies.DistinctBodyIdentities:N0} " +
                $"hit={BodyHitRate(census.ProfileCounters.DeltaBodies):P1}");
        }

        void RunV2Comparison(int population, bool sparse)
        {
            int active = Math.Max(1,
                (int)Math.Round(population * .35, MidpointRounding.AwayFromZero));
            P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(
                population, population, active, sparse ? population : 1)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            var game = new Pf5R7GameAuthorityWorkload(
                population, Pf5R7MovementShape.FreeMovement, chatEnabled: false);
            Console.WriteLine($"PF5-R7 V2-link R={population} " +
                (sparse ? "sparse" : "dense"));
            using var harness = new P03WholeFlowHarness(
                workload,
                P03NetworkProfile.ChunkPublication,
                2,
                stageTimingEnabled: true,
                gameDetailedAttribution: false,
                compression: RemoteChunkBundleCompressionPolicy.Selected,
                singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
                bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
                legacyRangeCensus: false,
                authorityThroughNetworkTimingEnabled: true,
                serverOwnedRemoteTimingEnabled: true,
                chunkReadModel: PreparedChunkPublicationReadModel.PreparedLatestState,
                socketIoMode: UltraSim.Network.SharedUdpSocketIoMode.PlatformSendReceiveBatch,
                backgroundEgressTimingEnabled: true,
                externalServerWorkload: game);
            P03WholeFlowRow flow = harness.Run(warmup, ticks);
            Require(flow.Converged && flow.Transport.CapacityDrops == 0,
                "PF5-R7 V2-link row did not converge.");
            v2ComparisonRows.Add(new Pf5R7V2ComparisonRow(
                population,
                sparse ? "V2ShapedSparseReconstruction" : "DenseHotspot",
                flow,
                (Pf5R7GameAuthoritySummary)game.CaptureSummary()));
            Console.WriteLine($"  server p95={flow.Timing.ServerOwnedTick.P95:F3}ms " +
                $"remote p95={flow.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                $"whole-flow p95={flow.Timing.Tick.P95:F3}ms");
        }

        void RunWholeServerMatrix(int population)
        {
            foreach (bool network in new[] { false, true })
                foreach (bool movement in new[] { false, true })
                    foreach (bool chat in new[] { false, true })
                    {
                        int active = Math.Max(1,
                            (int)Math.Round(population * .35, MidpointRounding.AwayFromZero));
                        int dirty = network ? (movement ? active : Math.Min(20, population)) : 1;
                        P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(
                            population, population, dirty, 1)
                            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
                        if (!network)
                            workload = workload with
                            {
                                Id = $"connected-idle-r{population}",
                                MutationPeriodTicks = 1_000_000,
                            };
                        var game = new Pf5R7GameAuthorityWorkload(
                            population,
                            movement ? Pf5R7MovementShape.FreeMovement :
                                Pf5R7MovementShape.Stationary,
                            chat,
                            chaosChat: chat);
                        Console.WriteLine($"PF5-R7 whole R={population} " +
                            $"N={(network ? 1 : 0)} M={(movement ? 1 : 0)} C={(chat ? 1 : 0)}");
                        using var harness = new P03WholeFlowHarness(
                            workload,
                            P03NetworkProfile.ChunkPublication,
                            2,
                            stageTimingEnabled: true,
                            gameDetailedAttribution: false,
                            compression: RemoteChunkBundleCompressionPolicy.Selected,
                            singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
                            bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
                            legacyRangeCensus: false,
                            authorityThroughNetworkTimingEnabled: true,
                            serverOwnedRemoteTimingEnabled: true,
                            chunkReadModel: PreparedChunkPublicationReadModel.PreparedLatestState,
                            socketIoMode: UltraSim.Network.SharedUdpSocketIoMode.PlatformSendReceiveBatch,
                            backgroundEgressTimingEnabled: true,
                            externalServerWorkload: game);
                        P03WholeFlowRow flow = harness.Run(warmup, ticks);
                        Require(flow.Converged && flow.Transport.CapacityDrops == 0,
                            "PF5-R7 whole-server cell did not converge.");
                        var summary = (Pf5R7GameAuthoritySummary)game.CaptureSummary();
                        Require(flow.Timing.ServerOwnedTick.Samples == ticks &&
                                flow.Timing.AttributedMainThread.Samples == ticks &&
                                flow.Timing.UnattributedResidual.Samples == ticks,
                            "PF5-R7 whole-server phase clocks lost tick alignment.");
                        wholeServerRows.Add(new Pf5R7WholeServerRow(
                            population, network, movement, chat, flow, summary));
                        Console.WriteLine($"  server p95={flow.Timing.ServerOwnedTick.P95:F3}ms " +
                            $"remote p95={flow.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                            $"physics p95={flow.Timing.LocomotionAndPhysics.P95:F3}ms " +
                            $"chat p95={flow.Timing.ChatAndCommands.P95:F3}ms " +
                            $"residual p95={flow.Timing.UnattributedResidual.P95:F3}ms");
                    }
        }

        void RunAuthorityOnly(int population)
        {
            using var game = new Pf5R7GameAuthorityWorkload(
                population, Pf5R7MovementShape.Stationary, chatEnabled: false);
            for (int tick = 1; tick <= warmup; tick++) _ = game.RunTick(tick, measure: false);
            game.ResetMeasurement();
            var server = new List<double>(ticks);
            var authority = new List<double>(ticks);
            var physics = new List<double>(ticks);
            var world = new List<double>(ticks);
            for (int tick = 1; tick <= ticks; tick++)
            {
                P03ExternalServerPhaseSample sample = game.RunTick(warmup + tick, measure: true);
                server.Add(ToMilliseconds(sample.ServerOwnedTicks));
                authority.Add(ToMilliseconds(sample.AuthorityAndEcsTicks));
                physics.Add(ToMilliseconds(sample.LocomotionAndPhysicsTicks));
                world.Add(ToMilliseconds(sample.WorldCommitTicks));
            }
            authorityOnlyRows.Add(new Pf5R7AuthorityOnlyRow(
                population,
                P03Metric.From(server),
                P03Metric.From(authority),
                P03Metric.From(physics),
                P03Metric.From(world),
                (Pf5R7GameAuthoritySummary)game.CaptureSummary()));
        }

        void RunCollisionPhysics(int population)
        {
            P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(
                population, population, 1, 1)
                .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124) with
                {
                    Id = $"collision-connected-idle-r{population}",
                    MutationPeriodTicks = 1_000_000,
                };
            var game = new Pf5R7GameAuthorityWorkload(
                population,
                Pf5R7MovementShape.CollisionChaos,
                chatEnabled: false);
            using var harness = new P03WholeFlowHarness(
                workload,
                P03NetworkProfile.ChunkPublication,
                2,
                compression: RemoteChunkBundleCompressionPolicy.Selected,
                singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
                bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
                legacyRangeCensus: false,
                serverOwnedRemoteTimingEnabled: true,
                socketIoMode: UltraSim.Network.SharedUdpSocketIoMode.PlatformSendReceiveBatch,
                externalServerWorkload: game);
            P03WholeFlowRow flow = harness.Run(warmup, ticks);
            physicsRows.Add(new Pf5R7PhysicsRow(
                population,
                "DenseConvergenceNoActorCollision",
                flow,
                (Pf5R7GameAuthoritySummary)game.CaptureSummary()));
        }
    }

    private static P03WholeFlowRow RunHarness(
        P03NetworkWorkload workload,
        int warmup,
        int ticks,
        bool detailed)
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
            singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
            bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
            historyAudit: false,
            legacyRangeCensus: false,
            authorityThroughNetworkTimingEnabled: true,
            serverOwnedRemoteTimingEnabled: true,
            chunkReadModel: PreparedChunkPublicationReadModel.PreparedLatestState,
            socketIoMode: UltraSim.Network.SharedUdpSocketIoMode.PlatformSendReceiveBatch,
            backgroundEgressTimingEnabled: true);
        P03WholeFlowRow row = harness.Run(warmup, ticks);
        Require(row.Converged && row.Transport.CapacityDrops == 0,
            "PF5-R7 topology fixture did not converge without capacity loss.");
        return row;
    }

    private static void Prime()
    {
        P03NetworkWorkload workload = P03NetworkWorkload.ChunkTopology(8, 8, 3, 1)
            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
        using var harness = new P03WholeFlowHarness(
            workload,
            P03NetworkProfile.ChunkPublication,
            2,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
            legacyRangeCensus: false,
            socketIoMode: UltraSim.Network.SharedUdpSocketIoMode.PlatformSendReceiveBatch);
        _ = harness.Run(3, 3);
    }

    private static Pf5R7SubsystemInventoryEntry[] BuildInventory() =>
    [
        new("ECS scheduler/tick marker", "main-thread tick work",
            "ServerRuntime.Tick -> EcsWorld.Tick"),
        new("session/input", "event-driven + connection-owner work",
            "HostedUdpAdmissionHost and RemoteGuestPlayableApplication message processing"),
        new("locomotion/physics", "main-thread tick work",
            "GameWorldActions.CommitLocomotionAfterEcsStep + UltraSimSimplePhysics"),
        new("world/publication commit", "main-thread tick work",
            "GameWorldState.CommitAfterEcsStep and ChunkPublicationStore preparation"),
        new("recipient scheduling/encoding/proof", "connection-owner server work",
            "ChunkPublicationWorkScheduler + RemoteChunkWorldStateServer"),
        new("UDP egress", "background/concurrent work",
            "bounded BackgroundPlatformBatch owner; completion/handoff remains separately measured"),
        new("chat/commands", "event-driven work",
            "GameChatService and RemoteChatDeliveryService"),
        new("persistence", "event-driven/inactive in rated topology fixture",
            "capture/restore exists; no regular persistence tick"),
        new("content HTTP", "event-driven background host work",
            "separate from UDP gameplay and inactive in warmed topology fixture"),
        new("diagnostics/admin", "cold/event-driven",
            "read-only snapshots; no mutable admin loop"),
        new("Sim Core", "inactive/not-yet-implemented",
            "no meaningful current production tick workload found"),
        new("pathfinding", "inactive/not-yet-implemented",
            "destination locomotion is direct current-physics movement, not a pathfinding system"),
        new("Dungeon Core", "inactive/not-yet-implemented",
            "no meaningful current production tick workload found"),
    ];

    private static V2DenseHistory ReadV2DenseHistory(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        V2DenseStage[] stages = document.RootElement.GetProperty("stages")
            .EnumerateArray()
            .Select(static stage => new V2DenseStage(
                stage.GetProperty("target_clients").GetInt32(),
                stage.GetProperty("server_tick_avg_ms").GetDouble(),
                stage.GetProperty("server_tick_p95_ms").GetDouble(),
                stage.GetProperty("server_tick_p99_ms").GetDouble(),
                stage.GetProperty("server_async_previous_send_p95_ms").GetDouble(),
                stage.GetProperty("server_replication_wire_datagrams").GetUInt64()))
            .ToArray();
        return new V2DenseHistory(
            path,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            "Separate server plus four client-worker processes; one session-owned movement entity " +
                "per accepted client; dense hotspot; four historical semantic families. Main " +
                "server tick and previous async send are separate distributions and are not added.",
            stages);
    }

    private static double BodyHitRate(in RemoteChunkDeltaBodyStoreSnapshot value) =>
        value.BodyRequests == 0 ? 0 : value.BodyHits / (double)value.BodyRequests;

    private static double ToMilliseconds(long ticks) =>
        ticks * 1_000d / Stopwatch.Frequency;

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? value = ReadText(args, name);
        return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
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

internal sealed record Pf5R7SpatialReport(
    string Checkpoint,
    DateTimeOffset CapturedUtc,
    string StartingHead,
    string MeasurementHead,
    string CorrectnessCandidate,
    string Runtime,
    string OperatingSystem,
    int LogicalCpuCount,
    long StopwatchFrequency,
    int WarmupTicks,
    int MeasuredTicks,
    int DiagnosticCensusTicks,
    string ProductionPolicy,
    string ProcessTopologyQualification,
    string WholeServerQualification,
    string TopologyDefinition,
    string ControlledFanoutQualification,
    string InstrumentationQualification,
    string ArchivedSparseFixtureDisposition,
    V2DenseHistory HistoricalV2Dense,
    Pf5R7TopologyRow[] TopologyRows,
    Pf5R7V2ComparisonRow[] V2ComparisonRows,
    Pf5R7WholeServerRow[] WholeServerRows,
    Pf5R7AuthorityOnlyRow[] AuthorityOnlyRows,
    Pf5R7PhysicsRow[] PhysicsRows,
    Pf5R7SubsystemInventoryEntry[] SubsystemInventory);

internal sealed record Pf5R7TopologyRow(
    string Label,
    int Groups,
    int InactiveStoredChunks,
    P03WholeFlowRow Rated,
    P03ProfileCounters DiagnosticCensus);

internal sealed record Pf5R7WholeServerRow(
    int Participants,
    bool NetworkReplication,
    bool MovementAndPhysics,
    bool Chat,
    P03WholeFlowRow Flow,
    Pf5R7GameAuthoritySummary Game);

internal sealed record Pf5R7V2ComparisonRow(
    int Participants,
    string Layout,
    P03WholeFlowRow Flow,
    Pf5R7GameAuthoritySummary Game);

internal sealed record Pf5R7AuthorityOnlyRow(
    int Participants,
    P03Metric ServerOwnedTick,
    P03Metric AuthorityAndEcs,
    P03Metric LocomotionAndPhysics,
    P03Metric WorldCommit,
    Pf5R7GameAuthoritySummary Game);

internal sealed record Pf5R7PhysicsRow(
    int Participants,
    string Workload,
    P03WholeFlowRow Flow,
    Pf5R7GameAuthoritySummary Game);

internal sealed record Pf5R7SubsystemInventoryEntry(
    string Subsystem,
    string Ownership,
    string CurrentImplementation);

internal sealed record V2DenseHistory(
    string Artifact,
    string Sha256,
    string Interpretation,
    V2DenseStage[] Stages);

internal sealed record V2DenseStage(
    int Clients,
    double ServerTickMeanMilliseconds,
    double ServerTickP95Milliseconds,
    double ServerTickP99Milliseconds,
    double PreviousAsyncSendP95Milliseconds,
    ulong WireDatagrams);
