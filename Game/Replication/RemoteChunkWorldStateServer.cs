using System.Runtime.CompilerServices;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteChunkWorldStateTransportLimits(
    int MaximumOutstandingDeliveries)
{
    public static RemoteChunkWorldStateTransportLimits Default => new(1_024);

    internal void Validate()
    {
        if (MaximumOutstandingDeliveries <= 0)
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
    long ReservedBytes);

/// <summary>
/// N1.2 connection-owned delivery lifecycle for ordinary chunk deltas and absences. Publication
/// knowledge advances only through an authenticated application proof retained in this bounded
/// ledger. Snapshot recovery and segmented delta sets remain later protocol checkpoints.
/// </summary>
public sealed class RemoteChunkWorldStateServer : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private readonly OfflineDirectChunkRecipient _recipient;
    private ChunkPublicationStore _source;
    private readonly OfflineDirectChunkWireEncoder _encoder;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _messageScratch;
    private readonly OutstandingDelivery[] _outstanding;
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
        _outstanding = new OutstandingDelivery[limits.MaximumOutstandingDeliveries];
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
        ulong deliveryId = _nextDeliveryId == 0 ? ulong.MaxValue : _nextDeliveryId;
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
            OfflineDirectChunkWireEncodeStatus.Snapshot => RemoteChunkWorldStateScheduleStatus.SnapshotRequired,
            OfflineDirectChunkWireEncodeStatus.EncodingFailed =>
                RemoteChunkWorldStateScheduleStatus.TransportNotAccepting,
            _ => default,
        };
        if (encoded is not (OfflineDirectChunkWireEncodeStatus.Delta or
            OfflineDirectChunkWireEncodeStatus.Absence))
            return mapped;
        if (_nextDeliveryId == 0)
            return RemoteChunkWorldStateScheduleStatus.DeliverySequenceExhausted;
        if (result.Written > _peer.MaximumUnfragmentedMessageBytes)
        {
            SaturatingIncrement(ref _oversizeDeltas);
            return RemoteChunkWorldStateScheduleStatus.PayloadTooLarge;
        }
        if (_outstandingCount == _outstanding.Length)
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

        _outstanding[_outstandingCount++] = new OutstandingDelivery(result.Ticket);
        _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;
        if (result.Kind == RemoteChunkPublicationKind.Delta)
        {
            SaturatingIncrement(ref _deltasQueued);
            return RemoteChunkWorldStateScheduleStatus.QueuedDelta;
        }
        SaturatingIncrement(ref _absencesQueued);
        return RemoteChunkWorldStateScheduleStatus.QueuedAbsence;
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
            InvalidateObsolete(ticket.Key);
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
            checked((long)_outstanding.Length * Unsafe.SizeOf<OutstandingDelivery>() +
                _messageScratch.Length));
    }

    private void InvalidateAll()
    {
        if (_outstandingCount == 0) return;
        SaturatingAdd(ref _invalidatedDeliveries, checked((ulong)_outstandingCount));
        _outstanding.AsSpan(0, _outstandingCount).Clear();
        _outstandingCount = 0;
    }

    private void Invalidate(SpatialChunkKey key)
    {
        for (int index = _outstandingCount - 1; index >= 0; index--)
        {
            if (_outstanding[index].Ticket.Key != key) continue;
            RemoveAt(index);
            SaturatingIncrement(ref _invalidatedDeliveries);
        }
    }

    private void InvalidateObsolete(SpatialChunkKey key)
    {
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
                candidate.Cursor.Revision.Value <= applied.Cursor.Revision.Value;
            if (!obsolete) continue;
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
        internal OutstandingDelivery(OfflineDirectChunkDeliveryTicket ticket) => Ticket = ticket;
        internal OfflineDirectChunkDeliveryTicket Ticket { get; }
    }
}
