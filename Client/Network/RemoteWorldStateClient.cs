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
    int KnownIdentityCount,
    int ActiveEntityCount,
    int PendingApplicationAcknowledgements,
    ulong AppliedBundles,
    ulong DuplicateBundles,
    ulong RejectedBundles,
    bool IsFaulted,
    long ReservedBytes);

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
public sealed class RemoteWorldStateClient : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly StateEpoch _stateEpoch;
    private readonly int _maximumKnownIdentities;
    private readonly Dictionary<uint, KnowledgeState> _knowledge;
    private readonly RemoteWorldEntitySnapshot[] _active;
    private readonly uint[] _slotGenerations;
    private readonly int[] _freeSlots;
    private readonly RemoteWorldStateRecord[] _records;
    private readonly RemoteWorldStateAppliedMessage[] _pendingAcknowledgements;
    private readonly byte[] _ackScratch = new byte[RemoteWorldStateAppliedMessageCodec.EncodedSize];
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
    private bool _faulted;

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
        _records = new RemoteWorldStateRecord[maximumRecordsPerBundle];
        _pendingAcknowledgements = new RemoteWorldStateAppliedMessage[maximumPendingAcknowledgements];
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
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

    /// <summary>Retries bounded application acknowledgements after Control backpressure.</summary>
    public bool TryFlushAcknowledgements()
    {
        EnsureOwnerThread();
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

    public int CopyActiveEntities(Span<RemoteWorldEntitySnapshot> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _activeCount)
            throw new ArgumentException("Destination cannot contain every active client entity.", nameof(destination));
        int count = 0;
        for (int index = 0; index < _activeHigh; index++)
            if (_active[index].Slot.IsValid) destination[count++] = _active[index];
        destination[..count].Sort(static (left, right) =>
        {
            int index = left.Entity.Index.CompareTo(right.Entity.Index);
            return index != 0 ? index : left.Entity.Generation.CompareTo(right.Entity.Generation);
        });
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
            (long)_knowledgeMapCapacity * (sizeof(int) * 3L + sizeof(uint) + Unsafe.SizeOf<KnowledgeState>()));
        return new(
            _stateEpoch,
            _knowledge.Count,
            _activeCount,
            _ackCount,
            _appliedBundles,
            _duplicateBundles,
            _rejectedBundles,
            _faulted,
            bytes);
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
        _slotGenerations[slot] = 1;
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
        if (!RemoteWorldStateAppliedMessageCodec.TryWriteExact(_ackScratch, in acknowledgement))
            throw new InvalidOperationException("A validated WorldState acknowledgement did not encode.");
        return _peer.TryQueue(
            RemoteWorldStateAppliedMessageCodec.Descriptor.Id,
            _ackScratch) == NetworkQueueStatus.Accepted;
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
