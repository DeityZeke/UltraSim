using System.Runtime.CompilerServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteDirtyEntitySpatialFanoutServerSnapshot(
    ulong BundlesQueued,
    ulong ApplicationProofs,
    ulong ApplicationRejections,
    ulong MalformedProofs,
    DirtyEntityBuildStatus LastBuildStatus,
    DirtyEntityProcessStatus LastProcessStatus,
    long ReservedBytes);

/// <summary>
/// Connection-owned Game adapter for DirtyEntitySpatialFanout.  Network sees only a normal
/// WorldState-lane descriptor and bounded bytes; entity discovery and proof interpretation stay
/// here in Game.
/// </summary>
public sealed class RemoteDirtyEntitySpatialFanoutServer : IRemoteGameProtocolHandler
{
    public static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.DirtyEntitySpatialFanout;

    private readonly NetworkProtocolPeer _peer;
    private readonly DirtyEntitySpatialFanout _fanout;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly DirtyEntityDeliveryRecord[] _records;
    private readonly RemoteDirtyEntityRecord[] _wireRecords;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _bundlesQueued;
    private ulong _applicationProofs;
    private ulong _applicationRejections;
    private ulong _malformedProofs;
    private DirtyEntityBuildStatus _lastBuildStatus;
    private DirtyEntityProcessStatus _lastProcessStatus = DirtyEntityProcessStatus.NoData;

    public RemoteDirtyEntitySpatialFanoutServer(
        NetworkProtocolPeer peer,
        DirtyEntitySpatialFanout fanout,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        int maximumRecordsPerBundle = 32)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _fanout = fanout ?? throw new ArgumentNullException(nameof(fanout));
        if (!participant.IsValid) throw new ArgumentException("A valid participant is required.", nameof(participant));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (maximumRecordsPerBundle <= 0 || maximumRecordsPerBundle > UltraSimGameProtocol.MaximumDirtyEntityRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecordsPerBundle));
        _participant = participant;
        _applicationSessionEpoch = applicationSessionEpoch;
        _records = new DirtyEntityDeliveryRecord[maximumRecordsPerBundle];
        _wireRecords = new RemoteDirtyEntityRecord[maximumRecordsPerBundle];
    }

    public DirtyEntityBuildStatus TrySchedule(long nowMilliseconds, int byteBudget)
    {
        EnsureOwnerThread();
        _lastProcessStatus = _fanout.TryProcessDirtyEntities(nowMilliseconds, out _);
        if (_lastProcessStatus == DirtyEntityProcessStatus.CapacityExceeded)
        {
            _lastBuildStatus = DirtyEntityBuildStatus.SpatialCapacityExceeded;
            return _lastBuildStatus;
        }
        _lastBuildStatus = _fanout.TryBuildBundle(
            _participant, nowMilliseconds, byteBudget, _records, out DirtyEntityBundle bundle);
        if (_lastBuildStatus != DirtyEntityBuildStatus.Success) return _lastBuildStatus;
        for (int index = 0; index < bundle.RecordCount; index++)
        {
            ref readonly DirtyEntityDeliveryRecord record = ref _records[index];
            _wireRecords[index] = new(
                record.DeliveryId, record.Entity.Index, record.Entity.Generation,
                record.CanonicalVersion, record.IsPresent, (byte)record.Horizon,
                record.Position.XMillimeters, record.Position.ZMillimeters,
                record.SimulationState.TickMarker, record.SimulationState.InputVelocity);
        }
        if (!UltraSimGameProtocol.TryWriteDirtyEntityBundle(
                _messageScratch, _applicationSessionEpoch, bundle.StateEpoch.Value,
                bundle.DeliveryBundleId, _wireRecords.AsSpan(0, bundle.RecordCount), out int written) ||
            _peer.TryQueue(RemoteDirtyEntityStateHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
        {
            _fanout.AbandonBundle(bundle.DeliveryBundleId);
            _lastBuildStatus = DirtyEntityBuildStatus.TransportNotAccepting;
            return _lastBuildStatus;
        }
        _records.AsSpan(0, bundle.RecordCount).Clear();
        _wireRecords.AsSpan(0, bundle.RecordCount).Clear();
        if (_bundlesQueued != ulong.MaxValue) _bundlesQueued++;
        return _lastBuildStatus;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId != RemoteDirtyEntityStateAppliedMessageCodec.Descriptor.Id) return false;
        if (!RemoteDirtyEntityStateAppliedMessageCodec.TryReadExact(
                payload, out RemoteDirtyEntityStateAppliedMessage acknowledgement) ||
            acknowledgement.ApplicationSessionEpoch != _applicationSessionEpoch ||
            acknowledgement.StateEpoch != _fanout.StateEpoch.Value ||
            acknowledgement.BundleId == 0)
        {
            if (_malformedProofs != ulong.MaxValue) _malformedProofs++;
            return true;
        }
        bool applied = acknowledgement.Status == RemoteWorldStateApplyStatus.Applied;
        if (_fanout.TryAcknowledgeBundle(_participant, acknowledgement.BundleId, applied))
        {
            if (applied) _applicationProofs++; else _applicationRejections++;
        }
        return true;
    }

    public RemoteDirtyEntitySpatialFanoutServerSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_bundlesQueued, _applicationProofs, _applicationRejections,
            _malformedProofs, _lastBuildStatus, _lastProcessStatus, checked(sizeof(ulong) * 4L +
                (long)_records.Length * Unsafe.SizeOf<DirtyEntityDeliveryRecord>() +
                (long)_wireRecords.Length * Unsafe.SizeOf<RemoteDirtyEntityRecord>() +
                _messageScratch.Length));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The dirty entity remote adapter is owner-thread-bound.");
    }
}
