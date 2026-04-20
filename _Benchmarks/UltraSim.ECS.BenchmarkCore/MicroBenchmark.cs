using System.Diagnostics;
using System.Text.Json;

namespace UltraSim.ECS.Benchmarks.Core;

public readonly record struct ScenarioMeasurement(
    long Operations,
    long Checksum);

public sealed class BenchmarkScenario
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool Comparable { get; init; }
    public required int Scale { get; init; }
    public int WarmupCount { get; init; } = 1;
    public int IterationCount { get; init; } = 5;
    public required Func<ScenarioMeasurement> Execute { get; init; }
}

public static class MicroBenchmark
{
    public static BenchmarkRunReport RunSuite(
        string suiteName,
        string kernelName,
        IEnumerable<BenchmarkScenario> scenarios)
    {
        var results = new List<BenchmarkResult>();
        foreach (BenchmarkScenario scenario in scenarios)
            results.Add(RunScenario(scenario));

        return new BenchmarkRunReport
        {
            SuiteName = suiteName,
            KernelName = kernelName,
            RuntimeVersion = Environment.Version.ToString(),
            Configuration = GetConfiguration(),
            ProcessorCount = Environment.ProcessorCount,
            TimestampUtc = DateTimeOffset.UtcNow,
            Results = results,
        };
    }

    public static void WriteReport(BenchmarkRunReport report, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(report, options));
    }

    private static BenchmarkResult RunScenario(BenchmarkScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(scenario.Execute);

        for (int i = 0; i < scenario.WarmupCount; i++)
            _ = scenario.Execute();

        ForceFullCollection();

        var elapsed = new double[scenario.IterationCount];
        var allocated = new long[scenario.IterationCount];
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long checksum = 0;
        long operations = 0;

        for (int i = 0; i < scenario.IterationCount; i++)
        {
            ForceFullCollection();

            long allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            long timestampBefore = Stopwatch.GetTimestamp();
            ScenarioMeasurement measurement = scenario.Execute();
            long timestampAfter = Stopwatch.GetTimestamp();
            long allocationAfter = GC.GetTotalAllocatedBytes(precise: true);

            checksum ^= measurement.Checksum;
            operations = measurement.Operations;
            elapsed[i] = Stopwatch.GetElapsedTime(timestampBefore, timestampAfter).TotalMilliseconds;
            allocated[i] = allocationAfter - allocationBefore;
        }

        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        double meanMs = elapsed.Average();
        double minMs = elapsed.Min();
        double maxMs = elapsed.Max();
        long meanAllocated = (long)allocated.Average();
        double nsPerOp = operations > 0
            ? meanMs * 1_000_000d / operations
            : 0d;
        double opsPerSecond = meanMs > 0 && operations > 0
            ? operations / (meanMs / 1000d)
            : 0d;

        return new BenchmarkResult
        {
            Name = scenario.Name,
            Category = scenario.Category,
            Comparable = scenario.Comparable,
            Scale = scenario.Scale,
            WarmupCount = scenario.WarmupCount,
            IterationCount = scenario.IterationCount,
            OperationsPerIteration = operations,
            MeanMilliseconds = meanMs,
            MinMilliseconds = minMs,
            MaxMilliseconds = maxMs,
            MeanNanosecondsPerOperation = nsPerOp,
            OperationsPerSecond = opsPerSecond,
            MeanAllocatedBytes = meanAllocated,
            Gen0Collections = gen0After - gen0Before,
            Gen1Collections = gen1After - gen1Before,
            Gen2Collections = gen2After - gen2Before,
            Checksum = checksum,
        };
    }

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string GetConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
