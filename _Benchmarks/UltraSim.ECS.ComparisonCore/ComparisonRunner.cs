using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Text.Json;

namespace UltraSim.ECS.Comparison;

public static class ComparisonRunner
{
    public const string ProtocolVersion = "ecs-v2-v3-semantic-comparison-v1";
    private static long s_sink;

    public static ComparisonRunReport Run(
        string generation,
        string kernelProfile,
        IEnumerable<ComparisonScenario> scenarios,
        ComparisonRunOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generation);
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelProfile);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(options);
        if (options.WarmupCount < 0)
            throw new ArgumentOutOfRangeException(nameof(options.WarmupCount));
        if (options.IterationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.IterationCount));

        var results = new List<ComparisonScenarioResult>();
        foreach (ComparisonScenario scenario in scenarios)
        {
            if (!MatchesFilter(scenario.Descriptor, options))
                continue;
            results.Add(scenario.IsMeasured
                ? RunMeasured(scenario, options)
                : CreateUnsupported(scenario));
        }

        return new ComparisonRunReport
        {
            ProtocolVersion = ProtocolVersion,
            Generation = generation,
            KernelProfile = kernelProfile,
            RuntimeVersion = Environment.Version.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            OSDescription = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Configuration = GetConfiguration(),
            TieredCompilationSetting = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
            ReadyToRunSetting = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
            ServerGarbageCollection = GCSettings.IsServerGC,
            ForcesFullCollectionBeforeMeasuredIteration = true,
            AllocationMeasurementScope = "GC.GetAllocatedBytesForCurrentThread; v1 measured scenarios are single-threaded",
            ProcessorCount = Environment.ProcessorCount,
            TimestampUtc = DateTimeOffset.UtcNow,
            Results = results,
        };
    }

    public static void WriteJson(ComparisonRunReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }

    public static ComparisonRunReport ReadJson(string path) =>
        JsonSerializer.Deserialize<ComparisonRunReport>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Could not deserialize comparison report '{path}'.");

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    internal static double Percentile(ReadOnlySpan<double> sorted, double percentile)
    {
        if (sorted.IsEmpty)
            return 0;
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)position;
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    private static ComparisonScenarioResult RunMeasured(
        ComparisonScenario scenario,
        ComparisonRunOptions options)
    {
        Func<IComparisonWorkload> factory = scenario.CreateWorkload
            ?? throw new InvalidOperationException("A measured scenario requires a workload factory.");
        ScenarioDescriptor descriptor = scenario.Descriptor;

        CanonicalState expectedState;
        long expectedOperations;
        using (IComparisonWorkload probe = factory())
        {
            ScenarioMeasurement measurement = probe.Execute();
            expectedOperations = measurement.Operations;
            expectedState = probe.CaptureCanonicalState();
            Consume(measurement.Checksum);
        }
        if (expectedOperations != descriptor.OperationsPerIteration)
        {
            throw new InvalidOperationException(
                $"{descriptor.Id} declared {descriptor.OperationsPerIteration} operations but executed {expectedOperations}.");
        }

        for (int index = 0; index < options.WarmupCount; index++)
        {
            using IComparisonWorkload workload = factory();
            ScenarioMeasurement measurement = workload.Execute();
            CanonicalState state = workload.CaptureCanonicalState();
            ValidateState(descriptor, expectedState, state, expectedOperations, measurement.Operations);
            Consume(measurement.Checksum);
        }

        var nanoseconds = new double[options.IterationCount];
        var allocated = new long[options.IterationCount];
        long checksum = 0;
        int gen0 = 0;
        int gen1 = 0;
        int gen2 = 0;

        for (int index = 0; index < options.IterationCount; index++)
        {
            using IComparisonWorkload workload = factory();
            ForceFullCollection();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long timestampBefore = Stopwatch.GetTimestamp();
            ScenarioMeasurement measurement = workload.Execute();
            long timestampAfter = Stopwatch.GetTimestamp();
            long allocationAfter = GC.GetAllocatedBytesForCurrentThread();
            gen0 += GC.CollectionCount(0) - gen0Before;
            gen1 += GC.CollectionCount(1) - gen1Before;
            gen2 += GC.CollectionCount(2) - gen2Before;

            CanonicalState state = workload.CaptureCanonicalState();
            ValidateState(descriptor, expectedState, state, expectedOperations, measurement.Operations);
            nanoseconds[index] = Stopwatch.GetElapsedTime(timestampBefore, timestampAfter).TotalNanoseconds;
            allocated[index] = allocationAfter - allocationBefore;
            checksum = unchecked(checksum * 397L) ^ measurement.Checksum;
            Consume(measurement.Checksum);
        }

        Array.Sort(nanoseconds);
        double meanNanoseconds = nanoseconds.Average();
        double nsPerOperation = meanNanoseconds / expectedOperations;
        double operationsPerSecond = expectedOperations * 1_000_000_000d / meanNanoseconds;
        double bytesPerSecond = operationsPerSecond * descriptor.EffectiveBytesPerOperation;

        return new ComparisonScenarioResult
        {
            Descriptor = descriptor,
            Status = ComparisonScenarioStatus.Measured,
            WarmupCount = options.WarmupCount,
            IterationCount = options.IterationCount,
            MeanNanoseconds = meanNanoseconds,
            MinNanoseconds = nanoseconds[0],
            P50Nanoseconds = Percentile(nanoseconds, 0.50),
            P95Nanoseconds = Percentile(nanoseconds, 0.95),
            P99Nanoseconds = Percentile(nanoseconds, 0.99),
            MaxNanoseconds = nanoseconds[^1],
            MeanNanosecondsPerOperation = nsPerOperation,
            OperationsPerSecond = operationsPerSecond,
            EffectiveGiBPerSecond = bytesPerSecond / (1024d * 1024d * 1024d),
            MeanAllocatedBytes = (long)allocated.Average(),
            MaxAllocatedBytes = allocated.Max(),
            Gen0Collections = gen0,
            Gen1Collections = gen1,
            Gen2Collections = gen2,
            Checksum = checksum,
            CanonicalState = expectedState,
        };
    }

    private static ComparisonScenarioResult CreateUnsupported(ComparisonScenario scenario) => new()
    {
        Descriptor = scenario.Descriptor,
        Status = ComparisonScenarioStatus.Unsupported,
        UnsupportedReason = scenario.UnsupportedReason ?? "Not implemented by this generation.",
    };

    private static void ValidateState(
        ScenarioDescriptor descriptor,
        in CanonicalState expected,
        in CanonicalState actual,
        long expectedOperations,
        long actualOperations)
    {
        if (actualOperations != expectedOperations)
            throw new InvalidOperationException($"{descriptor.Id} changed its operation count across iterations.");
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{descriptor.Id} produced non-canonical state. Expected {expected}; actual {actual}.");
        }
    }

    private static bool MatchesFilter(ScenarioDescriptor descriptor, ComparisonRunOptions options) =>
        (string.IsNullOrWhiteSpace(options.ScenarioFilter) ||
         descriptor.Id.Contains(options.ScenarioFilter, StringComparison.OrdinalIgnoreCase)) &&
        (!options.ScaleFilter.HasValue || descriptor.Scale == options.ScaleFilter.Value);

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Consume(long value) => Volatile.Write(ref s_sink, value);

    private static string GetConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}
