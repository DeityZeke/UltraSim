using System.Runtime.InteropServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

/// <summary>Authoritative two-dimensional point used by the game-owned replication broadphase.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct ReplicationSpatialPoint(int XMillimeters, int ZMillimeters);

/// <summary>Stable grid coordinate used only to index replication space.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct ReplicationSpatialRegionId(int X, int Z);

/// <summary>Recipient-relative delivery urgency. It is not identity or payload representation.</summary>
public enum GameInterestTier : byte
{
    None = 0,
    Far = 1,
    Near = 2,
    Immediate = 3,
}

/// <summary>Farthest ordinary dynamic horizon admitted by one game replication family.</summary>
public enum GameReplicationHorizon : byte
{
    None = 0,
    Immediate = 1,
    Near = 2,
    Far = 3,
}

/// <summary>Fixed spatial and cadence evidence profile for one maintained-interest graph.</summary>
public readonly record struct GameInterestProfile(
    int RegionEdgeMillimeters,
    int ImmediateEnterRadiusMillimeters,
    int ImmediateLeaveRadiusMillimeters,
    int NearRadiusMillimeters,
    int FarRadiusMillimeters,
    long NearIntervalMilliseconds,
    long FarIntervalMilliseconds,
    long ApplicationAckRetryMilliseconds,
    long ApplicationAckTimeoutMilliseconds)
{
    public static GameInterestProfile Reference => new(
        RegionEdgeMillimeters: 16_000,
        ImmediateEnterRadiusMillimeters: 5_000,
        ImmediateLeaveRadiusMillimeters: 5_500,
        NearRadiusMillimeters: 96_000,
        FarRadiusMillimeters: 192_000,
        NearIntervalMilliseconds: 33,
        FarIntervalMilliseconds: 50,
        ApplicationAckRetryMilliseconds: 100,
        ApplicationAckTimeoutMilliseconds: 2_000);

    internal void Validate()
    {
        if (RegionEdgeMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(RegionEdgeMillimeters));
        if (ImmediateEnterRadiusMillimeters <= 0 ||
            ImmediateLeaveRadiusMillimeters < ImmediateEnterRadiusMillimeters)
            throw new ArgumentOutOfRangeException(nameof(ImmediateLeaveRadiusMillimeters));
        if (NearRadiusMillimeters < ImmediateLeaveRadiusMillimeters ||
            FarRadiusMillimeters < NearRadiusMillimeters)
            throw new ArgumentOutOfRangeException(nameof(FarRadiusMillimeters));
        if (NearIntervalMilliseconds <= 0 || FarIntervalMilliseconds <= 0 ||
            ApplicationAckRetryMilliseconds <= 0 ||
            ApplicationAckTimeoutMilliseconds < ApplicationAckRetryMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(ApplicationAckTimeoutMilliseconds));
    }
}

/// <summary>Fixed memory bounds for one maintained-interest graph and its application-ACK ledger.</summary>
public readonly record struct GameInterestLimits(
    int MaximumEntities,
    int MaximumRecipients,
    int MaximumRegions,
    int MaximumRegionInterestEdges,
    int MaximumRecipientEntityRelations,
    int MaximumOutstandingBundles,
    int MaximumRecordsPerBundle)
{
    public static GameInterestLimits Default => new(
        MaximumEntities: 4_096,
        MaximumRecipients: 256,
        MaximumRegions: 16_384,
        MaximumRegionInterestEdges: 196_608,
        MaximumRecipientEntityRelations: 262_144,
        MaximumOutstandingBundles: 1_024,
        MaximumRecordsPerBundle: 32);

    internal void Validate()
    {
        if (MaximumEntities <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumEntities));
        if (MaximumRecipients <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumRecipients));
        if (MaximumRegions <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumRegions));
        if (MaximumRegionInterestEdges <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRegionInterestEdges));
        if (MaximumRecipientEntityRelations <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRecipientEntityRelations));
        if (MaximumOutstandingBundles <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutstandingBundles));
        if (MaximumRecordsPerBundle <= 0 || MaximumRecordsPerBundle > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaximumRecordsPerBundle));
    }
}

public enum GameInterestUpdateStatus : byte
{
    Success = 0,
    NoChange = 1,
    InvalidInput = 2,
    StaleVersion = 3,
    CapacityExceeded = 4,
    RecipientNonviable = 5,
    SequenceExhausted = 6,
}

public enum GameWorldStateBuildStatus : byte
{
    Success = 0,
    NoData = 1,
    UnknownRecipient = 2,
    RecipientNonviable = 3,
    DestinationTooSmall = 4,
    ByteBudgetTooSmall = 5,
    OutstandingCapacityExceeded = 6,
    SequenceExhausted = 7,
    TransportNotAccepting = 8,
    BaselineInProgress = 9,
}

public enum GameWorldBaselineCaptureStatus : byte
{
    Success = 0,
    UnknownRecipient = 1,
    RecipientNonviable = 2,
    DestinationTooSmall = 3,
    SequenceExhausted = 4,
}

