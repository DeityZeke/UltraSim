using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

/// <summary>Fixed storage bounds for the concrete dirty-chunk spatial-discovery profile.</summary>
public readonly record struct DirtyChunkSpatialFanoutLimits(
    int MaximumRecipients,
    int MaximumRecipientChunkRows,
    int MaximumSpatialRegions,
    int MaximumDirtyChunks,
    int MaximumOutstandingDeliveries,
    int MaximumSnapshotRecords,
    int MaximumDeltaRecords,
    int SpatialRegionEdgeMillimeters,
    ulong MaximumDeliveryAgeSteps = 1_024)
{
    public static DirtyChunkSpatialFanoutLimits Default => new(
        MaximumRecipients: 256,
        MaximumRecipientChunkRows: 262_144,
        MaximumSpatialRegions: 16_384,
        MaximumDirtyChunks: 8_192,
        MaximumOutstandingDeliveries: 1_024,
        MaximumSnapshotRecords: 64,
        MaximumDeltaRecords: 64,
        SpatialRegionEdgeMillimeters: 16_000,
        MaximumDeliveryAgeSteps: 1_024);

    internal void Validate()
    {
        if (MaximumRecipients <= 0 || MaximumRecipientChunkRows <= 0 ||
            MaximumSpatialRegions <= 0 || MaximumDirtyChunks <= 0 ||
            MaximumOutstandingDeliveries < MaximumRecipients || MaximumSnapshotRecords <= 0 ||
            MaximumDeltaRecords <= 0 || SpatialRegionEdgeMillimeters <= 0 ||
            MaximumDeliveryAgeSteps == 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumRecipients));
        if (MaximumSnapshotRecords > UltraSimGameProtocol.MaximumChunkPublicationRecords ||
            MaximumDeltaRecords > UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(MaximumSnapshotRecords));
        _ = checked(MaximumRecipients * 2);
    }
}

public enum DirtyChunkRecipientUpdateStatus : byte
{
    Reconciled = 1,
    NoChange = 2,
    InvalidInput = 3,
    CapacityExceeded = 4,
    SourceRequiresRecovery = 5,
}

public enum DirtyChunkProcessStatus : byte
{
    Success = 1,
    NoData = 2,
    CapacityExceeded = 3,
    CommitGapRequiresRecovery = 4,
    SourceEpochRequiresRecovery = 5,
}

public enum DirtyChunkBuildStatus : byte
{
    Success = 1,
    NoData = 2,
    NotDue = 3,
    UnknownRecipient = 4,
    CapacityExceeded = 5,
    SourceRequiresRecovery = 6,
    FutureCursor = 7,
}

public enum DirtyChunkAppliedProofStatus : byte
{
    Applied = 1,
    Duplicate = 2,
    Stale = 3,
    Rejected = 4,
    Invalid = 5,
}

/// <summary>One borrowed semantic dirty-chunk delivery. Spans remain valid until the next build.</summary>
public readonly ref struct DirtyChunkDelivery
{
    internal DirtyChunkDelivery(
        ParticipantId participant,
        ulong deliveryId,
        OfflineDirectChunkDeliveryTicket ticket,
        ChunkPublicationCursor baseCursor,
        ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> snapshot,
        ReadOnlySpan<WorldPublicDeltaRecord> deltas)
    {
        Participant = participant;
        DeliveryId = deliveryId;
        Ticket = ticket;
        BaseCursor = baseCursor;
        Batch = batch;
        Snapshot = snapshot;
        Deltas = deltas;
    }

    public ParticipantId Participant { get; }
    public ulong DeliveryId { get; }
    public OfflineDirectChunkDeliveryTicket Ticket { get; }
    public ChunkPublicationCursor BaseCursor { get; }
    public ChunkPublicationBatch Batch { get; }
    public OfflineDirectChunkReadStatus Status => Ticket.Status;
    public SpatialChunkKey Key => Ticket.Key;
    public ChunkPublicationCursor Cursor => Ticket.Cursor;
    public ReadOnlySpan<WorldPublicRecord> Snapshot { get; }
    public ReadOnlySpan<WorldPublicDeltaRecord> Deltas { get; }
}

/// <summary>
/// Cold attribution for Dirty Chunk Push. Recipient chunk rows are semantic knowledge/debt;
/// the spatial index contains recipient anchors only and has no chunk-to-recipient membership.
/// </summary>
public readonly record struct DirtyChunkSpatialFanoutSnapshot(
    PublicationEpoch SourceEpoch,
    int RecipientCount,
    int RecipientSpatialRegionCount,
    int RecipientChunkKnowledgeRowCount,
    int PendingChunkObligationCount,
    int DirtyChunkCount,
    int DirtyChunkCapacity,
    int OutstandingDeliveryCount,
    int OutstandingDeliveryCapacity,
    ulong SourceCommitsObserved,
    ulong DirtyChunksObserved,
    ulong DirtyChunksCoalescedBeforeDrain,
    ulong DirtyChunksProcessed,
    ulong SpatialRecipientDiscoveryOperations,
    ulong RecipientSpatialCellsExamined,
    ulong CandidateRecipientsExamined,
    ulong InterestedRecipientsDiscovered,
    ulong RecipientTopologyReconciliations,
    ulong IncomingCurrentTruthScopesQueued,
    ulong DirtyObligationsCreated,
    ulong DirtyObligationCoalesces,
    ulong CadenceDeferrals,
    ulong PublicationReads,
    ulong DeltaReads,
    ulong SnapshotFallbacks,
    ulong AbsenceReads,
    ulong ApplicationProofsApplied,
    ulong DuplicateStaleRejectedProofs,
    ulong CommitGapRecoveries,
    ulong SourceEpochRecoveries,
    ulong CapacityFailures,
    ulong DeliveryTimeouts,
    long ReservedBytes);

