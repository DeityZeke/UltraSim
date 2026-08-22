using System.Runtime.CompilerServices;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

public readonly record struct ChunkPublicationWorkSchedulerLimits(
    int MaximumRecipients,
    int MaximumMemberships)
{
    public bool IsValid => MaximumRecipients > 0 && MaximumMemberships >= MaximumRecipients;
}

/// <summary>Generation-fenced scheduler identity; it is not a connection or game identity.</summary>
public readonly record struct ChunkReplicationRecipientId(int Index, uint Generation)
{
    public bool IsValid => Index >= 0 && Generation != 0;
}

public enum ChunkPublicationWorkKind : byte
{
    Incoming = 1,
    Dirty = 2,
}

/// <summary>One due game-owned publication scope. It contains no encoded bytes or Network state.</summary>
public readonly record struct ChunkPublicationWork(
    ChunkReplicationRecipientId Recipient,
    SpatialChunkKey Key,
    ChunkInterestTier Tier,
    ChunkPublicationWorkKind Kind);

public enum ChunkPublicationCommitObservationStatus : byte
{
    Observed = 1,
    NoNewCommit = 2,
    SourceRequiresReplacement = 3,
    CommitGapRequiresRefresh = 4,
}

public readonly record struct ChunkPublicationWorkSchedulerSnapshot(
    PublicationEpoch SourceEpoch,
    ulong ObservedCommitSequence,
    int RecipientCount,
    int MembershipCount,
    int SharedChunkCount,
    int PendingWorkCount,
    int CurrentCleanMembershipCount,
    ulong IncomingQueued,
    ulong DirtyCommitsObserved,
    ulong DirtyChunksObserved,
    ulong DirtyChunksWithoutSubscribers,
    ulong DirtyRecipientFanout,
    ulong DirtyWorkCoalesces,
    ulong PendingMembershipsExamined,
    ulong DueWorkDequeued,
    ulong CadenceDeferrals,
    ulong SourceRefreshes,
    long ReservedBytes);

