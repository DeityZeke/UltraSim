using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

/// <summary>Fixed capacities owned by one canonical game projection.</summary>
public readonly record struct GameReplicationLimits(
    int MaximumEntities,
    int MaximumParticipants,
    int MaximumLifecycleFactsPerCapture,
    int MaximumDeltasPerCapture,
    int LifecycleHistoryCapacity,
    int ApplicationEventCapacity,
    int AuthoritativeInputCapacity)
{
    public static GameReplicationLimits Default => new(
        4_096,
        256,
        4_096,
        8_192,
        8_192,
        1_024,
        UltraSimGameComposition.NetworkCommandCapacity);

    internal void Validate()
    {
        if (MaximumEntities <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumEntities));
        if (MaximumParticipants <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumParticipants));
        if (MaximumLifecycleFactsPerCapture <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumLifecycleFactsPerCapture));
        if (MaximumDeltasPerCapture < MaximumLifecycleFactsPerCapture)
            throw new ArgumentOutOfRangeException(nameof(MaximumDeltasPerCapture));
        if (LifecycleHistoryCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(LifecycleHistoryCapacity));
        if (ApplicationEventCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(ApplicationEventCapacity));
        if (AuthoritativeInputCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(AuthoritativeInputCapacity));
    }
}

public enum GameProjectionCaptureStatus : byte
{
    Success = 0,
    BaselineRebuilt = 1,
    RuntimeNotRunning = 2,
    ReplicationNotConfigured = 3,
    CapacityExceeded = 4,
    LifecycleResyncRequired = 5,
    RebuildRequired = 6,
    Faulted = 7,
    /// <summary>
    /// The ECS cursors remain unacknowledged, but the bounded capture interval no
    /// longer fits this projection. Rebuild a baseline before capturing again.
    /// </summary>
    CaptureResyncRequired = 8,
}

public enum GameProjectionDeltaKind : byte
{
    Created = 1,
    Destroyed = 2,
    ComponentAdded = 3,
    ComponentRemoved = 4,
    SimulationStateChanged = 5,
}

/// <summary>Canonical client-semantic state for one projected entity.</summary>
public readonly record struct GameProjectedEntity(
    ReplicationEntityId Entity,
    bool HasSimulationState,
    GameSimulationState SimulationState);

/// <summary>Current participant-to-canonical-projection binding.</summary>
public readonly record struct GameProjectionParticipant(
    ParticipantId Participant,
    ReplicationEntityId ControlledEntity,
    ulong ServerGenerationId);

/// <summary>Current canonical owner binding and input-admission proof.</summary>
public readonly record struct GameProjectionOwnerState(
    GameProjectionParticipant Binding,
    GameProjectedEntity Entity,
    ulong LastAcceptedInputSequence);

/// <summary>One ordered semantic change produced by a successful projection capture.</summary>
public readonly record struct GameProjectionDelta(
    ulong Sequence,
    GameProjectionDeltaKind Kind,
    ReplicationEntityId Entity,
    ComponentId Component,
    bool HasSimulationState,
    GameSimulationState SimulationState);

/// <summary>One retained projection-lifecycle fact derived from committed ECS topology.</summary>
public readonly record struct GameProjectionLifecycleFact(
    ulong Sequence,
    ulong SourceSequence,
    LifecycleKind Kind,
    ReplicationEntityId Entity,
    ComponentId Component);

public enum GameProjectionHistoryReadStatus : byte
{
    NoData = 0,
    Success = 1,
    HistoryLost = 2,
    /// <summary>The requested cursor is ahead of the producer's published sequence.</summary>
    FutureSequence = 3,
}

public readonly record struct GameProjectionHistoryReadResult(
    GameProjectionHistoryReadStatus Status,
    int Count,
    ulong NextSequence,
    ulong OldestRetainedSequence,
    ulong NextPublishedSequence);