/// <summary>
/// Concrete chunk-granular dirty-time spatial fanout. Authoritative chunk history and cursors are
/// delegated to the accepted N1 recipient/store types; this object retains only bounded dirty keys,
/// recipient anchor buckets, and recipient-local semantic obligations.
/// </summary>
public sealed class DirtyChunkSpatialFanout
{
    public static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.DirtyChunkSpatialFanout;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly DirtyChunkSpatialFanoutLimits _limits;
    private readonly ChunkInterestProfile _interestProfile;
    private readonly OfflineDirectChunkCadence _cadence;
    private readonly int _subscriptionsPerRecipient;
    private readonly int _maximumOutstandingDeliveriesPerRecipient;
    private ChunkPublicationStore _source;
    private readonly RecipientSlot[] _recipients;
    private readonly int[] _recipientOutstandingDeliveries;
    private readonly DirtyChunkRecipientRow[] _rows;
    private readonly int[] _freeRows;
    private readonly int[] _rowIndex;
    private readonly int _rowIndexMask;
    private readonly int[] _recipientPendingHeads;
    private readonly int[] _recipientPendingCounts;
    private readonly DeliverySlot[] _deliveries;
    private readonly SpatialRegionKey[] _regionKeys;
    private readonly int[] _regionHeads;
    private readonly byte[] _regionStates;
    private readonly int _regionMask;
    private readonly SpatialChunkKey[] _dirtyKeys;
    private readonly SpatialChunkKey[] _dirtyIndexKeys;
    private readonly byte[] _dirtyIndexStates;
    private readonly int _dirtyIndexMask;
    private readonly ChunkInterestEntry[] _previousInterest;
    private readonly ChunkInterestEntry[] _desiredInterest;
    private readonly OfflineDirectChunkSubscriptionChange[] _changes;
    private readonly WorldPublicRecord[] _snapshotScratch;
    private readonly WorldPublicDeltaRecord[] _deltaScratch;
    private readonly ulong[] _candidateStamps;
    private int _recipientCount;
    private int _rowCount;
    private int _freeRowCount;
    private int _regionCount;
    private int _deliveryCount;
    private int _dirtyCount;
    private int _dirtyHead;
    private int _dirtyTail;
    private ulong _nextDeliveryId = 1;
    private PublicationEpoch _observedEpoch;
    private ulong _observedCommit;
    private ulong _observedHistoryInvalidations;
    private bool _hasObservedSource;
    private ulong _candidateStamp;
    private ulong _sourceCommitsObserved;
    private ulong _dirtyChunksObserved;
    private ulong _dirtyChunksCoalescedBeforeDrain;
    private ulong _dirtyChunksProcessed;
    private ulong _spatialRecipientDiscoveryOperations;
    private ulong _recipientSpatialCellsExamined;
    private ulong _candidateRecipientsExamined;
    private ulong _interestedRecipientsDiscovered;
    private ulong _recipientTopologyReconciliations;
    private ulong _incomingCurrentTruthScopesQueued;
    private ulong _dirtyObligationsCreated;
    private ulong _dirtyObligationCoalesces;
    private ulong _applicationProofsApplied;
    private ulong _duplicateStaleRejectedProofs;
    private ulong _commitGapRecoveries;
    private ulong _sourceEpochRecoveries;
    private ulong _capacityFailures;
    private ulong _deliveryTimeouts;

    public DirtyChunkSpatialFanout(
        ChunkPublicationStore source,
        DirtyChunkSpatialFanoutLimits limits = default,
        ChunkInterestProfile interestProfile = default,
        OfflineDirectChunkCadence cadence = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (limits == default) limits = DirtyChunkSpatialFanoutLimits.Default;
        if (interestProfile == default) interestProfile = GameChunkInterestProfile.Default;
        if (cadence == default) cadence = OfflineDirectChunkCadence.EvidenceProfile;
        limits.Validate();
        if (!interestProfile.IsValid) throw new ArgumentOutOfRangeException(nameof(interestProfile));
        if (!cadence.IsValid) throw new ArgumentOutOfRangeException(nameof(cadence));
        _source = source;
        _limits = limits;
        _interestProfile = interestProfile;
        _cadence = cadence;
        _subscriptionsPerRecipient = Math.Max(1,
            (limits.MaximumRecipientChunkRows + limits.MaximumRecipients - 1) /
            limits.MaximumRecipients);
        _maximumOutstandingDeliveriesPerRecipient = Math.Max(
            1, limits.MaximumOutstandingDeliveries / limits.MaximumRecipients);
        _recipients = new RecipientSlot[limits.MaximumRecipients];
        _recipientOutstandingDeliveries = new int[limits.MaximumRecipients];
        _rows = new DirtyChunkRecipientRow[limits.MaximumRecipientChunkRows];
        _freeRows = new int[limits.MaximumRecipientChunkRows];
        int rowIndexCapacity = MapCapacity(limits.MaximumRecipientChunkRows);
        _rowIndex = new int[rowIndexCapacity];
        _rowIndexMask = rowIndexCapacity - 1;
        _recipientPendingHeads = new int[limits.MaximumRecipients];
        _recipientPendingCounts = new int[limits.MaximumRecipients];
        Array.Fill(_recipientPendingHeads, -1);
        for (int index = 0; index < _freeRows.Length; index++)
            _freeRows[index] = _freeRows.Length - index - 1;
        _freeRowCount = _freeRows.Length;
        _deliveries = new DeliverySlot[limits.MaximumOutstandingDeliveries];
        int mapCapacity = MapCapacity(limits.MaximumSpatialRegions);
        _regionKeys = new SpatialRegionKey[mapCapacity];
        _regionHeads = new int[mapCapacity];
        _regionStates = new byte[mapCapacity];
        _regionMask = mapCapacity - 1;
        Array.Fill(_regionHeads, -1);
        _dirtyKeys = new SpatialChunkKey[limits.MaximumDirtyChunks];
        int dirtyIndexCapacity = MapCapacity(limits.MaximumDirtyChunks);
        _dirtyIndexKeys = new SpatialChunkKey[dirtyIndexCapacity];
        _dirtyIndexStates = new byte[dirtyIndexCapacity];
        _dirtyIndexMask = dirtyIndexCapacity - 1;
        _previousInterest = new ChunkInterestEntry[_subscriptionsPerRecipient];
        _desiredInterest = new ChunkInterestEntry[_subscriptionsPerRecipient];
        _changes = new OfflineDirectChunkSubscriptionChange[checked(_subscriptionsPerRecipient * 2)];
        _snapshotScratch = new WorldPublicRecord[limits.MaximumSnapshotRecords];
        _deltaScratch = new WorldPublicDeltaRecord[limits.MaximumDeltaRecords];
        _candidateStamps = new ulong[limits.MaximumRecipients];
        for (int index = 0; index < _recipients.Length; index++)
        {
            _recipients[index].Recipient = CreateRecipient();
        }
        _observedEpoch = source.Epoch;
        _observedHistoryInvalidations = source.GetSnapshot().HistoryInvalidations;
    }

    public ChunkPublicationStore Source => _source;
    public DirtyChunkSpatialFanoutLimits Limits => _limits;
    public ChunkInterestProfile InterestProfile => _interestProfile;
    public int RecipientCount => _recipientCount;

    /// <summary>Admits or authoritatively moves one recipient and reconciles its current chunk scope.</summary>
    public DirtyChunkRecipientUpdateStatus SetRecipientAnchor(
        ParticipantId participant,
        SpatialSpaceId space,
        in WorldPosition anchor,
        ulong cadenceStep)
        => SetRecipientAnchor(participant, space, anchor, cadenceStep, _changes, out _);

