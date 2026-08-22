using System.Runtime.InteropServices;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

/// <summary>Fixed N1.0 recipient capacities. These are independent of publication-store capacity.</summary>
public readonly record struct OfflineDirectChunkRecipientLimits(
    int MaximumSubscriptions,
    int MaximumSnapshotRecords,
    int MaximumDeltaRecords)
{
    public bool IsValid =>
        MaximumSubscriptions > 0 &&
        MaximumSnapshotRecords > 0 &&
        MaximumDeltaRecords > 0;
}

/// <summary>Recipient-owned public-chunk query cadence. Tier changes do not rewrite chunk state.</summary>
public readonly record struct OfflineDirectChunkCadence(
    uint ImmediateSteps,
    uint NearSteps,
    uint FarSteps)
{
    public static OfflineDirectChunkCadence EvidenceProfile { get; } = new(1, 2, 4);

    public bool IsValid => ImmediateSteps > 0 && NearSteps >= ImmediateSteps && FarSteps >= NearSteps;

    internal uint Period(ChunkInterestTier tier) => tier switch
    {
        ChunkInterestTier.Immediate => ImmediateSteps,
        ChunkInterestTier.Near => NearSteps,
        ChunkInterestTier.Far => FarSteps,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}

public enum OfflineDirectChunkReconcileStatus : byte
{
    Reconciled = 1,
    InvalidInterest = 2,
    SubscriptionCapacityExceeded = 3,
    ChangeDestinationTooSmall = 4,
    ReadPending = 5,
}

public enum OfflineDirectChunkSubscriptionChangeKind : byte
{
    Entered = 1,
    Left = 2,
    TierChanged = 3,
}

/// <summary>One complete recipient topology change. Leaves are emitted before enters.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly record struct OfflineDirectChunkSubscriptionChange(
    OfflineDirectChunkSubscriptionChangeKind Kind,
    SpatialChunkKey Key,
    ChunkInterestTier PreviousTier,
    ChunkInterestTier CurrentTier);

/// <summary>One recipient-owned chunk cursor. It contains no client or transport slot identity.</summary>
public readonly record struct OfflineDirectChunkSubscription(
    ChunkInterestEntry Interest,
    ChunkPublicationCursor Cursor,
    bool HasKnowledge,
    uint CadencePhase);

public readonly record struct OfflineDirectChunkReconcileResult(
    OfflineDirectChunkReconcileStatus Status,
    PublicationEpoch SourceEpoch,
    bool SourceReset,
    int SubscriptionCount,
    int RequiredSubscriptions,
    int ChangeCount,
    int RequiredChanges);

public enum OfflineDirectChunkReadStatus : byte
{
    Snapshot = 1,
    Delta = 2,
    NoChanges = 3,
    ChunkAbsent = 4,
    NotDue = 5,
    InvalidSubscription = 6,
    SourceRequiresReconcile = 7,
    ReadPending = 8,
    SnapshotCapacityExceeded = 9,
    DeltaCapacityExceeded = 10,
    FutureCursor = 11,
}

public enum OfflineDirectChunkAppliedProofStatus : byte
{
    Applied = 1,
    Duplicate = 2,
    Stale = 3,
    Invalid = 4,
}

/// <summary>
/// Trusted server-side record of one encoded remote delivery. The constructor is internal so
/// untrusted proof bytes cannot manufacture publication knowledge without a retained offer.
/// </summary>
public readonly struct OfflineDirectChunkDeliveryTicket
{
    internal OfflineDirectChunkDeliveryTicket(
        ulong deliveryId,
        PublicationEpoch sourceEpoch,
        SpatialChunkKey key,
        OfflineDirectChunkReadStatus status,
        ChunkPublicationCursor cursor)
    {
        DeliveryId = deliveryId;
        SourceEpoch = sourceEpoch;
        Key = key;
        Status = status;
        Cursor = cursor;
    }

    public ulong DeliveryId { get; }
    public PublicationEpoch SourceEpoch { get; }
    public SpatialChunkKey Key { get; }
    public OfflineDirectChunkReadStatus Status { get; }
    public ChunkPublicationCursor Cursor { get; }
    public bool IsValid => DeliveryId != 0 && SourceEpoch.IsValid && Key.Space.IsValid &&
        (Status == OfflineDirectChunkReadStatus.ChunkAbsent && !Cursor.IsValid ||
         Status is OfflineDirectChunkReadStatus.Snapshot or OfflineDirectChunkReadStatus.Delta &&
         Cursor.IsValid && Cursor.Epoch == SourceEpoch && Cursor.Key == Key);
}

