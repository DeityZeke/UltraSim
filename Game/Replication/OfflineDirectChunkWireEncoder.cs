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
    OfflineDirectChunkDeliveryTicket Ticket)
{
    public ulong BaseRevision { get; init; }
    public ulong CurrentRevision { get; init; }
    public ulong HistoryFloor { get; init; }
    public ulong ContentHash { get; init; }
    public int RecordCount { get; init; }
}

/// <summary>Detailed-only PF4E-R2 timing for trusted server-side publication preparation.</summary>
public readonly record struct OfflineDirectChunkWireEncoderAttribution(
    bool Enabled,
    ulong SourceReadCalls,
    ulong SourceReadTicks,
    ulong DeltaMaterializationCalls,
    ulong DeltaMaterializationTicks,
    ulong DeltaRecordsMaterialized,
    ulong PreparedValidationHashCalls,
    ulong PreparedValidationHashTicks,
    ulong PreparedRecordsValidated,
    ulong PreparedContentHashCalls,
    ulong CanonicalStandaloneEncodeCalls,
    ulong CanonicalStandaloneEncodeTicks,
    ulong CanonicalStandaloneBytes,
    ulong PreparedStandaloneWrites,
    ulong ProofValidationCalls,
    ulong ProofValidationTicks,
    ulong CursorAdvancementCalls,
    ulong CursorAdvancementTicks,
    ulong BodyRequests,
    ulong BodyHits,
    ulong BodyMisses,
    ulong BodyRecordsServed,
    ulong BodyRecordsBuilt,
    ulong MaterializationsAvoided,
    ulong ValidationHashCallsAvoided,
    ulong BodyLookupTicks,
    ulong BodyFillTicks);

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
    private readonly byte[] _deltaRecordBytes;
    private RemoteChunkDeltaBodyStore? _bodyStore;
    private bool _attributionEnabled;
    private ulong _sourceReadCalls;
    private ulong _sourceReadTicks;
    private ulong _deltaMaterializationCalls;
    private ulong _deltaMaterializationTicks;
    private ulong _deltaRecordsMaterialized;
    private ulong _preparedValidationHashCalls;
    private ulong _preparedValidationHashTicks;
    private ulong _preparedRecordsValidated;
    private ulong _preparedContentHashCalls;
    private ulong _canonicalStandaloneEncodeCalls;
    private ulong _canonicalStandaloneEncodeTicks;
    private ulong _canonicalStandaloneBytes;
    private ulong _preparedStandaloneWrites;
    private ulong _proofValidationCalls;
    private ulong _proofValidationTicks;
    private ulong _cursorAdvancementCalls;
    private ulong _cursorAdvancementTicks;
    private ulong _bodyRequests;
    private ulong _bodyHits;
    private ulong _bodyMisses;
    private ulong _bodyRecordsServed;
    private ulong _bodyRecordsBuilt;
    private ulong _materializationsAvoided;
    private ulong _validationHashCallsAvoided;
    private ulong _bodyLookupTicks;
    private ulong _bodyFillTicks;

    public OfflineDirectChunkWireEncoder(
        ulong applicationSessionEpoch,
        int maximumRecords,
        RemoteChunkDeltaBodyStore? bodyStore = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (maximumRecords is <= 0 or > UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        _applicationSessionEpoch = applicationSessionEpoch;
        _snapshotRecords = new RemoteChunkPublicRecord[maximumRecords];
        _deltaRecords = new RemoteChunkDeltaRecord[maximumRecords];
        _deltaRecordBytes = new byte[checked(
            maximumRecords * RemoteChunkDeltaRecord.EncodedSize)];
        _bodyStore = bodyStore;
    }

    public void ResetAttribution(bool enabled)
    {
        EnsureOwner();
        _attributionEnabled = enabled;
        _sourceReadCalls = _sourceReadTicks = 0;
        _deltaMaterializationCalls = _deltaMaterializationTicks = 0;
        _deltaRecordsMaterialized = 0;
        _preparedValidationHashCalls = _preparedValidationHashTicks = 0;
        _preparedRecordsValidated = _preparedContentHashCalls = 0;
        _canonicalStandaloneEncodeCalls = _canonicalStandaloneEncodeTicks = 0;
        _canonicalStandaloneBytes = 0;
        _preparedStandaloneWrites = 0;
        _proofValidationCalls = _proofValidationTicks = 0;
        _cursorAdvancementCalls = _cursorAdvancementTicks = 0;
        _bodyRequests = _bodyHits = _bodyMisses = 0;
        _bodyRecordsServed = _bodyRecordsBuilt = 0;
        _materializationsAvoided = _validationHashCallsAvoided = 0;
        _bodyLookupTicks = _bodyFillTicks = 0;
    }

    public OfflineDirectChunkWireEncoderAttribution GetAttribution()
    {
        EnsureOwner();
        return new OfflineDirectChunkWireEncoderAttribution(
            _attributionEnabled,
            _sourceReadCalls,
            _sourceReadTicks,
            _deltaMaterializationCalls,
            _deltaMaterializationTicks,
            _deltaRecordsMaterialized,
            _preparedValidationHashCalls,
            _preparedValidationHashTicks,
            _preparedRecordsValidated,
            _preparedContentHashCalls,
            _canonicalStandaloneEncodeCalls,
            _canonicalStandaloneEncodeTicks,
            _canonicalStandaloneBytes,
            _preparedStandaloneWrites,
            _proofValidationCalls,
            _proofValidationTicks,
            _cursorAdvancementCalls,
            _cursorAdvancementTicks,
            _bodyRequests,
            _bodyHits,
            _bodyMisses,
            _bodyRecordsServed,
            _bodyRecordsBuilt,
            _materializationsAvoided,
            _validationHashCallsAvoided,
            _bodyLookupTicks,
            _bodyFillTicks);
    }

    internal void ReplaceBodyStore(RemoteChunkDeltaBodyStore? bodyStore)
    {
        EnsureOwner();
        _bodyStore = bodyStore;
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

        bool knownAbsence = subscriptionIndex >= 0 &&
            subscriptionIndex < recipient.Subscriptions.Length &&
            recipient.Subscriptions[subscriptionIndex].HasKnowledge &&
            !recipient.Subscriptions[subscriptionIndex].Cursor.IsValid;
        long sourceReadStarted = AttributionTimestamp();
        OfflineDirectChunkReadStatus readStatus = recipient.TryBeginRead(
            source, subscriptionIndex, cadenceStep, out OfflineDirectChunkRead read);
        Attribute(ref _sourceReadCalls, ref _sourceReadTicks, sourceReadStarted);
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

    /// <summary>
    /// Materializes one ordinary publication for representation selection. Snapshot recovery still
    /// uses its canonical standalone bytes; Delta and Absence retain only validated semantic facts
    /// until the caller selects standalone 120/121 or bundled 129 representation.
    /// </summary>
    public OfflineDirectChunkWireEncodeStatus TryPrepareForRepresentation(
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore source,
        int subscriptionIndex,
        ulong cadenceStep,
        ulong deliveryId,
        Span<byte> snapshotDestination,
        out OfflineDirectChunkWireEncodeResult result)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (deliveryId == 0)
            return Finish(OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);

        RemoteChunkDeltaBodySharingMode sharing = _bodyStore?.Mode ??
            RemoteChunkDeltaBodySharingMode.Disabled;
        bool bodyLookupAlreadyAttempted = false;
        if (sharing == RemoteChunkDeltaBodySharingMode.Enabled)
        {
            long cachedRangeStarted = AttributionTimestamp();
            if (recipient.TryInspectDueDeltaRange(
                    source, subscriptionIndex, cadenceStep,
                    out OfflineDirectChunkSubscription subscription,
                    out ChunkPublicationBatch inspectedBatch))
            {
                bodyLookupAlreadyAttempted = true;
                var identity = new RemoteChunkDeltaBodyIdentity(
                    source.Epoch, inspectedBatch.Key, subscription.Cursor.Revision.Value,
                    inspectedBatch.Cursor.Revision.Value, inspectedBatch.HistoryFloor.Value);
                long lookupStarted = AttributionTimestamp();
                if (_attributionEnabled) SaturatingIncrement(ref _bodyRequests);
                bool hit = _bodyStore!.TryCopyEncoded(
                    source, in identity, _deltaRecordBytes,
                    out int cachedRecordCount, out int cachedEncodedBytes,
                    out ulong cachedHash);
                AttributeElapsed(ref _bodyLookupTicks, lookupStarted);
                if (hit)
                {
                    if (recipient.TryBeginCachedDeltaRead(
                            source, subscriptionIndex, cadenceStep, in inspectedBatch,
                            cachedRecordCount, out OfflineDirectChunkRead cachedRead))
                    {
                        if (_attributionEnabled)
                        {
                            SaturatingIncrement(ref _bodyHits);
                            SaturatingAdd(ref _bodyRecordsServed,
                                checked((ulong)cachedRecordCount));
                            SaturatingIncrement(ref _materializationsAvoided);
                            SaturatingIncrement(ref _validationHashCallsAvoided);
                        }
                        if (cachedEncodedBytes !=
                            cachedRecordCount * RemoteChunkDeltaRecord.EncodedSize)
                            throw new InvalidOperationException(
                                "A retained Delta body has divergent canonical byte length.");
                        Attribute(ref _sourceReadCalls, ref _sourceReadTicks, cachedRangeStarted);
                        OfflineDirectChunkDeliveryTicket cachedTicket =
                            recipient.ReleaseReadForRemote(deliveryId);
                        result = new OfflineDirectChunkWireEncodeResult(
                            OfflineDirectChunkWireEncodeStatus.Delta,
                            RemoteChunkPublicationKind.Delta,
                            UltraSimGameProtocol.GetChunkDeltaEncodedBytes(cachedRecordCount),
                            cachedTicket)
                        {
                            BaseRevision = subscription.Cursor.Revision.Value,
                            CurrentRevision = cachedRead.Batch.Cursor.Revision.Value,
                            HistoryFloor = cachedRead.Batch.HistoryFloor.Value,
                            ContentHash = cachedHash,
                            RecordCount = cachedRecordCount,
                        };
                        return result.Status;
                    }
                    _deltaRecordBytes.AsSpan(0, cachedEncodedBytes).Clear();
                }
                if (_attributionEnabled) SaturatingIncrement(ref _bodyMisses);
            }
            Attribute(ref _sourceReadCalls, ref _sourceReadTicks, cachedRangeStarted);
        }

        bool knownAbsence = subscriptionIndex >= 0 &&
            subscriptionIndex < recipient.Subscriptions.Length &&
            recipient.Subscriptions[subscriptionIndex].HasKnowledge &&
            !recipient.Subscriptions[subscriptionIndex].Cursor.IsValid;
        long sourceReadStarted = AttributionTimestamp();
        OfflineDirectChunkReadStatus readStatus = recipient.TryBeginRead(
            source, subscriptionIndex, cadenceStep, out OfflineDirectChunkRead read);
        Attribute(ref _sourceReadCalls, ref _sourceReadTicks, sourceReadStarted);
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
                return EncodeSnapshot(recipient, deliveryId, snapshotDestination, in read, out result);
            case OfflineDirectChunkReadStatus.Delta:
                return PrepareDelta(
                    recipient, source, subscriptionIndex, deliveryId, in read,
                    bodyLookupAlreadyAttempted, out result);
            case OfflineDirectChunkReadStatus.ChunkAbsent:
                return PrepareAbsence(recipient, deliveryId, in read, out result);
            default:
                throw new InvalidOperationException($"Unexpected N1.1 read status {readStatus}.");
        }
    }

    /// <summary>Records and performs the selected canonical standalone representation.</summary>
    public bool TryEncodePreparedStandalone(
        in OfflineDirectChunkWireEncodeResult result,
        ReadOnlySpan<RemoteChunkDeltaRecord> deltaRecords,
        Span<byte> destination,
        out int written)
    {
        EnsureOwner();
        written = 0;
        long encodeStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _canonicalStandaloneEncodeCalls);
        bool encoded = result.Kind switch
        {
            RemoteChunkPublicationKind.Delta when deltaRecords.Length == result.RecordCount =>
                UltraSimGameProtocol.TryWritePreparedChunkDelta(
                    destination, _applicationSessionEpoch, result.Ticket.SourceEpoch.Value,
                    result.Ticket.DeliveryId, result.Ticket.Key.Space.Value,
                    result.Ticket.Key.Coord.X, result.Ticket.Key.Coord.Z,
                    result.BaseRevision, result.CurrentRevision, result.HistoryFloor,
                    result.ContentHash, deltaRecords, out written),
            RemoteChunkPublicationKind.Absence when result.RecordCount == 0 && deltaRecords.IsEmpty =>
                UltraSimGameProtocol.TryWriteChunkAbsence(
                    destination, _applicationSessionEpoch, result.Ticket.SourceEpoch.Value,
                    result.Ticket.DeliveryId, result.Ticket.Key.Space.Value,
                    result.Ticket.Key.Coord.X, result.Ticket.Key.Coord.Z, out written),
            _ => false,
        };
        AttributeElapsed(ref _canonicalStandaloneEncodeTicks, encodeStarted);
        if (_attributionEnabled && encoded)
        {
            SaturatingAdd(ref _canonicalStandaloneBytes, checked((ulong)written));
            if (result.Kind == RemoteChunkPublicationKind.Delta)
                SaturatingIncrement(ref _preparedStandaloneWrites);
        }
        return encoded && written == result.Written;
    }

    /// <summary>Writes a prepared standalone publication from canonical retained record bytes.</summary>
    public bool TryEncodePreparedStandaloneBytes(
        in OfflineDirectChunkWireEncodeResult result,
        ReadOnlySpan<byte> encodedDeltaRecords,
        Span<byte> destination,
        out int written)
    {
        EnsureOwner();
        written = 0;
        long encodeStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _canonicalStandaloneEncodeCalls);
        bool encoded = result.Kind switch
        {
            RemoteChunkPublicationKind.Delta when
                encodedDeltaRecords.Length ==
                    result.RecordCount * RemoteChunkDeltaRecord.EncodedSize =>
                UltraSimGameProtocol.TryWritePreparedChunkDeltaBytes(
                    destination, _applicationSessionEpoch, result.Ticket.SourceEpoch.Value,
                    result.Ticket.DeliveryId, result.Ticket.Key.Space.Value,
                    result.Ticket.Key.Coord.X, result.Ticket.Key.Coord.Z,
                    result.BaseRevision, result.CurrentRevision, result.HistoryFloor,
                    result.ContentHash, encodedDeltaRecords, out written),
            RemoteChunkPublicationKind.Absence when
                result.RecordCount == 0 && encodedDeltaRecords.IsEmpty =>
                UltraSimGameProtocol.TryWriteChunkAbsence(
                    destination, _applicationSessionEpoch, result.Ticket.SourceEpoch.Value,
                    result.Ticket.DeliveryId, result.Ticket.Key.Space.Value,
                    result.Ticket.Key.Coord.X, result.Ticket.Key.Coord.Z, out written),
            _ => false,
        };
        AttributeElapsed(ref _canonicalStandaloneEncodeTicks, encodeStarted);
        if (_attributionEnabled && encoded)
        {
            SaturatingAdd(ref _canonicalStandaloneBytes, checked((ulong)written));
            if (result.Kind == RemoteChunkPublicationKind.Delta)
                SaturatingIncrement(ref _preparedStandaloneWrites);
        }
        return encoded && written == result.Written;
    }

    /// <summary>
    /// The validated records from the latest successful Delta preparation. The owner must copy or
    /// encode them before the next encoder operation; the span never escapes this owner lifetime.
    /// </summary>
    public ReadOnlySpan<RemoteChunkDeltaRecord> PreparedDeltaRecords(int expectedCount)
    {
        EnsureOwner();
        if ((uint)expectedCount > (uint)_deltaRecords.Length)
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        int encodedBytes = checked(expectedCount * RemoteChunkDeltaRecord.EncodedSize);
        if (!UltraSimGameProtocol.TryReadPreparedDeltaRecordBytes(
                _deltaRecordBytes.AsSpan(0, encodedBytes), _deltaRecords,
                out int decoded) || decoded != expectedCount)
            throw new InvalidOperationException(
                "Prepared canonical Delta bytes could not be materialized for diagnostics.");
        return _deltaRecords.AsSpan(0, expectedCount);
    }

    public ReadOnlySpan<byte> PreparedDeltaRecordBytes(int expectedCount)
    {
        EnsureOwner();
        if ((uint)expectedCount > (uint)_deltaRecords.Length)
            throw new ArgumentOutOfRangeException(nameof(expectedCount));
        return _deltaRecordBytes.AsSpan(
            0, checked(expectedCount * RemoteChunkDeltaRecord.EncodedSize));
    }

    /// <summary>Encodes one caller-authorized complete repair snapshot regardless of cursor state.</summary>
    public OfflineDirectChunkWireEncodeStatus TryEncodeSnapshot(
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore source,
        int subscriptionIndex,
        ulong deliveryId,
        Span<byte> destination,
        out OfflineDirectChunkWireEncodeResult result)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(source);
        EnsureOwner();
        if (deliveryId == 0)
            return Finish(OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        OfflineDirectChunkReadStatus status = recipient.TryBeginSnapshotRead(
            source, subscriptionIndex, out OfflineDirectChunkRead read);
        return status switch
        {
            OfflineDirectChunkReadStatus.Snapshot =>
                EncodeSnapshot(recipient, deliveryId, destination, in read, out result),
            OfflineDirectChunkReadStatus.ChunkAbsent =>
                EncodeAbsence(recipient, deliveryId, destination, in read, out result),
            OfflineDirectChunkReadStatus.InvalidSubscription =>
                Finish(OfflineDirectChunkWireEncodeStatus.InvalidSubscription, out result),
            OfflineDirectChunkReadStatus.SourceRequiresReconcile =>
                Finish(OfflineDirectChunkWireEncodeStatus.SourceRequiresReconcile, out result),
            OfflineDirectChunkReadStatus.ReadPending =>
                Finish(OfflineDirectChunkWireEncodeStatus.ReadPending, out result),
            OfflineDirectChunkReadStatus.SnapshotCapacityExceeded =>
                Finish(OfflineDirectChunkWireEncodeStatus.CapacityExceeded, out result),
            _ => throw new InvalidOperationException($"Unexpected forced snapshot status {status}."),
        };
    }

    public bool TryCommitAppliedProof(
        OfflineDirectChunkRecipient recipient,
        in OfflineDirectChunkDeliveryTicket ticket,
        in RemoteChunkAppliedMessage proof,
        out OfflineDirectChunkAppliedProofStatus status)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        EnsureOwner();
        long validationStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _proofValidationCalls);
        bool valid = proof.ApplicationSessionEpoch == _applicationSessionEpoch &&
            proof.PublicationEpoch == ticket.SourceEpoch.Value &&
            proof.DeliveryId == ticket.DeliveryId &&
            proof.SpatialSpaceId == ticket.Key.Space.Value &&
            proof.ChunkX == ticket.Key.Coord.X && proof.ChunkZ == ticket.Key.Coord.Z &&
            proof.Kind == Kind(ticket.Status) &&
            proof.Revision == (ticket.Cursor.IsValid ? ticket.Cursor.Revision.Value : 0) &&
            proof.Status == RemoteChunkPublicationApplyStatus.Applied;
        AttributeElapsed(ref _proofValidationTicks, validationStarted);
        if (!valid)
        {
            status = OfflineDirectChunkAppliedProofStatus.Invalid;
            return false;
        }
        long advancementStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _cursorAdvancementCalls);
        status = recipient.CommitAppliedProof(ticket);
        AttributeElapsed(ref _cursorAdvancementTicks, advancementStarted);
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
            RemoteChunkPublicationKind.Snapshot, written, ticket)
        {
            CurrentRevision = read.Batch.Cursor.Revision.Value,
            HistoryFloor = read.Batch.HistoryFloor.Value,
            RecordCount = read.Snapshot.Length,
        };
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
        long materializationStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _deltaMaterializationCalls);
        MaterializeDeltaRecords(read.Deltas, _deltaRecords);
        if (_attributionEnabled)
            SaturatingAdd(ref _deltaRecordsMaterialized, checked((ulong)read.Deltas.Length));
        AttributeElapsed(ref _deltaMaterializationTicks, materializationStarted);
        long encodeStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _canonicalStandaloneEncodeCalls);
        bool encoded = UltraSimGameProtocol.TryWriteChunkDelta(
            destination, _applicationSessionEpoch, read.SourceEpoch.Value, deliveryId,
            read.Batch.Key.Space.Value, read.Batch.Key.Coord.X, read.Batch.Key.Coord.Z,
            baseRevision.Value, read.Batch.Cursor.Revision.Value, read.Batch.HistoryFloor.Value,
            _deltaRecords.AsSpan(0, read.Deltas.Length), out int written);
        AttributeElapsed(ref _canonicalStandaloneEncodeTicks, encodeStarted);
        if (_attributionEnabled && encoded)
            SaturatingAdd(ref _canonicalStandaloneBytes, checked((ulong)written));
        _deltaRecords.AsSpan(0, read.Deltas.Length).Clear();
        if (!encoded)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
        result = new(OfflineDirectChunkWireEncodeStatus.Delta,
            RemoteChunkPublicationKind.Delta, written, ticket)
        {
            BaseRevision = baseRevision.Value,
            CurrentRevision = read.Batch.Cursor.Revision.Value,
            HistoryFloor = read.Batch.HistoryFloor.Value,
            RecordCount = read.Deltas.Length,
        };
        return result.Status;
    }

    private OfflineDirectChunkWireEncodeStatus PrepareDelta(
        OfflineDirectChunkRecipient recipient,
        ChunkPublicationStore source,
        int subscriptionIndex,
        ulong deliveryId,
        in OfflineDirectChunkRead read,
        bool bodyLookupAlreadyAttempted,
        out OfflineDirectChunkWireEncodeResult result)
    {
        if (read.Deltas.Length > _deltaRecords.Length)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.CapacityExceeded, out result);
        ChunkPublicationRevision baseRevision =
            recipient.Subscriptions[subscriptionIndex].Cursor.Revision;
        var bodyIdentity = new RemoteChunkDeltaBodyIdentity(
            read.SourceEpoch, read.Batch.Key, baseRevision.Value,
            read.Batch.Cursor.Revision.Value, read.Batch.HistoryFloor.Value);
        RemoteChunkDeltaBodySharingMode sharing = _bodyStore?.Mode ??
            RemoteChunkDeltaBodySharingMode.Disabled;
        if (sharing == RemoteChunkDeltaBodySharingMode.Enabled &&
            !bodyLookupAlreadyAttempted)
        {
            long lookupStarted = AttributionTimestamp();
            if (_attributionEnabled) SaturatingIncrement(ref _bodyRequests);
            bool hit = _bodyStore!.TryCopyEncoded(
                source, in bodyIdentity, _deltaRecordBytes,
                out int cachedRecordCount, out int cachedEncodedBytes,
                out ulong cachedHash);
            AttributeElapsed(ref _bodyLookupTicks, lookupStarted);
            if (hit)
            {
                if (cachedRecordCount != read.Deltas.Length)
                    throw new InvalidOperationException(
                        "An exact Delta-body identity retained a divergent record count.");
                if (cachedEncodedBytes !=
                    cachedRecordCount * RemoteChunkDeltaRecord.EncodedSize)
                    throw new InvalidOperationException(
                        "An exact Delta-body identity retained divergent canonical bytes.");
                if (_attributionEnabled)
                {
                    SaturatingIncrement(ref _bodyHits);
                    SaturatingAdd(ref _bodyRecordsServed, checked((ulong)read.Deltas.Length));
                    SaturatingIncrement(ref _materializationsAvoided);
                    SaturatingIncrement(ref _validationHashCallsAvoided);
                }
                OfflineDirectChunkDeliveryTicket cachedTicket =
                    recipient.ReleaseReadForRemote(deliveryId);
                result = new(OfflineDirectChunkWireEncodeStatus.Delta,
                    RemoteChunkPublicationKind.Delta,
                    UltraSimGameProtocol.GetChunkDeltaEncodedBytes(read.Deltas.Length), cachedTicket)
                {
                    BaseRevision = baseRevision.Value,
                    CurrentRevision = read.Batch.Cursor.Revision.Value,
                    HistoryFloor = read.Batch.HistoryFloor.Value,
                    ContentHash = cachedHash,
                    RecordCount = read.Deltas.Length,
                };
                return result.Status;
            }
            if (_attributionEnabled) SaturatingIncrement(ref _bodyMisses);
        }
        long materializationStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _deltaMaterializationCalls);
        MaterializeDeltaRecords(read.Deltas, _deltaRecords);
        if (_attributionEnabled)
            SaturatingAdd(ref _deltaRecordsMaterialized, checked((ulong)read.Deltas.Length));
        AttributeElapsed(ref _deltaMaterializationTicks, materializationStarted);
        ReadOnlySpan<RemoteChunkDeltaRecord> records = _deltaRecords.AsSpan(0, read.Deltas.Length);
        long validationHashStarted = AttributionTimestamp();
        if (_attributionEnabled)
        {
            SaturatingIncrement(ref _preparedValidationHashCalls);
            SaturatingIncrement(ref _preparedContentHashCalls);
            SaturatingAdd(ref _preparedRecordsValidated, checked((ulong)records.Length));
        }
        bool valid = UltraSimGameProtocol.TryGetChunkDeltaContentHash(
                read.SourceEpoch.Value, read.Batch.Key.Space.Value,
                read.Batch.Key.Coord.X, read.Batch.Key.Coord.Z,
                baseRevision.Value, read.Batch.Cursor.Revision.Value,
                read.Batch.HistoryFloor.Value, records, out ulong contentHash);
        AttributeElapsed(ref _preparedValidationHashTicks, validationHashStarted);
        if (!valid)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        int encodedRecordBytes = checked(records.Length * RemoteChunkDeltaRecord.EncodedSize);
        if (!UltraSimGameProtocol.TryWritePreparedDeltaRecordBytes(
                records, _deltaRecordBytes, out int encodedWritten) ||
            encodedWritten != encodedRecordBytes)
            return Cancel(recipient, OfflineDirectChunkWireEncodeStatus.EncodingFailed, out result);
        if (sharing != RemoteChunkDeltaBodySharingMode.Disabled)
        {
            if (_attributionEnabled && sharing == RemoteChunkDeltaBodySharingMode.CensusOnly)
                SaturatingIncrement(ref _bodyRequests);
            long fillStarted = AttributionTimestamp();
            RemoteChunkDeltaBodyPublishStatus publish = _bodyStore!.PublishPrepared(
                source, in bodyIdentity, records,
                _deltaRecordBytes.AsSpan(0, encodedRecordBytes), contentHash,
                requestAlreadyCounted: sharing == RemoteChunkDeltaBodySharingMode.Enabled);
            AttributeElapsed(ref _bodyFillTicks, fillStarted);
            if (publish == RemoteChunkDeltaBodyPublishStatus.IdentityConflict)
                throw new InvalidOperationException(
                    "One exact source Delta-body identity produced different canonical records.");
            if (_attributionEnabled)
            {
                if (publish == RemoteChunkDeltaBodyPublishStatus.Published)
                    SaturatingAdd(ref _bodyRecordsBuilt, checked((ulong)records.Length));
                else if (sharing == RemoteChunkDeltaBodySharingMode.CensusOnly &&
                         publish == RemoteChunkDeltaBodyPublishStatus.ExistingExact)
                {
                    SaturatingIncrement(ref _bodyHits);
                    SaturatingAdd(ref _bodyRecordsServed, checked((ulong)records.Length));
                }
            }
        }
        OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
        result = new(OfflineDirectChunkWireEncodeStatus.Delta,
            RemoteChunkPublicationKind.Delta,
            UltraSimGameProtocol.GetChunkDeltaEncodedBytes(read.Deltas.Length), ticket)
        {
            BaseRevision = baseRevision.Value,
            CurrentRevision = read.Batch.Cursor.Revision.Value,
            HistoryFloor = read.Batch.HistoryFloor.Value,
            ContentHash = contentHash,
            RecordCount = read.Deltas.Length,
        };
        return result.Status;
    }

    private OfflineDirectChunkWireEncodeStatus PrepareAbsence(
        OfflineDirectChunkRecipient recipient,
        ulong deliveryId,
        in OfflineDirectChunkRead read,
        out OfflineDirectChunkWireEncodeResult result)
    {
        OfflineDirectChunkDeliveryTicket ticket = recipient.ReleaseReadForRemote(deliveryId);
        result = new(OfflineDirectChunkWireEncodeStatus.Absence,
            RemoteChunkPublicationKind.Absence, RemoteChunkAbsenceMessageCodec.EncodedSize, ticket);
        return result.Status;
    }

    private static void MaterializeDeltaRecords(
        ReadOnlySpan<WorldPublicDeltaRecord> source,
        Span<RemoteChunkDeltaRecord> destination)
    {
        for (int index = 0; index < source.Length; index++)
        {
            ref readonly WorldPublicDeltaRecord delta = ref source[index];
            WorldPublicRecord current = delta.Current;
            destination[index] = new RemoteChunkDeltaRecord(
                (RemoteChunkPublicOperation)delta.Operation,
                (RemoteChunkPublicFieldMask)delta.Fields,
                delta.Id.Index, delta.Id.Generation, delta.EntityPublicVersion,
                current.PrefabId, current.Position.Local.XMillimeters,
                current.Position.Local.ZMillimeters, current.Position.YMillimeters,
                current.Orientation.Units, (ushort)current.StateFlags,
                current.LastChangedRevision.Value);
        }
    }

    private OfflineDirectChunkWireEncodeStatus EncodeAbsence(
        OfflineDirectChunkRecipient recipient,
        ulong deliveryId,
        Span<byte> destination,
        in OfflineDirectChunkRead read,
        out OfflineDirectChunkWireEncodeResult result)
    {
        long encodeStarted = AttributionTimestamp();
        if (_attributionEnabled) SaturatingIncrement(ref _canonicalStandaloneEncodeCalls);
        bool encoded = UltraSimGameProtocol.TryWriteChunkAbsence(
            destination, _applicationSessionEpoch, read.SourceEpoch.Value, deliveryId,
            read.Batch.Key.Space.Value, read.Batch.Key.Coord.X, read.Batch.Key.Coord.Z,
            out int written);
        AttributeElapsed(ref _canonicalStandaloneEncodeTicks, encodeStarted);
        if (_attributionEnabled && encoded)
            SaturatingAdd(ref _canonicalStandaloneBytes, checked((ulong)written));
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

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private long AttributionTimestamp() =>
        _attributionEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void Attribute(ref ulong calls, ref ulong ticks, long started)
    {
        if (!_attributionEnabled) return;
        SaturatingIncrement(ref calls);
        AttributeElapsed(ref ticks, started);
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void AttributeElapsed(ref ulong destination, long started)
    {
        if (!_attributionEnabled) return;
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        if (elapsed > 0) SaturatingAdd(ref destination, checked((ulong)elapsed));
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void SaturatingAdd(ref ulong destination, ulong value) =>
        destination = ulong.MaxValue - destination < value ? ulong.MaxValue : destination + value;
}
