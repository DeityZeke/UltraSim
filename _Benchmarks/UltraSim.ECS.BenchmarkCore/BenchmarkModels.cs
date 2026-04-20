namespace UltraSim.ECS.Benchmarks.Core;

public sealed class BenchmarkRunReport
{
    public required string SuiteName { get; init; }
    public required string KernelName { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string Configuration { get; init; }
    public required int ProcessorCount { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IReadOnlyList<BenchmarkResult> Results { get; init; }
}

public sealed class BenchmarkResult
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool Comparable { get; init; }
    public required int Scale { get; init; }
    public required int WarmupCount { get; init; }
    public required int IterationCount { get; init; }
    public required long OperationsPerIteration { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MinMilliseconds { get; init; }
    public required double MaxMilliseconds { get; init; }
    public required double MeanNanosecondsPerOperation { get; init; }
    public required double OperationsPerSecond { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
}
