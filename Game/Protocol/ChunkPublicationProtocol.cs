using System.Runtime.InteropServices;
using UltraSim.Network;

namespace UltraSim.Game.Protocol;

public enum RemoteChunkPublicationKind : byte
{
    Snapshot = 1,
    Delta = 2,
    Absence = 3,
}

public enum RemoteChunkPublicationApplyStatus : byte
{
    Applied = 1,
    Rejected = 2,
    CapacityExceeded = 3,
    Contradictory = 4,
}

public enum RemoteChunkPublicOperation : byte
{
    Present = 1,
    Remove = 2,
}

[Flags]
public enum RemoteChunkPublicFieldMask : ushort
{
    None = 0,
    Transform = 1 << 0,
    Prefab = 1 << 1,
    Orientation = 1 << 2,
    StateFlags = 1 << 3,
    Complete = Transform | Prefab | Orientation | StateFlags,
}

[NetworkMessage(119, "ultrasim.game.chunk-snapshot", 1, NetworkLane.Bulk)]
public partial struct RemoteChunkSnapshotHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ulong SpatialSpaceId;
    public int ChunkX;
    public int ChunkZ;
    public ulong SchemaFingerprint;
    public ulong Revision;
    public ulong HistoryFloor;
    public ulong ContentHash;
    public ushort RecordCount;
}

[NetworkMessage(120, "ultrasim.game.chunk-delta", 1, NetworkLane.WorldState)]
public partial struct RemoteChunkDeltaHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ulong SpatialSpaceId;
    public int ChunkX;
    public int ChunkZ;
    public ulong SchemaFingerprint;
    public ulong BaseRevision;
    public ulong CurrentRevision;
    public ulong HistoryFloor;
    public ulong ContentHash;
    public ushort RecordCount;
}

[NetworkMessage(121, "ultrasim.game.chunk-absence", 1, NetworkLane.WorldState)]
public partial struct RemoteChunkAbsenceMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ulong SpatialSpaceId;
    public int ChunkX;
    public int ChunkZ;
    public ulong SchemaFingerprint;
}

/// <summary>
/// Application-level proof for one exact chunk delivery. Transport submission or acknowledgement
/// is not application knowledge; only an authenticated, validated Applied proof may advance it.
/// </summary>
[NetworkMessage(122, "ultrasim.game.chunk-applied", 1, NetworkLane.WorldState)]
public partial struct RemoteChunkAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ulong SpatialSpaceId;
    public int ChunkX;
    public int ChunkZ;
    public ulong Revision;
    public RemoteChunkPublicationKind Kind;
    public RemoteChunkPublicationApplyStatus Status;
}

/// <summary>Declares one bounded atomic transfer of a canonical chunk-snapshot body.</summary>
[NetworkMessage(123, "ultrasim.game.chunk-snapshot-begin", 1, NetworkLane.Bulk)]
public partial struct RemoteChunkSnapshotBeginMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ulong SpatialSpaceId;
    public int ChunkX;
    public int ChunkZ;
    public ulong SchemaFingerprint;
    public ulong Revision;
    public ulong HistoryFloor;
    public ulong ContentHash;
    public uint EncodedBytes;
    public ushort RecordCount;
    public ushort SegmentCount;
    public ushort SegmentPayloadBytes;
}

/// <summary>One independently reliable bounded byte range of a canonical chunk snapshot.</summary>
[NetworkMessage(124, "ultrasim.game.chunk-snapshot-segment", 1, NetworkLane.Bulk)]
public partial struct RemoteChunkSnapshotSegmentHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ushort SegmentIndex;
    public ushort SegmentCount;
    public ushort PayloadBytes;
}

/// <summary>Signals that a complete staged chunk snapshot may be validated and published.</summary>
[NetworkMessage(125, "ultrasim.game.chunk-snapshot-commit", 1, NetworkLane.Bulk)]
public partial struct RemoteChunkSnapshotCommitMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
    public ulong SpatialSpaceId;
    public int ChunkX;
    public int ChunkZ;
    public ulong Revision;
    public ulong ContentHash;
}

/// <summary>Reliably abandons one private chunk-snapshot candidate without changing live state.</summary>
[NetworkMessage(126, "ultrasim.game.chunk-snapshot-cancel", 1, NetworkLane.Bulk)]
public partial struct RemoteChunkSnapshotCancelMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong PublicationEpoch;
    public ulong DeliveryId;
}

