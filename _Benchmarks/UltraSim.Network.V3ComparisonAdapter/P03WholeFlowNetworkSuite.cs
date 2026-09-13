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
}

internal enum P03MutationPattern : byte
{
    Sequential = 1,
    OneChunkGroup = 2,
    EightChunkStripe = 3,
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
    P03MutationPattern MutationPattern = P03MutationPattern.Sequential)
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
    private readonly bool _gameDetailedAttribution;
    private readonly NetworkProtocolLimits _protocolLimits;
    private readonly SharedUdpProtocolHost _host;
    private readonly SharedUdpProtocolSession[] _sessions;
    private readonly P03RemoteProfileAdapter _adapter;
    private readonly long[] _x;
    private readonly long[] _z;
    private readonly ulong[] _versions;
    private readonly List<double> _tick = new();
    private readonly List<double> _authority = new();
    private readonly List<double> _schedule = new();
    private readonly List<double> _pump = new();
    private readonly List<double> _serverApplication = new();
    private readonly List<double> _clientApplication = new();
    private readonly List<double> _networkAndRemoteReplication = new();
    private readonly List<double> _serverRole = new();
    private readonly List<double> _clientRole = new();
    private bool _disposed;
    private long _tickServerApplication;
    private long _tickClientApplication;
    private long _tickClientNetwork;

    internal P03WholeFlowHarness(
        P03NetworkWorkload workload,
        P03NetworkProfile profile,
        int frameCap,
        bool stageTimingEnabled = true,
        bool gameDetailedAttribution = false,
        RemoteChunkBundleCompressionPolicy compression = default)
    {
        _workload = workload;
        _profile = profile;
        _frameCap = frameCap;
        _stageTimingEnabled = stageTimingEnabled;
        _gameDetailedAttribution = gameDetailedAttribution;
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
            SharedUdpSocketIoMode.PlatformSendReceiveBatch, 32, 32);
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
                workload, _sessions, _x, _z, _versions, s_content, compression),
            P03NetworkProfile.DirtyChunkSpatialFanout => new P03DirtyChunkRemoteAdapter(
                workload, _sessions, _x, _z, _versions, s_content),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
        _adapter.ConfigureTelemetry(gameDetailedAttribution
            ? GameTelemetryLevel.Detailed
            : GameTelemetryLevel.Off);
    }

    internal P03WholeFlowRow Run(int warmup, int ticks)
    {
        DrainToConvergence(0, 16_384);
        for (int tick = 1; tick <= warmup; tick++) RunTick(tick, measure: false);
        DrainToConvergence(checked((ulong)warmup + 1), 4_096);
        ResetMeasurement(ticks);
        P03TransportTotals before = CaptureTransport();
        SharedUdpListenerSnapshot listenerBefore = _host.GetSnapshot().Listener;
        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        for (int tick = 1; tick <= ticks; tick++) RunTick(warmup + tick, measure: true);
        DrainToConvergence(checked((ulong)(warmup + ticks + 1)), 4_096);
        long allocated = GC.GetTotalAllocatedBytes(false) - allocatedBefore;
        P03TransportTotals after = CaptureTransport();
        SharedUdpListenerSnapshot listenerAfter = _host.GetSnapshot().Listener;
        _adapter.Validate();
        P03ProfileCounters profile = _adapter.GetCounters();
        P03ProtocolTiming protocol = CaptureProtocolTiming();
        SharedUdpProtocolHostSnapshot hostSnapshot = _host.GetSnapshot();
        long profileServerReserved = _adapter.GetServerReservedBytes();
        long profileClientReserved = _adapter.GetClientReservedBytes();
        P03TransportSummary transport = P03TransportSummary.Delta(
            after, before, listenerAfter, listenerBefore, _frameCap);
        P03NormalizedCost normalized = P03NormalizedCost.Calculate(
            _workload.Recipients, ticks, _networkAndRemoteReplication, _serverRole,
            transport, profile);
        return new P03WholeFlowRow(
            0, _workload.Id, _profile.ToString(), _frameCap,
            _workload.Recipients, _workload.Entities, _workload.DirtyEntitiesPerTick,
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
                P03Metric.From(_tick), P03Metric.From(_authority), P03Metric.From(_schedule),
                P03Metric.From(_pump), P03Metric.From(_serverApplication),
                P03Metric.From(_clientApplication),
                P03Metric.From(_networkAndRemoteReplication), P03Metric.From(_serverRole),
                P03Metric.From(_clientRole), protocol),
            transport, profile, _adapter.SemanticDigest(), allocated,
            checked(profileServerReserved + profileClientReserved),
            profileServerReserved, profileClientReserved,
            hostSnapshot.KnownReservedBytes, hostSnapshot.KnownServerReservedBytes,
            hostSnapshot.KnownClientReservedBytes,
            _adapter.IsConverged, "", normalized);
    }

    private void RunTick(int tick, bool measure)
    {
        P03ProtocolTiming protocolBefore = measure ? CaptureProtocolTiming() : default;
        long totalStarted = measure ? Stopwatch.GetTimestamp() : 0;
        long started = measure ? Stopwatch.GetTimestamp() : 0;
        Mutate(tick);
        _adapter.ApplyAuthority(tick, _workload.MoveRecipients ? _workload.DirtyEntitiesPerTick : 0);
        double authority = measure ? Elapsed(started) : 0;

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
        double serverApp = ToMilliseconds(_tickServerApplication);
        double clientApp = ToMilliseconds(_tickClientApplication);
        double pump = ToMilliseconds(pumpTicks);
        _tick.Add(Elapsed(totalStarted));
        _authority.Add(authority);
        _schedule.Add(schedule);
        _pump.Add(pump);
        _serverApplication.Add(serverApp);
        _clientApplication.Add(clientApp);
        _networkAndRemoteReplication.Add(ToMilliseconds(networkReplicationTicks));
        P03ProtocolTiming current = CaptureProtocolTiming();
        _serverRole.Add(authority + schedule + serverApp +
            Math.Max(0, current.ServerMilliseconds - protocolBefore.ServerMilliseconds));
        _clientRole.Add(clientApp + ToMilliseconds(_tickClientNetwork) +
            Math.Max(0, current.ClientMilliseconds - protocolBefore.ClientMilliseconds));
    }

    private void Mutate(int tick)
    {
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
        for (int index = 0; index < _sessions.Length; index++)
        {
            long started = Stopwatch.GetTimestamp();
            while (_sessions[index].Server.TryPeekReceived(
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

    private void DrainToConvergence(ulong step, int guard)
    {
        for (int round = 0; round < guard; round++)
        {
            _adapter.Schedule(step + checked((ulong)round));
            for (int pass = 0; pass < 4; pass++) _ = PumpAndDispatch();
            if (_adapter.IsConverged) return;
            if ((round & 31) == 31) Thread.Yield();
        }
        throw new InvalidOperationException(
            $"{_profile}/{_workload.Id} did not converge: {_adapter.DescribeState()}.");
    }

    private void ResetMeasurement(int ticks)
    {
        _tick.Clear(); _authority.Clear(); _schedule.Clear(); _pump.Clear();
        _serverApplication.Clear(); _clientApplication.Clear();
        _networkAndRemoteReplication.Clear(); _serverRole.Clear(); _clientRole.Clear();
        _tick.EnsureCapacity(ticks); _authority.EnsureCapacity(ticks);
        _schedule.EnsureCapacity(ticks); _pump.EnsureCapacity(ticks);
        _serverApplication.EnsureCapacity(ticks); _clientApplication.EnsureCapacity(ticks);
        _networkAndRemoteReplication.EnsureCapacity(ticks);
        _serverRole.EnsureCapacity(ticks); _clientRole.EnsureCapacity(ticks);
        for (int index = 0; index < _sessions.Length; index++)
        {
            _sessions[index].Server.ResetStageTiming(_stageTimingEnabled);
            _sessions[index].Client.ResetStageTiming(_stageTimingEnabled);
        }
        _host.ResetListenerStageTiming(_stageTimingEnabled);
        _adapter.ResetCounters();
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
            ToMilliseconds(client.EmitTicks + client.InboundTicks));
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
        if (_workload.EntityLayout == P03EntityLayout.TwentyEntitiesPerChunk)
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
    internal abstract void Schedule(ulong step);
    internal abstract bool ProcessServer(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload);
    internal abstract bool ProcessClient(int recipient, in NetworkDeliveredMessage message,
        ReadOnlySpan<byte> payload);
    internal virtual void ProgressServers() { }
    internal virtual void ConfigureTelemetry(GameTelemetryLevel level) { }
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
    protected long RecipientX(int recipient) => X[recipient % X.Length];
    protected long RecipientZ(int recipient) => Z[recipient % Z.Length];
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
        WorldContentFingerprint content)
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
            PublicationEpoch.Create(),
            new ChunkPublicationLimits(workload.Entities,
                checked(workload.Entities * 2), 64,
                Math.Max(65_536, checked(workload.Entities * 64))));
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
    private readonly OfflineDirectChunkSubscriptionChange[] _changes;
    private readonly ChunkPublicationWork[] _dueWorks;
    private readonly RemoteChunkWorldStateScheduleStatus[] _dueStatuses;
    private readonly int[] _dueCounts;
    private readonly P03DeltaRangeSignature[] _rangeSignatures;
    private readonly int[] _rangeRecordCounts;
    private readonly WorldPublicDeltaRecord[] _rangeCensusScratch =
        new WorldPublicDeltaRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];
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
    private GameTelemetryLevel _telemetryLevel;
    private ulong _deltaRangeRequests;
    private ulong _uniqueDeltaRanges;
    private ulong _reusableDeltaRangeRequests;
    private ulong _deltaRangeRecordInstances;
    private ulong _uniqueDeltaRangeRecords;
    private ulong _reusableDeltaRangeRecords;
    private ulong _censusSourceDeltaReads;
    private const int MaximumDuePerRecipient = 64;

    internal P03ChunkPublicationRemoteAdapter(P03NetworkWorkload workload,
        SharedUdpProtocolSession[] sessions, long[] x, long[] z, ulong[] versions,
        WorldContentFingerprint content,
        RemoteChunkBundleCompressionPolicy compression = default)
        : base(workload, sessions, x, z, versions, content)
    {
        int subscriptions = workload.Id.StartsWith("sparse", StringComparison.Ordinal) ? 128 : 1_024;
        _recipients = new OfflineDirectChunkRecipient[workload.Recipients];
        _recipientIds = new ChunkReplicationRecipientId[workload.Recipients];
        _scheduler = new ChunkPublicationWorkScheduler(Source,
            new ChunkPublicationWorkSchedulerLimits(workload.Recipients,
                checked(workload.Recipients * subscriptions)));
        _servers = new RemoteChunkWorldStateServer[workload.Recipients];
        _clients = new RemoteChunkWorldStateClient[workload.Recipients];
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
            _servers[index] = new RemoteChunkWorldStateServer(
                sessions[index].Server, recipient, Source, ApplicationEpoch,
                new RemoteChunkWorldStateTransportLimits(128, 1_024, compression));
            _clients[index] = new RemoteChunkWorldStateClient(
                sessions[index].Client, Caches[index], ApplicationEpoch, 64, 256);
        }
        while (_scheduler.TryDequeueDue(0, out _)) { }
    }

    internal override void ConfigureTelemetry(GameTelemetryLevel level)
    {
        _telemetryLevel = level;
        _scheduler.ConfigureTelemetry(level);
        for (int index = 0; index < _servers.Length; index++)
            _servers[index].ConfigureTelemetry(level);
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
        P03WholeFlowHarness.Require(_scheduler.ObserveCommit(Source) ==
            ChunkPublicationCommitObservationStatus.Observed,
            "Chunk scheduler missed an authoritative commit.");
    }

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
        if (_telemetryLevel == GameTelemetryLevel.Detailed)
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
                    out int bundlesQueued);
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
                if (bundlesQueued != 0)
                    _bundleEntries += checked((ulong)successful);
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
        ChunkPublicationWorkSchedulerSnapshot scheduler = _scheduler.GetSnapshot();
        ChunkPublicationStoreSnapshot source = Source.GetSnapshot();
        P03CompressionCounters compression = default;
        for (int index = 0; index < _servers.Length; index++)
        {
            RemoteChunkCompressionCensusSnapshot value =
                _servers[index].GetTimingSnapshot().Knowledge.Compression;
            compression = compression.Add(value);
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
    }

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
        long value = checked(Source.GetSnapshot().ReservedBytes + _scheduler.GetSnapshot().ReservedBytes);
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

