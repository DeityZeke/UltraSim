using System.Runtime.CompilerServices;
using UltraSim.Client.Core;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Client.Network;

public readonly record struct RemoteChunkWorldStateClientSnapshot(
    int PendingProofs,
    int PendingProofCapacity,
    ulong AppliedDeltas,
    ulong AppliedAbsences,
    ulong DuplicateDeliveries,
    ulong RejectedDeliveries,
    ulong CapacityFailures,
    ulong ContradictoryDeliveries,
    ulong MalformedDeliveries,
    ulong ProofsQueued,
    long ReservedBytes);

/// <summary>
/// N1.2 client bridge for ordinary unfragmented chunk deltas and absences. Decoding and semantic
/// application complete atomically before an application proof becomes eligible for transport.
/// Complete snapshot recovery remains N1.3.
/// </summary>
public sealed class RemoteChunkWorldStateClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ClientWorldSemanticCache _cache;
    private readonly ulong _applicationSessionEpoch;
    private readonly RemoteChunkDeltaRecord[] _wireDeltas;
    private readonly WorldPublicDeltaRecord[] _semanticDeltas;
    private readonly RemoteChunkAppliedMessage[] _proofs;
    private readonly byte[] _proofScratch = new byte[RemoteChunkAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _proofHead;
    private int _proofCount;
    private ulong _appliedDeltas;
    private ulong _appliedAbsences;
    private ulong _duplicateDeliveries;
    private ulong _rejectedDeliveries;
    private ulong _capacityFailures;
    private ulong _contradictoryDeliveries;
    private ulong _malformedDeliveries;
    private ulong _proofsQueued;

    public RemoteChunkWorldStateClient(
        NetworkProtocolPeer peer,
        ClientWorldSemanticCache cache,
        ulong applicationSessionEpoch,
        int maximumDeltaRecords = UltraSimGameProtocol.MaximumChunkPublicationRecords,
        int maximumPendingProofs = 128)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (maximumDeltaRecords is <= 0 or > UltraSimGameProtocol.MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumDeltaRecords));
        if (maximumDeltaRecords > cache.Limits.MaximumPublicRecordsPerChunk)
            throw new ArgumentOutOfRangeException(nameof(maximumDeltaRecords));
        if (maximumPendingProofs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingProofs));
        _applicationSessionEpoch = applicationSessionEpoch;
        _wireDeltas = new RemoteChunkDeltaRecord[maximumDeltaRecords];
        _semanticDeltas = new WorldPublicDeltaRecord[maximumDeltaRecords];
        _proofs = new RemoteChunkAppliedMessage[maximumPendingProofs];
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId == RemoteChunkDeltaHeaderCodec.Descriptor.Id)
        {
            ProcessDelta(payload);
            return true;
        }
        if (message.MessageId == RemoteChunkAbsenceMessageCodec.Descriptor.Id)
        {
            ProcessAbsence(payload);
            return true;
        }
        return false;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        while (_proofCount != 0)
        {
            ref readonly RemoteChunkAppliedMessage proof = ref _proofs[_proofHead];
            if (!UltraSimGameProtocol.TryWriteChunkApplied(_proofScratch, proof, out int written))
                throw new InvalidOperationException("A retained chunk proof became non-canonical.");
            if (_peer.TryQueue(RemoteChunkAppliedMessageCodec.Descriptor.Id,
                    _proofScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
                return false;
            _proofs[_proofHead] = default;
            _proofHead = (_proofHead + 1) % _proofs.Length;
            _proofCount--;
            SaturatingIncrement(ref _proofsQueued);
        }
        return true;
    }

    public RemoteChunkWorldStateClientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new RemoteChunkWorldStateClientSnapshot(
            _proofCount,
            _proofs.Length,
            _appliedDeltas,
            _appliedAbsences,
            _duplicateDeliveries,
            _rejectedDeliveries,
            _capacityFailures,
            _contradictoryDeliveries,
            _malformedDeliveries,
            _proofsQueued,
            checked((long)_wireDeltas.Length * Unsafe.SizeOf<RemoteChunkDeltaRecord>() +
                (long)_semanticDeltas.Length * Unsafe.SizeOf<WorldPublicDeltaRecord>() +
                (long)_proofs.Length * Unsafe.SizeOf<RemoteChunkAppliedMessage>() +
                _proofScratch.Length));
    }

    private void ProcessDelta(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkDelta(
                payload, out RemoteChunkDeltaHeader header, _wireDeltas, out int count))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (header.ApplicationSessionEpoch != _applicationSessionEpoch || _proofCount == _proofs.Length)
        {
            SaturatingIncrement(ref _rejectedDeliveries);
            _wireDeltas.AsSpan(0, count).Clear();
            return;
        }

        var epoch = new PublicationEpoch(header.PublicationEpoch);
        var key = new SpatialChunkKey(new SpatialSpaceId(header.SpatialSpaceId), header.ChunkX, header.ChunkZ);
        RemoteChunkPublicationApplyStatus status;
        bool duplicate = false;
        if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle) ||
            !_cache.TryGetInfo(handle, out ClientSemanticChunkInfo info) ||
            !info.PublicationCursor.IsValid || info.PublicationCursor.Epoch != epoch)
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else if (info.PublicationCursor.Revision.Value >= header.CurrentRevision)
        {
            status = RemoteChunkPublicationApplyStatus.Applied;
            duplicate = true;
        }
        else if (info.PublicationCursor.Revision.Value != header.BaseRevision)
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else if (!FitsWorldProfile(_wireDeltas.AsSpan(0, count)))
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else
        {
            for (int index = 0; index < count; index++)
                _semanticDeltas[index] = ToSemantic(key, _wireDeltas[index]);
            var cursor = new ChunkPublicationCursor(
                epoch, key, new ChunkPublicationRevision(header.CurrentRevision));
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Delta,
                key,
                cursor,
                new ChunkPublicationRevision(header.HistoryFloor),
                count,
                count);
            bool applied = _cache.TryApplyDelta(
                handle, batch, _semanticDeltas.AsSpan(0, count), out ClientSemanticChunkError error);
            status = applied ? RemoteChunkPublicationApplyStatus.Applied : Map(error);
        }

        QueueProof(header.ApplicationSessionEpoch, header.PublicationEpoch, header.DeliveryId,
            header.SpatialSpaceId, header.ChunkX, header.ChunkZ, header.CurrentRevision,
            RemoteChunkPublicationKind.Delta, status);
        if (status == RemoteChunkPublicationApplyStatus.Applied)
        {
            if (duplicate) SaturatingIncrement(ref _duplicateDeliveries);
            else SaturatingIncrement(ref _appliedDeltas);
        }
        else RecordFailure(status);
        _wireDeltas.AsSpan(0, count).Clear();
        _semanticDeltas.AsSpan(0, count).Clear();
    }

    private void ProcessAbsence(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadChunkAbsence(
                payload, out RemoteChunkAbsenceMessage message))
        {
            SaturatingIncrement(ref _malformedDeliveries);
            return;
        }
        if (message.ApplicationSessionEpoch != _applicationSessionEpoch || _proofCount == _proofs.Length)
        {
            SaturatingIncrement(ref _rejectedDeliveries);
            return;
        }
        var epoch = new PublicationEpoch(message.PublicationEpoch);
        var key = new SpatialChunkKey(
            new SpatialSpaceId(message.SpatialSpaceId), message.ChunkX, message.ChunkZ);
        RemoteChunkPublicationApplyStatus status;
        if (!_cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle))
        {
            status = RemoteChunkPublicationApplyStatus.Rejected;
        }
        else
        {
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.ChunkAbsent, key, default, default, 0, 0);
            bool applied = _cache.TryApplyChunkAbsent(handle, epoch, batch,
                out ClientSemanticChunkError error);
            status = applied ? RemoteChunkPublicationApplyStatus.Applied : Map(error);
        }
        QueueProof(message.ApplicationSessionEpoch, message.PublicationEpoch, message.DeliveryId,
            message.SpatialSpaceId, message.ChunkX, message.ChunkZ, 0,
            RemoteChunkPublicationKind.Absence, status);
        if (status == RemoteChunkPublicationApplyStatus.Applied)
            SaturatingIncrement(ref _appliedAbsences);
        else
            RecordFailure(status);
    }

    private void QueueProof(
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        ulong spatialSpaceId,
        int chunkX,
        int chunkZ,
        ulong revision,
        RemoteChunkPublicationKind kind,
        RemoteChunkPublicationApplyStatus status)
    {
        int tail = (_proofHead + _proofCount) % _proofs.Length;
        _proofs[tail] = new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = spatialSpaceId,
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Revision = revision,
            Kind = kind,
            Status = status,
        };
        _proofCount++;
    }

    private static WorldPublicDeltaRecord ToSemantic(
        SpatialChunkKey key,
        in RemoteChunkDeltaRecord record)
    {
        var id = new WorldPublicEntityId(record.EntityIndex, record.EntityGeneration);
        WorldPublicOperation operation = (WorldPublicOperation)record.Operation;
        WorldPublicRecord current = operation == WorldPublicOperation.Present
            ? new WorldPublicRecord(
                id,
                record.EntityPublicVersion,
                record.PrefabId,
                new WorldPosition(
                    key.Coord,
                    new WorldLocalPosition(
                        record.LocalXMillimeters, record.LocalZMillimeters, record.YMillimeters)),
                new WorldYaw(record.OrientationUnits),
                (WorldPublicStateFlags)record.StateFlags,
                new ChunkPublicationRevision(record.LastChangedRevision))
            : new WorldPublicRecord(
                id, record.EntityPublicVersion, 0, default, default, default, default);
        return new WorldPublicDeltaRecord(
            operation,
            (WorldPublicFieldMask)record.Fields,
            id,
            record.EntityPublicVersion,
            current);
    }

    private static bool FitsWorldProfile(ReadOnlySpan<RemoteChunkDeltaRecord> records)
    {
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly RemoteChunkDeltaRecord record = ref records[index];
            if (record.Operation == RemoteChunkPublicOperation.Present &&
                (record.LocalXMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits ||
                 record.LocalZMillimeters >= GameWorldSpatialProfile.WorldChunkEdgeUnits))
                return false;
        }
        return true;
    }

    private static RemoteChunkPublicationApplyStatus Map(ClientSemanticChunkError error) => error switch
    {
        ClientSemanticChunkError.CapacityExceeded or
        ClientSemanticChunkError.PublicRecordCapacityExceeded or
        ClientSemanticChunkError.KnownIdentityCapacityExceeded =>
            RemoteChunkPublicationApplyStatus.CapacityExceeded,
        ClientSemanticChunkError.ContradictoryPublication =>
            RemoteChunkPublicationApplyStatus.Contradictory,
        _ => RemoteChunkPublicationApplyStatus.Rejected,
    };

    private void RecordFailure(RemoteChunkPublicationApplyStatus status)
    {
        switch (status)
        {
            case RemoteChunkPublicationApplyStatus.CapacityExceeded:
                SaturatingIncrement(ref _capacityFailures);
                break;
            case RemoteChunkPublicationApplyStatus.Contradictory:
                SaturatingIncrement(ref _contradictoryDeliveries);
                break;
            default:
                SaturatingIncrement(ref _rejectedDeliveries);
                break;
        }
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote chunk WorldState client is owner-thread bound.");
    }
}
