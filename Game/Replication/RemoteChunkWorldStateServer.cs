using System.Runtime.CompilerServices;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteChunkWorldStateTransportLimits(
    int MaximumOutstandingDeliveries,
    ulong MaximumSnapshotTransferSteps = 1_024)
{
    public static RemoteChunkWorldStateTransportLimits Default => new(1_024);

    internal void Validate()
    {
        if (MaximumOutstandingDeliveries <= 0 || MaximumSnapshotTransferSteps == 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutstandingDeliveries));
    }
}

public enum RemoteChunkWorldStateScheduleStatus : byte
{
    QueuedDelta = 1,
    QueuedAbsence = 2,
    NoPayload = 3,
    NotDue = 4,
    InvalidSubscription = 5,
    SourceRequiresReconcile = 6,
    ReadPending = 7,
    CapacityExceeded = 8,
    FutureCursor = 9,
    SnapshotRequired = 10,
    PayloadTooLarge = 11,
    DeliverySequenceExhausted = 12,
    TransportNotAccepting = 13,
    SnapshotTransferStarted = 14,
    SnapshotTransferBusy = 15,
}

public readonly record struct RemoteChunkWorldStateServerSnapshot(
    int OutstandingDeliveries,
    int OutstandingDeliveryCapacity,
    int MaximumUnfragmentedMessageBytes,
    int MaximumDeltaRecordsPerDatagram,
    ulong NextDeliveryId,
    ulong DeltasQueued,
    ulong AbsencesQueued,
    ulong AppliedProofs,
    ulong RejectedProofs,
    ulong MalformedProofs,
    ulong InvalidatedDeliveries,
    ulong OversizeDeltas,
    bool SnapshotTransferActive,
    bool SnapshotCancelPending,
    int SnapshotSegmentsQueued,
    ulong SnapshotsQueued,
    ulong SnapshotCancels,
    ulong SnapshotTimeouts,
    long ReservedBytes,
    long BundleScratchBytes,
    long BundleEntryAndRecordStagingBytes,
    long BundleTicketAndMetadataStagingBytes,
    long RetainedDetailedDiagnosticBytes,
    OfflineDirectChunkRecipientSnapshot Recipient,
    RemoteChunkReplicationTimingSnapshot Timing);

public readonly record struct RemoteChunkReplicationTimingSnapshot(
    GameTelemetryLevel Level,
    GameTimingDistribution InterestReconciliation,
    GameTimingDistribution EncodingAndScheduling,
    GameTimingDistribution QueueDrain,
    GameTimingDistribution ApplicationProof,
    OfflineDirectChunkReconcileAttribution ReconcileAttribution,
    RemoteChunkReplicationWorkSnapshot Work,
    RemoteChunkKnowledgeCensusSnapshot Knowledge);

/// <summary>PF4B bounded census of Delta-120 production for one recipient lifetime/window.</summary>
public readonly record struct RemoteChunkDeltaCensusSnapshot(
    ulong Messages,
    ulong ApplicationPayloadBytes,
    ulong Records,
    ulong ImmediateMessages,
    ulong NearMessages,
    ulong FarMessages,
    int UniqueChunks,
    bool UniqueChunkCensusOverflowed,
    ulong OverlappingSameChunkMessages,
    ulong OverlappingApplicationPayloadBytes,
    ulong SameChunkMessagesInOneAuthorityStep,
    ulong ExactRepeatedRecords,
    ulong ExactRepeatedRecordBytes,
    ulong RepeatedEntityIdentities,
    ulong RepeatedEntityRecordBytes,
    ulong RepeatedFieldBits,
    ulong OverlappedRevisionSpan,
    GameValueDistribution RecordsPerDelta,
    GameValueDistribution RevisionSpan,
    GameValueDistribution SameChunkOutstandingBeforeQueue);

/// <summary>PF4B bounded census of Proof-130 processing and actual knowledge advancement.</summary>
public readonly record struct RemoteChunkProofCensusSnapshot(
    ulong EnvelopesReceived,
    ulong LegacyProofMessagesReceived,
    ulong EnvelopeApplicationPayloadBytes,
    ulong EntriesReceived,
    ulong AppliedEntries,
    ulong RejectedEntries,
    ulong CapacityExceededEntries,
    ulong ContradictoryEntries,
    ulong DeltaEntries,
    ulong SnapshotEntries,
    ulong AbsenceEntries,
    ulong KnowledgeAdvancingEntries,
    ulong NonAdvancingAppliedEntries,
    ulong ExactOutstandingRetired,
    ulong SupersededOutstandingRetired,
    ulong MalformedOrUnknownEntries,
    GameValueDistribution EntriesPerEnvelope,
    GameValueDistribution DeliveryAgeAuthoritySteps,
    GameTimingDistribution DeliveryQueuedToProofProcessed);

/// <summary>PF4B bounded current and window ledger observations.</summary>
public readonly record struct RemoteChunkOutstandingCensusSnapshot(
    int Current,
    int HighWater,
    int CurrentChunksWithMultipleDeliveries,
    int CurrentMaximumPerChunk,
    ulong CurrentOldestAgeAuthoritySteps,
    ulong InvalidatedByAoiLeave,
    ulong InvalidatedBySourceReplacement,
    ulong InvalidatedByRecovery,
    ulong InvalidatedByLaterAppliedTruth);

/// <summary>PF4C physical-representation census for selected-profile message 129.</summary>
public readonly record struct RemoteChunkBundleCensusSnapshot(
    ulong Bundles,
    ulong Entries,
    ulong ApplicationPayloadBytes,
    ulong StandaloneApplicationPayloadBytes,
    ulong StandaloneMessagesReplaced,
    ulong GameMessagesSaved,
    ulong SharedHeaderBytesSaved,
    ulong SingleCandidatesEmittedStandalone,
    ulong FullByEntryCount,
    ulong FullByByteLimit,
    GameValueDistribution EntriesPerBundle,
    GameValueDistribution BytesPerBundle,
    GameValueDistribution MtuUtilizationPermille);

/// <summary>Authority-step freshness for the latest source truth included in one publication.</summary>
public readonly record struct RemoteChunkFreshnessTierSnapshot(
    ulong Publications,
    GameValueDistribution SourceToDueSteps,
    GameValueDistribution DueToNetworkQueueSteps,
    GameValueDistribution SourceToNetworkQueueSteps,
    GameValueDistribution SourceToProofSteps);

public readonly record struct RemoteChunkFreshnessCensusSnapshot(
    RemoteChunkFreshnessTierSnapshot Immediate,
    RemoteChunkFreshnessTierSnapshot Near,
    RemoteChunkFreshnessTierSnapshot Far);

public readonly record struct RemoteChunkTierTrafficSnapshot(
    ulong DuePublications,
    ulong DeltaRecords,
    ulong StandaloneCandidateApplicationBytes,
    ulong BundledEntries,
    ulong StandaloneEntries,
    ulong ProofEntries,
    ulong SelfPublicRecords);

/// <summary>PF4E record and representation census; candidate bytes are pre-bundle encoded bytes.</summary>
public readonly record struct RemoteChunkPublicationCensusSnapshot(
    ulong DeltaRecords,
    ulong SnapshotRecords,
    ulong AbsencePublications,
    int UniquePublicEntities,
    bool UniquePublicEntityCensusOverflowed,
    ulong SelfPublicRecords,
    ulong OtherPublicRecords,
    ulong SelfPublicRecordBytesUpperBound,
    GameValueDistribution DeltaPayloadBytes,
    ulong PayloadBytesAtMost128,
    ulong PayloadBytesAtMost256,
    ulong PayloadBytesAtMost512,
    ulong PayloadBytesAtMost1024,
    ulong PayloadBytesOver1024,
    RemoteChunkTierTrafficSnapshot Immediate,
    RemoteChunkTierTrafficSnapshot Near,
    RemoteChunkTierTrafficSnapshot Far);

/// <summary>
/// Read-only knowledge census. Physical UDP/frame accounting remains owned by Network; these are
/// Game-level messages, records, exact delivery debt, and application-proof effects.
/// </summary>
public readonly record struct RemoteChunkKnowledgeCensusSnapshot(
    RemoteChunkDeltaCensusSnapshot Delta,
    RemoteChunkProofCensusSnapshot Proof,
    RemoteChunkOutstandingCensusSnapshot Outstanding,
    RemoteChunkBundleCensusSnapshot Bundle,
    RemoteChunkFreshnessCensusSnapshot Freshness,
    RemoteChunkPublicationCensusSnapshot Publication);

/// <summary>Measurement-window work/debt attribution for the selected chunk adapter.</summary>
public readonly record struct RemoteChunkReplicationWorkSnapshot(
    ulong ScheduleAttempts,
    ulong DeliveriesQueued,
    ulong ImmediateDeliveriesQueued,
    ulong NearDeliveriesQueued,
    ulong FarDeliveriesQueued,
    ulong NoPayload,
    ulong CadenceDeferrals,
    ulong TransportDeferrals,
    ulong CapacityDeferrals,
    int OutstandingHighWater);