/// <summary>Shared recipient-independent semantic state cached per canonical version.</summary>
public readonly record struct GameWorldStateSemantic(
    ReplicationEntityId Entity,
    ulong CanonicalVersion,
    GameReplicationHorizon Horizon,
    ReplicationSpatialPoint Position,
    GameSimulationState SimulationState);

/// <summary>One complete recipient-relative present or absent WorldState operation.</summary>
public readonly record struct GameWorldStateRecord(
    ulong RelationSequence,
    ReplicationEntityId Entity,
    ulong CanonicalVersion,
    bool IsPresent,
    GameReplicationHorizon Horizon,
    ReplicationSpatialPoint Position,
    GameSimulationState SimulationState)
{
    public const int MaximumEncodedBytes =
        sizeof(ulong) + sizeof(uint) + sizeof(uint) + sizeof(ulong) + sizeof(byte) +
        sizeof(byte) + sizeof(int) * 4;
}

/// <summary>One bounded recipient bundle awaiting application-level acknowledgement.</summary>
public readonly record struct GameWorldStateBundle(
    ParticipantId Participant,
    StateEpoch StateEpoch,
    ulong BundleId,
    int RecordCount,
    int EstimatedEncodedBytes);

/// <summary>Current maintained relationship state exposed for deterministic tests and diagnostics.</summary>
public readonly record struct GameRecipientEntityRelationSnapshot(
    ParticipantId Participant,
    ReplicationEntityId Entity,
    GameInterestTier Tier,
    bool DesiredPresent,
    ulong KnownCanonicalVersion,
    ulong PendingCanonicalVersion,
    ulong PendingRelationSequence,
    long EligibleAtMilliseconds);

/// <summary>Cold bounded diagnostics for the maintained-interest graph.</summary>
public readonly record struct GameInterestSnapshot(
    StateEpoch StateEpoch,
    int EntityCount,
    int RecipientCount,
    int RegionCount,
    int RegionInterestEdgeCount,
    int RelationCount,
    int PendingRelationCount,
    int OutstandingBundleCount,
    int NonviableRecipientCount,
    ulong PendingWrites,
    ulong UniqueSemanticConstructions,
    ulong SemanticRecordCopies,
    ulong SemanticBytesCopied,
    ulong BundlesBuilt,
    ulong BundlesApplied,
    ulong BundlesRejected,
    ulong TimedOutBundles,
    int LastRegionEdgesAdded,
    int LastRegionEdgesRemoved,
    int LastRegionEdgesRetiered,
    int LastRelationsChanged,
    int LastRelationsExamined,
    long ReservedBytes);

/// <summary>
/// Opt-in cumulative work attribution for maintained WorldState interest and delivery.
/// Disabled production paths do not read the clock or update these counters.
/// </summary>
public readonly record struct GameInterestAttributionSnapshot(
    bool Enabled,
    ulong EntityUpserts,
    ulong StableFarFastPathUpserts,
    ulong GeneralPathUpserts,
    ulong ExistingRelationVisits,
    ulong FastPathRelationVisits,
    ulong FastPathReverseEdgeVisitsAvoided,
    ulong FastPathSecondRelationVisitsAvoided,
    ulong FastPathRelationEnsureLookupsAvoided,
    ulong FastPathImmediateEnters,
    ulong FastPathImmediateLeaves,
    ulong FastPathTierChanges,
    ulong FastPathPendingVersionUpdates,
    ulong ReverseRegionEdgeVisits,
    ulong RelationEnsureLookups,
    ulong PendingRelationVisits,
    ulong ActualRelationChanges,
    ulong PendingVersionRewrites,
    ulong EntityCanonicalTicks,
    ulong RelationMaintenanceTicks,
    ulong BundleBuildCalls,
    ulong BundleBuildSuccesses,
    ulong BundleBuildNoData,
    ulong BundleBuildOutstandingCapacity,
    ulong BundleBuildOtherFailures,
    ulong ExpirationRequests,
    ulong ExpirationSweeps,
    ulong ExpirationDistinctTimeRequests,
    ulong ExpirationRepeatedSameTimeRequests,
    ulong ExpirationSweepCacheHits,
    ulong ExpirationSlotsExamined,
    ulong ExpirationActiveSlotsExamined,
    ulong ExpirationActualExpirations,
    int ExpirationMaximumBundleHigh,
    ulong ExpirationSweepTicks,
    ulong PendingImmediateExamined,
    ulong PendingNearExamined,
    ulong PendingFarExamined,
    ulong PendingSelectedImmediate,
    ulong PendingSelectedNear,
    ulong PendingSelectedFar,
    ulong PendingSkippedAlreadySelected,
    ulong PendingSkippedCadence,
    ulong PendingSkippedAwaitingRetry,
    ulong BundleStopsNoEligibleDebt,
    ulong BundleStopsRecordLimit,
    ulong BundleStopsByteLimit,
    ulong BundleRecords,
    ulong BundleEstimatedBytes,
    int MaximumBundleRecords,
    int MaximumBundleEstimatedBytes,
    ulong ApplicationAcknowledgements,
    ulong AcknowledgementRecordsExamined,
    ulong AcknowledgementTicks);