internal readonly record struct P03WholeFlowScope(
    string Included,
    string Excluded,
    string AggregationExperiment);

internal readonly record struct P03WholeFlowRow(
    int ExecutionOrder,
    string Workload,
    string Profile,
    int MaximumUnreliableFramesPerDatagram,
    int Recipients,
    int Entities,
    int DirtyEntitiesPerTick,
    P03CadenceSummary Cadence,
    P03WholeFlowTiming Timing,
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
    P03NormalizedCost Normalized);

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
    P03Metric Authority,
    P03Metric GameScheduleAndCodecQueue,
    P03Metric CombinedProtocolPumpWall,
    P03Metric ServerGameProofApply,
    P03Metric ClientGameDecodeApplyAndProofQueue,
    P03Metric NetworkAndRemoteReplication,
    P03Metric ServerRole,
    P03Metric ClientRole,
    P03ProtocolTiming Protocol);

internal readonly record struct P03ProtocolTiming(
    P03ProtocolStage Server,
    P03ProtocolStage Client,
    double ServerMilliseconds,
    double ClientMilliseconds);

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
            gamePayloadBytes, frameHeaders, plaintext, decrypted, wireDatagrams, wireBytes,
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
    ulong PendingMembershipsExamined = 0,
    ulong SchedulerCadenceDeferrals = 0,
    ulong SourceDeltaReads = 0,
    ulong SourceSnapshotReads = 0,
    ulong SourceSnapshotFallbacks = 0,
    P03ChunkCpuAttribution ChunkCpu = default,
    P03DeltaRangeReuseCensus DeltaRangeReuse = default,
    P03CompressionCounters Compression = default);

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
    long MaximumCompressionMicroseconds)
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
        Math.Max(MaximumCompressionMicroseconds, value.Compression.MaximumMicroseconds));

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
    ulong CanonicalStandaloneEncodeCalls,
    ulong CanonicalStandaloneEncodeTicks,
    ulong CanonicalStandaloneBytes,
    ulong CanonicalReparseCalls,
    ulong CanonicalReparseRecords,
    ulong CanonicalReparseTicks,
    ulong BundlePreflightCalls,
    ulong BundlePreflightTicks,
    ulong BundleStagingCalls,
    ulong BundleStagingRecords,
    ulong BundleStagingTicks,
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
    ulong ProofTicketValidationCalls,
    ulong ProofTicketValidationTicks,
    ulong ProofValidationCalls,
    ulong ProofValidationTicks,
    ulong CursorAdvancementCalls,
    ulong CursorAdvancementTicks,
    ulong RecipientProofCommitCalls,
    ulong RecipientProofCommitTicks,
    ulong ProofRetirementCalls,
    ulong ProofRetirementTicks);

