using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace UltraSim.ECS.BitmapExperiment;

internal static class ExperimentRunner
{
    private static long s_sink;

    internal static ExperimentReport Run(
        IReadOnlyList<ExperimentScenarioDefinition> definitions,
        int scale,
        int warmupCount,
        int iterationCount,
        string? filter,
        uint orderSeed)
    {
        if (warmupCount < 0)
            throw new ArgumentOutOfRangeException(nameof(warmupCount));
        if (iterationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(iterationCount));

        ExperimentScenarioDefinition[] selected = definitions.Where(definition =>
            string.IsNullOrWhiteSpace(filter) ||
            definition.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            definition.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            definition.Kind.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
            throw new ArgumentException($"No bitmap experiment matched filter '{filter}'.", nameof(filter));
        Shuffle(selected, orderSeed);

        var results = new List<ExperimentResult>(selected.Length);
        foreach (ExperimentScenarioDefinition definition in selected)
        {
            results.Add(RunOne(definition, warmupCount, iterationCount));
            if ((results.Count % 10) == 0)
                Console.WriteLine($"  measured {results.Count}/{selected.Length}: {definition.Category}");
        }

        ValidateCrossPolicyCanonicalState(results);
        return new ExperimentReport
        {
            SuiteName = "UltraSim.ECS.BitmapBlockPoolExperiment",
            SemanticProfile = "bitmap-block-pool-prototype-v1",
            RuntimeVersion = Environment.Version.ToString(),
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            WarmupCount = warmupCount,
            IterationCount = iterationCount,
            RequestedScale = scale,
            Filter = filter,
            ExecutionOrder = "deterministic-fisher-yates-v1",
            OrderSeed = orderSeed,
            TimestampUtc = DateTimeOffset.UtcNow,
            Results = results,
        };
    }

    private static ExperimentResult RunOne(
        ExperimentScenarioDefinition definition,
        int warmupCount,
        int iterationCount)
    {
        using IExperimentScenario scenario = definition.Create();
        scenario.Prepare();
        ExperimentMeasurement probe = scenario.Execute();
        ulong expectedHash = scenario.CaptureCanonicalHash();
        long expectedOperations = probe.Operations;
        Consume(probe.Checksum);

        for (int index = 0; index < warmupCount; index++)
        {
            scenario.Prepare();
            ExperimentMeasurement warmup = scenario.Execute();
            Validate(scenario, expectedHash, expectedOperations, warmup);
            Consume(warmup.Checksum);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var elapsed = new double[iterationCount];
        var allocations = new long[iterationCount];
        long checksum = 0;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        for (int index = 0; index < iterationCount; index++)
        {
            scenario.Prepare();
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            ExperimentMeasurement measurement = scenario.Execute();
            long stop = Stopwatch.GetTimestamp();
            long allocationAfter = GC.GetAllocatedBytesForCurrentThread();
            Validate(scenario, expectedHash, expectedOperations, measurement);
            elapsed[index] = Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds;
            allocations[index] = allocationAfter - allocationBefore;
            checksum = unchecked((checksum * 397L) ^ measurement.Checksum);
            Consume(measurement.Checksum);
        }

        double[] sorted = elapsed.ToArray();
        Array.Sort(sorted);
        double mean = elapsed.Average();
        return new ExperimentResult
        {
            LogicalCaseId = scenario.LogicalCaseId,
            Name = scenario.Name,
            Category = scenario.Category,
            Variant = scenario.Kind.ToString(),
            Scale = scenario.Scale,
            ArchetypeBytesPerRow = scenario.ArchetypeBytesPerRow,
            QueryBytesPerRow = scenario.QueryBytesPerRow,
            OperationsPerIteration = expectedOperations,
            MeanMilliseconds = mean,
            MinMilliseconds = sorted[0],
            P50Milliseconds = Percentile(sorted, 0.50),
            P95Milliseconds = Percentile(sorted, 0.95),
            P99Milliseconds = Percentile(sorted, 0.99),
            MaxMilliseconds = sorted[^1],
            MeanNanosecondsPerOperation = mean * 1_000_000d / expectedOperations,
            MeanAllocatedBytes = (long)allocations.Average(),
            MaxAllocatedBytes = allocations.Max(),
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            Gen1Collections = GC.CollectionCount(1) - gen1Before,
            Gen2Collections = GC.CollectionCount(2) - gen2Before,
            Checksum = checksum,
            CanonicalHash = expectedHash.ToString("X16"),
            PayloadBytes = scenario.PayloadBytes,
            OccupancyMetadataBytes = scenario.OccupancyMetadataBytes,
            RowEntityMetadataBytes = scenario.RowEntityMetadataBytes,
            EffectiveGigabytesPerSecond = scenario.QueryBytesPerRow == 0
                ? 0
                : ((expectedOperations * (double)scenario.QueryBytesPerRow) / (mean / 1_000d)) / 1_000_000_000d,
            Topology = scenario.CaptureTopology(),
        };
    }

    private static void Validate(
        IExperimentScenario scenario,
        ulong expectedHash,
        long expectedOperations,
        ExperimentMeasurement measurement)
    {
        if (measurement.Operations != expectedOperations)
            throw new InvalidOperationException($"{scenario.Name} changed its operation count.");
        ulong actual = scenario.CaptureCanonicalHash();
        if (actual != expectedHash)
        {
            throw new InvalidOperationException(
                $"{scenario.Name} changed canonical state: {expectedHash:X16} != {actual:X16}.");
        }
    }

    private static void ValidateCrossPolicyCanonicalState(IReadOnlyList<ExperimentResult> results)
    {
        foreach (IGrouping<string, ExperimentResult> group in results.GroupBy(static result => result.LogicalCaseId))
        {
            string expected = group.First().CanonicalHash;
            foreach (ExperimentResult result in group)
            {
                if (!string.Equals(expected, result.CanonicalHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Logical case '{group.Key}' differs across policies: {expected} != {result.CanonicalHash} ({result.Variant}).");
                }
            }
        }
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)position;
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    private static void Consume(long value) => Volatile.Write(ref s_sink, value);

    private static void Shuffle(Span<ExperimentScenarioDefinition> definitions, uint seed)
    {
        uint state = seed == 0 ? 0xA341_316CU : seed;
        for (int index = definitions.Length - 1; index > 0; index--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            int selected = (int)(state % (uint)(index + 1));
            (definitions[index], definitions[selected]) = (definitions[selected], definitions[index]);
        }
    }
}
