using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraSim.ECS.ExtremeSphere;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SphereExecutionMode
{
    Serial,
    Parallel,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpherePipeline
{
    Split3,
    Fused2,
}

public sealed record SphereRunConfiguration(
    int EntityCount,
    int WarmupTicks,
    int MeasuredTicks,
    SphereExecutionMode Mode,
    SpherePipeline Pipeline,
    int BackgroundWorkers,
    uint Seed,
    float SpawnRadius,
    float DeltaSeconds,
    int ChunkSizeKiB);

public readonly record struct TimingDistribution(
    double MeanMicroseconds,
    double MinMicroseconds,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double P999Microseconds,
    double MaxMicroseconds,
    double StandardDeviationMicroseconds)
{
    public static TimingDistribution FromStopwatchTicks(ReadOnlySpan<long> samples)
    {
        if (samples.IsEmpty)
            return default;

        var values = new double[samples.Length];
        double total = 0;
        for (int index = 0; index < samples.Length; index++)
        {
            double microseconds = samples[index] * 1_000_000d / Stopwatch.Frequency;
            values[index] = microseconds;
            total += microseconds;
        }

        double mean = total / values.Length;
        double variance = 0;
        for (int index = 0; index < values.Length; index++)
        {
            double delta = values[index] - mean;
            variance += delta * delta;
        }
        Array.Sort(values);
        return new TimingDistribution(
            mean,
            values[0],
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99),
            Percentile(values, 0.999),
            values[^1],
            Math.Sqrt(variance / values.Length));
    }

    private static double Percentile(ReadOnlySpan<double> sorted, double percentile)
    {
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)position;
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}

public readonly record struct MemorySnapshot(
    long ManagedBytes,
    long GcHeapBytes,
    long GcCommittedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    long ReservedEcsPayloadBytes,
    long NativeChunkPayloadBytes)
{
    public static MemorySnapshot Capture(long reservedEcsPayloadBytes = 0, long nativeChunkPayloadBytes = 0)
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            gc.HeapSizeBytes,
            gc.TotalCommittedBytes,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            reservedEcsPayloadBytes,
            nativeChunkPayloadBytes);
    }
}

public readonly record struct AllocationMetrics(
    long AllThreadsBytes,
    long CallerThreadBytes,
    long InstrumentedWorkerBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

public readonly record struct SpawnMetrics(
    double ElapsedMilliseconds,
    double NanosecondsPerEntity,
    long AllThreadsAllocatedBytes,
    long CallerThreadAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

public readonly record struct CanonicalSphereState(
    string Hash,
    int Rows,
    long NonFiniteValues,
    double MeanRadius,
    double MinRadius,
    double MaxRadius,
    long FloatBitChecksum);

public readonly record struct SphereSystemTiming(
    string Name,
    long RowsPerTick,
    long EffectiveBytesPerEntity,
    TimingDistribution Timing,
    double MeanNanosecondsPerRow,
    double EffectiveGiBPerSecond);

public sealed class ExtremeSphereRunReport
{
    public required string ProtocolVersion { get; init; }
    public required string Generation { get; init; }
    public required string KernelProfile { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string FrameworkDescription { get; init; }
    public required string OSDescription { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required int ProcessorCount { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required SphereRunConfiguration Configuration { get; init; }
    public required SpawnMetrics Spawn { get; init; }
    public required TimingDistribution TickTiming { get; init; }
    public required double MeanNanosecondsPerEntityTick { get; init; }
    public required double EntityTicksPerSecond { get; init; }
    public required double EffectiveGiBPerSecond { get; init; }
    public required IReadOnlyList<SphereSystemTiming> Systems { get; init; }
    public required AllocationMetrics MeasurementAllocations { get; init; }
    public required MemorySnapshot BeforeWorldMemory { get; init; }
    public required MemorySnapshot AfterSeedMemory { get; init; }
    public required MemorySnapshot AfterMeasurementMemory { get; init; }
    public required CanonicalSphereState CanonicalState { get; init; }
    public required long SchedulerDispatches { get; init; }
    public required long SchedulerWorkerWakes { get; init; }
    public required long SchedulerWorkerParks { get; init; }
    public required long TotalRowsVisited { get; init; }
    public required string AllocationScope { get; init; }
    public required string Notes { get; init; }
}

public static class ExtremeSphereJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Write(ExtremeSphereRunReport report, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(report, Options));
    }

    public static ExtremeSphereRunReport Read(string path) =>
        JsonSerializer.Deserialize<ExtremeSphereRunReport>(File.ReadAllText(path), Options)
        ?? throw new InvalidOperationException($"Could not deserialize extreme-sphere report '{path}'.");

    public static string FrameworkDescription => RuntimeInformation.FrameworkDescription;
    public static string OSDescription => RuntimeInformation.OSDescription;
    public static string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
}
