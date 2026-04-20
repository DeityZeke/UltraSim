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

    public int PatchChunksParallel<TWrite>(int blockSize, PatchChunkAction<TWrite> action)
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TRead, TWrite>(int blockSize, PatchChunkAction<TRead, TWrite> action)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(_parallelFor, blockSize, action);
    }

    public int PatchChunksParallel<TRead1, TRead2, TWrite>(int blockSize, PatchChunkAction<TRead1, TRead2, TWrite> action)
        where TRead1 : unmanaged, IComponent<TRead1>
        where TRead2 : unmanaged, IComponent<TRead2>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        return World.PatchChunksParallel(_parallelFor, blockSize, action);
    }
}
