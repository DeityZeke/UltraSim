namespace UltraSim.ECS;

/// <summary>Trusted stack-only storage view produced from a cold-validated binding.</summary>
internal readonly ref struct BoundComponentChunk<T>
    where T : unmanaged
{
    internal BoundComponentChunk(
        uint chunkIndex,
        ReadOnlySpan<uint> entityIndices,
        Span<T> components,
        Span<ulong> enableWords)
    {
        ChunkIndex = chunkIndex;
        EntityIndices = entityIndices;
        Components = components;
        EnableWords = enableWords;
    }

    internal uint ChunkIndex { get; }

    internal ReadOnlySpan<uint> EntityIndices { get; }

    internal Span<T> Components { get; }

    internal Span<ulong> EnableWords { get; }
}
