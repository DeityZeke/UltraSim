namespace UltraSim.ECS;

/// <summary>
/// Internal constrained-dispatch seam shared by the executable native and pinned
/// storage candidates. It is not a public polymorphic hot-path API.
/// </summary>
internal interface IChunkArenaBackend : IDisposable
{
    ChunkArenaCapacity Capacity { get; }

    bool IsDisposed { get; }

    ChunkHandle Acquire();

    bool TryAcquire(out ChunkHandle handle);

    bool TryRelease(ChunkHandle handle);

    bool IsAlive(ChunkHandle handle);

    Span<byte> GetChunkBytes(ChunkHandle handle);

    ChunkArenaSnapshot GetSnapshot();
}