/// <summary>One complete public row; the enclosing chunk supplies global X/Z high bits.</summary>
public readonly record struct RemoteChunkPublicRecord(
    uint EntityIndex,
    uint EntityGeneration,
    ulong EntityPublicVersion,
    uint PrefabId,
    ushort LocalXMillimeters,
    ushort LocalZMillimeters,
    int YMillimeters,
    ushort OrientationUnits,
    ushort StateFlags,
    ulong LastChangedRevision)
{
    public const int EncodedSize = 40;
}

/// <summary>One coalesced public operation in ascending reusable public-index order.</summary>
public readonly record struct RemoteChunkDeltaRecord(
    RemoteChunkPublicOperation Operation,
    RemoteChunkPublicFieldMask Fields,
    uint EntityIndex,
    uint EntityGeneration,
    ulong EntityPublicVersion,
    uint PrefabId,
    ushort LocalXMillimeters,
    ushort LocalZMillimeters,
    int YMillimeters,
    ushort OrientationUnits,
    ushort StateFlags,
    ulong LastChangedRevision)
{
    public const int EncodedSize = 43;
}

public static partial class UltraSimGameProtocol
{
    public const int MaximumChunkPublicationRecords = 64;
    public const int MaximumChunkSnapshotSegments = 64;
    public const ulong ChunkPublicationSchemaFingerprint = 0x5553_4350_4231_3031UL; // USCPB101

