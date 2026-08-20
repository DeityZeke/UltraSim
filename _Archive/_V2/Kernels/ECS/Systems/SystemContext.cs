namespace UltraSim.ECS;

public readonly ref struct SystemContext
{
    private readonly Action<int, Action<int>>? _parallelFor;

    public readonly World World;
    public readonly uint Tick;
    public readonly float DeltaSeconds;
    public readonly IStructuralCommands Commands;

    internal SystemContext(World world, uint tick, float deltaSeconds, IStructuralCommands commands, Action<int, Action<int>>? parallelFor = null)
    {
        _parallelFor = parallelFor;
        World = world;
        Tick = tick;
        DeltaSeconds = deltaSeconds;
        Commands = commands;
    }

    public void ParallelFor(int count, Action<int> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (count <= 0)
            return;

        if (_parallelFor is null)
        {
            for (int i = 0; i < count; i++)
                action(i);

            return;
        }

        _parallelFor(count, action);
    }

    public int ReadChunks<T>(ReadOnlyChunkAction<T> action)
        where T : unmanaged, IComponent<T>
    {
        return World.ReadChunks(action);
    }

    public int ReadChunks<T1, T2>(ReadOnlyChunkAction<T1, T2> action)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
    {
        return World.ReadChunks(action);
    }

    public int ReadChunks<T1, T2, T3>(ReadOnlyChunkAction<T1, T2, T3> action)
        where T1 : unmanaged, IComponent<T1>
        where T2 : unmanaged, IComponent<T2>
        where T3 : unmanaged, IComponent<T3>
    {
        return World.ReadChunks(action);
    }

    public int PatchChunks<TWrite>(PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunks(action);
    }

    public int PatchChunks<TWrite>(World.PatchChunkPlanCache<TWrite> cache, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunks(cache, action);
    }

    public int PatchChunks<TRead, TWrite>(PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunks(action);
    }

    public int PatchChunks<TRead, TWrite>(World.PatchChunkPlanCache<TRead, TWrite> cache, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunks(cache, action);
    }

    public int PatchChunks<TRead1, TRead2, TWrite>(PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunks(action);
    }

    public int PatchChunks<TRead1, TRead2, TWrite>(World.PatchChunkPlanCache<TRead1, TRead2, TWrite> cache, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunks(cache, action);
    }

    public int PatchChunksParallel<TWrite>(int blockSize, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TWrite>(World.PatchChunkPlanCache<TWrite> cache, int blockSize, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(cache, _parallelFor, blockSize, action);
    }

    public int PatchChunksParallelSparse<TWrite>(int blockSize, SparsePatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallelSparse(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TRead, TWrite>(int blockSize, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TRead, TWrite>(World.PatchChunkPlanCache<TRead, TWrite> cache, int blockSize, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(cache, _parallelFor, blockSize, action);
    }

    public int PatchChunksParallelSparse<TRead, TWrite>(int blockSize, SparsePatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallelSparse(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TRead1, TRead2, TWrite>(int blockSize, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TRead1, TRead2, TWrite>(World.PatchChunkPlanCache<TRead1, TRead2, TWrite> cache, int blockSize, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(cache, _parallelFor, blockSize, action);
    }

    public int PatchChunksParallelSparse<TRead1, TRead2, TWrite>(int blockSize, SparsePatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallelSparse(_parallelFor, blockSize, action);
    }
}
