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
        ReadOnlySpan<ArchetypeReadWriteQueryRoute<TArena>> routes = plan.Routes;
        for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
        {
            ArchetypeChunkStore<TArena> store = routes[routeIndex].Storage.Store;
            int cursor = store.GetFirstOccupiedChunkIndex();
            while (cursor >= 0)
            {
                if (written == destination.Length)
                    throw new InvalidOperationException("Scheduler work-item capacity is exhausted.");
                int first = cursor;
                int chunkCount = 0;
                int rowCount = 0;
                while (cursor >= 0 && chunkCount < targetChunks)
                {
                    rowCount = checked(rowCount + store.GetRowCount(unchecked((uint)cursor)));
                    chunkCount++;
                    cursor = store.GetNextOccupiedChunkIndex(cursor);
                }
                destination[written++] = new SchedulerChunkPartition(routeIndex, first, chunkCount, rowCount);
            }
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
        ref readonly ArchetypeReadWriteQueryRoute<TArena> route = ref routes[partition.RouteIndex];
        if (route.HasRowFilters)
            throw new InvalidOperationException("Scheduled public plans do not expose row-filter authoring.");
        ArchetypeChunkStore<TArena> store = route.Storage.Store;
        int chunkIndex = partition.FirstChunkIndex;
        int visited = 0;
        for (int ordinal = 0; ordinal < partition.ChunkCount; ordinal++)
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
            chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex);
        }
        return visited;
    }
}
