using System.Text;
using System.Text.Json;
using UltraSim.ECS.Benchmarks.Core;

string resultsDir = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results");

string newPath = Path.Combine(resultsDir, "new-ecs-benchmarks.json");
string oldPath = Path.Combine(resultsDir, "old-ecs-benchmarks.json");
string summaryPath = Path.Combine(resultsDir, "ecs-benchmark-summary.md");

BenchmarkRunReport newReport = Load(newPath);
BenchmarkRunReport oldReport = Load(oldPath);
string summary = BuildSummary(newReport, oldReport);
Directory.CreateDirectory(resultsDir);
File.WriteAllText(summaryPath, summary);
Console.WriteLine(summary);

static BenchmarkRunReport Load(string path)
{
    string json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<BenchmarkRunReport>(json)
        ?? throw new InvalidOperationException($"Could not deserialize benchmark report: {path}");
}

static string BuildSummary(BenchmarkRunReport newReport, BenchmarkRunReport oldReport)
{
    var newComparable = newReport.Results.Where(static result => result.Comparable)
        .ToDictionary(static result => result.Name + "|" + result.Scale);
    var oldComparable = oldReport.Results.Where(static result => result.Comparable)
        .ToDictionary(static result => result.Name + "|" + result.Scale);

    var keys = newComparable.Keys.Intersect(oldComparable.Keys).OrderBy(static key => key, StringComparer.Ordinal).ToArray();

    var sb = new StringBuilder();
    sb.AppendLine("# ECS Benchmark Summary");
    sb.AppendLine();
    sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
    sb.AppendLine($"ProcessorCount: {newReport.ProcessorCount}");
    sb.AppendLine();
    sb.AppendLine("| Scenario | Scale | New ms | Old ms | New ns/op | Old ns/op | Faster | Alloc Delta |");
    sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |");

    foreach (string key in keys)
    {
        BenchmarkResult newer = newComparable[key];
        BenchmarkResult older = oldComparable[key];
        string faster = newer.MeanMilliseconds < older.MeanMilliseconds ? "new" : older.MeanMilliseconds < newer.MeanMilliseconds ? "old" : "tie";
        long allocDelta = newer.MeanAllocatedBytes - older.MeanAllocatedBytes;
        string scenario = newer.Name;
        sb.AppendLine($"| {scenario} | {newer.Scale} | {newer.MeanMilliseconds:F3} | {older.MeanMilliseconds:F3} | {newer.MeanNanosecondsPerOperation:F1} | {older.MeanNanosecondsPerOperation:F1} | {faster} | {allocDelta} |");
    }

    sb.AppendLine();
    sb.AppendLine("## New-Only Scenarios");
    foreach (BenchmarkResult result in newReport.Results.Where(static result => !result.Comparable))
        sb.AppendLine($"- `{result.Name}` scale `{result.Scale}` mean `{result.MeanMilliseconds:F3} ms`, alloc `{result.MeanAllocatedBytes}`");

    sb.AppendLine();
    sb.AppendLine("## Old-Only Scenarios");
    foreach (BenchmarkResult result in oldReport.Results.Where(static result => !result.Comparable))
        sb.AppendLine($"- `{result.Name}` scale `{result.Scale}` mean `{result.MeanMilliseconds:F3} ms`, alloc `{result.MeanAllocatedBytes}`");

    return sb.ToString();
}
