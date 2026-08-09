using System.Globalization;
using System.Text;

namespace UltraSim.ECS.BitmapExperiment;

internal sealed class ExperimentRegressionReport
{
    public required string SemanticProfile { get; init; }
    public required DateTimeOffset BaselineTimestampUtc { get; init; }
    public required DateTimeOffset CurrentTimestampUtc { get; init; }
    public required IReadOnlyList<ExperimentRegressionResult> Results { get; init; }
}

internal sealed class ExperimentRegressionResult
{
    public required string LogicalCaseId { get; init; }
    public required string Variant { get; init; }
    public required string Status { get; init; }
    public string? Note { get; init; }
    public double? BaselineNanosecondsPerOperation { get; init; }
    public double? CurrentNanosecondsPerOperation { get; init; }
    public double? CurrentToBaselineMeanRatio { get; init; }
    public double? CurrentToBaselineP95Ratio { get; init; }
    public long? AllocationDeltaBytes { get; init; }
}

internal static class ExperimentRegression
{
    internal static ExperimentRegressionReport Compare(ExperimentReport baseline, ExperimentReport current)
    {
        ValidateEnvironment(baseline, current);
        Dictionary<string, ExperimentResult> oldRows = Index(baseline.Results);
        Dictionary<string, ExperimentResult> newRows = Index(current.Results);
        string[] keys = oldRows.Keys.Union(newRows.Keys).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        var results = new List<ExperimentRegressionResult>(keys.Length);
        foreach (string key in keys)
        {
            oldRows.TryGetValue(key, out ExperimentResult? oldRow);
            newRows.TryGetValue(key, out ExperimentResult? newRow);
            ExperimentResult descriptor = oldRow ?? newRow!;
            if (oldRow is null || newRow is null)
            {
                results.Add(new ExperimentRegressionResult
                {
                    LogicalCaseId = descriptor.LogicalCaseId,
                    Variant = descriptor.Variant,
                    Status = "catalog-mismatch",
                    Note = oldRow is null ? "New row is absent from baseline." : "Baseline row is absent from current capture.",
                });
                continue;
            }
            if (oldRow.Scale != newRow.Scale ||
                oldRow.ArchetypeBytesPerRow != newRow.ArchetypeBytesPerRow ||
                oldRow.QueryBytesPerRow != newRow.QueryBytesPerRow ||
                oldRow.OperationsPerIteration != newRow.OperationsPerIteration)
            {
                results.Add(new ExperimentRegressionResult
                {
                    LogicalCaseId = descriptor.LogicalCaseId,
                    Variant = descriptor.Variant,
                    Status = "descriptor-mismatch",
                    Note = "Scale, width, byte count, or operation count changed.",
                });
                continue;
            }
            if (!string.Equals(oldRow.CanonicalHash, newRow.CanonicalHash, StringComparison.Ordinal))
            {
                results.Add(new ExperimentRegressionResult
                {
                    LogicalCaseId = descriptor.LogicalCaseId,
                    Variant = descriptor.Variant,
                    Status = "state-mismatch",
                    Note = $"Canonical state changed: {oldRow.CanonicalHash} -> {newRow.CanonicalHash}.",
                });
                continue;
            }
            results.Add(new ExperimentRegressionResult
            {
                LogicalCaseId = descriptor.LogicalCaseId,
                Variant = descriptor.Variant,
                Status = "comparable",
                BaselineNanosecondsPerOperation = oldRow.MeanNanosecondsPerOperation,
                CurrentNanosecondsPerOperation = newRow.MeanNanosecondsPerOperation,
                CurrentToBaselineMeanRatio = Divide(newRow.MeanNanosecondsPerOperation, oldRow.MeanNanosecondsPerOperation),
                CurrentToBaselineP95Ratio = Divide(newRow.P95Milliseconds, oldRow.P95Milliseconds),
                AllocationDeltaBytes = newRow.MeanAllocatedBytes - oldRow.MeanAllocatedBytes,
            });
        }
        return new ExperimentRegressionReport
        {
            SemanticProfile = current.SemanticProfile,
            BaselineTimestampUtc = baseline.TimestampUtc,
            CurrentTimestampUtc = current.TimestampUtc,
            Results = results,
        };
    }

    internal static string BuildMarkdown(ExperimentRegressionReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Bitmap block-pool baseline regression");
        text.AppendLine();
        text.AppendLine($"Baseline: `{report.BaselineTimestampUtc:O}`; current: `{report.CurrentTimestampUtc:O}`. Ratios are current/baseline; lower is faster.");
        text.AppendLine();
        text.AppendLine("| Case | Variant | Baseline ns/op | Current ns/op | Mean ratio | p95 ratio | Allocation delta | Status |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
        foreach (ExperimentRegressionResult result in report.Results)
        {
            text.Append("| `").Append(result.LogicalCaseId).Append("` | ")
                .Append(result.Variant).Append(" | ")
                .Append(F(result.BaselineNanosecondsPerOperation)).Append(" | ")
                .Append(F(result.CurrentNanosecondsPerOperation)).Append(" | ")
                .Append(R(result.CurrentToBaselineMeanRatio)).Append(" | ")
                .Append(R(result.CurrentToBaselineP95Ratio)).Append(" | ")
                .Append(result.AllocationDeltaBytes?.ToString("N0", CultureInfo.InvariantCulture) ?? "n/a").Append(" | ")
                .Append(result.Status);
            if (!string.IsNullOrWhiteSpace(result.Note))
                text.Append(": ").Append(result.Note);
            text.AppendLine(" |");
        }
        return text.ToString();
    }

    private static void ValidateEnvironment(ExperimentReport baseline, ExperimentReport current)
    {
        if (!string.Equals(baseline.SemanticProfile, current.SemanticProfile, StringComparison.Ordinal) ||
            !string.Equals(baseline.RuntimeVersion, current.RuntimeVersion, StringComparison.Ordinal) ||
            !string.Equals(baseline.OperatingSystem, current.OperatingSystem, StringComparison.Ordinal) ||
            !string.Equals(baseline.ProcessArchitecture, current.ProcessArchitecture, StringComparison.Ordinal) ||
            baseline.ProcessorCount != current.ProcessorCount ||
            baseline.WarmupCount != current.WarmupCount ||
            baseline.IterationCount != current.IterationCount ||
            baseline.RequestedScale != current.RequestedScale ||
            !string.Equals(baseline.Filter, current.Filter, StringComparison.Ordinal) ||
            !string.Equals(baseline.ExecutionOrder, current.ExecutionOrder, StringComparison.Ordinal) ||
            baseline.OrderSeed != current.OrderSeed)
        {
            throw new InvalidOperationException("Prototype baseline/current environment or measurement policy differs.");
        }
    }

    private static Dictionary<string, ExperimentResult> Index(IReadOnlyList<ExperimentResult> rows)
    {
        var index = new Dictionary<string, ExperimentResult>(StringComparer.Ordinal);
        foreach (ExperimentResult row in rows)
        {
            string key = $"{row.LogicalCaseId}|{row.Variant}";
            if (!index.TryAdd(key, row))
                throw new InvalidOperationException($"Duplicate prototype result '{key}'.");
        }
        return index;
    }

    private static double Divide(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;

    private static string F(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a";

    private static string R(double? value) => value.HasValue
        ? value.Value.ToString("F3", CultureInfo.InvariantCulture) + "x"
        : "n/a";
}