/// <summary>
/// N1.4 game-owned work discovery over accepted N1 subscriptions. Interest changes maintain a
/// bounded chunk-to-recipient reverse index. One authoritative dirty-chunk fact fans out only to
/// current subscribers; clean current scopes create no publication query. This class does not own
/// transport, payloads, application proof, or publication history.
/// </summary>
public sealed class ChunkPublicationWorkScheduler
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ChunkPublicationWorkSchedulerLimits _limits;
    private readonly RecipientState[] _recipients;
    private readonly uint[] _recipientGenerations;
    private readonly int[] _freeRecipients;
    private readonly MembershipState[] _memberships;
    private readonly int[] _freeMemberships;
    private readonly ChunkState[] _chunks;
    private readonly int[] _freeChunks;
    private readonly int[] _pendingMemberships;
    private readonly Dictionary<RecipientChunkKey, int> _membershipMap;
    private readonly Dictionary<SpatialChunkKey, int> _chunkMap;
    private readonly int _membershipMapCapacity;
    private readonly int _chunkMapCapacity;
    private ChunkPublicationStore _source;
    private int _freeRecipientCount;
    private int _freeMembershipCount;
    private int _freeChunkCount;
    private int _recipientCount;
    private int _reservedMembershipCapacity;
    private int _membershipCount;
    private int _chunkCount;
    private int _pendingCount;
    private int _pendingScanPosition;
    private ulong _pendingPassId;
    private ulong _pendingPassCadenceStep;
    private bool _hasPendingPassCadenceStep;
    private bool _pendingPassExhausted;
    private ulong _observedCommitSequence;
    private ulong _incomingQueued;
    private ulong _dirtyCommitsObserved;
    private ulong _dirtyChunksObserved;
    private ulong _dirtyChunksWithoutSubscribers;
    private ulong _dirtyRecipientFanout;
    private ulong _dirtyWorkCoalesces;
    private ulong _pendingMembershipsExamined;
    private ulong _dueWorkDequeued;
    private ulong _cadenceDeferrals;
    private ulong _sourceRefreshes;

    public ChunkPublicationWorkScheduler(
        ChunkPublicationStore source,
        ChunkPublicationWorkSchedulerLimits limits)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (!limits.IsValid) throw new ArgumentOutOfRangeException(nameof(limits));
        _limits = limits;
        _recipients = new RecipientState[limits.MaximumRecipients];
        _recipientGenerations = new uint[limits.MaximumRecipients];
        _freeRecipients = new int[limits.MaximumRecipients];
        _memberships = new MembershipState[limits.MaximumMemberships];
        _freeMemberships = new int[limits.MaximumMemberships];
        _chunks = new ChunkState[limits.MaximumMemberships];
        _freeChunks = new int[limits.MaximumMemberships];
        _pendingMemberships = new int[limits.MaximumMemberships];
        _membershipMap = new Dictionary<RecipientChunkKey, int>(limits.MaximumMemberships);
        _chunkMap = new Dictionary<SpatialChunkKey, int>(limits.MaximumMemberships);
        _membershipMapCapacity = _membershipMap.EnsureCapacity(limits.MaximumMemberships);
        _chunkMapCapacity = _chunkMap.EnsureCapacity(limits.MaximumMemberships);
        for (int index = limits.MaximumRecipients - 1; index >= 0; index--)
            _freeRecipients[_freeRecipientCount++] = index;
        for (int index = limits.MaximumMemberships - 1; index >= 0; index--)
        {
            _freeMemberships[_freeMembershipCount++] = index;
            _freeChunks[_freeChunkCount++] = index;
            _memberships[index].PendingPosition = -1;
            _memberships[index].RecipientPrevious = -1;
            _memberships[index].RecipientNext = -1;
            _memberships[index].ChunkPrevious = -1;
            _memberships[index].ChunkNext = -1;
            _chunks[index].MembershipHead = -1;
        }
        _observedCommitSequence = source.CommitSequence;
    }

    public ChunkPublicationWorkSchedulerLimits Limits => _limits;

    public bool TryRegister(
        OfflineDirectChunkRecipient recipient,
        out ChunkReplicationRecipientId id)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        EnsureOwner();
        if (_freeRecipientCount == 0 ||
            recipient.Limits.MaximumSubscriptions >
                _limits.MaximumMemberships - _reservedMembershipCapacity)
        {
            id = default;
            return false;
        }

        int index = _freeRecipients[--_freeRecipientCount];
        uint generation = _recipientGenerations[index] == uint.MaxValue
            ? 0
            : _recipientGenerations[index] + 1;
        if (generation == 0)
        {
            _freeRecipients[_freeRecipientCount++] = index;
            id = default;
            return false;
        }
        _recipientGenerations[index] = generation;
        _recipients[index] = new RecipientState
        {
            Active = true,
            Generation = generation,
            Recipient = recipient,
            MembershipHead = -1,
            ReservedMemberships = recipient.Limits.MaximumSubscriptions,
        };
        _recipientCount++;
        _reservedMembershipCapacity += recipient.Limits.MaximumSubscriptions;
        id = new ChunkReplicationRecipientId(index, generation);
        return true;
    }

    public bool RemoveRecipient(ChunkReplicationRecipientId id)
    {
        EnsureOwner();
        if (!TryResolve(id, out int recipientIndex)) return false;
        int membership = _recipients[recipientIndex].MembershipHead;
        while (membership >= 0)
        {
            int next = _memberships[membership].RecipientNext;
            RemoveMembership(membership);
            membership = next;
        }
        _reservedMembershipCapacity -= _recipients[recipientIndex].ReservedMemberships;
        _recipients[recipientIndex] = default;
        _recipients[recipientIndex].MembershipHead = -1;
        _freeRecipients[_freeRecipientCount++] = recipientIndex;
        _recipientCount--;
        return true;
    }

    /// <summary>
    /// Applies trusted, already-committed recipient topology changes. Registering reserves the
    /// recipient's complete declared subscription capacity, so accepted recipient reconciliation
    /// cannot partially exceed this scheduler afterward.
    /// </summary>
    public bool TryApplyReconciliation(
        ChunkReplicationRecipientId id,
        ReadOnlySpan<OfflineDirectChunkSubscriptionChange> changes,
        in OfflineDirectChunkReconcileResult result)
    {
        EnsureOwner();
        if (!TryResolve(id, out int recipientIndex) ||
            result.Status != OfflineDirectChunkReconcileStatus.Reconciled ||
            changes.Length != result.ChangeCount ||
            result.SubscriptionCount > _recipients[recipientIndex].Recipient!.Limits.MaximumSubscriptions)
            return false;

        int leaves = 0;
        int enters = 0;
        for (int index = 0; index < changes.Length; index++)
        {
            ref readonly OfflineDirectChunkSubscriptionChange change = ref changes[index];
            if (!change.Key.Space.IsValid) return false;
            bool exists = _membershipMap.ContainsKey(new RecipientChunkKey(id, change.Key));
            switch (change.Kind)
            {
                case OfflineDirectChunkSubscriptionChangeKind.Left when exists:
                    leaves++;
                    break;
                case OfflineDirectChunkSubscriptionChangeKind.Entered when !exists:
                    if (FindSubscription(_recipients[recipientIndex].Recipient!, change.Key) < 0)
                        return false;
                    enters++;
                    break;
                case OfflineDirectChunkSubscriptionChangeKind.TierChanged when exists:
                    if (FindSubscription(_recipients[recipientIndex].Recipient!, change.Key) < 0)
                        return false;
                    break;
                default:
                    return false;
            }
        }
        if (enters > _freeMembershipCount + leaves) return false;
        if (CountRecipientMemberships(recipientIndex) - leaves + enters != result.SubscriptionCount)
            return false;

        for (int index = 0; index < changes.Length; index++)
        {
            ref readonly OfflineDirectChunkSubscriptionChange change = ref changes[index];
            var mapKey = new RecipientChunkKey(id, change.Key);
            switch (change.Kind)
            {
                case OfflineDirectChunkSubscriptionChangeKind.Left:
                    RemoveMembership(_membershipMap[mapKey]);
                    break;
                case OfflineDirectChunkSubscriptionChangeKind.Entered:
                    AddMembership(recipientIndex, id, change.Key);
                    break;
                case OfflineDirectChunkSubscriptionChangeKind.TierChanged:
                    RefreshMembershipCadence(_membershipMap[mapKey]);
                    break;
            }
        }
        if (result.SourceReset) QueueRecipientIncoming(recipientIndex);
        return CountRecipientMemberships(recipientIndex) == result.SubscriptionCount;
    }

    /// <summary>
    /// Observes exactly the next authoritative commit. Missing a commit fails closed: the caller
    /// must call <see cref="RefreshAllFromCurrentSource"/> so every retained cursor is queried from
    /// current truth rather than silently losing shared dirty discovery.
    /// </summary>
    public ChunkPublicationCommitObservationStatus ObserveCommit(ChunkPublicationStore source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (!ReferenceEquals(source, _source) || source.Epoch != _source.Epoch)
            return ChunkPublicationCommitObservationStatus.SourceRequiresReplacement;
        ulong current = source.CommitSequence;
        if (current == _observedCommitSequence)
            return ChunkPublicationCommitObservationStatus.NoNewCommit;
        if (_observedCommitSequence == ulong.MaxValue || current != _observedCommitSequence + 1)
            return ChunkPublicationCommitObservationStatus.CommitGapRequiresRefresh;

        ReadOnlySpan<SpatialChunkKey> dirty = source.LastCommittedDirtyChunks;
        for (int index = 0; index < dirty.Length; index++) MarkDirty(dirty[index]);
        _observedCommitSequence = current;
        SaturatingIncrement(ref _dirtyCommitsObserved);
        SaturatingAdd(ref _dirtyChunksObserved, checked((ulong)dirty.Length));
        return ChunkPublicationCommitObservationStatus.Observed;
    }

    /// <summary>
    /// Rebinds a publication generation and makes every retained scope eligible to establish
    /// current truth. Recipient cursor reset remains owned by its accepted reconciliation path.
    /// </summary>
    public void ReplaceSource(ChunkPublicationStore source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (ReferenceEquals(source, _source)) return;
        if (source.Epoch == _source.Epoch)
            throw new ArgumentException("A replacement source must own a distinct epoch.", nameof(source));
        _source = source;
        _observedCommitSequence = source.CommitSequence;
        QueueAllIncoming();
        SaturatingIncrement(ref _sourceRefreshes);
    }

    /// <summary>Recovers a missed commit observation without inventing per-recipient history.</summary>
    public void RefreshAllFromCurrentSource()
    {
        EnsureOwner();
        _observedCommitSequence = _source.CommitSequence;
        QueueAllIncoming();
        SaturatingIncrement(ref _sourceRefreshes);
    }

    /// <summary>Dequeues one cadence-eligible incoming or dirty scope.</summary>
    public bool TryDequeueDue(ulong cadenceStep, out ChunkPublicationWork work)
    {
        EnsureOwner();
        if (_pendingCount == 0)
        {
            work = default;
            return false;
        }
        if (!_hasPendingPassCadenceStep || _pendingPassCadenceStep != cadenceStep)
            StartPendingPass(cadenceStep);
        if (_pendingPassExhausted)
        {
            work = default;
            return false;
        }

        int remaining = _pendingCount;
        while (remaining-- > 0 && _pendingCount != 0)
        {
            if (_pendingScanPosition >= _pendingCount) _pendingScanPosition = 0;
            int position = _pendingScanPosition;
            _pendingScanPosition = position + 1;
            SaturatingIncrement(ref _pendingMembershipsExamined);
            int membershipIndex = _pendingMemberships[position];
            ref MembershipState membership = ref _memberships[membershipIndex];
            if (membership.LastExaminedPendingPass == _pendingPassId) continue;
            membership.LastExaminedPendingPass = _pendingPassId;
            if (membership.PendingKind == ChunkPublicationWorkKind.Dirty &&
                cadenceStep % membership.CadencePeriod != membership.CadencePhase)
            {
                SaturatingIncrement(ref _cadenceDeferrals);
                continue;
            }
            work = new ChunkPublicationWork(
                membership.Recipient,
                membership.Key,
                membership.Tier,
                membership.PendingKind);
            RemovePending(membershipIndex);
            SaturatingIncrement(ref _dueWorkDequeued);
            return true;
        }
        _pendingPassExhausted = true;
        work = default;
        return false;
    }

    /// <summary>Returns transiently blocked work to the bounded coalescing set.</summary>
    public bool Requeue(in ChunkPublicationWork work)
    {
        EnsureOwner();
        if (!work.Recipient.IsValid || !work.Key.Space.IsValid ||
            !_membershipMap.TryGetValue(new RecipientChunkKey(work.Recipient, work.Key), out int membership))
            return false;
        Queue(membership, work.Kind);
        return true;
    }

    public ChunkPublicationWorkSchedulerSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new ChunkPublicationWorkSchedulerSnapshot(
            _source.Epoch,
            _observedCommitSequence,
            _recipientCount,
            _membershipCount,
            _chunkCount,
            _pendingCount,
            _membershipCount - _pendingCount,
            _incomingQueued,
            _dirtyCommitsObserved,
            _dirtyChunksObserved,
            _dirtyChunksWithoutSubscribers,
            _dirtyRecipientFanout,
            _dirtyWorkCoalesces,
            _pendingMembershipsExamined,
            _dueWorkDequeued,
            _cadenceDeferrals,
            _sourceRefreshes,
            CalculateReservedBytes());
    }

    private void MarkDirty(SpatialChunkKey key)
    {
        if (!_chunkMap.TryGetValue(key, out int chunkIndex))
        {
            SaturatingIncrement(ref _dirtyChunksWithoutSubscribers);
            return;
        }
        int membership = _chunks[chunkIndex].MembershipHead;
        while (membership >= 0)
        {
            int next = _memberships[membership].ChunkNext;
            SaturatingIncrement(ref _dirtyRecipientFanout);
            Queue(membership, ChunkPublicationWorkKind.Dirty);
            membership = next;
        }
    }

    private void AddMembership(
        int recipientIndex,
        ChunkReplicationRecipientId recipient,
        SpatialChunkKey key)
    {
        if (_freeMembershipCount == 0)
            throw new InvalidOperationException("Preflighted publication membership capacity disappeared.");
        int chunkIndex = GetOrCreateChunk(key);
        int index = _freeMemberships[--_freeMembershipCount];
        ref MembershipState membership = ref _memberships[index];
        membership = new MembershipState
        {
            Active = true,
            Recipient = recipient,
            Key = key,
            RecipientPrevious = -1,
            RecipientNext = _recipients[recipientIndex].MembershipHead,
            ChunkPrevious = -1,
            ChunkNext = _chunks[chunkIndex].MembershipHead,
            ChunkIndex = chunkIndex,
            PendingPosition = -1,
        };
        if (membership.RecipientNext >= 0)
            _memberships[membership.RecipientNext].RecipientPrevious = index;
        _recipients[recipientIndex].MembershipHead = index;
        if (membership.ChunkNext >= 0)
            _memberships[membership.ChunkNext].ChunkPrevious = index;
        _chunks[chunkIndex].MembershipHead = index;
        _chunks[chunkIndex].MembershipCount++;
        _membershipMap.Add(new RecipientChunkKey(recipient, key), index);
        _membershipCount++;
        RefreshMembershipCadence(index);
        Queue(index, ChunkPublicationWorkKind.Incoming);
    }

    private void RemoveMembership(int index)
    {
        ref MembershipState membership = ref _memberships[index];
        RemovePending(index);
        int recipientIndex = membership.Recipient.Index;
        if (membership.RecipientPrevious >= 0)
            _memberships[membership.RecipientPrevious].RecipientNext = membership.RecipientNext;
        else
            _recipients[recipientIndex].MembershipHead = membership.RecipientNext;
        if (membership.RecipientNext >= 0)
            _memberships[membership.RecipientNext].RecipientPrevious = membership.RecipientPrevious;

        int chunkIndex = membership.ChunkIndex;
        if (membership.ChunkPrevious >= 0)
            _memberships[membership.ChunkPrevious].ChunkNext = membership.ChunkNext;
        else
            _chunks[chunkIndex].MembershipHead = membership.ChunkNext;
        if (membership.ChunkNext >= 0)
            _memberships[membership.ChunkNext].ChunkPrevious = membership.ChunkPrevious;
        _chunks[chunkIndex].MembershipCount--;
        _membershipMap.Remove(new RecipientChunkKey(membership.Recipient, membership.Key));
        if (_chunks[chunkIndex].MembershipCount == 0) RemoveChunk(chunkIndex);

        membership = default;
        membership.PendingPosition = -1;
        membership.RecipientPrevious = -1;
        membership.RecipientNext = -1;
        membership.ChunkPrevious = -1;
        membership.ChunkNext = -1;
        _freeMemberships[_freeMembershipCount++] = index;
        _membershipCount--;
    }

    private int GetOrCreateChunk(SpatialChunkKey key)
    {
        if (_chunkMap.TryGetValue(key, out int existing)) return existing;
        if (_freeChunkCount == 0)
            throw new InvalidOperationException("Publication reverse-index chunk capacity was exhausted.");
        int index = _freeChunks[--_freeChunkCount];
        _chunks[index] = new ChunkState { Active = true, Key = key, MembershipHead = -1 };
        _chunkMap.Add(key, index);
        _chunkCount++;
        return index;
    }

    private void RemoveChunk(int index)
    {
        _chunkMap.Remove(_chunks[index].Key);
        _chunks[index] = default;
        _chunks[index].MembershipHead = -1;
        _freeChunks[_freeChunkCount++] = index;
        _chunkCount--;
    }

    private void RefreshMembershipCadence(int membershipIndex)
    {
        ref MembershipState membership = ref _memberships[membershipIndex];
        OfflineDirectChunkRecipient recipient =
            _recipients[membership.Recipient.Index].Recipient!;
        int subscriptionIndex = FindSubscription(recipient, membership.Key);
        if (subscriptionIndex < 0)
            throw new InvalidOperationException("Accepted subscription topology and scheduler diverged.");
        OfflineDirectChunkSubscription subscription = recipient.Subscriptions[subscriptionIndex];
        uint cadencePeriod = recipient.GetCadencePeriod(subscription.Interest.Tier);
        bool cadenceChanged = membership.Tier != subscription.Interest.Tier ||
            membership.CadencePeriod != cadencePeriod ||
            membership.CadencePhase != subscription.CadencePhase;
        membership.Tier = subscription.Interest.Tier;
        membership.CadencePeriod = cadencePeriod;
        membership.CadencePhase = subscription.CadencePhase;
        if (cadenceChanged && membership.PendingPosition >= 0)
        {
            membership.LastExaminedPendingPass = 0;
            _pendingPassExhausted = false;
            _pendingScanPosition = membership.PendingPosition;
        }
    }

    private void QueueAllIncoming()
    {
        for (int index = 0; index < _memberships.Length; index++)
            if (_memberships[index].Active) Queue(index, ChunkPublicationWorkKind.Incoming);
    }

    private void QueueRecipientIncoming(int recipientIndex)
    {
        for (int membership = _recipients[recipientIndex].MembershipHead;
             membership >= 0;
             membership = _memberships[membership].RecipientNext)
            Queue(membership, ChunkPublicationWorkKind.Incoming);
    }

    private void Queue(int membershipIndex, ChunkPublicationWorkKind kind)
    {
        ref MembershipState membership = ref _memberships[membershipIndex];
        if (membership.PendingPosition >= 0)
        {
            if (membership.PendingKind == ChunkPublicationWorkKind.Incoming ||
                membership.PendingKind == kind)
            {
                if (kind == ChunkPublicationWorkKind.Dirty)
                    SaturatingIncrement(ref _dirtyWorkCoalesces);
                return;
            }
            membership.PendingKind = ChunkPublicationWorkKind.Incoming;
            membership.LastExaminedPendingPass = 0;
            _pendingPassExhausted = false;
            _pendingScanPosition = membership.PendingPosition;
            SaturatingIncrement(ref _incomingQueued);
            return;
        }
        membership.PendingKind = kind;
        membership.PendingPosition = _pendingCount;
        membership.LastExaminedPendingPass = 0;
        _pendingMemberships[_pendingCount++] = membershipIndex;
        _pendingPassExhausted = false;
        _pendingScanPosition = membership.PendingPosition;
        if (kind == ChunkPublicationWorkKind.Incoming)
            SaturatingIncrement(ref _incomingQueued);
    }

    private void RemovePending(int membershipIndex)
    {
        ref MembershipState membership = ref _memberships[membershipIndex];
        int position = membership.PendingPosition;
        if (position < 0) return;
        int last = --_pendingCount;
        int replacement = _pendingMemberships[last];
        _pendingMemberships[position] = replacement;
        _pendingMemberships[last] = 0;
        if (position != last) _memberships[replacement].PendingPosition = position;
        membership.PendingPosition = -1;
        membership.PendingKind = default;
        membership.LastExaminedPendingPass = 0;
        _pendingScanPosition = _pendingCount == 0 ? 0 : Math.Min(position, _pendingCount - 1);
    }

    private bool TryResolve(ChunkReplicationRecipientId id, out int index)
    {
        if (id.IsValid && (uint)id.Index < (uint)_recipients.Length &&
            _recipients[id.Index].Active && _recipients[id.Index].Generation == id.Generation)
        {
            index = id.Index;
            return true;
        }
        index = -1;
        return false;
    }

    private int CountRecipientMemberships(int recipientIndex)
    {
        int count = 0;
        for (int membership = _recipients[recipientIndex].MembershipHead;
             membership >= 0;
             membership = _memberships[membership].RecipientNext)
            count++;
        return count;
    }

    private static int FindSubscription(
        OfflineDirectChunkRecipient recipient,
        SpatialChunkKey key)
    {
        ReadOnlySpan<OfflineDirectChunkSubscription> subscriptions = recipient.Subscriptions;
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

    private long CalculateReservedBytes()
    {
        long bytes = checked(
            (long)_recipients.Length * Unsafe.SizeOf<RecipientState>() +
            (long)_recipientGenerations.Length * sizeof(uint) +
            (long)_freeRecipients.Length * sizeof(int) +
            (long)_memberships.Length * Unsafe.SizeOf<MembershipState>() +
            (long)_freeMemberships.Length * sizeof(int) +
            (long)_chunks.Length * Unsafe.SizeOf<ChunkState>() +
            (long)_freeChunks.Length * sizeof(int) +
            (long)_pendingMemberships.Length * sizeof(int));
        bytes = checked(bytes +
            (long)_membershipMapCapacity * (Unsafe.SizeOf<RecipientChunkKey>() + sizeof(int) * 2) +
            (long)_chunkMapCapacity * (Unsafe.SizeOf<SpatialChunkKey>() + sizeof(int) * 2));
        return bytes;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, ulong amount) =>
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;

    private void StartPendingPass(ulong cadenceStep)
    {
        if (_pendingPassId == ulong.MaxValue)
        {
            for (int index = 0; index < _memberships.Length; index++)
                _memberships[index].LastExaminedPendingPass = 0;
            _pendingPassId = 1;
        }
        else
        {
            _pendingPassId++;
        }
        _pendingPassCadenceStep = cadenceStep;
        _hasPendingPassCadenceStep = true;
        _pendingPassExhausted = false;
        _pendingScanPosition = 0;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The chunk publication work scheduler is owner-thread bound.");
    }

    private readonly record struct RecipientChunkKey(
        ChunkReplicationRecipientId Recipient,
        SpatialChunkKey Key);

    private struct RecipientState
    {
        internal bool Active;
        internal uint Generation;
        internal OfflineDirectChunkRecipient? Recipient;
        internal int MembershipHead;
        internal int ReservedMemberships;
    }

    private struct MembershipState
    {
        internal bool Active;
        internal ChunkReplicationRecipientId Recipient;
        internal SpatialChunkKey Key;
        internal ChunkInterestTier Tier;
        internal uint CadencePeriod;
        internal uint CadencePhase;
        internal int RecipientPrevious;
        internal int RecipientNext;
        internal int ChunkPrevious;
        internal int ChunkNext;
        internal int ChunkIndex;
        internal int PendingPosition;
        internal ChunkPublicationWorkKind PendingKind;
        internal ulong LastExaminedPendingPass;
    }

    private struct ChunkState
    {
        internal bool Active;
        internal SpatialChunkKey Key;
        internal int MembershipHead;
        internal int MembershipCount;
    }
}
