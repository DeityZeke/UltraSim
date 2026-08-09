using System.Globalization;
using System.Text;

namespace UltraSim.ECS.BitmapExperiment;

internal static class ExperimentMarkdown
{
    internal static string Build(ExperimentReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Bitmap block-pool experiment results");
        text.AppendLine();
        text.AppendLine($"Captured `{report.TimestampUtc:O}` on `{report.OperatingSystem}` / `{report.ProcessArchitecture}`, .NET `{report.RuntimeVersion}`.");
        text.AppendLine();
        text.AppendLine($"Profile: `{report.SemanticProfile}`; requested scale: `{report.RequestedScale:N0}`; warmups: `{report.WarmupCount}`; measured iterations: `{report.IterationCount}`.");
        text.AppendLine($"Execution order: `{report.ExecutionOrder}` with seed `{report.OrderSeed}`.");
        text.AppendLine();
        text.AppendLine("`DensePacked` is the prototype's same-shell packed control, not a substitute label for production V29. Speedup is `DensePacked ns/op / variant ns/op`; above 1.0 means the variant is faster. Canonical hashes are required to match within every logical case.");
        text.AppendLine();
        foreach (IGrouping<string, ExperimentResult> category in report.Results.GroupBy(static result => result.Category))
        {
            text.AppendLine($"## {category.Key}");
            text.AppendLine();
            text.AppendLine("| Case | Variant | ns/op | vs packed | ms | alloc | GB/s | holes | full/partial words | relocations | relocated bytes |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, ExperimentResult> logicalCase in category.GroupBy(static result => result.LogicalCaseId))
            {
                ExperimentResult? dense = logicalCase.FirstOrDefault(static result => result.Variant == nameof(PrototypeStorageKind.DensePacked));
                foreach (ExperimentResult result in logicalCase)
                {
                    double speedup = dense is null ? double.NaN : dense.MeanNanosecondsPerOperation / result.MeanNanosecondsPerOperation;
                    text.Append("| `").Append(result.LogicalCaseId).Append("` | ")
                        .Append(result.Variant).Append(" | ")
                        .Append(F(result.MeanNanosecondsPerOperation)).Append(" | ")
                        .Append(double.IsNaN(speedup) ? "n/a" : speedup.ToString("F2", CultureInfo.InvariantCulture) + "x").Append(" | ")
                        .Append(F(result.MeanMilliseconds)).Append(" | ")
                        .Append(result.MeanAllocatedBytes.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                        .Append(F(result.EffectiveGigabytesPerSecond)).Append(" | ")
                        .Append(result.Topology.HoleCount.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                        .Append(result.Topology.FullWords).Append('/').Append(result.Topology.PartialWords).Append(" | ")
                        .Append(result.Topology.RowRelocations.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                        .Append(result.Topology.RelocatedPayloadBytes.ToString("N0", CultureInfo.InvariantCulture)).AppendLine(" |");
                }
            }
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string F(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
