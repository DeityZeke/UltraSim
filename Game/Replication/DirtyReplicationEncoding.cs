namespace UltraSim.Game.Replication;

/// <summary>
/// Canonical bounded Game-message shape used when semantic profiles materialize delivery debt.
/// Remote codecs must preserve this shape, but the Game profile does not depend on Network.
/// </summary>
public static class DirtyReplicationEncoding
{
    public const int MaximumRecordsPerChunk = 64;
    public const int MaximumChunksPerBundle = 32;
    public const int DirtyEntityBundleHeaderBytes = sizeof(ulong) * 3 + sizeof(byte);
    public const int DirtyChunkBundleHeaderBytes = sizeof(ulong) * 3 + sizeof(byte);
    public const int ChunkSnapshotRecordBytes = 40;
    public const int ChunkDeltaRecordBytes = 43;
    private const int ChunkCommonEntryHeaderBytes =
        sizeof(byte) + sizeof(ulong) * 2 + sizeof(int) * 2;
    private const int ChunkSnapshotEntryHeaderBytes =
        ChunkCommonEntryHeaderBytes + sizeof(ulong) * 3 + sizeof(ushort);
    private const int ChunkDeltaEntryHeaderBytes =
        ChunkCommonEntryHeaderBytes + sizeof(ulong) * 4 + sizeof(ushort);

    public static int GetChunkEntryEncodedBytes(OfflineDirectChunkReadStatus status, int recordCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        if (recordCount > MaximumRecordsPerChunk)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        return status switch
        {
            OfflineDirectChunkReadStatus.Snapshot =>
                checked(ChunkSnapshotEntryHeaderBytes + recordCount * ChunkSnapshotRecordBytes),
            OfflineDirectChunkReadStatus.Delta when recordCount != 0 =>
                checked(ChunkDeltaEntryHeaderBytes + recordCount * ChunkDeltaRecordBytes),
            OfflineDirectChunkReadStatus.ChunkAbsent when recordCount == 0 => ChunkCommonEntryHeaderBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }
}
