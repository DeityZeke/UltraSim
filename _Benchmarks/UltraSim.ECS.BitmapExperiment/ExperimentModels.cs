namespace UltraSim.ECS.BitmapExperiment;

internal readonly record struct ExperimentMeasurement(long Operations, long Checksum);

internal interface IExperimentScenario : IDisposable
{
    string LogicalCaseId { get; }
    string Name { get; }
    string Category { get; }
    PrototypeStorageKind Kind { get; }
    int Scale { get; }
    int ArchetypeBytesPerRow { get; }
    int QueryBytesPerRow { get; }
    void Prepare();
    ExperimentMeasurement Execute();
    ulong CaptureCanonicalHash();
    PrototypeTopology CaptureTopology();
    long PayloadBytes { get; }
    long OccupancyMetadataBytes { get; }
    long RowEntityMetadataBytes { get; }
}

internal sealed record ExperimentScenarioDefinition(
    string LogicalCaseId,
    string Name,
    string Category,
    PrototypeStorageKind Kind,
    int Scale,
    Func<IExperimentScenario> Create);

internal sealed class ExperimentReport
{
    public required string SuiteName { get; init; }
    public required string SemanticProfile { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string OperatingSystem { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required int ProcessorCount { get; init; }
    public required int WarmupCount { get; init; }
    public required int IterationCount { get; init; }
    public required int RequestedScale { get; init; }
    public required string? Filter { get; init; }
    public required string ExecutionOrder { get; init; }
    public required uint OrderSeed { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IReadOnlyList<ExperimentResult> Results { get; init; }
}

internal sealed class ExperimentResult
{
    public required string LogicalCaseId { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Variant { get; init; }
    public required int Scale { get; init; }
    public required int ArchetypeBytesPerRow { get; init; }
    public required int QueryBytesPerRow { get; init; }
    public required long OperationsPerIteration { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MinMilliseconds { get; init; }
    public required double P50Milliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double P99Milliseconds { get; init; }
    public required double MaxMilliseconds { get; init; }
    public required double MeanNanosecondsPerOperation { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required long MaxAllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
    public required string CanonicalHash { get; init; }
    public required long PayloadBytes { get; init; }
    public required long OccupancyMetadataBytes { get; init; }
    public required long RowEntityMetadataBytes { get; init; }
    public required double EffectiveGigabytesPerSecond { get; init; }
    public required PrototypeTopology Topology { get; init; }
}
