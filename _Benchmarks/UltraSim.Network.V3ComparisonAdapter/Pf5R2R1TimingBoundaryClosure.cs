using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using UltraSim.Game.Replication;
using UltraSim.Game.World;

internal static class Pf5R2R1TimingBoundaryClosure
{
    private const string MeasurementBase =
        "76a2659fcad02a7ca8bcbd16c6f3a7aca497d893";
    private const string CorrectnessCandidate =
        "992fc541460f18534da9036c73cb711277de11ce";

    internal static void Run(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 30);
        int ticks = ReadInt(args, "--ticks", 270);
        int samples = ReadInt(args, "--samples", 3);
        int[] populations = ParsePopulations(
            ReadText(args, "--populations") ?? "64,96,128,160,192,256");
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        if (focused)
        {
            populations = [64, 128];
            warmup = Math.Min(warmup, 12);
            ticks = Math.Min(ticks, 45);
            samples = 1;
        }
        if (warmup < 1 || ticks < 10 || samples is < 1 or > 9 ||
            populations.Any(value => value is < 2 or > 512))
            throw new ArgumentOutOfRangeException(nameof(args));

        Prime(PreparedChunkPublicationReadModel.HistoryDerivedComparison);
        Prime(PreparedChunkPublicationReadModel.PreparedLatestState);

        P03WholeFlowRow boundary = RunHarness(
            focused ? 16 : 64,
            PreparedChunkPublicationReadModel.PreparedLatestState,
            warmup,
            ticks,
            authorityThroughNetworkTimingEnabled: true);
        VerifyBoundary(boundary, ticks);

        P03WholeFlowRow overheadBefore = RunHarness(
            focused ? 16 : 64,
            PreparedChunkPublicationReadModel.PreparedLatestState,
            warmup,
            ticks,
            authorityThroughNetworkTimingEnabled: false);
        P03WholeFlowRow overheadEnabled = RunHarness(
            focused ? 16 : 64,
            PreparedChunkPublicationReadModel.PreparedLatestState,
            warmup,
            ticks,
            authorityThroughNetworkTimingEnabled: true);
        P03WholeFlowRow overheadAfter = RunHarness(
            focused ? 16 : 64,
            PreparedChunkPublicationReadModel.PreparedLatestState,
            warmup,
            ticks,
            authorityThroughNetworkTimingEnabled: false);
        RequireEquivalent(overheadBefore, overheadEnabled);
        RequireEquivalent(overheadEnabled, overheadAfter);
        double overheadControlP95 = (overheadBefore.Timing.NetworkAndRemoteReplication.P95 +
            overheadAfter.Timing.NetworkAndRemoteReplication.P95) / 2d;
        var overhead = new Pf5R2R1TimingOverhead(
            overheadBefore.Timing.NetworkAndRemoteReplication.P95,
            overheadEnabled.Timing.NetworkAndRemoteReplication.P95,
            overheadAfter.Timing.NetworkAndRemoteReplication.P95,
            overheadControlP95,
            overheadControlP95 == 0 ? 0 :
                (overheadEnabled.Timing.NetworkAndRemoteReplication.P95 - overheadControlP95) /
                overheadControlP95 * 100d,
            "The direct combined interval reuses the existing whole-tick start and endpoint " +
            "timestamp; its timed path adds no Stopwatch read and only one retained-list append " +
            "after the narrow Network sample has been captured.");

        var rows = new List<Pf5R2R1ComparisonRow>();
        for (int populationIndex = 0; populationIndex < populations.Length; populationIndex++)
        {
            int population = populations[populationIndex];
            int count = population <= 128 ? samples : 1;
            for (int sample = 0; sample < count; sample++)
            {
                PreparedChunkPublicationReadModel first = ((populationIndex + sample) & 1) == 0
                    ? PreparedChunkPublicationReadModel.HistoryDerivedComparison
                    : PreparedChunkPublicationReadModel.PreparedLatestState;
                RunOne(population, sample + 1, first);
                RunOne(population, sample + 1, first ==
                    PreparedChunkPublicationReadModel.HistoryDerivedComparison
                        ? PreparedChunkPublicationReadModel.PreparedLatestState
                        : PreparedChunkPublicationReadModel.HistoryDerivedComparison);
            }
        }