/// <summary>Header for one immediately consumable OfflineDirect projection frame.</summary>
public readonly record struct GameProjectionFrame(
    StateEpoch StateEpoch,
    ulong ServerGenerationId,
    ulong SimulationTick,
    ulong BaselineRevision,
    ulong PreviousFrameSequence,
    ulong FrameSequence,
    ulong NextDeltaSequence,
    int DeltaCount,
    ulong LifecycleOldestSequence,
    ulong LifecycleNextSequence,
    ulong ApplicationEventOldestSequence,
    ulong ApplicationEventNextSequence);

/// <summary>
/// Cold diagnostics for the currently published canonical projection.
/// <see cref="ReservedBytes"/> includes fixed arrays and the reserved participant
/// map bucket/entry payload; CLR object/array headers are excluded.
/// </summary>
public readonly record struct GameProjectionSnapshot(
    StateEpoch StateEpoch,
    ulong ServerGenerationId,
    ulong BaselineRevision,
    ulong FrameSequence,
    int EntityCount,
    int ParticipantCount,
    int LastDeltaCount,
    bool RebuildRequired,
    bool IsFaulted,
    ulong LifecycleOverwrittenCount,
    int RetainedApplicationEvents,
    int ApplicationEventConsumers,
    int RetainedApplicationEventHighWater,
    ulong ApplicationEventMinimumCursorScanEntries,
    int PendingAuthoritativeInputs,
    long ReservedBytes);

/// <summary>Result of one post-step canonical capture attempt.</summary>
public readonly record struct GameProjectionCaptureResult(
    GameProjectionCaptureStatus Status,
    GameProjectionFrame Frame,
    int LifecycleFacts,
    int ChangedRows,
    string? Message)
{
    public bool Succeeded => Status is GameProjectionCaptureStatus.Success or GameProjectionCaptureStatus.BaselineRebuilt;
    public bool RequiresBaseline => Status is GameProjectionCaptureStatus.BaselineRebuilt or
        GameProjectionCaptureStatus.LifecycleResyncRequired or GameProjectionCaptureStatus.RebuildRequired or
        GameProjectionCaptureStatus.CaptureResyncRequired;
}

/// <summary>One non-coalescible game fact retained independently from projection lifecycle.</summary>
public readonly record struct GameApplicationEvent(
    ulong Sequence,
    StateEpoch StateEpoch,
    ParticipantId Participant,
    ushort Kind,
    ulong Subject,
    ulong PayloadHash);

/// <summary>One bounded external intent awaiting authoritative scheduler admission.</summary>
public readonly record struct GameAuthoritativeInput(
    ParticipantId Participant,
    ulong ClientSequence,
    GameSimulationCommandKind Kind,
    int TickMarkerDelta)
{
    public GameAuthoritativeInput(
        ParticipantId participant,
        ulong clientSequence,
        int tickMarkerDelta)
        : this(participant, clientSequence, GameSimulationCommandKind.ApplyTickMarkerDelta, tickMarkerDelta)
    {
    }
}

public enum GameAuthoritativeIngressStatus : byte
{
    Success = 0,
    NoData = 1,
    RuntimeNotRunning = 2,
    ReplicationNotConfigured = 3,
    GenerationChanged = 4,
    SchedulerCapacityExceeded = 5,
}

public readonly record struct GameAuthoritativeIngressResult(
    GameAuthoritativeIngressStatus Status,
    int Accepted,
    int Rejected);

public enum OfflineProjectionApplyStatus : byte
{
    Success = 0,
    BaselineRequired = 1,
    CapacityExceeded = 2,
    InvalidDelta = 3,
}

public readonly record struct OfflineProjectionSnapshot(
    StateEpoch StateEpoch,
    ulong ServerGenerationId,
    ulong BaselineRevision,
    ulong FrameSequence,
    int EntityCount,
    int ParticipantCount,
    ulong NextApplicationEventSequence,
    NetworkTraceDigest SemanticTraceDigest,
    ulong CanonicalHash,
    long ReservedBytes);