/// <summary>
/// N1.2/N1.3 connection-owned delivery lifecycle for ordinary chunk state and exceptional atomic
/// Bulk snapshots. Publication knowledge advances only through an authenticated application proof
/// retained in the bounded ledger. Segmented ordinary deltas remain out of scope.
/// </summary>
public sealed class RemoteChunkWorldStateServer : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    public static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.ChunkPublication;

    private readonly NetworkProtocolPeer _peer;
    private readonly OfflineDirectChunkRecipient _recipient;
    private ChunkPublicationStore _source;
    private readonly OfflineDirectChunkWireEncoder _encoder;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _messageScratch;
    private readonly byte[] _bundleScratch;
    private readonly RemoteDirtyChunkBundleEntry[] _bundleEntries;
    private readonly RemoteChunkDeltaRecord[] _bundleDeltaRecords;
    private readonly OfflineDirectChunkWireEncodeResult[] _bundleResults;
    private readonly ChunkInterestTier[] _bundleTiers;
    private readonly ulong[] _bundleSourceSteps;
    private readonly ulong[] _bundleDueSteps;
    private readonly int[] _bundleStandaloneBytes;
    private readonly int[] _bundleWorkPositions;
    private readonly int[] _bundleSubscriptionIndices;
    private readonly byte[] _snapshotPayload;
    private readonly OutstandingDelivery[] _outstanding;
    private readonly ulong _maximumSnapshotTransferSteps;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _outstandingCount;
    private ulong _nextDeliveryId = 1;
    private ulong _deltasQueued;
    private ulong _absencesQueued;
    private ulong _appliedProofs;
    private ulong _rejectedProofs;
    private ulong _malformedProofs;
    private ulong _invalidatedDeliveries;
    private ulong _oversizeDeltas;
    private OfflineDirectChunkDeliveryTicket _snapshotTicket;
    private RemoteChunkSnapshotHeader _snapshotHeader;
    private int _snapshotLength;
    private int _snapshotSegmentPayloadBytes;
    private int _snapshotSegmentCount;
    private int _snapshotNextSegment;
    private bool _snapshotBeginQueued;
    private ulong _snapshotStartedStep;
    private RemoteChunkSnapshotCancelMessage _pendingCancel;
    private int _snapshotSegmentsQueued;
    private ulong _snapshotsQueued;
    private ulong _snapshotCancels;
    private ulong _snapshotTimeouts;
    private GameTelemetryLevel _telemetryLevel;
    private readonly GameOperationTimingWindow _reconcileTiming = new();
    private readonly GameOperationTimingWindow _scheduleTiming = new();
    private readonly GameOperationTimingWindow _progressTiming = new();
    private readonly GameOperationTimingWindow _proofTiming = new();
    private readonly GameOperationTimingWindow _deliveryProofLatency = new();
    private readonly GameValueDistributionWindow _deltaRecords = new();
    private readonly GameValueDistributionWindow _deltaRevisionSpan = new();
    private readonly GameValueDistributionWindow _sameChunkOutstanding = new();
    private readonly GameValueDistributionWindow _proofEnvelopeOccupancy = new();
    private readonly GameValueDistributionWindow _deliveryAgeSteps = new();
    private readonly SpatialChunkKey[] _timedUniqueDeltaChunks;
    private readonly bool[] _timedUniqueDeltaChunkOccupied;
    private WorldPublicEntityId[]? _timedUniquePublicEntities;
    private bool[]? _timedUniquePublicEntityOccupied;
    private RemoteChunkDeltaRecord[]? _timedDeltaScratch;
    private RemoteChunkDeltaRecord[]? _timedOutstandingDeltaRecords;
    private ushort[]? _timedOutstandingDeltaRecordCounts;
    private ulong _timedScheduleAttempts;
    private ulong _timedDeliveriesQueued;
    private ulong _timedImmediateQueued;
    private ulong _timedNearQueued;
    private ulong _timedFarQueued;
    private ulong _timedNoPayload;
    private ulong _timedCadenceDeferrals;
    private ulong _timedTransportDeferrals;
    private ulong _timedCapacityDeferrals;
    private int _timedOutstandingHighWater;
    private int _timedUniqueDeltaChunkCount;
    private bool _timedUniqueDeltaChunkOverflowed;
    private int _timedUniquePublicEntityCount;
    private bool _timedUniquePublicEntityOverflowed;
    private WorldPublicEntityId _controlledPublicEntity;
    private ulong _lastServiceStep;
    private ulong _timedDeltaMessages;
    private ulong _timedDeltaBytes;
    private ulong _timedDeltaRecordCount;
    private ulong _timedDeltaImmediate;
    private ulong _timedDeltaNear;
    private ulong _timedDeltaFar;
    private ulong _timedSnapshotRecords;
    private ulong _timedAbsencePublications;
    private ulong _timedSelfPublicRecords;
    private ulong _timedOtherPublicRecords;
    private GameValueDistributionWindow? _deltaPayloadBytes;
    private readonly ulong[] _deltaPayloadBuckets = new ulong[5];
    private readonly TierTrafficWindow[] _tierTraffic =
        [new(), new(), new()];
    private ulong _timedOverlappingDeltaMessages;
    private ulong _timedOverlappingDeltaBytes;
    private ulong _timedSameStepDeltaMessages;
    private ulong _timedExactRepeatedRecords;
    private ulong _timedExactRepeatedRecordBytes;
    private ulong _timedRepeatedEntityIdentities;
    private ulong _timedRepeatedFieldBits;
    private ulong _timedOverlappedRevisionSpan;
    private ulong _timedProofEnvelopes;
    private ulong _timedLegacyProofMessages;
    private ulong _timedProofEnvelopeBytes;
    private ulong _timedProofEntries;
    private ulong _timedProofApplied;
    private ulong _timedProofRejected;
    private ulong _timedProofCapacity;
    private ulong _timedProofContradictory;
    private ulong _timedProofDelta;
    private ulong _timedProofSnapshot;
    private ulong _timedProofAbsence;
    private ulong _timedKnowledgeAdvancements;
    private ulong _timedNonAdvancingApplied;
    private ulong _timedExactDebtRetired;
    private ulong _timedSupersededDebtRetired;
    private ulong _timedMalformedProofEntries;
    private ulong _timedInvalidatedAoi;
    private ulong _timedInvalidatedSource;
    private ulong _timedInvalidatedRecovery;
    private ulong _timedBundles;
    private ulong _timedBundleEntries;
    private ulong _timedBundleBytes;
    private ulong _timedBundleStandaloneBytes;
    private ulong _timedBundleMessagesReplaced;
    private ulong _timedBundleMessagesSaved;
    private ulong _timedBundleHeaderBytesSaved;
    private ulong _timedSingleCandidates;
    private ulong _timedBundleFullByEntries;
    private ulong _timedBundleFullByBytes;
    private GameValueDistributionWindow? _bundleOccupancy;
    private GameValueDistributionWindow? _bundleEncodedBytes;
    private GameValueDistributionWindow? _bundleMtuUtilization;
    private TierFreshnessWindow? _immediateFreshness;
    private TierFreshnessWindow? _nearFreshness;
    private TierFreshnessWindow? _farFreshness;

    public RemoteChunkWorldStateServer(
        NetworkProtocolPeer peer,
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore source,
        ulong applicationSessionEpoch,
        RemoteChunkWorldStateTransportLimits limits = default)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _recipient = recipient ?? throw new ArgumentNullException(nameof(recipient));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (limits == default) limits = RemoteChunkWorldStateTransportLimits.Default;
        limits.Validate();
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (recipient.Limits.MaximumSnapshotRecords > UltraSimGameProtocol.MaximumChunkPublicationRecords ||
            recipient.Limits.MaximumDeltaRecords > UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(recipient));
        _applicationSessionEpoch = applicationSessionEpoch;
        _encoder = new OfflineDirectChunkWireEncoder(
            applicationSessionEpoch,
            Math.Max(recipient.Limits.MaximumSnapshotRecords, recipient.Limits.MaximumDeltaRecords));
        _messageScratch = new byte[Math.Max(
            UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(recipient.Limits.MaximumSnapshotRecords),
            UltraSimGameProtocol.GetChunkDeltaEncodedBytes(recipient.Limits.MaximumDeltaRecords))];
        _bundleScratch = new byte[peer.MaximumUnfragmentedMessageBytes];
        _bundleEntries = new RemoteDirtyChunkBundleEntry[
            UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        // One unfragmented 129 envelope cannot contain more than 64 fixed-size delta records
        // under the accepted protocol maximum. This remains independently bounded from telemetry.
        int maximumBundledDeltaRecords = Math.Min(
            UltraSimGameProtocol.MaximumDirtyChunkBundleEntries *
                UltraSimGameProtocol.MaximumChunkPublicationRecords,
            Math.Max(UltraSimGameProtocol.MaximumChunkPublicationRecords,
                peer.MaximumUnfragmentedMessageBytes / RemoteChunkDeltaRecord.EncodedSize));
        _bundleDeltaRecords = new RemoteChunkDeltaRecord[maximumBundledDeltaRecords];
        _bundleResults = new OfflineDirectChunkWireEncodeResult[
            UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _bundleTiers = new ChunkInterestTier[UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _bundleSourceSteps = new ulong[UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _bundleDueSteps = new ulong[UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _bundleStandaloneBytes = new int[UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _bundleWorkPositions = new int[UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _bundleSubscriptionIndices = new int[UltraSimGameProtocol.MaximumDirtyChunkBundleEntries];
        _snapshotPayload = new byte[
            UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(recipient.Limits.MaximumSnapshotRecords)];
        int segmentPayloadBytes = peer.MaximumUnfragmentedMessageBytes -
            RemoteChunkSnapshotSegmentHeaderCodec.EncodedSize;
        if (segmentPayloadBytes <= 0 ||
            (_snapshotPayload.Length + segmentPayloadBytes - 1) / segmentPayloadBytes >
                UltraSimGameProtocol.MaximumChunkSnapshotSegments)
            throw new ArgumentOutOfRangeException(nameof(peer),
                "The transport cannot carry the configured snapshot within the bounded segment count.");
        _outstanding = new OutstandingDelivery[limits.MaximumOutstandingDeliveries];
        _maximumSnapshotTransferSteps = limits.MaximumSnapshotTransferSteps;
        // Window-unique chunks are not bounded by the current subscription count. Size this cold
        // census to the source's bounded resident-key capacity and surface saturation explicitly
        // rather than faulting a healthy application if a long diagnostic window sees churn.
        int chunkSetCapacity = 1;
        long targetChunkSetCapacity = checked((long)source.Limits.MaximumResidentChunks * 2);
        while (chunkSetCapacity < targetChunkSetCapacity)
            chunkSetCapacity = checked(chunkSetCapacity * 2);
        _timedUniqueDeltaChunks = new SpatialChunkKey[chunkSetCapacity];
        _timedUniqueDeltaChunkOccupied = new bool[chunkSetCapacity];
    }

    public OfflineDirectChunkRecipient Recipient => _recipient;

    public void SetControlledPublicEntity(WorldPublicEntityId value)
    {
        EnsureOwner();
        if (!value.IsValid) throw new ArgumentException("A controlled public identity is required.", nameof(value));
        _controlledPublicEntity = value;
    }

    public void ConfigureTelemetry(GameTelemetryLevel level)
    {
        EnsureOwner();
        _telemetryLevel = level;
        _reconcileTiming.Configure(level);
        _scheduleTiming.Configure(level);
        _progressTiming.Configure(level);
        _proofTiming.Configure(level);
        _deliveryProofLatency.Configure(level);
        _deltaRecords.Clear();
        _deltaRevisionSpan.Clear();
        _sameChunkOutstanding.Clear();
        _proofEnvelopeOccupancy.Clear();
        _deliveryAgeSteps.Clear();
        _recipient.ResetTelemetryWindow(level == GameTelemetryLevel.Detailed);
        _timedScheduleAttempts = _timedDeliveriesQueued = 0;
        _timedImmediateQueued = _timedNearQueued = _timedFarQueued = 0;
        _timedNoPayload = _timedCadenceDeferrals = 0;
        _timedTransportDeferrals = _timedCapacityDeferrals = 0;
        _timedOutstandingHighWater = _outstandingCount;
        _timedUniqueDeltaChunkCount = 0;
        _timedUniqueDeltaChunkOverflowed = false;
        Array.Clear(_timedUniqueDeltaChunks);
        Array.Clear(_timedUniqueDeltaChunkOccupied);
        _timedUniquePublicEntityCount = 0;
        _timedUniquePublicEntityOverflowed = false;
        if (_timedUniquePublicEntities is not null) Array.Clear(_timedUniquePublicEntities);
        if (_timedUniquePublicEntityOccupied is not null)
            Array.Clear(_timedUniquePublicEntityOccupied);
        _timedDeltaMessages = _timedDeltaBytes = _timedDeltaRecordCount = 0;
        _timedDeltaImmediate = _timedDeltaNear = _timedDeltaFar = 0;
        _timedSnapshotRecords = _timedAbsencePublications = 0;
        _timedSelfPublicRecords = _timedOtherPublicRecords = 0;
        _deltaPayloadBytes?.Clear();
        Array.Clear(_deltaPayloadBuckets);
        for (int index = 0; index < _tierTraffic.Length; index++) _tierTraffic[index].Clear();
        _timedOverlappingDeltaMessages = _timedOverlappingDeltaBytes = 0;
        _timedSameStepDeltaMessages = 0;
        _timedExactRepeatedRecords = _timedExactRepeatedRecordBytes = 0;
        _timedRepeatedEntityIdentities = _timedRepeatedFieldBits = 0;
        _timedOverlappedRevisionSpan = 0;
        _timedProofEnvelopes = _timedLegacyProofMessages = 0;
        _timedProofEnvelopeBytes = _timedProofEntries = 0;
        _timedProofApplied = _timedProofRejected = 0;
        _timedProofCapacity = _timedProofContradictory = 0;
        _timedProofDelta = _timedProofSnapshot = _timedProofAbsence = 0;
        _timedKnowledgeAdvancements = _timedNonAdvancingApplied = 0;
        _timedExactDebtRetired = _timedSupersededDebtRetired = 0;
        _timedMalformedProofEntries = 0;
        _timedInvalidatedAoi = _timedInvalidatedSource = _timedInvalidatedRecovery = 0;
        _timedBundles = _timedBundleEntries = _timedBundleBytes = 0;
        _timedBundleStandaloneBytes = _timedBundleMessagesReplaced = 0;
        _timedBundleMessagesSaved = _timedBundleHeaderBytesSaved = 0;
        _timedSingleCandidates = _timedBundleFullByEntries = _timedBundleFullByBytes = 0;
        if (level == GameTelemetryLevel.Detailed)
        {
            int entitySetCapacity = 1;
            long targetEntitySetCapacity = checked((long)_source.Limits.MaximumPublicEntities * 2);
            while (entitySetCapacity < targetEntitySetCapacity)
                entitySetCapacity = checked(entitySetCapacity * 2);
            _timedUniquePublicEntities ??= new WorldPublicEntityId[entitySetCapacity];
            _timedUniquePublicEntityOccupied ??= new bool[entitySetCapacity];
            _deltaPayloadBytes ??= new GameValueDistributionWindow();
            _timedDeltaScratch ??= new RemoteChunkDeltaRecord[
                _recipient.Limits.MaximumDeltaRecords];
            _timedOutstandingDeltaRecords ??= new RemoteChunkDeltaRecord[checked(
                _outstanding.Length * _recipient.Limits.MaximumDeltaRecords)];
            _timedOutstandingDeltaRecordCounts ??= new ushort[_outstanding.Length];
            _bundleOccupancy ??= new GameValueDistributionWindow();
            _bundleEncodedBytes ??= new GameValueDistributionWindow();
            _bundleMtuUtilization ??= new GameValueDistributionWindow();
            _immediateFreshness ??= new TierFreshnessWindow();
            _nearFreshness ??= new TierFreshnessWindow();
            _farFreshness ??= new TierFreshnessWindow();
        }
        _bundleOccupancy?.Clear();
        _bundleEncodedBytes?.Clear();
        _bundleMtuUtilization?.Clear();
        _immediateFreshness?.Clear();
        _nearFreshness?.Clear();
        _farFreshness?.Clear();
        if (_timedOutstandingDeltaRecordCounts is not null)
            Array.Clear(_timedOutstandingDeltaRecordCounts);
        if (_timedOutstandingDeltaRecords is not null)
            Array.Clear(_timedOutstandingDeltaRecords);
    }

    public RemoteChunkReplicationTimingSnapshot GetTimingSnapshot()
    {
        EnsureOwner();
        return new RemoteChunkReplicationTimingSnapshot(
            _telemetryLevel,
            _reconcileTiming.Capture(),
            _scheduleTiming.Capture(),
            _progressTiming.Capture(),
            _proofTiming.Capture(),
            _recipient.GetReconcileAttribution(),
            new RemoteChunkReplicationWorkSnapshot(
                _timedScheduleAttempts, _timedDeliveriesQueued,
                _timedImmediateQueued, _timedNearQueued, _timedFarQueued,
                _timedNoPayload, _timedCadenceDeferrals, _timedTransportDeferrals,
                _timedCapacityDeferrals, _timedOutstandingHighWater),
            CaptureKnowledgeCensus());
    }

    /// <summary>
    /// Rebinds a surviving connection adapter to a replacement generation's publication source.
    /// Every outstanding delivery belongs to the prior publication epoch and is invalidated before
    /// the next required recipient reconciliation.
    /// </summary>
    public void ReplaceSource(ChunkPublicationStore source)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(_source, source)) return;
        if (source.Epoch == _source.Epoch)
            throw new ArgumentException(
                "A replacement publication source must own a distinct epoch.", nameof(source));
        InvalidateAll(OutstandingInvalidationReason.SourceReplacement);
        _source = source;
    }

    /// <summary>Test-only injection for the monotonic delivery-ID exhaustion boundary.</summary>
    internal void SetNextDeliveryIdForTesting(ulong nextDeliveryId)
    {
        EnsureOwner();
        _nextDeliveryId = nextDeliveryId;
    }

    /// <summary>
    /// Reconciles game-owned chunk interest and invalidates delivery lineage for every leave or
    /// publication-epoch replacement before a key can be admitted as a new subscription lifetime.
    /// </summary>
    public OfflineDirectChunkReconcileStatus Reconcile(
        SpatialSpaceId space,
        in WorldPosition anchor,
        Span<OfflineDirectChunkSubscriptionChange> changes,
        out OfflineDirectChunkReconcileResult result)
    {
        EnsureOwner();
        long timing = _reconcileTiming.StartDetailed();
        OfflineDirectChunkReconcileStatus status;
        try
        {
            status = _recipient.Reconcile(_source, space, anchor, changes, out result);
        }
        finally { _reconcileTiming.RecordDetailed(timing); }
        if (status != OfflineDirectChunkReconcileStatus.Reconciled) return status;
        if (result.SourceReset)
        {
            InvalidateAll(OutstandingInvalidationReason.SourceReplacement);
            return status;
        }
        for (int index = 0; index < result.ChangeCount; index++)
            if (changes[index].Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
                Invalidate(changes[index].Key, OutstandingInvalidationReason.AoiLeave);
        return status;
    }

    public RemoteChunkWorldStateScheduleStatus TrySchedule(int subscriptionIndex, ulong cadenceStep)
    {
        OfflineDirectChunkSubscriptionView subscriptions = _recipient.Subscriptions;
        ChunkInterestTier tier = (uint)subscriptionIndex < (uint)subscriptions.Length
            ? subscriptions[subscriptionIndex].Interest.Tier
            : default;
        return TrySchedule(subscriptionIndex, cadenceStep, cadenceStep, cadenceStep, tier, out _);
    }

    private RemoteChunkWorldStateScheduleStatus TrySchedule(
        int subscriptionIndex,
        ulong cadenceStep,
        ulong latestSourceStep,
        ulong firstDueStep,
        ChunkInterestTier tier,
        out int applicationBytesQueued)
    {
        EnsureOwner();
        applicationBytesQueued = 0;
        _lastServiceStep = Math.Max(_lastServiceStep, cadenceStep);
        long timing = _scheduleTiming.StartDetailed();
        RemoteChunkWorldStateScheduleStatus status;
        try
        {
            status = TryScheduleCore(subscriptionIndex, cadenceStep, latestSourceStep,
                firstDueStep, tier, out applicationBytesQueued);
        }
        finally { _scheduleTiming.RecordDetailed(timing); }
        RecordScheduleResult(subscriptionIndex, status);
        return status;
    }

    private RemoteChunkWorldStateScheduleStatus TryScheduleCore(
        int subscriptionIndex,
        ulong cadenceStep,
        ulong latestSourceStep,
        ulong firstDueStep,
        ChunkInterestTier tier,
        out int applicationBytesQueued)
    {
        applicationBytesQueued = 0;
        if (_nextDeliveryId == 0)
            return RemoteChunkWorldStateScheduleStatus.DeliverySequenceExhausted;
        ulong deliveryId = _nextDeliveryId;
        OfflineDirectChunkWireEncodeStatus encoded = _encoder.TryEncode(
            _recipient, _source, subscriptionIndex, cadenceStep, deliveryId,
            _messageScratch, out OfflineDirectChunkWireEncodeResult result);
        RemoteChunkWorldStateScheduleStatus mapped = encoded switch
        {
            OfflineDirectChunkWireEncodeStatus.NoPayload => RemoteChunkWorldStateScheduleStatus.NoPayload,
            OfflineDirectChunkWireEncodeStatus.NotDue => RemoteChunkWorldStateScheduleStatus.NotDue,
            OfflineDirectChunkWireEncodeStatus.InvalidSubscription =>
                RemoteChunkWorldStateScheduleStatus.InvalidSubscription,
            OfflineDirectChunkWireEncodeStatus.SourceRequiresReconcile =>
                RemoteChunkWorldStateScheduleStatus.SourceRequiresReconcile,
            OfflineDirectChunkWireEncodeStatus.ReadPending => RemoteChunkWorldStateScheduleStatus.ReadPending,
            OfflineDirectChunkWireEncodeStatus.CapacityExceeded =>
                RemoteChunkWorldStateScheduleStatus.CapacityExceeded,
            OfflineDirectChunkWireEncodeStatus.FutureCursor => RemoteChunkWorldStateScheduleStatus.FutureCursor,
            OfflineDirectChunkWireEncodeStatus.EncodingFailed =>
                RemoteChunkWorldStateScheduleStatus.TransportNotAccepting,
            _ => default,
        };
        // A snapshot is one bounded transaction for this application consumer. Do not replace it
        // while its exact proof is still in flight: a paced transport can otherwise cancel a valid
        // snapshot every simulation tick before the proof returns. Ordinary WorldState deltas and
        // absences remain independent and may progress beside the active Bulk transaction.
        if (encoded == OfflineDirectChunkWireEncodeStatus.Snapshot && HasOutstandingSnapshot())
            return RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy;
        if (encoded == OfflineDirectChunkWireEncodeStatus.Snapshot)
            return StartSnapshotTransfer(in result, cadenceStep);
        if (encoded is not (OfflineDirectChunkWireEncodeStatus.Delta or
            OfflineDirectChunkWireEncodeStatus.Absence))
            return mapped;
        // An absence contains no newer semantic state that can supersede an earlier unproved
        // absence for the same subscription lifetime. Retaining duplicates only consumes the
        // bounded independent-state lane and can delay the one snapshot which establishes an
        // occupied chunk. Deltas remain coalescible while their revisions advance.
        if (encoded == OfflineDirectChunkWireEncodeStatus.Absence &&
            HasOutstandingAbsence(result.Ticket.Key))
            return RemoteChunkWorldStateScheduleStatus.NotDue;
        if (result.Written > _peer.MaximumUnfragmentedMessageBytes)
        {
            SaturatingIncrement(ref _oversizeDeltas);
            return RemoteChunkWorldStateScheduleStatus.PayloadTooLarge;
        }
        if (!HasOutstandingCapacity())
            return RemoteChunkWorldStateScheduleStatus.CapacityExceeded;

        NetworkMessageId messageId = result.Kind switch
        {
            RemoteChunkPublicationKind.Delta => RemoteChunkDeltaHeaderCodec.Descriptor.Id,
            RemoteChunkPublicationKind.Absence => RemoteChunkAbsenceMessageCodec.Descriptor.Id,
            _ => throw new InvalidOperationException("N1.2 only transports deltas and absences."),
        };
        if (_peer.TryQueue(messageId, _messageScratch.AsSpan(0, result.Written)) !=
            NetworkQueueStatus.Accepted)
            return RemoteChunkWorldStateScheduleStatus.TransportNotAccepting;
        applicationBytesQueued = result.Written;

        int retainedIndex = _outstandingCount;
        _outstanding[retainedIndex] = new OutstandingDelivery(
            result.Ticket,
            cadenceStep,
            latestSourceStep,
            firstDueStep,
            tier,
            _telemetryLevel == GameTelemetryLevel.Detailed
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0);
        if (result.Kind == RemoteChunkPublicationKind.Delta)
        {
            RecordQueuedDelta(in result, subscriptionIndex, cadenceStep, retainedIndex);
            RecordRepresentation(tier, bundled: false);
        }
        else
        {
            RecordAbsence(tier, bundled: false);
        }
        RecordFreshnessQueued(tier, latestSourceStep, firstDueStep, cadenceStep);
        _outstandingCount++;
        _timedOutstandingHighWater = Math.Max(_timedOutstandingHighWater, _outstandingCount);
        _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;
        if (result.Kind == RemoteChunkPublicationKind.Delta)
        {
            SaturatingIncrement(ref _deltasQueued);
            return RemoteChunkWorldStateScheduleStatus.QueuedDelta;
        }
        SaturatingIncrement(ref _absencesQueued);
        return RemoteChunkWorldStateScheduleStatus.QueuedAbsence;
    }

    /// <summary>
    /// Schedules one game-discovered chunk scope without treating the recipient's sorted-array
    /// position as durable identity.
    /// </summary>
    public RemoteChunkWorldStateScheduleStatus TrySchedule(SpatialChunkKey key, ulong cadenceStep)
    {
        EnsureOwner();
        int subscriptionIndex = FindSubscription(key);
        return subscriptionIndex < 0
            ? RemoteChunkWorldStateScheduleStatus.InvalidSubscription
            : TrySchedule(subscriptionIndex, cadenceStep);
    }

    public RemoteChunkWorldStateScheduleStatus TrySchedule(
        in ChunkPublicationWork work,
        ulong cadenceStep)
        => TrySchedule(in work, cadenceStep, out _);

    public RemoteChunkWorldStateScheduleStatus TrySchedule(
        in ChunkPublicationWork work,
        ulong cadenceStep,
        out int applicationBytesQueued)
    {
        EnsureOwner();
        int subscriptionIndex = FindSubscription(work.Key);
        if (subscriptionIndex < 0)
        {
            applicationBytesQueued = 0;
            return RemoteChunkWorldStateScheduleStatus.InvalidSubscription;
        }
        return TrySchedule(subscriptionIndex, cadenceStep, work.LatestSourceStep,
            work.FirstDueStep, work.Tier, out applicationBytesQueued);
    }

    /// <summary>
    /// Materializes one bounded same-recipient service pass. Two or more already-due ordinary
    /// publications share message 129 when that encoding is physically smaller. A singleton uses
    /// its canonical 120/121 message, and snapshots retain the segmented 123/124/125 transaction.
    /// The returned count is the exact prefix whose statuses are final for this pass.
    /// </summary>
    public int TryScheduleDueBundle(
        ReadOnlySpan<ChunkPublicationWork> works,
        ulong cadenceStep,
        Span<RemoteChunkWorldStateScheduleStatus> statuses,
        out int gameMessagesQueued)
        => TryScheduleDueBundle(
            works, cadenceStep, statuses, out gameMessagesQueued, out _, out _);

    public int TryScheduleDueBundle(
        ReadOnlySpan<ChunkPublicationWork> works,
        ulong cadenceStep,
        Span<RemoteChunkWorldStateScheduleStatus> statuses,
        out int gameMessagesQueued,
        out int applicationBytesQueued,
        out int bundlesQueued)
    {
        EnsureOwner();
        if (statuses.Length < works.Length) throw new ArgumentException(
            "The schedule-status destination is smaller than the due-work source.", nameof(statuses));
        gameMessagesQueued = 0;
        applicationBytesQueued = 0;
        bundlesQueued = 0;
        if (works.IsEmpty) return 0;
        _lastServiceStep = Math.Max(_lastServiceStep, cadenceStep);
        long timing = _scheduleTiming.StartDetailed();
        try
        {
            return TryScheduleDueBundleCore(
                works, cadenceStep, statuses, out gameMessagesQueued,
                out applicationBytesQueued, out bundlesQueued);
        }
        finally { _scheduleTiming.RecordDetailed(timing); }
    }

    private int TryScheduleDueBundleCore(
        ReadOnlySpan<ChunkPublicationWork> works,
        ulong cadenceStep,
        Span<RemoteChunkWorldStateScheduleStatus> statuses,
        out int gameMessagesQueued,
        out int applicationBytesQueued,
        out int bundlesQueued)
    {
        gameMessagesQueued = 0;
        applicationBytesQueued = 0;
        bundlesQueued = 0;
        int processed = 0;
        int staged = 0;
        int deltaRecords = 0;
        int bundleBytes = RemoteDirtyChunkBundleHeaderCodec.EncodedSize;
        int standaloneBytes = 0;
        bool fullByByteLimit = false;
        int maximumEntries = Math.Min(
            UltraSimGameProtocol.MaximumDirtyChunkBundleEntries,
            _outstanding.Length - _outstandingCount);
        if (maximumEntries == 0)
        {
            statuses[0] = RemoteChunkWorldStateScheduleStatus.CapacityExceeded;
            RecordScheduleResult(FindSubscription(works[0].Key), statuses[0]);
            return 1;
        }

        while (processed < works.Length && staged < maximumEntries)
        {
            ref readonly ChunkPublicationWork work = ref works[processed];
            int subscriptionIndex = FindSubscription(work.Key);
            if (subscriptionIndex < 0)
            {
                statuses[processed] = RemoteChunkWorldStateScheduleStatus.InvalidSubscription;
                RecordScheduleResult(subscriptionIndex, statuses[processed]);
                processed++;
                continue;
            }
            if (_nextDeliveryId == 0 ||
                (ulong)staged > ulong.MaxValue - _nextDeliveryId)
            {
                if (staged != 0) break;
                statuses[processed] = RemoteChunkWorldStateScheduleStatus.DeliverySequenceExhausted;
                RecordScheduleResult(subscriptionIndex, statuses[processed]);
                return processed + 1;
            }

            ulong deliveryId = _nextDeliveryId + (ulong)staged;
            OfflineDirectChunkWireEncodeStatus encoded = _encoder.TryEncode(
                _recipient, _source, subscriptionIndex, cadenceStep, deliveryId,
                _messageScratch, out OfflineDirectChunkWireEncodeResult result);
            RemoteChunkWorldStateScheduleStatus mapped = Map(encoded);
            if (encoded == OfflineDirectChunkWireEncodeStatus.Snapshot)
            {
                if (staged != 0) break;
                statuses[processed] = HasOutstandingSnapshot()
                    ? RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy
                    : StartSnapshotTransfer(in result, cadenceStep);
                RecordScheduleResult(subscriptionIndex, statuses[processed]);
                return processed + 1;
            }
            if (encoded is not (OfflineDirectChunkWireEncodeStatus.Delta or
                OfflineDirectChunkWireEncodeStatus.Absence))
            {
                if (staged != 0 && IsTransient(mapped)) break;
                statuses[processed] = mapped;
                RecordScheduleResult(subscriptionIndex, mapped);
                processed++;
                if (IsTransient(mapped)) break;
                continue;
            }
            if (encoded == OfflineDirectChunkWireEncodeStatus.Absence &&
                HasOutstandingAbsence(result.Ticket.Key))
            {
                statuses[processed] = RemoteChunkWorldStateScheduleStatus.NotDue;
                RecordScheduleResult(subscriptionIndex, statuses[processed]);
                processed++;
                continue;
            }
            if (result.Written > _peer.MaximumUnfragmentedMessageBytes)
            {
                SaturatingIncrement(ref _oversizeDeltas);
                statuses[processed] = RemoteChunkWorldStateScheduleStatus.PayloadTooLarge;
                RecordScheduleResult(subscriptionIndex, statuses[processed]);
                processed++;
                continue;
            }

            int entryBytes = UltraSimGameProtocol.GetDirtyChunkBundleEntryEncodedBytes(
                result.Kind, result.RecordCount);
            if (bundleBytes + entryBytes > _peer.MaximumUnfragmentedMessageBytes)
            {
                // The bundle envelope is only a representation candidate. Its larger header may
                // exceed a custom peer limit even though this first canonical 120/121 message
                // fits. Retain that first entry so the singleton path below queues the canonical
                // standalone bytes. Once a prior entry exists, the bundle really is full and the
                // next independently valid publication remains pending for the next service pass.
                if (staged != 0)
                {
                    fullByByteLimit = true;
                    break;
                }
            }

            int recordOffset = deltaRecords;
            ulong contentHash = 0;
            if (result.Kind == RemoteChunkPublicationKind.Delta)
            {
                if (!UltraSimGameProtocol.TryReadChunkDelta(
                        _messageScratch.AsSpan(0, result.Written),
                        out RemoteChunkDeltaHeader header,
                        _bundleDeltaRecords.AsSpan(deltaRecords), out int decoded) ||
                    decoded != result.RecordCount ||
                    header.DeliveryId != result.Ticket.DeliveryId ||
                    header.BaseRevision != result.BaseRevision ||
                    header.CurrentRevision != result.CurrentRevision ||
                    deltaRecords + decoded > _bundleDeltaRecords.Length)
                    throw new InvalidOperationException(
                        "A canonical selected-profile delta could not enter bundle staging.");
                deltaRecords += decoded;
                contentHash = header.ContentHash;
            }
            else if (!UltraSimGameProtocol.TryReadChunkAbsence(
                         _messageScratch.AsSpan(0, result.Written),
                         out RemoteChunkAbsenceMessage absence) ||
                     absence.DeliveryId != result.Ticket.DeliveryId)
            {
                throw new InvalidOperationException(
                    "A canonical selected-profile absence could not enter bundle staging.");
            }

            if (staged == 0)
                _messageScratch.AsSpan(0, result.Written).CopyTo(_bundleScratch);
            _bundleEntries[staged] = new RemoteDirtyChunkBundleEntry(
                result.Kind,
                result.Ticket.DeliveryId,
                result.Ticket.Key.Space.Value,
                result.Ticket.Key.Coord.X,
                result.Ticket.Key.Coord.Z,
                result.BaseRevision,
                result.CurrentRevision,
                result.HistoryFloor,
                contentHash,
                checked((ushort)result.RecordCount),
                result.Kind == RemoteChunkPublicationKind.Delta ? recordOffset : 0);
            _bundleResults[staged] = result;
            _bundleTiers[staged] = work.Tier;
            _bundleSourceSteps[staged] = work.LatestSourceStep;
            _bundleDueSteps[staged] = work.FirstDueStep;
            _bundleStandaloneBytes[staged] = result.Written;
            _bundleWorkPositions[staged] = processed;
            _bundleSubscriptionIndices[staged] = subscriptionIndex;
            bundleBytes += entryBytes;
            standaloneBytes = checked(standaloneBytes + result.Written);
            staged++;
            processed++;
        }

        if (staged == 0) return processed;
        NetworkMessageId messageId;
        int written;
        bool bundled = staged >= 2 && bundleBytes < standaloneBytes;
        int materializedEntries = staged;
        if (bundled)
        {
            if (!UltraSimGameProtocol.TryWriteDirtyChunkBundle(
                    _bundleScratch, _applicationSessionEpoch, _source.Epoch.Value,
                    _bundleResults[0].Ticket.DeliveryId,
                    _bundleEntries.AsSpan(0, staged), [],
                    _bundleDeltaRecords.AsSpan(0, deltaRecords), out written) ||
                written != bundleBytes)
                throw new InvalidOperationException(
                    "Preflighted selected-profile bundle materialization diverged.");
            messageId = RemoteDirtyChunkBundleHeaderCodec.Descriptor.Id;
        }
        else
        {
            staged = 1;
            processed = Math.Min(processed, _bundleWorkPositions[0] + 1);
            written = _bundleStandaloneBytes[0];
            messageId = _bundleResults[0].Kind == RemoteChunkPublicationKind.Delta
                ? RemoteChunkDeltaHeaderCodec.Descriptor.Id
                : RemoteChunkAbsenceMessageCodec.Descriptor.Id;
        }

        if (_peer.TryQueue(messageId, _bundleScratch.AsSpan(0, written)) !=
            NetworkQueueStatus.Accepted)
        {
            for (int index = 0; index < staged; index++)
            {
                int position = _bundleWorkPositions[index];
                statuses[position] = RemoteChunkWorldStateScheduleStatus.TransportNotAccepting;
                RecordScheduleResult(_bundleSubscriptionIndices[index], statuses[position]);
            }
            ClearBundleStaging(materializedEntries, deltaRecords);
            return processed;
        }

        long queuedTimestamp = _telemetryLevel == GameTelemetryLevel.Detailed
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0;
        for (int index = 0; index < staged; index++)
        {
            OfflineDirectChunkWireEncodeResult result = _bundleResults[index];
            int retainedIndex = _outstandingCount;
            _outstanding[retainedIndex] = new OutstandingDelivery(
                result.Ticket, cadenceStep, _bundleSourceSteps[index],
                _bundleDueSteps[index], _bundleTiers[index], queuedTimestamp);
            if (result.Kind == RemoteChunkPublicationKind.Delta)
            {
                ReadOnlySpan<RemoteChunkDeltaRecord> records =
                    _bundleDeltaRecords.AsSpan(_bundleEntries[index].RecordOffset,
                        _bundleEntries[index].RecordCount);
                RecordQueuedDelta(in result, _bundleSubscriptionIndices[index], cadenceStep,
                    retainedIndex, records);
                RecordRepresentation(_bundleTiers[index], bundled);
                SaturatingIncrement(ref _deltasQueued);
                statuses[_bundleWorkPositions[index]] =
                    RemoteChunkWorldStateScheduleStatus.QueuedDelta;
            }
            else
            {
                RecordAbsence(_bundleTiers[index], bundled);
                SaturatingIncrement(ref _absencesQueued);
                statuses[_bundleWorkPositions[index]] =
                    RemoteChunkWorldStateScheduleStatus.QueuedAbsence;
            }
            _outstandingCount++;
            RecordFreshnessQueued(_bundleTiers[index], _bundleSourceSteps[index],
                _bundleDueSteps[index], cadenceStep);
            RecordScheduleResult(_bundleSubscriptionIndices[index],
                statuses[_bundleWorkPositions[index]]);
        }
        _timedOutstandingHighWater = Math.Max(_timedOutstandingHighWater, _outstandingCount);
        AdvanceDeliveryIds(staged);
        gameMessagesQueued = 1;
        applicationBytesQueued = written;
        bundlesQueued = bundled ? 1 : 0;
        if (bundled) RecordBundleQueued(staged, written, standaloneBytes, fullByByteLimit);
        else if (_telemetryLevel == GameTelemetryLevel.Detailed)
            SaturatingIncrement(ref _timedSingleCandidates);
        ClearBundleStaging(materializedEntries, deltaRecords);
        return processed;
    }

    /// <summary>
    /// Starts a complete repair snapshot regardless of the retained chunk cursor. This is the
    /// explicit recovery route after an MTU-oversize delta or client-declared semantic rejection.
    /// </summary>
    public RemoteChunkWorldStateScheduleStatus TryScheduleSnapshot(
        int subscriptionIndex,
        ulong serviceStep)
    {
        EnsureOwner();
        _lastServiceStep = Math.Max(_lastServiceStep, serviceStep);
        if (_nextDeliveryId == 0)
            return RemoteChunkWorldStateScheduleStatus.DeliverySequenceExhausted;
        if (HasOutstandingSnapshot())
            return RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy;
        OfflineDirectChunkWireEncodeStatus encoded = _encoder.TryEncodeSnapshot(
            _recipient, _source, subscriptionIndex, _nextDeliveryId,
            _messageScratch, out OfflineDirectChunkWireEncodeResult result);
        if (encoded == OfflineDirectChunkWireEncodeStatus.Snapshot)
            return StartSnapshotTransfer(in result, serviceStep);
        if (encoded == OfflineDirectChunkWireEncodeStatus.Absence)
            return QueueEncodedOrdinary(in result, serviceStep);
        return encoded switch
        {
            OfflineDirectChunkWireEncodeStatus.InvalidSubscription =>
                RemoteChunkWorldStateScheduleStatus.InvalidSubscription,
            OfflineDirectChunkWireEncodeStatus.SourceRequiresReconcile =>
                RemoteChunkWorldStateScheduleStatus.SourceRequiresReconcile,
            OfflineDirectChunkWireEncodeStatus.ReadPending =>
                RemoteChunkWorldStateScheduleStatus.ReadPending,
            OfflineDirectChunkWireEncodeStatus.CapacityExceeded =>
                RemoteChunkWorldStateScheduleStatus.CapacityExceeded,
            _ => RemoteChunkWorldStateScheduleStatus.TransportNotAccepting,
        };
    }

    /// <summary>
    /// Expires the one bounded snapshot transaction without pinning publication history. The
    /// reliable cancel is retained across transport backpressure and changes no applied cursor.
    /// </summary>
    public bool ExpireSnapshotTransfer(ulong serviceStep)
    {
        EnsureOwner();
        _lastServiceStep = Math.Max(_lastServiceStep, serviceStep);
        if (_snapshotTicket.IsValid &&
            serviceStep >= _snapshotStartedStep &&
            serviceStep - _snapshotStartedStep >= _maximumSnapshotTransferSteps)
        {
            CancelActiveSnapshot();
            RecordInvalidation(OutstandingInvalidationReason.Recovery);
            SaturatingIncrement(ref _snapshotTimeouts);
            return true;
        }
        for (int index = 0; index < _outstandingCount; index++)
        {
            ref readonly OutstandingDelivery delivery = ref _outstanding[index];
            if (delivery.Ticket.Status != OfflineDirectChunkReadStatus.Snapshot ||
                serviceStep < delivery.StartedStep ||
                serviceStep - delivery.StartedStep < _maximumSnapshotTransferSteps)
                continue;
            QueueCancel(delivery.Ticket);
            RemoveAt(index);
            RecordInvalidation(OutstandingInvalidationReason.Recovery);
            SaturatingIncrement(ref _snapshotTimeouts);
            return true;
        }
        return false;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        long timing = _progressTiming.StartDetailed();
        try
        {
            return TryProgressCore();
        }
        finally { _progressTiming.RecordDetailed(timing); }
    }

    private bool TryProgressCore()
    {
        if (_pendingCancel.DeliveryId != 0)
        {
            if (!UltraSimGameProtocol.TryWriteChunkSnapshotCancel(
                    _messageScratch, in _pendingCancel, out int cancelBytes))
                throw new InvalidOperationException("A retained snapshot cancel became invalid.");
            if (_peer.TryQueue(RemoteChunkSnapshotCancelMessageCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, cancelBytes)) != NetworkQueueStatus.Accepted)
                return false;
            _pendingCancel = default;
            SaturatingIncrement(ref _snapshotCancels);
        }
        if (!_snapshotTicket.IsValid) return true;

        if (!_snapshotBeginQueued)
        {
            var begin = new RemoteChunkSnapshotBeginMessage
            {
                ApplicationSessionEpoch = _applicationSessionEpoch,
                PublicationEpoch = _snapshotHeader.PublicationEpoch,
                DeliveryId = _snapshotHeader.DeliveryId,
                SpatialSpaceId = _snapshotHeader.SpatialSpaceId,
                ChunkX = _snapshotHeader.ChunkX,
                ChunkZ = _snapshotHeader.ChunkZ,
                SchemaFingerprint = _snapshotHeader.SchemaFingerprint,
                Revision = _snapshotHeader.Revision,
                HistoryFloor = _snapshotHeader.HistoryFloor,
                ContentHash = _snapshotHeader.ContentHash,
                EncodedBytes = checked((uint)_snapshotLength),
                RecordCount = _snapshotHeader.RecordCount,
                SegmentCount = checked((ushort)_snapshotSegmentCount),
                SegmentPayloadBytes = checked((ushort)_snapshotSegmentPayloadBytes),
            };
            if (!UltraSimGameProtocol.TryWriteChunkSnapshotBegin(
                    _messageScratch, in begin, out int beginBytes))
                throw new InvalidOperationException("A retained snapshot Begin became invalid.");
            if (_peer.TryQueue(RemoteChunkSnapshotBeginMessageCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, beginBytes)) != NetworkQueueStatus.Accepted)
                return false;
            _snapshotBeginQueued = true;
        }

        while (_snapshotNextSegment < _snapshotSegmentCount)
        {
            int offset = checked(_snapshotNextSegment * _snapshotSegmentPayloadBytes);
            int count = Math.Min(_snapshotSegmentPayloadBytes, _snapshotLength - offset);
            var header = new RemoteChunkSnapshotSegmentHeader
            {
                ApplicationSessionEpoch = _applicationSessionEpoch,
                PublicationEpoch = _snapshotHeader.PublicationEpoch,
                DeliveryId = _snapshotHeader.DeliveryId,
                SegmentIndex = checked((ushort)_snapshotNextSegment),
                SegmentCount = checked((ushort)_snapshotSegmentCount),
                PayloadBytes = checked((ushort)count),
            };
            if (!UltraSimGameProtocol.TryWriteChunkSnapshotSegment(
                    _messageScratch, in header, _snapshotPayload.AsSpan(offset, count),
                    out int segmentBytes))
                throw new InvalidOperationException("A retained snapshot segment became invalid.");
            if (segmentBytes > _peer.MaximumUnfragmentedMessageBytes)
                throw new InvalidOperationException("A snapshot segment exceeded its admitted envelope.");
            if (_peer.TryQueue(RemoteChunkSnapshotSegmentHeaderCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, segmentBytes)) != NetworkQueueStatus.Accepted)
                return false;
            _snapshotNextSegment++;
            _snapshotSegmentsQueued++;
        }

        var commit = new RemoteChunkSnapshotCommitMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            PublicationEpoch = _snapshotHeader.PublicationEpoch,
            DeliveryId = _snapshotHeader.DeliveryId,
            SpatialSpaceId = _snapshotHeader.SpatialSpaceId,
            ChunkX = _snapshotHeader.ChunkX,
            ChunkZ = _snapshotHeader.ChunkZ,
            Revision = _snapshotHeader.Revision,
            ContentHash = _snapshotHeader.ContentHash,
        };
        if (!UltraSimGameProtocol.TryWriteChunkSnapshotCommit(
                _messageScratch, in commit, out int commitBytes))
            throw new InvalidOperationException("A retained snapshot Commit became invalid.");
        if (_peer.TryQueue(RemoteChunkSnapshotCommitMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, commitBytes)) != NetworkQueueStatus.Accepted)
            return false;
        _outstanding[_outstandingCount++] = new OutstandingDelivery(
            _snapshotTicket,
            _snapshotStartedStep,
            _snapshotStartedStep,
            _snapshotStartedStep,
            TierForKey(_snapshotTicket.Key),
            _telemetryLevel == GameTelemetryLevel.Detailed
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0);
        _timedOutstandingHighWater = Math.Max(_timedOutstandingHighWater, _outstandingCount);
        ClearActiveSnapshot();
        SaturatingIncrement(ref _snapshotsQueued);
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        long timing = _proofTiming.StartDetailed();
        try
        {
            return TryProcessCore(in message, payload);
        }
        finally { _proofTiming.RecordDetailed(timing); }
    }

    private bool TryProcessCore(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        if (message.MessageId == RemoteChunkAppliedMessageCodec.Descriptor.Id)
        {
            if (!UltraSimGameProtocol.TryReadChunkApplied(
                    payload, out RemoteChunkAppliedMessage proof))
            {
                SaturatingIncrement(ref _malformedProofs);
                RecordMalformedProof();
            }
            else
            {
                if (_telemetryLevel == GameTelemetryLevel.Detailed)
                {
                    SaturatingIncrement(ref _timedLegacyProofMessages);
                    SaturatingIncrement(ref _timedProofEntries);
                }
                ProcessProof(in proof);
            }
            return true;
        }
        if (message.MessageId != RemoteChunkAppliedEnvelopeHeaderCodec.Descriptor.Id) return false;
        Span<RemoteChunkAppliedEnvelopeEntry> entries =
            stackalloc RemoteChunkAppliedEnvelopeEntry[
                UltraSimGameProtocol.MaximumChunkAppliedEnvelopeEntries];
        if (!UltraSimGameProtocol.TryReadChunkAppliedEnvelope(
                payload, out RemoteChunkAppliedEnvelopeHeader header, entries, out int count) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch)
        {
            SaturatingIncrement(ref _malformedProofs);
            RecordMalformedProof();
            return true;
        }
        if (_telemetryLevel == GameTelemetryLevel.Detailed)
        {
            SaturatingIncrement(ref _timedProofEnvelopes);
            SaturatingAdd(ref _timedProofEnvelopeBytes, checked((ulong)payload.Length));
            SaturatingAdd(ref _timedProofEntries, checked((ulong)count));
            _proofEnvelopeOccupancy.Record(checked((ulong)count));
        }
        for (int entry = 0; entry < count; entry++)
        {
            RemoteChunkAppliedMessage proof = entries[entry].ToMessage(
                header.ApplicationSessionEpoch, header.PublicationEpoch);
            ProcessProof(in proof);
        }
        return true;
    }

    private void ProcessProof(in RemoteChunkAppliedMessage proof)
    {
        int index = FindDelivery(proof.DeliveryId);
        if (index < 0 || !Matches(_outstanding[index].Ticket, proof))
        {
            SaturatingIncrement(ref _malformedProofs);
            RecordMalformedProof();
            return;
        }

        OutstandingDelivery retained = _outstanding[index];
        OfflineDirectChunkDeliveryTicket ticket = retained.Ticket;
        RecordProofEntry(in proof);
        if (_telemetryLevel == GameTelemetryLevel.Detailed)
            SaturatingIncrement(ref TierTraffic(retained.Tier).ProofEntries);
        RecordDeliveryLatency(in retained);
        if (proof.Status != RemoteChunkPublicationApplyStatus.Applied)
        {
            RemoveAt(index);
            if (_telemetryLevel == GameTelemetryLevel.Detailed)
                SaturatingIncrement(ref _timedExactDebtRetired);
            SaturatingIncrement(ref _rejectedProofs);
            return;
        }
        if (!_encoder.TryCommitAppliedProof(
                _recipient, ticket, proof, out OfflineDirectChunkAppliedProofStatus status))
        {
            RemoveAt(index);
            if (_telemetryLevel == GameTelemetryLevel.Detailed)
            {
                SaturatingIncrement(ref _timedExactDebtRetired);
                SaturatingIncrement(ref _timedMalformedProofEntries);
            }
            SaturatingIncrement(ref _malformedProofs);
            return;
        }
        RemoveAt(index);
        if (_telemetryLevel == GameTelemetryLevel.Detailed)
            SaturatingIncrement(ref _timedExactDebtRetired);
        if (status is OfflineDirectChunkAppliedProofStatus.Applied or
            OfflineDirectChunkAppliedProofStatus.Duplicate or
            OfflineDirectChunkAppliedProofStatus.Stale)
        {
            if (_telemetryLevel == GameTelemetryLevel.Detailed)
            {
                if (status == OfflineDirectChunkAppliedProofStatus.Applied)
                    SaturatingIncrement(ref _timedKnowledgeAdvancements);
                else
                    SaturatingIncrement(ref _timedNonAdvancingApplied);
            }
            InvalidateObsolete(in ticket);
            SaturatingIncrement(ref _appliedProofs);
        }
    }

    public RemoteChunkWorldStateServerSnapshot GetSnapshot()
    {
        EnsureOwner();
        long bundleScratchBytes = _bundleScratch.LongLength;
        long bundleEntryAndRecordBytes = checked(
            (long)_bundleEntries.Length * Unsafe.SizeOf<RemoteDirtyChunkBundleEntry>() +
            (long)_bundleDeltaRecords.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>());
        long bundleTicketAndMetadataBytes = checked(
            (long)_bundleResults.Length * Unsafe.SizeOf<OfflineDirectChunkWireEncodeResult>() +
            (long)_bundleTiers.Length * Unsafe.SizeOf<ChunkInterestTier>() +
            (long)(_bundleSourceSteps.Length + _bundleDueSteps.Length) * sizeof(ulong) +
            (long)(_bundleStandaloneBytes.Length + _bundleWorkPositions.Length +
                _bundleSubscriptionIndices.Length) * sizeof(int));
        long detailedDiagnosticBytes = GetRetainedDetailedDiagnosticBytes();
        return new RemoteChunkWorldStateServerSnapshot(
            _outstandingCount,
            _outstanding.Length,
            _peer.MaximumUnfragmentedMessageBytes,
            UltraSimGameProtocol.GetMaximumChunkDeltaRecordsForPayload(
                _peer.MaximumUnfragmentedMessageBytes),
            _nextDeliveryId,
            _deltasQueued,
            _absencesQueued,
            _appliedProofs,
            _rejectedProofs,
            _malformedProofs,
            _invalidatedDeliveries,
            _oversizeDeltas,
            _snapshotTicket.IsValid,
            _pendingCancel.DeliveryId != 0,
            _snapshotSegmentsQueued,
            _snapshotsQueued,
            _snapshotCancels,
            _snapshotTimeouts,
            checked((long)_outstanding.Length * Unsafe.SizeOf<OutstandingDelivery>() +
                _messageScratch.Length + _bundleScratch.Length + _snapshotPayload.Length +
                (long)_bundleEntries.Length * Unsafe.SizeOf<RemoteDirtyChunkBundleEntry>() +
                (long)_bundleDeltaRecords.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>() +
                (long)_bundleResults.Length * Unsafe.SizeOf<OfflineDirectChunkWireEncodeResult>() +
                (long)_bundleTiers.Length * Unsafe.SizeOf<ChunkInterestTier>() +
                (long)(_bundleSourceSteps.Length + _bundleDueSteps.Length) * sizeof(ulong) +
                (long)(_bundleStandaloneBytes.Length + _bundleWorkPositions.Length +
                    _bundleSubscriptionIndices.Length) * sizeof(int) +
                (long)_timedUniqueDeltaChunks.Length * Unsafe.SizeOf<SpatialChunkKey>() +
                _timedUniqueDeltaChunkOccupied.Length +
                (_timedUniquePublicEntities is null ? 0L :
                    (long)_timedUniquePublicEntities.Length *
                        Unsafe.SizeOf<WorldPublicEntityId>()) +
                (_timedUniquePublicEntityOccupied?.LongLength ?? 0L) +
                (_timedDeltaScratch is null ? 0L :
                    (long)_timedDeltaScratch.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>()) +
                (_timedOutstandingDeltaRecords is null ? 0L :
                    (long)_timedOutstandingDeltaRecords.Length *
                        Unsafe.SizeOf<RemoteChunkDeltaRecord>()) +
                (_timedOutstandingDeltaRecordCounts?.LongLength ?? 0L) * sizeof(ushort) +
                (_bundleOccupancy is null ? 0L :
                    3L * GameValueDistributionWindow.ReservedPayloadBytes) +
                (_deltaPayloadBytes is null ? 0L :
                    GameValueDistributionWindow.ReservedPayloadBytes) +
                (_immediateFreshness is null ? 0L :
                    12L * GameValueDistributionWindow.ReservedPayloadBytes)),
            bundleScratchBytes,
            bundleEntryAndRecordBytes,
            bundleTicketAndMetadataBytes,
            detailedDiagnosticBytes,
            _recipient.GetSnapshot(),
            GetTimingSnapshot());
    }

    private long GetRetainedDetailedDiagnosticBytes() => checked(
        (_timedDeltaScratch is null ? 0L :
            (long)_timedDeltaScratch.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>()) +
        (_timedOutstandingDeltaRecords is null ? 0L :
            (long)_timedOutstandingDeltaRecords.Length *
                Unsafe.SizeOf<RemoteChunkDeltaRecord>()) +
        (_timedOutstandingDeltaRecordCounts?.LongLength ?? 0L) * sizeof(ushort) +
        (_timedUniquePublicEntities is null ? 0L :
            (long)_timedUniquePublicEntities.Length * Unsafe.SizeOf<WorldPublicEntityId>()) +
        (_timedUniquePublicEntityOccupied?.LongLength ?? 0L) +
        (_bundleOccupancy is null ? 0L :
            3L * GameValueDistributionWindow.ReservedPayloadBytes) +
        (_deltaPayloadBytes is null ? 0L :
            GameValueDistributionWindow.ReservedPayloadBytes) +
        (_immediateFreshness is null ? 0L :
            12L * GameValueDistributionWindow.ReservedPayloadBytes));

    private RemoteChunkWorldStateScheduleStatus StartSnapshotTransfer(
        in OfflineDirectChunkWireEncodeResult result,
        ulong serviceStep)
    {
        if (_snapshotTicket.IsValid)
            return RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy;
        for (int index = 0; index < _outstandingCount; index++)
        {
            OfflineDirectChunkDeliveryTicket retained = _outstanding[index].Ticket;
            if (retained.Status != OfflineDirectChunkReadStatus.Snapshot) continue;
            if (retained.Key != result.Ticket.Key)
                return RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy;
            QueueCancel(retained);
            RemoveAt(index);
            RecordInvalidation(OutstandingInvalidationReason.Recovery);
            SaturatingIncrement(ref _invalidatedDeliveries);
            break;
        }
        if (!HasOutstandingCapacity()) return RemoteChunkWorldStateScheduleStatus.CapacityExceeded;
        var reader = new NetworkPacketReader(_messageScratch.AsSpan(0, result.Written));
        if (!RemoteChunkSnapshotHeaderCodec.TryRead(ref reader, out RemoteChunkSnapshotHeader header) ||
            header.DeliveryId != result.Ticket.DeliveryId ||
            header.PublicationEpoch != result.Ticket.SourceEpoch.Value ||
            header.SpatialSpaceId != result.Ticket.Key.Space.Value ||
            header.ChunkX != result.Ticket.Key.Coord.X || header.ChunkZ != result.Ticket.Key.Coord.Z ||
            header.Revision != result.Ticket.Cursor.Revision.Value)
            throw new InvalidOperationException("The canonical snapshot and delivery ticket diverged.");
        int segmentPayload = _peer.MaximumUnfragmentedMessageBytes -
            RemoteChunkSnapshotSegmentHeaderCodec.EncodedSize;
        if (segmentPayload is <= 0 or > ushort.MaxValue)
            throw new InvalidOperationException("The transport cannot admit a bounded snapshot segment.");
        _messageScratch.AsSpan(0, result.Written).CopyTo(_snapshotPayload);
        _snapshotTicket = result.Ticket;
        _snapshotHeader = header;
        _snapshotLength = result.Written;
        _snapshotSegmentPayloadBytes = segmentPayload;
        _snapshotSegmentCount = (result.Written + segmentPayload - 1) / segmentPayload;
        _snapshotNextSegment = 0;
        _snapshotBeginQueued = false;
        _snapshotStartedStep = serviceStep;
        if (_telemetryLevel == GameTelemetryLevel.Detailed)
            SaturatingAdd(ref _timedSnapshotRecords, checked((ulong)result.RecordCount));
        _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;
        _ = TryProgress();
        return RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted;
    }

    private RemoteChunkWorldStateScheduleStatus QueueEncodedOrdinary(
        in OfflineDirectChunkWireEncodeResult result,
        ulong serviceStep)
    {
        if (result.Kind != RemoteChunkPublicationKind.Absence)
            throw new InvalidOperationException("Only an absence can complete forced recovery ordinarily.");
        if (!HasOutstandingCapacity()) return RemoteChunkWorldStateScheduleStatus.CapacityExceeded;
        if (_peer.TryQueue(RemoteChunkAbsenceMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, result.Written)) != NetworkQueueStatus.Accepted)
            return RemoteChunkWorldStateScheduleStatus.TransportNotAccepting;
        _outstanding[_outstandingCount++] = new OutstandingDelivery(
            result.Ticket,
            serviceStep,
            serviceStep,
            serviceStep,
            TierForKey(result.Ticket.Key),
            _telemetryLevel == GameTelemetryLevel.Detailed
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0);
        _timedOutstandingHighWater = Math.Max(_timedOutstandingHighWater, _outstandingCount);
        _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;
        SaturatingIncrement(ref _absencesQueued);
        return RemoteChunkWorldStateScheduleStatus.QueuedAbsence;
    }

    private void RecordScheduleResult(
        int subscriptionIndex,
        RemoteChunkWorldStateScheduleStatus status)
    {
        if (_telemetryLevel == GameTelemetryLevel.Off) return;
        SaturatingIncrement(ref _timedScheduleAttempts);
        switch (status)
        {
            case RemoteChunkWorldStateScheduleStatus.QueuedDelta:
            case RemoteChunkWorldStateScheduleStatus.QueuedAbsence:
            case RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted:
                SaturatingIncrement(ref _timedDeliveriesQueued);
                OfflineDirectChunkSubscriptionView subscriptions = _recipient.Subscriptions;
                if ((uint)subscriptionIndex < (uint)subscriptions.Length)
                {
                    switch (subscriptions[subscriptionIndex].Interest.Tier)
                    {
                        case ChunkInterestTier.Immediate:
                            SaturatingIncrement(ref _timedImmediateQueued);
                            SaturatingIncrement(ref _tierTraffic[0].DuePublications);
                            break;
                        case ChunkInterestTier.Near:
                            SaturatingIncrement(ref _timedNearQueued);
                            SaturatingIncrement(ref _tierTraffic[1].DuePublications);
                            break;
                        case ChunkInterestTier.Far:
                            SaturatingIncrement(ref _timedFarQueued);
                            SaturatingIncrement(ref _tierTraffic[2].DuePublications);
                            break;
                    }
                }
                break;
            case RemoteChunkWorldStateScheduleStatus.NoPayload:
                SaturatingIncrement(ref _timedNoPayload);
                break;
            case RemoteChunkWorldStateScheduleStatus.NotDue:
                SaturatingIncrement(ref _timedCadenceDeferrals);
                break;
            case RemoteChunkWorldStateScheduleStatus.TransportNotAccepting:
            case RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy:
                SaturatingIncrement(ref _timedTransportDeferrals);
                break;
            case RemoteChunkWorldStateScheduleStatus.CapacityExceeded:
                SaturatingIncrement(ref _timedCapacityDeferrals);
                break;
        }
    }

    private bool HasOutstandingCapacity() =>
        _outstandingCount < _outstanding.Length - (_snapshotTicket.IsValid ? 1 : 0);

    private bool HasOutstandingSnapshot()
    {
        if (_snapshotTicket.IsValid) return true;
        for (int index = 0; index < _outstandingCount; index++)
            if (_outstanding[index].Ticket.Status == OfflineDirectChunkReadStatus.Snapshot)
                return true;
        return false;
    }

    private bool HasOutstandingAbsence(SpatialChunkKey key)
    {
        for (int index = 0; index < _outstandingCount; index++)
            if (_outstanding[index].Ticket.Key == key &&
                _outstanding[index].Ticket.Status == OfflineDirectChunkReadStatus.ChunkAbsent)
                return true;
        return false;
    }

    private void ClearActiveSnapshot()
    {
        _snapshotPayload.AsSpan(0, _snapshotLength).Clear();
        _snapshotTicket = default;
        _snapshotHeader = default;
        _snapshotLength = 0;
        _snapshotSegmentPayloadBytes = 0;
        _snapshotSegmentCount = 0;
        _snapshotNextSegment = 0;
        _snapshotBeginQueued = false;
        _snapshotStartedStep = 0;
    }

    private void CancelActiveSnapshot()
    {
        if (!_snapshotTicket.IsValid) return;
        if (_snapshotBeginQueued) QueueCancel(_snapshotTicket);
        ClearActiveSnapshot();
        SaturatingIncrement(ref _invalidatedDeliveries);
    }

    private void QueueCancel(in OfflineDirectChunkDeliveryTicket ticket)
    {
        if (ticket.Status != OfflineDirectChunkReadStatus.Snapshot) return;
        if (_pendingCancel.DeliveryId != 0 && _pendingCancel.DeliveryId != ticket.DeliveryId)
            throw new InvalidOperationException("Only one snapshot cancellation may be retained.");
        _pendingCancel = new RemoteChunkSnapshotCancelMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            PublicationEpoch = ticket.SourceEpoch.Value,
            DeliveryId = ticket.DeliveryId,
        };
    }

    private void InvalidateAll(OutstandingInvalidationReason reason)
    {
        bool activeSnapshot = _snapshotTicket.IsValid;
        CancelActiveSnapshot();
        if (activeSnapshot) RecordInvalidation(reason);
        for (int index = 0; index < _outstandingCount; index++)
            QueueCancel(_outstanding[index].Ticket);
        for (int index = 0; index < _outstandingCount; index++) RecordInvalidation(reason);
        SaturatingAdd(ref _invalidatedDeliveries, checked((ulong)_outstandingCount));
        _outstanding.AsSpan(0, _outstandingCount).Clear();
        if (_timedOutstandingDeltaRecordCounts is not null)
            _timedOutstandingDeltaRecordCounts.AsSpan(0, _outstandingCount).Clear();
        if (_timedOutstandingDeltaRecords is not null)
            _timedOutstandingDeltaRecords.AsSpan(
                0, checked(_outstandingCount * _recipient.Limits.MaximumDeltaRecords)).Clear();
        _outstandingCount = 0;
    }

    private void Invalidate(SpatialChunkKey key, OutstandingInvalidationReason reason)
    {
        if (_snapshotTicket.IsValid && _snapshotTicket.Key == key)
        {
            CancelActiveSnapshot();
            RecordInvalidation(reason);
        }
        for (int index = _outstandingCount - 1; index >= 0; index--)
        {
            if (_outstanding[index].Ticket.Key != key) continue;
            QueueCancel(_outstanding[index].Ticket);
            RemoveAt(index);
            RecordInvalidation(reason);
            SaturatingIncrement(ref _invalidatedDeliveries);
        }
    }

    private void InvalidateObsolete(in OfflineDirectChunkDeliveryTicket appliedTicket)
    {
        SpatialChunkKey key = appliedTicket.Key;
        if (_snapshotTicket.IsValid && _snapshotTicket.Key == key &&
            _snapshotTicket.DeliveryId < appliedTicket.DeliveryId)
        {
            CancelActiveSnapshot();
            if (_telemetryLevel == GameTelemetryLevel.Detailed)
                SaturatingIncrement(ref _timedSupersededDebtRetired);
        }
        int subscription = FindSubscription(key);
        if (subscription < 0)
        {
            Invalidate(key, OutstandingInvalidationReason.AoiLeave);
            return;
        }
        OfflineDirectChunkSubscription applied = _recipient.Subscriptions[subscription];
        for (int index = _outstandingCount - 1; index >= 0; index--)
        {
            OfflineDirectChunkDeliveryTicket candidate = _outstanding[index].Ticket;
            if (candidate.Key != key) continue;
            bool obsolete = !applied.Cursor.IsValid ||
                candidate.Cursor.IsValid &&
                candidate.Cursor.Revision.Value <= applied.Cursor.Revision.Value ||
                candidate.DeliveryId < appliedTicket.DeliveryId;
            if (!obsolete) continue;
            QueueCancel(candidate);
            RemoveAt(index);
            if (_telemetryLevel == GameTelemetryLevel.Detailed)
                SaturatingIncrement(ref _timedSupersededDebtRetired);
            SaturatingIncrement(ref _invalidatedDeliveries);
        }
    }

    private int FindSubscription(SpatialChunkKey key)
    {
        OfflineDirectChunkSubscriptionView subscriptions = _recipient.Subscriptions;
        int low = 0;
        int high = subscriptions.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = subscriptions[middle].Interest.Key.CompareTo(key);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private int FindDelivery(ulong deliveryId)
    {
        for (int index = 0; index < _outstandingCount; index++)
            if (_outstanding[index].Ticket.DeliveryId == deliveryId) return index;
        return -1;
    }

    private void RemoveAt(int index)
    {
        _outstandingCount--;
        _outstanding[index] = _outstanding[_outstandingCount];
        _outstanding[_outstandingCount] = default;
        if (_timedOutstandingDeltaRecordCounts is null ||
            _timedOutstandingDeltaRecords is null)
            return;
        int recordsPerDelivery = _recipient.Limits.MaximumDeltaRecords;
        if (index != _outstandingCount)
        {
            _timedOutstandingDeltaRecords.AsSpan(
                    _outstandingCount * recordsPerDelivery, recordsPerDelivery)
                .CopyTo(_timedOutstandingDeltaRecords.AsSpan(
                    index * recordsPerDelivery, recordsPerDelivery));
            _timedOutstandingDeltaRecordCounts[index] =
                _timedOutstandingDeltaRecordCounts[_outstandingCount];
        }
        _timedOutstandingDeltaRecords.AsSpan(
            _outstandingCount * recordsPerDelivery, recordsPerDelivery).Clear();
        _timedOutstandingDeltaRecordCounts[_outstandingCount] = 0;
    }

    private bool Matches(
        in OfflineDirectChunkDeliveryTicket ticket,
        in RemoteChunkAppliedMessage proof) =>
        ticket.IsValid &&
        proof.PublicationEpoch == ticket.SourceEpoch.Value &&
        proof.ApplicationSessionEpoch == _applicationSessionEpoch &&
        proof.DeliveryId == ticket.DeliveryId &&
        proof.SpatialSpaceId == ticket.Key.Space.Value &&
        proof.ChunkX == ticket.Key.Coord.X && proof.ChunkZ == ticket.Key.Coord.Z &&
        proof.Kind == Kind(ticket.Status) &&
        proof.Revision == (ticket.Cursor.IsValid ? ticket.Cursor.Revision.Value : 0);

    private void RecordQueuedDelta(
        in OfflineDirectChunkWireEncodeResult result,
        int subscriptionIndex,
        ulong cadenceStep,
        int retainedIndex)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed ||
            _timedDeltaScratch is null || _timedOutstandingDeltaRecords is null ||
            _timedOutstandingDeltaRecordCounts is null)
            return;
        if (!UltraSimGameProtocol.TryReadChunkDelta(
                _messageScratch.AsSpan(0, result.Written), out RemoteChunkDeltaHeader header,
                _timedDeltaScratch, out int recordCount) ||
            recordCount != result.RecordCount || header.DeliveryId != result.Ticket.DeliveryId ||
            header.BaseRevision != result.BaseRevision ||
            header.CurrentRevision != result.CurrentRevision)
            throw new InvalidOperationException("A queued canonical delta diverged from its census.");

        RecordQueuedDelta(in result, subscriptionIndex, cadenceStep, retainedIndex,
            _timedDeltaScratch.AsSpan(0, recordCount));
        _timedDeltaScratch.AsSpan(0, recordCount).Clear();
    }

    private void RecordQueuedDelta(
        in OfflineDirectChunkWireEncodeResult result,
        int subscriptionIndex,
        ulong cadenceStep,
        int retainedIndex,
        ReadOnlySpan<RemoteChunkDeltaRecord> currentRecords)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed ||
            _timedOutstandingDeltaRecords is null ||
            _timedOutstandingDeltaRecordCounts is null)
            return;
        int recordCount = currentRecords.Length;

        int sameChunk = 0;
        int sameStep = 0;
        int latestIndex = -1;
        ulong latestRevision = 0;
        for (int index = 0; index < _outstandingCount; index++)
        {
            ref readonly OutstandingDelivery outstanding = ref _outstanding[index];
            if (outstanding.Ticket.Key != result.Ticket.Key) continue;
            sameChunk++;
            if (outstanding.StartedStep == cadenceStep) sameStep++;
            ulong revision = outstanding.Ticket.Cursor.IsValid
                ? outstanding.Ticket.Cursor.Revision.Value
                : 0;
            if (latestIndex < 0 || revision > latestRevision ||
                revision == latestRevision &&
                outstanding.Ticket.DeliveryId > _outstanding[latestIndex].Ticket.DeliveryId)
            {
                latestIndex = index;
                latestRevision = revision;
            }
        }

        int exactRepeated = 0;
        int repeatedIdentities = 0;
        int repeatedFieldBits = 0;
        if (latestIndex >= 0)
        {
            int maximumRecords = _recipient.Limits.MaximumDeltaRecords;
            ReadOnlySpan<RemoteChunkDeltaRecord> previous =
                _timedOutstandingDeltaRecords.AsSpan(
                    latestIndex * maximumRecords,
                    _timedOutstandingDeltaRecordCounts[latestIndex]);
            for (int currentIndex = 0; currentIndex < recordCount; currentIndex++)
            {
                ref readonly RemoteChunkDeltaRecord current =
                    ref currentRecords[currentIndex];
                for (int priorIndex = 0; priorIndex < previous.Length; priorIndex++)
                {
                    ref readonly RemoteChunkDeltaRecord prior = ref previous[priorIndex];
                    if (prior.EntityIndex != current.EntityIndex ||
                        prior.EntityGeneration != current.EntityGeneration)
                        continue;
                    repeatedIdentities++;
                    repeatedFieldBits += System.Numerics.BitOperations.PopCount(
                        (uint)(prior.Fields & current.Fields));
                    if (prior == current) exactRepeated++;
                    break;
                }
            }
        }

        SaturatingIncrement(ref _timedDeltaMessages);
        SaturatingAdd(ref _timedDeltaBytes, checked((ulong)result.Written));
        SaturatingAdd(ref _timedDeltaRecordCount, checked((ulong)recordCount));
        _deltaPayloadBytes!.Record(checked((ulong)result.Written));
        int payloadBucket = result.Written switch
        {
            <= 128 => 0,
            <= 256 => 1,
            <= 512 => 2,
            <= 1024 => 3,
            _ => 4,
        };
        SaturatingIncrement(ref _deltaPayloadBuckets[payloadBucket]);
        _deltaRecords.Record(checked((ulong)recordCount));
        ulong revisionSpan = result.CurrentRevision >= result.BaseRevision
            ? result.CurrentRevision - result.BaseRevision
            : 0;
        _deltaRevisionSpan.Record(revisionSpan);
        _sameChunkOutstanding.Record(checked((ulong)sameChunk));
        if (sameChunk != 0)
        {
            SaturatingIncrement(ref _timedOverlappingDeltaMessages);
            // This is an explicit upper bound on recovery-overlap cost. The fixed record codec
            // cannot attribute shared header/value bytes to individual historical operations.
            SaturatingAdd(ref _timedOverlappingDeltaBytes, checked((ulong)result.Written));
        }
        if (sameStep != 0) SaturatingIncrement(ref _timedSameStepDeltaMessages);
        SaturatingAdd(ref _timedExactRepeatedRecords, checked((ulong)exactRepeated));
        SaturatingAdd(ref _timedExactRepeatedRecordBytes,
            checked((ulong)exactRepeated * RemoteChunkDeltaRecord.EncodedSize));
        SaturatingAdd(ref _timedRepeatedEntityIdentities, checked((ulong)repeatedIdentities));
        SaturatingAdd(ref _timedRepeatedFieldBits, checked((ulong)repeatedFieldBits));
        if (latestRevision > result.BaseRevision)
            SaturatingAdd(ref _timedOverlappedRevisionSpan,
                Math.Min(latestRevision, result.CurrentRevision) - result.BaseRevision);

        OfflineDirectChunkSubscriptionView subscriptions = _recipient.Subscriptions;
        if ((uint)subscriptionIndex < (uint)subscriptions.Length)
        {
            ChunkInterestTier tier = subscriptions[subscriptionIndex].Interest.Tier;
            ref TierTrafficWindow traffic = ref TierTraffic(tier);
            SaturatingAdd(ref traffic.DeltaRecords, checked((ulong)recordCount));
            SaturatingAdd(ref traffic.StandaloneCandidateApplicationBytes,
                checked((ulong)result.Written));
            switch (tier)
            {
                case ChunkInterestTier.Immediate:
                    SaturatingIncrement(ref _timedDeltaImmediate);
                    break;
                case ChunkInterestTier.Near:
                    SaturatingIncrement(ref _timedDeltaNear);
                    break;
                case ChunkInterestTier.Far:
                    SaturatingIncrement(ref _timedDeltaFar);
                    break;
            }
        }
        for (int index = 0; index < currentRecords.Length; index++)
        {
            ref readonly RemoteChunkDeltaRecord record = ref currentRecords[index];
            var identity = new WorldPublicEntityId(record.EntityIndex, record.EntityGeneration);
            AddUniquePublicEntity(identity);
            if (_controlledPublicEntity.IsValid && identity == _controlledPublicEntity)
            {
                SaturatingIncrement(ref _timedSelfPublicRecords);
                if ((uint)subscriptionIndex < (uint)subscriptions.Length)
                    SaturatingIncrement(ref TierTraffic(
                        subscriptions[subscriptionIndex].Interest.Tier).SelfPublicRecords);
            }
            else
            {
                SaturatingIncrement(ref _timedOtherPublicRecords);
            }
        }
        AddUniqueDeltaChunk(result.Ticket.Key);

        int offset = checked(retainedIndex * _recipient.Limits.MaximumDeltaRecords);
        currentRecords.CopyTo(_timedOutstandingDeltaRecords.AsSpan(offset, recordCount));
        _timedOutstandingDeltaRecordCounts[retainedIndex] = checked((ushort)recordCount);
    }

    private void AddUniqueDeltaChunk(SpatialChunkKey key)
    {
        int mask = _timedUniqueDeltaChunks.Length - 1;
        ulong hash = key.Space.Value;
        unchecked
        {
            hash ^= (uint)key.Coord.X * 0x9E37_79B1U;
            hash ^= (uint)key.Coord.Z * 0x85EB_CA77U;
            hash ^= hash >> 33;
            hash *= 0xFF51_AFD7_ED55_8CCDUL;
            hash ^= hash >> 33;
        }
        int slot = checked((int)(hash & (uint)mask));
        for (int probe = 0; probe < _timedUniqueDeltaChunks.Length; probe++)
        {
            if (!_timedUniqueDeltaChunkOccupied[slot])
            {
                _timedUniqueDeltaChunkOccupied[slot] = true;
                _timedUniqueDeltaChunks[slot] = key;
                _timedUniqueDeltaChunkCount++;
                return;
            }
            if (_timedUniqueDeltaChunks[slot] == key) return;
            slot = (slot + 1) & mask;
        }
        _timedUniqueDeltaChunkOverflowed = true;
    }

    private void AddUniquePublicEntity(WorldPublicEntityId identity)
    {
        WorldPublicEntityId[] entities = _timedUniquePublicEntities!;
        bool[] occupied = _timedUniquePublicEntityOccupied!;
        int mask = entities.Length - 1;
        int slot = identity.GetHashCode() & mask;
        for (int probe = 0; probe < entities.Length; probe++)
        {
            if (!occupied[slot])
            {
                occupied[slot] = true;
                entities[slot] = identity;
                _timedUniquePublicEntityCount++;
                return;
            }
            if (entities[slot] == identity) return;
            slot = (slot + 1) & mask;
        }
        _timedUniquePublicEntityOverflowed = true;
    }

    private void RecordRepresentation(ChunkInterestTier tier, bool bundled)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        ref TierTrafficWindow value = ref TierTraffic(tier);
        if (bundled) SaturatingIncrement(ref value.BundledEntries);
        else SaturatingIncrement(ref value.StandaloneEntries);
    }

    private void RecordAbsence(ChunkInterestTier tier, bool bundled)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        SaturatingIncrement(ref _timedAbsencePublications);
        RecordRepresentation(tier, bundled);
    }

    private ref TierTrafficWindow TierTraffic(ChunkInterestTier tier) => ref _tierTraffic[
        tier switch
        {
            ChunkInterestTier.Immediate => 0,
            ChunkInterestTier.Near => 1,
            ChunkInterestTier.Far => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(tier)),
        }];

    private RemoteChunkTierTrafficSnapshot CaptureTierTraffic(int index)
    {
        ref readonly TierTrafficWindow value = ref _tierTraffic[index];
        return new RemoteChunkTierTrafficSnapshot(
            value.DuePublications,
            value.DeltaRecords,
            value.StandaloneCandidateApplicationBytes,
            value.BundledEntries,
            value.StandaloneEntries,
            value.ProofEntries,
            value.SelfPublicRecords);
    }

    private void RecordProofEntry(in RemoteChunkAppliedMessage proof)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        switch (proof.Status)
        {
            case RemoteChunkPublicationApplyStatus.Applied:
                SaturatingIncrement(ref _timedProofApplied);
                break;
            case RemoteChunkPublicationApplyStatus.Rejected:
                SaturatingIncrement(ref _timedProofRejected);
                break;
            case RemoteChunkPublicationApplyStatus.CapacityExceeded:
                SaturatingIncrement(ref _timedProofCapacity);
                break;
            case RemoteChunkPublicationApplyStatus.Contradictory:
                SaturatingIncrement(ref _timedProofContradictory);
                break;
        }
        switch (proof.Kind)
        {
            case RemoteChunkPublicationKind.Delta:
                SaturatingIncrement(ref _timedProofDelta);
                break;
            case RemoteChunkPublicationKind.Snapshot:
                SaturatingIncrement(ref _timedProofSnapshot);
                break;
            case RemoteChunkPublicationKind.Absence:
                SaturatingIncrement(ref _timedProofAbsence);
                break;
        }
    }

    private void RecordDeliveryLatency(in OutstandingDelivery delivery)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        ulong age = _lastServiceStep >= delivery.StartedStep
            ? _lastServiceStep - delivery.StartedStep
            : 0;
        _deliveryAgeSteps.Record(age);
        if (delivery.QueuedTimestamp != 0)
            _deliveryProofLatency.RecordDetailed(delivery.QueuedTimestamp);
        Freshness(delivery.Tier)?.RecordProof(
            Math.Min(delivery.SourceStep, _lastServiceStep), _lastServiceStep);
    }

    private void RecordMalformedProof()
    {
        if (_telemetryLevel == GameTelemetryLevel.Detailed)
            SaturatingIncrement(ref _timedMalformedProofEntries);
    }

    private void RecordInvalidation(OutstandingInvalidationReason reason)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        switch (reason)
        {
            case OutstandingInvalidationReason.AoiLeave:
                SaturatingIncrement(ref _timedInvalidatedAoi);
                break;
            case OutstandingInvalidationReason.SourceReplacement:
                SaturatingIncrement(ref _timedInvalidatedSource);
                break;
            case OutstandingInvalidationReason.Recovery:
                SaturatingIncrement(ref _timedInvalidatedRecovery);
                break;
        }
    }

    private RemoteChunkKnowledgeCensusSnapshot CaptureKnowledgeCensus()
    {
        int multipleChunks = 0;
        int maximumPerChunk = 0;
        ulong oldestAge = 0;
        for (int index = 0; index < _outstandingCount; index++)
        {
            ref readonly OutstandingDelivery delivery = ref _outstanding[index];
            if (_lastServiceStep >= delivery.StartedStep)
                oldestAge = Math.Max(oldestAge, _lastServiceStep - delivery.StartedStep);
            bool seen = false;
            for (int prior = 0; prior < index; prior++)
                if (_outstanding[prior].Ticket.Key == delivery.Ticket.Key)
                {
                    seen = true;
                    break;
                }
            if (seen) continue;
            int depth = 1;
            for (int later = index + 1; later < _outstandingCount; later++)
                if (_outstanding[later].Ticket.Key == delivery.Ticket.Key) depth++;
            maximumPerChunk = Math.Max(maximumPerChunk, depth);
            if (depth > 1) multipleChunks++;
        }
        return new RemoteChunkKnowledgeCensusSnapshot(
            new RemoteChunkDeltaCensusSnapshot(
                _timedDeltaMessages,
                _timedDeltaBytes,
                _timedDeltaRecordCount,
                _timedDeltaImmediate,
                _timedDeltaNear,
                _timedDeltaFar,
                _timedUniqueDeltaChunkCount,
                _timedUniqueDeltaChunkOverflowed,
                _timedOverlappingDeltaMessages,
                _timedOverlappingDeltaBytes,
                _timedSameStepDeltaMessages,
                _timedExactRepeatedRecords,
                _timedExactRepeatedRecordBytes,
                _timedRepeatedEntityIdentities,
                checked(_timedRepeatedEntityIdentities *
                    (ulong)RemoteChunkDeltaRecord.EncodedSize),
                _timedRepeatedFieldBits,
                _timedOverlappedRevisionSpan,
                _deltaRecords.Capture(),
                _deltaRevisionSpan.Capture(),
                _sameChunkOutstanding.Capture()),
            new RemoteChunkProofCensusSnapshot(
                _timedProofEnvelopes,
                _timedLegacyProofMessages,
                _timedProofEnvelopeBytes,
                _timedProofEntries,
                _timedProofApplied,
                _timedProofRejected,
                _timedProofCapacity,
                _timedProofContradictory,
                _timedProofDelta,
                _timedProofSnapshot,
                _timedProofAbsence,
                _timedKnowledgeAdvancements,
                _timedNonAdvancingApplied,
                _timedExactDebtRetired,
                _timedSupersededDebtRetired,
                _timedMalformedProofEntries,
                _proofEnvelopeOccupancy.Capture(),
                _deliveryAgeSteps.Capture(),
                _deliveryProofLatency.Capture()),
            new RemoteChunkOutstandingCensusSnapshot(
                _outstandingCount,
                _timedOutstandingHighWater,
                multipleChunks,
                maximumPerChunk,
                oldestAge,
                _timedInvalidatedAoi,
                _timedInvalidatedSource,
                _timedInvalidatedRecovery,
                _timedSupersededDebtRetired),
            new RemoteChunkBundleCensusSnapshot(
                _timedBundles,
                _timedBundleEntries,
                _timedBundleBytes,
                _timedBundleStandaloneBytes,
                _timedBundleMessagesReplaced,
                _timedBundleMessagesSaved,
                _timedBundleHeaderBytesSaved,
                _timedSingleCandidates,
                _timedBundleFullByEntries,
                _timedBundleFullByBytes,
                _bundleOccupancy?.Capture() ?? default,
                _bundleEncodedBytes?.Capture() ?? default,
                _bundleMtuUtilization?.Capture() ?? default),
            new RemoteChunkFreshnessCensusSnapshot(
                _immediateFreshness?.Capture() ?? default,
                _nearFreshness?.Capture() ?? default,
                _farFreshness?.Capture() ?? default),
            new RemoteChunkPublicationCensusSnapshot(
                _timedDeltaRecordCount,
                _timedSnapshotRecords,
                _timedAbsencePublications,
                _timedUniquePublicEntityCount,
                _timedUniquePublicEntityOverflowed,
                _timedSelfPublicRecords,
                _timedOtherPublicRecords,
                checked(_timedSelfPublicRecords *
                    (ulong)RemoteChunkDeltaRecord.EncodedSize),
                _deltaPayloadBytes?.Capture() ?? default,
                _deltaPayloadBuckets[0], _deltaPayloadBuckets[1],
                _deltaPayloadBuckets[2], _deltaPayloadBuckets[3],
                _deltaPayloadBuckets[4],
                CaptureTierTraffic(0), CaptureTierTraffic(1), CaptureTierTraffic(2)));
    }

    private static RemoteChunkWorldStateScheduleStatus Map(
        OfflineDirectChunkWireEncodeStatus encoded) => encoded switch
    {
        OfflineDirectChunkWireEncodeStatus.NoPayload => RemoteChunkWorldStateScheduleStatus.NoPayload,
        OfflineDirectChunkWireEncodeStatus.NotDue => RemoteChunkWorldStateScheduleStatus.NotDue,
        OfflineDirectChunkWireEncodeStatus.InvalidSubscription =>
            RemoteChunkWorldStateScheduleStatus.InvalidSubscription,
        OfflineDirectChunkWireEncodeStatus.SourceRequiresReconcile =>
            RemoteChunkWorldStateScheduleStatus.SourceRequiresReconcile,
        OfflineDirectChunkWireEncodeStatus.ReadPending =>
            RemoteChunkWorldStateScheduleStatus.ReadPending,
        OfflineDirectChunkWireEncodeStatus.CapacityExceeded =>
            RemoteChunkWorldStateScheduleStatus.CapacityExceeded,
        OfflineDirectChunkWireEncodeStatus.FutureCursor =>
            RemoteChunkWorldStateScheduleStatus.FutureCursor,
        OfflineDirectChunkWireEncodeStatus.EncodingFailed =>
            RemoteChunkWorldStateScheduleStatus.TransportNotAccepting,
        _ => default,
    };

    private static bool IsTransient(RemoteChunkWorldStateScheduleStatus status) => status is
        RemoteChunkWorldStateScheduleStatus.TransportNotAccepting or
        RemoteChunkWorldStateScheduleStatus.CapacityExceeded or
        RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy or
        RemoteChunkWorldStateScheduleStatus.ReadPending or
        RemoteChunkWorldStateScheduleStatus.SourceRequiresReconcile;

    private void AdvanceDeliveryIds(int count)
    {
        if (count <= 0) return;
        ulong last = _nextDeliveryId + checked((ulong)count - 1);
        _nextDeliveryId = last == ulong.MaxValue ? 0 : last + 1;
    }

    private void ClearBundleStaging(int entries, int deltaRecords)
    {
        _bundleEntries.AsSpan(0, entries).Clear();
        _bundleResults.AsSpan(0, entries).Clear();
        _bundleTiers.AsSpan(0, entries).Clear();
        _bundleSourceSteps.AsSpan(0, entries).Clear();
        _bundleDueSteps.AsSpan(0, entries).Clear();
        _bundleStandaloneBytes.AsSpan(0, entries).Clear();
        _bundleWorkPositions.AsSpan(0, entries).Clear();
        _bundleSubscriptionIndices.AsSpan(0, entries).Clear();
        _bundleDeltaRecords.AsSpan(0, deltaRecords).Clear();
    }

    private void RecordBundleQueued(
        int entries,
        int bytes,
        int standaloneBytes,
        bool fullByByteLimit)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        SaturatingIncrement(ref _timedBundles);
        SaturatingAdd(ref _timedBundleEntries, checked((ulong)entries));
        SaturatingAdd(ref _timedBundleBytes, checked((ulong)bytes));
        SaturatingAdd(ref _timedBundleStandaloneBytes, checked((ulong)standaloneBytes));
        SaturatingAdd(ref _timedBundleMessagesReplaced, checked((ulong)entries));
        SaturatingAdd(ref _timedBundleMessagesSaved, checked((ulong)(entries - 1)));
        SaturatingAdd(ref _timedBundleHeaderBytesSaved,
            checked((ulong)(standaloneBytes - bytes)));
        _bundleOccupancy!.Record(checked((ulong)entries));
        _bundleEncodedBytes!.Record(checked((ulong)bytes));
        _bundleMtuUtilization!.Record(checked((ulong)bytes * 1_000UL /
            (ulong)_peer.MaximumUnfragmentedMessageBytes));
        if (entries == _bundleEntries.Length)
            SaturatingIncrement(ref _timedBundleFullByEntries);
        if (fullByByteLimit)
            SaturatingIncrement(ref _timedBundleFullByBytes);
    }

    private void RecordFreshnessQueued(
        ChunkInterestTier tier,
        ulong sourceStep,
        ulong dueStep,
        ulong queuedStep)
    {
        if (_telemetryLevel != GameTelemetryLevel.Detailed) return;
        TierFreshnessWindow? window = Freshness(tier);
        if (window is null) return;
        ulong admittedSource = Math.Min(sourceStep, queuedStep);
        ulong admittedDue = Math.Clamp(dueStep, admittedSource, queuedStep);
        window.RecordQueued(admittedSource, admittedDue, queuedStep);
    }

    private TierFreshnessWindow? Freshness(ChunkInterestTier tier) => tier switch
    {
        ChunkInterestTier.Immediate => _immediateFreshness,
        ChunkInterestTier.Near => _nearFreshness,
        ChunkInterestTier.Far => _farFreshness,
        _ => null,
    };

    private static RemoteChunkPublicationKind Kind(OfflineDirectChunkReadStatus status) => status switch
    {
        OfflineDirectChunkReadStatus.Snapshot => RemoteChunkPublicationKind.Snapshot,
        OfflineDirectChunkReadStatus.Delta => RemoteChunkPublicationKind.Delta,
        OfflineDirectChunkReadStatus.ChunkAbsent => RemoteChunkPublicationKind.Absence,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private ChunkInterestTier TierForKey(SpatialChunkKey key)
    {
        int index = FindSubscription(key);
        return index >= 0 ? _recipient.Subscriptions[index].Interest.Tier : default;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, ulong amount) =>
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote chunk WorldState server is owner-thread bound.");
    }

    private readonly struct OutstandingDelivery
    {
        internal OutstandingDelivery(
            OfflineDirectChunkDeliveryTicket ticket,
            ulong startedStep,
            ulong sourceStep,
            ulong dueStep,
            ChunkInterestTier tier,
            long queuedTimestamp)
        {
            Ticket = ticket;
            StartedStep = startedStep;
            SourceStep = sourceStep;
            DueStep = dueStep;
            Tier = tier;
            QueuedTimestamp = queuedTimestamp;
        }
        internal OfflineDirectChunkDeliveryTicket Ticket { get; }
        internal ulong StartedStep { get; }
        internal ulong SourceStep { get; }
        internal ulong DueStep { get; }
        internal ChunkInterestTier Tier { get; }
        internal long QueuedTimestamp { get; }
    }

    private sealed class TierFreshnessWindow
    {
        private readonly GameValueDistributionWindow _sourceToDue = new();
        private readonly GameValueDistributionWindow _dueToQueue = new();
        private readonly GameValueDistributionWindow _sourceToQueue = new();
        private readonly GameValueDistributionWindow _sourceToProof = new();
        private ulong _publications;

        internal void RecordQueued(ulong source, ulong due, ulong queued)
        {
            SaturatingIncrement(ref _publications);
            _sourceToDue.Record(due - source);
            _dueToQueue.Record(queued - due);
            _sourceToQueue.Record(queued - source);
        }

        internal void RecordProof(ulong source, ulong proof) =>
            _sourceToProof.Record(proof >= source ? proof - source : 0);

        internal RemoteChunkFreshnessTierSnapshot Capture() => new(
            _publications,
            _sourceToDue.Capture(),
            _dueToQueue.Capture(),
            _sourceToQueue.Capture(),
            _sourceToProof.Capture());

        internal void Clear()
        {
            _publications = 0;
            _sourceToDue.Clear();
            _dueToQueue.Clear();
            _sourceToQueue.Clear();
            _sourceToProof.Clear();
        }
    }

    private enum OutstandingInvalidationReason : byte
    {
        AoiLeave = 1,
        SourceReplacement = 2,
        Recovery = 3,
    }

    private struct TierTrafficWindow
    {
        internal ulong DuePublications;
        internal ulong DeltaRecords;
        internal ulong StandaloneCandidateApplicationBytes;
        internal ulong BundledEntries;
        internal ulong StandaloneEntries;
        internal ulong ProofEntries;
        internal ulong SelfPublicRecords;

        internal void Clear() => this = default;
    }
}
