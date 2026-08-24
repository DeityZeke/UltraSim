using System.Diagnostics;
using System.Runtime.CompilerServices;
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

/// <summary>
/// Borrowed read view over compact recipient-owned subscriptions. Indexing reconstructs the
/// self-validating public cursor from the recipient-wide epoch, retained key, and applied revision.
/// The view is valid only until the recipient next reconciles or commits publication knowledge.
/// </summary>
public readonly ref struct OfflineDirectChunkSubscriptionView
{
    private readonly OfflineDirectChunkRecipient? _owner;

    internal OfflineDirectChunkSubscriptionView(OfflineDirectChunkRecipient owner) =>
        _owner = owner;

    public int Length => _owner?.SubscriptionCountForView ?? 0;

    public OfflineDirectChunkSubscription this[int index]
    {
        get
        {
            if (_owner is null) throw new IndexOutOfRangeException();
            return _owner.GetSubscriptionForView(index);
        }
    }
}

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
    ulong FullInterestQueries,
    ulong StableInterestFastPaths,
    int StableInterestMovementBudgetMillimeters,
    long ReservedBytes);

/// <summary>Cold fixed-capacity payload attribution; excludes CLR object/array headers.</summary>
public readonly record struct OfflineDirectChunkRecipientStorageSnapshot(
    int RetainedSubscriptionBytes,
    int PublicSubscriptionViewBytes,
    long CurrentSubscriptionsBytes,
    long NextSubscriptionsBytes,
    long PreviousInterestBytes,
    long DesiredInterestBytes,
    long SnapshotScratchBytes,
    long DeltaScratchBytes,
    long TotalReservedBytes);

/// <summary>
/// Opt-in crossing-path attribution. Timings are raw <see cref="Stopwatch"/> ticks and remain
/// dormant until explicitly enabled; they do not participate in subscription semantics.
/// </summary>
public readonly record struct OfflineDirectChunkReconcileAttribution(
    bool Enabled,
    ulong StableFastPaths,
    ulong ExactQueries,
    ulong PreviousInterestCopyTicks,
    ulong ExactQueryTicks,
    ulong ChangeCountTicks,
    ulong NextBankBuildTicks,
    ulong ChangeEmissionTicks,
    ulong PublicationTicks);