        foreach (IGrouping<int, Pf5R2R1ComparisonRow> population in
                 rows.GroupBy(value => value.Population))
        foreach (IGrouping<int, Pf5R2R1ComparisonRow> sample in
                 population.GroupBy(value => value.Sample))
        {
            Pf5R2R1ComparisonRow oldRow = sample.Single(value => !value.Prepared);
            Pf5R2R1ComparisonRow newRow = sample.Single(value => value.Prepared);
            RequireEquivalent(oldRow.Flow, newRow.Flow);
            VerifyBoundary(newRow.Flow, ticks);
        }

        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r2-r1-timing-boundary.json";
        var report = new Pf5R2R1TimingReport(
            "ultrasim-concept-demo-pf5-r2-r1-timing-boundary-v2",
            DateTimeOffset.UtcNow,
            MeasurementBase,
            ReadGitSha(),
            CorrectnessCandidate,
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            warmup,
            ticks,
            samples,
            "AuthorityMutation is measured from immediately before workload mutation through " +
            "ApplyAuthority, including ChunkPublication commit and prepared source creation.",
            "NetworkAndRemoteReplication begins after ApplyAuthority and accumulates the existing " +
            "server-owned scheduling, server Network, and server proof-application work.",
            "AuthorityThroughNetworkAndRemoteReplication is one direct elapsed interval per tick " +
            "from immediately before authoritative mutation through the endpoint after the final " +
            "remote transport/application/proof pump. It includes the interleaved client-side " +
            "transport and semantic work required to reach that endpoint and is never reconstructed " +
            "from phase durations or aggregate percentiles.",
            "ChunkPublication; Message129WhenBeneficial; selective message148 LZ4 " +
            "canonical>=384/saving>=32; Immediate/Near/Far 1/2/4; CrossLane cap 2; " +
            "Game manifest v29; transport revision 2; authority 60 Hz; participant cap 64",
            boundary.TimingBoundary,
            overhead,
            rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
        return;

        void RunOne(int population, int sample, PreparedChunkPublicationReadModel model)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            P03WholeFlowRow flow = RunHarness(
                population, model, warmup, ticks,
                authorityThroughNetworkTimingEnabled: true);
            PreparedChunkPublicationSnapshot prepared = flow.ProfileCounters.PreparedPublication;
            if (model == PreparedChunkPublicationReadModel.PreparedLatestState)
                Require(prepared.Selected && prepared.HistoryDerivedSteadyStateCalls == 0,
                    "PF5-R2-R1 selected path invoked history reconstruction.");
            else
                Require(!prepared.Selected && prepared.HistoryDerivedSteadyStateCalls != 0,
                    "PF5-R2-R1 old control did not invoke history reconstruction.");
            rows.Add(new Pf5R2R1ComparisonRow(
                population,
                sample,
                model == PreparedChunkPublicationReadModel.PreparedLatestState,
                flow));
            Console.WriteLine($"PF5-R2-R1 {population} " +
                $"{(model == PreparedChunkPublicationReadModel.PreparedLatestState ? "prepared" : "history")}: " +
                $"authority/network/combined p95 " +
                $"{flow.Timing.AuthorityMutation.P95:F3}/" +
                $"{flow.Timing.NetworkAndRemoteReplication.P95:F3}/" +
                $"{flow.Timing.AuthorityThroughNetworkAndRemoteReplication.P95:F3} ms");
        }
    }

    private static P03WholeFlowRow RunHarness(
        int population,
        PreparedChunkPublicationReadModel model,
        int warmup,
        int ticks,
        bool authorityThroughNetworkTimingEnabled)
    {
        P03NetworkWorkload workload = P03NetworkWorkload.ChunkProductionDense(population)
            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
        using var harness = new P03WholeFlowHarness(
            workload,
            P03NetworkProfile.ChunkPublication,
            2,
            stageTimingEnabled: false,
            gameDetailedAttribution: false,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            historyAudit: false,
            legacyRangeCensus: false,
            authorityThroughNetworkTimingEnabled: authorityThroughNetworkTimingEnabled,
            chunkReadModel: model);
        return harness.Run(warmup, ticks);
    }

    private static void Prime(PreparedChunkPublicationReadModel model)
    {
        _ = RunHarness(16, model, 12, 12, authorityThroughNetworkTimingEnabled: true);
    }

    private static void VerifyBoundary(in P03WholeFlowRow row, int ticks)
    {
        P03TimingBoundaryEvidence boundary = row.TimingBoundary;
        Require(boundary.Captured &&
                boundary.CombinedStartOrder < boundary.AuthorityStartOrder &&
                boundary.AuthorityStartOrder < boundary.PreparedSourceObservedOrder &&
                boundary.PreparedSourceObservedOrder < boundary.AuthorityEndOrder &&
                boundary.AuthorityEndOrder < boundary.NetworkStartOrder &&
                boundary.NetworkStartOrder < boundary.NetworkEndOrder &&
                boundary.NetworkEndOrder < boundary.CombinedEndOrder &&
                boundary.PreparedSourcePublicationsObserved != 0 &&
                boundary.CombinedFromSingleElapsedInterval &&
                !boundary.CombinedFromPhaseDurations,
            "PF5-R2-R1 source preparation escaped the direct per-tick combined boundary.");
        Require(row.Timing.AuthorityMutation.Samples == ticks &&
                row.Timing.NetworkAndRemoteReplication.Samples == ticks &&
                row.Timing.AuthorityThroughNetworkAndRemoteReplication.Samples == ticks,
            "PF5-R2-R1 timing sample counts diverged.");
        Require(Math.Abs(row.Timing.Tick.Mean -
                row.Timing.AuthorityThroughNetworkAndRemoteReplication.Mean) <= 0.000_001 &&
                row.Timing.AuthorityThroughNetworkAndRemoteReplication.Mean >=
                    row.Timing.AuthorityMutation.Mean +
                    row.Timing.NetworkAndRemoteReplication.Mean,
            "PF5-R2-R1 combined sample was not the direct authority-through-pump interval.");
    }

    private static void RequireEquivalent(in P03WholeFlowRow left, in P03WholeFlowRow right)
    {
        Require(left.SemanticDigest == right.SemanticDigest,
            "PF5-R2-R1 semantic digests diverged.");
        Compare(left.ProfileCounters.SuccessfulSemanticPublications,
            right.ProfileCounters.SuccessfulSemanticPublications, "publications");
        Compare(left.ProfileCounters.GameMessagesQueued,
            right.ProfileCounters.GameMessagesQueued, "Game messages");
        Compare(left.ProfileCounters.ProofEntries,
            right.ProfileCounters.ProofEntries, "proof entries");
        Compare(left.ProfileCounters.ChunkBundleMessages,
            right.ProfileCounters.ChunkBundleMessages, "bundle messages");
        Compare(left.ProfileCounters.ChunkBundleEntries,
            right.ProfileCounters.ChunkBundleEntries, "bundle entries");
        Compare(left.Transport.WireDatagrams, right.Transport.WireDatagrams, "wire datagrams");

        static void Compare(ulong leftValue, ulong rightValue, string label)
        {
            Require(leftValue == rightValue,
                $"PF5-R2-R1 {label} changed: {leftValue}->{rightValue}.");
        }
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

internal sealed record Pf5R2R1TimingReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string MeasurementBase,
    string GitSha,
    string CorrectnessCandidate,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessors,
    long StopwatchFrequency,
    int WarmupTicks,
    int MeasuredTicks,
    int Samples,
    string AuthorityMutationBoundary,
    string NetworkAndRemoteReplicationBoundary,
    string AuthorityThroughNetworkAndRemoteReplicationBoundary,
    string FrozenPolicy,
    P03TimingBoundaryEvidence BoundaryRegression,
    Pf5R2R1TimingOverhead TimingOverhead,
    Pf5R2R1ComparisonRow[] Rows);

internal sealed record Pf5R2R1TimingOverhead(
    double DisabledBeforeNetworkP95,
    double EnabledNetworkP95,
    double DisabledAfterNetworkP95,
    double DisabledBracketMeanNetworkP95,
    double EnabledDeltaPercent,
    string ImplementationCost);

internal sealed record Pf5R2R1ComparisonRow(
    int Population,
    int Sample,
    bool Prepared,
    P03WholeFlowRow Flow);