internal sealed class P03ChunkCpuAttributionAccumulator
{
    internal bool Enabled;
    internal ulong ScheduleTotalTicks;
    internal ulong ProofTotalTicks;
    internal ulong ReadinessSelectionCalls;
    internal ulong ReadinessSelectionTicks;
    internal ulong SchedulerCadenceCheckCalls;
    internal ulong SchedulerCadenceCheckTicks;
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
    internal ulong CanonicalStandaloneEncodeCalls;
    internal ulong CanonicalStandaloneEncodeTicks;
    internal ulong CanonicalStandaloneBytes;
    internal ulong CanonicalReparseCalls;
    internal ulong CanonicalReparseRecords;
    internal ulong CanonicalReparseTicks;
    internal ulong BundlePreflightCalls;
    internal ulong BundlePreflightTicks;
    internal ulong BundleStagingCalls;
    internal ulong BundleStagingRecords;
    internal ulong BundleStagingTicks;
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

    internal void Add(in RemoteChunkCpuAttributionSnapshot value)
    {
        Enabled |= value.Enabled;
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
        CanonicalStandaloneEncodeCalls += value.Encoder.CanonicalStandaloneEncodeCalls;
        CanonicalStandaloneEncodeTicks += value.Encoder.CanonicalStandaloneEncodeTicks;
        CanonicalStandaloneBytes += value.Encoder.CanonicalStandaloneBytes;
        CanonicalReparseCalls += value.CanonicalReparseCalls;
        CanonicalReparseRecords += value.CanonicalReparseRecords;
        CanonicalReparseTicks += value.CanonicalReparseTicks;
        BundlePreflightCalls += value.BundlePreflightCalls;
        BundlePreflightTicks += value.BundlePreflightTicks;
        BundleStagingCalls += value.BundleStagingCalls;
        BundleStagingRecords += value.BundleStagingRecords;
        BundleStagingTicks += value.BundleStagingTicks;
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
    }

