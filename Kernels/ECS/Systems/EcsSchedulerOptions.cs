using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Fixed scheduler reservations resolved before world construction.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct EcsSchedulerOptions : IEquatable<EcsSchedulerOptions>
{
    public EcsSchedulerOptions(
        int maximumSystems,
        int maximumDependencies,
        int maximumWorkItems,
        int maximumStructuralSegments,
        int backgroundWorkerCount,
        bool callerParticipates = true,
        int structuralCommandsPerProducer = 1_024,
        int structuralPayloadBytesPerProducer = 64 * 1_024,
        int maximumPartitionsPerSystem = 32,
        bool profilingEnabled = false,
        bool phaseStaggeringEnabled = false,
        int maximumStaggerPhases = 10)
    {
        if (maximumSystems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSystems));
        if (maximumDependencies < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDependencies));
        if (maximumWorkItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWorkItems));
        if (maximumStructuralSegments < maximumWorkItems)
            throw new ArgumentOutOfRangeException(nameof(maximumStructuralSegments));
        if (backgroundWorkerCount < 0)
            throw new ArgumentOutOfRangeException(nameof(backgroundWorkerCount));
        if (!callerParticipates && backgroundWorkerCount == 0)
            throw new ArgumentException("At least one execution participant is required.");
        if (structuralCommandsPerProducer < 0)
            throw new ArgumentOutOfRangeException(nameof(structuralCommandsPerProducer));
        if (structuralPayloadBytesPerProducer < 0)
            throw new ArgumentOutOfRangeException(nameof(structuralPayloadBytesPerProducer));
        if (maximumPartitionsPerSystem <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPartitionsPerSystem));
        if (maximumStaggerPhases < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumStaggerPhases));

        MaximumSystems = maximumSystems;
        MaximumDependencies = maximumDependencies;
        MaximumWorkItems = maximumWorkItems;
        MaximumStructuralSegments = maximumStructuralSegments;
        BackgroundWorkerCount = backgroundWorkerCount;
        CallerParticipates = callerParticipates;
        StructuralCommandsPerProducer = structuralCommandsPerProducer;
        StructuralPayloadBytesPerProducer = structuralPayloadBytesPerProducer;
        MaximumPartitionsPerSystem = maximumPartitionsPerSystem;
        ProfilingEnabled = profilingEnabled;
        PhaseStaggeringEnabled = phaseStaggeringEnabled;
        MaximumStaggerPhases = maximumStaggerPhases;
    }

    public static EcsSchedulerOptions Disabled => default;
    public int MaximumSystems { get; }
    public int MaximumDependencies { get; }
    public int MaximumWorkItems { get; }
    public int MaximumStructuralSegments { get; }
    public int BackgroundWorkerCount { get; }
    public bool CallerParticipates { get; }
    public int StructuralCommandsPerProducer { get; }
    public int StructuralPayloadBytesPerProducer { get; }
    public int MaximumPartitionsPerSystem { get; }
    public bool ProfilingEnabled { get; }
    public bool PhaseStaggeringEnabled { get; }
    public int MaximumStaggerPhases { get; }
    public bool IsEnabled => MaximumSystems > 0;
    public int ExecutionConcurrency => BackgroundWorkerCount + (CallerParticipates ? 1 : 0);

    public bool Equals(EcsSchedulerOptions other) =>
        MaximumSystems == other.MaximumSystems && MaximumDependencies == other.MaximumDependencies &&
        MaximumWorkItems == other.MaximumWorkItems && MaximumStructuralSegments == other.MaximumStructuralSegments &&
        BackgroundWorkerCount == other.BackgroundWorkerCount && CallerParticipates == other.CallerParticipates &&
        StructuralCommandsPerProducer == other.StructuralCommandsPerProducer &&
        StructuralPayloadBytesPerProducer == other.StructuralPayloadBytesPerProducer &&
        MaximumPartitionsPerSystem == other.MaximumPartitionsPerSystem && ProfilingEnabled == other.ProfilingEnabled &&
        PhaseStaggeringEnabled == other.PhaseStaggeringEnabled && MaximumStaggerPhases == other.MaximumStaggerPhases;

    public override bool Equals(object? obj) => obj is EcsSchedulerOptions other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(MaximumSystems, MaximumDependencies, MaximumWorkItems,
        MaximumStructuralSegments, BackgroundWorkerCount, CallerParticipates, StructuralCommandsPerProducer,
        StructuralPayloadBytesPerProducer);
    public static bool operator ==(EcsSchedulerOptions left, EcsSchedulerOptions right) => left.Equals(right);
    public static bool operator !=(EcsSchedulerOptions left, EcsSchedulerOptions right) => !left.Equals(right);
}
