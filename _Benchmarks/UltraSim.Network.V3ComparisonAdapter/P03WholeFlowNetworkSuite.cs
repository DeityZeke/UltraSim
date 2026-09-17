using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

/// <summary>
/// P0.3G whole-flow attribution. Every row uses the same real shared UDP listener, authenticated
/// protocol peers, Game codecs, client semantic application, and reverse application proof. It
/// deliberately excludes H0.4 owner-thread handoff and external H0.1/H0.2 admission; those remain
/// separately frozen hosted evidence rather than being silently approximated in-process.
/// </summary>
internal static class P03WholeFlowNetworkSuite
{
    private const int ExecutionSeed = 0x5037001;
    private static readonly P03NetworkProfile[] s_profiles = Enum.GetValues<P03NetworkProfile>();

    internal static void Run(string[] args)
    {
        if (args.Contains("--pf5-r2-r1-timing-boundary", StringComparer.Ordinal))
        {
            Pf5R2R1TimingBoundaryClosure.Run(args);
            return;
        }
        if (args.Contains("--pf5-r5-singleton-compression", StringComparer.Ordinal))
        {
            RunPf5R5SingletonCompression(args);
            return;
        }
        if (args.Contains("--pf5-r6-body-sharing", StringComparer.Ordinal))
        {
            RunPf5R6BodySharing(args);
            return;
        }
        if (args.Contains("--pf5-r3-background-egress", StringComparer.Ordinal))
        {
            RunPf5R3BackgroundEgress(args);
            return;
        }
        if (args.Contains("--pf5-r2-shared-publication", StringComparer.Ordinal))
        {
            Pf5R2SharedPublicationAudit.Run(args);
            return;
        }
        if (args.Contains("--pf5-r1-history-audit", StringComparer.Ordinal))
        {
            Pf5R1HistoryAudit.Run(args);
            return;
        }
        if (args.Contains("--pf4f-compression-oracle", StringComparer.Ordinal))
        {
            Pf4fCompressionOracle.Run(args);
            return;
        }
        if (args.Contains("--pf4e-r2-staging-microbench", StringComparer.Ordinal))
        {
            RunChunkStagingMicrobenchmark(args);
            return;
        }
        if (args.Contains("--pf4e-r2-chunk-cpu", StringComparer.Ordinal))
        {
            RunChunkCpuEfficiency(args);
            return;
        }
        if (args.Contains("--pf4e-r1-rated-reconciliation", StringComparer.Ordinal))
        {
            RunRatedReconciliation(args);
            return;
        }
        if (args.Contains("--p03h-rated-server-role-sweep", StringComparer.Ordinal))
        {
            RunRatedServerRoleSweep(args);
            return;
        }
        int denseRecipients = ReadInt(args, "--dense-recipients", 400);
        int denseEntities = ReadInt(args, "--dense-entities", 1_000);
        int sparsePopulation = ReadInt(args, "--sparse-population", 512);
        int warmup = ReadInt(args, "--warmup", 30);
        int ticks = ReadInt(args, "--ticks", 90);
        string? serverTickMillisecondsText = ReadText(args, "--server-tick-milliseconds");
        string? tickMillisecondsText = serverTickMillisecondsText ??
            ReadText(args, "--tick-milliseconds");
        int tickMilliseconds = tickMillisecondsText is null
            ? 16
            : int.Parse(tickMillisecondsText, System.Globalization.CultureInfo.InvariantCulture);
        bool overrideTickCadence = tickMillisecondsText is not null;
        P03AoICadencePolicy? aoiCadence = ParseAoICadence(ReadText(args, "--aoi-cadence"));
        bool stageTimingEnabled = !args.Contains("--probes-off", StringComparer.Ordinal);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/Baselines/network-p0.3g-whole-flow-attribution.json";
        P03NetworkProfile[] profiles = ParseProfiles(ReadText(args, "--profiles"));
        int[] caps = ParseCaps(ReadText(args, "--frame-caps"));
        string shape = ReadText(args, "--shape") ?? "all";
        P03NetworkWorkload[] workloads = shape.ToLowerInvariant() switch
        {
            "dense" => [Configure(P03NetworkWorkload.Dense(denseRecipients, denseEntities))],
            "sparse" => [Configure(P03NetworkWorkload.Sparse(sparsePopulation))],
            "all" => [Configure(P03NetworkWorkload.Dense(denseRecipients, denseEntities)),
                Configure(P03NetworkWorkload.Sparse(sparsePopulation))],
            _ => throw new ArgumentException($"Unknown whole-flow shape '{shape}'."),
        };
        if (warmup < 1 || ticks < 10 || tickMilliseconds <= 0 ||
            denseRecipients <= 0 || denseEntities <= 0 ||
            sparsePopulation <= 0 || caps.Any(value => value is not (1 or 2 or 4 or 8)))
            throw new ArgumentOutOfRangeException(nameof(args));

        var cases = new List<P03WholeFlowCase>(workloads.Length * profiles.Length * caps.Length);
        foreach (P03NetworkWorkload workload in workloads)
        foreach (P03NetworkProfile profile in profiles)
        foreach (int cap in caps)
            cases.Add(new P03WholeFlowCase(workload, profile, cap));
        var random = new Random(ExecutionSeed);
        for (int index = cases.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (cases[index], cases[other]) = (cases[other], cases[index]);
        }

        var rows = new List<P03WholeFlowRow>(cases.Count);
        for (int executionOrder = 0; executionOrder < cases.Count; executionOrder++)
        {
            P03WholeFlowCase run = cases[executionOrder];
            P03NetworkWorkload workload = run.Workload;
            P03NetworkProfile profile = run.Profile;
            int cap = run.FrameCap;
            Console.WriteLine($"WHOLE FLOW {workload.Id} {profile} frames={cap}: constructing...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var harness = new P03WholeFlowHarness(
                workload, profile, cap, stageTimingEnabled);
            P03WholeFlowRow row = harness.Run(warmup, ticks) with
            {
                ExecutionOrder = executionOrder,
            };
            rows.Add(row);
            Console.WriteLine($"WHOLE FLOW {workload.Id} {profile} frames={cap}: " +
                $"tick p95 {row.Timing.Tick.P95:F3} ms, server {row.Timing.ServerRole.P95:F3} ms, " +
                $"datagrams {row.Transport.DataDatagrams:N0}, frames/datagram " +
                $"{row.Transport.FramesPerDatagram:F2}, wire {row.Transport.WireBytes / 1048576d:F2} MiB");
        }
        var report = new P03WholeFlowReport(
            "ultrasim-network-p03g-whole-flow-attribution-v4",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new P03WholeFlowScope(
                "Timed path: authoritative mutations and recipient-anchor updates; concrete Game " +
                "profile scheduling and canonical codec; Network retained queue; lane selection; " +
                "frame encoding; AES-GCM; real shared UDP listener with sendmmsg/recvmmsg where " +
                "available; client decrypt/admission; concrete semantic application; reverse " +
                "application proof; server proof interpretation.",
                "Cold host/session construction, ECDH handshake, initial replication/bootstrap, " +
                "WorldPack construction, semantic verification, snapshots, and final drain are " +
                "outside timed ticks. H0.4 connection-owner handoff, external admission, public WAN, " +
                "loss, and slow-client behavior are excluded and remain separate evidence gates.",
                "A comparison-only manifest negotiates up to eight unreliable frames. Local send " +
                "caps 1/2/4/8 are the only A/B policy change; the production selected policy is " +
                "cross-lane cap two. " +
                $"Protocol/listener stage probes were {(stageTimingEnabled ? "enabled" : "disabled")}. " +
                $"The server semantic tick duration was {tickMilliseconds} ms. " +
                $"AOI cadence was {(aoiCadence?.ToString() ?? "the workload default")}."),
            ExecutionSeed, warmup, ticks, rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        P03NetworkWorkload Configure(P03NetworkWorkload workload) => aoiCadence.HasValue
            ? workload.WithServerTickAndAoICadence(tickMilliseconds, aoiCadence.Value)
            : overrideTickCadence
                ? workload.WithTickDuration(tickMilliseconds)
                : workload;
    }

    private static void RunPf5R5SingletonCompression(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 60);
        int ticks = ReadInt(args, "--ticks", 270);
        int[] populations = ParseOptionalPositiveIntegers(ReadText(args, "--populations")) ??
            [64, 128, 192, 256, 300, 350];
        bool detailed = args.Contains("--game-detailed", StringComparer.Ordinal);
        bool highOnly = args.Contains("--high-only", StringComparer.Ordinal);
        bool egressTiming = !string.Equals(
            ReadText(args, "--egress-timing"), "off", StringComparison.OrdinalIgnoreCase);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/network-pf5-r5-singleton-compression.json";
        if (warmup < 1 || ticks < 10)
            throw new ArgumentOutOfRangeException(nameof(args));

        RemoteChunkSingletonDeltaCompression[] policies =
        [
            RemoteChunkSingletonDeltaCompression.Disabled,
            RemoteChunkSingletonDeltaCompression.WhenBeneficial,
        ];
        var cases = new List<(int Population, bool High,
            RemoteChunkSingletonDeltaCompression Policy)>(populations.Length * 4);
        foreach (int population in populations)
        foreach (bool high in highOnly ? new[] { true } : new[] { false, true })
        foreach (RemoteChunkSingletonDeltaCompression policy in policies)
            cases.Add((population, high, policy));
        var random = new Random(ExecutionSeed ^ 0x5F50_5005);
        for (int index = cases.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (cases[index], cases[other]) = (cases[other], cases[index]);
        }

        foreach (RemoteChunkSingletonDeltaCompression policy in policies)
        {
            using var prime = CreateHarness(
                Math.Min(populations[0], 32), high: true, policy);
            _ = prime.Run(Math.Max(30, warmup), 10);
        }

        var rows = new List<P03WholeFlowRow>(cases.Count);
        for (int executionOrder = 0; executionOrder < cases.Count; executionOrder++)
        {
            (int population, bool high, RemoteChunkSingletonDeltaCompression policy) =
                cases[executionOrder];
            Console.WriteLine($"PF5-R5 {(high ? "high" : "low")}/{policy}/{population}: " +
                "constructing...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using P03WholeFlowHarness harness = CreateHarness(population, high, policy);
            P03WholeFlowRow row = harness.Run(warmup, ticks) with
            {
                ExecutionOrder = executionOrder,
                Curve = $"pf5-r5-{(high ? "high" : "low")}-{policy}",
            };
            P03WholeFlowHarness.Require(row.Converged && row.Transport.CapacityDrops == 0,
                "PF5-R5 real-UDP row did not converge without capacity loss.");
            rows.Add(row);
            Console.WriteLine($"PF5-R5 {(high ? "high" : "low")}/{policy}/{population}: " +
                $"remote p95={row.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                $"120={row.ProfileCounters.Compression.SingletonDeltaCandidates - row.ProfileCounters.Compression.SingletonSelected:N0} " +
                $"singleton148={row.ProfileCounters.Compression.SingletonSelected:N0} " +
                $"fallback_writes={row.ProfileCounters.Compression.StandaloneFallbackWrites:N0} " +
                $"app={row.ProfileCounters.ApplicationBytesQueued:N0} " +
                $"wire={row.Transport.WireBytes:N0}");
        }

        var report = new P03WholeFlowReport(
            "ultrasim-network-pf5-r5-singleton-compression-v1",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new P03WholeFlowScope(
                "Matched E=1000 low dirty=20 and high dirty=round(35% of R) controls over real " +
                    "secure loopback UDP, production BackgroundPlatformBatch egress, client " +
                    "decode/application, and exact application proof.",
                "Construction, admission, bootstrap, warmup, and final convergence drain are " +
                    "excluded. Server and clients share one process, so ProcessCpu includes both; " +
                    "ServerOwnedRemoteReplicationBudget excludes client work and background send.",
                $"PF5-R5 singleton compression Off versus WhenBeneficial; canonical 129 >= " +
                    $"{RemoteChunkBundleCompressionPolicy.Selected.MinimumCanonicalBytes} bytes; " +
                    $"minimum saving versus actual 120 = " +
                    $"{RemoteChunkBundleCompressionPolicy.Selected.MinimumNetSavingsBytes} bytes; " +
                    $"BackgroundPlatformBatch; CrossLane cap two; {warmup} warmup and {ticks} " +
                    $"measured ticks; seed {ExecutionSeed}."),
            ExecutionSeed,
            warmup,
            ticks,
            rows.OrderBy(row => row.Recipients).ThenBy(row => row.Curve).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        P03WholeFlowHarness CreateHarness(
            int population,
            bool high,
            RemoteChunkSingletonDeltaCompression policy)
        {
            int dirty = high
                ? Math.Max(20, (int)Math.Round(
                    population * .35, MidpointRounding.AwayFromZero))
                : 20;
            P03NetworkWorkload workload = P03NetworkWorkload.
                ChunkMatchedControl(population, 1_000, dirty).
                WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            return new P03WholeFlowHarness(
                workload, P03NetworkProfile.ChunkPublication, 2,
                stageTimingEnabled: false,
                gameDetailedAttribution: detailed,
                compression: RemoteChunkBundleCompressionPolicy.Selected,
                singletonDeltaCompression: policy,
                serverOwnedRemoteTimingEnabled: true,
                socketIoMode: SharedUdpSocketIoMode.BackgroundPlatformBatch,
                backgroundEgressTimingEnabled: egressTiming);
        }
    }

    private static void RunPf5R6BodySharing(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 60);
        int ticks = ReadInt(args, "--ticks", 270);
        int[] populations = ParseOptionalPositiveIntegers(ReadText(args, "--populations")) ??
            [64, 128, 192, 256, 300, 350];
        bool detailed = args.Contains("--game-detailed", StringComparer.Ordinal);
        bool includeCensus = args.Contains("--include-census", StringComparer.Ordinal);
        bool censusOnly = args.Contains("--census-only", StringComparer.Ordinal);
        bool includeDivergence = args.Contains("--include-divergence", StringComparer.Ordinal);
        int divergenceRecipients = ReadInt(args, "--divergence-recipients", 64);
        int divergenceTicks = ReadInt(args, "--divergence-ticks", 180);
        if (includeCensus && censusOnly)
            throw new ArgumentException("Choose either --include-census or --census-only.");
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/network-pf5-r6-body-sharing.json";
        if (warmup < 1 || ticks < 10 || divergenceRecipients is < 2 or > 512 ||
            divergenceTicks < 20)
            throw new ArgumentOutOfRangeException(nameof(args));

        RemoteChunkDeltaBodySharingMode[] modes = censusOnly
            ? [RemoteChunkDeltaBodySharingMode.CensusOnly]
            : includeCensus
            ? [RemoteChunkDeltaBodySharingMode.Disabled,
                RemoteChunkDeltaBodySharingMode.CensusOnly,
                RemoteChunkDeltaBodySharingMode.Enabled]
            : [RemoteChunkDeltaBodySharingMode.Disabled,
                RemoteChunkDeltaBodySharingMode.Enabled];
        var cases = new List<(int Population, bool High,
            RemoteChunkDeltaBodySharingMode Mode)>(populations.Length * modes.Length * 2);
        foreach (int population in populations)
        foreach (bool high in new[] { false, true })
        foreach (RemoteChunkDeltaBodySharingMode mode in modes)
            cases.Add((population, high, mode));
        var random = new Random(ExecutionSeed ^ 0x5F60_6006);
        for (int index = cases.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (cases[index], cases[other]) = (cases[other], cases[index]);
        }

        foreach (RemoteChunkDeltaBodySharingMode mode in modes)
        {
            using var prime = CreateHarness(Math.Min(populations[0], 32), high: true, mode);
            _ = prime.Run(Math.Max(30, warmup), 10);
        }

        var rows = new List<P03WholeFlowRow>(cases.Count);
        for (int executionOrder = 0; executionOrder < cases.Count; executionOrder++)
        {
            (int population, bool high, RemoteChunkDeltaBodySharingMode mode) =
                cases[executionOrder];
            Console.WriteLine($"PF5-R6 {(high ? "high" : "low")}/{mode}/{population}: " +
                "constructing...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using P03WholeFlowHarness harness = CreateHarness(population, high, mode);
            P03WholeFlowRow row = harness.Run(warmup, ticks) with
            {
                ExecutionOrder = executionOrder,
                Curve = $"pf5-r6-{(high ? "high" : "low")}-{mode}",
            };
            P03WholeFlowHarness.Require(row.Converged && row.Transport.CapacityDrops == 0,
                "PF5-R6 real-UDP row did not converge without capacity loss.");
            rows.Add(row);
            RemoteChunkDeltaBodyStoreSnapshot bodies = row.ProfileCounters.DeltaBodies;
            Console.WriteLine($"PF5-R6 {(high ? "high" : "low")}/{mode}/{population}: " +
                $"remote p95={row.Timing.ServerOwnedRemoteReplicationBudget.P95:F3}ms " +
                $"requests={bodies.BodyRequests:N0} distinct={bodies.DistinctBodyIdentities:N0} " +
                $"fills={bodies.BodyFills:N0} " +
                $"hits={bodies.BodyHits:N0} records={bodies.BodyRecordsServed:N0}/" +
                $"{bodies.BodyRecordsBuilt:N0} bundles=" +
                $"{row.ProfileCounters.ChunkBundleMessages:N0}/" +
                $"{row.ProfileCounters.ChunkBundleEntries:N0} compressed=" +
                $"{row.ProfileCounters.Compression.Selected:N0} canonical=" +
                $"{row.ProfileCounters.Compression.CanonicalBytesSelected:N0} selected=" +
                $"{row.ProfileCounters.Compression.CompressedRepresentationBytes:N0} " +
                $"wire={row.Transport.WireBytes:N0}");
        }

        if (!censusOnly)
        foreach (IGrouping<(int Recipients, int Dirty), P03WholeFlowRow> group in rows.GroupBy(
                     row => (row.Recipients, row.DirtyEntitiesPerTick)))
        {
            P03WholeFlowRow off = group.Single(row => row.Curve.EndsWith(
                $"-{RemoteChunkDeltaBodySharingMode.Disabled}", StringComparison.Ordinal));
            P03WholeFlowRow on = group.Single(row => row.Curve.EndsWith(
                $"-{RemoteChunkDeltaBodySharingMode.Enabled}", StringComparison.Ordinal));
            ulong offCanonicalApplicationBytes = checked(
                off.ProfileCounters.ApplicationBytesQueued -
                off.ProfileCounters.Compression.CompressedRepresentationBytes +
                off.ProfileCounters.Compression.CanonicalBytesSelected);
            ulong onCanonicalApplicationBytes = checked(
                on.ProfileCounters.ApplicationBytesQueued -
                on.ProfileCounters.Compression.CompressedRepresentationBytes +
                on.ProfileCounters.Compression.CanonicalBytesSelected);
            P03WholeFlowHarness.Require(
                off.SemanticDigest == on.SemanticDigest &&
                off.ProfileCounters.SuccessfulSemanticPublications ==
                    on.ProfileCounters.SuccessfulSemanticPublications &&
                off.ProfileCounters.ProofEntries == on.ProfileCounters.ProofEntries &&
                off.ProfileCounters.GameMessagesQueued == on.ProfileCounters.GameMessagesQueued &&
                off.ProfileCounters.ChunkBundleMessages ==
                    on.ProfileCounters.ChunkBundleMessages &&
                off.ProfileCounters.ChunkBundleEntries ==
                    on.ProfileCounters.ChunkBundleEntries &&
                off.ProfileCounters.Compression.Selected ==
                    on.ProfileCounters.Compression.Selected &&
                off.ProfileCounters.Compression.CanonicalBytesSelected ==
                    on.ProfileCounters.Compression.CanonicalBytesSelected &&
                offCanonicalApplicationBytes == onCanonicalApplicationBytes,
                $"PF5-R6 body sharing changed semantic or canonical wire work: " +
                $"digest {off.SemanticDigest}/{on.SemanticDigest}, publications " +
                $"{off.ProfileCounters.SuccessfulSemanticPublications}/" +
                $"{on.ProfileCounters.SuccessfulSemanticPublications}, proofs " +
                $"{off.ProfileCounters.ProofEntries}/{on.ProfileCounters.ProofEntries}, messages " +
                $"{off.ProfileCounters.GameMessagesQueued}/" +
                $"{on.ProfileCounters.GameMessagesQueued}, canonical application bytes " +
                $"{offCanonicalApplicationBytes}/{onCanonicalApplicationBytes}, physical " +
                $"application bytes {off.ProfileCounters.ApplicationBytesQueued}/" +
                $"{on.ProfileCounters.ApplicationBytesQueued}.");
        }

        Pf5R1ScenarioReport[] divergence = [];
        if (includeDivergence)
        {
            string[] scenarios =
            [
                "synchronized", "staggered-joins", "cadence-phases", "controlled-loss",
                "controlled-reorder", "proof-delay", "temporary-stalls", "aoi-churn",
                "reconnect", "history-pressure", "combined-divergence",
            ];
            divergence = new Pf5R1ScenarioReport[scenarios.Length];
            for (int index = 0; index < scenarios.Length; index++)
            {
                divergence[index] = Pf5R1HistoryAudit.RunPreparedBodyScenarioForPf5R6(
                    scenarios[index], divergenceRecipients, divergenceTicks,
                    ExecutionSeed ^ 0x5F60_6016);
                RemoteChunkDeltaBodyStoreSnapshot body =
                    divergence[index].CompatibleBodies;
                Console.WriteLine($"PF5-R6 divergence {scenarios[index]}: " +
                    $"requests={body.BodyRequests:N0} fills={body.BodyFills:N0} " +
                    $"hits={body.BodyHits:N0} evictions={body.BodyEvictions:N0}");
            }
        }

        var report = new Pf5R6BodySharingReport(
            "ultrasim-network-pf5-r6-compatible-body-sharing-v1",
            DateTimeOffset.UtcNow, ReadGitSha(), Environment.Version.ToString(),
            Environment.OSVersion.VersionString, Environment.ProcessorCount,
            Stopwatch.Frequency,
            new P03WholeFlowScope(
                "Matched E=1000 low dirty=20 and high dirty=round(35% of R) controls over real " +
                    "secure loopback UDP, production BackgroundPlatformBatch egress, client " +
                    "decode/application, and exact independent application proof.",
                "Construction, admission, bootstrap, warmup, and final convergence drain are " +
                    "excluded. The body store is source-lifetime-owned and never supplies a Delta " +
                    "until the existing recipient read transaction authorizes the exact range.",
                $"PF5-R6 sharing Off/Census/On as selected; singleton compression disabled; " +
                    $"multi-entry 148 remains 384/32; cadence 1/2/4; CrossLane cap two; " +
                    $"{warmup} warmup and {ticks} measured ticks; seed {ExecutionSeed}."),
            ExecutionSeed, warmup, ticks,
            rows.OrderBy(row => row.Recipients).ThenBy(row => row.DirtyEntitiesPerTick)
                .ThenBy(row => row.Curve).ToArray(), divergence);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        P03WholeFlowHarness CreateHarness(
            int population,
            bool high,
            RemoteChunkDeltaBodySharingMode mode)
        {
            int dirty = high
                ? Math.Max(20, (int)Math.Round(
                    population * .35, MidpointRounding.AwayFromZero))
                : 20;
            P03NetworkWorkload workload = P03NetworkWorkload.
                ChunkMatchedControl(population, 1_000, dirty).
                WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            return new P03WholeFlowHarness(
                workload, P03NetworkProfile.ChunkPublication, 2,
                stageTimingEnabled: false,
                gameDetailedAttribution: detailed,
                compression: RemoteChunkBundleCompressionPolicy.Selected,
                singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
                bodySharing: mode,
                serverOwnedRemoteTimingEnabled: true,
                socketIoMode: SharedUdpSocketIoMode.BackgroundPlatformBatch,
                backgroundEgressTimingEnabled: true);
        }
    }

    private static void RunPf5R3BackgroundEgress(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 60);
        int ticks = ReadInt(args, "--ticks", 270);
        int tickMilliseconds = ReadInt(args, "--server-tick-milliseconds", 16);
        int burstPeriod = ReadInt(args, "--dirty-burst-period", 1);
        bool egressTimingEnabled = !string.Equals(
            ReadText(args, "--egress-timing"), "off", StringComparison.OrdinalIgnoreCase);
        bool gameDetailedAttribution = args.Contains("--game-detailed", StringComparer.Ordinal);
        int[] populations = ParseOptionalPositiveIntegers(ReadText(args, "--populations")) ??
            [64, 128, 192, 256, 300];
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/network-pf5-r3-background-egress.json";
        SharedUdpSocketIoMode[] modes = ParsePf5R3SocketModes(ReadText(args, "--socket-modes"));
        if (warmup < 1 || ticks < 10 || tickMilliseconds <= 0 ||
            burstPeriod is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(args));

        var cases = new List<(int Population, SharedUdpSocketIoMode Mode)>(
            checked(populations.Length * modes.Length));
        foreach (int population in populations)
        foreach (SharedUdpSocketIoMode mode in modes)
            cases.Add((population, mode));
        var random = new Random(ExecutionSeed ^ 0x5F3E_6001);
        for (int index = cases.Count - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (cases[index], cases[other]) = (cases[other], cases[index]);
        }

        foreach (SharedUdpSocketIoMode mode in modes)
        {
            using var prime = new P03WholeFlowHarness(
                CreateWorkload(Math.Min(populations[0], 32)),
                P03NetworkProfile.ChunkPublication, 2,
                stageTimingEnabled: false,
                gameDetailedAttribution: gameDetailedAttribution,
                compression: RemoteChunkBundleCompressionPolicy.Selected,
                serverOwnedRemoteTimingEnabled: true,
                socketIoMode: mode,
                backgroundEgressTimingEnabled: egressTimingEnabled);
            _ = prime.Run(Math.Max(30, warmup), 10);
        }

        var rows = new List<P03WholeFlowRow>(cases.Count);
        for (int executionOrder = 0; executionOrder < cases.Count; executionOrder++)
        {
            (int population, SharedUdpSocketIoMode mode) = cases[executionOrder];
            P03NetworkWorkload workload = CreateWorkload(population);
            Console.WriteLine($"PF5-R3 {mode}/{population}: constructing...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var harness = new P03WholeFlowHarness(
                workload, P03NetworkProfile.ChunkPublication, 2,
                stageTimingEnabled: false,
                gameDetailedAttribution: gameDetailedAttribution,
                compression: RemoteChunkBundleCompressionPolicy.Selected,
                serverOwnedRemoteTimingEnabled: true,
                socketIoMode: mode,
                backgroundEgressTimingEnabled: egressTimingEnabled);
            P03WholeFlowRow row = harness.Run(warmup, ticks) with
            {
                ExecutionOrder = executionOrder,
                Curve = "pf5-r3-production-shaped",
            };
            rows.Add(row);
            Console.WriteLine($"PF5-R3 {mode}/{population}: remote-budget p95 " +
                $"{row.Timing.ServerOwnedRemoteReplicationBudget.P95:F3} ms, " +
                $"p99 {row.Timing.ServerOwnedRemoteReplicationBudget.P99:F3} ms, " +
                $"queue-high-water {row.BackgroundEgress.HighWaterDatagrams:N0}, " +
                $"wire {row.Transport.WireBytes / 1048576d:F2} MiB");
        }

        foreach (IGrouping<int, P03WholeFlowRow> populationRows in
                 rows.GroupBy(row => row.Recipients))
        {
            P03WholeFlowRow first = populationRows.First();
            foreach (P03WholeFlowRow row in populationRows.Skip(1))
            {
                P03WholeFlowHarness.Require(
                    StringComparer.Ordinal.Equals(first.SemanticDigest, row.SemanticDigest),
                    "PF5-R3 socket modes changed the authoritative semantic digest.");
                P03WholeFlowHarness.Require(
                    first.ProfileCounters.SuccessfulSemanticPublications ==
                        row.ProfileCounters.SuccessfulSemanticPublications &&
                    first.ProfileCounters.ProofEntries == row.ProfileCounters.ProofEntries &&
                    first.ProfileCounters.GameMessagesQueued ==
                        row.ProfileCounters.GameMessagesQueued &&
                    first.ProfileCounters.ChunkBundleEntries ==
                        row.ProfileCounters.ChunkBundleEntries,
                    "PF5-R3 socket modes changed semantic publication/proof work.");
            }
        }

        var report = new Pf5R3BackgroundEgressReport(
            "ultrasim-network-pf5-r3-background-egress-v1",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            ExecutionSeed,
            warmup,
            ticks,
                "Current PF5-R2 shared prepared-latest-state ChunkPublication; message 129 when " +
                "beneficial; selective message-148 LZ4 at 384/32; Immediate/Near/Far 1/2/4; " +
                "CrossLane unreliable aggregation cap two; real secure loopback UDP; " +
                $"dirty burst period {burstPeriod} (equal long-window source-row rate).",
            "ServerOwnedRemoteReplicationBudget is the per-tick sum of shared publication " +
                "preparation, recipient commit observation, Game scheduling/encoding/proof, " +
                "Network protocol work, and listener submission. Background worker physical " +
                "send time and client work are excluded from that main-thread metric.",
            "Background byte/count fields are exact measurement-window deltas. Queue latency " +
                "histogram percentiles are bounded worker-lifetime observations including " +
                "bootstrap and warmup because production telemetry is cumulative. Detailed " +
                $"background timing was {(egressTimingEnabled ? "enabled" : "disabled")}; " +
                $"Game detailed attribution was {(gameDetailedAttribution ? "enabled" : "disabled")}.",
            rows.OrderBy(row => row.Recipients)
                .ThenBy(row => (int)Enum.Parse<SharedUdpSocketIoMode>(row.SocketIoMode))
                .ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));

        P03NetworkWorkload CreateWorkload(int population)
        {
            P03NetworkWorkload workload = P03NetworkWorkload.ChunkProductionDense(population)
                .WithServerTickAndAoICadence(tickMilliseconds,
                    P03AoICadencePolicy.Selected124);
            return burstPeriod == 1 ? workload : workload.WithDirtyBurst(burstPeriod);
        }
    }

