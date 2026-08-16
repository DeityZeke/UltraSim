using System.Runtime.CompilerServices;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public readonly record struct ClientWorldPoint(int XMillimeters, int ZMillimeters);

public enum RemoteWorldStateClientApplyStatus : byte
{
    Applied = 0,
    Duplicate = 1,
    Rejected = 2,
    CapacityExceeded = 3,
}

public readonly record struct RemoteWorldEntitySnapshot(
    ReplicationEntityId Entity,
    ClientEntitySlot Slot,
    ulong CanonicalVersion,
    byte Horizon,
    int XMillimeters,
    int ZMillimeters,
    int TickMarker,
    int InputVelocity);

public readonly record struct RemoteWorldStateClientSnapshot(
    StateEpoch StateEpoch,
    ulong BaselineRevision,
    ulong EventFloor,
    ulong RelationSequenceFloor,
    int KnownIdentityCount,
    int ActiveEntityCount,
    int PendingApplicationAcknowledgements,
    ulong AppliedBundles,
    ulong DuplicateBundles,
    ulong RejectedBundles,
    ulong AppliedBaselines,
    ulong RejectedBaselines,
    bool BaselineCandidateActive,
    bool IsFaulted,
    long ReservedBytes);

/// <summary>Cold inspection of proofs produced since the previous flush boundary.</summary>
public readonly record struct RemoteWorldStateAcknowledgementFlushOpportunity(
    int ProducedAcknowledgements,
    int EncodedBytes,
    int PendingAcknowledgements,
    int Applied,
    int Rejected,
    int CapacityExceeded);