    internal P03ChunkCpuAttribution Capture() => new(
        Enabled, ScheduleTotalTicks, ProofTotalTicks,
        ReadinessSelectionCalls, ReadinessSelectionTicks,
        SchedulerCadenceCheckCalls, SchedulerCadenceCheckTicks,
        SubscriptionLookupCalls, SubscriptionLookupComparisons, SubscriptionLookupTicks,
        SourceReadCalls, SourceReadTicks,
        RecipientCadenceCheckCalls, RecipientCadenceCheckTicks,
        DeltaCopyCalls, DeltaCopyTicks, DeltaRecordsCopied,
        DeltaMaterializationCalls, DeltaMaterializationTicks, DeltaRecordsMaterialized,
        CanonicalStandaloneEncodeCalls, CanonicalStandaloneEncodeTicks,
        CanonicalStandaloneBytes,
        CanonicalReparseCalls, CanonicalReparseRecords, CanonicalReparseTicks,
        BundlePreflightCalls, BundlePreflightTicks,
        BundleStagingCalls, BundleStagingRecords, BundleStagingTicks,
        BundleEncodeCalls, BundleEncodeEntries, BundleEncodeRecords, BundleEncodeTicks,
        NetworkQueueCalls, NetworkQueueTicks,
        OutstandingBookkeepingCalls, OutstandingBookkeepingTicks,
        DetailedCensusTicks,
        ProofLookupCalls, ProofLookupComparisons, ProofLookupTicks,
        ProofTicketValidationCalls, ProofTicketValidationTicks,
        ProofValidationCalls, ProofValidationTicks,
        CursorAdvancementCalls, CursorAdvancementTicks,
        RecipientProofCommitCalls, RecipientProofCommitTicks,
        ProofRetirementCalls, ProofRetirementTicks);
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
