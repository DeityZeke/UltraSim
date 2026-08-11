using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    public SystemId RegisterReadWriteSystem<TRead, TWrite, TSystem, TJob>(
        SystemDefinition definition,
        in ReadWriteJobPlan<TRead, TWrite> plan,
        TSystem runtime)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TSystem : class, ISystemRuntime<TJob>
        where TJob : struct, IScheduledReadWriteChunkJob<TRead, TWrite>
    {
        EnsureOwnerThread();
        SystemScheduler scheduler = GetSchedulerForRegistration();
        ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> prepared = plan.GetPlan();
        if (prepared.WorldId != Id)
            throw new ArgumentException("The job plan belongs to a different world.", nameof(plan));
        SystemId id = scheduler.GetNextRegistrationId();
        var node = new ReadWriteSystemNode<TSystem, TRead, TWrite, TJob>(
            id, definition, runtime, scheduler.ExecutionConcurrency, _core, prepared);
        Span<ComponentId> reads = stackalloc ComponentId[1] { prepared.ReadComponentId };
        Span<ComponentId> writes = stackalloc ComponentId[1] { prepared.WriteComponentId };
        scheduler.RegisterNode(node, reads, writes);
        return id;
    }

    public ReadWriteJobPlan<TRead, TWrite> PrepareReadWriteJob<TRead, TWrite>()
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        EnsureOwnerThread();
        if (!Schema.TryGetComponentId<TRead>(out ComponentId readComponentId))
            throw new ArgumentException($"Component type {typeof(TRead)} is not registered in this world schema.");
        if (!Schema.TryGetComponentId<TWrite>(out ComponentId writeComponentId))
            throw new ArgumentException($"Component type {typeof(TWrite)} is not registered in this world schema.");
        return new ReadWriteJobPlan<TRead, TWrite>(
            _core.PrepareReadWriteQuery<TRead, TWrite>(readComponentId, writeComponentId));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ExecuteJob<TRead, TWrite, TJob>(
        in ReadWriteJobPlan<TRead, TWrite> plan,
        ref TJob job)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TJob : struct, IReadWriteChunkJob<TRead, TWrite>
    {
        EnsureRuntimeAccess();
        return _core.VisitReadWriteChunks(plan.GetPlan(), ref job);
    }
}
