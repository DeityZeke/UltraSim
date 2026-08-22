using UltraSim.Game.Protocol;
using UltraSim.Game.World;

namespace UltraSim.Game.Replication;

public enum OfflineDirectChunkWireEncodeStatus : byte
{
    Snapshot = 1,
    Delta = 2,
    Absence = 3,
    NoPayload = 4,
    NotDue = 5,
    InvalidSubscription = 6,
    SourceRequiresReconcile = 7,
    ReadPending = 8,
    CapacityExceeded = 9,
    FutureCursor = 10,
    EncodingFailed = 11,
}

public readonly record struct OfflineDirectChunkWireEncodeResult(
    OfflineDirectChunkWireEncodeStatus Status,
    RemoteChunkPublicationKind Kind,
    int Written,
    OfflineDirectChunkDeliveryTicket Ticket);

/// <summary>
/// N1.1 packet-free server-side wire preparation. Encoding releases publication scratch but never
/// advances recipient knowledge. A later authenticated matching Applied proof commits the retained
/// delivery ticket; transport submission alone has no semantic effect.
/// </summary>
public sealed class OfflineDirectChunkWireEncoder
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ulong _applicationSessionEpoch;
    private readonly RemoteChunkPublicRecord[] _snapshotRecords;
    private readonly RemoteChunkDeltaRecord[] _deltaRecords;

    public OfflineDirectChunkWireEncoder(ulong applicationSessionEpoch, int maximumRecords)
    {
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (maximumRecords is <= 0 or > UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        _applicationSessionEpoch = applicationSessionEpoch;
        _snapshotRecords = new RemoteChunkPublicRecord[maximumRecords];
        _deltaRecords = new RemoteChunkDeltaRecord[maximumRecords];
    }

    public OfflineDirectChunkWireEncodeStatus TryEncode(
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore source,
        int subscriptionIndex,
        ulong cadenceStep,
        ulong deliveryId,
        Span<byte> destination,
        out OfflineDirectChunkWireEncodeResult result)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (deliveryId == 0)
        {
            result = new(OfflineDirectChunkWireEncodeStatus.EncodingFailed, default, 0, default);
            return result.Status;
        }

        bool knownAbsence = (uint)subscriptionIndex < (uint)recipient.Subscriptions.Length &&
            recipient.Subscriptions[subscriptionIndex].HasKnowledge &&
            !recipient.Subscriptions[subscriptionIndex].Cursor.IsValid;
        OfflineDirectChunkReadStatus readStatus = recipient.TryBeginRead(
            source, subscriptionIndex, cadenceStep, out OfflineDirectChunkRead read);
        switch (readStatus)
        {
            case OfflineDirectChunkReadStatus.NotDue:
                return Finish(OfflineDirectChunkWireEncodeStatus.NotDue, out result);
            case OfflineDirectChunkReadStatus.InvalidSubscription:
                return Finish(OfflineDirectChunkWireEncodeStatus.InvalidSubscription, out result);
            case OfflineDirectChunkReadStatus.SourceRequiresReconcile:
                return Finish(OfflineDirectChunkWireEncodeStatus.SourceRequiresReconcile, out result);
            case OfflineDirectChunkReadStatus.ReadPending:
                return Finish(OfflineDirectChunkWireEncodeStatus.ReadPending, out result);
            case OfflineDirectChunkReadStatus.SnapshotCapacityExceeded:
            case OfflineDirectChunkReadStatus.DeltaCapacityExceeded:
                return Finish(OfflineDirectChunkWireEncodeStatus.CapacityExceeded, out result);
            case OfflineDirectChunkReadStatus.FutureCursor:
                return Finish(OfflineDirectChunkWireEncodeStatus.FutureCursor, out result);
            case OfflineDirectChunkReadStatus.NoChanges:
                recipient.CommitRead();
                return Finish(OfflineDirectChunkWireEncodeStatus.NoPayload, out result);
            case OfflineDirectChunkReadStatus.ChunkAbsent when knownAbsence:
                recipient.CommitRead();
                return Finish(OfflineDirectChunkWireEncodeStatus.NoPayload, out result);
            case OfflineDirectChunkReadStatus.Snapshot:
                return EncodeSnapshot(recipient, deliveryId, destination, in read, out result);
            case OfflineDirectChunkReadStatus.Delta:
                return EncodeDelta(
                    recipient, subscriptionIndex, deliveryId, destination, in read, out result);
            case OfflineDirectChunkReadStatus.ChunkAbsent:
                return EncodeAbsence(recipient, deliveryId, destination, in read, out result);
            default:
                throw new InvalidOperationException($"Unexpected N1.1 read status {readStatus}.");
        }
    }

    public bool TryCommitAppliedProof(
        OfflineDirectChunkRecipient recipient,
        in OfflineDirectChunkDeliveryTicket ticket,
        in RemoteChunkAppliedMessage proof,
        out OfflineDirectChunkAppliedProofStatus status)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        EnsureOwner();
        if (proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.PublicationEpoch != ticket.SourceEpoch.Value ||
            proof.DeliveryId != ticket.DeliveryId ||
            proof.SpatialSpaceId != ticket.Key.Space.Value ||
            proof.ChunkX != ticket.Key.Coord.X || proof.ChunkZ != ticket.Key.Coord.Z ||
            proof.Kind != Kind(ticket.Status) ||
            proof.Revision != (ticket.Cursor.IsValid ? ticket.Cursor.Revision.Value : 0) ||
            proof.Status != RemoteChunkPublicationApplyStatus.Applied)
        {
            status = OfflineDirectChunkAppliedProofStatus.Invalid;
            return false;
        }
        status = recipient.CommitAppliedProof(ticket);
        return status is OfflineDirectChunkAppliedProofStatus.Applied or
            OfflineDirectChunkAppliedProofStatus.Duplicate or
            OfflineDirectChunkAppliedProofStatus.Stale;
    }

    private OfflineDirectChunkWireEncodeStatus EncodeSnapshot(
        OfflineDirectChunkRecipient recipient,
        ulong deliveryId,
        Span<byte> destination,
        in OfflineDirectChunkRead read,
        out OfflineDirectChunkWireEncodeResult result)
    {
        if (read.Snapshot.Length > _snapshotRecords.Length)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.CapacityExceeded, out result);
        for (int index = 0; index < read.Snapshot.Length; index++)
        {
            ref readonly WorldPublicRecord record = ref read.Snapshot[index];
            _snapshotRecords[index] = new RemoteChunkPublicRecord(
                record.Id.Index, record.Id.Generation, record.EntityPublicVersion, record.PrefabId,
                record.Position.Local.XMillimeters, record.Position.Local.ZMillimeters,
                record.Position.YMillimeters, record.Orientation.Units, (ushort)record.StateFlags,
                record.LastChangedRevision.Value);
        }
        bool encoded = UltraSimGameProtocol.TryWriteChunkSnapshot(
            destination, _applicationSessionEpoch, read.SourceEpoch.Value, deliveryId,
            read.Batch.Key.Space.Value, read.Batch.Key.Coord.X, read.Batch.Key.Coord.Z,
            read.Batch.Cursor.Revision.Value, read.Batch.HistoryFloor.Value,
            _snapshotRecords.AsSpan(0, read.Snapshot.Length), out int written);
        _snapshotRecords.AsSpan(0, read.Snapshot.Length).Clear();
        if (!encoded)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
        result = new(OfflineDirectChunkWireEncodeStatus.Snapshot,
            RemoteChunkPublicationKind.Snapshot, written, ticket);
        return result.Status;
    }

    private OfflineDirectChunkWireEncodeStatus EncodeDelta(
        OfflineDirectChunkRecipient recipient,
        int subscriptionIndex,
        ulong deliveryId,
        Span<byte> destination,
        in OfflineDirectChunkRead read,
        out OfflineDirectChunkWireEncodeResult result)
    {
        if (read.Deltas.Length > _deltaRecords.Length)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.CapacityExceeded, out result);
        ChunkPublicationRevision baseRevision =
            recipient.Subscriptions[subscriptionIndex].Cursor.Revision;
        for (int index = 0; index < read.Deltas.Length; index++)
        {
            ref readonly WorldPublicDeltaRecord delta = ref read.Deltas[index];
            WorldPublicRecord current = delta.Current;
            _deltaRecords[index] = new RemoteChunkDeltaRecord(
                (RemoteChunkPublicOperation)delta.Operation,
                (RemoteChunkPublicFieldMask)delta.Fields,
                delta.Id.Index, delta.Id.Generation, delta.EntityPublicVersion,
                current.PrefabId, current.Position.Local.XMillimeters,
                current.Position.Local.ZMillimeters, current.Position.YMillimeters,
                current.Orientation.Units, (ushort)current.StateFlags,
                current.LastChangedRevision.Value);
        }
        bool encoded = UltraSimGameProtocol.TryWriteChunkDelta(
            destination, _applicationSessionEpoch, read.SourceEpoch.Value, deliveryId,
            read.Batch.Key.Space.Value, read.Batch.Key.Coord.X, read.Batch.Key.Coord.Z,
            baseRevision.Value, read.Batch.Cursor.Revision.Value, read.Batch.HistoryFloor.Value,
            _deltaRecords.AsSpan(0, read.Deltas.Length), out int written);
        _deltaRecords.AsSpan(0, read.Deltas.Length).Clear();
        if (!encoded)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
        result = new(OfflineDirectChunkWireEncodeStatus.Delta,
            RemoteChunkPublicationKind.Delta, written, ticket);
        return result.Status;
    }

    private OfflineDirectChunkWireEncodeStatus EncodeAbsence(
        OfflineDirectChunkRecipient recipient,
        ulong deliveryId,
        Span<byte> destination,
        in OfflineDirectChunkRead read,
        out OfflineDirectChunkWireEncodeResult result)
    {
        bool encoded = UltraSimGameProtocol.TryWriteChunkAbsence(
            destination, _applicationSessionEpoch, read.SourceEpoch.Value, deliveryId,
            read.Batch.Key.Space.Value, read.Batch.Key.Coord.X, read.Batch.Key.Coord.Z,
            out int written);
        if (!encoded)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
        result = new(OfflineDirectChunkWireEncodeStatus.Absence,
            RemoteChunkPublicationKind.Absence, written, ticket);
        return result.Status;
    }

    private static OfflineDirectChunkWireEncodeStatus Cancel(
        OfflineDirectChunkRecipient recipient,
        OfflineDirectChunkWireEncodeStatus status,
        out OfflineDirectChunkWireEncodeResult result)
    {
        recipient.CancelRead();
        result = new(status, default, 0, default);
        return status;
    }

    private static OfflineDirectChunkWireEncodeStatus Finish(
        OfflineDirectChunkWireEncodeStatus status,
        out OfflineDirectChunkWireEncodeResult result)
    {
        result = new(status, default, 0, default);
        return status;
    }

    private static RemoteChunkPublicationKind Kind(OfflineDirectChunkReadStatus status) => status switch
    {
        OfflineDirectChunkReadStatus.Snapshot => RemoteChunkPublicationKind.Snapshot,
        OfflineDirectChunkReadStatus.Delta => RemoteChunkPublicationKind.Delta,
        OfflineDirectChunkReadStatus.ChunkAbsent => RemoteChunkPublicationKind.Absence,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The OfflineDirect chunk wire encoder is owner-thread bound.");
    }
}