    /// <summary>
    /// Admits or moves one recipient and returns the exact topology changes published by the
    /// retained chunk recipient. Failure publishes neither recipient topology nor change rows.
    /// </summary>
    public DirtyChunkRecipientUpdateStatus SetRecipientAnchor(
        ParticipantId participant,
        SpatialSpaceId space,
        in WorldPosition anchor,
        ulong cadenceStep,
        Span<OfflineDirectChunkSubscriptionChange> changes,
        out OfflineDirectChunkReconcileResult result)
    {
        EnsureOwner();
        result = default;
        if (!participant.IsValid || !space.IsValid)
            return DirtyChunkRecipientUpdateStatus.InvalidInput;
        int recipientIndex = FindRecipient(participant);
        bool isNew = recipientIndex < 0;
        if (isNew)
        {
            recipientIndex = FindFreeRecipient();
            if (recipientIndex < 0)
                return CapacityFailure<DirtyChunkRecipientUpdateStatus>(DirtyChunkRecipientUpdateStatus.CapacityExceeded);
        }

        ref RecipientSlot slot = ref _recipients[recipientIndex];
        SpatialRegionKey newRegion = RegionFor(space, anchor);
        int oldRegion = slot.Active ? slot.RegionMapIndex : -1;
        if (FindRegion(newRegion) < 0 &&
            _regionCount >= _limits.MaximumSpatialRegions)
            return CapacityFailure<DirtyChunkRecipientUpdateStatus>(DirtyChunkRecipientUpdateStatus.CapacityExceeded);

        if (!TryPreflightReconcile(slot.Recipient, space, anchor, out int desiredCount,
                out int enters, out int leaves))
            return CapacityFailure<DirtyChunkRecipientUpdateStatus>(DirtyChunkRecipientUpdateStatus.CapacityExceeded);
        if (_rowCount - leaves + enters > _limits.MaximumRecipientChunkRows)
            return CapacityFailure<DirtyChunkRecipientUpdateStatus>(DirtyChunkRecipientUpdateStatus.CapacityExceeded);

        OfflineDirectChunkReconcileStatus status = slot.Recipient.Reconcile(
            _source, space, anchor, changes, out result);
        if (status != OfflineDirectChunkReconcileStatus.Reconciled)
        {
            if (status is OfflineDirectChunkReconcileStatus.SubscriptionCapacityExceeded or
                OfflineDirectChunkReconcileStatus.ChangeDestinationTooSmall)
                return CapacityFailure<DirtyChunkRecipientUpdateStatus>(DirtyChunkRecipientUpdateStatus.CapacityExceeded);
            return status == OfflineDirectChunkReconcileStatus.InvalidInterest
                ? DirtyChunkRecipientUpdateStatus.InvalidInput
                : DirtyChunkRecipientUpdateStatus.SourceRequiresRecovery;
        }

        if (isNew)
        {
            slot.Participant = participant;
            slot.Active = true;
            _recipientCount++;
        }
        slot.Space = space;
        slot.Anchor = anchor;
        slot.RegionMapIndex = FindOrCreateRegion(newRegion);
        if (oldRegion >= 0 && oldRegion != slot.RegionMapIndex)
            MoveRecipientRegion(recipientIndex, oldRegion, slot.RegionMapIndex);
        else if (oldRegion < 0)
            AddRecipientToRegion(recipientIndex, slot.RegionMapIndex);

        _recipientTopologyReconciliations++;
        ApplyReconciliation(recipientIndex, changes, result);
        if (result.SourceReset && !isNew)
        {
            for (int subscription = 0; subscription < slot.Recipient.Subscriptions.Length; subscription++)
                QueueCurrentTruth(recipientIndex, slot.Recipient.Subscriptions[subscription].Interest.Key);
        }
        return result.ChangeCount == 0 && !isNew
            ? DirtyChunkRecipientUpdateStatus.NoChange
            : DirtyChunkRecipientUpdateStatus.Reconciled;
    }

    /// <summary>Retires all recipient-local chunk scope without manufacturing source absence.</summary>
    public bool RemoveRecipient(ParticipantId participant)
    {
        EnsureOwner();
        int index = FindRecipient(participant);
        if (index < 0) return false;
        ref RecipientSlot slot = ref _recipients[index];
        for (int row = 0; row < _rows.Length; row++)
            if (_rows[row].Active && _rows[row].RecipientIndex == index)
                RetireRow(row);
        InvalidateDeliveriesForRecipient(index);
        RemoveRecipientFromRegion(index, slot.RegionMapIndex);
        _recipientPendingHeads[index] = -1;
        _recipientPendingCounts[index] = 0;
        _recipientOutstandingDeliveries[index] = 0;
        OfflineDirectChunkRecipient retainedRecipient = slot.Recipient;
        retainedRecipient.ResetForReuse();
        slot = new RecipientSlot { Recipient = retainedRecipient };
        _recipientCount--;
        return true;
    }

