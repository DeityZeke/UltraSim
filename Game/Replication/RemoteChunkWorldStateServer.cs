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

/// <summary>
/// Read-only knowledge census. Physical UDP/frame accounting remains owned by Network; these are
/// Game-level messages, records, exact delivery debt, and application-proof effects.
/// </summary>
public readonly record struct RemoteChunkKnowledgeCensusSnapshot(
    RemoteChunkDeltaCensusSnapshot Delta,
    RemoteChunkProofCensusSnapshot Proof,
    RemoteChunkOutstandingCensusSnapshot Outstanding);

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
    private ulong _lastServiceStep;
    private ulong _timedDeltaMessages;
    private ulong _timedDeltaBytes;
    private ulong _timedDeltaRecordCount;
    private ulong _timedDeltaImmediate;
    private ulong _timedDeltaNear;
    private ulong _timedDeltaFar;
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
        _recipient.ResetReconcileAttribution(level == GameTelemetryLevel.Detailed);
        _timedScheduleAttempts = _timedDeliveriesQueued = 0;
        _timedImmediateQueued = _timedNearQueued = _timedFarQueued = 0;
        _timedNoPayload = _timedCadenceDeferrals = 0;
        _timedTransportDeferrals = _timedCapacityDeferrals = 0;
        _timedOutstandingHighWater = _outstandingCount;
        _timedUniqueDeltaChunkCount = 0;
        _timedUniqueDeltaChunkOverflowed = false;
        Array.Clear(_timedUniqueDeltaChunks);
        Array.Clear(_timedUniqueDeltaChunkOccupied);
        _timedDeltaMessages = _timedDeltaBytes = _timedDeltaRecordCount = 0;
        _timedDeltaImmediate = _timedDeltaNear = _timedDeltaFar = 0;
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
        if (level == GameTelemetryLevel.Detailed)
        {
            _timedDeltaScratch ??= new RemoteChunkDeltaRecord[
                _recipient.Limits.MaximumDeltaRecords];
            _timedOutstandingDeltaRecords ??= new RemoteChunkDeltaRecord[checked(
                _outstanding.Length * _recipient.Limits.MaximumDeltaRecords)];
            _timedOutstandingDeltaRecordCounts ??= new ushort[_outstanding.Length];
        }
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
        EnsureOwner();
        _lastServiceStep = Math.Max(_lastServiceStep, cadenceStep);
        long timing = _scheduleTiming.StartDetailed();
        RemoteChunkWorldStateScheduleStatus status;
        try
        {
            status = TryScheduleCore(subscriptionIndex, cadenceStep);
        }
        finally { _scheduleTiming.RecordDetailed(timing); }
        RecordScheduleResult(subscriptionIndex, status);
        return status;
    }

    private RemoteChunkWorldStateScheduleStatus TryScheduleCore(
        int subscriptionIndex,
        ulong cadenceStep)
    {
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

        int retainedIndex = _outstandingCount;
        _outstanding[retainedIndex] = new OutstandingDelivery(
            result.Ticket,
            cadenceStep,
            _telemetryLevel == GameTelemetryLevel.Detailed
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0);
        if (result.Kind == RemoteChunkPublicationKind.Delta)
            RecordQueuedDelta(in result, subscriptionIndex, cadenceStep, retainedIndex);
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
                _messageScratch.Length + _snapshotPayload.Length +
                (long)_timedUniqueDeltaChunks.Length * Unsafe.SizeOf<SpatialChunkKey>() +
                _timedUniqueDeltaChunkOccupied.Length +
                (_timedDeltaScratch is null ? 0L :
                    (long)_timedDeltaScratch.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>()) +
                (_timedOutstandingDeltaRecords is null ? 0L :
                    (long)_timedOutstandingDeltaRecords.Length *
                        Unsafe.SizeOf<RemoteChunkDeltaRecord>()) +
                (_timedOutstandingDeltaRecordCounts?.LongLength ?? 0L) * sizeof(ushort)),
            _recipient.GetSnapshot(),
            GetTimingSnapshot());
    }

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
                            break;
                        case ChunkInterestTier.Near:
                            SaturatingIncrement(ref _timedNearQueued);
                            break;
                        case ChunkInterestTier.Far:
                            SaturatingIncrement(ref _timedFarQueued);
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
                    ref _timedDeltaScratch[currentIndex];
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
            switch (subscriptions[subscriptionIndex].Interest.Tier)
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
        AddUniqueDeltaChunk(result.Ticket.Key);

        int offset = checked(retainedIndex * _recipient.Limits.MaximumDeltaRecords);
        _timedDeltaScratch.AsSpan(0, recordCount)
            .CopyTo(_timedOutstandingDeltaRecords.AsSpan(offset, recordCount));
        _timedOutstandingDeltaRecordCounts[retainedIndex] = checked((ushort)recordCount);
        _timedDeltaScratch.AsSpan(0, recordCount).Clear();
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
                _timedSupersededDebtRetired));
    }

    private static RemoteChunkPublicationKind Kind(OfflineDirectChunkReadStatus status) => status switch
    {
        OfflineDirectChunkReadStatus.Snapshot => RemoteChunkPublicationKind.Snapshot,
        OfflineDirectChunkReadStatus.Delta => RemoteChunkPublicationKind.Delta,
        OfflineDirectChunkReadStatus.ChunkAbsent => RemoteChunkPublicationKind.Absence,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

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
            long queuedTimestamp)
        {
            Ticket = ticket;
            StartedStep = startedStep;
            QueuedTimestamp = queuedTimestamp;
        }
        internal OfflineDirectChunkDeliveryTicket Ticket { get; }
        internal ulong StartedStep { get; }
        internal long QueuedTimestamp { get; }
    }

    private enum OutstandingInvalidationReason : byte
    {
        AoiLeave = 1,
        SourceReplacement = 2,
        Recovery = 3,
    }
}
