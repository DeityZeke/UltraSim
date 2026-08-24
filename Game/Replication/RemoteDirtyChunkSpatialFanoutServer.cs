using System.Runtime.CompilerServices;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteDirtyChunkSpatialFanoutServerSnapshot(
    ulong DeliveriesQueued,
    ulong BundlesQueued,
    ulong BundleEntriesQueued,
    ulong ApplicationProofs,
    ulong ApplicationRejections,
    ulong MalformedProofs,
    DirtyChunkBuildStatus LastBuildStatus,
    DirtyChunkProcessStatus LastProcessStatus,
    int OutstandingDeliveries,
    ulong DeliveryTimeouts,
    ulong SnapshotTimeouts,
    bool SnapshotTransferActive,
    bool SnapshotCancelPending,
    long ReservedBytes);

/// <summary>
/// Thin Game composition adapter for DirtyChunkSpatialFanout. It reuses the accepted chunk
/// snapshot/delta/absence messages and the profile-neutral RemoteChunkWorldStateClient; Network
/// never interprets dirty keys, chunk cursors, or interest tiers.
/// </summary>
public sealed class RemoteDirtyChunkSpatialFanoutServer : IRemoteGameProtocolHandler,
    IRemoteGameProtocolProgressor
{
    public static GamePublicReplicationProfileDescriptor PublicReplicationProfile =>
        GamePublicReplicationProfiles.DirtyChunkSpatialFanout;

    private readonly NetworkProtocolPeer _peer;
    private readonly DirtyChunkSpatialFanout _fanout;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly RemoteChunkPublicRecord[] _snapshotRecords;
    private readonly RemoteChunkDeltaRecord[] _deltaRecords;
    private readonly RemoteDirtyChunkBundleEntry[] _bundleEntries;
    private readonly byte[] _messageScratch;
    private readonly byte[] _snapshotPayload;
    private readonly DeliveryLineage[] _outstanding;
    private readonly int _snapshotSegmentPayloadBytes;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _outstandingCount;
    private bool _snapshotActive;
    private bool _snapshotBeginQueued;
    private RemoteChunkSnapshotHeader _snapshotHeader;
    private OfflineDirectChunkDeliveryTicket _snapshotTicket;
    private int _snapshotLength;
    private int _snapshotSegmentCount;
    private int _snapshotNextSegment;
    private ulong _snapshotStartedStep;
    private RemoteChunkSnapshotCancelMessage _pendingCancel;
    private ulong _deliveriesQueued;
    private ulong _bundlesQueued;
    private ulong _bundleEntriesQueued;
    private ulong _applicationProofs;
    private ulong _applicationRejections;
    private ulong _malformedProofs;
    private ulong _deliveryTimeouts;
    private ulong _snapshotTimeouts;
    private DirtyChunkBuildStatus _lastBuildStatus = DirtyChunkBuildStatus.NoData;
    private DirtyChunkProcessStatus _lastProcessStatus = DirtyChunkProcessStatus.NoData;

    public RemoteDirtyChunkSpatialFanoutServer(
        NetworkProtocolPeer peer,
        DirtyChunkSpatialFanout fanout,
        ParticipantId participant,
        ulong applicationSessionEpoch)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _fanout = fanout ?? throw new ArgumentNullException(nameof(fanout));
        if (!participant.IsValid) throw new ArgumentException("A valid participant is required.", nameof(participant));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        _participant = participant;
        _applicationSessionEpoch = applicationSessionEpoch;
        _snapshotRecords = new RemoteChunkPublicRecord[fanout.Limits.MaximumSnapshotRecords];
        _deltaRecords = new RemoteChunkDeltaRecord[fanout.Limits.MaximumDeltaRecords];
        _bundleEntries = new RemoteDirtyChunkBundleEntry[fanout.Limits.MaximumChunksPerBundle];
        _messageScratch = new byte[Math.Max(
            UltraSimGameProtocol.MaximumMessageBytes, peer.MaximumUnfragmentedMessageBytes)];
        _snapshotPayload = new byte[UltraSimGameProtocol.GetChunkSnapshotEncodedBytes(
            fanout.Limits.MaximumSnapshotRecords)];
        _outstanding = new DeliveryLineage[fanout.Limits.MaximumOutstandingDeliveries];
        _snapshotSegmentPayloadBytes = peer.MaximumUnfragmentedMessageBytes -
            RemoteChunkSnapshotSegmentHeaderCodec.EncodedSize;
        if (_snapshotSegmentPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(peer));
    }

    public DirtyChunkSpatialFanout Fanout => _fanout;

    public DirtyChunkBuildStatus TrySchedule(ulong cadenceStep)
    {
        EnsureOwner();
        ExpireOutstandingDeliveries(cadenceStep);
        _lastProcessStatus = _fanout.TryProcessDirtyChunks(cadenceStep, out _);
        if (_lastProcessStatus == DirtyChunkProcessStatus.CapacityExceeded)
        {
            _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
            return _lastBuildStatus;
        }
        if (_snapshotActive || _pendingCancel.DeliveryId != 0 ||
            _outstandingCount == _outstanding.Length)
        {
            _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
            return _lastBuildStatus;
        }
        _lastBuildStatus = _fanout.TryBuildDelivery(
            _participant, cadenceStep, out DirtyChunkDelivery delivery);
        if (_lastBuildStatus != DirtyChunkBuildStatus.Success) return _lastBuildStatus;
        if (!TryQueueDelivery(in delivery, cadenceStep))
        {
            _fanout.AbandonDelivery(_participant, delivery.DeliveryId);
            _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
            return _lastBuildStatus;
        }
        _deliveriesQueued++;
        return _lastBuildStatus;
    }

    /// <summary>
    /// Queues one MTU-bounded recipient-wide envelope while retaining independent per-chunk
    /// delivery tickets and proofs. Oversized first entries remain eligible for the existing
    /// single-delivery/snapshot path.
    /// </summary>
    public DirtyChunkBuildStatus TryScheduleBundle(ulong cadenceStep)
    {
        EnsureOwner();
        ExpireOutstandingDeliveries(cadenceStep);
        _lastProcessStatus = _fanout.TryProcessDirtyChunks(cadenceStep, out _);
        if (_lastProcessStatus == DirtyChunkProcessStatus.CapacityExceeded)
            return _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
        if (_snapshotActive || _pendingCancel.DeliveryId != 0 ||
            _outstandingCount == _outstanding.Length)
            return _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;

        int available = _outstanding.Length - _outstandingCount;
        int entryLimit = Math.Min(_bundleEntries.Length, available);
        if (entryLimit == 0)
            return _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
        int byteBudget = _peer.MaximumUnfragmentedMessageBytes;
        _lastBuildStatus = _fanout.TryBuildBundle(
            _participant, cadenceStep, byteBudget, out DirtyChunkDeliveryBundle bundle);
        if (_lastBuildStatus != DirtyChunkBuildStatus.Success) return _lastBuildStatus;
        if (bundle.Entries.Length > entryLimit)
        {
            AbandonBundle(in bundle);
            return _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
        }

        int snapshotOffset = 0;
        int deltaOffset = 0;
        for (int index = 0; index < bundle.Entries.Length; index++)
        {
            DirtyChunkDeliveryEntry entry = bundle.Entries[index];
            RemoteChunkPublicationKind kind = entry.Status switch
            {
                OfflineDirectChunkReadStatus.Snapshot => RemoteChunkPublicationKind.Snapshot,
                OfflineDirectChunkReadStatus.Delta => RemoteChunkPublicationKind.Delta,
                OfflineDirectChunkReadStatus.ChunkAbsent => RemoteChunkPublicationKind.Absence,
                _ => 0,
            };
            if (kind == 0)
            {
                AbandonBundle(in bundle);
                return _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
            }
            int recordCount = entry.Status == OfflineDirectChunkReadStatus.Snapshot
                ? entry.SnapshotCount
                : entry.Status == OfflineDirectChunkReadStatus.Delta ? entry.DeltaCount : 0;
            int recordOffset = kind == RemoteChunkPublicationKind.Snapshot
                ? snapshotOffset
                : kind == RemoteChunkPublicationKind.Delta ? deltaOffset : 0;
            _bundleEntries[index] = new RemoteDirtyChunkBundleEntry(
                kind, entry.DeliveryId, entry.Key.Space.Value,
                entry.Key.Coord.X, entry.Key.Coord.Z,
                entry.BaseCursor.IsValid ? entry.BaseCursor.Revision.Value : 0,
                entry.Ticket.Cursor.IsValid ? entry.Ticket.Cursor.Revision.Value : 0,
                entry.Batch.HistoryFloor.IsValid ? entry.Batch.HistoryFloor.Value : 0,
                0, checked((ushort)recordCount), recordOffset);
            if (kind == RemoteChunkPublicationKind.Snapshot)
            {
                ReadOnlySpan<WorldPublicRecord> records = bundle.Snapshot(in entry);
                WriteSnapshotRecords(records, _snapshotRecords.AsSpan(snapshotOffset));
                snapshotOffset += records.Length;
            }
            else if (kind == RemoteChunkPublicationKind.Delta)
            {
                ReadOnlySpan<WorldPublicDeltaRecord> records = bundle.Delta(in entry);
                WriteDeltaRecords(records, _deltaRecords.AsSpan(deltaOffset));
                deltaOffset += records.Length;
            }
        }
        if (!UltraSimGameProtocol.TryWriteDirtyChunkBundle(
                _messageScratch, _applicationSessionEpoch, bundle.SourceEpoch.Value,
                bundle.BundleId, _bundleEntries.AsSpan(0, bundle.Entries.Length),
                _snapshotRecords.AsSpan(0, snapshotOffset),
                _deltaRecords.AsSpan(0, deltaOffset), out int written) ||
            written > _peer.MaximumUnfragmentedMessageBytes ||
            _peer.TryQueue(RemoteDirtyChunkBundleHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
        {
            AbandonBundle(in bundle);
            ClearBundleScratch(bundle.Entries.Length, snapshotOffset, deltaOffset);
            return _lastBuildStatus = DirtyChunkBuildStatus.CapacityExceeded;
        }
        for (int index = 0; index < bundle.Entries.Length; index++)
            AddOutstanding(bundle.Entries[index].Ticket, cadenceStep);
        _deliveriesQueued = checked(_deliveriesQueued + (ulong)bundle.Entries.Length);
        _bundlesQueued++;
        _bundleEntriesQueued = checked(_bundleEntriesQueued + (ulong)bundle.Entries.Length);
        ClearBundleScratch(bundle.Entries.Length, snapshotOffset, deltaOffset);
        return _lastBuildStatus;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId == RemoteChunkAppliedMessageCodec.Descriptor.Id)
        {
            if (!UltraSimGameProtocol.TryReadChunkApplied(
                    payload, out RemoteChunkAppliedMessage proof) ||
                proof.ApplicationSessionEpoch != _applicationSessionEpoch)
                _malformedProofs++;
            else
                ProcessProof(in proof);
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
            _malformedProofs++;
            return true;
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
            _malformedProofs++;
            return;
        }
        bool applied = proof.Status == RemoteChunkPublicationApplyStatus.Applied;
        DirtyChunkAppliedProofStatus status = _fanout.TryAcknowledgeDelivery(
            _participant, proof.DeliveryId, applied);
        RemoveOutstanding(index);
        if (status == DirtyChunkAppliedProofStatus.Applied)
            _applicationProofs++;
        else if (!applied || status is DirtyChunkAppliedProofStatus.Rejected)
            _applicationRejections++;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        if (_pendingCancel.DeliveryId != 0)
        {
            if (!UltraSimGameProtocol.TryWriteChunkSnapshotCancel(
                    _messageScratch, in _pendingCancel, out int cancelBytes) ||
                _peer.TryQueue(RemoteChunkSnapshotCancelMessageCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, cancelBytes)) != NetworkQueueStatus.Accepted)
                return false;
            _pendingCancel = default;
        }
        if (!_snapshotActive) return true;
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
                    _messageScratch, in begin, out int beginBytes) ||
                _peer.TryQueue(RemoteChunkSnapshotBeginMessageCodec.Descriptor.Id,
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
                    out int segmentBytes) ||
                _peer.TryQueue(RemoteChunkSnapshotSegmentHeaderCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, segmentBytes)) != NetworkQueueStatus.Accepted)
                return false;
            _snapshotNextSegment++;
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
                _messageScratch, in commit, out int commitBytes) ||
            _peer.TryQueue(RemoteChunkSnapshotCommitMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, commitBytes)) != NetworkQueueStatus.Accepted)
            return false;
        _snapshotActive = false;
        _snapshotBeginQueued = false;
        _snapshotHeader = default;
        _snapshotTicket = default;
        _snapshotLength = 0;
        _snapshotSegmentCount = 0;
        _snapshotNextSegment = 0;
        _snapshotStartedStep = 0;
        return true;
    }

    /// <summary>
    /// Expires both the adapter's transport ledger and the core's matching semantic delivery
    /// ledger. Superseded core deliveries are also removed so old lineages cannot consume adapter
    /// capacity until their proof timeout.
    /// </summary>
    public int ExpireOutstandingDeliveries(ulong cadenceStep)
    {
        EnsureOwner();
        int expired = 0;
        bool snapshotDeliveryExpired = false;
        if (_snapshotActive)
        {
            bool coreGone = !_fanout.IsDeliveryActive(_participant, _snapshotTicket.DeliveryId);
            bool timedOut = IsExpired(_snapshotStartedStep, cadenceStep);
            if (coreGone || timedOut)
            {
                _fanout.AbandonDelivery(_participant, _snapshotTicket.DeliveryId);
                if (_snapshotBeginQueued) QueueCancel(_snapshotTicket);
                ClearSnapshotTransfer();
                if (timedOut)
                {
                    _snapshotTimeouts++;
                    snapshotDeliveryExpired = true;
                    expired++;
                }
            }
        }
        for (int index = 0; index < _outstanding.Length; index++)
        {
            if (!_outstanding[index].Active) continue;
            DeliveryLineage lineage = _outstanding[index];
            bool coreGone = !_fanout.IsDeliveryActive(_participant, lineage.Ticket.DeliveryId);
            bool timedOut = IsExpired(lineage.SentCadenceStep, cadenceStep);
            if (!coreGone && !timedOut) continue;
            _fanout.AbandonDelivery(_participant, lineage.Ticket.DeliveryId);
            RemoveOutstanding(index);
            if (timedOut && !snapshotDeliveryExpired)
            {
                _deliveryTimeouts++;
                expired++;
            }
        }
        return expired;
    }

    public bool ReplaceSource(ChunkPublicationStore source)
    {
        EnsureOwner();
        if (!_fanout.ReplaceSource(source)) return false;
        CancelActiveSnapshot();
        for (int index = 0; index < _outstanding.Length; index++)
            if (_outstanding[index].Active)
            {
                _fanout.AbandonDelivery(_participant, _outstanding[index].Ticket.DeliveryId);
                _outstanding[index] = default;
            }
        _outstandingCount = 0;
        return true;
    }

    public RemoteDirtyChunkSpatialFanoutServerSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new(
            _deliveriesQueued, _bundlesQueued, _bundleEntriesQueued,
            _applicationProofs, _applicationRejections, _malformedProofs,
            _lastBuildStatus, _lastProcessStatus, _outstandingCount,
            _deliveryTimeouts, _snapshotTimeouts, _snapshotActive,
            _pendingCancel.DeliveryId != 0,
            checked((long)_snapshotRecords.Length * Unsafe.SizeOf<RemoteChunkPublicRecord>() +
                (long)_deltaRecords.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>() +
                (long)_bundleEntries.Length * Unsafe.SizeOf<RemoteDirtyChunkBundleEntry>() +
                _messageScratch.Length + _snapshotPayload.Length +
                (long)_outstanding.Length * Unsafe.SizeOf<DeliveryLineage>()));
    }

    private void AbandonBundle(in DirtyChunkDeliveryBundle bundle)
    {
        for (int index = 0; index < bundle.Entries.Length; index++)
            _fanout.AbandonDelivery(_participant, bundle.Entries[index].DeliveryId);
    }

    private void ClearBundleScratch(int entries, int snapshots, int deltas)
    {
        _bundleEntries.AsSpan(0, entries).Clear();
        _snapshotRecords.AsSpan(0, snapshots).Clear();
        _deltaRecords.AsSpan(0, deltas).Clear();
    }

    private bool TryQueueDelivery(in DirtyChunkDelivery delivery, ulong cadenceStep)
    {
        if (delivery.Status == OfflineDirectChunkReadStatus.Snapshot)
        {
            if (!UltraSimGameProtocol.TryWriteChunkSnapshot(
                    _snapshotPayload, _applicationSessionEpoch,
                    delivery.Ticket.SourceEpoch.Value, delivery.DeliveryId,
                    delivery.Key.Space.Value, delivery.Key.Coord.X, delivery.Key.Coord.Z,
                    delivery.Ticket.Cursor.Revision.Value, delivery.Batch.HistoryFloor.Value,
                    ToSnapshotRecords(delivery.Snapshot), out _snapshotLength))
                return false;
            if (!UltraSimGameProtocol.TryReadChunkSnapshot(
                    _snapshotPayload.AsSpan(0, _snapshotLength), out _snapshotHeader,
                    _snapshotRecords, out _))
                return false;
            _snapshotSegmentCount = checked((_snapshotLength + _snapshotSegmentPayloadBytes - 1) /
                _snapshotSegmentPayloadBytes);
            if (_snapshotSegmentCount > UltraSimGameProtocol.MaximumChunkSnapshotSegments)
                return false;
            _snapshotTicket = delivery.Ticket;
            _snapshotActive = true;
            _snapshotStartedStep = cadenceStep;
            AddOutstanding(delivery.Ticket, cadenceStep);
            return true;
        }

        int written;
        NetworkMessageId messageId;
        if (delivery.Status == OfflineDirectChunkReadStatus.Delta)
        {
            if (!delivery.BaseCursor.IsValid || delivery.Deltas.IsEmpty) return false;
            if (!UltraSimGameProtocol.TryWriteChunkDelta(
                    _messageScratch, _applicationSessionEpoch,
                    delivery.Ticket.SourceEpoch.Value, delivery.DeliveryId,
                    delivery.Key.Space.Value, delivery.Key.Coord.X, delivery.Key.Coord.Z,
                    delivery.BaseCursor.Revision.Value, delivery.Ticket.Cursor.Revision.Value,
                    delivery.Batch.HistoryFloor.Value,
                    ToDeltaRecords(delivery.Deltas), out written))
                return false;
            messageId = RemoteChunkDeltaHeaderCodec.Descriptor.Id;
        }
        else if (delivery.Status == OfflineDirectChunkReadStatus.ChunkAbsent)
        {
            if (!UltraSimGameProtocol.TryWriteChunkAbsence(
                    _messageScratch, _applicationSessionEpoch,
                    delivery.Ticket.SourceEpoch.Value, delivery.DeliveryId,
                    delivery.Key.Space.Value, delivery.Key.Coord.X, delivery.Key.Coord.Z,
                    out written))
                return false;
            messageId = RemoteChunkAbsenceMessageCodec.Descriptor.Id;
        }
        else return false;
        if (written > _peer.MaximumUnfragmentedMessageBytes ||
            _peer.TryQueue(messageId, _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        AddOutstanding(delivery.Ticket, cadenceStep);
        return true;
    }

    private ReadOnlySpan<RemoteChunkPublicRecord> ToSnapshotRecords(ReadOnlySpan<WorldPublicRecord> records)
    {
        WriteSnapshotRecords(records, _snapshotRecords);
        return _snapshotRecords.AsSpan(0, records.Length);
    }

    private ReadOnlySpan<RemoteChunkDeltaRecord> ToDeltaRecords(ReadOnlySpan<WorldPublicDeltaRecord> records)
    {
        WriteDeltaRecords(records, _deltaRecords);
        return _deltaRecords.AsSpan(0, records.Length);
    }

    private static void WriteSnapshotRecords(
        ReadOnlySpan<WorldPublicRecord> records,
        Span<RemoteChunkPublicRecord> destination)
    {
        for (int index = 0; index < records.Length; index++)
        {
            WorldPublicRecord record = records[index];
            destination[index] = new(
                record.Id.Index, record.Id.Generation, record.EntityPublicVersion,
                record.PrefabId, record.Position.Local.XMillimeters,
                record.Position.Local.ZMillimeters, record.Position.YMillimeters,
                record.Orientation.Units, (ushort)record.StateFlags,
                record.LastChangedRevision.Value);
        }
    }

    private static void WriteDeltaRecords(
        ReadOnlySpan<WorldPublicDeltaRecord> records,
        Span<RemoteChunkDeltaRecord> destination)
    {
        for (int index = 0; index < records.Length; index++)
        {
            WorldPublicDeltaRecord record = records[index];
            WorldPublicRecord current = record.Current;
            destination[index] = new(
                (RemoteChunkPublicOperation)record.Operation,
                (RemoteChunkPublicFieldMask)record.Fields,
                record.Id.Index, record.Id.Generation, record.EntityPublicVersion,
                current.PrefabId, current.Position.Local.XMillimeters,
                current.Position.Local.ZMillimeters, current.Position.YMillimeters,
                current.Orientation.Units, (ushort)current.StateFlags,
                current.LastChangedRevision.Value);
        }
    }

    private static bool Matches(
        in OfflineDirectChunkDeliveryTicket ticket,
        in RemoteChunkAppliedMessage proof) =>
        proof.PublicationEpoch == ticket.SourceEpoch.Value &&
        proof.SpatialSpaceId == ticket.Key.Space.Value &&
        proof.ChunkX == ticket.Key.Coord.X && proof.ChunkZ == ticket.Key.Coord.Z &&
        proof.DeliveryId == ticket.DeliveryId &&
        proof.Revision == (ticket.Cursor.IsValid ? ticket.Cursor.Revision.Value : 0) &&
        proof.Kind == (ticket.Status switch
        {
            OfflineDirectChunkReadStatus.Snapshot => RemoteChunkPublicationKind.Snapshot,
            OfflineDirectChunkReadStatus.Delta => RemoteChunkPublicationKind.Delta,
            OfflineDirectChunkReadStatus.ChunkAbsent => RemoteChunkPublicationKind.Absence,
            _ => 0,
        });

    private int FindDelivery(ulong deliveryId)
    {
        for (int index = 0; index < _outstanding.Length; index++)
            if (_outstanding[index].Active && _outstanding[index].Ticket.DeliveryId == deliveryId)
                return index;
        return -1;
    }

    private void AddOutstanding(in OfflineDirectChunkDeliveryTicket ticket, ulong cadenceStep)
    {
        for (int index = 0; index < _outstanding.Length; index++)
            if (!_outstanding[index].Active)
            {
                _outstanding[index] = new DeliveryLineage
                {
                    Active = true, Ticket = ticket, SentCadenceStep = cadenceStep,
                };
                _outstandingCount++;
                return;
            }
        throw new InvalidOperationException("Outstanding delivery preflight diverged.");
    }

    private void RemoveOutstanding(int index)
    {
        _outstanding[index] = default;
        _outstandingCount--;
    }

    private bool IsExpired(ulong sentStep, ulong cadenceStep) =>
        cadenceStep >= sentStep &&
        cadenceStep - sentStep >= _fanout.Limits.MaximumDeliveryAgeSteps;

    private void ClearSnapshotTransfer()
    {
        _snapshotActive = false;
        _snapshotBeginQueued = false;
        _snapshotTicket = default;
        _snapshotHeader = default;
        _snapshotLength = 0;
        _snapshotSegmentCount = 0;
        _snapshotNextSegment = 0;
        _snapshotStartedStep = 0;
    }

    private void CancelActiveSnapshot()
    {
        if (!_snapshotActive) return;
        _fanout.AbandonDelivery(_participant, _snapshotTicket.DeliveryId);
        if (_snapshotBeginQueued) QueueCancel(_snapshotTicket);
        ClearSnapshotTransfer();
    }

    private void QueueCancel(in OfflineDirectChunkDeliveryTicket ticket)
    {
        if (ticket.Status != OfflineDirectChunkReadStatus.Snapshot) return;
        if (_pendingCancel.DeliveryId != 0 &&
            _pendingCancel.DeliveryId != ticket.DeliveryId)
            throw new InvalidOperationException("Only one snapshot cancellation may be retained.");
        _pendingCancel = new RemoteChunkSnapshotCancelMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            PublicationEpoch = ticket.SourceEpoch.Value,
            DeliveryId = ticket.DeliveryId,
        };
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The dirty-chunk remote adapter is owner-thread-bound.");
    }

    private struct DeliveryLineage
    {
        internal OfflineDirectChunkDeliveryTicket Ticket;
        internal ulong SentCadenceStep;
        internal bool Active;
    }
}
