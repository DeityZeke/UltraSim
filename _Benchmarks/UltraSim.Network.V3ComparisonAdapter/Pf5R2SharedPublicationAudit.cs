using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using UltraSim.Game.Replication;
using UltraSim.Game.World;

internal static class Pf5R2SharedPublicationAudit
{
    private const string MeasurementBase =
        "02fb6a3d3b15c89273467ceed613215f869ed8b5";
    private const string PriorCorrectnessCandidate =
        "af9fcb740c02eb08efa8d4985d929b50b135504e";
    private const int Seed = 0x5F52_0001;

    internal static void Run(string[] args)
    {
        int warmup = ReadInt(args, "--warmup", 30);
        int ticks = ReadInt(args, "--ticks", 270);
        int samples = ReadInt(args, "--samples", 3);
        int[] populations = ParsePopulations(
            ReadText(args, "--populations") ?? "64,96,128,160,192,256,300,350");
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        if (focused)
        {
            populations = [64, 128];
            warmup = Math.Min(warmup, 12);
            ticks = Math.Min(ticks, 45);
            samples = 1;
        }
        if (warmup < 1 || ticks < 10 || samples < 1 || samples > 9 ||
            populations.Any(value => value is < 2 or > 512))
            throw new ArgumentOutOfRangeException(nameof(args));

        int divergenceRecipients = ReadInt(args, "--divergence-recipients", focused ? 16 : 64);
        int divergenceTicks = ReadInt(args, "--divergence-ticks", focused ? 45 : 180);
        string[] scenarios = focused
            ? ["synchronized", "combined-divergence", "history-pressure"]
            :
            [
                "quiet",
                "synchronized",
                "staggered-joins",
                "cadence-phases",
                "controlled-loss",
                "controlled-reorder",
                "proof-delay",
                "temporary-stalls",
                "aoi-churn",
                "reconnect",
                "history-pressure",
                "combined-divergence",
            ];
        var divergence = new List<Pf5R1ScenarioReport>(scenarios.Length);
        foreach (string scenario in scenarios)
        {
            Console.WriteLine($"PF5-R2 divergence {scenario}: constructing...");
            Pf5R1ScenarioReport row = Pf5R1HistoryAudit.RunPreparedScenarioForPf5R2(
                scenario, divergenceRecipients, divergenceTicks, Seed);
            Require(row.PreparedSource.Selected &&
                row.PreparedSource.HistoryDerivedSteadyStateCalls == 0,
                $"PF5-R2 {scenario} invoked old history reconstruction.");
            divergence.Add(row);
            Console.WriteLine($"PF5-R2 {scenario}: prepared=" +
                $"{row.PreparedSource.PreparedSourcePublications:N0}, references=" +
                $"{row.PreparedSource.ReferencedSourcePublications:N0}, " +
                $"rebases={row.PreparedSource.RebaseRequirements:N0}, " +
                $"snapshots={row.SnapshotOccurrences:N0}");
        }

        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r2-shared-publication.json";
        var rows = new List<Pf5R2ComparisonRow>();
        for (int populationIndex = 0; populationIndex < populations.Length; populationIndex++)
        {
            int population = populations[populationIndex];
            int modeSamples = population <= 128 ? samples : 1;
            for (int sample = 0; sample < modeSamples; sample++)
            {
                // Alternate order so process drift does not consistently favor one model.
                PreparedChunkPublicationReadModel first = ((populationIndex + sample) & 1) == 0
                    ? PreparedChunkPublicationReadModel.HistoryDerivedComparison
                    : PreparedChunkPublicationReadModel.PreparedLatestState;
                RunOne(population, sample, first);
                RunOne(population, sample, first ==
                    PreparedChunkPublicationReadModel.PreparedLatestState
                        ? PreparedChunkPublicationReadModel.HistoryDerivedComparison
                        : PreparedChunkPublicationReadModel.PreparedLatestState);
            }
        }

        foreach (IGrouping<int, Pf5R2ComparisonRow> population in rows.GroupBy(value => value.Population))
        {
            Pf5R2ComparisonRow[] oldRows = population.Where(value => !value.Prepared).ToArray();
            Pf5R2ComparisonRow[] newRows = population.Where(value => value.Prepared).ToArray();
            Require(oldRows.Length == newRows.Length && oldRows.Length != 0,
                "PF5-R2 A/B sample cardinality diverged.");
            string digest = oldRows[0].Flow.SemanticDigest;
            Require(oldRows.Concat(newRows).All(value => value.Flow.SemanticDigest == digest),
                "PF5-R2 A/B semantic digests diverged.");
            for (int index = 0; index < oldRows.Length; index++)
            {
                string? mismatch = DescribeSemanticWorkMismatch(
                    oldRows[index].Flow, newRows[index].Flow);
                Require(mismatch is null,
                    $"PF5-R2 A/B changed canonical Game work: {mismatch}");
            }
        }

        var report = new Pf5R2AuditReport(
            "ultrasim-concept-demo-pf5-r2-shared-publication-v1",
            DateTimeOffset.UtcNow,
            MeasurementBase,
            ReadGitSha(),
            PriorCorrectnessCandidate,
            Environment.Version.ToString(),
            Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            Stopwatch.Frequency,
            warmup,
            ticks,
            samples,
            "ChunkPublication; Message129WhenBeneficial; selective message148 LZ4 " +
            "canonical>=384/saving>=32; Immediate/Near/Far 1/2/4; CrossLane cap 2; " +
            "Game manifest v29; transport revision 2",
            divergence.ToArray(),
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
            string label = model == PreparedChunkPublicationReadModel.PreparedLatestState
                ? "prepared" : "history";
            Console.WriteLine($"PF5-R2 {population} {label} sample {sample + 1}: constructing...");
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
                chunkReadModel: model);
            P03WholeFlowRow flow = harness.Run(warmup, ticks);
            PreparedChunkPublicationSnapshot prepared = flow.ProfileCounters.PreparedPublication;
            if (model == PreparedChunkPublicationReadModel.PreparedLatestState)
                Require(prepared.Selected && prepared.HistoryDerivedSteadyStateCalls == 0 &&
                    prepared.RecipientRangeReads != 0,
                    "Selected PF5-R2 run did not use only prepared source publications.");
            else
                Require(!prepared.Selected && prepared.HistoryDerivedSteadyStateCalls != 0,
                    "Comparison PF5-R2 run did not use history reconstruction.");
            rows.Add(new Pf5R2ComparisonRow(
                population,
                sample + 1,
                model == PreparedChunkPublicationReadModel.PreparedLatestState,
                flow));
            Console.WriteLine($"PF5-R2 {population} {label}: schedule p95 " +
                $"{flow.Timing.GameScheduleAndCodecQueue.P95:F3} ms, network p95 " +
                $"{flow.Timing.NetworkAndRemoteReplication.P95:F3} ms, " +
                $"source publications {prepared.PreparedSourcePublications:N0}, " +
                $"recipient reads {prepared.RecipientRangeReads:N0}");
        }
    }

    private static string? DescribeSemanticWorkMismatch(
        in P03WholeFlowRow left,
        in P03WholeFlowRow right)
    {
        var differences = new List<string>();
        Compare("publications", left.ProfileCounters.SuccessfulSemanticPublications,
            right.ProfileCounters.SuccessfulSemanticPublications);
        Compare("messages", left.ProfileCounters.GameMessagesQueued,
            right.ProfileCounters.GameMessagesQueued);
        Compare("proofs", left.ProfileCounters.ProofEntries,
            right.ProfileCounters.ProofEntries);
        Compare("bundle-messages", left.ProfileCounters.ChunkBundleMessages,
            right.ProfileCounters.ChunkBundleMessages);
        Compare("bundle-entries", left.ProfileCounters.ChunkBundleEntries,
            right.ProfileCounters.ChunkBundleEntries);
        Compare("compression-selections", left.ProfileCounters.Compression.Selected,
            right.ProfileCounters.Compression.Selected);
        return differences.Count == 0 ? null : string.Join(", ", differences);

        void Compare(string label, ulong oldValue, ulong newValue)
        {
            if (oldValue != newValue) differences.Add($"{label} {oldValue}->{newValue}");
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

internal sealed record Pf5R2AuditReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string MeasurementBase,
    string GitSha,
    string PriorCorrectnessCandidate,
    string Runtime,
    string OperatingSystem,
    int LogicalProcessors,
    long StopwatchFrequency,
    int WarmupTicks,
    int MeasuredTicks,
    int Samples,
    string FrozenPolicy,
    Pf5R1ScenarioReport[] DivergenceScenarios,
    Pf5R2ComparisonRow[] Rows);

internal sealed record Pf5R2ComparisonRow(
    int Population,
    int Sample,
    bool Prepared,
    P03WholeFlowRow Flow);