    /// <summary>Rebinds the authoritative source epoch and queues current retained scopes for truth.</summary>
    public bool ReplaceSource(ChunkPublicationStore source)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(source);
        if (source.Epoch == _source.Epoch) return false;
        InvalidateAllDeliveries();
        _source = source;
        _observedEpoch = source.Epoch;
        _observedCommit = 0;
        _observedHistoryInvalidations = source.GetSnapshot().HistoryInvalidations;
        _hasObservedSource = false;
        _sourceEpochRecoveries++;
        for (int index = 0; index < _recipients.Length; index++)
        {
            if (!_recipients[index].Active) continue;
            RecipientSlot slot = _recipients[index];
            DirtyChunkRecipientUpdateStatus status = SetRecipientAnchor(
                slot.Participant, slot.Space, slot.Anchor, 0);
            if (status == DirtyChunkRecipientUpdateStatus.CapacityExceeded)
                return false;
        }
        return true;
    }

    /// <summary>Observes one authoritative source and retains each changed chunk exactly once.</summary>
    public DirtyChunkProcessStatus TryProcessDirtyChunks(ulong cadenceStep, out int processed)
    {
        EnsureOwner();
        processed = 0;
        ExpireOutstandingDeliveries(cadenceStep);
        if (_source.Epoch != _observedEpoch)
            return DirtyChunkProcessStatus.SourceEpochRequiresRecovery;
        DirtyChunkProcessStatus observation = ObserveSource();
        bool observationCapacity = observation == DirtyChunkProcessStatus.CapacityExceeded;
        if (observationCapacity && _dirtyCount == 0)
            return observation;
        while (_dirtyCount != 0)
        {
            SpatialChunkKey key = _dirtyKeys[_dirtyHead];
            DirtyChunkProcessStatus status = ProcessDirtyKey(key, cadenceStep);
            if (status == DirtyChunkProcessStatus.CapacityExceeded)
                return status;
            DequeueDirty(key);
            processed++;
            _dirtyChunksProcessed++;
        }
        if (observationCapacity) return DirtyChunkProcessStatus.CapacityExceeded;
        return processed == 0 && observation == DirtyChunkProcessStatus.NoData
            ? DirtyChunkProcessStatus.NoData
            : observation == DirtyChunkProcessStatus.CommitGapRequiresRecovery
                ? observation
                : DirtyChunkProcessStatus.Success;
    }

    public DirtyChunkProcessStatus TryProcessDirtyChunks(long cadenceStep, out int processed) =>
        TryProcessDirtyChunks(cadenceStep < 0 ? 0UL : checked((ulong)cadenceStep), out processed);

    /// <summary>
    /// Observes source commit lineage without draining dirty keys. This is useful when the game
    /// batches several authoritative commits before one bounded dirty-work pass.
    /// </summary>
    public DirtyChunkProcessStatus ObserveDirtyChunks()
    {
        EnsureOwner();
        if (_source.Epoch != _observedEpoch)
            return DirtyChunkProcessStatus.SourceEpochRequiresRecovery;
        return ObserveSource();
    }

    /// <summary>Builds one current chunk delivery from recipient applied knowledge to source truth.</summary>
    public DirtyChunkBuildStatus TryBuildDelivery(
        ParticipantId participant,
        ulong cadenceStep,
        out DirtyChunkDelivery delivery)
        => TryBuildDelivery(participant, default, cadenceStep, out delivery, useKey: false);

    /// <summary>Builds one specific pending chunk, useful when a caller owns per-key scheduling.</summary>
    public DirtyChunkBuildStatus TryBuildDelivery(
        ParticipantId participant,
        SpatialChunkKey key,
        ulong cadenceStep,
        out DirtyChunkDelivery delivery)
        => TryBuildDelivery(participant, key, cadenceStep, out delivery, useKey: true);

    private DirtyChunkBuildStatus TryBuildDelivery(
        ParticipantId participant,
        SpatialChunkKey key,
        ulong cadenceStep,
        out DirtyChunkDelivery delivery,
        bool useKey)
    {
        EnsureOwner();
        ExpireOutstandingDeliveries(cadenceStep);
        delivery = default;
        int recipientIndex = FindRecipient(participant);
        if (recipientIndex < 0) return DirtyChunkBuildStatus.UnknownRecipient;
        if (!useKey)
        {
            if (_recipientPendingCounts[recipientIndex] == 0)
                return DirtyChunkBuildStatus.NoData;
            for (int index = _recipientPendingHeads[recipientIndex]; index >= 0;)
            {
                int next = _rows[index].PendingNext;
                if (_rows[index].PendingWasSent)
                {
                    index = next;
                    continue;
                }
                DirtyChunkBuildStatus status = TryBuildDeliveryAtRow(
                    participant, recipientIndex, index, cadenceStep, out delivery);
                if (status is DirtyChunkBuildStatus.NotDue or DirtyChunkBuildStatus.NoData)
                {
                    index = next;
                    continue;
                }
                return status;
            }
            return DirtyChunkBuildStatus.NoData;
        }
        int rowIndex = FindPendingRow(recipientIndex, key);
        if (rowIndex < 0) return DirtyChunkBuildStatus.NoData;
        return TryBuildDeliveryAtRow(participant, recipientIndex, rowIndex, cadenceStep, out delivery);
    }

    private DirtyChunkBuildStatus TryBuildDeliveryAtRow(
        ParticipantId participant,
        int recipientIndex,
        int rowIndex,
        ulong cadenceStep,
        out DirtyChunkDelivery delivery)
    {
        delivery = default;
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        int subscription = FindSubscription(recipientIndex, row.Key);
        if (subscription < 0)
        {
            RetireRow(rowIndex);
            return DirtyChunkBuildStatus.NoData;
        }
        if (row.PendingWasSent) return DirtyChunkBuildStatus.NoData;
        if (_recipientOutstandingDeliveries[recipientIndex] >=
            _maximumOutstandingDeliveriesPerRecipient)
            return DirtyChunkBuildStatus.CapacityExceeded;
        if (_deliveryCount >= _deliveries.Length || _nextDeliveryId == 0)
            return CapacityFailure<DirtyChunkBuildStatus>(DirtyChunkBuildStatus.CapacityExceeded);

        ChunkPublicationCursor baseCursor = _recipients[recipientIndex].Recipient.Subscriptions[subscription].Cursor;
        OfflineDirectChunkReadStatus status = _recipients[recipientIndex].Recipient.TryBeginRead(
            _source, subscription, cadenceStep, out OfflineDirectChunkRead read);
        if (status == OfflineDirectChunkReadStatus.NotDue)
            return DirtyChunkBuildStatus.NotDue;
        if (status == OfflineDirectChunkReadStatus.FutureCursor)
            return DirtyChunkBuildStatus.FutureCursor;
        if (status == OfflineDirectChunkReadStatus.SourceRequiresReconcile)
            return DirtyChunkBuildStatus.SourceRequiresRecovery;
        if (status is OfflineDirectChunkReadStatus.SnapshotCapacityExceeded or
            OfflineDirectChunkReadStatus.DeltaCapacityExceeded)
            return CapacityFailure<DirtyChunkBuildStatus>(DirtyChunkBuildStatus.CapacityExceeded);
        if (status == OfflineDirectChunkReadStatus.NoChanges)
        {
            _recipients[recipientIndex].Recipient.CommitRead();
            DeactivatePending(rowIndex);
            row.PendingWasSent = false;
            row.PendingDeliveryId = 0;
            return DirtyChunkBuildStatus.NoData;
        }
        if (status is not (OfflineDirectChunkReadStatus.Snapshot or
            OfflineDirectChunkReadStatus.Delta or OfflineDirectChunkReadStatus.ChunkAbsent))
            return DirtyChunkBuildStatus.NoData;

        read.Snapshot.CopyTo(_snapshotScratch);
        read.Deltas.CopyTo(_deltaScratch);
        ulong deliveryId = _nextDeliveryId;
        _nextDeliveryId = deliveryId == ulong.MaxValue ? 0 : deliveryId + 1;
        OfflineDirectChunkDeliveryTicket ticket =
            _recipients[recipientIndex].Recipient.ReleaseReadForRemote(deliveryId);
        int deliveryIndex = FindFreeDelivery();
        _deliveries[deliveryIndex] = new DeliverySlot
        {
            Active = true,
            Participant = participant,
            RecipientIndex = recipientIndex,
            RowIndex = rowIndex,
            Ticket = ticket,
            SentCadenceStep = cadenceStep,
        };
        _deliveryCount++;
        _recipientOutstandingDeliveries[recipientIndex]++;
        row.PendingWasSent = true;
        row.PendingDeliveryId = deliveryId;
        delivery = new DirtyChunkDelivery(
            participant, deliveryId, ticket, baseCursor, read.Batch,
            _snapshotScratch.AsSpan(0, read.Snapshot.Length),
            _deltaScratch.AsSpan(0, read.Deltas.Length));
        return DirtyChunkBuildStatus.Success;
    }

    /// <summary>
    /// Rearms sent semantic debt whose application proof did not arrive within the bounded
    /// delivery age. The old proof is then stale; it can never advance knowledge.
    /// </summary>
    public int ExpireOutstandingDeliveries(ulong cadenceStep)
    {
        EnsureOwner();
        int expired = 0;
        for (int index = 0; index < _deliveries.Length; index++)
        {
            if (!_deliveries[index].Active ||
                cadenceStep < _deliveries[index].SentCadenceStep ||
                cadenceStep - _deliveries[index].SentCadenceStep < _limits.MaximumDeliveryAgeSteps)
                continue;
            ref DeliverySlot delivery = ref _deliveries[index];
            ref DirtyChunkRecipientRow row = ref _rows[delivery.RowIndex];
            if (row.Active && row.Pending && row.PendingWasSent &&
                row.PendingDeliveryId == delivery.Ticket.DeliveryId)
            {
                row.PendingWasSent = false;
                row.PendingDeliveryId = 0;
            }
            ClearDelivery(index);
            expired++;
            if (_deliveryTimeouts != ulong.MaxValue) _deliveryTimeouts++;
        }
        return expired;
    }

    /// <summary>Records the only knowledge-changing event: a matching semantic application proof.</summary>
    public DirtyChunkAppliedProofStatus TryAcknowledgeDelivery(
        ParticipantId participant,
        ulong deliveryId,
        bool applied)
    {
        EnsureOwner();
        int deliveryIndex = FindDelivery(participant, deliveryId);
        if (deliveryIndex < 0)
        {
            _duplicateStaleRejectedProofs++;
            return DirtyChunkAppliedProofStatus.Stale;
        }
        ref DeliverySlot delivery = ref _deliveries[deliveryIndex];
        int rowIndex = delivery.RowIndex;
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        bool current = row.Active && row.Pending && row.PendingWasSent &&
            row.PendingDeliveryId == deliveryId;
        if (!current)
        {
            ClearDelivery(deliveryIndex);
            _duplicateStaleRejectedProofs++;
            return DirtyChunkAppliedProofStatus.Stale;
        }
        if (!applied)
        {
            ClearDelivery(deliveryIndex);
            row.PendingWasSent = false;
            row.PendingDeliveryId = 0;
            _duplicateStaleRejectedProofs++;
            return DirtyChunkAppliedProofStatus.Rejected;
        }

        OfflineDirectChunkAppliedProofStatus committed =
            _recipients[delivery.RecipientIndex].Recipient.CommitAppliedProof(delivery.Ticket);
        ClearDelivery(deliveryIndex);
        if (committed is OfflineDirectChunkAppliedProofStatus.Applied or
            OfflineDirectChunkAppliedProofStatus.Duplicate or
            OfflineDirectChunkAppliedProofStatus.Stale)
        {
            DeactivatePending(rowIndex);
            row.PendingWasSent = false;
            row.PendingDeliveryId = 0;
            if (committed == OfflineDirectChunkAppliedProofStatus.Applied)
                _applicationProofsApplied++;
            else
                _duplicateStaleRejectedProofs++;
            return committed == OfflineDirectChunkAppliedProofStatus.Applied
                ? DirtyChunkAppliedProofStatus.Applied
                : committed == OfflineDirectChunkAppliedProofStatus.Duplicate
                    ? DirtyChunkAppliedProofStatus.Duplicate
                    : DirtyChunkAppliedProofStatus.Stale;
        }
        row.PendingWasSent = false;
        row.PendingDeliveryId = 0;
        _duplicateStaleRejectedProofs++;
        return DirtyChunkAppliedProofStatus.Invalid;
    }

    public bool AbandonDelivery(ParticipantId participant, ulong deliveryId)
    {
        EnsureOwner();
        int index = FindDelivery(participant, deliveryId);
        if (index < 0) return false;
        ref DeliverySlot delivery = ref _deliveries[index];
        ref DirtyChunkRecipientRow row = ref _rows[delivery.RowIndex];
        if (row.Active && row.PendingDeliveryId == deliveryId)
        {
            row.PendingWasSent = false;
            row.PendingDeliveryId = 0;
        }
        ClearDelivery(index);
        return true;
    }

    internal bool IsDeliveryActive(ParticipantId participant, ulong deliveryId)
    {
        EnsureOwner();
        return FindDelivery(participant, deliveryId) >= 0;
    }

    public DirtyChunkSpatialFanoutSnapshot GetSnapshot()
    {
        EnsureOwner();
        ulong cadenceDeferrals = 0;
        ulong publicationReads = 0;
        ulong deltaReads = 0;
        ulong snapshotFallbacks = 0;
        ulong absenceReads = 0;
        long reserved = checked(
            (long)_recipients.Length * Unsafe.SizeOf<RecipientSlot>() +
            (long)_recipientOutstandingDeliveries.Length * sizeof(int) +
            (long)_rows.Length * Unsafe.SizeOf<DirtyChunkRecipientRow>() +
            (long)_freeRows.Length * sizeof(int) +
            (long)_rowIndex.Length * sizeof(int) +
            (long)_recipientPendingHeads.Length * sizeof(int) +
            (long)_recipientPendingCounts.Length * sizeof(int) +
            (long)_deliveries.Length * Unsafe.SizeOf<DeliverySlot>() +
            (long)_regionKeys.Length * Unsafe.SizeOf<SpatialRegionKey>() +
            (long)_regionHeads.Length * sizeof(int) + _regionStates.Length +
            (long)_dirtyKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
            (long)_dirtyIndexKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
            _dirtyIndexStates.Length +
            (long)_previousInterest.Length * Unsafe.SizeOf<ChunkInterestEntry>() +
            (long)_desiredInterest.Length * Unsafe.SizeOf<ChunkInterestEntry>() +
            (long)_changes.Length * Unsafe.SizeOf<OfflineDirectChunkSubscriptionChange>() +
            (long)_snapshotScratch.Length * Unsafe.SizeOf<WorldPublicRecord>() +
            (long)_deltaScratch.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>() +
            (long)_candidateStamps.Length * sizeof(ulong));
        for (int index = 0; index < _recipients.Length; index++)
        {
            reserved = checked(reserved +
                _recipients[index].Recipient.GetStorageSnapshot().TotalReservedBytes);
            if (!_recipients[index].Active) continue;
            OfflineDirectChunkRecipientSnapshot snapshot = _recipients[index].Recipient.GetSnapshot();
            cadenceDeferrals += snapshot.CadenceDeferrals;
            publicationReads += snapshot.SnapshotReads + snapshot.NoChangeReads;
            deltaReads += snapshot.DeltaReads;
            snapshotFallbacks += snapshot.SnapshotFallbacks;
            if (snapshot.ChunkAbsentReads != 0) absenceReads += snapshot.ChunkAbsentReads;
        }
        int pending = 0;
        for (int index = 0; index < _rows.Length; index++)
            if (_rows[index].Active && _rows[index].Pending) pending++;
        return new(
            _source.Epoch, _recipientCount, _regionCount, _rowCount, pending,
            _dirtyCount, _dirtyKeys.Length, _deliveryCount, _deliveries.Length,
            _sourceCommitsObserved, _dirtyChunksObserved, _dirtyChunksCoalescedBeforeDrain,
            _dirtyChunksProcessed, _spatialRecipientDiscoveryOperations,
            _recipientSpatialCellsExamined, _candidateRecipientsExamined,
            _interestedRecipientsDiscovered, _recipientTopologyReconciliations,
            _incomingCurrentTruthScopesQueued, _dirtyObligationsCreated,
            _dirtyObligationCoalesces, cadenceDeferrals, publicationReads, deltaReads,
            snapshotFallbacks, absenceReads, _applicationProofsApplied,
            _duplicateStaleRejectedProofs, _commitGapRecoveries, _sourceEpochRecoveries,
            _capacityFailures, _deliveryTimeouts, reserved);
    }

    private DirtyChunkProcessStatus ObserveSource()
    {
        ulong commit = _source.CommitSequence;
        ulong historyInvalidations = _source.GetSnapshot().HistoryInvalidations;
        if (!_hasObservedSource)
        {
            _hasObservedSource = true;
            _observedCommit = 0;
            _observedEpoch = _source.Epoch;
            _observedHistoryInvalidations = historyInvalidations;
        }
        if (commit == _observedCommit) return DirtyChunkProcessStatus.NoData;
        if (commit != _observedCommit + 1)
        {
            _commitGapRecoveries++;
            if (!QueueAllCurrentScopes()) return DirtyChunkProcessStatus.CapacityExceeded;
            _observedCommit = commit;
            _observedHistoryInvalidations = historyInvalidations;
            _sourceCommitsObserved++;
            return DirtyChunkProcessStatus.CommitGapRequiresRecovery;
        }
        if (historyInvalidations != _observedHistoryInvalidations)
        {
            if (!QueueAllCurrentScopes()) return DirtyChunkProcessStatus.CapacityExceeded;
            _observedHistoryInvalidations = historyInvalidations;
            _observedCommit = commit;
            _sourceCommitsObserved++;
            return DirtyChunkProcessStatus.CommitGapRequiresRecovery;
        }
        ReadOnlySpan<SpatialChunkKey> dirty = _source.LastCommittedDirtyChunks;
        int needed = 0;
        for (int index = 0; index < dirty.Length; index++)
            if (!ContainsDirty(dirty[index])) needed++;
        if (_dirtyCount + needed > _dirtyKeys.Length)
            return CapacityFailure<DirtyChunkProcessStatus>(DirtyChunkProcessStatus.CapacityExceeded);
        for (int index = 0; index < dirty.Length; index++)
            EnqueueDirty(dirty[index]);
        _observedCommit = commit;
        _observedHistoryInvalidations = historyInvalidations;
        _sourceCommitsObserved++;
        return dirty.Length == 0 ? DirtyChunkProcessStatus.NoData : DirtyChunkProcessStatus.Success;
    }

    private DirtyChunkProcessStatus ProcessDirtyKey(SpatialChunkKey key, ulong cadenceStep)
    {
        _spatialRecipientDiscoveryOperations++;
        _candidateStamp = _candidateStamp == ulong.MaxValue ? 1 : _candidateStamp + 1;
        long edge = GameWorldSpatialProfile.WorldChunkEdgeUnits;
        long minimumX = checked((long)key.Coord.X * edge - _interestProfile.FarLeaveRadiusMillimeters);
        long maximumX = checked((long)(key.Coord.X + 1L) * edge + _interestProfile.FarLeaveRadiusMillimeters);
        long minimumZ = checked((long)key.Coord.Z * edge - _interestProfile.FarLeaveRadiusMillimeters);
        long maximumZ = checked((long)(key.Coord.Z + 1L) * edge + _interestProfile.FarLeaveRadiusMillimeters);
        FloorDiv(minimumX, _limits.SpatialRegionEdgeMillimeters, out long firstX);
        FloorDiv(maximumX, _limits.SpatialRegionEdgeMillimeters, out long lastX);
        FloorDiv(minimumZ, _limits.SpatialRegionEdgeMillimeters, out long firstZ);
        FloorDiv(maximumZ, _limits.SpatialRegionEdgeMillimeters, out long lastZ);
        for (long x = firstX; x <= lastX; x++)
        {
            for (long z = firstZ; z <= lastZ; z++)
            {
                _recipientSpatialCellsExamined++;
                if (x < int.MinValue || x > int.MaxValue || z < int.MinValue || z > int.MaxValue)
                    continue;
                int region = FindRegion(new SpatialRegionKey(key.Space, (int)x, (int)z));
                if (region < 0) continue;
                for (int recipient = _regionHeads[region]; recipient >= 0;
                    recipient = _recipients[recipient].NextRegionRecipient)
                {
                    if (_candidateStamps[recipient] == _candidateStamp) continue;
                    _candidateStamps[recipient] = _candidateStamp;
                    _candidateRecipientsExamined++;
                    int subscription = FindSubscription(recipient, key);
                    if (subscription < 0) continue;
                    _interestedRecipientsDiscovered++;
                    if (!MarkDirtyObligation(recipient, key,
                            _recipients[recipient].Recipient.Subscriptions[subscription].Interest.Tier))
                        return CapacityFailure<DirtyChunkProcessStatus>(DirtyChunkProcessStatus.CapacityExceeded);
                }
            }
        }
        return DirtyChunkProcessStatus.Success;
    }

    private bool MarkDirtyObligation(int recipientIndex, SpatialChunkKey key, ChunkInterestTier tier)
    {
        int rowIndex = FindRow(recipientIndex, key);
        if (rowIndex < 0)
        {
            if (_rowCount >= _rows.Length) return false;
            rowIndex = FindFreeRow();
            _rows[rowIndex] = new DirtyChunkRecipientRow
            {
                Active = true, RecipientIndex = recipientIndex,
                Key = key, Tier = tier, PendingPrevious = -1, PendingNext = -1,
            };
            AddRowIndex(rowIndex);
            ActivatePending(rowIndex);
            _rowCount++;
            _dirtyObligationsCreated++;
            return true;
        }
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        row.Tier = tier;
        if (row.Pending && !row.PendingWasSent)
        {
            _dirtyObligationCoalesces++;
            return true;
        }
        if (row.PendingWasSent)
        {
            InvalidateDelivery(row.PendingDeliveryId);
            row.PendingWasSent = false;
            row.PendingDeliveryId = 0;
        }
        ActivatePending(rowIndex);
        _dirtyObligationCoalesces++;
        return true;
    }

    private void ApplyReconciliation(
        int recipientIndex,
        ReadOnlySpan<OfflineDirectChunkSubscriptionChange> changes,
        in OfflineDirectChunkReconcileResult result)
    {
        for (int index = 0; index < result.ChangeCount; index++)
        {
            ref readonly OfflineDirectChunkSubscriptionChange change = ref changes[index];
            if (change.Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
            {
                int row = FindRow(recipientIndex, change.Key);
                if (row >= 0) RetireRow(row);
            }
            else if (change.Kind == OfflineDirectChunkSubscriptionChangeKind.Entered)
            {
                int row = FindRow(recipientIndex, change.Key);
                if (row < 0)
                {
                    row = FindFreeRow();
                    _rows[row] = new DirtyChunkRecipientRow
                    {
                        Active = true, RecipientIndex = recipientIndex,
                        Key = change.Key, Tier = change.CurrentTier,
                        PendingPrevious = -1, PendingNext = -1,
                    };
                    AddRowIndex(row);
                    ActivatePending(row);
                    _rowCount++;
                    _incomingCurrentTruthScopesQueued++;
                }
            }
            else
            {
                int row = FindRow(recipientIndex, change.Key);
                if (row >= 0) _rows[row].Tier = change.CurrentTier;
            }
        }
    }

    private bool QueueAllCurrentScopes()
    {
        for (int index = 0; index < _rows.Length; index++)
        {
            if (!_rows[index].Active) continue;
            QueueCurrentTruth(_rows[index].RecipientIndex, _rows[index].Key);
        }
        return true;
    }

    private void QueueCurrentTruth(int recipientIndex, SpatialChunkKey key)
    {
        int row = FindRow(recipientIndex, key);
        if (row >= 0)
        {
            ActivatePending(row);
            if (_rows[row].PendingWasSent)
            {
                InvalidateDelivery(_rows[row].PendingDeliveryId);
                _rows[row].PendingWasSent = false;
                _rows[row].PendingDeliveryId = 0;
            }
        }
        _incomingCurrentTruthScopesQueued++;
    }

    private bool TryPreflightReconcile(
        OfflineDirectChunkRecipient recipient,
        SpatialSpaceId space,
        in WorldPosition anchor,
        out int desiredCount,
        out int enters,
        out int leaves)
    {
        var view = recipient.Subscriptions;
        int previousCount = view.Length;
        if (previousCount > _previousInterest.Length) { desiredCount = enters = leaves = 0; return false; }
        for (int index = 0; index < previousCount; index++)
            _previousInterest[index] = view[index].Interest;
        ChunkInterestQueryStatus query = ChunkInterestQuery.TryQuery(
            space, anchor, _interestProfile, _previousInterest.AsSpan(0, previousCount),
            _desiredInterest, out desiredCount, out int required, out _);
        if (query != ChunkInterestQueryStatus.Success || required != desiredCount)
        {
            enters = leaves = 0;
            return false;
        }
        enters = 0;
        leaves = 0;
        int previous = 0;
        int desired = 0;
        while (previous < previousCount || desired < desiredCount)
        {
            if (previous == previousCount) { enters += desiredCount - desired; break; }
            if (desired == desiredCount) { leaves += previousCount - previous; break; }
            int comparison = _previousInterest[previous].Key.CompareTo(_desiredInterest[desired].Key);
            if (comparison < 0) { leaves++; previous++; }
            else if (comparison > 0) { enters++; desired++; }
            else { previous++; desired++; }
        }
        return true;
    }

    private int FindSubscription(int recipientIndex, SpatialChunkKey key)
    {
        var view = _recipients[recipientIndex].Recipient.Subscriptions;
        int low = 0;
        int high = view.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = view[middle].Interest.Key.CompareTo(key);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        return -1;
    }

    private int FindPendingRow(int recipientIndex, SpatialChunkKey key)
    {
        int index = FindRow(recipientIndex, key);
        return index >= 0 && _rows[index].Pending && !_rows[index].PendingWasSent
            ? index
            : -1;
    }

    private void AddRowIndex(int rowIndex)
    {
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        int slot = RowHash(row.RecipientIndex, row.Key) & _rowIndexMask;
        int firstDeleted = -1;
        for (int probe = 0; probe < _rowIndex.Length; probe++)
        {
            int value = _rowIndex[slot];
            if (value == 0)
            {
                _rowIndex[firstDeleted >= 0 ? firstDeleted : slot] = rowIndex + 1;
                return;
            }
            if (value < 0 && firstDeleted < 0) firstDeleted = slot;
            slot = (slot + 1) & _rowIndexMask;
        }
        if (firstDeleted >= 0)
        {
            _rowIndex[firstDeleted] = rowIndex + 1;
            return;
        }
        throw new InvalidOperationException("Dirty-chunk row index capacity diverged from row capacity.");
    }

    private void RemoveRowIndex(int rowIndex)
    {
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        int slot = RowHash(row.RecipientIndex, row.Key) & _rowIndexMask;
        for (int probe = 0; probe < _rowIndex.Length; probe++)
        {
            int value = _rowIndex[slot];
            if (value == 0) return;
            if (value == rowIndex + 1)
            {
                _rowIndex[slot] = -1;
                return;
            }
            slot = (slot + 1) & _rowIndexMask;
        }
    }

    private void ActivatePending(int rowIndex)
    {
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        if (row.Pending) return;
        int recipientIndex = row.RecipientIndex;
        row.Pending = true;
        row.PendingPrevious = -1;
        row.PendingNext = _recipientPendingHeads[recipientIndex];
        if (row.PendingNext >= 0)
            _rows[row.PendingNext].PendingPrevious = rowIndex;
        _recipientPendingHeads[recipientIndex] = rowIndex;
        _recipientPendingCounts[recipientIndex]++;
    }

    private void DeactivatePending(int rowIndex)
    {
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        if (!row.Pending) return;
        int recipientIndex = row.RecipientIndex;
        if (row.PendingPrevious >= 0)
            _rows[row.PendingPrevious].PendingNext = row.PendingNext;
        else
            _recipientPendingHeads[recipientIndex] = row.PendingNext;
        if (row.PendingNext >= 0)
            _rows[row.PendingNext].PendingPrevious = row.PendingPrevious;
        row.Pending = false;
        row.PendingPrevious = -1;
        row.PendingNext = -1;
        _recipientPendingCounts[recipientIndex]--;
    }

    private int FindRow(int recipientIndex, SpatialChunkKey key)
    {
        int slot = RowHash(recipientIndex, key) & _rowIndexMask;
        for (int probe = 0; probe < _rowIndex.Length; probe++)
        {
            int value = _rowIndex[slot];
            if (value == 0) return -1;
            if (value > 0)
            {
                int rowIndex = value - 1;
                ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
                if (row.Active && row.RecipientIndex == recipientIndex && row.Key == key)
                    return rowIndex;
            }
            slot = (slot + 1) & _rowIndexMask;
        }
        return -1;
    }

    private void RetireRow(int rowIndex)
    {
        ref DirtyChunkRecipientRow row = ref _rows[rowIndex];
        if (!row.Active) return;
        if (row.PendingWasSent) InvalidateDelivery(row.PendingDeliveryId);
        DeactivatePending(rowIndex);
        RemoveRowIndex(rowIndex);
        row = default;
        _freeRows[_freeRowCount++] = rowIndex;
        _rowCount--;
    }

    private int FindFreeRow()
    {
        return _freeRowCount == 0 ? -1 : _freeRows[--_freeRowCount];
    }

    private int FindFreeDelivery()
    {
        for (int index = 0; index < _deliveries.Length; index++)
            if (!_deliveries[index].Active) return index;
        throw new InvalidOperationException("A delivery slot was not available after preflight.");
    }

    private int FindDelivery(ParticipantId participant, ulong deliveryId)
    {
        for (int index = 0; index < _deliveries.Length; index++)
            if (_deliveries[index].Active && _deliveries[index].Participant == participant &&
                _deliveries[index].Ticket.DeliveryId == deliveryId) return index;
        return -1;
    }

    private void InvalidateDelivery(ulong deliveryId)
    {
        if (deliveryId == 0) return;
        for (int index = 0; index < _deliveries.Length; index++)
            if (_deliveries[index].Active && _deliveries[index].Ticket.DeliveryId == deliveryId)
            {
                ClearDelivery(index);
                return;
            }
    }

    private void InvalidateDeliveriesForRecipient(int recipientIndex)
    {
        for (int index = 0; index < _deliveries.Length; index++)
            if (_deliveries[index].Active && _deliveries[index].RecipientIndex == recipientIndex)
                ClearDelivery(index);
    }

    private void InvalidateAllDeliveries()
    {
        for (int index = 0; index < _deliveries.Length; index++)
            if (_deliveries[index].Active) ClearDelivery(index);
        for (int index = 0; index < _rows.Length; index++)
            if (_rows[index].Active)
            {
                _rows[index].PendingWasSent = false;
                _rows[index].PendingDeliveryId = 0;
                ActivatePending(index);
            }
    }

    private void ClearDelivery(int index)
    {
        int recipientIndex = _deliveries[index].RecipientIndex;
        if (_deliveries[index].Active)
            _recipientOutstandingDeliveries[recipientIndex]--;
        _deliveries[index] = default;
        _deliveryCount--;
    }

    private bool ContainsDirty(SpatialChunkKey key)
    {
        int slot = Hash(key) & _dirtyIndexMask;
        for (int probe = 0; probe < _dirtyIndexKeys.Length; probe++)
        {
            byte state = _dirtyIndexStates[slot];
            if (state == 0) return false;
            if (state == 1 && _dirtyIndexKeys[slot] == key) return true;
            slot = (slot + 1) & _dirtyIndexMask;
        }
        return false;
    }

    private void EnqueueDirty(SpatialChunkKey key)
    {
        if (ContainsDirty(key)) { _dirtyChunksCoalescedBeforeDrain++; return; }
        if (_dirtyCount >= _dirtyKeys.Length) throw new InvalidOperationException("Dirty queue preflight diverged.");
        _dirtyKeys[_dirtyTail] = key;
        _dirtyTail = (_dirtyTail + 1) % _dirtyKeys.Length;
        _dirtyCount++;
        AddDirtyIndex(key);
        _dirtyChunksObserved++;
    }

    private void DequeueDirty(SpatialChunkKey key)
    {
        _dirtyKeys[_dirtyHead] = default;
        _dirtyHead = (_dirtyHead + 1) % _dirtyKeys.Length;
        _dirtyCount--;
        RemoveDirtyIndex(key);
    }

    private void AddDirtyIndex(SpatialChunkKey key)
    {
        int slot = Hash(key) & _dirtyIndexMask;
        int firstDeleted = -1;
        for (int probe = 0; probe < _dirtyIndexKeys.Length; probe++)
        {
            byte state = _dirtyIndexStates[slot];
            if (state == 0)
            {
                int target = firstDeleted >= 0 ? firstDeleted : slot;
                _dirtyIndexKeys[target] = key;
                _dirtyIndexStates[target] = 1;
                return;
            }
            if (state == 2 && firstDeleted < 0) firstDeleted = slot;
            slot = (slot + 1) & _dirtyIndexMask;
        }
        if (firstDeleted >= 0)
        {
            _dirtyIndexKeys[firstDeleted] = key;
            _dirtyIndexStates[firstDeleted] = 1;
            return;
        }
        throw new InvalidOperationException("Dirty-chunk key index capacity diverged from queue capacity.");
    }

    private void RemoveDirtyIndex(SpatialChunkKey key)
    {
        int slot = Hash(key) & _dirtyIndexMask;
        for (int probe = 0; probe < _dirtyIndexKeys.Length; probe++)
        {
            byte state = _dirtyIndexStates[slot];
            if (state == 0) return;
            if (state == 1 && _dirtyIndexKeys[slot] == key)
            {
                _dirtyIndexStates[slot] = 2;
                _dirtyIndexKeys[slot] = default;
                return;
            }
            slot = (slot + 1) & _dirtyIndexMask;
        }
    }

    private int FindRecipient(ParticipantId participant)
    {
        for (int index = 0; index < _recipients.Length; index++)
            if (_recipients[index].Active && _recipients[index].Participant == participant) return index;
        return -1;
    }

    private int FindFreeRecipient()
    {
        for (int index = 0; index < _recipients.Length; index++)
            if (!_recipients[index].Active) return index;
        return -1;
    }

    private SpatialRegionKey RegionFor(SpatialSpaceId space, in WorldPosition anchor) =>
        new(space, FloorDiv(anchor.GlobalXMillimeters, _limits.SpatialRegionEdgeMillimeters),
            FloorDiv(anchor.GlobalZMillimeters, _limits.SpatialRegionEdgeMillimeters));

    private int FindRegion(SpatialRegionKey key)
    {
        int index = Hash(key) & _regionMask;
        for (int probe = 0; probe < _regionStates.Length && _regionStates[index] != 0; probe++)
        {
            if (_regionStates[index] == 1 && _regionKeys[index] == key) return index;
            index = (index + 1) & _regionMask;
        }
        return -1;
    }

    private int FindOrCreateRegion(SpatialRegionKey key)
    {
        int index = Hash(key) & _regionMask;
        int tombstone = -1;
        for (int probe = 0; probe < _regionStates.Length && _regionStates[index] != 0; probe++)
        {
            if (_regionStates[index] == 1 && _regionKeys[index] == key) return index;
            if (_regionStates[index] == 2 && tombstone < 0) tombstone = index;
            index = (index + 1) & _regionMask;
        }
        if (_regionStates[index] != 0 && tombstone < 0)
            throw new InvalidOperationException("The recipient spatial index has no reusable slot.");
        if (tombstone >= 0) index = tombstone;
        _regionStates[index] = 1;
        _regionKeys[index] = key;
        _regionHeads[index] = -1;
        _regionCount++;
        return index;
    }

    private void AddRecipientToRegion(int recipientIndex, int region)
    {
        _recipients[recipientIndex].NextRegionRecipient = _regionHeads[region];
        _recipients[recipientIndex].RegionMapIndex = region;
        _regionHeads[region] = recipientIndex;
    }

    private void RemoveRecipientFromRegion(int recipientIndex, int region)
    {
        if (region < 0) return;
        int prior = -1;
        for (int current = _regionHeads[region]; current >= 0;
            current = _recipients[current].NextRegionRecipient)
        {
            if (current != recipientIndex) { prior = current; continue; }
            int next = _recipients[current].NextRegionRecipient;
            if (prior < 0) _regionHeads[region] = next;
            else _recipients[prior].NextRegionRecipient = next;
            _recipients[current].NextRegionRecipient = -1;
            if (_regionHeads[region] < 0)
            {
                _regionStates[region] = 2;
                _regionKeys[region] = default;
                _regionCount--;
            }
            return;
        }
    }

    private void MoveRecipientRegion(int recipientIndex, int oldRegion, int newRegion)
    {
        RemoveRecipientFromRegion(recipientIndex, oldRegion);
        AddRecipientToRegion(recipientIndex, newRegion);
    }

    private static int FloorDiv(long value, int divisor)
    {
        long quotient = value / divisor;
        long remainder = value % divisor;
        if (remainder < 0) quotient--;
        return checked((int)quotient);
    }

    private static void FloorDiv(long value, int divisor, out long quotient)
    {
        quotient = value / divisor;
        if (value % divisor < 0) quotient--;
    }

    private static int Hash(in SpatialRegionKey key)
    {
        unchecked
        {
            uint hash = 2_166_136_261u;
            hash = (hash ^ (uint)key.Space.Value) * 16_777_619u;
            hash = (hash ^ (uint)key.X) * 16_777_619u;
            hash = (hash ^ (uint)key.Z) * 16_777_619u;
            return (int)hash & int.MaxValue;
        }
    }

    private static int Hash(in SpatialChunkKey key)
    {
        unchecked
        {
            uint hash = 2_166_136_261u;
            hash = (hash ^ (uint)key.Space.Value) * 16_777_619u;
            hash = (hash ^ (uint)key.Coord.X) * 16_777_619u;
            hash = (hash ^ (uint)key.Coord.Z) * 16_777_619u;
            return (int)hash & int.MaxValue;
        }
    }

    private static int RowHash(int recipientIndex, in SpatialChunkKey key)
    {
        unchecked
        {
            uint hash = (uint)Hash(key);
            hash = (hash ^ (uint)recipientIndex) * 16_777_619u;
            return (int)hash & int.MaxValue;
        }
    }

    private static int MapCapacity(int maximum)
    {
        int capacity = 2;
        while (capacity < maximum * 2) capacity = checked(capacity << 1);
        return capacity;
    }

    private T CapacityFailure<T>(T status)
    {
        _capacityFailures++;
        return status;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("DirtyChunkSpatialFanout is owner-thread-bound.");
    }

    private OfflineDirectChunkRecipient CreateRecipient() => new(
        new OfflineDirectChunkRecipientLimits(
            _subscriptionsPerRecipient,
            _limits.MaximumSnapshotRecords,
            _limits.MaximumDeltaRecords),
        _interestProfile, _cadence);

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct RecipientSlot
    {
        internal ParticipantId Participant;
        internal SpatialSpaceId Space;
        internal WorldPosition Anchor;
        internal OfflineDirectChunkRecipient Recipient;
        internal int RegionMapIndex;
        internal int NextRegionRecipient;
        internal bool Active;
    }

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct DirtyChunkRecipientRow
    {
        internal int RecipientIndex;
        internal SpatialChunkKey Key;
        internal ChunkInterestTier Tier;
        internal bool Active;
        internal bool Pending;
        internal bool PendingWasSent;
        internal ulong PendingDeliveryId;
        internal int PendingPrevious;
        internal int PendingNext;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DeliverySlot
    {
        internal ParticipantId Participant;
        internal OfflineDirectChunkDeliveryTicket Ticket;
        internal int RecipientIndex;
        internal int RowIndex;
        internal ulong SentCadenceStep;
        internal bool Active;
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct SpatialRegionKey(SpatialSpaceId Space, int X, int Z);
}
