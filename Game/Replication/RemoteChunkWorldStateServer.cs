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
    long ReservedBytes);

/// <summary>
/// N1.2/N1.3 connection-owned delivery lifecycle for ordinary chunk state and exceptional atomic
/// Bulk snapshots. Publication knowledge advances only through an authenticated application proof
/// retained in the bounded ledger. Segmented ordinary deltas remain out of scope.
/// </summary>
public sealed class RemoteChunkWorldStateServer : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
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
    }

    public OfflineDirectChunkRecipient Recipient => _recipient;

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
        InvalidateAll();
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
        OfflineDirectChunkReconcileStatus status =
            _recipient.Reconcile(_source, space, anchor, changes, out result);
        if (status != OfflineDirectChunkReconcileStatus.Reconciled) return status;
        if (result.SourceReset)
        {
            InvalidateAll();
            return status;
        }
        for (int index = 0; index < result.ChangeCount; index++)
            if (changes[index].Kind == OfflineDirectChunkSubscriptionChangeKind.Left)
                Invalidate(changes[index].Key);
        return status;
    }

    public RemoteChunkWorldStateScheduleStatus TrySchedule(int subscriptionIndex, ulong cadenceStep)
    {
        EnsureOwner();
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
        if (encoded == OfflineDirectChunkWireEncodeStatus.Snapshot)
            return StartSnapshotTransfer(in result, cadenceStep);
        if (encoded is not (OfflineDirectChunkWireEncodeStatus.Delta or
            OfflineDirectChunkWireEncodeStatus.Absence))
            return mapped;
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

        _outstanding[_outstandingCount++] = new OutstandingDelivery(result.Ticket, cadenceStep);
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
    /// Starts a complete repair snapshot regardless of the retained chunk cursor. This is the
    /// explicit recovery route after an MTU-oversize delta or client-declared semantic rejection.
    /// </summary>
    public RemoteChunkWorldStateScheduleStatus TryScheduleSnapshot(
        int subscriptionIndex,
        ulong serviceStep)
    {
        EnsureOwner();
        if (_nextDeliveryId == 0)
            return RemoteChunkWorldStateScheduleStatus.DeliverySequenceExhausted;
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
        if (_snapshotTicket.IsValid &&
            serviceStep >= _snapshotStartedStep &&
            serviceStep - _snapshotStartedStep >= _maximumSnapshotTransferSteps)
        {
            CancelActiveSnapshot();
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
            SaturatingIncrement(ref _snapshotTimeouts);
            return true;
        }
        return false;
    }

    public bool TryProgress()
    {
        EnsureOwner();
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
        _outstanding[_outstandingCount++] =
            new OutstandingDelivery(_snapshotTicket, _snapshotStartedStep);
        ClearActiveSnapshot();
        SaturatingIncrement(ref _snapshotsQueued);
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId != RemoteChunkAppliedMessageCodec.Descriptor.Id) return false;
        if (!UltraSimGameProtocol.TryReadChunkApplied(payload, out RemoteChunkAppliedMessage proof))
        {
            SaturatingIncrement(ref _malformedProofs);
            return true;
        }
        int index = FindDelivery(proof.DeliveryId);
        if (index < 0 || !Matches(_outstanding[index].Ticket, proof))
        {
            SaturatingIncrement(ref _malformedProofs);
            return true;
        }

        OfflineDirectChunkDeliveryTicket ticket = _outstanding[index].Ticket;
        if (proof.Status != RemoteChunkPublicationApplyStatus.Applied)
        {
            RemoveAt(index);
            SaturatingIncrement(ref _rejectedProofs);
            return true;
        }
        if (!_encoder.TryCommitAppliedProof(
                _recipient, ticket, proof, out OfflineDirectChunkAppliedProofStatus status))
        {
            RemoveAt(index);
            SaturatingIncrement(ref _malformedProofs);
            return true;
        }
        RemoveAt(index);
        if (status is OfflineDirectChunkAppliedProofStatus.Applied or
            OfflineDirectChunkAppliedProofStatus.Duplicate or
            OfflineDirectChunkAppliedProofStatus.Stale)
        {
            InvalidateObsolete(in ticket);
            SaturatingIncrement(ref _appliedProofs);
        }
        return true;
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
                _messageScratch.Length + _snapshotPayload.Length));
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
        _outstanding[_outstandingCount++] = new OutstandingDelivery(result.Ticket, serviceStep);
        _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;
        SaturatingIncrement(ref _absencesQueued);
        return RemoteChunkWorldStateScheduleStatus.QueuedAbsence;
    }

    private bool HasOutstandingCapacity() =>
        _outstandingCount < _outstanding.Length - (_snapshotTicket.IsValid ? 1 : 0);

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

    private void InvalidateAll()
    {
        CancelActiveSnapshot();
        for (int index = 0; index < _outstandingCount; index++)
            QueueCancel(_outstanding[index].Ticket);
        SaturatingAdd(ref _invalidatedDeliveries, checked((ulong)_outstandingCount));
        _outstanding.AsSpan(0, _outstandingCount).Clear();
        _outstandingCount = 0;
    }

    private void Invalidate(SpatialChunkKey key)
    {
        if (_snapshotTicket.IsValid && _snapshotTicket.Key == key) CancelActiveSnapshot();
        for (int index = _outstandingCount - 1; index >= 0; index--)
        {
            if (_outstanding[index].Ticket.Key != key) continue;
            QueueCancel(_outstanding[index].Ticket);
            RemoveAt(index);
            SaturatingIncrement(ref _invalidatedDeliveries);
        }
    }

    private void InvalidateObsolete(in OfflineDirectChunkDeliveryTicket appliedTicket)
    {
        SpatialChunkKey key = appliedTicket.Key;
        if (_snapshotTicket.IsValid && _snapshotTicket.Key == key &&
            _snapshotTicket.DeliveryId < appliedTicket.DeliveryId)
            CancelActiveSnapshot();
        int subscription = FindSubscription(key);
        if (subscription < 0)
        {
            Invalidate(key);
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
            SaturatingIncrement(ref _invalidatedDeliveries);
        }
    }

    private int FindSubscription(SpatialChunkKey key)
    {
        ReadOnlySpan<OfflineDirectChunkSubscription> subscriptions = _recipient.Subscriptions;
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
        internal OutstandingDelivery(OfflineDirectChunkDeliveryTicket ticket, ulong startedStep)
        {
            Ticket = ticket;
            StartedStep = startedStep;
        }
        internal OfflineDirectChunkDeliveryTicket Ticket { get; }
        internal ulong StartedStep { get; }
    }
}
