namespace UltraSim.ECS;

internal sealed class ReadWriteSystemNode<TSystem, TRead, TWrite, TJob> :
    ScheduledSystemNode<TSystem, TJob>
    where TSystem : class, ISystemRuntime<TJob>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
    where TJob : struct, IScheduledReadWriteChunkJob<TRead, TWrite>
{
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> _plan;

    internal ReadWriteSystemNode(SystemId id, SystemDefinition definition, TSystem runtime,
        int participants, EcsWorld<NativeChunkArena> world,
        ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> plan)
        : base(id, definition, runtime, participants)
    {
        _world = world;
        _plan = plan;
    }

    internal override SchedulerQueryShape GetShape() => _world.GetReadWriteSchedulerShape(_plan);

    internal override int BuildPartitions(in SchedulerQueryShape shape,
        Span<SchedulerChunkPartition> destination, int desiredPartitions) =>
        _world.BuildReadWriteSchedulerPartitions(_plan, shape, desiredPartitions, destination);

    internal override int ExecutePartition(int participant, in SchedulerChunkPartition partition,
        in SystemJobContext context)
    {
        ref TJob participantJob = ref GetParticipantJob(participant);
        return _world.ExecuteReadWriteSchedulerPartition(
            _plan,
            partition,
            Publication.Acquire(),
            context,
            ref participantJob);
    }
}

internal sealed class TrackedReadWriteSystemNode<TSystem, TRead, TWrite, TJob> :
    ScheduledSystemNode<TSystem, TJob>
    where TSystem : class, ISystemRuntime<TJob>
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
    where TJob : struct, IScheduledTrackedReadWriteChunkJob<TRead, TWrite>
{
    private readonly EcsWorld<NativeChunkArena> _world;
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> _plan;

    internal TrackedReadWriteSystemNode(SystemId id, SystemDefinition definition, TSystem runtime,
        int participants, EcsWorld<NativeChunkArena> world,
        ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> plan)
        : base(id, definition, runtime, participants)
    {
        _world = world;
        _plan = plan;
    }

    internal override SchedulerQueryShape GetShape() => _world.GetReadWriteSchedulerShape(_plan);

    internal override int BuildPartitions(in SchedulerQueryShape shape,
        Span<SchedulerChunkPartition> destination, int desiredPartitions) =>
        _world.BuildReadWriteSchedulerPartitions(_plan, shape, desiredPartitions, destination);

    internal override int ExecutePartition(int participant, in SchedulerChunkPartition partition,
        in SystemJobContext context)
    {
        ref TJob participantJob = ref GetParticipantJob(participant);
        return _world.ExecuteTrackedReadWriteSchedulerPartition(
            _plan, partition, Publication, context, ref participantJob);
    }
}
