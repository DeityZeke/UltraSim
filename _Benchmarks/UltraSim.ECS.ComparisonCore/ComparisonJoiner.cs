using System.Text;
using System.Text.Json;

namespace UltraSim.ECS.Comparison;

public static class ComparisonJoiner
{
    public static CrossGenerationReport Join(
        ComparisonRunReport v2,
        ComparisonRunReport v3,
        string captureOrder = "external-or-unknown")
    {
        ArgumentNullException.ThrowIfNull(v2);
        ArgumentNullException.ThrowIfNull(v3);
        if (v2.ProtocolVersion != ComparisonRunner.ProtocolVersion ||
            v3.ProtocolVersion != ComparisonRunner.ProtocolVersion)
        {
            throw new InvalidOperationException("Comparison reports use an incompatible protocol version.");
        }
        if (!string.Equals(v2.RuntimeVersion, v3.RuntimeVersion, StringComparison.Ordinal) ||
            !string.Equals(v2.FrameworkDescription, v3.FrameworkDescription, StringComparison.Ordinal) ||
            !string.Equals(v2.ProcessArchitecture, v3.ProcessArchitecture, StringComparison.Ordinal) ||
            !string.Equals(v2.TieredCompilationSetting, v3.TieredCompilationSetting, StringComparison.Ordinal) ||
            !string.Equals(v2.ReadyToRunSetting, v3.ReadyToRunSetting, StringComparison.Ordinal) ||
            v2.ServerGarbageCollection != v3.ServerGarbageCollection ||
            v2.ForcesFullCollectionBeforeMeasuredIteration != v3.ForcesFullCollectionBeforeMeasuredIteration ||
            !string.Equals(v2.AllocationMeasurementScope, v3.AllocationMeasurementScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("V2 and V3 reports were not captured with the same runtime or measurement policy.");
        }

        Dictionary<string, ComparisonScenarioResult> v2ByKey = Index(v2);
        Dictionary<string, ComparisonScenarioResult> v3ByKey = Index(v3);
        string[] keys = v2ByKey.Keys.Union(v3ByKey.Keys).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        var results = new List<CrossGenerationResult>(keys.Length);
        foreach (string key in keys)
        {
            v2ByKey.TryGetValue(key, out ComparisonScenarioResult? left);
            v3ByKey.TryGetValue(key, out ComparisonScenarioResult? right);
            ScenarioDescriptor descriptor = left?.Descriptor ?? right!.Descriptor;
            if (left is null || right is null)
            {
                results.Add(new CrossGenerationResult
                {
                    Descriptor = descriptor,
                    Status = "catalog-mismatch",
                    Note = left is null ? "Missing from V2 adapter." : "Missing from V3 adapter.",
                    V2 = left,
                    V3 = right,
                });
                continue;
            }
            ValidateDescriptorPair(left.Descriptor, right.Descriptor);
            if (left.Status != ComparisonScenarioStatus.Measured ||
                right.Status != ComparisonScenarioStatus.Measured)
            {
                results.Add(new CrossGenerationResult
                {
                    Descriptor = descriptor,
                    Status = "unsupported",
                    Note = $"V2: {left.UnsupportedReason ?? left.Status.ToString()}; V3: {right.UnsupportedReason ?? right.Status.ToString()}",
                    V2 = left,
                    V3 = right,
                });
                continue;
            }

            bool hashMatches = string.Equals(
                left.CanonicalState.Hash,
                right.CanonicalState.Hash,
                StringComparison.Ordinal) &&
                left.CanonicalState == right.CanonicalState;
            if (!hashMatches)
            {
                results.Add(new CrossGenerationResult
                {
                    Descriptor = descriptor,
                    Status = "state-mismatch",
                    Note = $"V2 {left.CanonicalState}; V3 {right.CanonicalState}",
                    V2 = left,
                    V3 = right,
                    CanonicalStateMatches = false,
                });
                continue;
            }
            results.Add(new CrossGenerationResult
            {
                Descriptor = descriptor,
                Status = "comparable",
                V2 = left,
                V3 = right,
                CanonicalStateMatches = true,
                V3ToV2MeanRatio = Divide(right.MeanNanoseconds, left.MeanNanoseconds),
                V3ToV2P95Ratio = Divide(right.P95Nanoseconds, left.P95Nanoseconds),
                AllocationDeltaBytes = right.MeanAllocatedBytes - left.MeanAllocatedBytes,
            });
        }

        return new CrossGenerationReport
        {
            ProtocolVersion = ComparisonRunner.ProtocolVersion,
            TimestampUtc = DateTimeOffset.UtcNow,
            CaptureOrder = captureOrder,
            V2 = v2,
            V3 = v3,
            Results = results,
        };
    }

    public static void WriteJson(CrossGenerationReport report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(report, ComparisonRunner.JsonOptions));
    }