    private static SharedUdpSocketIoMode[] ParsePf5R3SocketModes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return
            [
                SharedUdpSocketIoMode.Scalar,
                SharedUdpSocketIoMode.PlatformBatch,
                SharedUdpSocketIoMode.BackgroundScalar,
                SharedUdpSocketIoMode.BackgroundPlatformBatch,
            ];
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant() switch
            {
                "scalar" or "inline-scalar" => SharedUdpSocketIoMode.Scalar,
                "platform-batch" or "inline-batched" => SharedUdpSocketIoMode.PlatformBatch,
                "background-scalar" => SharedUdpSocketIoMode.BackgroundScalar,
                "background-platform-batch" or "background-batched" =>
                    SharedUdpSocketIoMode.BackgroundPlatformBatch,
                _ => throw new ArgumentException($"Unknown PF5-R3 socket mode '{value}'."),
            })
            .Distinct()
            .ToArray();
    }

    private static void RunChunkStagingMicrobenchmark(string[] args)
    {
        int warmup = ReadInt(args, "--micro-warmup", 2_000);
        int iterations = ReadInt(args, "--micro-iterations", 10_000);
        int samples = ReadInt(args, "--micro-samples", 31);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/network-pf4e-r2-staging-microbench.json";
        if (warmup < 1 || iterations < 100 || samples < 5)
            throw new ArgumentOutOfRangeException(nameof(args));
        var rows = new List<Pf4eR2StagingMicroRow>();
        foreach ((string name, int entries, int recordsPerEntry, bool absence) in
                 new (string, int, int, bool)[]
                 {
                     ("standalone-delta", 1, 8, false),
                     ("bundle-2", 2, 2, false),
                     ("bundle-4", 4, 2, false),
                     ("bundle-8", 8, 2, false),
                     ("near-mtu-bundle", 13, 1, false),
                     ("absence", 1, 0, true),
                 })
        {
            var fixture = new Pf4eR2StagingFixture(entries, recordsPerEntry, absence);
            P03Metric oldMetric = Measure(fixture.OldCycle);
            P03Metric directMetric = Measure(fixture.DirectCycle);
            // Prime the allocation observer/JIT call shape itself outside the retained values.
            _ = MeasureAllocation(fixture.OldCycle);
            _ = MeasureAllocation(fixture.DirectCycle);
            long oldAllocation = MeasureAllocation(fixture.OldCycle);
            long directAllocation = MeasureAllocation(fixture.DirectCycle);
            if (oldAllocation != 0 || directAllocation != 0)
                throw new InvalidOperationException(
                    $"PF4E-R2 staging microbench allocated for {name}: " +
                    $"old={oldAllocation}, direct={directAllocation}.");
            if (!fixture.CanonicalEquivalent)
                throw new InvalidOperationException(
                    $"PF4E-R2 staging representations diverged for {name}.");
            rows.Add(new(name, entries, recordsPerEntry, fixture.EncodedBytes,
                oldMetric, directMetric, oldAllocation, directAllocation,
                oldMetric.Mean == 0 ? 0 :
                    (oldMetric.Mean - directMetric.Mean) / oldMetric.Mean * 100d));
            Console.WriteLine($"PF4E-R2 MICRO {name}: old p95={oldMetric.P95:F1} ns, " +
                $"direct p95={directMetric.P95:F1} ns, " +
                $"mean reduction={rows[^1].MeanReductionPercent:F1}% bytes={fixture.EncodedBytes}");
        }
        var report = new Pf4eR2StagingMicroReport(
            "ultrasim-network-pf4e-r2-staging-microbench-v1",
            DateTimeOffset.UtcNow, ReadGitSha(), Environment.Version.ToString(),
            Environment.OSVersion.VersionString, Stopwatch.Frequency,
            warmup, iterations, samples, rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        P03Metric Measure(Func<ulong> body)
        {
            ulong checksum = 0;
            for (int index = 0; index < warmup; index++) checksum ^= body();
            var values = new double[samples];
            for (int sample = 0; sample < samples; sample++)
            {
                long started = Stopwatch.GetTimestamp();
                for (int index = 0; index < iterations; index++) checksum ^= body();
                long elapsed = Stopwatch.GetTimestamp() - started;
                values[sample] = elapsed * 1_000_000_000d /
                    Stopwatch.Frequency / iterations;
            }
            GC.KeepAlive(checksum);
            return P03Metric.From(values);
        }

        long MeasureAllocation(Func<ulong> body)
        {
            for (int index = 0; index < warmup; index++) _ = body();
            long before = GC.GetAllocatedBytesForCurrentThread();
            ulong checksum = 0;
            for (int index = 0; index < iterations; index++) checksum ^= body();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(checksum);
            return allocated;
        }
    }

    private static void RunChunkCpuEfficiency(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 30);
        int ticks = ReadInt(args, "--ticks", 270);
        int tickMilliseconds = ReadInt(args, "--server-tick-milliseconds", 16);
        int frameCap = ReadInt(args, "--rated-frame-cap", 2);
        int[] populations = ParseOptionalPositiveIntegers(ReadText(args, "--populations")) ??
            [64, 96, 128];
        string label = ReadText(args, "--implementation-label") ?? "current";
        RemoteChunkBundleCompressionPolicy compression =
            ParseCompression(ReadText(args, "--compression"));
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/network-pf4e-r2-chunk-cpu-attribution.json";
        if (warmup < 1 || ticks < 10 || tickMilliseconds <= 0 ||
            frameCap is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(args));

        var curves = new (string Id, P03NetworkProfile Profile,
            Func<int, P03NetworkWorkload> Workload)[]
        {
            ("dirty-entity-dense-control", P03NetworkProfile.DirtyEntitySpatialFanout,
                population => P03NetworkWorkload.Dense(population, 1_000)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("chunk-unbatchable-single-dirty-chunk", P03NetworkProfile.ChunkPublication,
                population => P03NetworkWorkload.ChunkUnbatchableDense(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("pf4e-existing-chunk-dense", P03NetworkProfile.ChunkPublication,
                population => P03NetworkWorkload.ChunkExistingDense(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("chunk-production-localized-multichunk", P03NetworkProfile.ChunkPublication,
                population => P03NetworkWorkload.ChunkProductionDense(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
        };
        string? requestedCurves = ReadText(args, "--curves");
        HashSet<string>? selectedCurves = string.IsNullOrWhiteSpace(requestedCurves)
            ? null
            : requestedCurves.Split(',', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedCurves is not null && selectedCurves.Any(id =>
                !curves.Any(curve => curve.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("The PF4E-R2 curve selection contains an unknown id.");

        var rows = new List<P03WholeFlowRow>();
        int executionOrder = 0;
        foreach ((string curveId, P03NetworkProfile profile,
                     Func<int, P03NetworkWorkload> createWorkload) in curves)
        {
            if (selectedCurves is not null && !selectedCurves.Contains(curveId)) continue;
            using var prime = new P03WholeFlowHarness(
                createWorkload(Math.Min(populations[0], 32)), profile, frameCap,
                stageTimingEnabled: false, gameDetailedAttribution: true,
                compression: compression);
            _ = prime.Run(Math.Max(30, warmup), 10);
        }
        foreach ((string curveId, P03NetworkProfile profile,
                     Func<int, P03NetworkWorkload> createWorkload) in curves)
        foreach (int population in populations)
        {
            if (selectedCurves is not null && !selectedCurves.Contains(curveId)) continue;
            P03NetworkWorkload workload = createWorkload(population);
            Console.WriteLine($"PF4E-R2 {label}/{curveId}/{population}: constructing...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var harness = new P03WholeFlowHarness(
                workload, profile, frameCap,
                stageTimingEnabled: false, gameDetailedAttribution: true,
                compression: compression);
            P03WholeFlowRow row = harness.Run(warmup, ticks) with
            {
                ExecutionOrder = executionOrder++,
                Curve = $"{label}/{curveId}",
            };
            rows.Add(row);
            Console.WriteLine($"PF4E-R2 {label}/{curveId}/{population}: schedule p95 " +
                $"{row.Timing.GameScheduleAndCodecQueue.P95:F3} ms; publications " +
                $"{row.ProfileCounters.SuccessfulSemanticPublications:N0}; detailed=" +
                $"{row.ProfileCounters.ChunkCpu.Enabled}");
        }

        var report = new P03WholeFlowReport(
            "ultrasim-network-pf4e-r2-chunk-cpu-attribution-v1",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new P03WholeFlowScope(
                "Real-loopback-UDP whole flow plus opt-in detailed ChunkPublication CPU " +
                "attribution for readiness, source reads, materialization, canonical encoding, " +
                "bundle staging/encoding, queue admission, retained debt, and proof retirement.",
                "Cold construction, handshake, bootstrap, warmup, final convergence drain, and " +
                "validation are excluded. Detailed timing overhead makes these attribution rows " +
                "unsuitable as rated p95 evidence; probes-off R1/R2 rows remain authoritative.",
                $"Implementation label {label}; cap {frameCap}; selected 1/2/4 cadence; " +
                $"compression {(compression.Enabled ? "Lz4BlockV1" : "off")}; " +
                $"{warmup} warmup and {ticks} measured ticks; seed {ExecutionSeed}."),
            ExecutionSeed, warmup, ticks, rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static void RunRatedReconciliation(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 30);
        int ticks = ReadInt(args, "--ticks", 270);
        int tickMilliseconds = ReadInt(args, "--server-tick-milliseconds", 16);
        int frameCap = ReadInt(args, "--rated-frame-cap", 2);
        double stopMilliseconds = ReadDouble(args, "--server-role-stop-p95", 4d);
        int[] populations = ParseOptionalPositiveIntegers(ReadText(args, "--populations")) ??
            [64, 96, 128, 192, 256, 384, 448, 512, 576, 640, 704, 768];
        bool stopAfterFailure = !args.Contains("--retain-full-ladder", StringComparer.Ordinal);
        bool stageTimingEnabled = !args.Contains("--probes-off", StringComparer.Ordinal);
        RemoteChunkBundleCompressionPolicy compression =
            ParseCompression(ReadText(args, "--compression"));
        string output = ReadText(args, "--output") ??
            "_Benchmarks/Baselines/network-pf4e-r1-rated-reconciliation-raw.json";
        if (warmup < 1 || ticks < 10 || tickMilliseconds <= 0 ||
            frameCap is not (1 or 2 or 4 or 8) || stopMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(args));

        var curves = new (string Id, P03NetworkProfile Profile,
            Func<int, P03NetworkWorkload> Workload)[]
        {
            ("current-dirty-entity-dense", P03NetworkProfile.DirtyEntitySpatialFanout,
                population => P03NetworkWorkload.Dense(population, 1_000)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("current-maintained-entity-sparse", P03NetworkProfile.MaintainedEntityRelation,
                population => P03NetworkWorkload.Sparse(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("pf4e-existing-chunk-dense", P03NetworkProfile.ChunkPublication,
                population => P03NetworkWorkload.ChunkExistingDense(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("chunk-unbatchable-single-dirty-chunk", P03NetworkProfile.ChunkPublication,
                population => P03NetworkWorkload.ChunkUnbatchableDense(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
            ("chunk-production-localized-multichunk", P03NetworkProfile.ChunkPublication,
                population => P03NetworkWorkload.ChunkProductionDense(population)
                    .WithServerTickAndAoICadence(tickMilliseconds,
                        P03AoICadencePolicy.Selected124)),
        };
        string? requestedCurves = ReadText(args, "--curves");
        HashSet<string>? selectedCurves = string.IsNullOrWhiteSpace(requestedCurves)
            ? null
            : requestedCurves.Split(',', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedCurves is not null && selectedCurves.Any(id =>
                !curves.Any(curve => curve.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("The reconciliation curve selection contains an unknown id.");

        var rows = new List<P03WholeFlowRow>();
        int executionOrder = 0;
        int primePopulation = Math.Min(populations[0], 32);
        foreach ((string curveId, P03NetworkProfile profile,
                     Func<int, P03NetworkWorkload> createWorkload) in curves)
        {
            if (selectedCurves is not null && !selectedCurves.Contains(curveId)) continue;
            Console.WriteLine($"RECONCILE {curveId}: process-level tiering prime...");
            using var prime = new P03WholeFlowHarness(
                createWorkload(primePopulation), profile, frameCap, stageTimingEnabled,
                compression: compression);
            _ = prime.Run(Math.Max(30, warmup), 10);
        }
        foreach ((string curveId, P03NetworkProfile profile,
                     Func<int, P03NetworkWorkload> createWorkload) in curves)
        {
            if (selectedCurves is not null && !selectedCurves.Contains(curveId)) continue;
            bool retainedPass = false;
            foreach (int population in populations)
            {
                P03NetworkWorkload workload = createWorkload(population);
                Console.WriteLine($"RECONCILE {curveId}/{workload.Id} {profile} " +
                    $"frames={frameCap}: constructing...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using var harness = new P03WholeFlowHarness(
                    workload, profile, frameCap, stageTimingEnabled,
                    compression: compression);
                P03WholeFlowRow row = harness.Run(warmup, ticks) with
                {
                    ExecutionOrder = executionOrder++,
                    Curve = curveId,
                };
                rows.Add(row);
                double legacy = row.Timing.ServerRole.P95;
                Console.WriteLine($"RECONCILE {curveId}/{workload.Id}: legacy p95 " +
                    $"{legacy:F3} ms, inclusive p95 " +
                    $"{row.Timing.NetworkAndRemoteReplication.P95:F3} ms, publications " +
                    $"{row.ProfileCounters.SuccessfulSemanticPublications:N0}, bundles " +
                    $"{row.ProfileCounters.ChunkBundleMessages:N0}");
                if (legacy <= stopMilliseconds) retainedPass = true;
                else if (stopAfterFailure && retainedPass) break;
            }
        }

        var report = new P03WholeFlowReport(
            "ultrasim-network-pf4e-r1-rated-replication-scaling-reconciliation-v1",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new P03WholeFlowScope(
                "Current-code, real-loopback-UDP comparison of the historical DirtyEntity dense " +
                "and MaintainedEntity sparse controls with three explicitly shaped selected " +
                "ChunkPublication workloads. Every row includes authority mutation, Game " +
                "schedule/codec/queue, client semantic apply, application proof, and convergence.",
                "Cold construction, handshake, bootstrap, warmup, final convergence drain, and " +
                "semantic validation are outside timed ticks. Production policy, participant " +
                "limits, payload formats, cadence, AOI, proof, and reliability are unchanged.",
                $"All current rows use cap {frameCap}, selected 1/2/4 cadence, {warmup} warmup " +
                $"and {ticks} measured ticks. Legacy ServerRole matches P0.3J when probes are " +
                "off; the inclusive Network+replication clock is retained separately. " +
                $"Stage probes were {(stageTimingEnabled ? "enabled" : "disabled")}. Each " +
                $"curve was process-tiering primed at {primePopulation} recipients outside all " +
                "retained rows.") ,
            ExecutionSeed, warmup, ticks, rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static void RunRatedServerRoleSweep(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 30);
        int ticks = ReadInt(args, "--ticks", 270);
        string? serverTickMillisecondsText = ReadText(args, "--server-tick-milliseconds");
        string? tickMillisecondsText = serverTickMillisecondsText ??
            ReadText(args, "--tick-milliseconds");
        int tickMilliseconds = tickMillisecondsText is null
            ? 16
            : int.Parse(tickMillisecondsText, System.Globalization.CultureInfo.InvariantCulture);
        bool overrideTickCadence = tickMillisecondsText is not null;
        P03AoICadencePolicy? aoiCadence = ParseAoICadence(ReadText(args, "--aoi-cadence"));
        double stopMilliseconds = ReadDouble(args, "--server-role-stop-p95", 4d);
        string stopMetric = ReadText(args, "--stop-metric") ?? "server-role";
        P03NetworkProfile ratedProfile = Enum.Parse<P03NetworkProfile>(
            ReadText(args, "--rated-profile") ?? nameof(P03NetworkProfile.DirtyEntitySpatialFanout),
            ignoreCase: true);
        int maximumPopulation = ReadInt(args, "--maximum-population", 4_096);
        int denseStart = ReadInt(args, "--dense-start", 256);
        int denseStep = ReadInt(args, "--dense-step", 64);
        int sparseStart = ReadInt(args, "--sparse-start", 256);
        int sparseStep = ReadInt(args, "--sparse-step", 128);
        int[]? densePopulations = ParseOptionalPositiveIntegers(
            ReadText(args, "--dense-populations"));
        int[]? sparsePopulations = ParseOptionalPositiveIntegers(
            ReadText(args, "--sparse-populations"));
        int ratedFrameCap = ReadInt(args, "--rated-frame-cap", 1);
        bool stageTimingEnabled = !args.Contains("--probes-off", StringComparer.Ordinal);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/Baselines/network-p0.3h-rated-server-role-sweep.json";
        if (warmup < 1 || ticks < 10 || tickMilliseconds <= 0 ||
            stopMilliseconds <= 0 || denseStart <= 0 ||
            sparseStart <= 0 || denseStep <= 0 || sparseStep <= 0 ||
            maximumPopulation < Math.Max(denseStart, sparseStart) ||
            ratedFrameCap is not (1 or 2 or 4 or 8) ||
            stopMetric is not ("server-role" or "network-replication" or "tick"))
            throw new ArgumentOutOfRangeException(nameof(args));

        var rows = new List<P03WholeFlowRow>();
        int executionOrder = 0;
        RunSeries(
            densePopulations ??
                Enumerable.Range(0, checked((maximumPopulation - denseStart) / denseStep + 1))
                    .Select(index => denseStart + index * denseStep).ToArray(),
            population => Configure(P03NetworkWorkload.Dense(population, 1_000)),
            ratedProfile);
        RunSeries(
            sparsePopulations ??
                Enumerable.Range(0, checked((maximumPopulation - sparseStart) / sparseStep + 1))
                    .Select(index => sparseStart + index * sparseStep).ToArray(),
            population => Configure(P03NetworkWorkload.Sparse(population)),
            ratedProfile);

        var report = new P03WholeFlowReport(
            ratedProfile == P03NetworkProfile.ChunkPublication && ratedFrameCap == 2 &&
                stopMetric == "network-replication"
                ? "ultrasim-network-pf4e-selected-profile-rated-recipient-sweep-v1"
                : "ultrasim-network-p03h-rated-server-role-sweep-v4",
            DateTimeOffset.UtcNow,
            ReadGitSha(),
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            new P03WholeFlowScope(
                $"Comparison whole flow using {ratedProfile} and unreliable frame cap " +
                $"{ratedFrameCap}. Dense has 1,000 public entities and two-percent spatial " +
                "dirt; sparse has one moving public entity per recipient and thirty-five-percent " +
                "spatial dirt. The two shapes therefore expose normalized quantities and are not " +
                "an equal-semantic-work causal pair.",
                "Each series stops after retaining its first completed population with " +
                $"{stopMetric} p95 greater than {stopMilliseconds:F3} ms. " +
                "Cold construction/bootstrap and " +
                "semantic verification remain outside timed ticks.",
                "This is a localhost server-role rating observation, not a public-WAN CCU or " +
                "latency SLA. p99/max are retained as tail context; p95 is the stop criterion. " +
                $"Protocol/listener stage probes were {(stageTimingEnabled ? "enabled" : "disabled")}. " +
                $"Dense populations: {DescribePopulations(densePopulations, denseStart, denseStep)}; " +
                $"sparse populations: {DescribePopulations(sparsePopulations, sparseStart, sparseStep)}. " +
                $"The server semantic tick duration was {tickMilliseconds} ms. " +
                $"AOI cadence was {(aoiCadence?.ToString() ?? "the workload default")}."),
            ExecutionSeed, warmup, ticks, rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        P03NetworkWorkload Configure(P03NetworkWorkload workload) => aoiCadence.HasValue
            ? workload.WithServerTickAndAoICadence(tickMilliseconds, aoiCadence.Value)
            : overrideTickCadence
                ? workload.WithTickDuration(tickMilliseconds)
                : workload;

        void RunSeries(
            IEnumerable<int> populations,
            Func<int, P03NetworkWorkload> createWorkload,
            P03NetworkProfile profile)
        {
            foreach (int population in populations)
            {
                if (population > maximumPopulation) break;
                P03NetworkWorkload workload = createWorkload(population);
                Console.WriteLine($"RATED {workload.Id} {profile} frames={ratedFrameCap}: constructing...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using var harness = new P03WholeFlowHarness(
                    workload, profile, ratedFrameCap, stageTimingEnabled);
                P03WholeFlowRow row = harness.Run(warmup, ticks) with
                {
                    ExecutionOrder = executionOrder++,
                };
                rows.Add(row);
                Console.WriteLine($"RATED {workload.Id} {profile}: tick p95 " +
                    $"{row.Timing.Tick.P95:F3} ms, server p95 " +
                    $"{row.Timing.ServerRole.P95:F3} ms, p99 " +
                    $"{row.Timing.ServerRole.P99:F3} ms, max " +
                    $"{row.Timing.ServerRole.Maximum:F3} ms, Network+replication p95 " +
                    $"{row.Timing.NetworkAndRemoteReplication.P95:F3} ms");
                double observed = stopMetric switch
                {
                    "tick" => row.Timing.Tick.P95,
                    "network-replication" => row.Timing.NetworkAndRemoteReplication.P95,
                    _ => row.Timing.ServerRole.P95,
                };
                if (observed > stopMilliseconds) break;
            }
        }
    }

    private static P03AoICadencePolicy? ParseAoICadence(string? text) =>
        text?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "0/1/2" or "012" or "default" => P03AoICadencePolicy.Default012,
            "1/1/2" or "112" or "slower" => P03AoICadencePolicy.Slower112,
            "1/2/4" or "124" or "selected" => P03AoICadencePolicy.Selected124,
            _ => throw new ArgumentException($"Unknown AOI cadence '{text}'."),
        };

    private static RemoteChunkBundleCompressionPolicy ParseCompression(string? text) =>
        text?.Trim().ToLowerInvariant() switch
        {
            null or "" or "off" or "canonical" =>
                RemoteChunkBundleCompressionPolicy.Disabled,
            "lz4" or "selected" => RemoteChunkBundleCompressionPolicy.Selected,
            _ => throw new ArgumentException($"Unknown chunk-bundle compression '{text}'."),
        };

    private static P03NetworkProfile[] ParseProfiles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return s_profiles;
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Enum.Parse<P03NetworkProfile>(value, ignoreCase: true)).ToArray();
    }

    private static int[] ParseCaps(string? text) => string.IsNullOrWhiteSpace(text)
        ? [1, 2, 4, 8]
        : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static int[]? ParseOptionalPositiveIntegers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int[] values = text.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value,
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length == 0 || values.Any(value => value <= 0) ||
            values.Zip(values.Skip(1), (left, right) => right <= left).Any(invalid => invalid))
            throw new ArgumentOutOfRangeException(nameof(text));
        return values;
    }

    private static string DescribePopulations(int[]? values, int start, int step) =>
        values is null ? $"start {start} by {step}" : string.Join(',', values);

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? text = ReadText(args, name);
        return text is null ? fallback : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double ReadDouble(string[] args, string name, double fallback)
    {
        string? text = ReadText(args, name);
        return text is null ? fallback : double.Parse(text,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadText(string[] args, string name)
    {
        string prefix = name + "=";
        string? value = args.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? null : value[prefix.Length..];
    }

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
}

internal readonly record struct P03WholeFlowCase(
    P03NetworkWorkload Workload,
    P03NetworkProfile Profile,
    int FrameCap);

internal enum P03NetworkProfile : byte
{
    MaintainedEntityRelation = 1,
    ChunkPublication = 2,
    DirtyEntitySpatialFanout = 3,
    DirtyEntityChunkBatchFanout = 4,
    DirtyChunkSpatialFanout = 5,
}

internal enum P03AoICadencePolicy : byte
{
    Default012 = 1,
    Slower112 = 2,
    Selected124 = 3,
}

internal enum P03EntityLayout : byte
{
    GridFourMeters = 1,
    TwentyEntitiesPerChunk = 2,
    V2DenseClosestTwoMeterGrid = 3,
    TopologyChunkGroups = 4,
    TopologyCompactGroups = 5,
}

internal enum P03MutationPattern : byte
{
    Sequential = 1,
    OneChunkGroup = 2,
    EightChunkStripe = 3,
    ActiveDistributed = 4,
    FixedPrefix = 5,
    MatchedDistributedPrefix = 6,
    BurstChunkStripe = 7,
    SingleAcrossChunkStripe = 8,
    TopologyOnePerGroup = 9,
}

internal readonly record struct P03NetworkWorkload(
    string Id,
    int Recipients,
    int Entities,
    int DirtyEntitiesPerTick,
    bool MoveRecipients,
    GameInterestProfile EntityInterest,
    ChunkInterestProfile ChunkInterest,
    OfflineDirectChunkCadence ChunkCadence,
    int TickDurationMilliseconds,
    P03EntityLayout EntityLayout = P03EntityLayout.GridFourMeters,
    P03MutationPattern MutationPattern = P03MutationPattern.Sequential,
    int ActiveEntities = 0,
    int InputIntervalTicks = 1,
    string SemanticFamilies = "public-world-latest-state",
    int MutationPeriodTicks = 1,
    int TopologyGroups = 0,
    int InactiveStoredChunks = 0)
{
    internal static P03NetworkWorkload Dense(int recipients, int entities) => new(
        $"dense-{recipients}", recipients, entities, Math.Max(1, entities * 2 / 100), false,
        GameInterestProfile.Reference, GameChunkInterestProfile.Default,
        OfflineDirectChunkCadence.EvidenceProfile, 16);

    internal static P03NetworkWorkload Sparse(int population) => new(
        $"sparse-{population}", population, population,
        Math.Max(1, (int)Math.Round(population * .35, MidpointRounding.AwayFromZero)), true,
        new GameInterestProfile(10_000, 7_500, 7_500, 23_500, 39_500,
            32, 48, 100, 2_000),
        new ChunkInterestProfile(7_500, 7_500, 23_500, 23_500, 39_500, 39_500),
        new OfflineDirectChunkCadence(1, 2, 3), 16);

    internal static P03NetworkWorkload ChunkExistingDense(int recipients) =>
        Dense(recipients, 1_000) with { Id = $"chunk-existing-dense-{recipients}" };

    internal static P03NetworkWorkload ChunkUnbatchableDense(int recipients) =>
        Dense(recipients, 1_000) with
        {
            Id = $"chunk-unbatchable-dense-{recipients}",
            EntityLayout = P03EntityLayout.TwentyEntitiesPerChunk,
            MutationPattern = P03MutationPattern.OneChunkGroup,
        };

    internal static P03NetworkWorkload ChunkProductionDense(int recipients) =>
        Dense(recipients, 1_000) with
        {
            Id = $"chunk-production-dense-{recipients}",
            EntityLayout = P03EntityLayout.TwentyEntitiesPerChunk,
            MutationPattern = P03MutationPattern.EightChunkStripe,
        };

    internal P03NetworkWorkload WithDirtyBurst(int period)
    {
        if (period is < 2 or > 16 || DirtyEntitiesPerTick > Entities / period)
            throw new ArgumentOutOfRangeException(nameof(period));
        return this with
        {
            Id = $"{Id}-burst{period}",
            DirtyEntitiesPerTick = checked(DirtyEntitiesPerTick * period),
            MutationPattern = P03MutationPattern.BurstChunkStripe,
            MutationPeriodTicks = period,
        };
    }

    internal static P03NetworkWorkload ChunkV2Shaped(
        int recipients,
        int entities,
        bool v2Activity)
    {
        int active = Math.Max(1,
            (int)Math.Round(entities * .35, MidpointRounding.AwayFromZero));
        int dirty = v2Activity ? active : Math.Min(20, entities);
        var entityInterest = new GameInterestProfile(
            16_000, 7_500, 7_500, 23_500, 39_500,
            32, 48, 100, 2_000)
        {
            ImmediateIntervalMilliseconds = 16,
        };
        var chunkInterest = new ChunkInterestProfile(
            7_500, 7_500, 23_500, 23_500, 39_500, 39_500);
        return new P03NetworkWorkload(
            $"v2-shaped-e{entities}-r{recipients}-" +
                (v2Activity ? "v2-activity" : "current-activity"),
            recipients,
            entities,
            dirty,
            false,
            entityInterest,
            chunkInterest,
            new OfflineDirectChunkCadence(1, 2, 3),
            16,
            P03EntityLayout.V2DenseClosestTwoMeterGrid,
            v2Activity ? P03MutationPattern.ActiveDistributed : P03MutationPattern.Sequential,
            active,
            3,
            "V3 unified public-world latest-state; V2 comparison had movement, visual, bulk, and ordered-event families");
    }

    internal static P03NetworkWorkload ChunkMatchedControl(
        int recipients,
        int entities,
        int dirtyEntitiesPerTick)
    {
        if (dirtyEntitiesPerTick < 1 || dirtyEntitiesPerTick > entities)
            throw new ArgumentOutOfRangeException(nameof(dirtyEntitiesPerTick));
        P03NetworkWorkload basis = ChunkV2Shaped(
            recipients, entities, v2Activity: false);
        return basis with
        {
            Id = $"matched-e{entities}-r{recipients}-dirty{dirtyEntitiesPerTick}",
            DirtyEntitiesPerTick = dirtyEntitiesPerTick,
            MutationPattern = P03MutationPattern.MatchedDistributedPrefix,
            ActiveEntities = recipients,
        };
    }

    internal static P03NetworkWorkload ChunkTopology(
        int recipients,
        int entities,
        int dirtyEntitiesPerTick,
        int groups,
        int inactiveStoredChunks = 0)
    {
        if (recipients <= 0 || entities < recipients || dirtyEntitiesPerTick <= 0 ||
            dirtyEntitiesPerTick > entities || groups <= 0 || groups > recipients ||
            inactiveStoredChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(groups));
        int active = Math.Max(dirtyEntitiesPerTick,
            (int)Math.Round(entities * .35, MidpointRounding.AwayFromZero));
        P03NetworkWorkload basis = ChunkV2Shaped(
            recipients, entities, v2Activity: false);
        return basis with
        {
            Id = $"topology-r{recipients}-e{entities}-g{groups}-dirty{dirtyEntitiesPerTick}" +
                (inactiveStoredChunks == 0 ? string.Empty : $"-inactive{inactiveStoredChunks}"),
            DirtyEntitiesPerTick = dirtyEntitiesPerTick,
            EntityLayout = P03EntityLayout.TopologyChunkGroups,
            MutationPattern = P03MutationPattern.MatchedDistributedPrefix,
            ActiveEntities = active,
            TopologyGroups = groups,
            InactiveStoredChunks = inactiveStoredChunks,
        };
    }

    internal static P03NetworkWorkload ChunkFixedDeliveryTopology(
        int recipientPublicationRelationships,
        int groups)
    {
        if (recipientPublicationRelationships <= 0 || groups <= 0 ||
            groups > recipientPublicationRelationships ||
            recipientPublicationRelationships % groups != 0)
            throw new ArgumentOutOfRangeException(nameof(groups));
        P03NetworkWorkload basis = ChunkTopology(
            recipientPublicationRelationships,
            recipientPublicationRelationships,
            groups,
            groups);
        return basis with
        {
            Id = $"fixed-delivery-r{recipientPublicationRelationships}-g{groups}",
            EntityLayout = P03EntityLayout.TopologyCompactGroups,
            MutationPattern = P03MutationPattern.TopologyOnePerGroup,
            ActiveEntities = recipientPublicationRelationships,
        };
    }

    internal void CopyDirtyEntityIndices(int tick, Span<int> destination)
    {
        if (destination.Length < DirtyEntitiesPerTick)
            throw new ArgumentException("The dirty-entity destination is too small.",
                nameof(destination));
        Span<int> selected = destination[..DirtyEntitiesPerTick];
        switch (MutationPattern)
        {
            case P03MutationPattern.Sequential:
            {
                int start = checked((tick - 1) * DirtyEntitiesPerTick % Entities);
                for (int offset = 0; offset < selected.Length; offset++)
                    selected[offset] = (start + offset) % Entities;
                break;
            }
            case P03MutationPattern.OneChunkGroup:
            {
                int groupCount = Entities / DirtyEntitiesPerTick;
                if (Entities % DirtyEntitiesPerTick != 0)
                    throw new InvalidOperationException(
                        "The one-chunk workload requires complete fixed-size groups.");
                int start = ((tick - 1) % groupCount) * DirtyEntitiesPerTick;
                for (int offset = 0; offset < selected.Length; offset++)
                    selected[offset] = start + offset;
                break;
            }
            case P03MutationPattern.EightChunkStripe:
            {
                const int chunksPerStripe = 8;
                const int entitiesPerChunk = 20;
                int chunkCount = Entities / entitiesPerChunk;
                if (Entities % entitiesPerChunk != 0 || DirtyEntitiesPerTick != 20)
                    throw new InvalidOperationException(
                        "The production chunk workload requires 20 changes over complete 20-row chunks.");
                int startChunk = checked((tick - 1) * chunksPerStripe % chunkCount);
                for (int offset = 0; offset < selected.Length; offset++)
                {
                    int stripeChunk = offset % chunksPerStripe;
                    int round = offset / chunksPerStripe;
                    int chunk = (startChunk + stripeChunk) % chunkCount;
                    int row = (tick + stripeChunk + round * chunksPerStripe) % entitiesPerChunk;
                    selected[offset] = chunk * entitiesPerChunk + row;
                }
                break;
            }
            case P03MutationPattern.BurstChunkStripe:
            {
                const int entitiesPerChunk = 20;
                int chunkCount = Entities / entitiesPerChunk;
                int chunksPerBurst = Math.Min(chunkCount, checked(8 * MutationPeriodTicks));
                if (Entities % entitiesPerChunk != 0 ||
                    DirtyEntitiesPerTick > checked(chunksPerBurst * entitiesPerChunk))
                    throw new InvalidOperationException(
                        "The burst chunk workload exceeds its complete-row stripe.");
                int burst = (tick - 1) / MutationPeriodTicks;
                int startChunk = checked(burst * chunksPerBurst % chunkCount);
                for (int offset = 0; offset < selected.Length; offset++)
                {
                    int stripeChunk = offset % chunksPerBurst;
                    int round = offset / chunksPerBurst;
                    int chunk = (startChunk + stripeChunk) % chunkCount;
                    int row = (burst + stripeChunk + round * chunksPerBurst) % entitiesPerChunk;
                    selected[offset] = chunk * entitiesPerChunk + row;
                }
                break;
            }
            case P03MutationPattern.SingleAcrossChunkStripe:
            {
                const int entitiesPerChunk = 20;
                if (DirtyEntitiesPerTick != 1 || Entities % entitiesPerChunk != 0)
                    throw new InvalidOperationException(
                        "The single-record chunk stripe requires complete 20-row chunks.");
                int chunkCount = Entities / entitiesPerChunk;
                int chunk = (tick - 1) % chunkCount;
                int row = ((tick - 1) / chunkCount) % entitiesPerChunk;
                selected[0] = chunk * entitiesPerChunk + row;
                break;
            }
            case P03MutationPattern.ActiveDistributed:
            {
                if (ActiveEntities <= 0 || DirtyEntitiesPerTick != ActiveEntities ||
                    ActiveEntities > Entities)
                    throw new InvalidOperationException(
                        "The V2-shaped distributed-active workload has invalid activity bounds.");
                for (int offset = 0; offset < selected.Length; offset++)
                    selected[offset] = checked((int)((long)offset * Entities / ActiveEntities));
                break;
            }
            case P03MutationPattern.FixedPrefix:
            {
                for (int offset = 0; offset < selected.Length; offset++)
                    selected[offset] = offset;
                break;
            }
            case P03MutationPattern.MatchedDistributedPrefix:
            {
                if (ActiveEntities <= 0 || ActiveEntities > Entities ||
                    DirtyEntitiesPerTick > ActiveEntities)
                    throw new InvalidOperationException(
                        "The matched distributed-prefix workload has invalid bounds.");
                int domain = 1;
                int bits = 0;
                while (domain < ActiveEntities)
                {
                    domain = checked(domain * 2);
                    bits++;
                }
                int written = 0;
                for (int candidate = 0; candidate < domain && written < selected.Length;
                    candidate++)
                {
                    int value = candidate;
                    int reversed = 0;
                    for (int bit = 0; bit < bits; bit++)
                    {
                        reversed = (reversed << 1) | (value & 1);
                        value >>= 1;
                    }
                    if (reversed < ActiveEntities) selected[written++] = reversed;
                }
                if (written != selected.Length)
                    throw new InvalidOperationException(
                        "The matched distributed-prefix workload did not fill its selection.");
                break;
            }
            case P03MutationPattern.TopologyOnePerGroup:
            {
                if (TopologyGroups <= 0 || DirtyEntitiesPerTick != TopologyGroups ||
                    Entities % TopologyGroups != 0)
                    throw new InvalidOperationException(
                        "The fixed-delivery topology requires one mutation per equal group.");
                for (int group = 0; group < TopologyGroups; group++)
                    selected[group] = checked(group * Entities / TopologyGroups);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(MutationPattern));
        }
        selected.Sort();
        for (int index = 1; index < selected.Length; index++)
            if (selected[index] == selected[index - 1])
                throw new InvalidOperationException("The workload selected one entity twice.");
    }

    internal P03NetworkWorkload WithTickDuration(int tickMilliseconds)
    {
        if (tickMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickMilliseconds));
        return this with
        {
            TickDurationMilliseconds = tickMilliseconds,
            ChunkCadence = new OfflineDirectChunkCadence(
                1,
                Period(EntityInterest.NearIntervalMilliseconds, tickMilliseconds),
                Period(EntityInterest.FarIntervalMilliseconds, tickMilliseconds)),
        };
    }

    internal P03NetworkWorkload WithServerTickAndAoICadence(
        int tickMilliseconds,
        P03AoICadencePolicy cadence)
    {
        if (tickMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickMilliseconds));
        long immediateInterval = cadence == P03AoICadencePolicy.Slower112
            ? checked(2L * tickMilliseconds)
            : cadence == P03AoICadencePolicy.Selected124
                ? tickMilliseconds
                : 0;
        long farInterval = cadence == P03AoICadencePolicy.Selected124
            ? checked(4L * tickMilliseconds)
            : checked(3L * tickMilliseconds);
        return this with
        {
            TickDurationMilliseconds = tickMilliseconds,
            EntityInterest = EntityInterest with
            {
                ImmediateIntervalMilliseconds = immediateInterval,
                NearIntervalMilliseconds = checked(2L * tickMilliseconds),
                FarIntervalMilliseconds = farInterval,
            },
            ChunkCadence = new OfflineDirectChunkCadence(
                cadence == P03AoICadencePolicy.Slower112 ? 2u : 1u,
                2,
                cadence == P03AoICadencePolicy.Selected124 ? 4u : 3u),
        };
    }

    private static uint Period(long intervalMilliseconds, int tickMilliseconds) =>
        checked((uint)Math.Max(1L,
            (intervalMilliseconds + tickMilliseconds - 1L) / tickMilliseconds));
}

internal sealed class P03WholeFlowHarness : IDisposable
{
    private const ulong ApplicationEpoch = 0xF03_7001;
    private static readonly WorldContentFingerprint s_content = new(0xF03_7001, 0xA77B_1001);
    private static readonly UdpProtocolLimits s_udp = new(
        1_472, 65_536, 65_536, 16_384, 30_000, 30_000, 5_000, 5_000);

    private readonly P03NetworkWorkload _workload;
    private readonly P03NetworkProfile _profile;
    private readonly int _frameCap;
    private readonly bool _stageTimingEnabled;
    private readonly GameTelemetryLevel _gameTelemetryLevel;
    private readonly int _serverProofDispatchIntervalTicks;
    private readonly int _postMeasurementDrainRepetitions;
    private readonly bool _authorityThroughNetworkTimingEnabled;
    private readonly bool _serverOwnedRemoteTimingEnabled;
    private readonly bool _retainRawSamples;
    private readonly IP03ExternalServerWorkload? _externalServerWorkload;
    private readonly SharedUdpSocketIoMode _socketIoMode;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly SharedUdpProtocolHost _host;
    private readonly SharedUdpProtocolSession[] _sessions;
    private readonly P03RemoteProfileAdapter _adapter;
    private readonly long[] _x;
    private readonly long[] _z;
    private readonly ulong[] _versions;
    private readonly List<double> _tick = new();
    private readonly List<double> _authority = new();
    private readonly List<double> _baseAuthoritativeMutation = new();
    private readonly List<double> _remotePublicationPreparation = new();
    private readonly List<double> _recipientCommitObservation = new();
    private readonly List<double> _schedule = new();
    private readonly List<double> _pump = new();
    private readonly List<double> _serverApplication = new();
    private readonly List<double> _clientApplication = new();
    private readonly List<double> _networkAndRemoteReplication = new();
    private readonly List<double> _serverOutboundProtocol = new();
    private readonly List<double> _serverInboundProtocol = new();
    private readonly List<double> _networkOtherResidual = new();
    private readonly List<double> _serverOwnedRemoteReplicationBudget = new();
    private readonly List<double> _authorityThroughNetworkAndRemoteReplication = new();
    private readonly List<double> _serverRole = new();
    private readonly List<double> _clientRole = new();
    private readonly List<double> _serverOwnedTick = new();
    private readonly List<double> _attributedMainThread = new();
    private readonly List<double> _unattributedResidual = new();
    private readonly List<double> _inputAndSession = new();
    private readonly List<double> _chatAndCommands = new();
    private readonly List<double> _authorityAndEcs = new();
    private readonly List<double> _locomotionAndPhysics = new();
    private readonly List<double> _inventoryAndItems = new();
    private readonly List<double> _worldCommit = new();
    private P03TimingBoundaryEvidence _timingBoundary;
    private bool _disposed;
    private long _tickServerApplication;
    private long _tickClientApplication;
    private long _tickClientNetwork;
    private int _currentTick;

    internal P03WholeFlowHarness(
        P03NetworkWorkload workload,
        P03NetworkProfile profile,
        int frameCap,
        bool stageTimingEnabled = true,
        bool gameDetailedAttribution = false,
        RemoteChunkBundleCompressionPolicy compression = default,
        RemoteChunkSingletonDeltaCompression singletonDeltaCompression = default,
        RemoteChunkDeltaBodySharingMode bodySharing = RemoteChunkDeltaBodySharingMode.Enabled,
        bool historyAudit = false,
        bool legacyRangeCensus = true,
        bool authorityThroughNetworkTimingEnabled = true,
        bool serverOwnedRemoteTimingEnabled = false,
        PreparedChunkPublicationReadModel chunkReadModel =
            PreparedChunkPublicationReadModel.PreparedLatestState,
        SharedUdpSocketIoMode socketIoMode =
            SharedUdpSocketIoMode.PlatformSendReceiveBatch,
        bool backgroundEgressTimingEnabled = true,
        IP03ExternalServerWorkload? externalServerWorkload = null,
        bool retainRawSamples = false,
        GameTelemetryLevel? gameTelemetryLevel = null,
        int serverProofDispatchIntervalTicks = 1,
        int postMeasurementDrainRepetitions = 1,
        IRemoteChunkCompressionInputObserver? compressionInputObserver = null,
        bool uniqueApplicationEpochs = false)
    {
        _workload = workload;
        _profile = profile;
        _frameCap = frameCap;
        _stageTimingEnabled = stageTimingEnabled;
        _gameTelemetryLevel = gameTelemetryLevel ?? (gameDetailedAttribution
            ? GameTelemetryLevel.Detailed
            : GameTelemetryLevel.Off);
        if (serverProofDispatchIntervalTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(serverProofDispatchIntervalTicks));
        _serverProofDispatchIntervalTicks = serverProofDispatchIntervalTicks;
        if (postMeasurementDrainRepetitions <= 0)
            throw new ArgumentOutOfRangeException(nameof(postMeasurementDrainRepetitions));
        _postMeasurementDrainRepetitions = postMeasurementDrainRepetitions;
        _authorityThroughNetworkTimingEnabled = authorityThroughNetworkTimingEnabled;
        _serverOwnedRemoteTimingEnabled = serverOwnedRemoteTimingEnabled;
        _retainRawSamples = retainRawSamples;
        _socketIoMode = socketIoMode;
        _externalServerWorkload = externalServerWorkload;
        _x = new long[workload.Entities];
        _z = new long[workload.Entities];
        _versions = new ulong[workload.Entities];
        InitializePositions();
        NetworkProtocolManifest manifest = NetworkProtocolManifest.Create(
            13, UltraSimGameProtocol.Manifest.Messages.ToArray(),
            maximumUnreliableDataFramesPerDatagram: 8);
        _protocolLimits = new NetworkProtocolLimits(
            MaximumReliableMessages: 32,
            MaximumReceivedMessages: 64,
            MaximumMessageBytes: UltraSimGameProtocol.MaximumMessageBytes,
            MaximumFragmentsPerMessage: 64,
            SentPacketCapacity: 256,
            MaximumInboundPacketsPerPump: 128,
            MaximumPacketsPerPump: 128,
            MinimumRetransmissionSteps: 2,
            MaximumRetransmissionSteps: 128,
            InitialCongestionWindowPackets: 16,
            MaximumCongestionWindowPackets: 128,
            MaximumIndependentUnreliableMessages: 16,
            ReliablePayloadByteCapacity: 65_536,
            ReceivedPayloadByteCapacity: 131_072,
            MaximumUnreliableFramesPerDatagram: frameCap);
        var shared = new SharedUdpProtocolLimits(
            workload.Recipients, 32, Math.Max(8_192, workload.Recipients * 64),
            socketIoMode, 32, 32,
            BackgroundEgressTimingEnabled: backgroundEgressTimingEnabled);
        _host = SharedUdpProtocolHost.CreateSelfTrustedLoopback(
            _protocolLimits, s_udp, shared, manifest);
        _sessions = new SharedUdpProtocolSession[workload.Recipients];
        for (int index = 0; index < _sessions.Length; index++)
        {
            Require(_host.TryAddLoopbackSession(out SharedUdpProtocolSession? session, out _),
                $"Could not create shared-listener session {index}.");
            _sessions[index] = session!;
        }
        _adapter = profile switch
        {
            P03NetworkProfile.MaintainedEntityRelation => new P03MaintainedRemoteAdapter(
                workload, _sessions, _x, _z, _versions),
            P03NetworkProfile.DirtyEntitySpatialFanout => new P03DirtyEntityRemoteAdapter(
                workload, _sessions, _x, _z, _versions, chunkBatch: false),
            P03NetworkProfile.DirtyEntityChunkBatchFanout => new P03DirtyEntityRemoteAdapter(
                workload, _sessions, _x, _z, _versions, chunkBatch: true),
            P03NetworkProfile.ChunkPublication => new P03ChunkPublicationRemoteAdapter(
                workload, _sessions, _x, _z, _versions, s_content, compression,
                singletonDeltaCompression, bodySharing, historyAudit, legacyRangeCensus,
                chunkReadModel, compressionInputObserver, uniqueApplicationEpochs),
            P03NetworkProfile.DirtyChunkSpatialFanout => new P03DirtyChunkRemoteAdapter(
                workload, _sessions, _x, _z, _versions, s_content),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
        _adapter.ConfigureTelemetry(_gameTelemetryLevel);
        _adapter.ConfigureServerOwnedRemoteTiming(serverOwnedRemoteTimingEnabled);
    }

    internal P03WholeFlowRow Run(int warmup, int ticks)
    {
        _ = DrainToConvergence(0, 16_384);
        for (int tick = 1; tick <= warmup; tick++) RunTick(tick, measure: false);
        _ = DrainToConvergence(checked((ulong)warmup + 1), 4_096);
        ResetMeasurement(ticks);
        P03TransportTotals before = CaptureTransport();
        SharedUdpListenerSnapshot listenerBefore = _host.GetSnapshot().Listener;
        using Process measuredProcess = Process.GetCurrentProcess();
        measuredProcess.Refresh();
        TimeSpan processCpuBefore = measuredProcess.TotalProcessorTime;
        long processWorkingSetBefore = measuredProcess.WorkingSet64;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long processWallStarted = Stopwatch.GetTimestamp();
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        for (int tick = 1; tick <= ticks; tick++) RunTick(warmup + tick, measure: true);

        // Freeze every cumulative child at the same boundary as the measured parent samples.
        // Convergence work is intentionally captured as a separate delta below.
        double processWallMilliseconds = Stopwatch.GetElapsedTime(processWallStarted)
            .TotalMilliseconds;
        double processCpuMilliseconds =
            (measuredProcess.TotalProcessorTime - processCpuBefore).TotalMilliseconds;
        measuredProcess.Refresh();
        long processWorkingSetAfter = measuredProcess.WorkingSet64;
        long processPeakWorkingSetBytes = measuredProcess.PeakWorkingSet64;
        long allocated = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
        int gen0Collections = GC.CollectionCount(0) - gen0Before;
        int gen1Collections = GC.CollectionCount(1) - gen1Before;
        int gen2Collections = GC.CollectionCount(2) - gen2Before;
        P03TransportTotals measuredAfter = CaptureTransport();
        SharedUdpListenerSnapshot measuredListenerAfter = _host.GetSnapshot().Listener;
        P03ProfileCounters profile = _adapter.GetCounters();
        P03ProtocolTiming protocol = CaptureProtocolTiming();

        int drainRounds = 0;
        ulong drainStep = checked((ulong)(warmup + ticks + 1));
        for (int repetition = 0; repetition < _postMeasurementDrainRepetitions; repetition++)
            drainRounds = checked(drainRounds + DrainToConvergence(
                checked(drainStep + (ulong)repetition * 4_096UL), 4_096));
        P03TransportTotals finalAfter = CaptureTransport();
        SharedUdpListenerSnapshot finalListenerAfter = _host.GetSnapshot().Listener;
        P03ProfileCounters finalProfile = _adapter.GetCounters();
        P03ProtocolTiming finalProtocol = CaptureProtocolTiming();
        _adapter.Validate();
        SharedUdpProtocolHostSnapshot hostSnapshot = _host.GetSnapshot();
        long profileServerReserved = _adapter.GetServerReservedBytes();
        long profileClientReserved = _adapter.GetClientReservedBytes();
        P03TransportSummary transport = P03TransportSummary.Delta(
            measuredAfter, before, measuredListenerAfter, listenerBefore, _frameCap);
        P03DrainSummary drain = P03DrainSummary.Create(
            _postMeasurementDrainRepetitions, drainRounds,
            in profile, in finalProfile, in protocol, in finalProtocol,
            in finalAfter, in measuredAfter,
            in finalListenerAfter, in measuredListenerAfter,
            _adapter.IsConverged);
        P03NormalizedCost normalized = P03NormalizedCost.Calculate(
            _workload.Recipients, ticks, _networkAndRemoteReplication, _serverRole,
            transport, profile);
        return new P03WholeFlowRow(
            0, _workload.Id, _profile.ToString(), _frameCap, _socketIoMode.ToString(),
            _workload.Recipients, _workload.Entities, _workload.DirtyEntitiesPerTick,
            _workload.ActiveEntities,
            _workload.InputIntervalTicks,
            _workload.InputIntervalTicks <= 0 ? 0 :
                _workload.ActiveEntities / (double)_workload.InputIntervalTicks,
            _workload.SemanticFamilies,
            new P03CadenceSummary(
                $"{_workload.ChunkCadence.ImmediateSteps - 1}/" +
                $"{_workload.ChunkCadence.NearSteps - 1}/" +
                $"{_workload.ChunkCadence.FarSteps - 1}",
                _workload.TickDurationMilliseconds,
                _workload.EntityInterest.ImmediateIntervalMilliseconds,
                _workload.EntityInterest.NearIntervalMilliseconds,
                _workload.EntityInterest.FarIntervalMilliseconds,
                _workload.ChunkCadence.ImmediateSteps,
                _workload.ChunkCadence.NearSteps,
                _workload.ChunkCadence.FarSteps),
            new P03WholeFlowTiming(
                P03Metric.From(_tick), P03Metric.From(_authority),
                P03Metric.From(_baseAuthoritativeMutation),
                P03Metric.From(_remotePublicationPreparation),
                P03Metric.From(_recipientCommitObservation),
                P03Metric.From(_schedule),
                P03Metric.From(_pump), P03Metric.From(_serverApplication),
                P03Metric.From(_clientApplication),
                P03Metric.From(_networkAndRemoteReplication),
                P03Metric.From(_serverOwnedRemoteReplicationBudget),
                P03Metric.From(_authorityThroughNetworkAndRemoteReplication),
                P03Metric.From(_serverRole),
                P03Metric.From(_clientRole), protocol,
                P03Metric.From(_serverOwnedTick),
                P03Metric.From(_attributedMainThread),
                P03Metric.From(_unattributedResidual),
                P03Metric.From(_inputAndSession),
                P03Metric.From(_chatAndCommands),
                P03Metric.From(_authorityAndEcs),
                P03Metric.From(_locomotionAndPhysics),
                P03Metric.From(_inventoryAndItems),
                P03Metric.From(_worldCommit),
                P03Metric.From(_serverOutboundProtocol),
                P03Metric.From(_serverInboundProtocol),
                P03Metric.From(_networkOtherResidual)),
            _timingBoundary,
            transport, profile, _adapter.SemanticDigest(), allocated,
            checked(profileServerReserved + profileClientReserved),
            profileServerReserved, profileClientReserved,
            hostSnapshot.KnownReservedBytes, hostSnapshot.KnownServerReservedBytes,
            hostSnapshot.KnownClientReservedBytes,
            _adapter.IsConverged, "", normalized,
            P03BackgroundEgressSummary.Delta(
                measuredListenerAfter.BackgroundEgress, listenerBefore.BackgroundEgress),
            new P03ProcessWindowSummary(
                processWallMilliseconds,
                processCpuMilliseconds,
                processWallMilliseconds <= 0 ? 0 :
                    processCpuMilliseconds / processWallMilliseconds,
                processPeakWorkingSetBytes,
                processWorkingSetBefore,
                processWorkingSetAfter,
                gen0Collections,
                gen1Collections,
                gen2Collections),
            new P03ListenerConfigurationSummary(
                measuredListenerAfter.ConfiguredSocketBufferBytes,
                measuredListenerAfter.ActualSocketBufferBytes,
                measuredListenerAfter.ReservedBytes),
            _retainRawSamples
                ? new P03WholeFlowRawSamples(
                    _remotePublicationPreparation.ToArray(),
                    _recipientCommitObservation.ToArray(),
                    _schedule.ToArray(),
                    _serverApplication.ToArray(),
                    _networkAndRemoteReplication.ToArray(),
                    ExclusiveResidualSamples(
                        _networkAndRemoteReplication, _schedule, _serverApplication,
                        _serverOutboundProtocol, _serverInboundProtocol),
                    _serverOutboundProtocol.ToArray(),
                    _serverInboundProtocol.ToArray(),
                    _serverOwnedRemoteReplicationBudget.ToArray(),
                    _serverOwnedTick.ToArray(),
                    _tick.ToArray())
                : null,
            drain,
            new P03MeasurementWindowBoundary(
                CountersCapturedBeforeDrain: true,
                ProtocolTimingCapturedBeforeDrain: true,
                TransportCapturedBeforeDrain: true,
                ProcessWindowCapturedBeforeDrain: true,
                ticks),
            _adapter.ApplicationEpochs);
    }

    private static double[] ExclusiveResidualSamples(
        IReadOnlyList<double> parent,
        IReadOnlyList<double> first,
        IReadOnlyList<double> second,
        IReadOnlyList<double> third,
        IReadOnlyList<double> fourth)
    {
        if (parent.Count != first.Count || parent.Count != second.Count ||
            parent.Count != third.Count || parent.Count != fourth.Count)
            throw new InvalidOperationException("Whole-flow phase sample counts diverged.");
        var result = new double[parent.Count];
        for (int index = 0; index < result.Length; index++)
            result[index] = parent[index] - first[index] - second[index] -
                third[index] - fourth[index];
        return result;
    }

    private void RunTick(int tick, bool measure)
    {
        _currentTick = tick;
        P03ProtocolTiming protocolBefore = measure ? CaptureProtocolTiming() : default;
        long totalStarted = measure ? Stopwatch.GetTimestamp() : 0;
        P03ExternalServerPhaseSample external = _externalServerWorkload?.RunTick(tick, measure)
            ?? default;
        bool captureBoundary = measure && _authorityThroughNetworkTimingEnabled &&
            !_timingBoundary.Captured;
        int boundaryOrder = 0;
        int combinedStartOrder = captureBoundary ? ++boundaryOrder : 0;
        int authorityStartOrder = captureBoundary ? ++boundaryOrder : 0;
        ulong preparedBefore = captureBoundary
            ? _adapter.PreparedSourcePublicationCount
            : 0;
        long started = measure ? Stopwatch.GetTimestamp() : 0;
        Mutate(tick);
        long mutationTicks = measure ? Stopwatch.GetTimestamp() - started : 0;
        long preparationStarted = measure ? Stopwatch.GetTimestamp() : 0;
        _adapter.ApplyAuthority(tick, _workload.MoveRecipients ? _workload.DirtyEntitiesPerTick : 0);
        long applyAuthorityTicks = measure
            ? Stopwatch.GetTimestamp() - preparationStarted
            : 0;
        long authorityTicks = measure ? mutationTicks + applyAuthorityTicks : 0;
        long publicationPreparationTicks = measure && _serverOwnedRemoteTimingEnabled
            ? checked((long)_adapter.LastPreparedSourcePreparationTicks)
            : applyAuthorityTicks;
        long recipientWakeupTicks = measure && _serverOwnedRemoteTimingEnabled
            ? checked((long)_adapter.LastRecipientCommitObservationTicks)
            : 0;
        long baseAuthoritativeTicks = Math.Max(
            0, authorityTicks - publicationPreparationTicks - recipientWakeupTicks);
        double authority = ToMilliseconds(authorityTicks);
        ulong preparedAfter = captureBoundary
            ? _adapter.PreparedSourcePublicationCount
            : 0;
        int preparedSourceObservedOrder = captureBoundary && preparedAfter > preparedBefore
            ? ++boundaryOrder
            : 0;
        int recipientWakeupObservedOrder = captureBoundary && recipientWakeupTicks > 0
            ? ++boundaryOrder
            : 0;
        int authorityEndOrder = captureBoundary ? ++boundaryOrder : 0;

        int networkStartOrder = captureBoundary ? ++boundaryOrder : 0;
        started = measure ? Stopwatch.GetTimestamp() : 0;
        _adapter.Schedule(checked((ulong)tick));
        long networkReplicationTicks = measure ? Stopwatch.GetTimestamp() - started : 0;
        double schedule = measure ? Elapsed(started) : 0;
        _tickServerApplication = 0;
        _tickClientApplication = 0;
        _tickClientNetwork = 0;
        long pumpTicks = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            long pumpStarted = measure ? Stopwatch.GetTimestamp() : 0;
            networkReplicationTicks += PumpAndDispatch(measure);
            if (measure) pumpTicks += Stopwatch.GetTimestamp() - pumpStarted;
        }
        if (!measure) return;
        int networkEndOrder = captureBoundary ? ++boundaryOrder : 0;
        long authorityThroughNetworkTicks = Stopwatch.GetTimestamp() - totalStarted;
        int combinedEndOrder = captureBoundary ? ++boundaryOrder : 0;
        double serverApp = ToMilliseconds(_tickServerApplication);
        double clientApp = ToMilliseconds(_tickClientApplication);
        double pump = ToMilliseconds(pumpTicks);
        _tick.Add(ToMilliseconds(authorityThroughNetworkTicks));
        _authority.Add(authority);
        _baseAuthoritativeMutation.Add(ToMilliseconds(baseAuthoritativeTicks));
        _remotePublicationPreparation.Add(ToMilliseconds(publicationPreparationTicks));
        _recipientCommitObservation.Add(ToMilliseconds(recipientWakeupTicks));
        _schedule.Add(schedule);
        _pump.Add(pump);
        _serverApplication.Add(serverApp);
        _clientApplication.Add(clientApp);
        _networkAndRemoteReplication.Add(ToMilliseconds(networkReplicationTicks));
        _serverOwnedRemoteReplicationBudget.Add(
            ToMilliseconds(publicationPreparationTicks + recipientWakeupTicks +
                networkReplicationTicks));
        if (_authorityThroughNetworkTimingEnabled)
            _authorityThroughNetworkAndRemoteReplication.Add(
                ToMilliseconds(authorityThroughNetworkTicks));
        if (captureBoundary)
        {
            _timingBoundary = new P03TimingBoundaryEvidence(
                true,
                combinedStartOrder,
                authorityStartOrder,
                preparedSourceObservedOrder,
                recipientWakeupObservedOrder,
                authorityEndOrder,
                networkStartOrder,
                networkEndOrder,
                combinedEndOrder,
                preparedAfter >= preparedBefore ? preparedAfter - preparedBefore : 0,
                true,
                false);
        }
        P03ProtocolTiming current = CaptureProtocolTiming();
        double serverOutbound = current.ServerOutboundMilliseconds -
            protocolBefore.ServerOutboundMilliseconds;
        double serverInbound = current.ServerInboundMilliseconds -
            protocolBefore.ServerInboundMilliseconds;
        _serverOutboundProtocol.Add(serverOutbound);
        _serverInboundProtocol.Add(serverInbound);
        _networkOtherResidual.Add(ToMilliseconds(networkReplicationTicks) - schedule - serverApp -
            serverOutbound - serverInbound);
        _serverRole.Add(authority + schedule + serverApp +
            Math.Max(0, current.ServerMilliseconds - protocolBefore.ServerMilliseconds));
        _clientRole.Add(clientApp + ToMilliseconds(_tickClientNetwork) +
            Math.Max(0, current.ClientMilliseconds - protocolBefore.ClientMilliseconds));
        long remoteTicks = checked(publicationPreparationTicks + recipientWakeupTicks +
            networkReplicationTicks);
        long attributedTicks = checked(external.InputAndSessionTicks +
            external.ChatAndCommandsTicks + external.AuthorityAndEcsTicks +
            external.LocomotionAndPhysicsTicks + external.InventoryAndItemTicks +
            external.WorldCommitTicks + remoteTicks);
        long serverOwnedTicks = checked(external.ServerOwnedTicks + remoteTicks);
        _serverOwnedTick.Add(ToMilliseconds(serverOwnedTicks));
        _attributedMainThread.Add(ToMilliseconds(attributedTicks));
        _unattributedResidual.Add(ToMilliseconds(Math.Max(0, serverOwnedTicks - attributedTicks)));
        _inputAndSession.Add(ToMilliseconds(external.InputAndSessionTicks));
        _chatAndCommands.Add(ToMilliseconds(external.ChatAndCommandsTicks));
        _authorityAndEcs.Add(ToMilliseconds(external.AuthorityAndEcsTicks));
        _locomotionAndPhysics.Add(ToMilliseconds(external.LocomotionAndPhysicsTicks));
        _inventoryAndItems.Add(ToMilliseconds(external.InventoryAndItemTicks));
        _worldCommit.Add(ToMilliseconds(external.WorldCommitTicks));
    }

    private void Mutate(int tick)
    {
        if (_workload.MutationPeriodTicks > 1 &&
            (tick - 1) % _workload.MutationPeriodTicks != 0)
            return;
        Span<int> dirty = stackalloc int[_workload.DirtyEntitiesPerTick];
        _workload.CopyDirtyEntityIndices(tick, dirty);
        for (int offset = 0; offset < dirty.Length; offset++)
        {
            int entity = dirty[offset];
            long dx = _workload.Id.StartsWith("sparse", StringComparison.Ordinal)
                ? 10 + entity % 3
                : ((tick + entity) & 1) == 0 ? 1 : -1;
            long dz = _workload.Id.StartsWith("sparse", StringComparison.Ordinal)
                ? 5 + entity % 2
                : 0;
            _x[entity] = checked(_x[entity] + dx);
            _z[entity] = checked(_z[entity] + dz);
            _versions[entity] = checked(_versions[entity] + 1);
        }
    }

    private long PumpAndDispatch(bool measure = false)
    {
        long serverStarted = measure ? Stopwatch.GetTimestamp() : 0;
        _adapter.ProgressServers();
        long serverTicks = measure ? Stopwatch.GetTimestamp() - serverStarted : 0;
        if (measure)
        {
            _host.PumpMeasured(out long hostServerTicks, out long hostClientTicks);
            serverTicks += hostServerTicks;
            _tickClientNetwork += hostClientTicks;
        }
        else
        {
            _host.Pump();
        }
        bool dispatchServerProofs = _serverProofDispatchIntervalTicks == 1 ||
            _currentTick % _serverProofDispatchIntervalTicks == 0;
        for (int index = 0; index < _sessions.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            while (dispatchServerProofs && _sessions[index].Server.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                if (!_adapter.ProcessServer(index, in message, payload))
                    throw new InvalidOperationException(
                        $"Server adapter rejected message {message.MessageId.Value}.");
                Require(_sessions[index].Server.TryConsumeReceived(), "Server receive consume failed.");
            }
            long serverApplicationTicks = Stopwatch.GetTimestamp() - started;
            _tickServerApplication += serverApplicationTicks;
            if (measure) serverTicks += serverApplicationTicks;
            started = Stopwatch.GetTimestamp();
            while (_sessions[index].Client.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                if (!_adapter.ProcessClient(index, in message, payload))
                    throw new InvalidOperationException(
                        $"Client adapter rejected message {message.MessageId.Value}.");
                Require(_sessions[index].Client.TryConsumeReceived(), "Client receive consume failed.");
            }
            _adapter.ProgressClient(index);
            _tickClientApplication += Stopwatch.GetTimestamp() - started;
        }
        return serverTicks;
    }

    private int DrainToConvergence(ulong step, int guard)
    {
        for (int round = 0; round < guard; round++)
        {
            _currentTick = checked((int)Math.Min(int.MaxValue, step + (ulong)round));
            _adapter.Schedule(step + checked((ulong)round));
            for (int pass = 0; pass < 4; pass++) _ = PumpAndDispatch();
            if (_adapter.IsConverged) return checked(round + 1);
            if ((round & 31) == 31) Thread.Yield();
        }
        throw new InvalidOperationException(
            $"{_profile}/{_workload.Id} did not converge: {_adapter.DescribeState()}.");
    }

    private void ResetMeasurement(int ticks)
    {
        _tick.Clear(); _authority.Clear(); _schedule.Clear(); _pump.Clear();
        _baseAuthoritativeMutation.Clear(); _remotePublicationPreparation.Clear();
        _recipientCommitObservation.Clear();
        _serverApplication.Clear(); _clientApplication.Clear();
        _networkAndRemoteReplication.Clear();
        _serverOutboundProtocol.Clear(); _serverInboundProtocol.Clear();
        _networkOtherResidual.Clear();
        _serverOwnedRemoteReplicationBudget.Clear();
        _authorityThroughNetworkAndRemoteReplication.Clear();
        _serverRole.Clear(); _clientRole.Clear();
        _serverOwnedTick.Clear(); _attributedMainThread.Clear();
        _unattributedResidual.Clear(); _inputAndSession.Clear();
        _chatAndCommands.Clear(); _authorityAndEcs.Clear();
        _locomotionAndPhysics.Clear(); _inventoryAndItems.Clear(); _worldCommit.Clear();
        _timingBoundary = default;
        _tick.EnsureCapacity(ticks); _authority.EnsureCapacity(ticks);
        _baseAuthoritativeMutation.EnsureCapacity(ticks);
        _remotePublicationPreparation.EnsureCapacity(ticks);
        _recipientCommitObservation.EnsureCapacity(ticks);
        _schedule.EnsureCapacity(ticks); _pump.EnsureCapacity(ticks);
        _serverApplication.EnsureCapacity(ticks); _clientApplication.EnsureCapacity(ticks);
        _networkAndRemoteReplication.EnsureCapacity(ticks);
        _serverOutboundProtocol.EnsureCapacity(ticks);
        _serverInboundProtocol.EnsureCapacity(ticks);
        _networkOtherResidual.EnsureCapacity(ticks);
        _serverOwnedRemoteReplicationBudget.EnsureCapacity(ticks);
        _authorityThroughNetworkAndRemoteReplication.EnsureCapacity(ticks);
        _serverRole.EnsureCapacity(ticks); _clientRole.EnsureCapacity(ticks);
        _serverOwnedTick.EnsureCapacity(ticks); _attributedMainThread.EnsureCapacity(ticks);
        _unattributedResidual.EnsureCapacity(ticks); _inputAndSession.EnsureCapacity(ticks);
        _chatAndCommands.EnsureCapacity(ticks); _authorityAndEcs.EnsureCapacity(ticks);
        _locomotionAndPhysics.EnsureCapacity(ticks); _inventoryAndItems.EnsureCapacity(ticks);
        _worldCommit.EnsureCapacity(ticks);
        for (int index = 0; index < _sessions.Length; index++)
        {
            _sessions[index].Server.ResetStageTiming(_stageTimingEnabled);
            _sessions[index].Client.ResetStageTiming(_stageTimingEnabled);
        }
        _host.ResetListenerStageTiming(_stageTimingEnabled);
        _adapter.ResetCounters();
        _externalServerWorkload?.ResetMeasurement();
    }

    private P03ProtocolTiming CaptureProtocolTiming()
    {
        NetworkProtocolStageTiming server = default;
        NetworkProtocolStageTiming client = default;
        for (int index = 0; index < _sessions.Length; index++)
        {
            server = Add(server, _sessions[index].Server.GetStageTiming());
            client = Add(client, _sessions[index].Client.GetStageTiming());
        }
        return new P03ProtocolTiming(
            Stage(server), Stage(client),
            ToMilliseconds(server.EmitTicks + server.InboundTicks),
            ToMilliseconds(client.EmitTicks + client.InboundTicks),
            ToMilliseconds(server.EmitTicks),
            ToMilliseconds(server.InboundTicks),
            ToMilliseconds(client.EmitTicks),
            ToMilliseconds(client.InboundTicks));
    }

    private P03TransportTotals CaptureTransport()
    {
        P03TransportTotals total = default;
        for (int index = 0; index < _sessions.Length; index++)
        {
            SharedUdpProtocolSessionSnapshot snapshot = _sessions[index].GetSnapshot();
            total.Add(snapshot.Server);
            total.Add(snapshot.Client);
            total.ClientWireDatagrams += snapshot.ClientDatagrams.Sent;
            total.ClientWireBytes += snapshot.ClientDatagrams.SentBytes;
        }
        return total;
    }

    private void InitializePositions()
    {
        if (_workload.EntityLayout == P03EntityLayout.TopologyCompactGroups)
        {
            const int entitiesPerChunk = 64;
            const long groupStride = 192_000;
            ReadOnlySpan<(int X, int Z)> chunkOffsets =
            [
                (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1),
                (-1, -1), (1, -1), (-1, 1), (1, 1),
            ];
            int groupColumns = checked((int)Math.Ceiling(Math.Sqrt(_workload.TopologyGroups)));
            for (int group = 0; group < _workload.TopologyGroups; group++)
            {
                int start = checked(group * _workload.Entities / _workload.TopologyGroups);
                int end = checked((group + 1) * _workload.Entities / _workload.TopologyGroups);
                long originX = checked((long)(group % groupColumns) * groupStride);
                long originZ = checked((long)(group / groupColumns) * groupStride);
                for (int index = start; index < end; index++)
                {
                    int local = index - start;
                    int chunk = local / entitiesPerChunk;
                    if (chunk >= chunkOffsets.Length)
                        throw new InvalidOperationException(
                            "The compact topology exceeded its bounded nearby chunks.");
                    int slot = local % entitiesPerChunk;
                    _x[index] = checked(originX +
                        (long)chunkOffsets[chunk].X *
                            GameWorldSpatialProfile.WorldChunkEdgeUnits +
                        1_000L + slot % 8 * 1_750L);
                    _z[index] = checked(originZ +
                        (long)chunkOffsets[chunk].Z *
                            GameWorldSpatialProfile.WorldChunkEdgeUnits +
                        1_000L + slot / 8 * 1_750L);
                    _versions[index] = 1;
                }
            }
        }
        else if (_workload.EntityLayout == P03EntityLayout.TopologyChunkGroups)
        {
            const int entitiesPerChunk = 20;
            const long groupStride = 192_000;
            int groupColumns = checked((int)Math.Ceiling(Math.Sqrt(_workload.TopologyGroups)));
            for (int group = 0; group < _workload.TopologyGroups; group++)
            {
                int start = checked((int)((long)group * _workload.Entities /
                    _workload.TopologyGroups));
                int end = checked((int)((long)(group + 1) * _workload.Entities /
                    _workload.TopologyGroups));
                int count = end - start;
                int chunkCount = Math.Max(1,
                    (count + entitiesPerChunk - 1) / entitiesPerChunk);
                int chunkColumns = checked((int)Math.Ceiling(Math.Sqrt(chunkCount)));
                long originX = checked((long)(group % groupColumns) * groupStride);
                long originZ = checked((long)(group / groupColumns) * groupStride);
                for (int index = start; index < end; index++)
                {
                    int local = index - start;
                    int chunk = local / entitiesPerChunk;
                    int row = local % entitiesPerChunk;
                    _x[index] = checked(originX + (long)(chunk % chunkColumns) *
                        GameWorldSpatialProfile.WorldChunkEdgeUnits + 1_000L +
                        row % 5 * 2_500L);
                    _z[index] = checked(originZ + (long)(chunk / chunkColumns) *
                        GameWorldSpatialProfile.WorldChunkEdgeUnits + 1_000L +
                        row / 5 * 2_500L);
                    _versions[index] = 1;
                }
            }
        }
        else if (_workload.EntityLayout == P03EntityLayout.V2DenseClosestTwoMeterGrid)
        {
            const int width = 20;
            for (int index = 0; index < _workload.Entities; index++)
            {
                _x[index] = checked(1_000L + index % width * 2_000L);
                _z[index] = checked(1_000L + index / width * 2_000L);
                _versions[index] = 1;
            }
        }
        else if (_workload.EntityLayout == P03EntityLayout.TwentyEntitiesPerChunk)
        {
            const int entitiesPerChunk = 20;
            const int chunkWidth = 10;
            for (int index = 0; index < _workload.Entities; index++)
            {
                int chunk = index / entitiesPerChunk;
                int row = index % entitiesPerChunk;
                _x[index] = checked((long)(chunk % chunkWidth) *
                    GameWorldSpatialProfile.WorldChunkEdgeUnits + 1_000L + row % 5 * 2_500L);
                _z[index] = checked((long)(chunk / chunkWidth) *
                    GameWorldSpatialProfile.WorldChunkEdgeUnits + 1_000L + row / 5 * 2_500L);
                _versions[index] = 1;
            }
        }
        else if (!_workload.Id.StartsWith("sparse", StringComparison.Ordinal))
        {
            int width = checked((int)Math.Ceiling(Math.Sqrt(_workload.Entities)));
            for (int index = 0; index < _workload.Entities; index++)
            {
                _x[index] = checked(2_000L + index % width * 4_000L);
                _z[index] = checked(2_000L + index / width * 4_000L);
                _versions[index] = 1;
            }
        }
        else
        {
            const int width = 64;
            for (int index = 0; index < _workload.Entities; index++)
            {
                _x[index] = checked((index % width) * 12_000L);
                _z[index] = checked((index / width) * 12_000L);
                _versions[index] = 1;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter.Dispose();
        _externalServerWorkload?.Dispose();
        _host.Dispose();
    }

    internal static ParticipantId Participant(int index) => new(checked((ulong)index + 1));
    internal static int MaximumPlaintextDatagramBytes =>
        NetworkSecureDatagramFraming.MaximumPlaintextBytes(s_udp.MaximumDatagramBytes);
    internal static ReplicationEntityId ReplicationId(int index) => new(checked((uint)index), 1);
    internal static ReplicationSpatialPoint Point(long x, long z) =>
        new(checked((int)x), checked((int)z));
    internal static WorldPosition Position(long x, long z)
    {
        Require(WorldPosition.TryFromGlobalMillimeters(x, 0, z, out WorldPosition value),
            "Whole-flow position exceeded the world coordinate profile.");
        return value;
    }
    internal static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static double Elapsed(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private static double ToMilliseconds(long ticks) => ticks * 1_000d / Stopwatch.Frequency;
    private static double ToMilliseconds(ulong ticks) => ticks * 1_000d / Stopwatch.Frequency;

    private static P03ProtocolStage Stage(NetworkProtocolStageTiming value) => new(
        ToMilliseconds(value.SelectionTicks), ToMilliseconds(value.FrameEncodeTicks),
        ToMilliseconds(value.SecureSendTicks), ToMilliseconds(value.EndpointSendTicks),
        ToMilliseconds(value.EndpointReceiveTicks), ToMilliseconds(value.SecureReceiveTicks),
        ToMilliseconds(value.FrameAdmissionTicks), value.EmitCalls, value.IdleEmitCalls,
        value.InboundCalls, value.EmptyInboundCalls, value.InboundDatagrams,
        value.OutboundUnreliableFrames, value.InboundUnreliableFrames,
        value.LaneSelectionPasses, value.ReliableSlotProbes,
        value.IndependentSlotProbes, value.UnreliableSlotProbes,
        value.ExcludedSelectionProbes, value.EmptySelectionAttempts,
        value.ReadySelections);

    private static NetworkProtocolStageTiming Add(
        NetworkProtocolStageTiming left, NetworkProtocolStageTiming right) => new(
        left.Level,
        Add(left.EmitTicks, right.EmitTicks), Add(left.EmitCalls, right.EmitCalls),
        Add(left.IdleEmitCalls, right.IdleEmitCalls), Add(left.SelectionTicks, right.SelectionTicks),
        Add(left.FrameEncodeTicks, right.FrameEncodeTicks), Add(left.SecureSendTicks, right.SecureSendTicks),
        Add(left.EndpointSendTicks, right.EndpointSendTicks), Add(left.InboundTicks, right.InboundTicks),
        Add(left.InboundCalls, right.InboundCalls), Add(left.EmptyInboundCalls, right.EmptyInboundCalls),
        Add(left.InboundDatagrams, right.InboundDatagrams), Add(left.EndpointReceiveTicks, right.EndpointReceiveTicks),
        Add(left.SecureReceiveTicks, right.SecureReceiveTicks), Add(left.FrameAdmissionTicks, right.FrameAdmissionTicks),
        Add(left.ReliableCompactionTicks, right.ReliableCompactionTicks),
        Add(left.ReceivedCompactionTicks, right.ReceivedCompactionTicks),
        Add(left.OutboundUnreliableFrames, right.OutboundUnreliableFrames),
        Add(left.InboundUnreliableFrames, right.InboundUnreliableFrames),
        Add(left.LaneSelectionPasses, right.LaneSelectionPasses),
        Add(left.ReliableSlotProbes, right.ReliableSlotProbes),
        Add(left.IndependentSlotProbes, right.IndependentSlotProbes),
        Add(left.UnreliableSlotProbes, right.UnreliableSlotProbes),
        Add(left.ExcludedSelectionProbes, right.ExcludedSelectionProbes),
        Add(left.EmptySelectionAttempts, right.EmptySelectionAttempts),
        Add(left.ReadySelections, right.ReadySelections));
    private static ulong Add(ulong left, ulong right) => ulong.MaxValue - left < right
        ? ulong.MaxValue : left + right;
}

internal abstract class P03RemoteProfileAdapter : IDisposable
{
    protected const ulong ApplicationEpoch = 0xF03_7001;
    protected readonly P03NetworkWorkload Workload;
    protected readonly SharedUdpProtocolSession[] Sessions;
    protected readonly long[] X;
    protected readonly long[] Z;
    protected readonly ulong[] Versions;
    protected ulong ScheduleCalls;
    protected ulong ScheduleSuccesses;
    protected ulong ClientMessages;
    protected ulong ServerProofMessages;
    protected P03RemoteProfileAdapter(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions)
    {
        Workload = workload; Sessions = sessions; X = x; Z = z; Versions = versions;
    }
    internal abstract void ApplyAuthority(int tick, int movedRecipients);
    internal virtual ulong PreparedSourcePublicationCount => 0;
    internal virtual ulong LastPreparedSourcePreparationTicks => 0;
    internal virtual ulong LastRecipientCommitObservationTicks => 0;
    internal virtual P03ApplicationEpochSummary ApplicationEpochs => default;
    internal abstract void Schedule(ulong step);
    internal abstract bool ProcessServer(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload);
    internal abstract bool ProcessClient(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload);
    internal virtual void ProgressServers() { }
    internal virtual void ConfigureTelemetry(GameTelemetryLevel level) { }
    internal virtual void ConfigureServerOwnedRemoteTiming(bool enabled) { }
    internal abstract void ProgressClient(int recipient);
    internal abstract bool IsConverged { get; }
    internal abstract void Validate();
    internal abstract string SemanticDigest();
    internal abstract long GetServerReservedBytes();
    internal abstract long GetClientReservedBytes();
    internal abstract string DescribeState();
    internal virtual P03ProfileCounters GetCounters() => new(
        ScheduleCalls, ScheduleSuccesses, ClientMessages, ServerProofMessages, 0, 0, 0, 0);
    internal virtual void ResetCounters()
    {
        ScheduleCalls = ScheduleSuccesses = ClientMessages = ServerProofMessages = 0;
    }
    protected int ExpectedVisible(int recipient)
    {
        long rx = RecipientX(recipient);
        long rz = RecipientZ(recipient);
        long radius = Workload.EntityInterest.FarRadiusMillimeters;
        int count = 0;
        for (int entity = 0; entity < X.Length; entity++)
        {
            long dx = X[entity] - rx;
            long dz = Z[entity] - rz;
            if (dx * dx + dz * dz <= radius * radius) count++;
        }
        return count;
    }
    protected long RecipientX(int recipient)
    {
        if (Workload.EntityLayout != P03EntityLayout.TopologyCompactGroups)
            return X[recipient % X.Length];
        int group = checked((int)((long)recipient * Workload.TopologyGroups /
            Workload.Recipients));
        int columns = checked((int)Math.Ceiling(Math.Sqrt(Workload.TopologyGroups)));
        return checked((long)(group % columns) * 192_000L + 8_000L);
    }

    protected long RecipientZ(int recipient)
    {
        if (Workload.EntityLayout != P03EntityLayout.TopologyCompactGroups)
            return Z[recipient % Z.Length];
        int group = checked((int)((long)recipient * Workload.TopologyGroups /
            Workload.Recipients));
        int columns = checked((int)Math.Ceiling(Math.Sqrt(Workload.TopologyGroups)));
        return checked((long)(group / columns) * 192_000L + 8_000L);
    }
    protected static GameProjectedEntity Projected(int entity, ulong version) => new(
        P03WholeFlowHarness.ReplicationId(entity), true,
        new GameSimulationState { TickMarker = checked((int)version), InputVelocity = entity });
    public abstract void Dispose();
}

internal sealed class P03MaintainedRemoteAdapter : P03RemoteProfileAdapter
{
    private const int RecordsPerBundle = 24;
    private const int ByteBudget = 1_200;
    private readonly GameInterestService _service;
    private readonly RemoteWorldStateServer[] _servers;
    private readonly RemoteWorldStateClient[] _clients;

    internal P03MaintainedRemoteAdapter(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions)
        : base(workload, sessions, x, z, versions)
    {
        _service = new GameInterestService(new StateEpoch(0xF03_7101),
            CreateLimits(workload), workload.EntityInterest,
            GameInterestAdmissionMode.ExactDistance);
        for (int recipient = 0; recipient < workload.Recipients; recipient++)
            P03WholeFlowHarness.Require(_service.SetRecipientAnchor(
                P03WholeFlowHarness.Participant(recipient),
                P03WholeFlowHarness.Point(RecipientX(recipient), RecipientZ(recipient)), 0) ==
                GameInterestUpdateStatus.Success, "Maintained recipient setup failed.");
        for (int entity = 0; entity < workload.Entities; entity++)
            P03WholeFlowHarness.Require(_service.UpsertEntity(
                Projected(entity, Versions[entity]), Versions[entity],
                P03WholeFlowHarness.Point(X[entity], Z[entity]),
                GameReplicationHorizon.Far, 0) == GameInterestUpdateStatus.Success,
                "Maintained entity setup failed.");
        _servers = new RemoteWorldStateServer[sessions.Length];
        _clients = new RemoteWorldStateClient[sessions.Length];
        for (int index = 0; index < sessions.Length; index++)
        {
            int capacity = Math.Max(64, ExpectedVisible(index) + 32);
            _servers[index] = new RemoteWorldStateServer(
                sessions[index].Server, _service, P03WholeFlowHarness.Participant(index),
                ApplicationEpoch, RecordsPerBundle, capacity);
            _clients[index] = new RemoteWorldStateClient(
                sessions[index].Client, ApplicationEpoch, _service.StateEpoch,
                capacity, capacity, RecordsPerBundle, 128);
        }
    }

    internal override void ApplyAuthority(int tick, int movedRecipients)
    {
        for (int recipient = 0; recipient < movedRecipients; recipient++)
            P03WholeFlowHarness.Require(_service.SetRecipientAnchor(
                P03WholeFlowHarness.Participant(recipient),
                P03WholeFlowHarness.Point(RecipientX(recipient), RecipientZ(recipient)),
                checked(tick * (long)Workload.TickDurationMilliseconds)) is
                GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange,
                "Maintained recipient movement failed.");
        int start = checked((tick - 1) * Workload.DirtyEntitiesPerTick % Workload.Entities);
        for (int offset = 0; offset < Workload.DirtyEntitiesPerTick; offset++)
        {
            int entity = (start + offset) % Workload.Entities;
            P03WholeFlowHarness.Require(_service.UpsertEntity(
                Projected(entity, Versions[entity]), Versions[entity],
                P03WholeFlowHarness.Point(X[entity], Z[entity]),
                GameReplicationHorizon.Far,
                checked(tick * (long)Workload.TickDurationMilliseconds)) is
                GameInterestUpdateStatus.Success or GameInterestUpdateStatus.NoChange,
                "Maintained entity mutation failed.");
        }
    }

    internal override void Schedule(ulong step)
    {
        long now = checked((long)step * Workload.TickDurationMilliseconds);
        for (int recipient = 0; recipient < _servers.Length; recipient++)
            for (int bundle = 0; bundle < 8; bundle++)
            {
                ScheduleCalls++;
                GameWorldStateBuildStatus status = _servers[recipient].TrySchedule(now, ByteBudget);
                if (status != GameWorldStateBuildStatus.Success) break;
                ScheduleSuccesses++;
            }
    }

    internal override bool ProcessServer(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _servers[recipient].TryProcess(in message, payload);
        if (handled) ServerProofMessages++;
        return handled;
    }

    internal override bool ProcessClient(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _clients[recipient].TryProcess(in message, payload);
        if (handled) ClientMessages++;
        return handled;
    }

    internal override void ProgressClient(int recipient) =>
        _ = _clients[recipient].TryFlushAcknowledgements();

    internal override bool IsConverged
    {
        get
        {
            GameInterestSnapshot state = _service.GetSnapshot();
            if (state.PendingRelationCount != 0 || state.OutstandingBundleCount != 0 ||
                !NetworkQueuesEmpty()) return false;
            for (int index = 0; index < _clients.Length; index++)
            {
                RemoteWorldStateClientSnapshot client = _clients[index].GetSnapshot();
                if (client.ActiveEntityCount != ExpectedVisible(index) ||
                    client.PendingApplicationAcknowledgements != 0 || client.IsFaulted)
                    return false;
            }
            return true;
        }
    }

    internal override void Validate()
    {
        P03WholeFlowHarness.Require(IsConverged, "Maintained profile failed final convergence.");
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteWorldStateServerSnapshot server = _servers[index].GetSnapshot();
            RemoteWorldStateClientSnapshot client = _clients[index].GetSnapshot();
            P03WholeFlowHarness.Require(server.ApplicationRejections == 0 &&
                server.ApplicationCapacityFailures == 0 && server.MalformedAcknowledgements == 0 &&
                client.RejectedBundles == 0, "Maintained remote path rejected semantic state.");
        }
    }

    internal override string SemanticDigest()
    {
        ulong hash = 14_695_981_039_346_656_003UL;
        var scratch = new RemoteWorldEntitySnapshot[Workload.Entities];
        for (int recipient = 0; recipient < _clients.Length; recipient++)
        {
            int count = _clients[recipient].CopyActiveEntities(scratch);
            Array.Sort(scratch, 0, count, EntityComparer.Instance);
            Mix(ref hash, checked((ulong)count));
            for (int index = 0; index < count; index++) Mix(ref hash, scratch[index]);
        }
        return hash.ToString("X16");
    }

    internal override long GetServerReservedBytes()
    {
        long value = _service.GetSnapshot().ReservedBytes;
        for (int index = 0; index < _servers.Length; index++)
            value = checked(value + _servers[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override long GetClientReservedBytes()
    {
        long value = 0;
        for (int index = 0; index < _clients.Length; index++)
            value = checked(value + _clients[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override P03ProfileCounters GetCounters()
    {
        GameInterestSnapshot snapshot = _service.GetSnapshot();
        return base.GetCounters() with
        {
            SuccessfulSemanticPublications = ScheduleSuccesses,
            GameMessagesQueued = ScheduleSuccesses,
            SemanticKnowledgeRows = checked((ulong)snapshot.RelationCount),
        };
    }

    internal override string DescribeState() => _service.GetSnapshot().ToString();

    public override void Dispose() { }

    private bool NetworkQueuesEmpty()
    {
        for (int index = 0; index < Sessions.Length; index++)
            if (Sessions[index].Server.IndependentUnreliableQueued != 0 ||
                Sessions[index].Client.IndependentUnreliableQueued != 0 ||
                Sessions[index].Server.ReliableQueued != 0 || Sessions[index].Client.ReliableQueued != 0 ||
                Sessions[index].Server.ReceivedQueued != 0 || Sessions[index].Client.ReceivedQueued != 0)
                return false;
        return true;
    }

    private static GameInterestLimits CreateLimits(P03NetworkWorkload workload)
    {
        GameInterestProfile profile = workload.EntityInterest;
        int farCells = checked((profile.FarRadiusMillimeters + profile.RegionEdgeMillimeters - 1) /
            profile.RegionEdgeMillimeters);
        int width = checked(farCells * 2 + 1);
        int edges = checked(width * width * workload.Recipients * 2 + width);
        return new GameInterestLimits(
            workload.Entities, workload.Recipients,
            Math.Max(16_384, checked(workload.Entities + edges)),
            checked(edges + workload.Entities),
            checked(workload.Entities * workload.Recipients),
            Math.Max(64, workload.Recipients * 8), RecordsPerBundle);
    }

    internal static void Mix(ref ulong hash, in RemoteWorldEntitySnapshot row)
    {
        Mix(ref hash, row.Entity.Index); Mix(ref hash, row.Entity.Generation);
        Mix(ref hash, row.CanonicalVersion); Mix(ref hash, unchecked((ulong)row.XMillimeters));
        Mix(ref hash, unchecked((ulong)row.ZMillimeters));
    }
    internal static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash = unchecked(hash * 1_099_511_628_211UL);
    }
    internal sealed class EntityComparer : IComparer<RemoteWorldEntitySnapshot>
    {
        internal static readonly EntityComparer Instance = new();
        public int Compare(RemoteWorldEntitySnapshot x, RemoteWorldEntitySnapshot y) =>
            x.Entity.CompareTo(y.Entity);
    }
}

internal sealed class P03DirtyEntityRemoteAdapter : P03RemoteProfileAdapter
{
    private const int RecordsPerBundle = 32;
    private const int ByteBudget = 1_405;
    private readonly DirtyEntitySpatialFanout _service;
    private readonly RemoteDirtyEntitySpatialFanoutServer[] _servers;
    private readonly RemoteDirtyEntitySpatialFanoutClient[] _clients;

    internal P03DirtyEntityRemoteAdapter(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions, bool chunkBatch)
        : base(workload, sessions, x, z, versions)
    {
        GameInterestLimits baseLimits = P03MaintainedRemoteAdapter_CreateLimits(workload);
        var limits = new DirtyEntitySpatialFanoutLimits(
            workload.Entities, workload.Recipients, baseLimits.MaximumRegions,
            checked(workload.Entities * workload.Recipients),
            Math.Max(64, workload.Recipients * 8), RecordsPerBundle, 128_000);
        _service = chunkBatch
            ? new DirtyEntityChunkBatchFanout(new StateEpoch(0xF03_7202), limits, workload.EntityInterest)
            : new DirtyEntitySpatialFanout(new StateEpoch(0xF03_7201), limits, workload.EntityInterest);
        for (int recipient = 0; recipient < workload.Recipients; recipient++)
            P03WholeFlowHarness.Require(_service.SetRecipientAnchor(
                P03WholeFlowHarness.Participant(recipient),
                P03WholeFlowHarness.Point(RecipientX(recipient), RecipientZ(recipient)), 0) ==
                DirtyEntityUpdateStatus.Success, "DirtyEntity recipient setup failed.");
        for (int entity = 0; entity < workload.Entities; entity++) CommitEntity(entity, 0);
        _servers = new RemoteDirtyEntitySpatialFanoutServer[sessions.Length];
        _clients = new RemoteDirtyEntitySpatialFanoutClient[sessions.Length];
        for (int index = 0; index < sessions.Length; index++)
        {
            int capacity = Math.Max(64, ExpectedVisible(index) + 32);
            _servers[index] = new RemoteDirtyEntitySpatialFanoutServer(
                sessions[index].Server, _service, P03WholeFlowHarness.Participant(index),
                ApplicationEpoch, RecordsPerBundle);
            _clients[index] = new RemoteDirtyEntitySpatialFanoutClient(
                sessions[index].Client, ApplicationEpoch, _service.StateEpoch,
                capacity, capacity, RecordsPerBundle, 128);
        }
    }

    internal override void ApplyAuthority(int tick, int movedRecipients)
    {
        for (int recipient = 0; recipient < movedRecipients; recipient++)
            P03WholeFlowHarness.Require(_service.SetRecipientAnchor(
                P03WholeFlowHarness.Participant(recipient),
                P03WholeFlowHarness.Point(RecipientX(recipient), RecipientZ(recipient)),
                checked(tick * (long)Workload.TickDurationMilliseconds)) is
                DirtyEntityUpdateStatus.Success or DirtyEntityUpdateStatus.NoChange,
                "DirtyEntity recipient movement failed.");
        int start = checked((tick - 1) * Workload.DirtyEntitiesPerTick % Workload.Entities);
        for (int offset = 0; offset < Workload.DirtyEntitiesPerTick; offset++)
            CommitEntity((start + offset) % Workload.Entities, tick);
    }

    private void CommitEntity(int entity, int tick)
    {
        DirtyEntityUpdateStatus status = _service.CommitEntity(
            new DirtyEntityLatestState(
                P03WholeFlowHarness.ReplicationId(entity), Versions[entity], true, true,
                GameReplicationHorizon.Far,
                P03WholeFlowHarness.Point(X[entity], Z[entity]),
                Projected(entity, Versions[entity]).SimulationState),
            checked(tick * (long)Workload.TickDurationMilliseconds));
        P03WholeFlowHarness.Require(status is DirtyEntityUpdateStatus.Success or
            DirtyEntityUpdateStatus.NoChange, "DirtyEntity mutation failed.");
    }

    internal override void Schedule(ulong step)
    {
        long now = checked((long)step * Workload.TickDurationMilliseconds);
        for (int recipient = 0; recipient < _servers.Length; recipient++)
            for (int bundle = 0; bundle < 8; bundle++)
            {
                ScheduleCalls++;
                DirtyEntityBuildStatus status = _servers[recipient].TrySchedule(now, ByteBudget);
                if (status != DirtyEntityBuildStatus.Success) break;
                ScheduleSuccesses++;
            }
    }

    internal override bool ProcessServer(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _servers[recipient].TryProcess(in message, payload);
        if (handled) ServerProofMessages++;
        return handled;
    }
    internal override bool ProcessClient(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _clients[recipient].TryProcess(in message, payload);
        if (handled) ClientMessages++;
        return handled;
    }
    internal override void ProgressClient(int recipient) => _ = _clients[recipient].TryFlushProofs();

    internal override bool IsConverged
    {
        get
        {
            DirtyEntitySpatialFanoutSnapshot state = _service.GetSnapshot();
            if (state.PendingObligationCount != 0 || state.OutstandingBundleCount != 0)
                return false;
            for (int index = 0; index < _clients.Length; index++)
            {
                RemoteDirtyEntitySpatialFanoutClientSnapshot client = _clients[index].GetSnapshot();
                if (client.ActiveEntityCount != ExpectedVisible(index) ||
                    client.PendingApplicationProofs != 0 || client.IsFaulted)
                    return false;
                if (Sessions[index].Server.IndependentUnreliableQueued != 0 ||
                    Sessions[index].Client.IndependentUnreliableQueued != 0 ||
                    Sessions[index].Server.ReceivedQueued != 0 || Sessions[index].Client.ReceivedQueued != 0)
                    return false;
            }
            return true;
        }
    }
    internal override void Validate()
    {
        P03WholeFlowHarness.Require(IsConverged, "DirtyEntity profile failed final convergence.");
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteDirtyEntitySpatialFanoutServerSnapshot server = _servers[index].GetSnapshot();
            RemoteDirtyEntitySpatialFanoutClientSnapshot client = _clients[index].GetSnapshot();
            P03WholeFlowHarness.Require(server.ApplicationRejections == 0 &&
                server.MalformedProofs == 0 && client.RejectedBundles == 0,
                "DirtyEntity remote path rejected semantic state.");
        }
    }
    internal override string SemanticDigest()
    {
        ulong hash = 14_695_981_039_346_656_003UL;
        var scratch = new RemoteWorldEntitySnapshot[Workload.Entities];
        for (int recipient = 0; recipient < _clients.Length; recipient++)
        {
            int count = _clients[recipient].CopyActiveEntities(scratch);
            Array.Sort(scratch, 0, count, P03MaintainedRemoteAdapter.EntityComparer.Instance);
            P03MaintainedRemoteAdapter.Mix(ref hash, checked((ulong)count));
            for (int index = 0; index < count; index++)
                P03MaintainedRemoteAdapter.Mix(ref hash, scratch[index]);
        }
        return hash.ToString("X16");
    }
    internal override long GetServerReservedBytes()
    {
        long value = _service.GetSnapshot().ReservedBytes;
        for (int index = 0; index < _servers.Length; index++)
            value = checked(value + _servers[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override long GetClientReservedBytes()
    {
        long value = 0;
        for (int index = 0; index < _clients.Length; index++)
            value = checked(value + _clients[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override P03ProfileCounters GetCounters()
    {
        DirtyEntitySpatialFanoutSnapshot snapshot = _service.GetSnapshot();
        return base.GetCounters() with
        {
            SuccessfulSemanticPublications = ScheduleSuccesses,
            GameMessagesQueued = ScheduleSuccesses,
            SemanticKnowledgeRows = checked((ulong)snapshot.SemanticKnowledgeRowCount),
        };
    }
    internal override string DescribeState() => _service.GetSnapshot().ToString();
    public override void Dispose() { }

    private static GameInterestLimits P03MaintainedRemoteAdapter_CreateLimits(P03NetworkWorkload workload)
    {
        GameInterestProfile profile = workload.EntityInterest;
        int farCells = checked((profile.FarRadiusMillimeters + profile.RegionEdgeMillimeters - 1) /
            profile.RegionEdgeMillimeters);
        int width = checked(farCells * 2 + 1);
        int edges = checked(width * width * workload.Recipients * 2 + width);
        return new GameInterestLimits(workload.Entities, workload.Recipients,
            Math.Max(16_384, checked(workload.Entities + edges)), checked(edges + workload.Entities),
            checked(workload.Entities * workload.Recipients), Math.Max(64, workload.Recipients * 8), 32);
    }
}

internal abstract class P03ChunkRemoteAdapterBase : P03RemoteProfileAdapter
{
    private const ulong ComparisonPublicationEpoch = 0xF03_7203;
    protected readonly EcsWorld World;
    protected readonly Entity[] Entities;
    protected readonly ChunkPublicationStore Source;
    protected readonly BoundWorldPack BoundWorld;
    protected readonly SpatialChunkKey[] PopulatedKeys;
    protected readonly WorldPublicChange[] Changes;
    protected readonly ClientWorldSemanticCache[] Caches;
    protected readonly int[] ExpectedCounts;
    private readonly WorldPublicRecord[] _snapshotScratch =
        new WorldPublicRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];

    protected P03ChunkRemoteAdapterBase(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions,
        WorldContentFingerprint content,
        PreparedChunkPublicationReadModel readModel =
            PreparedChunkPublicationReadModel.PreparedLatestState)
        : base(workload, sessions, x, z, versions)
    {
        World = new EcsWorld(GameSchema.CreateSchema(), new EcsWorldOptions(
            new EntityCapacity(workload.Entities), 2, 4,
            lifecycleRecordCapacity: checked(workload.Entities * 2)));
        ArchetypeHandle empty = World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16,
            Math.Max(1, (workload.Entities + 255) / 256));
        World.Seal();
        World.Initialize();
        Entities = new Entity[workload.Entities];
        for (int index = 0; index < Entities.Length; index++)
            P03WholeFlowHarness.Require(World.TryCreate(empty, out Entities[index]),
                "Chunk fixture entity setup failed.");
        Source = new ChunkPublicationStore(
            new PublicationEpoch(ComparisonPublicationEpoch),
            new ChunkPublicationLimits(workload.Entities,
                checked(workload.Entities * 2), 64,
                Math.Max(65_536, checked(workload.Entities * 64))),
            readModel);
        Changes = new WorldPublicChange[workload.Entities];
        for (int index = 0; index < workload.Entities; index++)
            Changes[index] = Present(index);
        P03WholeFlowHarness.Require(Source.TryCommit(Changes, out _),
            "Initial chunk publication failed.");
        PopulatedKeys = Enumerable.Range(0, workload.Entities)
            .Select(index => P03WholeFlowHarness.Position(X[index], Z[index])
                .ToChunkKey(GameWorldState.DefaultSpace))
            .Distinct().Order().ToArray();
        BoundWorld = CreateWorld(CollectTerrainKeys(workload, x, z), content);
        Caches = new ClientWorldSemanticCache[workload.Recipients];
        ExpectedCounts = new int[workload.Recipients];
    }

    protected void CommitMutation(int tick)
    {
        Span<int> dirty = stackalloc int[Workload.DirtyEntitiesPerTick];
        Workload.CopyDirtyEntityIndices(tick, dirty);
        for (int index = 0; index < dirty.Length; index++)
            Changes[index] = Present(dirty[index]);
        P03WholeFlowHarness.Require(Source.TryCommit(
            Changes.AsSpan(0, Workload.DirtyEntitiesPerTick), out _),
            "Chunk publication mutation failed.");
    }

    protected ClientWorldSemanticCache CreateCache(int maximumResidentChunks)
        => new(BoundWorld, new ClientWorldSemanticCacheLimits(
            Math.Max(1, maximumResidentChunks), 0, 0,
            UltraSimGameProtocol.MaximumChunkPublicationRecords, Workload.Entities));

    protected void StageEmpty(ClientWorldSemanticCache cache, in SpatialChunkKey key)
    {
        if (cache.TryGetResidentHandle(key, out _)) return;
        P03WholeFlowHarness.Require(cache.TryRequest(
            key, out ClientSemanticChunkHandle handle, out _), "Client cache request failed.");
        P03WholeFlowHarness.Require(cache.TryStageFromBaseWorld(
            handle, [], default, out _), "Client cache empty base staging failed.");
        P03WholeFlowHarness.Require(cache.TryPublish(handle, out _),
            "Client cache empty publication failed.");
    }

    protected void ApplyTopologyChanges(
        ClientWorldSemanticCache cache,
        ReadOnlySpan<OfflineDirectChunkSubscriptionChange> changes)
    {
        for (int index = 0; index < changes.Length; index++)
        {
            ref readonly OfflineDirectChunkSubscriptionChange change = ref changes[index];
            if (change.Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
            {
                if (cache.TryGetResidentHandle(change.Key, out ClientSemanticChunkHandle handle))
                {
                    P03WholeFlowHarness.Require(cache.BeginRetire(handle) &&
                        cache.CompleteRetire(handle), "Client cache retirement failed.");
                }
            }
            else if (change.Kind == OfflineDirectChunkSubscriptionChangeKind.Entered)
            {
                StageEmpty(cache, change.Key);
            }
        }
    }

    protected void ApplyBootstrapSnapshot(
        ClientWorldSemanticCache cache,
        in ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> records)
    {
        P03WholeFlowHarness.Require(cache.TryGetResidentHandle(
            batch.Key, out ClientSemanticChunkHandle handle), "Bootstrap chunk was not resident.");
        P03WholeFlowHarness.Require(cache.TryApplySnapshot(
            handle, batch, records, out _), "Bootstrap snapshot application failed.");
    }

    protected void StageSnapshot(
        ClientWorldSemanticCache cache,
        in SpatialChunkKey key,
        in ChunkPublicationCursor cursor,
        ReadOnlySpan<WorldPublicRecord> records)
    {
        if (cache.TryGetResidentHandle(key, out _)) return;
        P03WholeFlowHarness.Require(cache.TryRequest(
            key, out ClientSemanticChunkHandle handle, out _), "Client cache request failed.");
        P03WholeFlowHarness.Require(cache.TryStageFromBaseWorld(
            handle, records, cursor, out _), "Client cache base staging failed.");
        P03WholeFlowHarness.Require(cache.TryPublish(handle, out _),
            "Client cache publication failed.");
    }

    protected int CountRelevantPopulated(long x, long z)
    {
        int capacity = Workload.Id.StartsWith("sparse", StringComparison.Ordinal) ? 128 : 1_024;
        var entries = new ChunkInterestEntry[capacity];
        ChunkInterestQueryStatus status = ChunkInterestQuery.TryQuery(
            GameWorldState.DefaultSpace, P03WholeFlowHarness.Position(x, z),
            Workload.ChunkInterest, [], entries, out int count, out _);
        P03WholeFlowHarness.Require(status == ChunkInterestQueryStatus.Success,
            "Chunk interest count query failed.");
        int populated = 0;
        for (int index = 0; index < count; index++)
            if (Array.BinarySearch(PopulatedKeys, entries[index].Key) >= 0) populated++;
        return populated;
    }

    protected int CountSubscriptions(long x, long z)
    {
        int capacity = Workload.Id.StartsWith("sparse", StringComparison.Ordinal) ? 128 : 1_024;
        var entries = new ChunkInterestEntry[capacity];
        ChunkInterestQueryStatus status = ChunkInterestQuery.TryQuery(
            GameWorldState.DefaultSpace, P03WholeFlowHarness.Position(x, z),
            Workload.ChunkInterest, [], entries, out int count, out _);
        P03WholeFlowHarness.Require(status == ChunkInterestQueryStatus.Success,
            "Chunk subscription count query failed.");
        return count;
    }

    protected string? DescribeCacheMismatch(
        ClientWorldSemanticCache cache,
        out SpatialChunkKey mismatchKey)
    {
        mismatchKey = default;
        var infos = new ClientSemanticChunkInfo[cache.Limits.MaximumResidentChunks];
        if (!cache.TryCopyResidentInfo(infos, out int count, out _))
            return "resident info destination was too small";
        for (int index = 0; index < count; index++)
        {
            ClientSemanticChunkInfo info = infos[index];
            if (!cache.TryGetResidentHandle(info.Key, out ClientSemanticChunkHandle handle) ||
                !cache.TryGetResidentView(handle, out ClientSemanticChunkView view))
            {
                mismatchKey = info.Key;
                return $"resident handle/view missing for {info.Key}";
            }
            ChunkPublicationReadStatus status = Source.TryCopySnapshot(
                info.Key, _snapshotScratch, out ChunkPublicationBatch batch);
            if (status == ChunkPublicationReadStatus.ChunkAbsent)
            {
                if (!view.PublicRecords.IsEmpty)
                {
                    mismatchKey = info.Key;
                    return $"source absent but client has {view.PublicRecords.Length} rows at {info.Key}";
                }
                continue;
            }
            if (status != ChunkPublicationReadStatus.Snapshot ||
                batch.Count != view.PublicRecords.Length ||
                !_snapshotScratch.AsSpan(0, batch.Count).SequenceEqual(view.PublicRecords))
            {
                mismatchKey = info.Key;
                return $"source/client mismatch at {info.Key}: status={status}, " +
                    $"source={batch.Count}, client={view.PublicRecords.Length}, " +
                    $"sourceRevision={(batch.Cursor.IsValid ? batch.Cursor.Revision.Value : 0)}, " +
                    $"clientRevision={(info.PublicationCursor.IsValid ? info.PublicationCursor.Revision.Value : 0)}";
            }
        }
        return null;
    }

    protected int CountSnapshotSubscriptions(OfflineDirectChunkRecipient recipient)
    {
        int count = 0;
        OfflineDirectChunkSubscriptionView subscriptions = recipient.Subscriptions;
        for (int index = 0; index < subscriptions.Length; index++)
            if (Source.TryCopySnapshot(
                    subscriptions[index].Interest.Key, _snapshotScratch, out _) ==
                ChunkPublicationReadStatus.Snapshot)
                count++;
        return count;
    }

    protected bool NetworkQueuesEmpty()
    {
        for (int index = 0; index < Sessions.Length; index++)
            if (Sessions[index].Server.IndependentUnreliableQueued != 0 ||
                Sessions[index].Client.IndependentUnreliableQueued != 0 ||
                Sessions[index].Server.ReliableQueued != 0 || Sessions[index].Client.ReliableQueued != 0 ||
                Sessions[index].Server.ReceivedQueued != 0 || Sessions[index].Client.ReceivedQueued != 0)
                return false;
        return true;
    }

    internal override string SemanticDigest()
    {
        ulong hash = 14_695_981_039_346_656_003UL;
        for (int index = 0; index < Caches.Length; index++)
        {
            ClientWorldSemanticCacheSnapshot snapshot = Caches[index].GetSnapshot();
            P03MaintainedRemoteAdapter.Mix(ref hash, checked((ulong)snapshot.KnownPublicIdentities));
            P03MaintainedRemoteAdapter.Mix(ref hash, snapshot.WorldSemanticHash.Low);
            P03MaintainedRemoteAdapter.Mix(ref hash, snapshot.WorldSemanticHash.High);
        }
        return hash.ToString("X16");
    }

    protected WorldPublicChange Present(int index) => WorldPublicChange.Present(
        Entities[index], P03WholeFlowHarness.Position(X[index], Z[index]),
        new WorldPublicDescriptor(GameWorldPublicPrefabIds.Character,
            new WorldYaw(unchecked((ushort)Versions[index])), WorldPublicStateFlags.Visible));

    public override void Dispose() => World.Dispose();

    private static BoundWorldPack CreateWorld(
        ReadOnlySpan<SpatialChunkKey> keys, WorldContentFingerprint content)
    {
        var builder = new WorldPackBuilder(
            GameWorldState.DefaultSpace, content,
            new WorldPackLimits(128 * 1024 * 1024, keys.Length, 8, 0, 0, 0));
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        Array.Fill(materials, (ushort)1);
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        P03WholeFlowHarness.Require(TerrainHeightPlaneBase.TryCreate(
            0, out TerrainHeightPlaneBase heightBase), "Terrain height base failed.");
        for (int index = 0; index < keys.Length; index++)
            P03WholeFlowHarness.Require(builder.TryAddBaseTerrain(
                keys[index], materials, heightBase, offsets, out _),
                "Whole-flow terrain fixture exceeded its bound.");
        P03WholeFlowHarness.Require(builder.TryBuild(out WorldPackArtifact? artifact, out _),
            "Whole-flow WorldPack build failed.");
        WorldPack pack = artifact!.Open();
        P03WholeFlowHarness.Require(pack.TryBind(
            new WorldContentBinding(content, [1]), out BoundWorldPack? bound, out _),
            "Whole-flow WorldPack binding failed.");
        return bound!;
    }

    private static SpatialChunkKey[] CollectTerrainKeys(
        P03NetworkWorkload workload,
        ReadOnlySpan<long> x,
        ReadOnlySpan<long> z)
    {
        if (workload.EntityLayout is P03EntityLayout.TopologyChunkGroups or
            P03EntityLayout.TopologyCompactGroups)
        {
            var topologyKeys = new HashSet<SpatialChunkKey>();
            var entries = new ChunkInterestEntry[1_024];
            int compactColumns = workload.EntityLayout == P03EntityLayout.TopologyCompactGroups
                ? checked((int)Math.Ceiling(Math.Sqrt(workload.TopologyGroups)))
                : 0;
            for (int index = 0; index < workload.Recipients; index++)
            {
                long recipientX = x[index];
                long recipientZ = z[index];
                if (workload.EntityLayout == P03EntityLayout.TopologyCompactGroups)
                {
                    int group = checked((int)((long)index * workload.TopologyGroups /
                        workload.Recipients));
                    recipientX = checked((long)(group % compactColumns) * 192_000L + 8_000L);
                    recipientZ = checked((long)(group / compactColumns) * 192_000L + 8_000L);
                }
                ChunkInterestQueryStatus status = ChunkInterestQuery.TryQuery(
                    GameWorldState.DefaultSpace,
                    P03WholeFlowHarness.Position(recipientX, recipientZ),
                    workload.ChunkInterest,
                    [], entries,
                    out int count,
                    out _);
                P03WholeFlowHarness.Require(status == ChunkInterestQueryStatus.Success,
                    "Topology terrain interest query exceeded its bounded scratch.");
                for (int entry = 0; entry < count; entry++)
                    topologyKeys.Add(entries[entry].Key);
            }
            for (int index = 0; index < x.Length; index++)
                topologyKeys.Add(P03WholeFlowHarness.Position(x[index], z[index])
                    .ToChunkKey(GameWorldState.DefaultSpace));
            int inactive = workload.InactiveStoredChunks;
            int offset = 0;
            while (inactive > 0)
            {
                var candidate = new SpatialChunkKey(
                    GameWorldState.DefaultSpace,
                    250_000 + offset,
                    250_000 + offset / 1_024);
                offset++;
                if (topologyKeys.Add(candidate)) inactive--;
            }
            SpatialChunkKey[] result = topologyKeys.ToArray();
            Array.Sort(result);
            return result;
        }
        const long movementAndBoundaryMargin = GameWorldSpatialProfile.WorldChunkEdgeUnits * 4L;
        long margin = checked(workload.ChunkInterest.FarLeaveRadiusMillimeters +
            movementAndBoundaryMargin);
        long minimumX = checked(x.ToArray().Min() - margin);
        long maximumX = checked(x.ToArray().Max() + margin);
        long minimumZ = checked(z.ToArray().Min() - margin);
        long maximumZ = checked(z.ToArray().Max() + margin);
        WorldPosition minimum = P03WholeFlowHarness.Position(minimumX, minimumZ);
        WorldPosition maximum = P03WholeFlowHarness.Position(maximumX, maximumZ);
        int width = checked(maximum.Chunk.X - minimum.Chunk.X + 1);
        int depth = checked(maximum.Chunk.Z - minimum.Chunk.Z + 1);
        var keys = new SpatialChunkKey[checked(width * depth)];
        int written = 0;
        for (int chunkZ = minimum.Chunk.Z; chunkZ <= maximum.Chunk.Z; chunkZ++)
            for (int chunkX = minimum.Chunk.X; chunkX <= maximum.Chunk.X; chunkX++)
                keys[written++] = new SpatialChunkKey(
                    GameWorldState.DefaultSpace, chunkX, chunkZ);
        return keys;
    }
}

internal sealed class P03ChunkPublicationRemoteAdapter : P03ChunkRemoteAdapterBase
{
    private readonly OfflineDirectChunkRecipient[] _recipients;
    private readonly ChunkReplicationRecipientId[] _recipientIds;
    private readonly ChunkPublicationWorkScheduler _scheduler;
    private readonly RemoteChunkWorldStateServer[] _servers;
    private readonly RemoteChunkWorldStateClient[] _clients;
    private readonly ulong[] _applicationEpochs;
    private readonly OfflineDirectChunkSubscriptionChange[] _changes;
    private readonly ChunkPublicationWork[] _dueWorks;
    private readonly RemoteChunkWorldStateScheduleStatus[] _dueStatuses;
    private readonly int[] _dueCounts;
    private readonly P03DeltaRangeSignature[] _rangeSignatures;
    private readonly int[] _rangeRecordCounts;
    private readonly WorldPublicDeltaRecord[] _rangeCensusScratch =
        new WorldPublicDeltaRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];
    private readonly bool _historyAuditEnabled;
    private readonly bool _legacyRangeCensusEnabled;
    private readonly IRemoteChunkCompressionInputObserver? _compressionInputObserver;
    private ulong _dueCandidates;
    private ulong _simultaneouslyDueCandidates;
    private ulong _multiDueRecipientPasses;
    private ulong _bundleMessages;
    private ulong _bundleEntries;
    private ulong _gameMessagesQueued;
    private ulong _applicationBytesQueued;
    private ulong _capTwoPairOpportunities;
    private ulong _snapshotOrRecoveryDeliveries;
    private int _maximumDuePerRecipientPass;
    private ChunkPublicationStoreSnapshot _sourceBaseline;
    private PreparedChunkPublicationSnapshot _preparedBaseline;
    private GameTelemetryLevel _telemetryLevel;
    private bool _serverOwnedRemoteTimingEnabled;
    private ulong _lastRecipientCommitObservationTicks;
    private ulong _deltaRangeRequests;
    private ulong _uniqueDeltaRanges;
    private ulong _reusableDeltaRangeRequests;
    private ulong _deltaRangeRecordInstances;
    private ulong _uniqueDeltaRangeRecords;
    private ulong _reusableDeltaRangeRecords;
    private ulong _censusSourceDeltaReads;
    private readonly RemoteChunkDeltaBodyStore _bodyStore;
    private const int MaximumDuePerRecipient = 64;

    internal P03ChunkPublicationRemoteAdapter(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions,
        WorldContentFingerprint content,
        RemoteChunkBundleCompressionPolicy compression = default,
        RemoteChunkSingletonDeltaCompression singletonDeltaCompression = default,
        RemoteChunkDeltaBodySharingMode bodySharing = RemoteChunkDeltaBodySharingMode.Enabled,
        bool historyAudit = false,
        bool legacyRangeCensus = true,
        PreparedChunkPublicationReadModel readModel =
            PreparedChunkPublicationReadModel.PreparedLatestState,
        IRemoteChunkCompressionInputObserver? compressionInputObserver = null,
        bool uniqueApplicationEpochs = false)
        : base(workload, sessions, x, z, versions, content, readModel)
    {
        _historyAuditEnabled = historyAudit;
        _legacyRangeCensusEnabled = legacyRangeCensus;
        _compressionInputObserver = compressionInputObserver;
        _bodyStore = new RemoteChunkDeltaBodyStore(Source, bodySharing);
        int subscriptions = workload.Id.StartsWith("sparse", StringComparison.Ordinal) ? 128 : 1_024;
        _recipients = new OfflineDirectChunkRecipient[workload.Recipients];
        _recipientIds = new ChunkReplicationRecipientId[workload.Recipients];
        _scheduler = new ChunkPublicationWorkScheduler(Source,
            new ChunkPublicationWorkSchedulerLimits(workload.Recipients,
                checked(workload.Recipients * subscriptions)));
        _servers = new RemoteChunkWorldStateServer[workload.Recipients];
        _clients = new RemoteChunkWorldStateClient[workload.Recipients];
        _applicationEpochs = new ulong[workload.Recipients];
        _changes = new OfflineDirectChunkSubscriptionChange[subscriptions * 2];
        _dueWorks = new ChunkPublicationWork[checked(workload.Recipients * MaximumDuePerRecipient)];
        _dueStatuses = new RemoteChunkWorldStateScheduleStatus[_dueWorks.Length];
        _dueCounts = new int[workload.Recipients];
        _rangeSignatures = new P03DeltaRangeSignature[_dueWorks.Length];
        _rangeRecordCounts = new int[_dueWorks.Length];
        for (int index = 0; index < workload.Recipients; index++)
        {
            var recipient = new OfflineDirectChunkRecipient(
                new OfflineDirectChunkRecipientLimits(subscriptions, 64, 64),
                workload.ChunkInterest, workload.ChunkCadence);
            _recipients[index] = recipient;
            P03WholeFlowHarness.Require(_scheduler.TryRegister(recipient, out _recipientIds[index]),
                "Chunk scheduler registration failed.");
            P03WholeFlowHarness.Require(recipient.Reconcile(
                Source, GameWorldState.DefaultSpace,
                P03WholeFlowHarness.Position(RecipientX(index), RecipientZ(index)), _changes,
                out OfflineDirectChunkReconcileResult result) ==
                OfflineDirectChunkReconcileStatus.Reconciled,
                "Chunk recipient setup reconciliation failed.");
            P03WholeFlowHarness.Require(_scheduler.TryApplyReconciliation(
                _recipientIds[index], _changes.AsSpan(0, result.ChangeCount), result),
                "Chunk scheduler setup reconciliation failed.");
            Caches[index] = CreateCache(Math.Min(
                subscriptions, checked(CountSubscriptions(RecipientX(index), RecipientZ(index)) + 64)));
            OfflineDirectChunkSubscriptionView initial = recipient.Subscriptions;
            for (int subscription = 0; subscription < initial.Length; subscription++)
                StageEmpty(Caches[index], initial[subscription].Interest.Key);
            Bootstrap(index);
            ExpectedCounts[index] = Caches[index].GetSnapshot().KnownPublicIdentities;
            ulong applicationEpoch = uniqueApplicationEpochs
                ? checked(ApplicationEpoch + (ulong)index)
                : ApplicationEpoch;
            _applicationEpochs[index] = applicationEpoch;
            _servers[index] = new RemoteChunkWorldStateServer(
                sessions[index].Server, recipient, Source, applicationEpoch,
                new RemoteChunkWorldStateTransportLimits(
                    128, 1_024, compression, singletonDeltaCompression, bodySharing),
                _bodyStore);
            _servers[index].SetCompressionInputObserverForDiagnostics(compressionInputObserver);
            _clients[index] = new RemoteChunkWorldStateClient(
                sessions[index].Client, Caches[index], applicationEpoch, 64, 256);
        }
        while (_scheduler.TryDequeueDue(0, out _)) { }
        if (_historyAuditEnabled)
            Source.ConfigureHistoryAudit(new ChunkPublicationHistoryAuditLimits(65_536));
    }

    internal override P03ApplicationEpochSummary ApplicationEpochs
    {
        get
        {
            if (_applicationEpochs.Length == 0) return new(true, 0, 0, 0, 0);
            ulong minimum = ulong.MaxValue;
            ulong maximum = 0;
            int distinct = 0;
            for (int index = 0; index < _applicationEpochs.Length; index++)
            {
                ulong candidate = _applicationEpochs[index];
                minimum = Math.Min(minimum, candidate);
                maximum = Math.Max(maximum, candidate);
                bool seen = false;
                for (int prior = 0; prior < index; prior++)
                    if (_applicationEpochs[prior] == candidate)
                    {
                        seen = true;
                        break;
                    }
                if (!seen) distinct++;
            }
            return new(true, _applicationEpochs.Length, distinct, minimum, maximum);
        }
    }

    internal override void ConfigureTelemetry(GameTelemetryLevel level)
    {
        _telemetryLevel = level;
        Source.ConfigurePreparedPublicationAttribution(
            sourceEnabled: level == GameTelemetryLevel.Detailed,
            recipientEnabled: level == GameTelemetryLevel.Detailed);
        _scheduler.ConfigureTelemetry(level);
        for (int index = 0; index < _servers.Length; index++)
            _servers[index].ConfigureTelemetry(level);
        _bodyStore.ConfigureDetailedTiming(level == GameTelemetryLevel.Detailed);
    }

    internal override void ConfigureServerOwnedRemoteTiming(bool enabled)
    {
        _serverOwnedRemoteTimingEnabled = enabled;
        Source.ConfigurePreparedPublicationAttribution(
            sourceEnabled: enabled || _telemetryLevel == GameTelemetryLevel.Detailed,
            recipientEnabled: _telemetryLevel == GameTelemetryLevel.Detailed);
    }

    private void Bootstrap(int recipientIndex)
    {
        OfflineDirectChunkRecipient recipient = _recipients[recipientIndex];
        for (int index = 0; index < recipient.Subscriptions.Length; index++)
        {
            OfflineDirectChunkReadStatus status = recipient.TryBeginRead(
                Source, index, 0, out OfflineDirectChunkRead read);
            P03WholeFlowHarness.Require(status is OfflineDirectChunkReadStatus.Snapshot or
                OfflineDirectChunkReadStatus.ChunkAbsent, "Chunk bootstrap read was not current truth.");
            if (status == OfflineDirectChunkReadStatus.Snapshot)
                ApplyBootstrapSnapshot(Caches[recipientIndex], read.Batch, read.Snapshot);
            recipient.CommitRead();
        }
    }

    internal override void ApplyAuthority(int tick, int movedRecipients)
    {
        for (int index = 0; index < movedRecipients; index++)
        {
            OfflineDirectChunkReconcileStatus status = _servers[index].Reconcile(
                GameWorldState.DefaultSpace,
                P03WholeFlowHarness.Position(RecipientX(index), RecipientZ(index)),
                _changes, out OfflineDirectChunkReconcileResult result);
            P03WholeFlowHarness.Require(status == OfflineDirectChunkReconcileStatus.Reconciled,
                "Chunk recipient movement reconciliation failed.");
            ApplyTopologyChanges(Caches[index], _changes.AsSpan(0, result.ChangeCount));
            P03WholeFlowHarness.Require(_scheduler.TryApplyReconciliation(
                _recipientIds[index], _changes.AsSpan(0, result.ChangeCount), result),
                "Chunk scheduler movement reconciliation failed.");
        }
        CommitMutation(tick);
        long wakeupStarted = _serverOwnedRemoteTimingEnabled ? Stopwatch.GetTimestamp() : 0;
        ChunkPublicationCommitObservationStatus observation = _scheduler.ObserveCommit(Source);
        _lastRecipientCommitObservationTicks = _serverOwnedRemoteTimingEnabled
            ? checked((ulong)(Stopwatch.GetTimestamp() - wakeupStarted))
            : 0;
        P03WholeFlowHarness.Require(observation ==
            ChunkPublicationCommitObservationStatus.Observed,
            "Chunk scheduler missed an authoritative commit.");
    }

    internal override ulong PreparedSourcePublicationCount =>
        Source.GetPreparedPublicationSnapshot().PreparedSourcePublications;

    internal override ulong LastPreparedSourcePreparationTicks =>
        Source.GetLastPreparedSourcePreparationTicks();

    internal override ulong LastRecipientCommitObservationTicks =>
        _lastRecipientCommitObservationTicks;

    internal override void Schedule(ulong step)
    {
        Array.Clear(_dueCounts);
        int guard = checked(Workload.Recipients * MaximumDuePerRecipient);
        while (guard-- > 0 && _scheduler.TryDequeueDue(step, out ChunkPublicationWork work))
        {
            int recipient = work.Recipient.Index;
            int count = _dueCounts[recipient];
            if (count == MaximumDuePerRecipient)
            {
                P03WholeFlowHarness.Require(_scheduler.Requeue(work),
                    "Chunk scheduler could not retain a bounded bundle overflow.");
                continue;
            }
            _dueWorks[recipient * MaximumDuePerRecipient + count] = work;
            _dueCounts[recipient] = count + 1;
        }
        if (_telemetryLevel == GameTelemetryLevel.Detailed && _legacyRangeCensusEnabled)
            CaptureDeltaRangeReuseCensus();
        for (int recipient = 0; recipient < _servers.Length; recipient++)
        {
            int count = _dueCounts[recipient];
            _dueCandidates += checked((ulong)count);
            _maximumDuePerRecipientPass = Math.Max(_maximumDuePerRecipientPass, count);
            if (count >= 2)
            {
                _multiDueRecipientPasses++;
                _simultaneouslyDueCandidates += checked((ulong)count);
            }
            int baseIndex = recipient * MaximumDuePerRecipient;
            int offset = 0;
            bool blocked = false;
            int pendingFrameBytes = 0;
            while (offset < count && !blocked)
            {
                int consumed = _servers[recipient].TryScheduleDueBundle(
                    _dueWorks.AsSpan(baseIndex + offset, count - offset), step,
                    _dueStatuses.AsSpan(baseIndex + offset, count - offset),
                    out int messagesQueued, out int applicationBytesQueued,
                    out int bundlesQueued, out int bundleEntriesQueued);
                P03WholeFlowHarness.Require(consumed > 0,
                    "Chunk selected-profile bundle pass made no progress.");
                _gameMessagesQueued += checked((ulong)messagesQueued);
                _applicationBytesQueued += checked((ulong)applicationBytesQueued);
                _bundleMessages += checked((ulong)bundlesQueued);
                if (messagesQueued != 0)
                {
                    int frameBytes = checked(applicationBytesQueued +
                        NetworkProtocolDataHeader.EncodedBytes);
                    if (pendingFrameBytes != 0 &&
                        pendingFrameBytes + frameBytes <=
                            P03WholeFlowHarness.MaximumPlaintextDatagramBytes)
                    {
                        _capTwoPairOpportunities++;
                        pendingFrameBytes = 0;
                    }
                    else
                    {
                        pendingFrameBytes = frameBytes;
                    }
                }
                ScheduleCalls += checked((ulong)consumed);
                int successful = 0;
                for (int index = 0; index < consumed; index++)
                {
                    int position = baseIndex + offset + index;
                    RemoteChunkWorldStateScheduleStatus status = _dueStatuses[position];
                    if (status is RemoteChunkWorldStateScheduleStatus.QueuedDelta or
                        RemoteChunkWorldStateScheduleStatus.QueuedAbsence or
                        RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted)
                    {
                        ScheduleSuccesses++;
                        successful++;
                        if (status == RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted)
                            _snapshotOrRecoveryDeliveries++;
                    }
                    else if (status is RemoteChunkWorldStateScheduleStatus.TransportNotAccepting or
                             RemoteChunkWorldStateScheduleStatus.CapacityExceeded or
                             RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy)
                    {
                        P03WholeFlowHarness.Require(_scheduler.Requeue(_dueWorks[position]),
                            "Chunk scheduler could not retain backpressured work.");
                        blocked = true;
                    }
                    else if (status is not (RemoteChunkWorldStateScheduleStatus.NoPayload or
                                 RemoteChunkWorldStateScheduleStatus.NotDue))
                        throw new InvalidOperationException(
                            $"Chunk remote schedule returned {status}.");
                }
                _bundleEntries += checked((ulong)bundleEntriesQueued);
                offset += consumed;
            }
            for (int index = offset; index < count; index++)
                P03WholeFlowHarness.Require(
                    _scheduler.Requeue(_dueWorks[baseIndex + index]),
                    "Chunk scheduler could not retain deferred grouped work.");
        }
        Array.Clear(_dueWorks);
        Array.Clear(_dueStatuses);
    }

    internal override void ProgressServers()
    {
        for (int index = 0; index < _servers.Length; index++) _ = _servers[index].TryProgress();
    }
    internal override void ProgressClient(int recipient) => _ = _clients[recipient].TryProgress();
    internal override bool ProcessServer(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _servers[recipient].TryProcess(in message, payload);
        if (handled) ServerProofMessages++;
        return handled;
    }
    internal override bool ProcessClient(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _clients[recipient].TryProcess(in message, payload);
        if (handled) ClientMessages++;
        return handled;
    }
    internal override bool IsConverged
    {
        get
        {
            if (_scheduler.GetSnapshot().PendingWorkCount != 0 || !NetworkQueuesEmpty()) return false;
            for (int index = 0; index < _servers.Length; index++)
                if (_servers[index].GetSnapshot().OutstandingDeliveries != 0 ||
                    _clients[index].GetSnapshot().PendingProofs != 0)
                    return false;
            return true;
        }
    }
    internal override void Validate()
    {
        P03WholeFlowHarness.Require(IsConverged, "ChunkPublication failed final convergence.");
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteChunkWorldStateServerSnapshot server = _servers[index].GetSnapshot();
            RemoteChunkWorldStateClientSnapshot client = _clients[index].GetSnapshot();
            string? mismatch = DescribeCacheMismatch(Caches[index], out SpatialChunkKey mismatchKey);
            ulong serverRevision = 0;
            OfflineDirectChunkSubscriptionView subscriptions = _recipients[index].Subscriptions;
            for (int subscription = 0; subscription < subscriptions.Length; subscription++)
                if (subscriptions[subscription].Interest.Key == mismatchKey)
                {
                    serverRevision = subscriptions[subscription].Cursor.IsValid
                        ? subscriptions[subscription].Cursor.Revision.Value : 0;
                    break;
                }
            P03WholeFlowHarness.Require(mismatch is null,
                $"ChunkPublication client {index} did not match current source truth: {mismatch}; " +
                $"serverAppliedRevision={serverRevision}, server={server}, client={client}.");
            P03WholeFlowHarness.Require(server.RejectedProofs == 0 &&
                server.MalformedProofs <= server.InvalidatedDeliveries &&
                client.RejectedDeliveries == 0 && client.MalformedDeliveries == 0 &&
                client.ContradictoryDeliveries == 0,
                $"ChunkPublication remote path rejected semantic state at recipient {index}: " +
                $"server={server} client={client}.");
        }
    }
    internal override P03ProfileCounters GetCounters()
    {
        _compressionInputObserver?.FreezeMeasurement();
        ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
        ChunkPublicationStoreSnapshot source = Source.GetSnapshot();
        P03CompressionCounters compression = default;
        P03ProofHealth proofHealth = default;
        P03ReplicationLifecycleCounters lifecycle = default;
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteChunkWorldStateServerSnapshot server = _servers[index].GetSnapshot();
            RemoteChunkReplicationTimingSnapshot timing = server.Timing;
            RemoteChunkKnowledgeCensusSnapshot knowledge = timing.Knowledge;
            compression = compression.Add(knowledge.Compression);
            proofHealth = proofHealth.Add(knowledge);
            lifecycle = lifecycle.Add(
                timing.Work, knowledge, server.OutstandingDeliveries);
        }
        return base.GetCounters() with
        {
            SuccessfulSemanticPublications = ScheduleSuccesses,
            GameMessagesQueued = _gameMessagesQueued,
            ApplicationBytesQueued = _applicationBytesQueued,
            InterestSubscriptions = checked((ulong)scheduler.MembershipCount),
            DueCandidatePublications = _dueCandidates,
            SimultaneouslyDuePublications = _simultaneouslyDueCandidates,
            MultiDueRecipientPasses = _multiDueRecipientPasses,
            ChunkBundleMessages = _bundleMessages,
            ChunkBundleEntries = _bundleEntries,
            CapTwoPairOpportunities = _capTwoPairOpportunities,
            MaximumDuePerRecipientPass = _maximumDuePerRecipientPass,
            SnapshotOrRecoveryDeliveries = _snapshotOrRecoveryDeliveries,
            ProofEntries = ScheduleSuccesses,
            DirtyChunksObserved = scheduler.DirtyChunksObserved,
            DirtyRecipientFanout = scheduler.DirtyRecipientFanout,
            CommitObservations = scheduler.CommitsObserved,
            EmptyCommitObservations = scheduler.EmptyCommitsObserved,
            DirtyCommitObservations = scheduler.DirtyCommitsObserved,
            DirtyWorkCoalesces = scheduler.DirtyWorkCoalesces,
            SchedulerRetryQueues = scheduler.RetryQueues,
            DueWorkDequeued = scheduler.DueWorkDequeued,
            PendingMembershipsExamined = scheduler.PendingMembershipsExamined,
            SchedulerCadenceDeferrals = scheduler.CadenceDeferrals,
            SourceDeltaReads = Difference(
                Difference(source.DeltaReads, _sourceBaseline.DeltaReads),
                _censusSourceDeltaReads),
            SourceSnapshotReads = Difference(source.SnapshotReads, _sourceBaseline.SnapshotReads),
            SourceSnapshotFallbacks = Difference(
                source.SnapshotFallbacks, _sourceBaseline.SnapshotFallbacks),
            ChunkCpu = CaptureCpuAttribution(scheduler.Cpu),
            DeltaRangeReuse = new P03DeltaRangeReuseCensus(
                _deltaRangeRequests, _uniqueDeltaRanges, _reusableDeltaRangeRequests,
                _deltaRangeRecordInstances, _uniqueDeltaRangeRecords,
                _reusableDeltaRangeRecords, _censusSourceDeltaReads),
            Compression = compression,
            ProofHealth = proofHealth,
            Lifecycle = lifecycle,
            HistoryAudit = _historyAuditEnabled ? Source.GetHistoryAuditSnapshot() : null,
            PreparedPublication = DifferencePreparedPublication(
                Source.GetPreparedPublicationSnapshot(), _preparedBaseline),
            DeltaBodies = _bodyStore.GetSnapshot(),
            ResidentSourceChunks = source.ResidentChunkCount,
            CurrentSharedSubscriptionChunks = scheduler.SharedChunkCount,
            CurrentMemberships = scheduler.MembershipCount,
            Topology = scheduler.Topology,
        };
    }
    internal override void ResetCounters()
    {
        base.ResetCounters();
        _dueCandidates = _simultaneouslyDueCandidates = _multiDueRecipientPasses = 0;
        _bundleMessages = _bundleEntries = _gameMessagesQueued = _applicationBytesQueued = 0;
        _capTwoPairOpportunities = _snapshotOrRecoveryDeliveries = 0;
        _maximumDuePerRecipientPass = 0;
        _deltaRangeRequests = _uniqueDeltaRanges = _reusableDeltaRangeRequests = 0;
        _deltaRangeRecordInstances = _uniqueDeltaRangeRecords =
            _reusableDeltaRangeRecords = _censusSourceDeltaReads = 0;
        _scheduler.ConfigureTelemetry(_telemetryLevel);
        for (int index = 0; index < _servers.Length; index++)
            _servers[index].ConfigureTelemetry(_telemetryLevel);
        _sourceBaseline = Source.GetSnapshot();
        _preparedBaseline = Source.GetPreparedPublicationSnapshot();
        _bodyStore.ResetStatistics();
        if (_compressionInputObserver is not null)
            _compressionInputObserver.Reset();
        if (_historyAuditEnabled)
            Source.ConfigureHistoryAudit(new ChunkPublicationHistoryAuditLimits(65_536));
    }

    private static PreparedChunkPublicationSnapshot DifferencePreparedPublication(
        PreparedChunkPublicationSnapshot after,
        PreparedChunkPublicationSnapshot before) => new(
            after.Selected,
            after.PublicationCapacity,
            after.RecordCapacity,
            after.RetainedPublications,
            after.RetainedRecords,
            after.RetainedTombstones,
            after.PublicationHighWater,
            after.RecordHighWater,
            after.TombstoneHighWater,
            Difference(after.AuthoritativeSourceMutations, before.AuthoritativeSourceMutations),
            Difference(after.DirtySourceChunks, before.DirtySourceChunks),
            Difference(after.PreparedSourcePublications, before.PreparedSourcePublications),
            Difference(after.PreparedSourceRecords, before.PreparedSourceRecords),
            Difference(after.RecipientRangeReads, before.RecipientRangeReads),
            Difference(after.ReferencedSourcePublications, before.ReferencedSourcePublications),
            Difference(after.ReferencedSourceRecords, before.ReferencedSourceRecords),
            Difference(after.HistoryDerivedSteadyStateCalls,
                before.HistoryDerivedSteadyStateCalls),
            Difference(after.RetentionEvictions, before.RetentionEvictions),
            Difference(after.PressureEvictions, before.PressureEvictions),
            Difference(after.RebaseRequirements, before.RebaseRequirements),
            Difference(after.SourcePreparationTicks, before.SourcePreparationTicks),
            Difference(after.RecipientRangePreparationTicks,
                before.RecipientRangePreparationTicks),
            Difference(after.RecipientChainTraversalTicks,
                before.RecipientChainTraversalTicks),
            Difference(after.RecipientCollapseTicks, before.RecipientCollapseTicks),
            Difference(after.RecipientMaterializationTicks,
                before.RecipientMaterializationTicks),
            Difference(after.RecipientSortTicks, before.RecipientSortTicks),
            Difference(after.RecipientSortCalls, before.RecipientSortCalls),
            Difference(after.RecipientIndicesSorted, before.RecipientIndicesSorted),
            Difference(after.RecipientTrivialSorts, before.RecipientTrivialSorts),
            Difference(after.RecipientRecordsConstructed,
                before.RecipientRecordsConstructed),
            Difference(after.RangeIdentityInspections, before.RangeIdentityInspections),
            Difference(after.RangeIdentityInspectionTicks,
                before.RangeIdentityInspectionTicks),
            Difference(after.CoverageValidationCalls, before.CoverageValidationCalls),
            Difference(after.CoverageValidationLinks, before.CoverageValidationLinks),
            Difference(after.CoverageValidationFailures, before.CoverageValidationFailures),
            Difference(after.CoverageValidationTicks, before.CoverageValidationTicks),
            after.CoveragePublicationHighWater,
            after.ReservedBytes);

    private void CaptureDeltaRangeReuseCensus()
    {
        int unique = 0;
        for (int recipient = 0; recipient < _recipients.Length; recipient++)
        {
            int baseIndex = recipient * MaximumDuePerRecipient;
            for (int workIndex = 0; workIndex < _dueCounts[recipient]; workIndex++)
            {
                ref readonly ChunkPublicationWork work = ref _dueWorks[baseIndex + workIndex];
                OfflineDirectChunkSubscriptionView subscriptions =
                    _recipients[recipient].Subscriptions;
                ChunkPublicationCursor cursor = default;
                for (int subscription = 0; subscription < subscriptions.Length; subscription++)
                    if (subscriptions[subscription].Interest.Key == work.Key)
                    {
                        cursor = subscriptions[subscription].Cursor;
                        break;
                    }
                if (!cursor.IsValid || !Source.TryGetCommittedView(
                        work.Key, out ChunkPublicationView view) ||
                    view.Revision.Value <= cursor.Revision.Value)
                    continue;
                var signature = new P03DeltaRangeSignature(
                    work.Key, cursor.Revision.Value, view.Revision.Value);
                _deltaRangeRequests++;
                int match = -1;
                for (int index = 0; index < unique; index++)
                    if (_rangeSignatures[index] == signature)
                    {
                        match = index;
                        break;
                    }
                if (match >= 0)
                {
                    _reusableDeltaRangeRequests++;
                    _deltaRangeRecordInstances = checked(_deltaRangeRecordInstances +
                        (ulong)_rangeRecordCounts[match]);
                    _reusableDeltaRangeRecords = checked(_reusableDeltaRangeRecords +
                        (ulong)_rangeRecordCounts[match]);
                    continue;
                }
                ChunkPublicationReadStatus status = Source.TryCopyDelta(
                    work.Key, cursor, _rangeCensusScratch, out ChunkPublicationBatch batch);
                _censusSourceDeltaReads++;
                P03WholeFlowHarness.Require(status == ChunkPublicationReadStatus.Delta,
                    "PF4E-R2 detailed range census diverged from due Delta work.");
                _rangeSignatures[unique] = signature;
                _rangeRecordCounts[unique] = batch.Count;
                unique++;
                _uniqueDeltaRanges++;
                _uniqueDeltaRangeRecords = checked(_uniqueDeltaRangeRecords +
                    (ulong)batch.Count);
                _deltaRangeRecordInstances = checked(_deltaRangeRecordInstances +
                    (ulong)batch.Count);
            }
        }
    }
    private static ulong Difference(ulong value, ulong baseline) =>
        value >= baseline ? value - baseline : 0;
    private P03ChunkCpuAttribution CaptureCpuAttribution(
        ChunkPublicationWorkSchedulerCpuAttribution scheduler)
    {
        var value = new P03ChunkCpuAttributionAccumulator
        {
            Enabled = scheduler.Enabled,
            ReadinessSelectionCalls = scheduler.SelectionCalls,
            ReadinessSelectionTicks = scheduler.SelectionTicks,
            SchedulerCadenceCheckCalls = scheduler.CadenceCheckCalls,
            SchedulerCadenceCheckTicks = scheduler.CadenceCheckTicks,
        };
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteChunkReplicationTimingSnapshot timing = _servers[index].GetTimingSnapshot();
            RemoteChunkCpuAttributionSnapshot cpu = timing.Cpu;
            value.ScheduleTotalTicks += timing.EncodingAndScheduling.TotalStopwatchTicks;
            value.ProofTotalTicks += timing.ApplicationProof.TotalStopwatchTicks;
            value.Add(in cpu);
        }
        return value.Capture();
    }
    internal override long GetServerReservedBytes()
    {
        long value = checked(Source.GetSnapshot().ReservedBytes + _scheduler.GetSnapshot().ReservedBytes +
            _bodyStore.GetSnapshot().ReservedBytes);
        for (int index = 0; index < _servers.Length; index++)
            value = checked(value + _recipients[index].GetSnapshot().ReservedBytes +
                _servers[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override long GetClientReservedBytes()
    {
        long value = 0;
        for (int index = 0; index < _clients.Length; index++)
            value = checked(value + _clients[index].GetSnapshot().ReservedBytes +
                Caches[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override string DescribeState() => _scheduler.GetSnapshot().ToString();
}

internal sealed class P03DirtyChunkRemoteAdapter : P03ChunkRemoteAdapterBase
{
    private readonly DirtyChunkSpatialFanout _service;
    private readonly RemoteDirtyChunkSpatialFanoutServer[] _servers;
    private readonly RemoteChunkWorldStateClient[] _clients;
    private readonly OfflineDirectChunkSubscriptionChange[] _changes;
    private ulong _baselineBundles;
    private ulong _baselineBundleEntries;
    private ulong _baselineSharedPageMaterializations;
    private ulong _baselineSharedPageHits;

    internal P03DirtyChunkRemoteAdapter(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions,
        WorldContentFingerprint content)
        : base(workload, sessions, x, z, versions, content)
    {
        int subscriptions = workload.Id.StartsWith("sparse", StringComparison.Ordinal) ? 128 : 1_024;
        _service = new DirtyChunkSpatialFanout(
            Source,
            new DirtyChunkSpatialFanoutLimits(
                workload.Recipients, checked(workload.Recipients * subscriptions),
                Math.Max(16_384, workload.Recipients * 4), 16_384,
                Math.Max(256, workload.Recipients * 16), 64, 64, 128_000,
                MaximumDeliveryAgeSteps: 1_024,
                MaximumChunksPerBundle: UltraSimGameProtocol.MaximumDirtyChunkBundleEntries),
            workload.ChunkInterest, workload.ChunkCadence);
        _servers = new RemoteDirtyChunkSpatialFanoutServer[workload.Recipients];
        _clients = new RemoteChunkWorldStateClient[workload.Recipients];
        _changes = new OfflineDirectChunkSubscriptionChange[subscriptions * 2];
        for (int index = 0; index < workload.Recipients; index++)
        {
            Caches[index] = CreateCache(Math.Min(
                subscriptions, checked(CountSubscriptions(RecipientX(index), RecipientZ(index)) + 64)));
            P03WholeFlowHarness.Require(_service.SetRecipientAnchor(
                P03WholeFlowHarness.Participant(index), GameWorldState.DefaultSpace,
                P03WholeFlowHarness.Position(RecipientX(index), RecipientZ(index)), 0, _changes,
                out OfflineDirectChunkReconcileResult result) ==
                DirtyChunkRecipientUpdateStatus.Reconciled,
                "DirtyChunk recipient setup failed.");
            ApplyTopologyChanges(Caches[index], _changes.AsSpan(0, result.ChangeCount));
        }
        Bootstrap();
        for (int index = 0; index < workload.Recipients; index++)
        {
            ExpectedCounts[index] = Caches[index].GetSnapshot().KnownPublicIdentities;
            _servers[index] = new RemoteDirtyChunkSpatialFanoutServer(
                sessions[index].Server, _service, P03WholeFlowHarness.Participant(index),
                ApplicationEpoch);
            _clients[index] = new RemoteChunkWorldStateClient(
                sessions[index].Client, Caches[index], ApplicationEpoch, 64, 256);
        }
    }

    private void Bootstrap()
    {
        P03WholeFlowHarness.Require(_service.TryProcessDirtyChunks(0, out _) is
            DirtyChunkProcessStatus.Success or DirtyChunkProcessStatus.NoData,
            "DirtyChunk bootstrap discovery failed.");
        int guard = Workload.Recipients * 2_048;
        while (guard-- > 0)
        {
            DirtyChunkBuildStatus status = _service.TryBuildNextBundle(
                0, UltraSimGameProtocol.MaximumMessageBytes, out DirtyChunkDeliveryBundle bundle);
            if (status == DirtyChunkBuildStatus.NoData) return;
            P03WholeFlowHarness.Require(status == DirtyChunkBuildStatus.Success,
                $"DirtyChunk bootstrap build returned {status}.");
            int recipient = checked((int)bundle.Participant.Value - 1);
            for (int index = 0; index < bundle.Entries.Length; index++)
            {
                DirtyChunkDeliveryEntry entry = bundle.Entries[index];
                if (entry.Status == OfflineDirectChunkReadStatus.Snapshot)
                    ApplyBootstrapSnapshot(Caches[recipient], entry.Batch,
                        bundle.Snapshot(in entry));
                P03WholeFlowHarness.Require(_service.TryAcknowledgeDelivery(
                    bundle.Participant, entry.DeliveryId, true) ==
                    DirtyChunkAppliedProofStatus.Applied,
                    "DirtyChunk bootstrap proof failed.");
            }
        }
        throw new InvalidOperationException("DirtyChunk bootstrap did not converge.");
    }

    internal override void ApplyAuthority(int tick, int movedRecipients)
    {
        for (int index = 0; index < movedRecipients; index++)
        {
            P03WholeFlowHarness.Require(_service.SetRecipientAnchor(
                P03WholeFlowHarness.Participant(index), GameWorldState.DefaultSpace,
                P03WholeFlowHarness.Position(RecipientX(index), RecipientZ(index)),
                checked((ulong)tick),
                _changes, out OfflineDirectChunkReconcileResult result) is
                DirtyChunkRecipientUpdateStatus.Reconciled or
                DirtyChunkRecipientUpdateStatus.NoChange,
                "DirtyChunk recipient movement failed.");
            ApplyTopologyChanges(Caches[index], _changes.AsSpan(0, result.ChangeCount));
        }
        CommitMutation(tick);
    }

    internal override void Schedule(ulong step)
    {
        for (int recipient = 0; recipient < _servers.Length; recipient++)
            for (int bundle = 0; bundle < 8; bundle++)
            {
                ScheduleCalls++;
                DirtyChunkBuildStatus status = _servers[recipient].TryScheduleBundle(step);
                if (status != DirtyChunkBuildStatus.Success) break;
                ScheduleSuccesses++;
            }
    }
    internal override void ProgressServers()
    {
        for (int index = 0; index < _servers.Length; index++) _ = _servers[index].TryProgress();
    }
    internal override void ProgressClient(int recipient) => _ = _clients[recipient].TryProgress();
    internal override bool ProcessServer(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _servers[recipient].TryProcess(in message, payload);
        if (handled) ServerProofMessages++;
        return handled;
    }
    internal override bool ProcessClient(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload)
    {
        bool handled = _clients[recipient].TryProcess(in message, payload);
        if (handled) ClientMessages++;
        return handled;
    }
    internal override bool IsConverged
    {
        get
        {
            DirtyChunkSpatialFanoutSnapshot state = _service.GetSnapshot();
            if (state.PendingChunkObligationCount != 0 || state.OutstandingDeliveryCount != 0 ||
                !NetworkQueuesEmpty()) return false;
            for (int index = 0; index < _servers.Length; index++)
                if (_servers[index].GetSnapshot().OutstandingDeliveries != 0 ||
                    _clients[index].GetSnapshot().PendingProofs != 0)
                    return false;
            return true;
        }
    }
    internal override void Validate()
    {
        P03WholeFlowHarness.Require(IsConverged, "DirtyChunk failed final convergence.");
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteDirtyChunkSpatialFanoutServerSnapshot server = _servers[index].GetSnapshot();
            RemoteChunkWorldStateClientSnapshot client = _clients[index].GetSnapshot();
            string? mismatch = DescribeCacheMismatch(Caches[index], out _);
            P03WholeFlowHarness.Require(mismatch is null,
                $"DirtyChunk client {index} did not match current source truth: {mismatch}.");
            P03WholeFlowHarness.Require(server.ApplicationRejections == 0 &&
                server.MalformedProofs == 0 && client.RejectedDeliveries == 0 &&
                client.MalformedDeliveries == 0 && client.ContradictoryDeliveries == 0,
                "DirtyChunk remote path rejected semantic state.");
        }
    }
    internal override P03ProfileCounters GetCounters()
    {
        ulong bundles = 0;
        ulong entries = 0;
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteDirtyChunkSpatialFanoutServerSnapshot snapshot = _servers[index].GetSnapshot();
            bundles += snapshot.BundlesQueued;
            entries += snapshot.BundleEntriesQueued;
        }
        DirtyChunkSpatialFanoutSnapshot core = _service.GetSnapshot();
        return new P03ProfileCounters(
            ScheduleCalls, ScheduleSuccesses, ClientMessages, ServerProofMessages,
            Subtract(bundles, _baselineBundles),
            Subtract(entries, _baselineBundleEntries),
            Subtract(core.SharedPageMaterializations, _baselineSharedPageMaterializations),
            Subtract(core.SharedPageHits, _baselineSharedPageHits));
    }
    internal override void ResetCounters()
    {
        base.ResetCounters();
        _baselineBundles = 0;
        _baselineBundleEntries = 0;
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteDirtyChunkSpatialFanoutServerSnapshot snapshot = _servers[index].GetSnapshot();
            _baselineBundles += snapshot.BundlesQueued;
            _baselineBundleEntries += snapshot.BundleEntriesQueued;
        }
        DirtyChunkSpatialFanoutSnapshot core = _service.GetSnapshot();
        _baselineSharedPageMaterializations = core.SharedPageMaterializations;
        _baselineSharedPageHits = core.SharedPageHits;
    }
    internal override long GetServerReservedBytes()
    {
        long value = checked(Source.GetSnapshot().ReservedBytes + _service.GetSnapshot().ReservedBytes);
        for (int index = 0; index < _servers.Length; index++)
            value = checked(value + _servers[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override long GetClientReservedBytes()
    {
        long value = 0;
        for (int index = 0; index < _clients.Length; index++)
            value = checked(value + _clients[index].GetSnapshot().ReservedBytes +
                Caches[index].GetSnapshot().ReservedBytes);
        return value;
    }
    internal override string DescribeState()
    {
        RemoteDirtyChunkSpatialFanoutServerSnapshot server = _servers[0].GetSnapshot();
        RemoteChunkWorldStateClientSnapshot client = _clients[0].GetSnapshot();
        return $"core={_service.GetSnapshot()} server0={server} client0={client} " +
            $"serverQueue={Sessions[0].Server.IndependentUnreliableQueued} " +
            $"clientQueue={Sessions[0].Client.IndependentUnreliableQueued}";
    }

    private static ulong Subtract(ulong value, ulong baseline) =>
        value >= baseline ? value - baseline : 0;
}

internal struct P03TransportTotals
{
    internal ulong SentPackets;
    internal ulong ReceivedPackets;
    internal ulong DataPackets;
    internal ulong AcknowledgementPackets;
    internal ulong EncryptionInvocations;
    internal ulong EncryptionPlaintextBytesAttempted;
    internal ulong PlaintextBytes;
    internal ulong DecryptedBytes;
    internal ulong Frames;
    internal ulong UnreliableDatagrams;
    internal ulong MultiFrameDatagrams;
    internal ulong HistogramOne;
    internal ulong HistogramTwo;
    internal ulong HistogramThree;
    internal ulong HistogramFour;
    internal ulong HistogramFive;
    internal ulong HistogramSix;
    internal ulong HistogramSeven;
    internal ulong HistogramEight;
    internal ulong CapacityDrops;
    internal ulong Retransmissions;
    internal ulong ClientWireDatagrams;
    internal ulong ClientWireBytes;
    internal void Add(NetworkProtocolSnapshot value)
    {
        SentPackets += value.SentPackets;
        ReceivedPackets += value.ReceivedPackets;
        DataPackets += value.DataPacketsSent;
        AcknowledgementPackets += value.AcknowledgementPacketsSent;
        EncryptionInvocations += value.EncryptionInvocations;
        EncryptionPlaintextBytesAttempted += value.EncryptionPlaintextBytesAttempted;
        PlaintextBytes += value.EncryptedPlaintextBytes;
        DecryptedBytes += value.DecryptedPlaintextBytes;
        CapacityDrops += value.CapacityDrops + value.RetainedPayloads.ByteCapacityFailures;
        Retransmissions += value.ReliableRetransmissions;
        NetworkUnreliableAggregationSnapshot aggregate = value.UnreliableAggregation;
        Frames += aggregate.SentFrames;
        UnreliableDatagrams += aggregate.SentDatagrams;
        MultiFrameDatagrams += aggregate.SentDatagramsWithTwoOrMoreFrames;
        HistogramOne += aggregate.SentHistogramOne;
        HistogramTwo += aggregate.SentHistogramTwo;
        HistogramThree += aggregate.SentHistogramThree;
        HistogramFour += aggregate.SentHistogramFour;
        HistogramFive += aggregate.SentHistogramFive;
        HistogramSix += aggregate.SentHistogramSix;
        HistogramSeven += aggregate.SentHistogramSeven;
        HistogramEight += aggregate.SentHistogramEightOrMore;
    }
}

internal readonly record struct P03WholeFlowReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string GitSha,
    string Runtime,
    string OperatingSystem,
    int ProcessorCount,
    long StopwatchFrequency,
    P03WholeFlowScope Scope,
    int ExecutionSeed,
    int WarmupTicks,
    int MeasuredTicks,
    P03WholeFlowRow[] Rows);

internal readonly record struct Pf5R6BodySharingReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string GitSha,
    string Runtime,
    string OperatingSystem,
    int ProcessorCount,
    long StopwatchFrequency,
    P03WholeFlowScope Scope,
    int ExecutionSeed,
    int WarmupTicks,
    int MeasuredTicks,
    P03WholeFlowRow[] Rows,
    Pf5R1ScenarioReport[] DivergenceScenarios);

internal readonly record struct P03WholeFlowScope(
    string Included,
    string Excluded,
    string AggregationExperiment);

internal readonly record struct P03WholeFlowRow(
    int ExecutionOrder,
    string Workload,
    string Profile,
    int MaximumUnreliableFramesPerDatagram,
    string SocketIoMode,
    int Recipients,
    int Entities,
    int DirtyEntitiesPerTick,
    int ActiveEntities,
    int InputIntervalTicks,
    double ModeledInputCommandsPerTick,
    string SemanticFamilies,
    P03CadenceSummary Cadence,
    P03WholeFlowTiming Timing,
    P03TimingBoundaryEvidence TimingBoundary,
    P03TransportSummary Transport,
    P03ProfileCounters ProfileCounters,
    string SemanticDigest,
    long AllocatedBytes,
    long ProfileReservedBytes,
    long ProfileServerReservedBytes,
    long ProfileClientReservedBytes,
    long NetworkReservedBytes,
    long NetworkServerReservedBytes,
    long NetworkClientReservedBytes,
    bool Converged,
    string Curve,
    P03NormalizedCost Normalized,
    P03BackgroundEgressSummary BackgroundEgress,
    P03ProcessWindowSummary ProcessWindow,
    P03ListenerConfigurationSummary ListenerConfiguration,
    P03WholeFlowRawSamples? RawSamples = null,
    P03DrainSummary Drain = default,
    P03MeasurementWindowBoundary MeasurementWindow = default,
    P03ApplicationEpochSummary ApplicationEpochs = default);

internal readonly record struct P03ApplicationEpochSummary(
    bool Applicable,
    int RecipientCount,
    int DistinctEpochCount,
    ulong MinimumEpoch,
    ulong MaximumEpoch);

internal readonly record struct P03MeasurementWindowBoundary(
    bool CountersCapturedBeforeDrain,
    bool ProtocolTimingCapturedBeforeDrain,
    bool TransportCapturedBeforeDrain,
    bool ProcessWindowCapturedBeforeDrain,
    int MeasuredTicks);

internal readonly record struct P03DrainSummary(
    int RequestedRepetitions,
    int ExecutedRounds,
    int OutstandingAtMeasurementEnd,
    int FinalOutstanding,
    ulong ScheduleAttempts,
    ulong DeliveriesQueued,
    ulong GameMessagesQueued,
    ulong ProofEntries,
    ulong KnowledgeAdvancements,
    ulong ExactDebtRetired,
    ulong SupersededDebtRetired,
    double ServerProtocolMilliseconds,
    double ClientProtocolMilliseconds,
    ulong ServerEmitCalls,
    ulong ServerInboundCalls,
    ulong ClientEmitCalls,
    ulong ClientInboundCalls,
    P03DrainTransportDelta Transport,
    P03DrainBackgroundEgressDelta BackgroundEgress,
    bool Converged)
{
    internal static P03DrainSummary Create(
        int requestedRepetitions,
        int executedRounds,
        in P03ProfileCounters measured,
        in P03ProfileCounters final,
        in P03ProtocolTiming measuredProtocol,
        in P03ProtocolTiming finalProtocol,
        in P03TransportTotals finalTransport,
        in P03TransportTotals measuredTransport,
        in SharedUdpListenerSnapshot finalListener,
        in SharedUdpListenerSnapshot measuredListener,
        bool converged) => new(
        requestedRepetitions,
        executedRounds,
        measured.Lifecycle.OutstandingAtCapture,
        final.Lifecycle.OutstandingAtCapture,
        D(final.Lifecycle.ScheduleAttempts, measured.Lifecycle.ScheduleAttempts),
        D(final.Lifecycle.DeliveriesQueued, measured.Lifecycle.DeliveriesQueued),
        D(final.GameMessagesQueued, measured.GameMessagesQueued),
        D(final.Lifecycle.ProofEntries, measured.Lifecycle.ProofEntries),
        D(final.Lifecycle.KnowledgeAdvancements, measured.Lifecycle.KnowledgeAdvancements),
        D(final.Lifecycle.ExactDebtRetired, measured.Lifecycle.ExactDebtRetired),
        D(final.Lifecycle.SupersededDebtRetired, measured.Lifecycle.SupersededDebtRetired),
        Math.Max(0, finalProtocol.ServerMilliseconds - measuredProtocol.ServerMilliseconds),
        Math.Max(0, finalProtocol.ClientMilliseconds - measuredProtocol.ClientMilliseconds),
        D(finalProtocol.Server.EmitCalls, measuredProtocol.Server.EmitCalls),
        D(finalProtocol.Server.InboundCalls, measuredProtocol.Server.InboundCalls),
        D(finalProtocol.Client.EmitCalls, measuredProtocol.Client.EmitCalls),
        D(finalProtocol.Client.InboundCalls, measuredProtocol.Client.InboundCalls),
        P03DrainTransportDelta.Create(
            finalTransport, measuredTransport, finalListener, measuredListener),
        P03DrainBackgroundEgressDelta.Create(
            finalListener.BackgroundEgress, measuredListener.BackgroundEgress),
        converged);

    private static ulong D(ulong after, ulong before) => after >= before ? after - before : 0;
}

// Drain-only evidence deliberately exposes exact cumulative deltas, not cumulative
// lifetime percentiles/high-water marks sampled after the drain. Percentiles cannot be
// subtracted into a window distribution; measured-window distributions live on the row.
internal readonly record struct P03DrainTransportDelta(
    ulong SecurePackets,
    ulong DataDatagrams,
    ulong AcknowledgementDatagrams,
    ulong UnreliableFrames,
    ulong UnreliableDatagrams,
    ulong EncryptedPlaintextBytes,
    ulong DecryptedPlaintextBytes,
    ulong WireDatagrams,
    ulong WireBytes,
    ulong CapacityDrops,
    ulong Retransmissions,
    ulong ListenerSendCalls,
    ulong ListenerReceiveCalls,
    ulong NativeSendCalls,
    ulong NativeReceiveCalls,
    ulong NativeSentDatagrams,
    ulong NativeReceivedDatagrams,
    double ListenerSendSyscallMilliseconds,
    double ListenerReceiveSyscallMilliseconds,
    double ListenerRoutingMilliseconds)
{
    internal static P03DrainTransportDelta Create(
        in P03TransportTotals after,
        in P03TransportTotals before,
        in SharedUdpListenerSnapshot listenerAfter,
        in SharedUdpListenerSnapshot listenerBefore)
    {
        SharedUdpReceiveCensusSnapshot receiveAfter = listenerAfter.ReceiveCensus;
        SharedUdpReceiveCensusSnapshot receiveBefore = listenerBefore.ReceiveCensus;
        ulong clientWireDatagrams = D(after.ClientWireDatagrams, before.ClientWireDatagrams);
        ulong clientWireBytes = D(after.ClientWireBytes, before.ClientWireBytes);
        return new(
            D(after.SentPackets, before.SentPackets),
            D(after.DataPackets, before.DataPackets),
            D(after.AcknowledgementPackets, before.AcknowledgementPackets),
            D(after.Frames, before.Frames),
            D(after.UnreliableDatagrams, before.UnreliableDatagrams),
            D(after.PlaintextBytes, before.PlaintextBytes),
            D(after.DecryptedBytes, before.DecryptedBytes),
            D(listenerAfter.Sent, listenerBefore.Sent) + clientWireDatagrams,
            D(listenerAfter.SentBytes, listenerBefore.SentBytes) + clientWireBytes,
            D(after.CapacityDrops, before.CapacityDrops) +
                D(listenerAfter.CapacityDrops, listenerBefore.CapacityDrops),
            D(after.Retransmissions, before.Retransmissions),
            D(listenerAfter.SendCalls, listenerBefore.SendCalls),
            D(listenerAfter.ReceiveCalls, listenerBefore.ReceiveCalls),
            D(listenerAfter.NativeSendCalls, listenerBefore.NativeSendCalls),
            D(receiveAfter.NativeReceiveCalls, receiveBefore.NativeReceiveCalls),
            D(listenerAfter.NativeSentDatagrams, listenerBefore.NativeSentDatagrams),
            D(receiveAfter.NativeReceivedDatagrams, receiveBefore.NativeReceivedDatagrams),
            Ms(D(listenerAfter.SendSystemCallTicks, listenerBefore.SendSystemCallTicks)),
            Ms(D(listenerAfter.ReceiveSystemCallTicks, listenerBefore.ReceiveSystemCallTicks)),
            Ms(D(receiveAfter.RoutingTicks, receiveBefore.RoutingTicks)));
    }

    private static ulong D(ulong after, ulong before) => after >= before ? after - before : 0;
    private static double Ms(ulong ticks) => ticks * 1_000d / Stopwatch.Frequency;
}

internal readonly record struct P03DrainBackgroundEgressDelta(
    int FinalQueuedDatagrams,
    long FinalQueuedBytes,
    ulong AcceptedDatagrams,
    ulong AcceptedBytes,
    ulong PhysicallySentDatagrams,
    ulong PhysicallySentBytes,
    ulong StalePathDrops,
    ulong PhysicalSendFailures,
    ulong CapacityRefusals,
    ulong WorkerWakeups,
    ulong DrainPasses,
    ulong ScalarSendCalls,
    ulong NativeSendCalls,
    ulong PartialNativeSendCalls,
    ulong WouldBlockEvents,
    ulong NoBufferEvents,
    ulong ProducerEnqueueSamples,
    double ProducerEnqueueTotalMilliseconds,
    ulong QueueResidenceSamples,
    double QueueResidenceTotalMilliseconds,
    double PhysicalSendSystemCallMilliseconds,
    double WorkerServiceMilliseconds,
    bool HasWorkerFailure,
    string LastSocketError)
{
    internal static P03DrainBackgroundEgressDelta Create(
        in SharedUdpBackgroundEgressSnapshot after,
        in SharedUdpBackgroundEgressSnapshot before) => new(
        after.CurrentDatagrams,
        after.CurrentBytes,
        D(after.AcceptedDatagrams, before.AcceptedDatagrams),
        D(after.AcceptedBytes, before.AcceptedBytes),
        D(after.PhysicallySentDatagrams, before.PhysicallySentDatagrams),
        D(after.PhysicallySentBytes, before.PhysicallySentBytes),
        D(after.StalePathDrops, before.StalePathDrops),
        D(after.PhysicalSendFailures, before.PhysicalSendFailures),
        D(after.CapacityRefusals, before.CapacityRefusals),
        D(after.WorkerWakeups, before.WorkerWakeups),
        D(after.DrainPasses, before.DrainPasses),
        D(after.ScalarSendCalls, before.ScalarSendCalls),
        D(after.NativeSendCalls, before.NativeSendCalls),
        D(after.PartialNativeSendCalls, before.PartialNativeSendCalls),
        D(after.WouldBlockEvents, before.WouldBlockEvents),
        D(after.NoBufferEvents, before.NoBufferEvents),
        D(after.ProducerEnqueue.Count, before.ProducerEnqueue.Count),
        Ms(D(after.ProducerEnqueue.TotalStopwatchTicks,
            before.ProducerEnqueue.TotalStopwatchTicks)),
        D(after.QueueResidence.Count, before.QueueResidence.Count),
        Ms(D(after.QueueResidence.TotalStopwatchTicks,
            before.QueueResidence.TotalStopwatchTicks)),
        Ms(D(after.PhysicalSendSystemCallStopwatchTicks,
            before.PhysicalSendSystemCallStopwatchTicks)),
        Ms(D(after.WorkerServiceStopwatchTicks,
            before.WorkerServiceStopwatchTicks)),
        after.HasWorkerFailure,
        after.LastSocketError.ToString());

    private static ulong D(ulong after, ulong before) => after >= before ? after - before : 0;
    private static double Ms(ulong ticks) => ticks * 1_000d / Stopwatch.Frequency;
}

internal sealed record P03WholeFlowRawSamples(
    double[] RemotePublicationPreparationMilliseconds,
    double[] RecipientCommitObservationMilliseconds,
    double[] GameScheduleAndCodecQueueMilliseconds,
    double[] ServerGameProofApplyMilliseconds,
    double[] NetworkAndRemoteReplicationMilliseconds,
    double[] NetworkProtocolAndOtherMilliseconds,
    double[] ServerOutboundProtocolMilliseconds,
    double[] ServerInboundProtocolMilliseconds,
    double[] ServerOwnedRemoteReplicationBudgetMilliseconds,
    double[] ServerOwnedTickMilliseconds,
    double[] WholeFixtureMilliseconds);

internal readonly record struct P03ProcessWindowSummary(
    double WallMilliseconds,
    double CpuMilliseconds,
    double CpuCores,
    long PeakWorkingSetBytes,
    long StartingWorkingSetBytes,
    long EndingWorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

internal readonly record struct P03ListenerConfigurationSummary(
    long ConfiguredSocketBufferBytes,
    long ActualSocketBufferBytes,
    long ReservedBytes);

internal readonly record struct Pf5R3BackgroundEgressReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string GitSha,
    string Runtime,
    string OperatingSystem,
    int ProcessorCount,
    long StopwatchFrequency,
    int ExecutionSeed,
    int WarmupTicks,
    int MeasuredTicks,
    string ProductionPolicy,
    string MainThreadMetric,
    string TelemetryScope,
    P03WholeFlowRow[] Rows);

internal readonly record struct P03BackgroundEgressSummary(
    string State,
    bool UsesNativeBatching,
    bool TimingEnabled,
    int SenderThreads,
    int CapacityDatagrams,
    long CapacityBytes,
    long ReservedBytes,
    int CurrentDatagrams,
    long CurrentBytes,
    int HighWaterDatagrams,
    long HighWaterBytes,
    ulong AcceptedDatagrams,
    ulong AcceptedBytes,
    ulong PhysicallySentDatagrams,
    ulong PhysicallySentBytes,
    ulong StalePathDrops,
    ulong PhysicalSendFailures,
    ulong CapacityRefusals,
    ulong WorkerWakeups,
    ulong DrainPasses,
    ulong ScalarSendCalls,
    ulong NativeSendCalls,
    ulong PartialNativeSendCalls,
    ulong WouldBlockEvents,
    ulong NoBufferEvents,
    int DrainBatchP50,
    int DrainBatchP95,
    int MaximumBatch,
    NetworkTimingDistribution ProducerEnqueue,
    NetworkTimingDistribution ProducerEnqueueComplete,
    NetworkTimingDistribution ProducerEnqueueAcceptedComplete,
    NetworkTimingDistribution ProducerEnqueueRefusedComplete,
    NetworkTimingDistribution ProducerEnqueueFaultedComplete,
    NetworkTimingDistribution QueueResidence,
    double OldestQueuedMilliseconds,
    double PhysicalSendSystemCallMilliseconds,
    double WorkerServiceMilliseconds,
    bool HasWorkerFailure,
    string LastSocketError)
{
    internal static P03BackgroundEgressSummary Delta(
        in SharedUdpBackgroundEgressSnapshot after,
        in SharedUdpBackgroundEgressSnapshot before) => new(
        after.State.ToString(),
        after.UsesNativeBatching,
        after.TimingEnabled,
        after.SenderThreads,
        after.CapacityDatagrams,
        after.CapacityBytes,
        after.ReservedBytes,
        after.CurrentDatagrams,
        after.CurrentBytes,
        after.HighWaterDatagrams,
        after.HighWaterBytes,
        D(after.AcceptedDatagrams, before.AcceptedDatagrams),
        D(after.AcceptedBytes, before.AcceptedBytes),
        D(after.PhysicallySentDatagrams, before.PhysicallySentDatagrams),
        D(after.PhysicallySentBytes, before.PhysicallySentBytes),
        D(after.StalePathDrops, before.StalePathDrops),
        D(after.PhysicalSendFailures, before.PhysicalSendFailures),
        D(after.CapacityRefusals, before.CapacityRefusals),
        D(after.WorkerWakeups, before.WorkerWakeups),
        D(after.DrainPasses, before.DrainPasses),
        D(after.ScalarSendCalls, before.ScalarSendCalls),
        D(after.NativeSendCalls, before.NativeSendCalls),
        D(after.PartialNativeSendCalls, before.PartialNativeSendCalls),
        D(after.WouldBlockEvents, before.WouldBlockEvents),
        D(after.NoBufferEvents, before.NoBufferEvents),
        after.DrainBatchP50,
        after.DrainBatchP95,
        after.MaximumBatch,
        WindowDistribution(after.ProducerEnqueue, before.ProducerEnqueue),
        WindowDistribution(after.ProducerEnqueueComplete, before.ProducerEnqueueComplete),
        WindowDistribution(
            after.ProducerEnqueueAcceptedComplete, before.ProducerEnqueueAcceptedComplete),
        WindowDistribution(
            after.ProducerEnqueueRefusedComplete, before.ProducerEnqueueRefusedComplete),
        WindowDistribution(
            after.ProducerEnqueueFaultedComplete, before.ProducerEnqueueFaultedComplete),
        WindowDistribution(after.QueueResidence, before.QueueResidence),
        after.OldestQueuedStopwatchTicks * 1_000d / Stopwatch.Frequency,
        D(after.PhysicalSendSystemCallStopwatchTicks,
            before.PhysicalSendSystemCallStopwatchTicks) * 1_000d / Stopwatch.Frequency,
        D(after.WorkerServiceStopwatchTicks,
            before.WorkerServiceStopwatchTicks) * 1_000d / Stopwatch.Frequency,
        after.HasWorkerFailure,
        after.LastSocketError.ToString());

    private static ulong D(ulong after, ulong before) => after >= before ? after - before : 0;

    // Count and total are exact measurement-window deltas. Histogram percentiles and the
    // maximum remain explicitly lifetime-scoped because the production snapshot exposes a
    // cumulative bounded histogram rather than mutable/resettable benchmark counters.
    private static NetworkTimingDistribution WindowDistribution(
        in NetworkTimingDistribution after,
        in NetworkTimingDistribution before) => new(
        D(after.Count, before.Count),
        D(after.TotalStopwatchTicks, before.TotalStopwatchTicks),
        after.P50StopwatchTicks,
        after.P95StopwatchTicks,
        after.P99StopwatchTicks,
        after.MaximumStopwatchTicks);
}

internal readonly record struct P03NormalizedCost(
    double LegacyServerRoleP95MicrosecondsPerRecipientTick,
    double InclusiveNetworkReplicationP95MicrosecondsPerRecipientTick,
    double MeanLegacyServerRoleMicrosecondsPerSuccessfulPublication,
    double MeanInclusiveNetworkReplicationMicrosecondsPerSuccessfulPublication,
    double ApplicationBytesPerRecipientTick,
    double WireBytesPerRecipientTick,
    double DatagramsPerRecipientTick,
    double SemanticPublicationsPerRecipientTick,
    double GameMessagesPerRecipientTick,
    double ProofEnvelopesPerPublication,
    double ScheduleAttemptsPerSuccessfulPublication)
{
    internal static P03NormalizedCost Calculate(
        int recipients,
        int ticks,
        IReadOnlyList<double> inclusive,
        IReadOnlyList<double> legacy,
        in P03TransportSummary transport,
        in P03ProfileCounters profile)
    {
        double recipientTicks = checked((double)recipients * ticks);
        ulong publications = profile.SuccessfulSemanticPublications != 0
            ? profile.SuccessfulSemanticPublications
            : profile.SuccessfulGameMessages;
        double publicationDivisor = publications == 0 ? 1 : publications;
        double legacyMean = legacy.Count == 0 ? 0 : legacy.Average();
        double inclusiveMean = inclusive.Count == 0 ? 0 : inclusive.Average();
        ulong gameMessages = profile.GameMessagesQueued != 0
            ? profile.GameMessagesQueued
            : profile.ClientGameMessages;
        return new(
            P03Metric.From(legacy).P95 * 1_000d / recipients,
            P03Metric.From(inclusive).P95 * 1_000d / recipients,
            legacyMean * 1_000d * ticks / publicationDivisor,
            inclusiveMean * 1_000d * ticks / publicationDivisor,
            transport.GamePayloadBytes / recipientTicks,
            transport.WireBytes / recipientTicks,
            transport.WireDatagrams / recipientTicks,
            publications / recipientTicks,
            gameMessages / recipientTicks,
            profile.ServerApplicationProofMessages / publicationDivisor,
            profile.ScheduleCalls / publicationDivisor);
    }
}

internal readonly record struct P03CadenceSummary(
    string AoiCadenceSkippedTicks,
    int TickDurationMilliseconds,
    long EntityImmediateIntervalMilliseconds,
    long EntityNearIntervalMilliseconds,
    long EntityFarIntervalMilliseconds,
    uint ChunkImmediatePeriodTicks,
    uint ChunkNearPeriodTicks,
    uint ChunkFarPeriodTicks);

internal readonly record struct P03WholeFlowTiming(
    P03Metric Tick,
    P03Metric AuthorityMutation,
    P03Metric BaseAuthoritativeMutation,
    P03Metric RemotePublicationPreparation,
    P03Metric RecipientCommitObservation,
    P03Metric GameScheduleAndCodecQueue,
    P03Metric CombinedProtocolPumpWall,
    P03Metric ServerGameProofApply,
    P03Metric ClientGameDecodeApplyAndProofQueue,
    P03Metric NetworkAndRemoteReplication,
    P03Metric ServerOwnedRemoteReplicationBudget,
    P03Metric AuthorityThroughNetworkAndRemoteReplication,
    P03Metric ServerRole,
    P03Metric ClientRole,
    P03ProtocolTiming Protocol,
    P03Metric ServerOwnedTick = default,
    P03Metric AttributedMainThread = default,
    P03Metric UnattributedResidual = default,
    P03Metric InputAndSession = default,
    P03Metric ChatAndCommands = default,
    P03Metric AuthorityAndEcs = default,
    P03Metric LocomotionAndPhysics = default,
    P03Metric InventoryAndItems = default,
    P03Metric WorldCommit = default,
    P03Metric ServerOutboundProtocol = default,
    P03Metric ServerInboundProtocol = default,
    P03Metric NetworkOtherResidual = default);

internal readonly record struct P03ExternalServerPhaseSample(
    long ServerOwnedTicks,
    long InputAndSessionTicks,
    long ChatAndCommandsTicks,
    long AuthorityAndEcsTicks,
    long LocomotionAndPhysicsTicks,
    long InventoryAndItemTicks,
    long WorldCommitTicks);

internal interface IP03ExternalServerWorkload : IDisposable
{
    P03ExternalServerPhaseSample RunTick(int tick, bool measure);

    void ResetMeasurement();

    object CaptureSummary();
}

internal readonly record struct P03TimingBoundaryEvidence(
    bool Captured,
    int CombinedStartOrder,
    int AuthorityStartOrder,
    int PreparedSourceObservedOrder,
    int RecipientWakeupObservedOrder,
    int AuthorityEndOrder,
    int NetworkStartOrder,
    int NetworkEndOrder,
    int CombinedEndOrder,
    ulong PreparedSourcePublicationsObserved,
    bool CombinedFromSingleElapsedInterval,
    bool CombinedFromPhaseDurations);

internal readonly record struct P03ProtocolTiming(
    P03ProtocolStage Server,
    P03ProtocolStage Client,
    double ServerMilliseconds,
    double ClientMilliseconds,
    double ServerOutboundMilliseconds,
    double ServerInboundMilliseconds,
    double ClientOutboundMilliseconds,
    double ClientInboundMilliseconds);

internal readonly record struct P03ProtocolStage(
    double SelectionMilliseconds,
    double FrameEncodeMilliseconds,
    double SecureSendMilliseconds,
    double EndpointSendMilliseconds,
    double EndpointReceiveMilliseconds,
    double SecureReceiveMilliseconds,
    double FrameAdmissionMilliseconds,
    ulong EmitCalls,
    ulong IdleEmitCalls,
    ulong InboundCalls,
    ulong EmptyInboundCalls,
    ulong InboundDatagrams,
    ulong OutboundUnreliableFrames,
    ulong InboundUnreliableFrames,
    ulong LaneSelectionPasses,
    ulong ReliableSlotProbes,
    ulong IndependentSlotProbes,
    ulong UnreliableSlotProbes,
    ulong ExcludedSelectionProbes,
    ulong EmptySelectionAttempts,
    ulong ReadySelections);

internal readonly record struct P03TransportSummary(
    ulong SecurePackets,
    ulong DataDatagrams,
    ulong AcknowledgementDatagrams,
    ulong UnreliableFrames,
    ulong UnreliableDatagrams,
    ulong MultiFrameDatagrams,
    double FramesPerDatagram,
    double MultiFramePercent,
    ulong GamePayloadBytes,
    ulong NetworkFrameHeaderBytes,
    ulong EncryptionInvocations,
    ulong EncryptionPlaintextBytesAttempted,
    ulong EncryptedPlaintextBytes,
    ulong DecryptedPlaintextBytes,
    ulong WireDatagrams,
    ulong WireBytes,
    long NetworkOverheadBytes,
    ulong CapacityDrops,
    ulong Retransmissions,
    ulong ListenerSendCalls,
    ulong ListenerReceiveCalls,
    ulong NativeSendCalls,
    ulong NativeReceiveCalls,
    ulong NativeSentDatagrams,
    ulong NativeReceivedDatagrams,
    double NativeSendDatagramsPerCall,
    double NativeReceiveDatagramsPerCall,
    int NativeSendBatchP50,
    int NativeSendBatchP95,
    int MaximumNativeSendBatch,
    int NativeReceiveBatchP50,
    int NativeReceiveBatchP95,
    int MaximumNativeReceiveBatch,
    int MaximumListenerOutboundQueued,
    double ListenerSendSyscallMilliseconds,
    double ListenerReceiveSyscallMilliseconds,
    double ListenerRoutingMilliseconds,
    ulong HistogramOne,
    ulong HistogramTwo,
    ulong HistogramThree,
    ulong HistogramFour,
    ulong HistogramFive,
    ulong HistogramSix,
    ulong HistogramSeven,
    ulong HistogramEightOrMore)
{
    internal static P03TransportSummary Delta(
        P03TransportTotals after,
        P03TransportTotals before,
        SharedUdpListenerSnapshot listenerAfter,
        SharedUdpListenerSnapshot listenerBefore,
        int maximumUnreliableFramesPerDatagram)
    {
        ulong packets = D(after.SentPackets, before.SentPackets);
        ulong data = D(after.DataPackets, before.DataPackets);
        ulong acks = D(after.AcknowledgementPackets, before.AcknowledgementPackets);
        ulong frames = D(after.Frames, before.Frames);
        ulong datagrams = D(after.UnreliableDatagrams, before.UnreliableDatagrams);
        ulong multi = D(after.MultiFrameDatagrams, before.MultiFrameDatagrams);
        ulong encryptionInvocations = D(
            after.EncryptionInvocations, before.EncryptionInvocations);
        ulong encryptionAttemptedBytes = D(
            after.EncryptionPlaintextBytesAttempted,
            before.EncryptionPlaintextBytesAttempted);
        ulong plaintext = D(after.PlaintextBytes, before.PlaintextBytes);
        ulong decrypted = D(after.DecryptedBytes, before.DecryptedBytes);
        ulong clientWireDatagrams = D(after.ClientWireDatagrams, before.ClientWireDatagrams);
        ulong clientWireBytes = D(after.ClientWireBytes, before.ClientWireBytes);
        ulong listenerDatagrams = D(listenerAfter.Sent, listenerBefore.Sent);
        ulong listenerBytes = D(listenerAfter.SentBytes, listenerBefore.SentBytes);
        ulong wireDatagrams = listenerDatagrams + clientWireDatagrams;
        ulong wireBytes = listenerBytes + clientWireBytes;
        ulong nativeSendCalls = D(listenerAfter.NativeSendCalls, listenerBefore.NativeSendCalls);
        ulong nativeSent = D(listenerAfter.NativeSentDatagrams, listenerBefore.NativeSentDatagrams);
        SharedUdpReceiveCensusSnapshot receiveAfter = listenerAfter.ReceiveCensus;
        SharedUdpReceiveCensusSnapshot receiveBefore = listenerBefore.ReceiveCensus;
        ulong nativeReceiveCalls = D(receiveAfter.NativeReceiveCalls, receiveBefore.NativeReceiveCalls);
        ulong nativeReceived = D(receiveAfter.NativeReceivedDatagrams, receiveBefore.NativeReceivedDatagrams);
        ulong histogramOne = D(after.HistogramOne, before.HistogramOne);
        if (maximumUnreliableFramesPerDatagram == 1 && frames == 0 && datagrams == 0)
        {
            // The frozen scalar cap-one path predates aggregation telemetry. Every Data packet in
            // this comparison carries exactly one independent-unreliable frame, so normalize that
            // equivalent work instead of reporting a misleading zero-frame ratio.
            frames = data;
            datagrams = data;
            histogramOne = data;
        }
        ulong frameHeaders = checked(frames * (ulong)NetworkProtocolDataHeader.EncodedBytes);
        ulong gamePayloadBytes = plaintext >= frameHeaders ? plaintext - frameHeaders : 0;
        return new(
            packets, data, acks, frames, datagrams, multi,
            datagrams == 0 ? 0 : frames / (double)datagrams,
            datagrams == 0 ? 0 : multi * 100d / datagrams,
            gamePayloadBytes, frameHeaders, encryptionInvocations,
            encryptionAttemptedBytes, plaintext, decrypted, wireDatagrams, wireBytes,
            checked((long)wireBytes - (long)plaintext),
            D(after.CapacityDrops, before.CapacityDrops) +
                D(listenerAfter.CapacityDrops, listenerBefore.CapacityDrops),
            D(after.Retransmissions, before.Retransmissions),
            D(listenerAfter.SendCalls, listenerBefore.SendCalls),
            D(listenerAfter.ReceiveCalls, listenerBefore.ReceiveCalls),
            nativeSendCalls, nativeReceiveCalls, nativeSent, nativeReceived,
            nativeSendCalls == 0 ? 0 : nativeSent / (double)nativeSendCalls,
            nativeReceiveCalls == 0 ? 0 : nativeReceived / (double)nativeReceiveCalls,
            listenerAfter.NativeSendBatchP50, listenerAfter.NativeSendBatchP95,
            listenerAfter.MaximumNativeSendBatch,
            receiveAfter.NativeReceiveBatchP50, receiveAfter.NativeReceiveBatchP95,
            receiveAfter.MaximumNativeReceiveBatch,
            listenerAfter.MaximumOutboundQueuedDatagrams,
            Ms(listenerAfter.SendSystemCallTicks), Ms(listenerAfter.ReceiveSystemCallTicks),
            Ms(receiveAfter.RoutingTicks),
            histogramOne,
            D(after.HistogramTwo, before.HistogramTwo),
            D(after.HistogramThree, before.HistogramThree),
            D(after.HistogramFour, before.HistogramFour),
            D(after.HistogramFive, before.HistogramFive),
            D(after.HistogramSix, before.HistogramSix),
            D(after.HistogramSeven, before.HistogramSeven),
            D(after.HistogramEight, before.HistogramEight));
    }
    private static ulong D(ulong after, ulong before) => after >= before ? after - before : 0;
    private static double Ms(ulong ticks) => ticks * 1_000d / Stopwatch.Frequency;
}

internal readonly record struct P03ProfileCounters(
    ulong ScheduleCalls,
    ulong SuccessfulGameMessages,
    ulong ClientGameMessages,
    ulong ServerApplicationProofMessages,
    ulong DirtyChunkBundles,
    ulong DirtyChunkBundleEntries,
    ulong DirtyChunkSharedPageMaterializations,
    ulong DirtyChunkSharedPageHits,
    ulong SuccessfulSemanticPublications = 0,
    ulong GameMessagesQueued = 0,
    ulong ApplicationBytesQueued = 0,
    ulong InterestSubscriptions = 0,
    ulong SemanticKnowledgeRows = 0,
    ulong DueCandidatePublications = 0,
    ulong SimultaneouslyDuePublications = 0,
    ulong MultiDueRecipientPasses = 0,
    ulong ChunkBundleMessages = 0,
    ulong ChunkBundleEntries = 0,
    ulong CapTwoPairOpportunities = 0,
    int MaximumDuePerRecipientPass = 0,
    ulong SnapshotOrRecoveryDeliveries = 0,
    ulong ProofEntries = 0,
    ulong DirtyChunksObserved = 0,
    ulong DirtyRecipientFanout = 0,
    ulong CommitObservations = 0,
    ulong EmptyCommitObservations = 0,
    ulong DirtyCommitObservations = 0,
    ulong DirtyWorkCoalesces = 0,
    ulong SchedulerRetryQueues = 0,
    ulong DueWorkDequeued = 0,
    ulong PendingMembershipsExamined = 0,
    ulong SchedulerCadenceDeferrals = 0,
    ulong SourceDeltaReads = 0,
    ulong SourceSnapshotReads = 0,
    ulong SourceSnapshotFallbacks = 0,
    P03ChunkCpuAttribution ChunkCpu = default,
    P03DeltaRangeReuseCensus DeltaRangeReuse = default,
    P03CompressionCounters Compression = default,
    P03ProofHealth ProofHealth = default,
    P03ReplicationLifecycleCounters Lifecycle = default,
    ChunkPublicationHistoryAuditSnapshot? HistoryAudit = null,
    PreparedChunkPublicationSnapshot PreparedPublication = default,
    RemoteChunkDeltaBodyStoreSnapshot DeltaBodies = default,
    int ResidentSourceChunks = 0,
    int CurrentSharedSubscriptionChunks = 0,
    int CurrentMemberships = 0,
    ChunkPublicationTopologyCensusSnapshot Topology = default);

internal readonly record struct P03ReplicationLifecycleCounters(
    ulong ScheduleAttempts,
    ulong DeliveriesQueued,
    ulong NoPayload,
    ulong CadenceDeferrals,
    ulong TransportDeferrals,
    ulong CapacityDeferrals,
    int OutstandingHighWater,
    int OutstandingAtCapture,
    ulong ProofEntries,
    ulong ProofApplied,
    ulong ProofRejected,
    ulong ProofCapacity,
    ulong ProofContradictory,
    ulong KnowledgeAdvancements,
    ulong NonAdvancingApplied,
    ulong ExactDebtRetired,
    ulong SupersededDebtRetired,
    ulong MalformedOrUnknown,
    ulong InvalidatedAoi,
    ulong InvalidatedSource,
    ulong InvalidatedRecovery)
{
    internal P03ReplicationLifecycleCounters Add(
        in RemoteChunkReplicationWorkSnapshot work,
        in RemoteChunkKnowledgeCensusSnapshot knowledge,
        int outstandingAtCapture)
    {
        UltraSim.Game.Replication.RemoteChunkProofCensusSnapshot proof = knowledge.Proof;
        RemoteChunkOutstandingCensusSnapshot outstanding = knowledge.Outstanding;
        return new(
            Plus(ScheduleAttempts, work.ScheduleAttempts),
            Plus(DeliveriesQueued, work.DeliveriesQueued),
            Plus(NoPayload, work.NoPayload),
            Plus(CadenceDeferrals, work.CadenceDeferrals),
            Plus(TransportDeferrals, work.TransportDeferrals),
            Plus(CapacityDeferrals, work.CapacityDeferrals),
            Math.Max(OutstandingHighWater, work.OutstandingHighWater),
            checked(OutstandingAtCapture + outstandingAtCapture),
            Plus(ProofEntries, proof.EntriesReceived),
            Plus(ProofApplied, proof.AppliedEntries),
            Plus(ProofRejected, proof.RejectedEntries),
            Plus(ProofCapacity, proof.CapacityExceededEntries),
            Plus(ProofContradictory, proof.ContradictoryEntries),
            Plus(KnowledgeAdvancements, proof.KnowledgeAdvancingEntries),
            Plus(NonAdvancingApplied, proof.NonAdvancingAppliedEntries),
            Plus(ExactDebtRetired, proof.ExactOutstandingRetired),
            Plus(SupersededDebtRetired, proof.SupersededOutstandingRetired),
            Plus(MalformedOrUnknown, proof.MalformedOrUnknownEntries),
            Plus(InvalidatedAoi, outstanding.InvalidatedByAoiLeave),
            Plus(InvalidatedSource, outstanding.InvalidatedBySourceReplacement),
            Plus(InvalidatedRecovery, outstanding.InvalidatedByRecovery));
    }

    private static ulong Plus(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal readonly record struct P03CompressionCounters(
    ulong Attempts,
    ulong Selected,
    ulong SkippedBySize,
    ulong RejectedAsUnprofitable,
    ulong CodecFailures,
    ulong CanonicalBytesAttempted,
    ulong CanonicalBytesSelected,
    ulong CompressedRepresentationBytes,
    ulong DirectBytesSaved,
    ulong CompressionStopwatchTicks,
    long MaximumCompressionMicroseconds,
    ulong SingletonDeltaCandidates,
    ulong SingletonEligibleByCanonicalSize,
    ulong SingletonAttempts,
    ulong SingletonCodecSuccesses,
    ulong SingletonCodecFailures,
    ulong SingletonBelowSavingsThreshold,
    ulong SingletonTooLargeForPeer,
    ulong SingletonDecodedBoundRejected,
    ulong SingletonProfitableVsCanonical129,
    ulong SingletonProfitableVsStandalone120,
    ulong SingletonSelected,
    ulong SingletonCanonical129Bytes,
    ulong SingletonStandalone120Bytes,
    ulong StandaloneFallbackWrites,
    ulong SingletonSelectedBytes,
    ulong SingletonBytesSavedVs120,
    ulong SingletonCompressionStopwatchTicks,
    long MaximumSingletonCompressionMicroseconds)
{
    internal P03CompressionCounters Add(in RemoteChunkCompressionCensusSnapshot value) => new(
        Add(Attempts, value.Attempts),
        Add(Selected, value.Selected),
        Add(SkippedBySize, value.SkippedBySize),
        Add(RejectedAsUnprofitable, value.RejectedAsUnprofitable),
        Add(CodecFailures, value.CodecFailures),
        Add(CanonicalBytesAttempted, value.CanonicalBytesAttempted),
        Add(CanonicalBytesSelected, value.CanonicalBytesSelected),
        Add(CompressedRepresentationBytes, value.CompressedRepresentationBytes),
        Add(DirectBytesSaved, value.DirectBytesSaved),
        Add(CompressionStopwatchTicks, value.Compression.TotalStopwatchTicks),
        Math.Max(MaximumCompressionMicroseconds, value.Compression.MaximumMicroseconds),
        Add(SingletonDeltaCandidates, value.SingletonDeltaCandidates),
        Add(SingletonEligibleByCanonicalSize, value.SingletonEligibleByCanonicalSize),
        Add(SingletonAttempts, value.SingletonAttempts),
        Add(SingletonCodecSuccesses, value.SingletonCodecSuccesses),
        Add(SingletonCodecFailures, value.SingletonCodecFailures),
        Add(SingletonBelowSavingsThreshold, value.SingletonBelowSavingsThreshold),
        Add(SingletonTooLargeForPeer, value.SingletonTooLargeForPeer),
        Add(SingletonDecodedBoundRejected, value.SingletonDecodedBoundRejected),
        Add(SingletonProfitableVsCanonical129, value.SingletonProfitableVsCanonical129),
        Add(SingletonProfitableVsStandalone120, value.SingletonProfitableVsStandalone120),
        Add(SingletonSelected, value.SingletonSelected),
        Add(SingletonCanonical129Bytes, value.SingletonCanonical129Bytes),
        Add(SingletonStandalone120Bytes, value.SingletonStandalone120Bytes),
        Add(StandaloneFallbackWrites, value.StandaloneFallbackWrites),
        Add(SingletonSelectedBytes, value.SingletonSelectedBytes),
        Add(SingletonBytesSavedVs120, value.SingletonBytesSavedVs120),
        Add(SingletonCompressionStopwatchTicks,
            value.SingletonCompression.TotalStopwatchTicks),
        Math.Max(MaximumSingletonCompressionMicroseconds,
            value.SingletonCompression.MaximumMicroseconds));

    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal readonly record struct P03ProofHealth(
    ulong ProofEntries,
    ulong AppliedEntries,
    ulong DeliveryAgeP95UpperBoundAuthoritySteps,
    long DeliveryQueuedToProofP95Microseconds,
    ulong SourceToProofP95UpperBoundAuthoritySteps,
    int OutstandingHighWater)
{
    internal P03ProofHealth Add(in RemoteChunkKnowledgeCensusSnapshot knowledge)
    {
        UltraSim.Game.Replication.RemoteChunkProofCensusSnapshot proof = knowledge.Proof;
        RemoteChunkFreshnessCensusSnapshot freshness = knowledge.Freshness;
        return new(
            Add(ProofEntries, proof.EntriesReceived),
            Add(AppliedEntries, proof.AppliedEntries),
            Math.Max(DeliveryAgeP95UpperBoundAuthoritySteps,
                proof.DeliveryAgeAuthoritySteps.WholeWindowP95UpperBound),
            Math.Max(DeliveryQueuedToProofP95Microseconds,
                proof.DeliveryQueuedToProofProcessed.WholeWindowP95Microseconds),
            Math.Max(SourceToProofP95UpperBoundAuthoritySteps,
                Math.Max(
                    freshness.Immediate.SourceToProofSteps.WholeWindowP95UpperBound,
                    Math.Max(
                        freshness.Near.SourceToProofSteps.WholeWindowP95UpperBound,
                        freshness.Far.SourceToProofSteps.WholeWindowP95UpperBound))),
            Math.Max(OutstandingHighWater, knowledge.Outstanding.HighWater));
    }

    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

internal readonly record struct P03DeltaRangeReuseCensus(
    ulong Requests,
    ulong UniqueRanges,
    ulong ReusableRequests,
    ulong RecordInstances,
    ulong UniqueRecords,
    ulong ReusableRecords,
    ulong CensusSourceReads);

internal readonly record struct P03DeltaRangeSignature(
    SpatialChunkKey Key,
    ulong BaseRevision,
    ulong CurrentRevision);

internal readonly record struct P03ChunkCpuAttribution(
    bool Enabled,
    ulong ScheduleTotalTicks,
    ulong ProofTotalTicks,
    ulong ReadinessSelectionCalls,
    ulong ReadinessSelectionTicks,
    ulong SchedulerCadenceCheckCalls,
    ulong SchedulerCadenceCheckTicks,
    ulong PreparedCandidateRequests,
    ulong PreparedCandidateBuilds,
    ulong PreparedCandidateLocalReuses,
    ulong PreparedCandidatePacketBoundaryCarries,
    ulong PreparedCandidateInvalidations,
    ulong PreparedCandidateRebuildsAfterInvalidation,
    ulong SubscriptionLookupCalls,
    ulong SubscriptionLookupComparisons,
    ulong SubscriptionLookupTicks,
    ulong SourceReadCalls,
    ulong SourceReadTicks,
    ulong RecipientCadenceCheckCalls,
    ulong RecipientCadenceCheckTicks,
    ulong DeltaCopyCalls,
    ulong DeltaCopyTicks,
    ulong DeltaRecordsCopied,
    ulong DeltaMaterializationCalls,
    ulong DeltaMaterializationTicks,
    ulong DeltaRecordsMaterialized,
    ulong PreparedValidationHashCalls,
    ulong PreparedValidationHashTicks,
    ulong PreparedRecordsValidated,
    ulong PreparedContentHashCalls,
    ulong CanonicalStandaloneEncodeCalls,
    ulong CanonicalStandaloneEncodeTicks,
    ulong CanonicalStandaloneBytes,
    ulong PreparedStandaloneWrites,
    ulong CanonicalReparseCalls,
    ulong CanonicalReparseRecords,
    ulong CanonicalReparseTicks,
    ulong BundlePreflightCalls,
    ulong BundlePreflightTicks,
    ulong BundleStagingCalls,
    ulong BundleStagingRecords,
    ulong BundleStagingTicks,
    ulong BundleRecordCopyCalls,
    ulong BundleRecordBytesCopied,
    ulong BundleRecordCopyTicks,
    ulong PacketBoundaryCarryCopyCalls,
    ulong PacketBoundaryCarryBytesCopied,
    ulong PacketBoundaryCarryCopyTicks,
    ulong BundleClearCalls,
    ulong BundleBytesCleared,
    ulong BundleClearTicks,
    ulong BundleEncodeCalls,
    ulong BundleEncodeEntries,
    ulong BundleEncodeRecords,
    ulong BundleEncodeTicks,
    ulong NetworkQueueCalls,
    ulong NetworkQueueTicks,
    ulong OutstandingBookkeepingCalls,
    ulong OutstandingBookkeepingTicks,
    ulong DetailedCensusTicks,
    ulong ProofLookupCalls,
    ulong ProofLookupComparisons,
    ulong ProofLookupTicks,
    ulong ProofSubscriptionLookupCalls,
    ulong ProofSubscriptionLookupComparisons,
    ulong ProofSubscriptionLookupTicks,
    ulong ProofTicketValidationCalls,
    ulong ProofTicketValidationTicks,
    ulong ProofValidationCalls,
    ulong ProofValidationTicks,
    ulong CursorAdvancementCalls,
    ulong CursorAdvancementTicks,
    ulong RecipientProofCommitCalls,
    ulong RecipientProofCommitTicks,
    ulong ProofRetirementCalls,
    ulong ProofRetirementTicks,
    ulong ProofObsoleteEntryChecks,
    ulong ProofObsoleteSameChunkChecks,
    ulong DetailedSlabCopies,
    ulong DetailedSlabClears);

internal sealed class P03ChunkCpuAttributionAccumulator
{
    internal bool Enabled;
    internal ulong ScheduleTotalTicks;
    internal ulong ProofTotalTicks;
    internal ulong ReadinessSelectionCalls;
    internal ulong ReadinessSelectionTicks;
    internal ulong SchedulerCadenceCheckCalls;
    internal ulong SchedulerCadenceCheckTicks;
    internal ulong PreparedCandidateRequests;
    internal ulong PreparedCandidateBuilds;
    internal ulong PreparedCandidateLocalReuses;
    internal ulong PreparedCandidatePacketBoundaryCarries;
    internal ulong PreparedCandidateInvalidations;
    internal ulong PreparedCandidateRebuildsAfterInvalidation;
    internal ulong SubscriptionLookupCalls;
    internal ulong SubscriptionLookupComparisons;
    internal ulong SubscriptionLookupTicks;
    internal ulong SourceReadCalls;
    internal ulong SourceReadTicks;
    internal ulong RecipientCadenceCheckCalls;
    internal ulong RecipientCadenceCheckTicks;
    internal ulong DeltaCopyCalls;
    internal ulong DeltaCopyTicks;
    internal ulong DeltaRecordsCopied;
    internal ulong DeltaMaterializationCalls;
    internal ulong DeltaMaterializationTicks;
    internal ulong DeltaRecordsMaterialized;
    internal ulong PreparedValidationHashCalls;
    internal ulong PreparedValidationHashTicks;
    internal ulong PreparedRecordsValidated;
    internal ulong PreparedContentHashCalls;
    internal ulong CanonicalStandaloneEncodeCalls;
    internal ulong CanonicalStandaloneEncodeTicks;
    internal ulong CanonicalStandaloneBytes;
    internal ulong PreparedStandaloneWrites;
    internal ulong CanonicalReparseCalls;
    internal ulong CanonicalReparseRecords;
    internal ulong CanonicalReparseTicks;
    internal ulong BundlePreflightCalls;
    internal ulong BundlePreflightTicks;
    internal ulong BundleStagingCalls;
    internal ulong BundleStagingRecords;
    internal ulong BundleStagingTicks;
    internal ulong BundleRecordCopyCalls;
    internal ulong BundleRecordBytesCopied;
    internal ulong BundleRecordCopyTicks;
    internal ulong PacketBoundaryCarryCopyCalls;
    internal ulong PacketBoundaryCarryBytesCopied;
    internal ulong PacketBoundaryCarryCopyTicks;
    internal ulong BundleClearCalls;
    internal ulong BundleBytesCleared;
    internal ulong BundleClearTicks;
    internal ulong BundleEncodeCalls;
    internal ulong BundleEncodeEntries;
    internal ulong BundleEncodeRecords;
    internal ulong BundleEncodeTicks;
    internal ulong NetworkQueueCalls;
    internal ulong NetworkQueueTicks;
    internal ulong OutstandingBookkeepingCalls;
    internal ulong OutstandingBookkeepingTicks;
    internal ulong DetailedCensusTicks;
    internal ulong ProofLookupCalls;
    internal ulong ProofLookupComparisons;
    internal ulong ProofLookupTicks;
    internal ulong ProofSubscriptionLookupCalls;
    internal ulong ProofSubscriptionLookupComparisons;
    internal ulong ProofSubscriptionLookupTicks;
    internal ulong ProofTicketValidationCalls;
    internal ulong ProofTicketValidationTicks;
    internal ulong ProofValidationCalls;
    internal ulong ProofValidationTicks;
    internal ulong CursorAdvancementCalls;
    internal ulong CursorAdvancementTicks;
    internal ulong RecipientProofCommitCalls;
    internal ulong RecipientProofCommitTicks;
    internal ulong ProofRetirementCalls;
    internal ulong ProofRetirementTicks;
    internal ulong ProofObsoleteEntryChecks;
    internal ulong ProofObsoleteSameChunkChecks;
    internal ulong DetailedSlabCopies;
    internal ulong DetailedSlabClears;

    internal void Add(in RemoteChunkCpuAttributionSnapshot value)
    {
        Enabled |= value.Enabled;
        PreparedCandidateRequests += value.PreparedCandidateRequests;
        PreparedCandidateBuilds += value.PreparedCandidateBuilds;
        PreparedCandidateLocalReuses += value.PreparedCandidateLocalReuses;
        PreparedCandidatePacketBoundaryCarries += value.PreparedCandidatePacketBoundaryCarries;
        PreparedCandidateInvalidations += value.PreparedCandidateInvalidations;
        PreparedCandidateRebuildsAfterInvalidation +=
            value.PreparedCandidateRebuildsAfterInvalidation;
        SubscriptionLookupCalls += value.SubscriptionLookupCalls;
        SubscriptionLookupComparisons += value.SubscriptionLookupComparisons;
        SubscriptionLookupTicks += value.SubscriptionLookupTicks;
        SourceReadCalls += value.Encoder.SourceReadCalls;
        SourceReadTicks += value.Encoder.SourceReadTicks;
        RecipientCadenceCheckCalls += value.RecipientRead.CadenceChecks;
        RecipientCadenceCheckTicks += value.RecipientRead.CadenceCheckTicks;
        DeltaCopyCalls += value.RecipientRead.DeltaCopyCalls;
        DeltaCopyTicks += value.RecipientRead.DeltaCopyTicks;
        DeltaRecordsCopied += value.RecipientRead.DeltaRecordsCopied;
        DeltaMaterializationCalls += value.Encoder.DeltaMaterializationCalls;
        DeltaMaterializationTicks += value.Encoder.DeltaMaterializationTicks;
        DeltaRecordsMaterialized += value.Encoder.DeltaRecordsMaterialized;
        PreparedValidationHashCalls += value.Encoder.PreparedValidationHashCalls;
        PreparedValidationHashTicks += value.Encoder.PreparedValidationHashTicks;
        PreparedRecordsValidated += value.Encoder.PreparedRecordsValidated;
        PreparedContentHashCalls += value.Encoder.PreparedContentHashCalls;
        CanonicalStandaloneEncodeCalls += value.Encoder.CanonicalStandaloneEncodeCalls;
        CanonicalStandaloneEncodeTicks += value.Encoder.CanonicalStandaloneEncodeTicks;
        CanonicalStandaloneBytes += value.Encoder.CanonicalStandaloneBytes;
        PreparedStandaloneWrites += value.Encoder.PreparedStandaloneWrites;
        CanonicalReparseCalls += value.CanonicalReparseCalls;
        CanonicalReparseRecords += value.CanonicalReparseRecords;
        CanonicalReparseTicks += value.CanonicalReparseTicks;
        BundlePreflightCalls += value.BundlePreflightCalls;
        BundlePreflightTicks += value.BundlePreflightTicks;
        BundleStagingCalls += value.BundleStagingCalls;
        BundleStagingRecords += value.BundleStagingRecords;
        BundleStagingTicks += value.BundleStagingTicks;
        BundleRecordCopyCalls += value.BundleRecordCopyCalls;
        BundleRecordBytesCopied += value.BundleRecordBytesCopied;
        BundleRecordCopyTicks += value.BundleRecordCopyTicks;
        PacketBoundaryCarryCopyCalls += value.PacketBoundaryCarryCopyCalls;
        PacketBoundaryCarryBytesCopied += value.PacketBoundaryCarryBytesCopied;
        PacketBoundaryCarryCopyTicks += value.PacketBoundaryCarryCopyTicks;
        BundleClearCalls += value.BundleClearCalls;
        BundleBytesCleared += value.BundleBytesCleared;
        BundleClearTicks += value.BundleClearTicks;
        BundleEncodeCalls += value.BundleEncodeCalls;
        BundleEncodeEntries += value.BundleEncodeEntries;
        BundleEncodeRecords += value.BundleEncodeRecords;
        BundleEncodeTicks += value.BundleEncodeTicks;
        NetworkQueueCalls += value.NetworkQueueCalls;
        NetworkQueueTicks += value.NetworkQueueTicks;
        OutstandingBookkeepingCalls += value.OutstandingBookkeepingCalls;
        OutstandingBookkeepingTicks += value.OutstandingBookkeepingTicks;
        DetailedCensusTicks += value.DetailedCensusTicks;
        ProofLookupCalls += value.ProofLookupCalls;
        ProofLookupComparisons += value.ProofLookupComparisons;
        ProofLookupTicks += value.ProofLookupTicks;
        ProofSubscriptionLookupCalls += value.ProofSubscriptionLookupCalls;
        ProofSubscriptionLookupComparisons += value.ProofSubscriptionLookupComparisons;
        ProofSubscriptionLookupTicks += value.ProofSubscriptionLookupTicks;
        ProofTicketValidationCalls += value.ProofTicketValidationCalls;
        ProofTicketValidationTicks += value.ProofTicketValidationTicks;
        ProofValidationCalls += value.Encoder.ProofValidationCalls;
        ProofValidationTicks += value.Encoder.ProofValidationTicks;
        CursorAdvancementCalls += value.Encoder.CursorAdvancementCalls;
        CursorAdvancementTicks += value.Encoder.CursorAdvancementTicks;
        RecipientProofCommitCalls += value.RecipientRead.AppliedProofCommitCalls;
        RecipientProofCommitTicks += value.RecipientRead.AppliedProofCommitTicks;
        ProofRetirementCalls += value.ProofRetirementCalls;
        ProofRetirementTicks += value.ProofRetirementTicks;
        ProofObsoleteEntryChecks += value.ProofObsoleteEntryChecks;
        ProofObsoleteSameChunkChecks += value.ProofObsoleteSameChunkChecks;
        DetailedSlabCopies += value.DetailedSlabCopies;
        DetailedSlabClears += value.DetailedSlabClears;
    }

    internal P03ChunkCpuAttribution Capture() => new(
        Enabled, ScheduleTotalTicks, ProofTotalTicks,
        ReadinessSelectionCalls, ReadinessSelectionTicks,
        SchedulerCadenceCheckCalls, SchedulerCadenceCheckTicks,
        PreparedCandidateRequests, PreparedCandidateBuilds,
        PreparedCandidateLocalReuses, PreparedCandidatePacketBoundaryCarries,
        PreparedCandidateInvalidations, PreparedCandidateRebuildsAfterInvalidation,
        SubscriptionLookupCalls, SubscriptionLookupComparisons, SubscriptionLookupTicks,
        SourceReadCalls, SourceReadTicks,
        RecipientCadenceCheckCalls, RecipientCadenceCheckTicks,
        DeltaCopyCalls, DeltaCopyTicks, DeltaRecordsCopied,
        DeltaMaterializationCalls, DeltaMaterializationTicks, DeltaRecordsMaterialized,
        PreparedValidationHashCalls, PreparedValidationHashTicks,
        PreparedRecordsValidated, PreparedContentHashCalls,
        CanonicalStandaloneEncodeCalls, CanonicalStandaloneEncodeTicks,
        CanonicalStandaloneBytes, PreparedStandaloneWrites,
        CanonicalReparseCalls, CanonicalReparseRecords, CanonicalReparseTicks,
        BundlePreflightCalls, BundlePreflightTicks,
        BundleStagingCalls, BundleStagingRecords, BundleStagingTicks,
        BundleRecordCopyCalls, BundleRecordBytesCopied, BundleRecordCopyTicks,
        PacketBoundaryCarryCopyCalls, PacketBoundaryCarryBytesCopied,
        PacketBoundaryCarryCopyTicks,
        BundleClearCalls, BundleBytesCleared, BundleClearTicks,
        BundleEncodeCalls, BundleEncodeEntries, BundleEncodeRecords, BundleEncodeTicks,
        NetworkQueueCalls, NetworkQueueTicks,
        OutstandingBookkeepingCalls, OutstandingBookkeepingTicks,
        DetailedCensusTicks,
        ProofLookupCalls, ProofLookupComparisons, ProofLookupTicks,
        ProofSubscriptionLookupCalls, ProofSubscriptionLookupComparisons,
        ProofSubscriptionLookupTicks,
        ProofTicketValidationCalls, ProofTicketValidationTicks,
        ProofValidationCalls, ProofValidationTicks,
        CursorAdvancementCalls, CursorAdvancementTicks,
        RecipientProofCommitCalls, RecipientProofCommitTicks,
        ProofRetirementCalls, ProofRetirementTicks,
        ProofObsoleteEntryChecks, ProofObsoleteSameChunkChecks,
        DetailedSlabCopies, DetailedSlabClears);
}

internal readonly record struct P03Metric(
    int Samples,
    double Mean,
    double P50,
    double P95,
    double P99,
    double Maximum)
{
    internal static P03Metric From(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return default;
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        return new(values.Count, sorted.Average(), Percentile(sorted, .50),
            Percentile(sorted, .95), Percentile(sorted, .99), sorted[^1]);
    }
    private static double Percentile(double[] sorted, double percentile)
    {
        int index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1,
            0, sorted.Length - 1);
        return sorted[index];
    }
}

internal sealed record Pf4eR2StagingMicroReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string GitSha,
    string Runtime,
    string OperatingSystem,
    long StopwatchFrequency,
    int Warmup,
    int IterationsPerSample,
    int Samples,
    Pf4eR2StagingMicroRow[] Rows);

internal sealed record Pf4eR2StagingMicroRow(
    string Shape,
    int Entries,
    int RecordsPerEntry,
    int EncodedBytes,
    P03Metric OldEncodeParseEncodeNanoseconds,
    P03Metric DirectStagingNanoseconds,
    long OldAllocatedBytes,
    long DirectAllocatedBytes,
    double MeanReductionPercent);

internal sealed class Pf4eR2StagingFixture
{
    private const ulong ApplicationEpoch = 0xF4E201;
    private const ulong PublicationEpoch = 0xF4E202;
    private const ulong Space = 1;
    private readonly int _entryCount;
    private readonly int _recordsPerEntry;
    private readonly bool _absence;
    private readonly byte[] _standalone = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly byte[] _oldBytes = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly byte[] _directBytes = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly RemoteChunkDeltaRecord[] _source;
    private readonly RemoteChunkDeltaRecord[] _oldStaging;
    private readonly RemoteChunkDeltaRecord[] _directStaging;
    private readonly RemoteDirtyChunkBundleEntry[] _oldEntries;
    private readonly RemoteDirtyChunkBundleEntry[] _directEntries;
    private int _oldWritten;
    private int _directWritten;

    internal Pf4eR2StagingFixture(int entryCount, int recordsPerEntry, bool absence)
    {
        _entryCount = entryCount;
        _recordsPerEntry = recordsPerEntry;
        _absence = absence;
        int records = checked(entryCount * recordsPerEntry);
        _source = new RemoteChunkDeltaRecord[records];
        _oldStaging = new RemoteChunkDeltaRecord[records];
        _directStaging = new RemoteChunkDeltaRecord[records];
        _oldEntries = new RemoteDirtyChunkBundleEntry[entryCount];
        _directEntries = new RemoteDirtyChunkBundleEntry[entryCount];
        for (int entry = 0; entry < entryCount; entry++)
        for (int record = 0; record < recordsPerEntry; record++)
        {
            int offset = checked(entry * recordsPerEntry + record);
            _source[offset] = new RemoteChunkDeltaRecord(
                RemoteChunkPublicOperation.Present, RemoteChunkPublicFieldMask.Complete,
                checked((uint)(record + 1)), 1, checked((ulong)offset + 1), 1,
                checked((ushort)(100 + record)), checked((ushort)(200 + record)), 0, 0,
                1, 2);
        }
        _ = OldCycle();
        _ = DirectCycle();
        CanonicalEquivalent = _oldWritten == _directWritten &&
            _oldBytes.AsSpan(0, _oldWritten).SequenceEqual(
                _directBytes.AsSpan(0, _directWritten));
        EncodedBytes = _directWritten;
        if (!_absence && entryCount > 1 && EncodedBytes > 1_405)
            throw new InvalidOperationException("PF4E-R2 near-MTU fixture exceeds 1,405 bytes.");
    }

    internal int EncodedBytes { get; }
    internal bool CanonicalEquivalent { get; }

    internal ulong OldCycle()
    {
        if (_absence)
        {
            if (!UltraSimGameProtocol.TryWriteChunkAbsence(
                    _oldBytes, ApplicationEpoch, PublicationEpoch, 1, Space, 0, 0,
                    out _oldWritten) ||
                !UltraSimGameProtocol.TryReadChunkAbsence(
                    _oldBytes.AsSpan(0, _oldWritten), out RemoteChunkAbsenceMessage decoded))
                throw new InvalidOperationException("PF4E-R2 old absence cycle failed.");
            return decoded.DeliveryId ^ checked((ulong)_oldWritten);
        }
        for (int entry = 0; entry < _entryCount; entry++)
        {
            ReadOnlySpan<RemoteChunkDeltaRecord> source =
                _source.AsSpan(entry * _recordsPerEntry, _recordsPerEntry);
            if (!UltraSimGameProtocol.TryWriteChunkDelta(
                    _standalone, ApplicationEpoch, PublicationEpoch,
                    checked((ulong)entry + 1), Space, entry, 0, 1, 2, 1, source,
                    out int standaloneWritten) ||
                !UltraSimGameProtocol.TryReadChunkDelta(
                    _standalone.AsSpan(0, standaloneWritten), out RemoteChunkDeltaHeader header,
                    _oldStaging.AsSpan(entry * _recordsPerEntry, _recordsPerEntry),
                    out int decoded) || decoded != _recordsPerEntry)
                throw new InvalidOperationException("PF4E-R2 old delta cycle failed.");
            _oldEntries[entry] = new RemoteDirtyChunkBundleEntry(
                RemoteChunkPublicationKind.Delta, header.DeliveryId, Space, entry, 0,
                1, 2, 1, header.ContentHash, checked((ushort)decoded),
                entry * _recordsPerEntry);
            if (_entryCount == 1)
            {
                _standalone.AsSpan(0, standaloneWritten).CopyTo(_oldBytes);
                _oldWritten = standaloneWritten;
            }
        }
        if (_entryCount > 1 && !UltraSimGameProtocol.TryWriteDirtyChunkBundle(
                _oldBytes, ApplicationEpoch, PublicationEpoch, 1, _oldEntries,
                [], _oldStaging, out _oldWritten))
            throw new InvalidOperationException("PF4E-R2 old bundle cycle failed.");
        return checked((ulong)_oldWritten) ^ _oldEntries[0].ContentHash;
    }

    internal ulong DirectCycle()
    {
        if (_absence)
        {
            if (!UltraSimGameProtocol.TryWriteChunkAbsence(
                    _directBytes, ApplicationEpoch, PublicationEpoch, 1, Space, 0, 0,
                    out _directWritten))
                throw new InvalidOperationException("PF4E-R2 direct absence cycle failed.");
            return checked((ulong)_directWritten) ^ 1UL;
        }
        for (int entry = 0; entry < _entryCount; entry++)
        {
            ReadOnlySpan<RemoteChunkDeltaRecord> source =
                _source.AsSpan(entry * _recordsPerEntry, _recordsPerEntry);
            if (!UltraSimGameProtocol.TryGetChunkDeltaContentHash(
                    PublicationEpoch, Space, entry, 0, 1, 2, 1, source, out ulong hash))
                throw new InvalidOperationException("PF4E-R2 direct validation failed.");
            source.CopyTo(_directStaging.AsSpan(entry * _recordsPerEntry));
            _directEntries[entry] = new RemoteDirtyChunkBundleEntry(
                RemoteChunkPublicationKind.Delta, checked((ulong)entry + 1), Space,
                entry, 0, 1, 2, 1, hash, checked((ushort)_recordsPerEntry),
                entry * _recordsPerEntry);
        }
        if (_entryCount == 1)
        {
            if (!UltraSimGameProtocol.TryWriteChunkDelta(
                    _directBytes, ApplicationEpoch, PublicationEpoch, 1, Space, 0, 0,
                    1, 2, 1, _directStaging, out _directWritten))
                throw new InvalidOperationException("PF4E-R2 direct standalone failed.");
        }
        else if (!UltraSimGameProtocol.TryWritePreparedDirtyChunkBundle(
                     _directBytes, ApplicationEpoch, PublicationEpoch, 1,
                     _directEntries, _directStaging, out _directWritten))
            throw new InvalidOperationException("PF4E-R2 direct bundle cycle failed.");
        return checked((ulong)_directWritten) ^ _directEntries[0].ContentHash;
    }
}
