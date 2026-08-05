namespace UltraSim.ECS;

/// <summary>Trusted paired storage view produced from a seal-compiled query route.</summary>
internal readonly ref struct BoundReadWriteComponentChunk<TRead, TWrite>
    where TRead : unmanaged
    where TWrite : unmanaged
{
    internal BoundReadWriteComponentChunk(
        uint chunkIndex,
        ReadOnlySpan<uint> entityIndices,
        ReadOnlySpan<TRead> readComponents,
        Span<TWrite> writeComponents,
        ReadOnlySpan<ulong> readEnableWords,
        Span<ulong> writeEnableWords)
    {
        ChunkIndex = chunkIndex;
        EntityIndices = entityIndices;
        ReadComponents = readComponents;
        WriteComponents = writeComponents;
        ReadEnableWords = readEnableWords;
        WriteEnableWords = writeEnableWords;
    }

    internal uint ChunkIndex { get; }
    internal ReadOnlySpan<uint> EntityIndices { get; }
    internal ReadOnlySpan<TRead> ReadComponents { get; }
    internal Span<TWrite> WriteComponents { get; }
    internal ReadOnlySpan<ulong> ReadEnableWords { get; }
    internal Span<ulong> WriteEnableWords { get; }
}