/// <summary>
/// Bounded many-entity retained client projection for complete WorldState bundles.
/// </summary>
/// <remarks>
/// Canonical replication indices are dictionary keys, never client array indices.
/// Active entities occupy private dense generational slots. Tombstone knowledge is
/// retained independently from presentation so delayed present records cannot
/// resurrect an older relation sequence. Camera selection only reads this retained
/// state and never changes server interest.
/// </remarks>
public sealed class RemoteWorldStateClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private StateEpoch _stateEpoch;
    private readonly int _maximumKnownIdentities;
    private Dictionary<uint, KnowledgeState> _knowledge;
    private RemoteWorldEntitySnapshot[] _active;
    private uint[] _slotGenerations;
    private int[] _freeSlots;
    private Dictionary<uint, KnowledgeState> _candidateKnowledge;
    private RemoteWorldEntitySnapshot[] _candidateActive;
    private uint[] _candidateSlotGenerations;
    private int[] _candidateFreeSlots;
    private readonly RemoteWorldStateRecord[] _baselineRecords;
    private readonly bool[] _baselineSegments;
    private readonly RemoteWorldStateRecord[] _records;
    private readonly RemoteWorldStateAppliedMessage[] _pendingAcknowledgements;
    private readonly byte[] _ackScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly int _knowledgeMapCapacity;
    private int _activeHigh;
    private int _activeCount;
    private int _freeSlotCount;
    private int _ackHead;
    private int _ackCount;
    private ulong _appliedBundles;
    private ulong _duplicateBundles;
    private ulong _rejectedBundles;
    private bool _acknowledgementCensusEnabled;
    private int _acknowledgementsProducedSinceFlush;
    private int _acknowledgementBytesProducedSinceFlush;
    private int _appliedAcknowledgementsSinceFlush;
    private int _rejectedAcknowledgementsSinceFlush;
    private int _capacityAcknowledgementsSinceFlush;
    private bool _faulted;
    private bool _hasPendingBaselineAcknowledgement;
    private RemoteWorldBaselineAppliedMessage _pendingBaselineAcknowledgement;
    private ulong _baselineRevision;
    private ulong _eventFloor;
    private ulong _relationSequenceFloor;
    private ulong _candidateBaselineId;
    private ulong _candidateBaselineRevision;
    private ulong _candidateEventFloor;
    private ulong _candidateRelationSequenceFloor;
    private ulong _candidateHash;
    private StateEpoch _candidateStateEpoch;
    private int _candidateRecordCount;
    private ushort _candidateSegmentCount;
    private ushort _candidateRecordsPerSegment;
    private int _receivedSegmentCount;
    private bool _candidateCommitReceived;
    private ulong _appliedBaselines;
    private ulong _rejectedBaselines;

    public RemoteWorldStateClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        StateEpoch stateEpoch,
        int maximumKnownIdentities,
        int maximumActiveEntities,
        int maximumRecordsPerBundle = 32,
        int maximumPendingAcknowledgements = 64)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid projection epoch is required.", nameof(stateEpoch));
        if (maximumKnownIdentities <= 0) throw new ArgumentOutOfRangeException(nameof(maximumKnownIdentities));
        if (maximumActiveEntities <= 0 || maximumActiveEntities > maximumKnownIdentities)
            throw new ArgumentOutOfRangeException(nameof(maximumActiveEntities));
        if (maximumRecordsPerBundle <= 0 || maximumRecordsPerBundle > UltraSimGameProtocol.MaximumWorldStateRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecordsPerBundle));
        if (maximumPendingAcknowledgements <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingAcknowledgements));
        _applicationSessionEpoch = applicationSessionEpoch;
        _stateEpoch = stateEpoch;
        _maximumKnownIdentities = maximumKnownIdentities;
        _knowledge = new Dictionary<uint, KnowledgeState>(maximumKnownIdentities);
        _knowledgeMapCapacity = _knowledge.EnsureCapacity(maximumKnownIdentities);
        _active = new RemoteWorldEntitySnapshot[maximumActiveEntities];
        _slotGenerations = new uint[maximumActiveEntities];
        _freeSlots = new int[maximumActiveEntities];
        _candidateKnowledge = new Dictionary<uint, KnowledgeState>(maximumKnownIdentities);
        _ = _candidateKnowledge.EnsureCapacity(maximumKnownIdentities);
        _candidateActive = new RemoteWorldEntitySnapshot[maximumActiveEntities];
        _candidateSlotGenerations = new uint[maximumActiveEntities];
        _candidateFreeSlots = new int[maximumActiveEntities];
        _baselineRecords = new RemoteWorldStateRecord[maximumKnownIdentities];
        _baselineSegments = new bool[maximumKnownIdentities];
        _records = new RemoteWorldStateRecord[maximumRecordsPerBundle];
        _pendingAcknowledgements = new RemoteWorldStateAppliedMessage[maximumPendingAcknowledgements];
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId == RemoteWorldBaselineBeginMessageCodec.Descriptor.Id)
        {
            ProcessBaselineBegin(payload);
            return true;
        }
        if (message.MessageId == RemoteWorldBaselineSegmentHeaderCodec.Descriptor.Id)
        {
            ProcessBaselineSegment(payload);
            return true;
        }
        if (message.MessageId == RemoteWorldBaselineCommitMessageCodec.Descriptor.Id)
        {
            ProcessBaselineCommit(payload);
            return true;
        }
        if (message.MessageId == RemoteWorldBaselineCancelMessageCodec.Descriptor.Id)
        {
            ProcessBaselineCancel(payload);
            return true;
        }
        if (message.MessageId != RemoteWorldStateBundleHeaderCodec.Descriptor.Id) return false;
        if (_faulted) return true;
        if (!UltraSimGameProtocol.TryReadWorldStateBundle(
                payload, out RemoteWorldStateBundleHeader header, _records, out int count))
        {
            SaturatingIncrement(ref _rejectedBundles);
            return true;
        }
        RemoteWorldStateApplyStatus acknowledgement;
        if (header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            header.StateEpoch != _stateEpoch.Value)
        {
            acknowledgement = RemoteWorldStateApplyStatus.Rejected;
            SaturatingIncrement(ref _rejectedBundles);
        }
        else if (_candidateBaselineId != 0)
        {
            acknowledgement = RemoteWorldStateApplyStatus.Rejected;
            SaturatingIncrement(ref _rejectedBundles);
        }
        else
        {
            RemoteWorldStateClientApplyStatus applied = ApplyDecodedBundle(_records.AsSpan(0, count));
            acknowledgement = applied switch
            {
                RemoteWorldStateClientApplyStatus.Applied or RemoteWorldStateClientApplyStatus.Duplicate =>
                    RemoteWorldStateApplyStatus.Applied,
                RemoteWorldStateClientApplyStatus.CapacityExceeded =>
                    RemoteWorldStateApplyStatus.CapacityExceeded,
                _ => RemoteWorldStateApplyStatus.Rejected,
            };
        }
        _records.AsSpan(0, count).Clear();
        QueueAcknowledgement(in header, acknowledgement);
        return true;
    }

    /// <summary>Atomically applies one already-decoded complete bundle.</summary>
    public RemoteWorldStateClientApplyStatus ApplyDecodedBundle(ReadOnlySpan<RemoteWorldStateRecord> records)
    {
        EnsureOwnerThread();
        if (_faulted || records.IsEmpty || records.Length > _records.Length)
            return RemoteWorldStateClientApplyStatus.Rejected;
        RemoteWorldStateClientApplyStatus preflight = Preflight(records, out bool anyChange);
        if (preflight != RemoteWorldStateClientApplyStatus.Applied) return preflight;
        if (!anyChange)
        {
            SaturatingIncrement(ref _duplicateBundles);
            return RemoteWorldStateClientApplyStatus.Duplicate;
        }

        // Free capacity before allocating it. Records are unique by canonical
        // identity within a bundle, so this order is semantically equivalent and
        // makes the preflight capacity proof independent of wire record ordering.
        for (int index = 0; index < records.Length; index++)
            if (!records[index].IsPresent) ApplyRecord(in records[index]);
        for (int index = 0; index < records.Length; index++)
            if (records[index].IsPresent) ApplyRecord(in records[index]);
        SaturatingIncrement(ref _appliedBundles);
        return RemoteWorldStateClientApplyStatus.Applied;
    }

    /// <summary>Retries bounded application proofs after protocol backpressure.</summary>
    public bool TryFlushAcknowledgements()
    {
        EnsureOwnerThread();
        ResetFlushOpportunity();
        if (_hasPendingBaselineAcknowledgement)
        {
            if (!TryQueueBaselineAcknowledgement(in _pendingBaselineAcknowledgement)) return false;
            _pendingBaselineAcknowledgement = default;
            _hasPendingBaselineAcknowledgement = false;
        }
        while (_ackCount != 0)
        {
            RemoteWorldStateAppliedMessage acknowledgement = _pendingAcknowledgements[_ackHead];
            if (!TryQueueAcknowledgement(in acknowledgement)) return false;
            _pendingAcknowledgements[_ackHead] = default;
            _ackHead = (_ackHead + 1) % _pendingAcknowledgements.Length;
            _ackCount--;
        }
        return true;
    }

    bool IRemoteGameProtocolProgressor.TryProgress() => TryFlushAcknowledgements();

    /// <summary>Enables one benchmark-only acknowledgement census epoch.</summary>
    public void ResetAcknowledgementCensus(bool enabled)
    {
        EnsureOwnerThread();
        _acknowledgementCensusEnabled = enabled;
        ResetFlushOpportunity();
    }

    public RemoteWorldStateAcknowledgementFlushOpportunity InspectAcknowledgementFlushOpportunity()
    {
        EnsureOwnerThread();
        return new(
            _acknowledgementsProducedSinceFlush,
            _acknowledgementBytesProducedSinceFlush,
            _ackCount,
            _appliedAcknowledgementsSinceFlush,
            _rejectedAcknowledgementsSinceFlush,
            _capacityAcknowledgementsSinceFlush);
    }

    public int CopyActiveEntities(Span<RemoteWorldEntitySnapshot> destination)
    {
        EnsureOwnerThread();
        int count = CopyActiveEntitiesUnordered(destination);
        destination[..count].Sort(static (left, right) =>
        {
            int index = left.Entity.Index.CompareTo(right.Entity.Index);
            return index != 0 ? index : left.Entity.Generation.CompareTo(right.Entity.Generation);
        });
        return count;
    }

    /// <summary>
    /// Copies active projection rows in private dense-slot order for cold diagnostics
    /// that do not require canonical ordering. This avoids making metric collection
    /// sort every recipient projection; callers must not treat the order as identity.
    /// </summary>
    public int CopyActiveEntitiesUnordered(Span<RemoteWorldEntitySnapshot> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _activeCount)
            throw new ArgumentException("Destination cannot contain every active client entity.", nameof(destination));
        int count = 0;
        for (int index = 0; index < _activeHigh; index++)
            if (_active[index].Slot.IsValid) destination[count++] = _active[index];
        return count;
    }

    /// <summary>
    /// Positively selects already-known presentation candidates without changing
    /// server interest or causing network lifecycle work.
    /// </summary>
    public int CopyPresentationCandidates(
        in ClientWorldPoint center,
        int radiusMillimeters,
        Span<ClientEntitySlot> destination)
    {
        EnsureOwnerThread();
        if (radiusMillimeters < 0) throw new ArgumentOutOfRangeException(nameof(radiusMillimeters));
        int count = 0;
        for (int index = 0; index < _activeHigh; index++)
        {
            ref readonly RemoteWorldEntitySnapshot entity = ref _active[index];
            if (!entity.Slot.IsValid || !WithinSquaredDistance(
                    center,
                    new ClientWorldPoint(entity.XMillimeters, entity.ZMillimeters),
                    radiusMillimeters)) continue;
            if (count == destination.Length)
                throw new ArgumentException("Destination cannot contain every presentation candidate.", nameof(destination));
            destination[count++] = entity.Slot;
        }
        return count;
    }

    public RemoteWorldStateClientSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        long bytes = checked(
            (long)_active.Length * Unsafe.SizeOf<RemoteWorldEntitySnapshot>() +
            (long)_slotGenerations.Length * sizeof(uint) +
            (long)_freeSlots.Length * sizeof(int) +
            (long)_records.Length * Unsafe.SizeOf<RemoteWorldStateRecord>() +
            (long)_pendingAcknowledgements.Length * Unsafe.SizeOf<RemoteWorldStateAppliedMessage>() +
            (long)_candidateActive.Length * Unsafe.SizeOf<RemoteWorldEntitySnapshot>() +
            (long)_candidateSlotGenerations.Length * sizeof(uint) +
            (long)_candidateFreeSlots.Length * sizeof(int) +
            (long)_baselineRecords.Length * Unsafe.SizeOf<RemoteWorldStateRecord>() +
            _baselineSegments.Length +
            _ackScratch.Length +
            2L * _knowledgeMapCapacity *
                (sizeof(int) * 3L + sizeof(uint) + Unsafe.SizeOf<KnowledgeState>()));
        return new(
            _stateEpoch,
            _baselineRevision,
            _eventFloor,
            _relationSequenceFloor,
            _knowledge.Count,
            _activeCount,
            _ackCount,
            _appliedBundles,
            _duplicateBundles,
            _rejectedBundles,
            _appliedBaselines,
            _rejectedBaselines,
            _candidateBaselineId != 0,
            _faulted,
            bytes);
    }

    private void ProcessBaselineBegin(ReadOnlySpan<byte> payload)
    {
        if (_faulted || !RemoteWorldBaselineBeginMessageCodec.TryReadExact(
                payload, out RemoteWorldBaselineBeginMessage begin) ||
            begin.ApplicationSessionEpoch != _applicationSessionEpoch || begin.StateEpoch == 0 ||
            begin.BaselineId == 0 || begin.BaselineRevision == 0)
        {
            SaturatingIncrement(ref _rejectedBaselines);
            return;
        }
        if (_candidateBaselineId == begin.BaselineId &&
            _candidateStateEpoch.Value == begin.StateEpoch &&
            _candidateBaselineRevision == begin.BaselineRevision)
        {
            QueueBaselineAcknowledgement(RemoteWorldBaselineApplyStatus.Prepared);
            return;
        }
        bool staleLive = (_stateEpoch.IsValid && begin.StateEpoch < _stateEpoch.Value) ||
            (begin.StateEpoch == _stateEpoch.Value && begin.BaselineRevision <= _baselineRevision);
        bool staleCandidate = _candidateBaselineId != 0 &&
            (begin.StateEpoch < _candidateStateEpoch.Value ||
             (begin.StateEpoch == _candidateStateEpoch.Value &&
              begin.BaselineRevision <= _candidateBaselineRevision));
        bool regressiveLiveFloor = begin.StateEpoch == _stateEpoch.Value &&
            (begin.RelationSequenceFloor < _relationSequenceFloor ||
             begin.EventFloor < _eventFloor);
        bool regressiveCandidateFloor = _candidateBaselineId != 0 &&
            begin.StateEpoch == _candidateStateEpoch.Value &&
            (begin.RelationSequenceFloor < _candidateRelationSequenceFloor ||
             begin.EventFloor < _candidateEventFloor);
        if (staleLive || staleCandidate || regressiveLiveFloor || regressiveCandidateFloor)
        {
            SaturatingIncrement(ref _rejectedBaselines);
            QueueRawBaselineAcknowledgement(in begin, RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        bool invalidShape = begin.SchemaFingerprint !=
                UltraSimGameProtocol.WorldStateBaselineSchemaFingerprint ||
            begin.RecordsPerSegment == 0 || begin.RecordsPerSegment > _records.Length ||
            begin.RecordCount > _baselineRecords.Length ||
            begin.RecordCount > _candidateActive.Length ||
            (begin.RecordCount != 0 && begin.RelationSequenceFloor == 0) ||
            begin.SegmentCount != (begin.RecordCount + begin.RecordsPerSegment - 1) /
                begin.RecordsPerSegment;
        if (invalidShape)
        {
            SaturatingIncrement(ref _rejectedBaselines);
            QueueRawBaselineAcknowledgement(in begin,
                begin.RecordCount > _baselineRecords.Length ||
                begin.RecordCount > _candidateActive.Length
                    ? RemoteWorldBaselineApplyStatus.CapacityExceeded
                    : RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }

        RetirePendingBaselineAcknowledgement();
        ClearCandidate();
        _candidateBaselineId = begin.BaselineId;
        _candidateBaselineRevision = begin.BaselineRevision;
        _candidateEventFloor = begin.EventFloor;
        _candidateRelationSequenceFloor = begin.RelationSequenceFloor;
        _candidateHash = begin.ContentHash;
        _candidateStateEpoch = new StateEpoch(begin.StateEpoch);
        _candidateRecordCount = checked((int)begin.RecordCount);
        _candidateSegmentCount = begin.SegmentCount;
        _candidateRecordsPerSegment = begin.RecordsPerSegment;
        QueueBaselineAcknowledgement(RemoteWorldBaselineApplyStatus.Prepared);
    }

    private void ProcessBaselineSegment(ReadOnlySpan<byte> payload)
    {
        if (_faulted || _candidateBaselineId == 0) return;
        if (!UltraSimGameProtocol.TryReadWorldBaselineSegment(
                payload, out RemoteWorldBaselineSegmentHeader header,
                _records, out int count))
        {
            SaturatingIncrement(ref _rejectedBaselines);
            RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        if (header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            header.StateEpoch != _candidateStateEpoch.Value ||
            header.BaselineId != _candidateBaselineId)
        {
            _records.AsSpan(0, count).Clear();
            SaturatingIncrement(ref _rejectedBaselines);
            return;
        }
        if (header.SegmentCount != _candidateSegmentCount)
        {
            _records.AsSpan(0, count).Clear();
            RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        int offset = checked(header.SegmentIndex * _candidateRecordsPerSegment);
        int expected = Math.Min(_candidateRecordsPerSegment, _candidateRecordCount - offset);
        if (offset < 0 || expected != count)
        {
            _records.AsSpan(0, count).Clear();
            RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        if (_baselineSegments[header.SegmentIndex])
        {
            if (!_records.AsSpan(0, count).SequenceEqual(
                    _baselineRecords.AsSpan(offset, count)))
                RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            _records.AsSpan(0, count).Clear();
            return;
        }
        _records.AsSpan(0, count).CopyTo(_baselineRecords.AsSpan(offset, count));
        _records.AsSpan(0, count).Clear();
        _baselineSegments[header.SegmentIndex] = true;
        _receivedSegmentCount++;
        TryCommitCandidate();
    }

    private void ProcessBaselineCommit(ReadOnlySpan<byte> payload)
    {
        if (_faulted || _candidateBaselineId == 0) return;
        if (!RemoteWorldBaselineCommitMessageCodec.TryReadExact(
                payload, out RemoteWorldBaselineCommitMessage commit))
        {
            SaturatingIncrement(ref _rejectedBaselines);
            RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        if (commit.ApplicationSessionEpoch != _applicationSessionEpoch ||
            commit.StateEpoch != _candidateStateEpoch.Value ||
            commit.BaselineId != _candidateBaselineId)
        {
            SaturatingIncrement(ref _rejectedBaselines);
            return;
        }
        if (commit.BaselineRevision != _candidateBaselineRevision ||
            commit.ContentHash != _candidateHash)
        {
            RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        _candidateCommitReceived = true;
        TryCommitCandidate();
    }

    private void ProcessBaselineCancel(ReadOnlySpan<byte> payload)
    {
        if (!RemoteWorldBaselineCancelMessageCodec.TryReadExact(
                payload, out RemoteWorldBaselineCancelMessage cancel) ||
            cancel.ApplicationSessionEpoch != _applicationSessionEpoch ||
            cancel.StateEpoch == 0 || cancel.BaselineId == 0)
        {
            SaturatingIncrement(ref _rejectedBaselines);
            return;
        }
        bool candidateMatches = _candidateBaselineId != 0 &&
            cancel.StateEpoch == _candidateStateEpoch.Value &&
            cancel.BaselineId == _candidateBaselineId;
        RetirePendingBaselineAcknowledgement(cancel.StateEpoch, cancel.BaselineId);
        if (candidateMatches) ClearCandidate();
    }

    private void TryCommitCandidate()
    {
        if (!_candidateCommitReceived || _receivedSegmentCount != _candidateSegmentCount) return;
        ReadOnlySpan<RemoteWorldStateRecord> records =
            _baselineRecords.AsSpan(0, _candidateRecordCount);
        if (UltraSimGameProtocol.ComputeWorldBaselineHash(records) != _candidateHash)
        {
            RejectCandidate(RemoteWorldBaselineApplyStatus.Rejected);
            return;
        }
        _candidateKnowledge.Clear();
        Array.Clear(_candidateActive);
        Array.Copy(_slotGenerations, _candidateSlotGenerations, _slotGenerations.Length);
        Array.Clear(_candidateFreeSlots);
        uint priorIndex = 0;
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly RemoteWorldStateRecord record = ref records[index];
            if (!record.IsPresent || record.RelationSequence == 0 || record.EntityGeneration == 0 ||
                record.CanonicalVersion == 0 || record.Horizon is 0 or > 3 ||
                record.RelationSequence > _candidateRelationSequenceFloor ||
                (index != 0 && record.EntityIndex <= priorIndex) || index >= _candidateActive.Length ||
                _candidateSlotGenerations[index] == uint.MaxValue)
            {
                RejectCandidate(index >= _candidateActive.Length
                    ? RemoteWorldBaselineApplyStatus.CapacityExceeded
                    : RemoteWorldBaselineApplyStatus.Rejected);
                return;
            }
            uint localGeneration = ++_candidateSlotGenerations[index];
            var slot = new ClientEntitySlot(checked((uint)index), localGeneration);
            _candidateActive[index] = new RemoteWorldEntitySnapshot(
                new ReplicationEntityId(record.EntityIndex, record.EntityGeneration),
                slot,
                record.CanonicalVersion,
                record.Horizon,
                record.XMillimeters,
                record.ZMillimeters,
                record.TickMarker,
                record.InputVelocity);
            _candidateKnowledge.Add(record.EntityIndex, new KnowledgeState
            {
                EntityGeneration = record.EntityGeneration,
                RelationSequence = record.RelationSequence,
                CanonicalVersion = record.CanonicalVersion,
                Present = true,
                SlotIndex = index,
                LocalSlotGeneration = localGeneration,
            });
            priorIndex = record.EntityIndex;
        }

        int candidateActiveHigh = _candidateRecordCount;
        int candidateFreeSlotCount = 0;
        for (int index = _candidateRecordCount; index < _candidateSlotGenerations.Length; index++)
        {
            uint generation = _candidateSlotGenerations[index];
            if (generation == 0) continue;
            candidateActiveHigh = index + 1;
            if (generation != uint.MaxValue)
                _candidateFreeSlots[candidateFreeSlotCount++] = index;
        }

        (_knowledge, _candidateKnowledge) = (_candidateKnowledge, _knowledge);
        (_active, _candidateActive) = (_candidateActive, _active);
        (_slotGenerations, _candidateSlotGenerations) =
            (_candidateSlotGenerations, _slotGenerations);
        (_freeSlots, _candidateFreeSlots) = (_candidateFreeSlots, _freeSlots);
        _activeHigh = candidateActiveHigh;
        _activeCount = _candidateRecordCount;
        _freeSlotCount = candidateFreeSlotCount;
        _stateEpoch = _candidateStateEpoch;
        _baselineRevision = _candidateBaselineRevision;
        _eventFloor = _candidateEventFloor;
        _relationSequenceFloor = _candidateRelationSequenceFloor;
        SaturatingIncrement(ref _appliedBaselines);
        QueueBaselineAcknowledgement(RemoteWorldBaselineApplyStatus.Applied);
        ClearCandidate();
    }

    private void RejectCandidate(RemoteWorldBaselineApplyStatus status)
    {
        SaturatingIncrement(ref _rejectedBaselines);
        QueueBaselineAcknowledgement(status);
        ClearCandidate();
    }

    private void QueueBaselineAcknowledgement(RemoteWorldBaselineApplyStatus status)
    {
        if (_candidateBaselineId == 0) return;
        var acknowledgement = new RemoteWorldBaselineAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = _candidateStateEpoch.Value,
            BaselineId = _candidateBaselineId,
            BaselineRevision = _candidateBaselineRevision,
            Status = status,
        };
        if (_hasPendingBaselineAcknowledgement)
        {
            if (!BaselineAcknowledgementsEqual(
                    in _pendingBaselineAcknowledgement, in acknowledgement))
            {
                _faulted = true;
                return;
            }
            if (TryQueueBaselineAcknowledgement(in _pendingBaselineAcknowledgement))
                RetirePendingBaselineAcknowledgement();
            return;
        }
        if (TryQueueBaselineAcknowledgement(in acknowledgement)) return;
        _pendingBaselineAcknowledgement = acknowledgement;
        _hasPendingBaselineAcknowledgement = true;
    }

    private void QueueRawBaselineAcknowledgement(
        in RemoteWorldBaselineBeginMessage begin,
        RemoteWorldBaselineApplyStatus status)
    {
        var acknowledgement = new RemoteWorldBaselineAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = begin.StateEpoch,
            BaselineId = begin.BaselineId,
            BaselineRevision = begin.BaselineRevision,
            Status = status,
        };
        if (_hasPendingBaselineAcknowledgement)
        {
            if (!BaselineAcknowledgementsEqual(
                    in _pendingBaselineAcknowledgement, in acknowledgement))
            {
                _faulted = true;
                return;
            }
            if (TryQueueBaselineAcknowledgement(in _pendingBaselineAcknowledgement))
                RetirePendingBaselineAcknowledgement();
            return;
        }
        if (TryQueueBaselineAcknowledgement(in acknowledgement)) return;
        _pendingBaselineAcknowledgement = acknowledgement;
        _hasPendingBaselineAcknowledgement = true;
    }

    private bool TryQueueBaselineAcknowledgement(
        in RemoteWorldBaselineAppliedMessage acknowledgement)
    {
        int bytes = RemoteWorldBaselineAppliedMessageCodec.EncodedSize;
        return RemoteWorldBaselineAppliedMessageCodec.TryWriteExact(
                _ackScratch.AsSpan(0, bytes), in acknowledgement) &&
            _peer.TryQueue(RemoteWorldBaselineAppliedMessageCodec.Descriptor.Id,
                _ackScratch.AsSpan(0, bytes)) == NetworkQueueStatus.Accepted;
    }

    private static bool BaselineAcknowledgementsEqual(
        in RemoteWorldBaselineAppliedMessage left,
        in RemoteWorldBaselineAppliedMessage right) =>
        left.ApplicationSessionEpoch == right.ApplicationSessionEpoch &&
        left.StateEpoch == right.StateEpoch &&
        left.BaselineId == right.BaselineId &&
        left.BaselineRevision == right.BaselineRevision &&
        left.Status == right.Status;

    private void RetirePendingBaselineAcknowledgement()
    {
        _pendingBaselineAcknowledgement = default;
        _hasPendingBaselineAcknowledgement = false;
    }

    private void RetirePendingBaselineAcknowledgement(ulong stateEpoch, ulong baselineId)
    {
        if (_hasPendingBaselineAcknowledgement &&
            _pendingBaselineAcknowledgement.ApplicationSessionEpoch == _applicationSessionEpoch &&
            _pendingBaselineAcknowledgement.StateEpoch == stateEpoch &&
            _pendingBaselineAcknowledgement.BaselineId == baselineId)
            RetirePendingBaselineAcknowledgement();
    }

    private void ClearCandidate()
    {
        _baselineRecords.AsSpan(0, _candidateRecordCount).Clear();
        _baselineSegments.AsSpan(0, _candidateSegmentCount).Clear();
        _candidateKnowledge.Clear();
        Array.Clear(_candidateActive);
        _candidateBaselineId = 0;
        _candidateBaselineRevision = 0;
        _candidateEventFloor = 0;
        _candidateRelationSequenceFloor = 0;
        _candidateHash = 0;
        _candidateStateEpoch = default;
        _candidateRecordCount = 0;
        _candidateSegmentCount = 0;
        _candidateRecordsPerSegment = 0;
        _receivedSegmentCount = 0;
        _candidateCommitReceived = false;
    }

    private RemoteWorldStateClientApplyStatus Preflight(
        ReadOnlySpan<RemoteWorldStateRecord> records,
        out bool anyChange)
    {
        anyChange = false;
        int newKnowledge = 0;
        int slotsNeeded = 0;
        int slotsFreed = 0;
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly RemoteWorldStateRecord record = ref records[index];
            if (record.RelationSequence == 0 || record.EntityGeneration == 0 ||
                record.CanonicalVersion == 0 || record.Horizon > 3 ||
                (record.IsPresent ? record.Horizon == 0 :
                    record.Horizon != 0 || record.XMillimeters != 0 || record.ZMillimeters != 0 ||
                    record.TickMarker != 0 || record.InputVelocity != 0))
                return RemoteWorldStateClientApplyStatus.Rejected;
            for (int prior = 0; prior < index; prior++)
                if (records[prior].EntityIndex == record.EntityIndex)
                    return RemoteWorldStateClientApplyStatus.Rejected;
            if (record.RelationSequence <= _relationSequenceFloor)
                continue;

            if (!_knowledge.TryGetValue(record.EntityIndex, out KnowledgeState knowledge))
            {
                newKnowledge++;
                if (record.IsPresent) slotsNeeded++;
                anyChange = true;
                continue;
            }
            if (record.RelationSequence <= knowledge.RelationSequence) continue;
            if (record.EntityGeneration < knowledge.EntityGeneration ||
                (record.EntityGeneration == knowledge.EntityGeneration &&
                 record.CanonicalVersion < knowledge.CanonicalVersion))
                return RemoteWorldStateClientApplyStatus.Rejected;
            if (record.EntityGeneration > knowledge.EntityGeneration && knowledge.Present)
            {
                if (record.IsPresent)
                {
                    if (_slotGenerations[knowledge.SlotIndex] == uint.MaxValue)
                        return RemoteWorldStateClientApplyStatus.CapacityExceeded;
                }
                else
                {
                    slotsFreed++;
                }
            }
            else if (record.IsPresent && !knowledge.Present)
            {
                slotsNeeded++;
            }
            else if (!record.IsPresent && knowledge.Present)
            {
                slotsFreed++;
            }
            anyChange = true;
        }
        if (_knowledge.Count + newKnowledge > _maximumKnownIdentities)
            return RemoteWorldStateClientApplyStatus.CapacityExceeded;
        int available = _freeSlotCount + (_active.Length - _activeHigh) + slotsFreed;
        if (slotsNeeded > available) return RemoteWorldStateClientApplyStatus.CapacityExceeded;
        return RemoteWorldStateClientApplyStatus.Applied;
    }

    private int AllocateActiveSlot()
    {
        int slot;
        while (_freeSlotCount != 0)
        {
            slot = _freeSlots[--_freeSlotCount];
            if (_slotGenerations[slot] == uint.MaxValue) continue;
            _slotGenerations[slot]++;
            _activeCount++;
            return slot;
        }
        if (_activeHigh == _active.Length)
            throw new InvalidOperationException("Client slot preflight diverged from commit.");
        slot = _activeHigh++;
        if (_slotGenerations[slot] == uint.MaxValue)
            throw new InvalidOperationException("Client slot preflight admitted a retired slot.");
        _slotGenerations[slot] = _slotGenerations[slot] == 0
            ? 1
            : checked(_slotGenerations[slot] + 1);
        _activeCount++;
        return slot;
    }

    private void ApplyRecord(in RemoteWorldStateRecord record)
    {
        bool exists = _knowledge.TryGetValue(record.EntityIndex, out KnowledgeState knowledge);
        if (exists && record.RelationSequence <= knowledge.RelationSequence) return;
        if (!exists)
        {
            knowledge = new KnowledgeState { SlotIndex = -1 };
            _knowledge.Add(record.EntityIndex, knowledge);
        }

        bool newCanonicalLifetime = knowledge.EntityGeneration != 0 &&
            record.EntityGeneration > knowledge.EntityGeneration;
        if (newCanonicalLifetime && knowledge.Present)
        {
            if (record.IsPresent)
            {
                int slot = knowledge.SlotIndex;
                uint generation = checked(_slotGenerations[slot] + 1);
                _slotGenerations[slot] = generation;
                knowledge.LocalSlotGeneration = generation;
            }
            else
            {
                ReleaseActiveSlot(ref knowledge);
            }
        }
        else if (record.IsPresent && !knowledge.Present)
        {
            int slot = AllocateActiveSlot();
            knowledge.SlotIndex = slot;
            knowledge.LocalSlotGeneration = _slotGenerations[slot];
            knowledge.Present = true;
        }
        else if (!record.IsPresent && knowledge.Present)
        {
            ReleaseActiveSlot(ref knowledge);
        }

        knowledge.EntityGeneration = record.EntityGeneration;
        knowledge.RelationSequence = record.RelationSequence;
        knowledge.CanonicalVersion = record.CanonicalVersion;
        knowledge.Present = record.IsPresent;
        if (record.IsPresent)
        {
            int slot = knowledge.SlotIndex;
            _active[slot] = new RemoteWorldEntitySnapshot(
                new ReplicationEntityId(record.EntityIndex, record.EntityGeneration),
                new ClientEntitySlot(checked((uint)slot), knowledge.LocalSlotGeneration),
                record.CanonicalVersion,
                record.Horizon,
                record.XMillimeters,
                record.ZMillimeters,
                record.TickMarker,
                record.InputVelocity);
        }
        _knowledge[record.EntityIndex] = knowledge;
    }

    private void ReleaseActiveSlot(ref KnowledgeState knowledge)
    {
        int slot = knowledge.SlotIndex;
        _active[slot] = default;
        knowledge.Present = false;
        knowledge.SlotIndex = -1;
        knowledge.LocalSlotGeneration = 0;
        _activeCount--;
        if (_slotGenerations[slot] != uint.MaxValue)
            _freeSlots[_freeSlotCount++] = slot;
    }

    private void QueueAcknowledgement(
        in RemoteWorldStateBundleHeader header,
        RemoteWorldStateApplyStatus status)
    {
        var acknowledgement = new RemoteWorldStateAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = header.StateEpoch,
            BundleId = header.BundleId,
            Status = status,
        };
        if (_acknowledgementCensusEnabled)
        {
            _acknowledgementsProducedSinceFlush++;
            _acknowledgementBytesProducedSinceFlush = checked(
                _acknowledgementBytesProducedSinceFlush +
                RemoteWorldStateAppliedMessageCodec.EncodedSize);
            switch (status)
            {
                case RemoteWorldStateApplyStatus.Applied:
                    _appliedAcknowledgementsSinceFlush++;
                    break;
                case RemoteWorldStateApplyStatus.Rejected:
                    _rejectedAcknowledgementsSinceFlush++;
                    break;
                case RemoteWorldStateApplyStatus.CapacityExceeded:
                    _capacityAcknowledgementsSinceFlush++;
                    break;
            }
        }
        if (TryQueueAcknowledgement(in acknowledgement)) return;
        if (_ackCount == _pendingAcknowledgements.Length)
        {
            _faulted = true;
            return;
        }
        int index = (_ackHead + _ackCount) % _pendingAcknowledgements.Length;
        _pendingAcknowledgements[index] = acknowledgement;
        _ackCount++;
    }

    private bool TryQueueAcknowledgement(in RemoteWorldStateAppliedMessage acknowledgement)
    {
        if (!RemoteWorldStateAppliedMessageCodec.TryWriteExact(
                _ackScratch.AsSpan(0, RemoteWorldStateAppliedMessageCodec.EncodedSize),
                in acknowledgement))
            throw new InvalidOperationException("A validated WorldState acknowledgement did not encode.");
        return _peer.TryQueue(
            RemoteWorldStateAppliedMessageCodec.Descriptor.Id,
            _ackScratch.AsSpan(0, RemoteWorldStateAppliedMessageCodec.EncodedSize)) ==
            NetworkQueueStatus.Accepted;
    }

    private void ResetFlushOpportunity()
    {
        _acknowledgementsProducedSinceFlush = 0;
        _acknowledgementBytesProducedSinceFlush = 0;
        _appliedAcknowledgementsSinceFlush = 0;
        _rejectedAcknowledgementsSinceFlush = 0;
        _capacityAcknowledgementsSinceFlush = 0;
    }

    private static bool WithinSquaredDistance(
        in ClientWorldPoint left,
        in ClientWorldPoint right,
        int radius)
    {
        Int128 dx = (Int128)left.XMillimeters - right.XMillimeters;
        Int128 dz = (Int128)left.ZMillimeters - right.ZMillimeters;
        Int128 r = radius;
        return dx * dx + dz * dz <= r * r;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote WorldState client is owner-thread-bound.");
    }

    private struct KnowledgeState
    {
        internal uint EntityGeneration;
        internal ulong RelationSequence;
        internal ulong CanonicalVersion;
        internal bool Present;
        internal int SlotIndex;
        internal uint LocalSlotGeneration;
    }
}
