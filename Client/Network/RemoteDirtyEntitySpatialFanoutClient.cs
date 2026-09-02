using System.Runtime.CompilerServices;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;

namespace UltraSim.Client.Network;

/// <summary>Client-side bounded adapter for the Game-owned Entity Dirty Push wire family.</summary>
public readonly record struct RemoteDirtyEntitySpatialFanoutClientSnapshot(
    StateEpoch StateEpoch,
    int KnownIdentityCount,
    int ActiveEntityCount,
    int PendingApplicationProofs,
    ulong AppliedBundles,
    ulong RejectedBundles,
    bool IsFaulted,
    long ReservedBytes);

/// <summary>
/// Reuses the accepted WorldState semantic application/cache machinery. The dirty profile changes
/// server discovery and debt, while generation fencing and application-proof semantics remain the
/// same client responsibility.
/// </summary>
public sealed class RemoteDirtyEntitySpatialFanoutClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private RemoteWorldStateClient _semantic;
    private readonly int _maximumKnownIdentities;
    private readonly int _maximumActiveEntities;
    private readonly int _maximumRecordsPerBundle;
    private readonly RemoteDirtyEntityRecord[] _wireRecords;
    private readonly RemoteWorldStateRecord[] _semanticRecords;
    private readonly RemoteDirtyEntityStateAppliedMessage[] _pendingProofs;
    private readonly byte[] _proofScratch = new byte[RemoteDirtyEntityStateAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _proofHead;
    private int _proofCount;
    private ulong _appliedBundles;
    private ulong _rejectedBundles;
    private bool _faulted;

    public RemoteDirtyEntitySpatialFanoutClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        StateEpoch stateEpoch,
        int maximumKnownIdentities,
        int maximumActiveEntities,
        int maximumRecordsPerBundle = 32,
        int maximumPendingProofs = 64)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (!stateEpoch.IsValid) throw new ArgumentException("A valid state epoch is required.", nameof(stateEpoch));
        if (maximumRecordsPerBundle <= 0 || maximumRecordsPerBundle > UltraSimGameProtocol.MaximumDirtyEntityRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecordsPerBundle));
        if (maximumPendingProofs <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPendingProofs));
        _applicationSessionEpoch = applicationSessionEpoch;
        _maximumKnownIdentities = maximumKnownIdentities;
        _maximumActiveEntities = maximumActiveEntities;
        _maximumRecordsPerBundle = maximumRecordsPerBundle;
        _semantic = CreateSemanticClient(stateEpoch);
        _wireRecords = new RemoteDirtyEntityRecord[maximumRecordsPerBundle];
        _semanticRecords = new RemoteWorldStateRecord[maximumRecordsPerBundle];
        _pendingProofs = new RemoteDirtyEntityStateAppliedMessage[maximumPendingProofs];
    }

    /// <summary>
    /// Replaces epoch-local client truth and pending proofs after an authoritative
    /// source epoch replacement. Old dirty bundles remain rejected by the epoch fence.
    /// </summary>
    public bool ReplaceStateEpoch(StateEpoch nextEpoch)
    {
        EnsureOwnerThread();
        if (_faulted || !nextEpoch.IsValid || nextEpoch.Value <= _semantic.GetSnapshot().StateEpoch.Value)
            return false;
        _semantic = CreateSemanticClient(nextEpoch);
        Array.Clear(_pendingProofs);
        _proofHead = 0;
        _proofCount = 0;
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId != RemoteDirtyEntityStateHeaderCodec.Descriptor.Id) return false;
        if (_faulted) return true;
        if (!UltraSimGameProtocol.TryReadDirtyEntityBundle(
                payload, out RemoteDirtyEntityStateHeader header, _wireRecords, out int count))
        {
            _rejectedBundles++;
            return true;
        }
        RemoteWorldStateApplyStatus status;
        if (header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            header.StateEpoch != _semantic.GetSnapshot().StateEpoch.Value)
        {
            status = RemoteWorldStateApplyStatus.Rejected;
            _rejectedBundles++;
        }
        else
        {
            for (int index = 0; index < count; index++)
            {
                ref readonly RemoteDirtyEntityRecord record = ref _wireRecords[index];
                _semanticRecords[index] = new(
                    record.DeliveryId, record.EntityIndex, record.EntityGeneration,
                    record.CanonicalVersion, record.IsPresent, record.Horizon,
                    record.XMillimeters, record.ZMillimeters,
                    record.TickMarker, record.InputVelocity);
            }
            RemoteWorldStateClientApplyStatus result = _semantic.ApplyDecodedBundle(_semanticRecords.AsSpan(0, count));
            status = result switch
            {
                RemoteWorldStateClientApplyStatus.Applied or RemoteWorldStateClientApplyStatus.Duplicate =>
                    RemoteWorldStateApplyStatus.Applied,
                RemoteWorldStateClientApplyStatus.CapacityExceeded => RemoteWorldStateApplyStatus.CapacityExceeded,
                _ => RemoteWorldStateApplyStatus.Rejected,
            };
            if (status == RemoteWorldStateApplyStatus.Applied) _appliedBundles++;
            else _rejectedBundles++;
        }
        _wireRecords.AsSpan(0, count).Clear();
        _semanticRecords.AsSpan(0, count).Clear();
        QueueProof(new RemoteDirtyEntityStateAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = header.StateEpoch,
            BundleId = header.BundleId,
            Status = status,
        });
        return true;
    }

    public bool TryFlushProofs()
    {
        EnsureOwnerThread();
        while (_proofCount != 0)
        {
            ref readonly RemoteDirtyEntityStateAppliedMessage proof = ref _pendingProofs[_proofHead];
            if (!RemoteDirtyEntityStateAppliedMessageCodec.TryWriteExact(
                    _proofScratch, in proof) ||
                _peer.TryQueue(RemoteDirtyEntityStateAppliedMessageCodec.Descriptor.Id,
                    _proofScratch) != NetworkQueueStatus.Accepted)
                return false;
            _pendingProofs[_proofHead] = default;
            _proofHead = (_proofHead + 1) % _pendingProofs.Length;
            _proofCount--;
        }
        return true;
    }

    bool IRemoteGameProtocolProgressor.TryProgress() => TryFlushProofs();

    public int CopyActiveEntities(Span<RemoteWorldEntitySnapshot> destination) =>
        _semantic.CopyActiveEntities(destination);

    public RemoteDirtyEntitySpatialFanoutClientSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        RemoteWorldStateClientSnapshot semantic = _semantic.GetSnapshot();
        return new(semantic.StateEpoch, semantic.KnownIdentityCount, semantic.ActiveEntityCount,
            _proofCount, _appliedBundles, _rejectedBundles, _faulted,
            checked(semantic.ReservedBytes + (long)_wireRecords.Length * Unsafe.SizeOf<RemoteDirtyEntityRecord>() +
                (long)_semanticRecords.Length * Unsafe.SizeOf<RemoteWorldStateRecord>() +
                (long)_pendingProofs.Length * Unsafe.SizeOf<RemoteDirtyEntityStateAppliedMessage>() +
                _proofScratch.Length));
    }

    private void QueueProof(in RemoteDirtyEntityStateAppliedMessage proof)
    {
        if (RemoteDirtyEntityStateAppliedMessageCodec.TryWriteExact(_proofScratch, in proof) &&
            _peer.TryQueue(RemoteDirtyEntityStateAppliedMessageCodec.Descriptor.Id, _proofScratch) == NetworkQueueStatus.Accepted)
            return;
        if (_proofCount == _pendingProofs.Length)
        {
            _faulted = true;
            return;
        }
        int index = (_proofHead + _proofCount) % _pendingProofs.Length;
        _pendingProofs[index] = proof;
        _proofCount++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The dirty entity client adapter is owner-thread-bound.");
    }

    private RemoteWorldStateClient CreateSemanticClient(StateEpoch stateEpoch) =>
        new(_peer, _applicationSessionEpoch, stateEpoch, _maximumKnownIdentities,
            _maximumActiveEntities, _maximumRecordsPerBundle, maximumPendingAcknowledgements: 1);
}