/// <summary>Borrowed result of one packet-free publication read; valid until commit/cancel.</summary>
public readonly ref struct OfflineDirectChunkRead
{
    internal OfflineDirectChunkRead(
        OfflineDirectChunkReadStatus status,
        PublicationEpoch sourceEpoch,
        ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> snapshot,
        ReadOnlySpan<WorldPublicDeltaRecord> deltas)
    {
        Status = status;
        SourceEpoch = sourceEpoch;
        Batch = batch;
        Snapshot = snapshot;
        Deltas = deltas;
    }

    public OfflineDirectChunkReadStatus Status { get; }
    public PublicationEpoch SourceEpoch { get; }
    public ChunkPublicationBatch Batch { get; }
    public ReadOnlySpan<WorldPublicRecord> Snapshot { get; }
    public ReadOnlySpan<WorldPublicDeltaRecord> Deltas { get; }
}

public readonly record struct OfflineDirectChunkRecipientSnapshot(
    PublicationEpoch SourceEpoch,
    int SubscriptionCount,
    ulong Reconciliations,
    ulong EnteredSubscriptions,
    ulong LeftSubscriptions,
    ulong TierChanges,
    ulong SnapshotReads,
    ulong ChunkAbsentReads,
    ulong DeltaReads,
    ulong NoChangeReads,
    ulong SnapshotFallbacks,
    ulong CadenceDeferrals,
    long ReservedBytes);

