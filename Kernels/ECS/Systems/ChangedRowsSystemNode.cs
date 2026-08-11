namespace UltraSim.ECS;

internal sealed class ChangedRowsSystemNode<TSystem, TRead, TChanged, TJob> :
    ScheduledSystemNode<TSystem, TJob>
    where TSystem : class, IChangedRowsSystemRuntime<TJob>
    where TRead : unmanaged, IComponent<TRead>
    where TChanged : unmanaged, IComponent<TChanged>
    where TJob : struct, IScheduledChangedRowsChunkJob<TRead>
{
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly ArchetypeChangedReadQueryPlan<NativeChunkArena, TRead> _plan;

    internal ChangedRowsSystemNode(
        SystemId id,
        SystemDefinition definition,
        TSystem runtime,
        int participants,
        EcsWorld<NativeChunkArena> world,
        ArchetypeChangedReadQueryPlan<NativeChunkArena, TRead> plan)
        : base(id, definition, runtime, participants)
    {
        _world = world;
        _plan = plan;
    }

    internal override bool SupportsParallel => false;

    internal override SchedulerQueryShape GetShape() =>
        _world.GetChangedRowsSchedulerShape(_plan);

    internal override int BuildPartitions(
        in SchedulerQueryShape shape,
        Span<SchedulerChunkPartition> destination,
        int desiredPartitions)
    {
        if (shape.Chunks == 0)
            return 0;
        destination[0] = new SchedulerChunkPartition(-1, 0, 1, Math.Max(1, shape.Rows));
        return 1;
    }

    internal override int ExecutePartition(
        int participant,
        in SchedulerChunkPartition partition,
        in SystemJobContext context)
    {
        ref TJob job = ref GetParticipantJob(participant);
        var adapter = new Adapter(context, job);
        ChangeCursor cursor = Runtime.Cursor;
        int rows = _world.VisitChangedReadRowsScheduled(_plan, ref cursor, ref adapter);
        Runtime.Cursor = cursor;
        job = adapter.Job;
        return rows;
    }

    private struct Adapter : IReadOnlyChunkJob<TRead>
    {
        internal Adapter(in SystemJobContext context, in TJob job)
        {
            Context = context;
            Job = job;
        }

        internal SystemJobContext Context;
        internal TJob Job;

        public void Execute(in ReadOnlyComponentChunk<TRead> chunk) =>
            Job.Execute(Context, chunk);
    }
}