    public static int GetChunkSnapshotEncodedBytes(int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        if (recordCount > MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        return checked(RemoteChunkSnapshotHeaderCodec.EncodedSize +
            recordCount * RemoteChunkPublicRecord.EncodedSize);
    }

    public static int GetChunkDeltaEncodedBytes(int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        if (recordCount > MaximumChunkPublicationRecords)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        return checked(RemoteChunkDeltaHeaderCodec.EncodedSize +
            recordCount * RemoteChunkDeltaRecord.EncodedSize);
    }

    public static int GetMaximumChunkDeltaRecordsForPayload(int maximumPayloadBytes)
    {
        if (maximumPayloadBytes <= RemoteChunkDeltaHeaderCodec.EncodedSize) return 0;
        return Math.Min(MaximumChunkPublicationRecords,
            (maximumPayloadBytes - RemoteChunkDeltaHeaderCodec.EncodedSize) /
            RemoteChunkDeltaRecord.EncodedSize);
    }

    public static int GetChunkSnapshotSegmentEncodedBytes(int payloadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadBytes);
        if (payloadBytes > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payloadBytes));
        return checked(RemoteChunkSnapshotSegmentHeaderCodec.EncodedSize + payloadBytes);
    }

    public static bool TryWriteChunkSnapshotBegin(
        Span<byte> destination,
        in RemoteChunkSnapshotBeginMessage message,
        out int written)
    {
        written = 0;
        if (!ValidSnapshotBegin(message) ||
            destination.Length < RemoteChunkSnapshotBeginMessageCodec.EncodedSize ||
            !RemoteChunkSnapshotBeginMessageCodec.TryWriteExact(
                destination[..RemoteChunkSnapshotBeginMessageCodec.EncodedSize], in message))
            return false;
        written = RemoteChunkSnapshotBeginMessageCodec.EncodedSize;
        return true;
    }

    public static bool TryReadChunkSnapshotBegin(
        ReadOnlySpan<byte> source,
        out RemoteChunkSnapshotBeginMessage message)
    {
        if (!RemoteChunkSnapshotBeginMessageCodec.TryReadExact(source, out message) ||
            !ValidSnapshotBegin(message))
        {
            message = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteChunkSnapshotSegment(
        Span<byte> destination,
        in RemoteChunkSnapshotSegmentHeader header,
        ReadOnlySpan<byte> payload,
        out int written)
    {
        written = 0;
        if (!ValidSnapshotSegment(header) || payload.Length != header.PayloadBytes ||
            payload.Overlaps(destination) ||
            destination.Length < GetChunkSnapshotSegmentEncodedBytes(payload.Length))
            return false;
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteChunkSnapshotSegmentHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBytes(payload))
            throw new InvalidOperationException("A preflighted chunk-snapshot segment did not fit.");
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadChunkSnapshotSegment(
        ReadOnlySpan<byte> source,
        out RemoteChunkSnapshotSegmentHeader header,
        Span<byte> payload,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        if (!RemoteChunkSnapshotSegmentHeaderCodec.TryRead(ref reader, out var staged) ||
            !ValidSnapshotSegment(staged) || reader.Remaining != staged.PayloadBytes ||
            staged.PayloadBytes > payload.Length || source.Overlaps(payload))
            return false;
        if (!reader.TryReadBytes(staged.PayloadBytes, out ReadOnlySpan<byte> bytes) ||
            !reader.IsComplete)
            return false;
        bytes.CopyTo(payload);
        header = staged;
        count = staged.PayloadBytes;
        return true;
    }

    public static bool TryWriteChunkSnapshotCommit(
        Span<byte> destination,
        in RemoteChunkSnapshotCommitMessage message,
        out int written)
    {
        written = 0;
        if (!ValidSnapshotCommit(message) ||
            destination.Length < RemoteChunkSnapshotCommitMessageCodec.EncodedSize ||
            !RemoteChunkSnapshotCommitMessageCodec.TryWriteExact(
                destination[..RemoteChunkSnapshotCommitMessageCodec.EncodedSize], in message))
            return false;
        written = RemoteChunkSnapshotCommitMessageCodec.EncodedSize;
        return true;
    }

    public static bool TryReadChunkSnapshotCommit(
        ReadOnlySpan<byte> source,
        out RemoteChunkSnapshotCommitMessage message)
    {
        if (!RemoteChunkSnapshotCommitMessageCodec.TryReadExact(source, out message) ||
            !ValidSnapshotCommit(message))
        {
            message = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteChunkSnapshotCancel(
        Span<byte> destination,
        in RemoteChunkSnapshotCancelMessage message,
        out int written)
    {
        written = 0;
        if (message.ApplicationSessionEpoch == 0 || message.PublicationEpoch == 0 ||
            message.DeliveryId == 0 ||
            destination.Length < RemoteChunkSnapshotCancelMessageCodec.EncodedSize ||
            !RemoteChunkSnapshotCancelMessageCodec.TryWriteExact(
                destination[..RemoteChunkSnapshotCancelMessageCodec.EncodedSize], in message))
            return false;
        written = RemoteChunkSnapshotCancelMessageCodec.EncodedSize;
        return true;
    }

    public static bool TryReadChunkSnapshotCancel(
        ReadOnlySpan<byte> source,
        out RemoteChunkSnapshotCancelMessage message)
    {
        if (!RemoteChunkSnapshotCancelMessageCodec.TryReadExact(source, out message) ||
            message.ApplicationSessionEpoch == 0 || message.PublicationEpoch == 0 ||
            message.DeliveryId == 0)
        {
            message = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteChunkSnapshot(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        ulong spatialSpaceId,
        int chunkX,
        int chunkZ,
        ulong revision,
        ulong historyFloor,
        ReadOnlySpan<RemoteChunkPublicRecord> records,
        out int written)
    {
        written = 0;
        if (!ValidLineage(applicationSessionEpoch, publicationEpoch, deliveryId, spatialSpaceId) ||
            revision == 0 || historyFloor == 0 || historyFloor > revision ||
            records.Length > MaximumChunkPublicationRecords ||
            !ValidateSnapshotRecords(records, revision))
            return false;
        int required = GetChunkSnapshotEncodedBytes(records.Length);
        if (destination.Length < required || MemoryMarshal.AsBytes(records).Overlaps(destination))
            return false;
        var header = new RemoteChunkSnapshotHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = spatialSpaceId,
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            SchemaFingerprint = ChunkPublicationSchemaFingerprint,
            Revision = revision,
            HistoryFloor = historyFloor,
            ContentHash = ComputeSnapshotHash(
                publicationEpoch, spatialSpaceId, chunkX, chunkZ, revision, historyFloor, records),
            RecordCount = checked((ushort)records.Length),
        };
        var writer = new NetworkPacketWriter(destination[..required]);
        if (!RemoteChunkSnapshotHeaderCodec.TryWrite(ref writer, in header))
            throw new InvalidOperationException("A preflighted chunk-snapshot header did not fit.");
        for (int index = 0; index < records.Length; index++)
            WriteSnapshotRecord(ref writer, records[index]);
        if (writer.WrittenCount != required)
            throw new InvalidOperationException("Chunk-snapshot size accounting diverged.");
        written = required;
        return true;
    }

    public static bool TryReadChunkSnapshot(
        ReadOnlySpan<byte> source,
        out RemoteChunkSnapshotHeader header,
        Span<RemoteChunkPublicRecord> records,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        if (!RemoteChunkSnapshotHeaderCodec.TryRead(ref reader, out RemoteChunkSnapshotHeader staged) ||
            !ValidLineage(staged.ApplicationSessionEpoch, staged.PublicationEpoch,
                staged.DeliveryId, staged.SpatialSpaceId) ||
            staged.SchemaFingerprint != ChunkPublicationSchemaFingerprint ||
            staged.Revision == 0 || staged.HistoryFloor == 0 || staged.HistoryFloor > staged.Revision ||
            staged.RecordCount > MaximumChunkPublicationRecords || staged.RecordCount > records.Length ||
            reader.Remaining != staged.RecordCount * RemoteChunkPublicRecord.EncodedSize)
            return false;

        NetworkPacketReader validation = reader;
        ulong hash = BeginSnapshotHash(staged.PublicationEpoch, staged.SpatialSpaceId,
            staged.ChunkX, staged.ChunkZ, staged.Revision, staged.HistoryFloor);
        uint priorIndex = 0;
        for (int index = 0; index < staged.RecordCount; index++)
        {
            if (!TryReadSnapshotRecord(ref validation, out RemoteChunkPublicRecord record) ||
                !ValidSnapshotRecord(record, staged.Revision) ||
                index != 0 && record.EntityIndex <= priorIndex)
                return false;
            priorIndex = record.EntityIndex;
            MixSnapshotRecord(ref hash, record);
        }
        if (!validation.IsComplete || hash != staged.ContentHash) return false;

        for (int index = 0; index < staged.RecordCount; index++)
        {
            _ = TryReadSnapshotRecord(ref reader, out RemoteChunkPublicRecord record);
            records[index] = record;
        }
        header = staged;
        count = staged.RecordCount;
        return true;
    }

    public static bool TryWriteChunkDelta(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        ulong spatialSpaceId,
        int chunkX,
        int chunkZ,
        ulong baseRevision,
        ulong currentRevision,
        ulong historyFloor,
        ReadOnlySpan<RemoteChunkDeltaRecord> records,
        out int written)
    {
        written = 0;
        if (!ValidLineage(applicationSessionEpoch, publicationEpoch, deliveryId, spatialSpaceId) ||
            baseRevision == 0 || currentRevision <= baseRevision || historyFloor == 0 ||
            historyFloor > baseRevision || records.IsEmpty ||
            records.Length > MaximumChunkPublicationRecords ||
            !ValidateDeltaRecords(records, baseRevision, currentRevision))
            return false;
        int required = GetChunkDeltaEncodedBytes(records.Length);
        if (destination.Length < required || MemoryMarshal.AsBytes(records).Overlaps(destination))
            return false;
        var header = new RemoteChunkDeltaHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = spatialSpaceId,
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            SchemaFingerprint = ChunkPublicationSchemaFingerprint,
            BaseRevision = baseRevision,
            CurrentRevision = currentRevision,
            HistoryFloor = historyFloor,
            ContentHash = ComputeDeltaHash(publicationEpoch, spatialSpaceId, chunkX, chunkZ,
                baseRevision, currentRevision, historyFloor, records),
            RecordCount = checked((ushort)records.Length),
        };
        var writer = new NetworkPacketWriter(destination[..required]);
        if (!RemoteChunkDeltaHeaderCodec.TryWrite(ref writer, in header))
            throw new InvalidOperationException("A preflighted chunk-delta header did not fit.");
        for (int index = 0; index < records.Length; index++) WriteDeltaRecord(ref writer, records[index]);
        if (writer.WrittenCount != required)
            throw new InvalidOperationException("Chunk-delta size accounting diverged.");
        written = required;
        return true;
    }

    public static bool TryReadChunkDelta(
        ReadOnlySpan<byte> source,
        out RemoteChunkDeltaHeader header,
        Span<RemoteChunkDeltaRecord> records,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        if (!RemoteChunkDeltaHeaderCodec.TryRead(ref reader, out RemoteChunkDeltaHeader staged) ||
            !ValidLineage(staged.ApplicationSessionEpoch, staged.PublicationEpoch,
                staged.DeliveryId, staged.SpatialSpaceId) ||
            staged.SchemaFingerprint != ChunkPublicationSchemaFingerprint ||
            staged.BaseRevision == 0 || staged.CurrentRevision <= staged.BaseRevision ||
            staged.HistoryFloor == 0 || staged.HistoryFloor > staged.BaseRevision ||
            staged.RecordCount == 0 || staged.RecordCount > MaximumChunkPublicationRecords ||
            staged.RecordCount > records.Length ||
            reader.Remaining != staged.RecordCount * RemoteChunkDeltaRecord.EncodedSize)
            return false;

        NetworkPacketReader validation = reader;
        ulong hash = BeginDeltaHash(staged.PublicationEpoch, staged.SpatialSpaceId,
            staged.ChunkX, staged.ChunkZ, staged.BaseRevision, staged.CurrentRevision,
            staged.HistoryFloor);
        uint priorIndex = 0;
        for (int index = 0; index < staged.RecordCount; index++)
        {
            if (!TryReadDeltaRecord(ref validation, out RemoteChunkDeltaRecord record) ||
                !ValidDeltaRecord(record, staged.BaseRevision, staged.CurrentRevision) ||
                index != 0 && record.EntityIndex <= priorIndex)
                return false;
            priorIndex = record.EntityIndex;
            MixDeltaRecord(ref hash, record);
        }
        if (!validation.IsComplete || hash != staged.ContentHash) return false;

        for (int index = 0; index < staged.RecordCount; index++)
        {
            _ = TryReadDeltaRecord(ref reader, out RemoteChunkDeltaRecord record);
            records[index] = record;
        }
        header = staged;
        count = staged.RecordCount;
        return true;
    }

    public static bool TryWriteChunkAbsence(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ulong publicationEpoch,
        ulong deliveryId,
        ulong spatialSpaceId,
        int chunkX,
        int chunkZ,
        out int written)
    {
        written = 0;
        if (!ValidLineage(applicationSessionEpoch, publicationEpoch, deliveryId, spatialSpaceId) ||
            destination.Length < RemoteChunkAbsenceMessageCodec.EncodedSize)
            return false;
        var message = new RemoteChunkAbsenceMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            PublicationEpoch = publicationEpoch,
            DeliveryId = deliveryId,
            SpatialSpaceId = spatialSpaceId,
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            SchemaFingerprint = ChunkPublicationSchemaFingerprint,
        };
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteChunkAbsenceMessageCodec.TryWrite(ref writer, in message)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadChunkAbsence(
        ReadOnlySpan<byte> source,
        out RemoteChunkAbsenceMessage message)
    {
        if (!RemoteChunkAbsenceMessageCodec.TryReadExact(source, out message) ||
            !ValidLineage(message.ApplicationSessionEpoch, message.PublicationEpoch,
                message.DeliveryId, message.SpatialSpaceId) ||
            message.SchemaFingerprint != ChunkPublicationSchemaFingerprint)
        {
            message = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteChunkApplied(
        Span<byte> destination,
        in RemoteChunkAppliedMessage message,
        out int written)
    {
        written = 0;
        if (!ValidLineage(message.ApplicationSessionEpoch, message.PublicationEpoch,
                message.DeliveryId, message.SpatialSpaceId) ||
            message.Kind is not (RemoteChunkPublicationKind.Snapshot or
                RemoteChunkPublicationKind.Delta or RemoteChunkPublicationKind.Absence) ||
            message.Status is not (RemoteChunkPublicationApplyStatus.Applied or
                RemoteChunkPublicationApplyStatus.Rejected or
                RemoteChunkPublicationApplyStatus.CapacityExceeded or
                RemoteChunkPublicationApplyStatus.Contradictory) ||
            (message.Kind == RemoteChunkPublicationKind.Absence ? message.Revision != 0 : message.Revision == 0) ||
            destination.Length < RemoteChunkAppliedMessageCodec.EncodedSize)
            return false;
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteChunkAppliedMessageCodec.TryWrite(ref writer, in message)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadChunkApplied(
        ReadOnlySpan<byte> source,
        out RemoteChunkAppliedMessage message)
    {
        if (!RemoteChunkAppliedMessageCodec.TryReadExact(source, out message) ||
            !ValidLineage(message.ApplicationSessionEpoch, message.PublicationEpoch,
                message.DeliveryId, message.SpatialSpaceId) ||
            message.Kind is not (RemoteChunkPublicationKind.Snapshot or
                RemoteChunkPublicationKind.Delta or RemoteChunkPublicationKind.Absence) ||
            message.Status is not (RemoteChunkPublicationApplyStatus.Applied or
                RemoteChunkPublicationApplyStatus.Rejected or
                RemoteChunkPublicationApplyStatus.CapacityExceeded or
                RemoteChunkPublicationApplyStatus.Contradictory) ||
            (message.Kind == RemoteChunkPublicationKind.Absence ? message.Revision != 0 : message.Revision == 0))
        {
            message = default;
            return false;
        }
        return true;
    }

    private static bool ValidLineage(ulong application, ulong publication, ulong delivery, ulong space) =>
        application != 0 && publication != 0 && delivery != 0 && space != 0;

    private static bool ValidSnapshotBegin(in RemoteChunkSnapshotBeginMessage message)
    {
        if (!ValidLineage(message.ApplicationSessionEpoch, message.PublicationEpoch,
                message.DeliveryId, message.SpatialSpaceId) ||
            message.SchemaFingerprint != ChunkPublicationSchemaFingerprint ||
            message.Revision == 0 || message.HistoryFloor == 0 ||
            message.HistoryFloor > message.Revision ||
            message.RecordCount > MaximumChunkPublicationRecords ||
            message.EncodedBytes != GetChunkSnapshotEncodedBytes(message.RecordCount) ||
            message.SegmentCount is 0 or > MaximumChunkSnapshotSegments ||
            message.SegmentPayloadBytes == 0)
            return false;
        return message.SegmentCount ==
            (message.EncodedBytes + message.SegmentPayloadBytes - 1) /
            message.SegmentPayloadBytes;
    }

    private static bool ValidSnapshotSegment(in RemoteChunkSnapshotSegmentHeader header) =>
        header.ApplicationSessionEpoch != 0 && header.PublicationEpoch != 0 &&
        header.DeliveryId != 0 &&
        header.SegmentCount is > 0 and <= MaximumChunkSnapshotSegments &&
        header.SegmentIndex < header.SegmentCount && header.PayloadBytes != 0;

    private static bool ValidSnapshotCommit(in RemoteChunkSnapshotCommitMessage message) =>
        ValidLineage(message.ApplicationSessionEpoch, message.PublicationEpoch,
            message.DeliveryId, message.SpatialSpaceId) && message.Revision != 0;

    private static bool ValidateSnapshotRecords(ReadOnlySpan<RemoteChunkPublicRecord> records, ulong revision)
    {
        for (int index = 0; index < records.Length; index++)
            if (!ValidSnapshotRecord(records[index], revision) ||
                index != 0 && records[index].EntityIndex <= records[index - 1].EntityIndex)
                return false;
        return true;
    }

    private static bool ValidSnapshotRecord(in RemoteChunkPublicRecord record, ulong revision) =>
        record.EntityGeneration != 0 && record.EntityPublicVersion != 0 && record.PrefabId != 0 &&
        (record.StateFlags & ~1) == 0 && record.LastChangedRevision != 0 &&
        record.LastChangedRevision <= revision;

    private static bool ValidateDeltaRecords(
        ReadOnlySpan<RemoteChunkDeltaRecord> records,
        ulong baseRevision,
        ulong currentRevision)
    {
        for (int index = 0; index < records.Length; index++)
            if (!ValidDeltaRecord(records[index], baseRevision, currentRevision) ||
                index != 0 && records[index].EntityIndex <= records[index - 1].EntityIndex)
                return false;
        return true;
    }

    private static bool ValidDeltaRecord(
        in RemoteChunkDeltaRecord record,
        ulong baseRevision,
        ulong currentRevision)
    {
        if (record.EntityGeneration == 0 || record.EntityPublicVersion == 0 ||
            (record.Fields & ~RemoteChunkPublicFieldMask.Complete) != 0)
            return false;
        if (record.Operation == RemoteChunkPublicOperation.Remove)
            return record.Fields == RemoteChunkPublicFieldMask.None && record.PrefabId == 0 &&
                record.LocalXMillimeters == 0 && record.LocalZMillimeters == 0 &&
                record.YMillimeters == 0 && record.OrientationUnits == 0 &&
                record.StateFlags == 0 && record.LastChangedRevision == 0;
        return record.Operation == RemoteChunkPublicOperation.Present &&
            record.Fields != RemoteChunkPublicFieldMask.None && record.PrefabId != 0 &&
            (record.StateFlags & ~1) == 0 && record.LastChangedRevision > baseRevision &&
            record.LastChangedRevision <= currentRevision;
    }

    private static void WriteSnapshotRecord(ref NetworkPacketWriter writer, in RemoteChunkPublicRecord record)
    {
        if (!writer.TryWriteUInt32(record.EntityIndex) ||
            !writer.TryWriteUInt32(record.EntityGeneration) ||
            !writer.TryWriteUInt64(record.EntityPublicVersion) ||
            !writer.TryWriteUInt32(record.PrefabId) ||
            !writer.TryWriteUInt16(record.LocalXMillimeters) ||
            !writer.TryWriteUInt16(record.LocalZMillimeters) ||
            !writer.TryWriteInt32(record.YMillimeters) ||
            !writer.TryWriteUInt16(record.OrientationUnits) ||
            !writer.TryWriteUInt16(record.StateFlags) ||
            !writer.TryWriteUInt64(record.LastChangedRevision))
            throw new InvalidOperationException("A preflighted chunk-snapshot record did not fit.");
    }

    private static bool TryReadSnapshotRecord(
        ref NetworkPacketReader reader,
        out RemoteChunkPublicRecord record)
    {
        if (!reader.TryReadUInt32(out uint index) || !reader.TryReadUInt32(out uint generation) ||
            !reader.TryReadUInt64(out ulong version) || !reader.TryReadUInt32(out uint prefab) ||
            !reader.TryReadUInt16(out ushort localX) || !reader.TryReadUInt16(out ushort localZ) ||
            !reader.TryReadInt32(out int y) || !reader.TryReadUInt16(out ushort orientation) ||
            !reader.TryReadUInt16(out ushort flags) || !reader.TryReadUInt64(out ulong changed))
        {
            record = default;
            return false;
        }
        record = new(index, generation, version, prefab, localX, localZ, y, orientation, flags, changed);
        return true;
    }

    private static void WriteDeltaRecord(ref NetworkPacketWriter writer, in RemoteChunkDeltaRecord record)
    {
        if (!writer.TryWriteByte((byte)record.Operation) || !writer.TryWriteUInt16((ushort)record.Fields) ||
            !writer.TryWriteUInt32(record.EntityIndex) || !writer.TryWriteUInt32(record.EntityGeneration) ||
            !writer.TryWriteUInt64(record.EntityPublicVersion) || !writer.TryWriteUInt32(record.PrefabId) ||
            !writer.TryWriteUInt16(record.LocalXMillimeters) ||
            !writer.TryWriteUInt16(record.LocalZMillimeters) || !writer.TryWriteInt32(record.YMillimeters) ||
            !writer.TryWriteUInt16(record.OrientationUnits) || !writer.TryWriteUInt16(record.StateFlags) ||
            !writer.TryWriteUInt64(record.LastChangedRevision))
            throw new InvalidOperationException("A preflighted chunk-delta record did not fit.");
    }

    private static bool TryReadDeltaRecord(ref NetworkPacketReader reader, out RemoteChunkDeltaRecord record)
    {
        if (!reader.TryReadByte(out byte operation) || !reader.TryReadUInt16(out ushort fields) ||
            !reader.TryReadUInt32(out uint index) || !reader.TryReadUInt32(out uint generation) ||
            !reader.TryReadUInt64(out ulong version) || !reader.TryReadUInt32(out uint prefab) ||
            !reader.TryReadUInt16(out ushort localX) || !reader.TryReadUInt16(out ushort localZ) ||
            !reader.TryReadInt32(out int y) || !reader.TryReadUInt16(out ushort orientation) ||
            !reader.TryReadUInt16(out ushort flags) || !reader.TryReadUInt64(out ulong changed))
        {
            record = default;
            return false;
        }
        record = new((RemoteChunkPublicOperation)operation, (RemoteChunkPublicFieldMask)fields,
            index, generation, version, prefab, localX, localZ, y, orientation, flags, changed);
        return true;
    }

    private static ulong ComputeSnapshotHash(
        ulong publication,
        ulong space,
        int chunkX,
        int chunkZ,
        ulong revision,
        ulong floor,
        ReadOnlySpan<RemoteChunkPublicRecord> records)
    {
        ulong hash = BeginSnapshotHash(publication, space, chunkX, chunkZ, revision, floor);
        for (int index = 0; index < records.Length; index++) MixSnapshotRecord(ref hash, records[index]);
        return hash;
    }

    private static ulong BeginSnapshotHash(
        ulong publication, ulong space, int chunkX, int chunkZ, ulong revision, ulong floor)
    {
        ulong hash = 14695981039346656037UL;
        Mix(ref hash, (byte)RemoteChunkPublicationKind.Snapshot);
        Mix(ref hash, publication);
        Mix(ref hash, space);
        Mix(ref hash, unchecked((uint)chunkX));
        Mix(ref hash, unchecked((uint)chunkZ));
        Mix(ref hash, revision);
        Mix(ref hash, floor);
        return hash;
    }

    private static void MixSnapshotRecord(ref ulong hash, in RemoteChunkPublicRecord record)
    {
        Mix(ref hash, record.EntityIndex);
        Mix(ref hash, record.EntityGeneration);
        Mix(ref hash, record.EntityPublicVersion);
        Mix(ref hash, record.PrefabId);
        Mix(ref hash, record.LocalXMillimeters);
        Mix(ref hash, record.LocalZMillimeters);
        Mix(ref hash, unchecked((uint)record.YMillimeters));
        Mix(ref hash, record.OrientationUnits);
        Mix(ref hash, record.StateFlags);
        Mix(ref hash, record.LastChangedRevision);
    }

    private static ulong ComputeDeltaHash(
        ulong publication,
        ulong space,
        int chunkX,
        int chunkZ,
        ulong baseRevision,
        ulong currentRevision,
        ulong floor,
        ReadOnlySpan<RemoteChunkDeltaRecord> records)
    {
        ulong hash = BeginDeltaHash(publication, space, chunkX, chunkZ,
            baseRevision, currentRevision, floor);
        for (int index = 0; index < records.Length; index++) MixDeltaRecord(ref hash, records[index]);
        return hash;
    }

    private static ulong BeginDeltaHash(
        ulong publication,
        ulong space,
        int chunkX,
        int chunkZ,
        ulong baseRevision,
        ulong currentRevision,
        ulong floor)
    {
        ulong hash = 14695981039346656037UL;
        Mix(ref hash, (byte)RemoteChunkPublicationKind.Delta);
        Mix(ref hash, publication);
        Mix(ref hash, space);
        Mix(ref hash, unchecked((uint)chunkX));
        Mix(ref hash, unchecked((uint)chunkZ));
        Mix(ref hash, baseRevision);
        Mix(ref hash, currentRevision);
        Mix(ref hash, floor);
        return hash;
    }

    private static void MixDeltaRecord(ref ulong hash, in RemoteChunkDeltaRecord record)
    {
        Mix(ref hash, (byte)record.Operation);
        Mix(ref hash, (ushort)record.Fields);
        Mix(ref hash, record.EntityIndex);
        Mix(ref hash, record.EntityGeneration);
        Mix(ref hash, record.EntityPublicVersion);
        Mix(ref hash, record.PrefabId);
        Mix(ref hash, record.LocalXMillimeters);
        Mix(ref hash, record.LocalZMillimeters);
        Mix(ref hash, unchecked((uint)record.YMillimeters));
        Mix(ref hash, record.OrientationUnits);
        Mix(ref hash, record.StateFlags);
        Mix(ref hash, record.LastChangedRevision);
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
            hash = unchecked((hash ^ (byte)(value >> shift)) * 1099511628211UL);
    }
}