/// <summary>
/// N1.0 packet-free recipient subscription/cursor layer. Game interest and applied publication
/// cursors live here; authoritative history remains in <see cref="ChunkPublicationStore"/>, and
/// client semantic/cache identity remains outside this server/game-owned object.
/// </summary>
public sealed class OfflineDirectChunkRecipient
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly OfflineDirectChunkRecipientLimits _limits;
    private readonly ChunkInterestProfile _interestProfile;
    private readonly OfflineDirectChunkCadence _cadence;
    private OfflineDirectChunkSubscription[] _subscriptions;
    private OfflineDirectChunkSubscription[] _nextSubscriptions;
    private readonly ChunkInterestEntry[] _previousInterest;
    private readonly ChunkInterestEntry[] _desiredInterest;
    private readonly WorldPublicRecord[] _snapshotScratch;
    private readonly WorldPublicDeltaRecord[] _deltaScratch;
    private int _subscriptionCount;
    private PublicationEpoch _sourceEpoch;
    private int _pendingIndex = -1;
    private ChunkPublicationBatch _pendingBatch;
    private OfflineDirectChunkReadStatus _pendingStatus;
    private ulong _reconciliations;
    private ulong _enteredSubscriptions;
    private ulong _leftSubscriptions;
    private ulong _tierChanges;
    private ulong _snapshotReads;
    private ulong _chunkAbsentReads;
    private ulong _deltaReads;
    private ulong _noChangeReads;
    private ulong _snapshotFallbacks;
    private ulong _cadenceDeferrals;

    public OfflineDirectChunkRecipient(
        OfflineDirectChunkRecipientLimits limits,
        ChunkInterestProfile interestProfile,
        OfflineDirectChunkCadence cadence)
    {
        if (!limits.IsValid) throw new ArgumentOutOfRangeException(nameof(limits));
        if (!interestProfile.IsValid) throw new ArgumentOutOfRangeException(nameof(interestProfile));
        if (!cadence.IsValid) throw new ArgumentOutOfRangeException(nameof(cadence));
        _limits = limits;
        _interestProfile = interestProfile;
        _cadence = cadence;
        _subscriptions = new OfflineDirectChunkSubscription[limits.MaximumSubscriptions];
        _nextSubscriptions = new OfflineDirectChunkSubscription[limits.MaximumSubscriptions];
        _previousInterest = new ChunkInterestEntry[limits.MaximumSubscriptions];
        _desiredInterest = new ChunkInterestEntry[limits.MaximumSubscriptions];
        _snapshotScratch = new WorldPublicRecord[limits.MaximumSnapshotRecords];
        _deltaScratch = new WorldPublicDeltaRecord[limits.MaximumDeltaRecords];
    }

    public OfflineDirectChunkRecipientLimits Limits => _limits;

    /// <summary>Borrowed rows; valid only until reconcile or a committed publication read.</summary>
    public ReadOnlySpan<OfflineDirectChunkSubscription> Subscriptions
    {
        get
        {
            EnsureOwner();
            return _subscriptions.AsSpan(0, _subscriptionCount);
        }
    }

    /// <summary>
    /// Reconciles one complete game-owned interest set from an authority-resolved anchor. This
    /// method is not an untrusted client-position admission route. Failure publishes neither
    /// topology nor source-epoch changes. Successful change rows always place leaves before enters
    /// so a bounded client can release old residency before requesting replacements.
    /// </summary>
    public OfflineDirectChunkReconcileStatus Reconcile(
        ChunkPublicationStore source,
        SpatialSpaceId space,
        in WorldPosition anchor,
        Span<OfflineDirectChunkSubscriptionChange> changes,
        out OfflineDirectChunkReconcileResult result)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (_pendingIndex >= 0)
        {
            result = Result(OfflineDirectChunkReconcileStatus.ReadPending, false, 0, 0, 0);
            return result.Status;
        }

        for (int index = 0; index < _subscriptionCount; index++)
            _previousInterest[index] = _subscriptions[index].Interest;
        ChunkInterestQueryStatus query = ChunkInterestQuery.TryQuery(
            space,
            anchor,
            _interestProfile,
            _previousInterest.AsSpan(0, _subscriptionCount),
            _desiredInterest,
            out int desiredCount,
            out int requiredSubscriptions);
        if (query == ChunkInterestQueryStatus.InvalidInput)
        {
            result = Result(OfflineDirectChunkReconcileStatus.InvalidInterest, false,
                requiredSubscriptions, 0, 0);
            return result.Status;
        }
        if (query == ChunkInterestQueryStatus.DestinationTooSmall)
        {
            result = Result(OfflineDirectChunkReconcileStatus.SubscriptionCapacityExceeded, false,
                requiredSubscriptions, 0, 0);
            return result.Status;
        }

        int requiredChanges = CountChanges(desiredCount);
        if (changes.Length < requiredChanges)
        {
            result = Result(OfflineDirectChunkReconcileStatus.ChangeDestinationTooSmall, false,
                requiredSubscriptions, 0, requiredChanges);
            return result.Status;
        }

        bool sourceReset = _sourceEpoch != source.Epoch;
        BuildNextSubscriptions(desiredCount, sourceReset);
        int changeCount = WriteChanges(changes, desiredCount);
        (_subscriptions, _nextSubscriptions) = (_nextSubscriptions, _subscriptions);
        _subscriptionCount = desiredCount;
        _sourceEpoch = source.Epoch;
        _reconciliations++;
        for (int index = 0; index < changeCount; index++)
        {
            switch (changes[index].Kind)
            {
                case OfflineDirectChunkSubscriptionChangeKind.Entered:
                    _enteredSubscriptions++;
                    break;
                case OfflineDirectChunkSubscriptionChangeKind.Left:
                    _leftSubscriptions++;
                    break;
                case OfflineDirectChunkSubscriptionChangeKind.TierChanged:
                    _tierChanges++;
                    break;
            }
        }
        result = Result(OfflineDirectChunkReconcileStatus.Reconciled, sourceReset,
            requiredSubscriptions, changeCount, requiredChanges);
        return result.Status;
    }

    /// <summary>
    /// Begins one due snapshot/delta query. The subscription cursor does not advance until
    /// <see cref="CommitRead"/> is called after the consumer has atomically applied the result.
    /// </summary>
    public OfflineDirectChunkReadStatus TryBeginRead(
        ChunkPublicationStore source,
        int subscriptionIndex,
        ulong cadenceStep,
        out OfflineDirectChunkRead read)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (_pendingIndex >= 0)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.ReadPending, source.Epoch);
            return read.Status;
        }
        if (source.Epoch != _sourceEpoch)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.SourceRequiresReconcile, source.Epoch);
            return read.Status;
        }
        if ((uint)subscriptionIndex >= (uint)_subscriptionCount)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.InvalidSubscription, source.Epoch);
            return read.Status;
        }

        OfflineDirectChunkSubscription subscription = _subscriptions[subscriptionIndex];
        uint period = _cadence.Period(subscription.Interest.Tier);
        if (subscription.HasKnowledge && cadenceStep % period != subscription.CadencePhase)
        {
            _cadenceDeferrals++;
            read = EmptyRead(OfflineDirectChunkReadStatus.NotDue, source.Epoch);
            return read.Status;
        }

        ChunkPublicationReadStatus publicationStatus;
        ChunkPublicationBatch batch;
        if (subscription.Cursor.IsValid)
        {
            publicationStatus = source.TryCopyDelta(
                subscription.Interest.Key, subscription.Cursor, _deltaScratch, out batch);
            switch (publicationStatus)
            {
                case ChunkPublicationReadStatus.Delta:
                    return PublishPending(subscriptionIndex, OfflineDirectChunkReadStatus.Delta,
                        source.Epoch, batch, default, _deltaScratch.AsSpan(0, batch.Count), out read);
                case ChunkPublicationReadStatus.NoChanges:
                    return PublishPending(subscriptionIndex, OfflineDirectChunkReadStatus.NoChanges,
                        source.Epoch, batch, default, default, out read);
                case ChunkPublicationReadStatus.DestinationTooSmall:
                    read = EmptyRead(OfflineDirectChunkReadStatus.DeltaCapacityExceeded, source.Epoch, batch);
                    return read.Status;
                case ChunkPublicationReadStatus.FutureCursor:
                    read = EmptyRead(OfflineDirectChunkReadStatus.FutureCursor, source.Epoch, batch);
                    return read.Status;
                case ChunkPublicationReadStatus.SnapshotRequired:
                    _snapshotFallbacks++;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected publication delta status: {publicationStatus}.");
            }
        }

        publicationStatus = source.TryCopySnapshot(
            subscription.Interest.Key, _snapshotScratch, out batch);
        if (publicationStatus == ChunkPublicationReadStatus.Snapshot)
            return PublishPending(subscriptionIndex, OfflineDirectChunkReadStatus.Snapshot,
                source.Epoch, batch, _snapshotScratch.AsSpan(0, batch.Count), default, out read);
        if (publicationStatus == ChunkPublicationReadStatus.ChunkAbsent)
            return PublishPending(subscriptionIndex, OfflineDirectChunkReadStatus.ChunkAbsent,
                source.Epoch, batch, default, default, out read);
        if (publicationStatus == ChunkPublicationReadStatus.DestinationTooSmall)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.SnapshotCapacityExceeded, source.Epoch, batch);
            return read.Status;
        }
        throw new InvalidOperationException($"Unexpected publication snapshot status: {publicationStatus}.");
    }

    /// <summary>
    /// Begins an explicit complete-snapshot repair for one admitted subscription without consulting
    /// its current cursor or cadence. This is the exceptional N1.3 path for an MTU-oversize delta,
    /// client repair, or another caller-established recovery requirement.
    /// </summary>
    public OfflineDirectChunkReadStatus TryBeginSnapshotRead(
        ChunkPublicationStore source,
        int subscriptionIndex,
        out OfflineDirectChunkRead read)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (_pendingIndex >= 0)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.ReadPending, source.Epoch);
            return read.Status;
        }
        if (source.Epoch != _sourceEpoch)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.SourceRequiresReconcile, source.Epoch);
            return read.Status;
        }
        if ((uint)subscriptionIndex >= (uint)_subscriptionCount)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.InvalidSubscription, source.Epoch);
            return read.Status;
        }

        ChunkPublicationReadStatus status = source.TryCopySnapshot(
            _subscriptions[subscriptionIndex].Interest.Key, _snapshotScratch, out var batch);
        if (status == ChunkPublicationReadStatus.Snapshot)
        {
            _snapshotFallbacks++;
            return PublishPending(subscriptionIndex, OfflineDirectChunkReadStatus.Snapshot,
                source.Epoch, batch, _snapshotScratch.AsSpan(0, batch.Count), default, out read);
        }
        if (status == ChunkPublicationReadStatus.ChunkAbsent)
            return PublishPending(subscriptionIndex, OfflineDirectChunkReadStatus.ChunkAbsent,
                source.Epoch, batch, default, default, out read);
        if (status == ChunkPublicationReadStatus.DestinationTooSmall)
        {
            read = EmptyRead(OfflineDirectChunkReadStatus.SnapshotCapacityExceeded, source.Epoch, batch);
            return read.Status;
        }
        throw new InvalidOperationException($"Unexpected forced publication snapshot status: {status}.");
    }

    /// <summary>Commits the outstanding read only after its semantic consumer succeeded.</summary>
    public void CommitRead()
    {
        EnsureOwner();
        if (_pendingIndex < 0) throw new InvalidOperationException("No publication read is pending.");
        ref OfflineDirectChunkSubscription subscription = ref _subscriptions[_pendingIndex];
        switch (_pendingStatus)
        {
            case OfflineDirectChunkReadStatus.Snapshot:
                subscription = subscription with { Cursor = _pendingBatch.Cursor, HasKnowledge = true };
                _snapshotReads++;
                break;
            case OfflineDirectChunkReadStatus.Delta:
                subscription = subscription with { Cursor = _pendingBatch.Cursor, HasKnowledge = true };
                _deltaReads++;
                break;
            case OfflineDirectChunkReadStatus.NoChanges:
                subscription = subscription with { Cursor = _pendingBatch.Cursor, HasKnowledge = true };
                _noChangeReads++;
                break;
            case OfflineDirectChunkReadStatus.ChunkAbsent:
                subscription = subscription with { Cursor = default, HasKnowledge = true };
                _snapshotReads++;
                _chunkAbsentReads++;
                break;
            default:
                throw new InvalidOperationException("The pending publication status cannot be committed.");
        }
        ClearPending();
    }

    /// <summary>Cancels one read without advancing recipient knowledge.</summary>
    public void CancelRead()
    {
        EnsureOwner();
        if (_pendingIndex < 0) throw new InvalidOperationException("No publication read is pending.");
        ClearPending();
    }

    /// <summary>
    /// Releases borrowed read scratch after a remote message is encoded without advancing applied
    /// knowledge. The caller retains the returned trusted ticket and may commit it only after an
    /// authenticated matching application proof. Queueing or sending bytes is never sufficient.
    /// </summary>
    public OfflineDirectChunkDeliveryTicket ReleaseReadForRemote(ulong deliveryId)
    {
        EnsureOwner();
        ArgumentOutOfRangeException.ThrowIfZero(deliveryId);
        if (_pendingIndex < 0) throw new InvalidOperationException("No publication read is pending.");
        if (_pendingStatus is not (OfflineDirectChunkReadStatus.Snapshot or
            OfflineDirectChunkReadStatus.Delta or OfflineDirectChunkReadStatus.ChunkAbsent))
            throw new InvalidOperationException("Only semantic payload reads may become remote deliveries.");
        SpatialChunkKey key = _subscriptions[_pendingIndex].Interest.Key;
        var ticket = new OfflineDirectChunkDeliveryTicket(
            deliveryId, _sourceEpoch, key, _pendingStatus, _pendingBatch.Cursor);
        ClearPending();
        return ticket;
    }

    /// <summary>
    /// Advances recipient knowledge from one trusted retained delivery ticket after the remote
    /// application proof has passed session, epoch, key, delivery-ID, kind, revision, and status
    /// validation. Duplicate/stale proof is harmless and never regresses a newer cursor.
    /// </summary>
    public OfflineDirectChunkAppliedProofStatus CommitAppliedProof(
        in OfflineDirectChunkDeliveryTicket ticket)
    {
        EnsureOwner();
        if (_pendingIndex >= 0 || !ticket.IsValid || ticket.SourceEpoch != _sourceEpoch)
            return OfflineDirectChunkAppliedProofStatus.Invalid;
        int subscriptionIndex = FindSubscription(ticket.Key);
        if (subscriptionIndex < 0) return OfflineDirectChunkAppliedProofStatus.Stale;
        ref OfflineDirectChunkSubscription subscription = ref _subscriptions[subscriptionIndex];

        if (ticket.Status == OfflineDirectChunkReadStatus.ChunkAbsent)
        {
            if (subscription.Cursor.IsValid) return OfflineDirectChunkAppliedProofStatus.Stale;
            if (subscription.HasKnowledge) return OfflineDirectChunkAppliedProofStatus.Duplicate;
            subscription = subscription with { Cursor = default, HasKnowledge = true };
            _snapshotReads++;
            _chunkAbsentReads++;
            return OfflineDirectChunkAppliedProofStatus.Applied;
        }

        if (!ticket.Cursor.IsValid) return OfflineDirectChunkAppliedProofStatus.Invalid;
        if (subscription.Cursor.IsValid)
        {
            int comparison = ticket.Cursor.Revision.CompareTo(subscription.Cursor.Revision);
            if (comparison < 0) return OfflineDirectChunkAppliedProofStatus.Stale;
            if (comparison == 0) return OfflineDirectChunkAppliedProofStatus.Duplicate;
        }
        subscription = subscription with { Cursor = ticket.Cursor, HasKnowledge = true };
        if (ticket.Status == OfflineDirectChunkReadStatus.Snapshot) _snapshotReads++;
        else _deltaReads++;
        return OfflineDirectChunkAppliedProofStatus.Applied;
    }

    public OfflineDirectChunkRecipientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new OfflineDirectChunkRecipientSnapshot(
            _sourceEpoch,
            _subscriptionCount,
            _reconciliations,
            _enteredSubscriptions,
            _leftSubscriptions,
            _tierChanges,
            _snapshotReads,
            _chunkAbsentReads,
            _deltaReads,
            _noChangeReads,
            _snapshotFallbacks,
            _cadenceDeferrals,
            CalculateReservedBytes());
    }

    private OfflineDirectChunkReadStatus PublishPending(
        int subscriptionIndex,
        OfflineDirectChunkReadStatus status,
        PublicationEpoch sourceEpoch,
        ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> snapshot,
        ReadOnlySpan<WorldPublicDeltaRecord> deltas,
        out OfflineDirectChunkRead read)
    {
        _pendingIndex = subscriptionIndex;
        _pendingStatus = status;
        _pendingBatch = batch;
        read = new OfflineDirectChunkRead(status, sourceEpoch, batch, snapshot, deltas);
        return status;
    }

    private static OfflineDirectChunkRead EmptyRead(
        OfflineDirectChunkReadStatus status,
        PublicationEpoch sourceEpoch,
        ChunkPublicationBatch batch = default) =>
        new(status, sourceEpoch, batch, default, default);

    private void ClearPending()
    {
        _pendingIndex = -1;
        _pendingStatus = default;
        _pendingBatch = default;
    }

    private int CountChanges(int desiredCount)
    {
        int previous = 0;
        int desired = 0;
        int count = 0;
        while (previous < _subscriptionCount || desired < desiredCount)
        {
            if (previous == _subscriptionCount)
            {
                count += desiredCount - desired;
                break;
            }
            if (desired == desiredCount)
            {
                count += _subscriptionCount - previous;
                break;
            }
            int comparison = _subscriptions[previous].Interest.Key.CompareTo(_desiredInterest[desired].Key);
            if (comparison < 0)
            {
                previous++;
                count++;
            }
            else if (comparison > 0)
            {
                desired++;
                count++;
            }
            else
            {
                if (_subscriptions[previous].Interest.Tier != _desiredInterest[desired].Tier) count++;
                previous++;
                desired++;
            }
        }
        return count;
    }

    private int FindSubscription(SpatialChunkKey key)
    {
        int low = 0;
        int high = _subscriptionCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = _subscriptions[middle].Interest.Key.CompareTo(key);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    private void BuildNextSubscriptions(int desiredCount, bool sourceReset)
    {
        int previous = 0;
        for (int desired = 0; desired < desiredCount; desired++)
        {
            ChunkInterestEntry interest = _desiredInterest[desired];
            while (previous < _subscriptionCount &&
                _subscriptions[previous].Interest.Key.CompareTo(interest.Key) < 0)
                previous++;
            if (previous < _subscriptionCount && _subscriptions[previous].Interest.Key == interest.Key)
            {
                OfflineDirectChunkSubscription retained = _subscriptions[previous];
                _nextSubscriptions[desired] = retained with
                {
                    Interest = interest,
                    Cursor = sourceReset ? default : retained.Cursor,
                    HasKnowledge = !sourceReset && retained.HasKnowledge,
                    CadencePhase = StablePhase(interest.Key, _cadence.Period(interest.Tier)),
                };
            }
            else
            {
                _nextSubscriptions[desired] = new OfflineDirectChunkSubscription(
                    interest,
                    default,
                    false,
                    StablePhase(interest.Key, _cadence.Period(interest.Tier)));
            }
        }
    }

    private int WriteChanges(Span<OfflineDirectChunkSubscriptionChange> changes, int desiredCount)
    {
        int written = 0;
        int previous = 0;
        int desired = 0;
        while (previous < _subscriptionCount)
        {
            SpatialChunkKey previousKey = _subscriptions[previous].Interest.Key;
            while (desired < desiredCount && _desiredInterest[desired].Key.CompareTo(previousKey) < 0)
                desired++;
            if (desired == desiredCount || _desiredInterest[desired].Key != previousKey)
            {
                ChunkInterestTier tier = _subscriptions[previous].Interest.Tier;
                changes[written++] = new OfflineDirectChunkSubscriptionChange(
                    OfflineDirectChunkSubscriptionChangeKind.Left, previousKey, tier, default);
            }
            previous++;
        }

        previous = 0;
        for (desired = 0; desired < desiredCount; desired++)
        {
            ChunkInterestEntry current = _desiredInterest[desired];
            while (previous < _subscriptionCount &&
                _subscriptions[previous].Interest.Key.CompareTo(current.Key) < 0)
                previous++;
            if (previous == _subscriptionCount || _subscriptions[previous].Interest.Key != current.Key)
            {
                changes[written++] = new OfflineDirectChunkSubscriptionChange(
                    OfflineDirectChunkSubscriptionChangeKind.Entered,
                    current.Key,
                    default,
                    current.Tier);
            }
            else if (_subscriptions[previous].Interest.Tier != current.Tier)
            {
                changes[written++] = new OfflineDirectChunkSubscriptionChange(
                    OfflineDirectChunkSubscriptionChangeKind.TierChanged,
                    current.Key,
                    _subscriptions[previous].Interest.Tier,
                    current.Tier);
            }
        }
        return written;
    }

    private OfflineDirectChunkReconcileResult Result(
        OfflineDirectChunkReconcileStatus status,
        bool sourceReset,
        int requiredSubscriptions,
        int changeCount,
        int requiredChanges) => new(
            status,
            _sourceEpoch,
            sourceReset,
            _subscriptionCount,
            requiredSubscriptions,
            changeCount,
            requiredChanges);

    private static uint StablePhase(SpatialChunkKey key, uint period)
    {
        if (period == 1) return 0;
        unchecked
        {
            uint hash = 2_166_136_261u;
            hash = (hash ^ (uint)key.Space.Value) * 16_777_619u;
            hash = (hash ^ (uint)key.Coord.X) * 16_777_619u;
            hash = (hash ^ (uint)key.Coord.Z) * 16_777_619u;
            return hash % period;
        }
    }

    private long CalculateReservedBytes() =>
        checked(
            (long)_subscriptions.Length * Marshal.SizeOf<OfflineDirectChunkSubscription>() +
            (long)_nextSubscriptions.Length * Marshal.SizeOf<OfflineDirectChunkSubscription>() +
            (long)_previousInterest.Length * Marshal.SizeOf<ChunkInterestEntry>() +
            (long)_desiredInterest.Length * Marshal.SizeOf<ChunkInterestEntry>() +
            (long)_snapshotScratch.Length * Marshal.SizeOf<WorldPublicRecord>() +
            (long)_deltaScratch.Length * Marshal.SizeOf<WorldPublicDeltaRecord>());

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The OfflineDirect chunk recipient is owner-thread bound.");
    }
}
