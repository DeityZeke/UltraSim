namespace UltraSim.ECS;

internal sealed partial class EcsWorld<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal SchedulerQueryShape GetReadWriteSchedulerShape<TRead, TWrite>(
        ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> plan)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        int rows = 0;
        int chunks = 0;
        foreach (ref readonly ArchetypeReadWriteQueryRoute<TArena> route in plan.Routes)
        {
            ArchetypeChunkStore<TArena> store = route.Storage.Store;
            for (int chunk = store.GetFirstOccupiedChunkIndex(); chunk >= 0;
                 chunk = store.GetNextOccupiedChunkIndex(chunk))
            {
                chunks++;
                rows = checked(rows + store.GetRowCount(unchecked((uint)chunk)));
            }
        }
        return new SchedulerQueryShape(rows, chunks);
    }

    internal int BuildReadWriteSchedulerPartitions<TRead, TWrite>(
        ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> plan,
        in SchedulerQueryShape shape,
        int desiredPartitions,
        Span<SchedulerChunkPartition> destination)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        if (desiredPartitions <= 0)
            throw new ArgumentOutOfRangeException(nameof(desiredPartitions));
        if (shape.Chunks == 0)
            return 0;
        int targetChunks = Math.Max(1, (shape.Chunks + desiredPartitions - 1) / desiredPartitions);
        int written = 0;
        int firstRoute = -1;
        int firstChunk = -1;
        int partitionChunks = 0;
        int partitionRows = 0;
        ReadOnlySpan<ArchetypeReadWriteQueryRoute<TArena>> routes = plan.Routes;
        for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            ArchetypeChunkStore<TArena> store = routes[routeIndex].Storage.Store;
            for (int cursor = store.GetFirstOccupiedChunkIndex(); cursor >= 0;
                 cursor = store.GetNextOccupiedChunkIndex(cursor))
            {
                if (partitionChunks == 0)
                {
                    firstRoute = routeIndex;
                    firstChunk = cursor;
                }
                partitionRows = checked(partitionRows + store.GetRowCount(unchecked((uint)cursor)));
                partitionChunks++;
                if (partitionChunks != targetChunks)
                    continue;
                if (written == destination.Length)
                    throw new InvalidOperationException("Scheduler work-item capacity is exhausted.");
                destination[written++] = new SchedulerChunkPartition(
                    firstRoute, firstChunk, partitionChunks, partitionRows);
                partitionChunks = 0;
                partitionRows = 0;
            }
        }
        if (partitionChunks != 0)
        {
            if (written == destination.Length)
                throw new InvalidOperationException("Scheduler work-item capacity is exhausted.");
            destination[written++] = new SchedulerChunkPartition(
                firstRoute, firstChunk, partitionChunks, partitionRows);
        }
        return written;
    }

    internal int ExecuteReadWriteSchedulerPartition<TRead, TWrite, TJob>(
        ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> plan,
        in SchedulerChunkPartition partition,
        ChangeVersion writeVersion,
        in SystemJobContext context,
        ref TJob job)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TJob : struct, IScheduledReadWriteChunkJob<TRead, TWrite>
    {
        System.Diagnostics.Debug.Assert(_isExecutingScope && writeVersion.IsValid);
        ReadOnlySpan<ArchetypeReadWriteQueryRoute<TArena>> routes = plan.Routes;
        if (!partition.IsValid || unchecked((uint)partition.RouteIndex) >= unchecked((uint)routes.Length))
            throw new ArgumentException("The scheduler chunk partition is invalid.", nameof(partition));
        int routeIndex = partition.RouteIndex;
        int chunkIndex = partition.FirstChunkIndex;
        int visited = 0;
        int remaining = partition.ChunkCount;
        while (remaining != 0)
        {
            if (unchecked((uint)routeIndex) >= unchecked((uint)routes.Length))
                throw new InvalidOperationException("A scheduler partition exceeded its matched routes.");
            ref readonly ArchetypeReadWriteQueryRoute<TArena> route = ref routes[routeIndex];
            if (route.HasRowFilters)
                throw new InvalidOperationException("Scheduled public plans do not expose row-filter authoring.");
            ArchetypeChunkStore<TArena> store = route.Storage.Store;
            while (chunkIndex >= 0 && remaining != 0)
            {
                BoundReadWriteComponentChunk<TRead, TWrite> bound =
                    store.GetBoundReadWriteComponentChunk<TRead, TWrite>(
                        chunkIndex,
                        route.ReadColumnIndex,
                        route.WriteColumnIndex,
                        plan.ReadIsEnableable,
                        plan.WriteIsEnableable);
                store.StampDenseChangeVersion(bound.ChunkIndex, route.WriteColumnIndex, writeVersion);
                var chunk = new ReadWriteComponentChunk<TRead, TWrite>(
                    Id, route.Storage.Id, bound.ChunkIndex, 0, bound.EntityIndices,
                    bound.ReadComponents, bound.WriteComponents, bound.ReadEnableWords, bound.WriteEnableWords);
                job.Execute(context, chunk);
                visited += chunk.Count;
                remaining--;
                chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex);
            }
            if (remaining != 0)
            {
                routeIndex++;
                chunkIndex = routeIndex < routes.Length
                    ? routes[routeIndex].Storage.Store.GetFirstOccupiedChunkIndex()
                    : -1;
            }
        }
        return visited;
    }
}