    public static CrossGenerationReport ReadJson(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CrossGenerationReport>(json, ComparisonRunner.JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize comparison report '{path}'.");
    }

    public static BaselineRegressionReport CompareAgainstBaseline(
        CrossGenerationReport baseline,
        CrossGenerationReport current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (!string.Equals(baseline.ProtocolVersion, current.ProtocolVersion, StringComparison.Ordinal) ||
            !string.Equals(current.ProtocolVersion, ComparisonRunner.ProtocolVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Baseline and current reports use incompatible protocol versions.");
        }

        ValidateEnvironmentPair(baseline.V2, current.V2, "V2 baseline/current");
        ValidateEnvironmentPair(baseline.V3, current.V3, "V3 baseline/current");

        Dictionary<string, CrossGenerationResult> baselineByKey = IndexJoined(baseline);
        Dictionary<string, CrossGenerationResult> currentByKey = IndexJoined(current);
        string[] keys = baselineByKey.Keys.Union(currentByKey.Keys)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        var results = new List<BaselineRegressionResult>(keys.Length);
        foreach (string key in keys)
        {
            baselineByKey.TryGetValue(key, out CrossGenerationResult? oldResult);
            currentByKey.TryGetValue(key, out CrossGenerationResult? newResult);
            ScenarioDescriptor descriptor = oldResult?.Descriptor ?? newResult!.Descriptor;
            if (oldResult is null || newResult is null)
            {
                results.Add(new BaselineRegressionResult
                {
                    Descriptor = descriptor,
                    Status = "catalog-mismatch",
                    Note = oldResult is null ? "New row is absent from the baseline." : "Baseline row is absent from the current capture.",
                });
                continue;
            }
            if (oldResult.Descriptor != newResult.Descriptor)
            {
                results.Add(new BaselineRegressionResult
                {
                    Descriptor = descriptor,
                    Status = "descriptor-mismatch",
                    Note = "Semantic, state, boundary, strength, operation-count, or byte-count contract changed; ratios suppressed.",
                });
                continue;
            }
            if (oldResult.Status == "state-mismatch" || newResult.Status == "state-mismatch")
            {
                results.Add(new BaselineRegressionResult
                {
                    Descriptor = descriptor,
                    Status = "state-mismatch",
                    Note = $"Cross-generation canonical state did not match. Baseline: {oldResult.Status}; current: {newResult.Status}. Ratios suppressed.",
                });
                continue;
            }
            if (oldResult.Status != "comparable" || newResult.Status != "comparable" ||
                oldResult.V2 is null || oldResult.V3 is null || newResult.V2 is null || newResult.V3 is null)
            {
                results.Add(new BaselineRegressionResult
                {
                    Descriptor = descriptor,
                    Status = "unsupported",
                    Note = $"Baseline: {oldResult.Status}; current: {newResult.Status}. Ratios require comparable state in both captures.",
                });
                continue;
            }
            if (oldResult.V2.WarmupCount != newResult.V2.WarmupCount ||
                oldResult.V3.WarmupCount != newResult.V3.WarmupCount ||
                oldResult.V2.IterationCount != newResult.V2.IterationCount ||
                oldResult.V3.IterationCount != newResult.V3.IterationCount)
            {
                results.Add(new BaselineRegressionResult
                {
                    Descriptor = descriptor,
                    Status = "measurement-mismatch",
                    Note = "Warmup or measured-iteration counts changed; ratios suppressed.",
                });
                continue;
            }

            bool v2StateMatches = oldResult.V2.CanonicalState == newResult.V2.CanonicalState;
            bool v3StateMatches = oldResult.V3.CanonicalState == newResult.V3.CanonicalState;
            if (!v2StateMatches || !v3StateMatches)
            {
                results.Add(new BaselineRegressionResult
                {
                    Descriptor = descriptor,
                    Status = "state-mismatch",
                    Note = $"V2 baseline/current match: {v2StateMatches}; V3 baseline/current match: {v3StateMatches}. Ratios suppressed.",
                    V2CanonicalStateMatches = v2StateMatches,
                    V3CanonicalStateMatches = v3StateMatches,
                });
                continue;
            }
            results.Add(new BaselineRegressionResult
            {
                Descriptor = descriptor,
                Status = "comparable",
                V2BaselineMeanNanosecondsPerOperation = oldResult.V2.MeanNanosecondsPerOperation,
                V2CurrentMeanNanosecondsPerOperation = newResult.V2.MeanNanosecondsPerOperation,
                V3BaselineMeanNanosecondsPerOperation = oldResult.V3.MeanNanosecondsPerOperation,
                V3CurrentMeanNanosecondsPerOperation = newResult.V3.MeanNanosecondsPerOperation,
                V2CurrentToBaselineMeanRatio = Divide(newResult.V2.MeanNanoseconds, oldResult.V2.MeanNanoseconds),
                V3CurrentToBaselineMeanRatio = Divide(newResult.V3.MeanNanoseconds, oldResult.V3.MeanNanoseconds),
                ControlNormalizedV3MeanRatio = Divide(
                    Divide(newResult.V3.MeanNanoseconds, oldResult.V3.MeanNanoseconds),
                    Divide(newResult.V2.MeanNanoseconds, oldResult.V2.MeanNanoseconds)),
                V3Signal = ClassifyRegressionSignal(
                    Divide(
                        Divide(newResult.V3.MeanNanoseconds, oldResult.V3.MeanNanoseconds),
                        Divide(newResult.V2.MeanNanoseconds, oldResult.V2.MeanNanoseconds))),
                V2CurrentToBaselineP95Ratio = Divide(newResult.V2.P95Nanoseconds, oldResult.V2.P95Nanoseconds),
                V3CurrentToBaselineP95Ratio = Divide(newResult.V3.P95Nanoseconds, oldResult.V3.P95Nanoseconds),
                V2AllocationDeltaBytes = newResult.V2.MeanAllocatedBytes - oldResult.V2.MeanAllocatedBytes,
                V3AllocationDeltaBytes = newResult.V3.MeanAllocatedBytes - oldResult.V3.MeanAllocatedBytes,
                V2CanonicalStateMatches = v2StateMatches,
                V3CanonicalStateMatches = v3StateMatches,
            });
        }

        return new BaselineRegressionReport
        {
            ProtocolVersion = ComparisonRunner.ProtocolVersion,
            TimestampUtc = DateTimeOffset.UtcNow,
            BaselineTimestampUtc = baseline.TimestampUtc,
            CurrentTimestampUtc = current.TimestampUtc,
            Results = results,
        };
    }

    public static void WriteRegressionJson(BaselineRegressionReport report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(report, ComparisonRunner.JsonOptions));
    }

    public static string BuildRegressionMarkdown(BaselineRegressionReport report)
    {
        int comparable = report.Results.Count(static result => result.Status == "comparable");
        int stateMismatches = report.Results.Count(static result => result.Status == "state-mismatch");
        int contractMismatches = report.Results.Count(static result =>
            result.Status is "catalog-mismatch" or "descriptor-mismatch" or "measurement-mismatch");
        int unsupported = report.Results.Count(static result => result.Status == "unsupported");
        var builder = new StringBuilder();
        builder.AppendLine("# ECS V2/V3 Baseline Regression");
        builder.AppendLine();
        builder.AppendLine($"Generated: {report.TimestampUtc:O}");
        builder.AppendLine($"Baseline capture: {report.BaselineTimestampUtc:O}");
        builder.AppendLine($"Current capture: {report.CurrentTimestampUtc:O}");
        builder.AppendLine($"Coverage: **{comparable} comparable**, **{stateMismatches} state mismatches**, **{contractMismatches} contract/catalog mismatches**, **{unsupported} unsupported**.");
        builder.AppendLine();
        builder.AppendLine("> Ratios are current/baseline; lower is faster. V2 is rerun as a frozen control. Similar V2 and V3 movement suggests host noise; V3-only movement suggests a rewrite change.");
        builder.AppendLine("> Signals are triage labels, not automatic acceptance/rejection. Confirm both process orders and inspect absolute time; tiny or sub-nanosecond rows can cross a percentage threshold on noise alone.");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Scale | V2 baseline ns/op | V2 current ns/op | V3 baseline ns/op | V3 current ns/op | V2 current/base | V3 current/base | V2 p95/base | V3 p95/base | V3/control | Signal | V2 alloc delta | V3 alloc delta |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | ");
        foreach (BaselineRegressionResult result in report.Results.Where(static result => result.Status == "comparable"))
        {
            builder.AppendLine($"| `{result.Descriptor.Id}` | {result.Descriptor.Scale} | {result.V2BaselineMeanNanosecondsPerOperation:F3} | {result.V2CurrentMeanNanosecondsPerOperation:F3} | {result.V3BaselineMeanNanosecondsPerOperation:F3} | {result.V3CurrentMeanNanosecondsPerOperation:F3} | {result.V2CurrentToBaselineMeanRatio:F3}x | {result.V3CurrentToBaselineMeanRatio:F3}x | {result.V2CurrentToBaselineP95Ratio:F3}x | {result.V3CurrentToBaselineP95Ratio:F3}x | {result.ControlNormalizedV3MeanRatio:F3}x | {result.V3Signal} | {result.V2AllocationDeltaBytes:+#;-#;0} | {result.V3AllocationDeltaBytes:+#;-#;0} |");
        }
        builder.AppendLine();
        builder.AppendLine("## Suppressed rows");
        builder.AppendLine();
        foreach (BaselineRegressionResult result in report.Results.Where(static result => result.Status != "comparable"))
            builder.AppendLine($"- `{result.Descriptor.Id}` ({result.Descriptor.Scale}): **{result.Status}** — {result.Note}");
        return builder.ToString();
    }

    public static string BuildMarkdown(CrossGenerationReport report)
    {
        int comparable = report.Results.Count(static result => result.Status == "comparable");
        int mismatched = report.Results.Count(static result => result.Status == "state-mismatch");
        int unsupported = report.Results.Count(static result => result.Status == "unsupported");
        var builder = new StringBuilder();
        builder.AppendLine("# ECS V2 vs V3 Semantic Baseline");
        builder.AppendLine();
        builder.AppendLine($"Generated: {report.TimestampUtc:O}");
        builder.AppendLine($"Protocol: `{report.ProtocolVersion}`");
        builder.AppendLine($"Runtime: `{report.V3.FrameworkDescription}` / `{report.V3.ProcessArchitecture}` / {report.V3.ProcessorCount} logical processors");
        builder.AppendLine($"Runtime policy: tiered compilation `{report.V3.TieredCompilationSetting}`, ReadyToRun `{report.V3.ReadyToRunSetting}`, server GC `{report.V3.ServerGarbageCollection}`");
        builder.AppendLine($"Measurement policy: pre-iteration full collection `{report.V3.ForcesFullCollectionBeforeMeasuredIteration}`; allocation scope `{report.V3.AllocationMeasurementScope}`");
        builder.AppendLine($"Profiles: V2 `{report.V2.KernelProfile}`; V3 `{report.V3.KernelProfile}`");
        builder.AppendLine($"Capture order: `{report.CaptureOrder}`");
        builder.AppendLine($"Coverage: **{comparable} comparable**, **{mismatched} state mismatches**, **{unsupported} unsupported parity rows**.");
        builder.AppendLine();
        builder.AppendLine("> Lower ratios favor V3. Setup, fixture allocation, canonical hashing, validation, and disposal are outside the timed boundary. Each generation runs in an isolated process because both kernels use the same assembly/type identity.");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Scale | Boundary | Strength | V2 ns/op | V3 ns/op | V3/V2 mean | V3/V2 p95 | V2 alloc | V3 alloc | State |");
        builder.AppendLine("| --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (CrossGenerationResult result in report.Results.Where(static result => result.Status == "comparable"))
        {
            ComparisonScenarioResult left = result.V2!;
            ComparisonScenarioResult right = result.V3!;
            builder.AppendLine($"| `{result.Descriptor.Id}` | {result.Descriptor.Scale} | {result.Descriptor.Boundary} | {result.Descriptor.Strength} | {left.MeanNanosecondsPerOperation:F3} | {right.MeanNanosecondsPerOperation:F3} | {result.V3ToV2MeanRatio:F3}x | {result.V3ToV2P95Ratio:F3}x | {left.MeanAllocatedBytes} | {right.MeanAllocatedBytes} | match |");
        }

        builder.AppendLine();
        builder.AppendLine("## Unsupported / future parity rows");
        builder.AppendLine();
        foreach (CrossGenerationResult result in report.Results.Where(static result => result.Status != "comparable"))
            builder.AppendLine($"- `{result.Descriptor.Id}`: {result.Note}");

        builder.AppendLine();
        builder.AppendLine("## Interpretation rules");
        builder.AppendLine();
        builder.AppendLine("- A timing ratio is published only when descriptors and normalized canonical final state match.");
        builder.AppendLine("- `Direct` means the semantic work and timed boundary match. `Provisional` means the result is still useful, but generation-specific mechanics differ enough that it should not be treated as a universal winner.");
        builder.AppendLine("- This is a regression baseline for the present overlap, not proof of complete V2 replacement. Unsupported rows must become measured only when the corresponding feature exists and its state contract can be validated.");
        builder.AppendLine("- Re-run reversed process order and in quiet isolated processes before making small (<10%) performance claims.");
        return builder.ToString();
    }

    private static Dictionary<string, ComparisonScenarioResult> Index(ComparisonRunReport report)
    {
        var index = new Dictionary<string, ComparisonScenarioResult>(StringComparer.Ordinal);
        foreach (ComparisonScenarioResult result in report.Results)
        {
            string key = Key(result.Descriptor);
            if (!index.TryAdd(key, result))
                throw new InvalidOperationException($"Duplicate comparison result '{key}' in {report.Generation}.");
        }
        return index;
    }

    private static Dictionary<string, CrossGenerationResult> IndexJoined(CrossGenerationReport report)
    {
        var index = new Dictionary<string, CrossGenerationResult>(StringComparer.Ordinal);
        foreach (CrossGenerationResult result in report.Results)
        {
            string key = Key(result.Descriptor);
            if (!index.TryAdd(key, result))
                throw new InvalidOperationException($"Duplicate joined comparison result '{key}'.");
        }
        return index;
    }

    private static void ValidateEnvironmentPair(
        ComparisonRunReport baseline,
        ComparisonRunReport current,
        string label)
    {
        if (!string.Equals(baseline.Generation, current.Generation, StringComparison.Ordinal) ||
            !string.Equals(baseline.KernelProfile, current.KernelProfile, StringComparison.Ordinal) ||
            !string.Equals(baseline.RuntimeVersion, current.RuntimeVersion, StringComparison.Ordinal) ||
            !string.Equals(baseline.FrameworkDescription, current.FrameworkDescription, StringComparison.Ordinal) ||
            !string.Equals(baseline.OSDescription, current.OSDescription, StringComparison.Ordinal) ||
            !string.Equals(baseline.ProcessArchitecture, current.ProcessArchitecture, StringComparison.Ordinal) ||
            !string.Equals(baseline.Configuration, current.Configuration, StringComparison.Ordinal) ||
            !string.Equals(baseline.TieredCompilationSetting, current.TieredCompilationSetting, StringComparison.Ordinal) ||
            !string.Equals(baseline.ReadyToRunSetting, current.ReadyToRunSetting, StringComparison.Ordinal) ||
            baseline.ServerGarbageCollection != current.ServerGarbageCollection ||
            baseline.ForcesFullCollectionBeforeMeasuredIteration != current.ForcesFullCollectionBeforeMeasuredIteration ||
            !string.Equals(baseline.AllocationMeasurementScope, current.AllocationMeasurementScope, StringComparison.Ordinal) ||
            baseline.ProcessorCount != current.ProcessorCount)
        {
            throw new InvalidOperationException($"{label} reports were not captured with the same environment and measurement policy.");
        }
    }

    private static void ValidateDescriptorPair(ScenarioDescriptor left, ScenarioDescriptor right)
    {
        if (left != right)
            throw new InvalidOperationException($"Descriptor mismatch for '{left.Id}' scale {left.Scale}.");
    }

    private static string Key(ScenarioDescriptor descriptor) => $"{descriptor.Id}|{descriptor.Scale}";

    private static double Divide(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;

    private static string ClassifyRegressionSignal(double ratio)
    {
        if (ratio == 0)
            return "unavailable";
        if (ratio < 0.90)
            return "material improvement";
        if (ratio < 0.95)
            return "improvement";
        if (ratio <= 1.02)
            return "noise/tolerated";
        if (ratio <= 1.05)
            return "investigate";
        if (ratio <= 1.10)
            return "must explain";
        return "reject candidate";
    }
}