/// <summary>
/// N1.0 packet-free recipient subscription/cursor layer. Game interest and applied publication
/// cursors live here; authoritative history remains in <see cref="ChunkPublicationStore"/>, and
/// client semantic/cache identity remains outside this server/game-owned object.
/// </summary>
public sealed class OfflineDirectChunkRecipient
{
    public static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.ChunkPublication;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly OfflineDirectChunkRecipientLimits _limits;
    private readonly ChunkInterestProfile _interestProfile;
    private readonly OfflineDirectChunkCadence _cadence;
    private OfflineDirectChunkSubscriptionState[] _subscriptions;
    private OfflineDirectChunkSubscriptionState[] _nextSubscriptions;
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
    private WorldPosition _lastInterestAnchor;
    private SpatialSpaceId _lastInterestSpace;
    private int _stableInterestMovementBudgetMillimeters;
    private bool _hasLastInterestAnchor;
    private ulong _fullInterestQueries;
    private ulong _stableInterestFastPaths;
    private bool _reconcileAttributionEnabled;
    private ulong _attributedStableFastPaths;
    private ulong _attributedExactQueries;
    private ulong _previousInterestCopyTicks;
    private ulong _exactQueryTicks;
    private ulong _changeCountTicks;
    private ulong _nextBankBuildTicks;
    private ulong _changeEmissionTicks;
    private ulong _reconcilePublicationTicks;

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
        _subscriptions = new OfflineDirectChunkSubscriptionState[limits.MaximumSubscriptions];
        _nextSubscriptions = new OfflineDirectChunkSubscriptionState[limits.MaximumSubscriptions];
        _previousInterest = new ChunkInterestEntry[limits.MaximumSubscriptions];
        _desiredInterest = new ChunkInterestEntry[limits.MaximumSubscriptions];
        _snapshotScratch = new WorldPublicRecord[limits.MaximumSnapshotRecords];
        _deltaScratch = new WorldPublicDeltaRecord[limits.MaximumDeltaRecords];
    }

    public OfflineDirectChunkRecipientLimits Limits => _limits;

    internal uint GetCadencePeriod(ChunkInterestTier tier) => _cadence.Period(tier);

    internal bool IsCadenceDue(SpatialChunkKey key, ChunkInterestTier tier, ulong cadenceStep)
    {
        uint period = _cadence.Period(tier);
        return cadenceStep % period == StablePhase(key, period);
    }

    /// <summary>Borrowed indexed view; valid only until reconcile or committed knowledge.</summary>
    public OfflineDirectChunkSubscriptionView Subscriptions
    {
        get
        {
            EnsureOwner();
            return new OfflineDirectChunkSubscriptionView(this);
        }
    }

    internal int SubscriptionCountForView
    {
        get
        {
            EnsureOwner();
            return _subscriptionCount;
        }
    }

    internal OfflineDirectChunkSubscription GetSubscriptionForView(int index)
    {
        EnsureOwner();
        if ((uint)index >= (uint)_subscriptionCount) throw new IndexOutOfRangeException();
        ref readonly OfflineDirectChunkSubscriptionState subscription = ref _subscriptions[index];
        ChunkPublicationCursor cursor = subscription.AppliedRevision.IsValid
            ? new ChunkPublicationCursor(_sourceEpoch, subscription.Key, subscription.AppliedRevision)
            : default;
        return new OfflineDirectChunkSubscription(
            new ChunkInterestEntry(subscription.Key, subscription.Tier),
            cursor,
            subscription.HasKnowledge,
            subscription.CadencePhase);
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

        if (_sourceEpoch == source.Epoch && _hasLastInterestAnchor &&
            _lastInterestSpace == space &&
            IsWithinStableInterestBudget(anchor))
        {
            if (_reconcileAttributionEnabled) SaturatingIncrement(ref _attributedStableFastPaths);
            _reconciliations++;
            _stableInterestFastPaths++;
            result = Result(
                OfflineDirectChunkReconcileStatus.Reconciled,
                false,
                _subscriptionCount,
                0,
                0);
            return result.Status;
        }

        long phaseStarted = AttributionTimestamp();
        for (int index = 0; index < _subscriptionCount; index++)
            _previousInterest[index] = _subscriptions[index].Interest;
        AttributeElapsed(ref _previousInterestCopyTicks, phaseStarted);
        phaseStarted = AttributionTimestamp();
        ChunkInterestQueryStatus query = ChunkInterestQuery.TryQuery(
            space,
            anchor,
            _interestProfile,
            _previousInterest.AsSpan(0, _subscriptionCount),
            _desiredInterest,
            out int desiredCount,
            out int requiredSubscriptions,
            out int stableMovementBudget);
        AttributeElapsed(ref _exactQueryTicks, phaseStarted);
        if (_reconcileAttributionEnabled) SaturatingIncrement(ref _attributedExactQueries);
        _fullInterestQueries++;
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

        phaseStarted = AttributionTimestamp();
        int requiredChanges = CountChanges(desiredCount);
        AttributeElapsed(ref _changeCountTicks, phaseStarted);
        if (changes.Length < requiredChanges)
        {
            result = Result(OfflineDirectChunkReconcileStatus.ChangeDestinationTooSmall, false,
                requiredSubscriptions, 0, requiredChanges);
            return result.Status;
        }

        bool sourceReset = _sourceEpoch != source.Epoch;
        if (requiredChanges == 0 && !sourceReset)
        {
            phaseStarted = AttributionTimestamp();
            _lastInterestAnchor = anchor;
            _lastInterestSpace = space;
            _stableInterestMovementBudgetMillimeters = stableMovementBudget;
            _hasLastInterestAnchor = true;
            _reconciliations++;
            result = Result(
                OfflineDirectChunkReconcileStatus.Reconciled,
                false,
                requiredSubscriptions,
                0,
                0);
            AttributeElapsed(ref _reconcilePublicationTicks, phaseStarted);
            return result.Status;
        }
        phaseStarted = AttributionTimestamp();
        int changeCount = WriteLeaves(changes, desiredCount);
        AttributeElapsed(ref _changeEmissionTicks, phaseStarted);
        phaseStarted = AttributionTimestamp();
        changeCount = BuildNextSubscriptionsAndWriteChanges(
            changes, desiredCount, sourceReset, changeCount);
        AttributeElapsed(ref _nextBankBuildTicks, phaseStarted);
        phaseStarted = AttributionTimestamp();
        (_subscriptions, _nextSubscriptions) = (_nextSubscriptions, _subscriptions);
        _subscriptionCount = desiredCount;
        _sourceEpoch = source.Epoch;
        _lastInterestAnchor = anchor;
        _lastInterestSpace = space;
        _stableInterestMovementBudgetMillimeters = stableMovementBudget;
        _hasLastInterestAnchor = true;
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
        AttributeElapsed(ref _reconcilePublicationTicks, phaseStarted);
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

        OfflineDirectChunkSubscriptionState subscription = _subscriptions[subscriptionIndex];
        uint period = _cadence.Period(subscription.Tier);
        if (subscription.HasKnowledge && cadenceStep % period != subscription.CadencePhase)
        {
            _cadenceDeferrals++;
            read = EmptyRead(OfflineDirectChunkReadStatus.NotDue, source.Epoch);
            return read.Status;
        }

        ChunkPublicationReadStatus publicationStatus;
        ChunkPublicationBatch batch;
        ChunkPublicationCursor cursor = CursorFor(subscription);
        if (cursor.IsValid)
        {
            publicationStatus = source.TryCopyDelta(
                subscription.Key, cursor, _deltaScratch, out batch);
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
            subscription.Key, _snapshotScratch, out batch);
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
            _subscriptions[subscriptionIndex].Key, _snapshotScratch, out var batch);
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
        ref OfflineDirectChunkSubscriptionState subscription = ref _subscriptions[_pendingIndex];
        switch (_pendingStatus)
        {
            case OfflineDirectChunkReadStatus.Snapshot:
                subscription.AppliedRevision = _pendingBatch.Cursor.Revision;
                subscription.HasKnowledge = true;
                _snapshotReads++;
                break;
            case OfflineDirectChunkReadStatus.Delta:
                subscription.AppliedRevision = _pendingBatch.Cursor.Revision;
                subscription.HasKnowledge = true;
                _deltaReads++;
                break;
            case OfflineDirectChunkReadStatus.NoChanges:
                subscription.AppliedRevision = _pendingBatch.Cursor.Revision;
                subscription.HasKnowledge = true;
                _noChangeReads++;
                break;
            case OfflineDirectChunkReadStatus.ChunkAbsent:
                subscription.AppliedRevision = default;
                subscription.HasKnowledge = true;
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
        SpatialChunkKey key = _subscriptions[_pendingIndex].Key;
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
        ref OfflineDirectChunkSubscriptionState subscription = ref _subscriptions[subscriptionIndex];

        if (ticket.Status == OfflineDirectChunkReadStatus.ChunkAbsent)
        {
            if (subscription.HasKnowledge && !subscription.AppliedRevision.IsValid)
                return OfflineDirectChunkAppliedProofStatus.Duplicate;
            subscription.AppliedRevision = default;
            subscription.HasKnowledge = true;
            _snapshotReads++;
            _chunkAbsentReads++;
            return OfflineDirectChunkAppliedProofStatus.Applied;
        }

        if (!ticket.Cursor.IsValid) return OfflineDirectChunkAppliedProofStatus.Invalid;
        if (subscription.AppliedRevision.IsValid)
        {
            int comparison = ticket.Cursor.Revision.CompareTo(subscription.AppliedRevision);
            if (comparison < 0) return OfflineDirectChunkAppliedProofStatus.Stale;
            if (comparison == 0) return OfflineDirectChunkAppliedProofStatus.Duplicate;
        }
        subscription.AppliedRevision = ticket.Cursor.Revision;
        subscription.HasKnowledge = true;
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
            _fullInterestQueries,
            _stableInterestFastPaths,
            _stableInterestMovementBudgetMillimeters,
            CalculateReservedBytes());
    }

    public OfflineDirectChunkRecipientStorageSnapshot GetStorageSnapshot()
    {
        EnsureOwner();
        int retainedBytes = Marshal.SizeOf<OfflineDirectChunkSubscriptionState>();
        long current = checked((long)_subscriptions.Length * retainedBytes);
        long next = checked((long)_nextSubscriptions.Length * retainedBytes);
        long previous = checked((long)_previousInterest.Length * Marshal.SizeOf<ChunkInterestEntry>());
        long desired = checked((long)_desiredInterest.Length * Marshal.SizeOf<ChunkInterestEntry>());
        long snapshot = checked((long)_snapshotScratch.Length * Marshal.SizeOf<WorldPublicRecord>());
        long delta = checked((long)_deltaScratch.Length * Marshal.SizeOf<WorldPublicDeltaRecord>());
        return new OfflineDirectChunkRecipientStorageSnapshot(
            retainedBytes,
            Marshal.SizeOf<OfflineDirectChunkSubscription>(),
            current,
            next,
            previous,
            desired,
            snapshot,
            delta,
            checked(current + next + previous + desired + snapshot + delta));
    }

    /// <summary>
    /// Enables or disables opt-in reconciliation attribution and clears every retained counter.
    /// The normal path pays only dormant branch checks while attribution is disabled.
    /// </summary>
    public void ResetReconcileAttribution(bool enabled)
    {
        EnsureOwner();
        _reconcileAttributionEnabled = enabled;
        _attributedStableFastPaths = 0;
        _attributedExactQueries = 0;
        _previousInterestCopyTicks = 0;
        _exactQueryTicks = 0;
        _changeCountTicks = 0;
        _nextBankBuildTicks = 0;
        _changeEmissionTicks = 0;
        _reconcilePublicationTicks = 0;
    }

    public OfflineDirectChunkReconcileAttribution GetReconcileAttribution()
    {
        EnsureOwner();
        return new OfflineDirectChunkReconcileAttribution(
            _reconcileAttributionEnabled,
            _attributedStableFastPaths,
            _attributedExactQueries,
            _previousInterestCopyTicks,
            _exactQueryTicks,
            _changeCountTicks,
            _nextBankBuildTicks,
            _changeEmissionTicks,
            _reconcilePublicationTicks);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long AttributionTimestamp() =>
        _reconcileAttributionEnabled ? Stopwatch.GetTimestamp() : 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AttributeElapsed(ref ulong destination, long started)
    {
        if (!_reconcileAttributionEnabled) return;
        long elapsed = Stopwatch.GetTimestamp() - started;
        if (elapsed <= 0) return;
        ulong value = checked((ulong)elapsed);
        destination = ulong.MaxValue - destination < value ? ulong.MaxValue : destination + value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
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

    private int BuildNextSubscriptionsAndWriteChanges(
        Span<OfflineDirectChunkSubscriptionChange> changes,
        int desiredCount,
        bool sourceReset,
        int written)
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
                OfflineDirectChunkSubscriptionState retained = _subscriptions[previous];
                _nextSubscriptions[desired] = new OfflineDirectChunkSubscriptionState(
                    interest.Key,
                    interest.Tier,
                    sourceReset ? default : retained.AppliedRevision,
                    !sourceReset && retained.HasKnowledge,
                    StablePhase(interest.Key, _cadence.Period(interest.Tier)));
                if (retained.Tier != interest.Tier)
                    changes[written++] = new OfflineDirectChunkSubscriptionChange(
                        OfflineDirectChunkSubscriptionChangeKind.TierChanged,
                        interest.Key,
                        retained.Tier,
                        interest.Tier);
            }
            else
            {
                _nextSubscriptions[desired] = new OfflineDirectChunkSubscriptionState(
                    interest.Key,
                    interest.Tier,
                    default,
                    false,
                    StablePhase(interest.Key, _cadence.Period(interest.Tier)));
                changes[written++] = new OfflineDirectChunkSubscriptionChange(
                    OfflineDirectChunkSubscriptionChangeKind.Entered,
                    interest.Key,
                    default,
                    interest.Tier);
            }
        }
        return written;
    }

    private int WriteLeaves(Span<OfflineDirectChunkSubscriptionChange> changes, int desiredCount)
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

    private bool IsWithinStableInterestBudget(in WorldPosition anchor)
    {
        long deltaX = Math.Abs(anchor.GlobalXMillimeters - _lastInterestAnchor.GlobalXMillimeters);
        long deltaZ = Math.Abs(anchor.GlobalZMillimeters - _lastInterestAnchor.GlobalZMillimeters);
        return deltaX + deltaZ <= _stableInterestMovementBudgetMillimeters;
    }

    private long CalculateReservedBytes() => GetStorageSnapshot().TotalReservedBytes;

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The OfflineDirect chunk recipient is owner-thread bound.");
    }

    private ChunkPublicationCursor CursorFor(in OfflineDirectChunkSubscriptionState subscription) =>
        subscription.AppliedRevision.IsValid
            ? new ChunkPublicationCursor(_sourceEpoch, subscription.Key, subscription.AppliedRevision)
            : default;

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct OfflineDirectChunkSubscriptionState
    {
        internal OfflineDirectChunkSubscriptionState(
            SpatialChunkKey key,
            ChunkInterestTier tier,
            ChunkPublicationRevision appliedRevision,
            bool hasKnowledge,
            uint cadencePhase)
        {
            Key = key;
            AppliedRevision = appliedRevision;
            CadencePhase = cadencePhase;
            Tier = tier;
            HasKnowledgeByte = hasKnowledge ? (byte)1 : (byte)0;
        }

        internal SpatialChunkKey Key;
        internal ChunkPublicationRevision AppliedRevision;
        internal uint CadencePhase;
        internal ChunkInterestTier Tier;
        private byte HasKnowledgeByte;

        internal readonly ChunkInterestEntry Interest => new(Key, Tier);
        internal bool HasKnowledge
        {
            readonly get => HasKnowledgeByte != 0;
            set => HasKnowledgeByte = value ? (byte)1 : (byte)0;
        }
    }
}
