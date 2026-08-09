using System.Text.Json.Serialization;

namespace UltraSim.ECS.Comparison;

public enum MeasurementBoundary
{
    HotLoop,
    RecordOnly,
    PlaybackOnly,
    EndToEnd,
}

public enum ComparisonStrength
{
    Direct,
    Provisional,
    Unsupported,
}

public readonly record struct ScenarioMeasurement(long Operations, long Checksum);

public readonly record struct CanonicalState(
    string Hash,
    int LivingEntities,
    int LogicalArchetypes,
    int ChangedEntities,
    int ReusedSlots);

public sealed record ScenarioDescriptor(
    string Id,
    string Name,
    string Category,
    MeasurementBoundary Boundary,
    ComparisonStrength Strength,
    int Scale,
    long OperationsPerIteration,
    long EffectiveBytesPerOperation,
    string SemanticContract,
    string StateContract);

public interface IComparisonWorkload : IDisposable
{
    ScenarioMeasurement Execute();

    CanonicalState CaptureCanonicalState();
}

public sealed class ComparisonScenario
{
    public required ScenarioDescriptor Descriptor { get; init; }

    public Func<IComparisonWorkload>? CreateWorkload { get; init; }

    public string? UnsupportedReason { get; init; }

    public bool IsMeasured => CreateWorkload is not null;
}

public sealed class ComparisonRunOptions
{
    public int WarmupCount { get; init; } = 2;

    public int IterationCount { get; init; } = 9;

    public string? ScenarioFilter { get; init; }

    public int? ScaleFilter { get; init; }
}

public sealed class ComparisonRunReport
{
    public required string ProtocolVersion { get; init; }

    public required string Generation { get; init; }

    public required string KernelProfile { get; init; }

    public required string RuntimeVersion { get; init; }

    public required string FrameworkDescription { get; init; }

    public required string OSDescription { get; init; }

    public required string ProcessArchitecture { get; init; }

    public required string Configuration { get; init; }

    public required string TieredCompilationSetting { get; init; }

    public required string ReadyToRunSetting { get; init; }

    public required bool ServerGarbageCollection { get; init; }

    public required bool ForcesFullCollectionBeforeMeasuredIteration { get; init; }

    public required string AllocationMeasurementScope { get; init; }

    public required int ProcessorCount { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required IReadOnlyList<ComparisonScenarioResult> Results { get; init; }
}

public sealed class ComparisonScenarioResult
{
    public required ScenarioDescriptor Descriptor { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ComparisonScenarioStatus Status { get; init; }

    public string? UnsupportedReason { get; init; }

    public int WarmupCount { get; init; }

    public int IterationCount { get; init; }

    public double MeanNanoseconds { get; init; }

    public double MinNanoseconds { get; init; }

    public double P50Nanoseconds { get; init; }

    public double P95Nanoseconds { get; init; }

    public double P99Nanoseconds { get; init; }

    public double MaxNanoseconds { get; init; }

    public double MeanNanosecondsPerOperation { get; init; }

    public double OperationsPerSecond { get; init; }

    public double EffectiveGiBPerSecond { get; init; }

    public long MeanAllocatedBytes { get; init; }

    public long MaxAllocatedBytes { get; init; }

    public int Gen0Collections { get; init; }

    public int Gen1Collections { get; init; }

    public int Gen2Collections { get; init; }

    public long Checksum { get; init; }

    public CanonicalState CanonicalState { get; init; }
}

public enum ComparisonScenarioStatus
{
    Measured,
    Unsupported,
}

public sealed class CrossGenerationReport
{
    public required string ProtocolVersion { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string CaptureOrder { get; init; }

    public required ComparisonRunReport V2 { get; init; }

    public required ComparisonRunReport V3 { get; init; }

    public required IReadOnlyList<CrossGenerationResult> Results { get; init; }
}

public sealed class CrossGenerationResult
{
    public required ScenarioDescriptor Descriptor { get; init; }

    public required string Status { get; init; }

    public string? Note { get; init; }

    public ComparisonScenarioResult? V2 { get; init; }

    public ComparisonScenarioResult? V3 { get; init; }

    public bool CanonicalStateMatches { get; init; }

    public double V3ToV2MeanRatio { get; init; }

    public double V3ToV2P95Ratio { get; init; }

    public long AllocationDeltaBytes { get; init; }
}

public sealed class BaselineRegressionReport
{
    public required string ProtocolVersion { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required DateTimeOffset BaselineTimestampUtc { get; init; }
    public required DateTimeOffset CurrentTimestampUtc { get; init; }
    public required IReadOnlyList<BaselineRegressionResult> Results { get; init; }
}

public sealed class BaselineRegressionResult
{
    public required ScenarioDescriptor Descriptor { get; init; }
    public required string Status { get; init; }
    public string? Note { get; init; }
    public double V2CurrentToBaselineMeanRatio { get; init; }
    public double V3CurrentToBaselineMeanRatio { get; init; }
    public double V2CurrentToBaselineP95Ratio { get; init; }
    public double V3CurrentToBaselineP95Ratio { get; init; }
    public long V2AllocationDeltaBytes { get; init; }
    public long V3AllocationDeltaBytes { get; init; }
    public bool V2CanonicalStateMatches { get; init; }
    public bool V3CanonicalStateMatches { get; init; }
}
