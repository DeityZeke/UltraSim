using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>Registers the handwritten generated 1-read/1-write chunk system.</summary>
    /// <remarks>
    /// Use for topology-selected contiguous ECS work. The writable span is a
    /// dense-write contract: exposing the chunk conservatively publishes its
    /// writable range as changed. Use the tracked registration when only a subset
    /// of visited rows is actually mutated.
    /// </remarks>
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

    /// <summary>Registers the handwritten generated 1-read/1-write tracked chunk system.</summary>
    /// <remarks>
    /// Use when the candidate rows are topology-selected but only some visited
    /// rows change. Mutation must pass through the tracked writable wrapper so
    /// exact publication matches the rows actually touched.
    /// </remarks>
    public SystemId RegisterTrackedReadWriteSystem<TRead, TWrite, TSystem, TJob>(
        SystemDefinition definition,
        in ReadWriteJobPlan<TRead, TWrite> plan,
        TSystem runtime)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TSystem : class, ISystemRuntime<TJob>
        where TJob : struct, IScheduledTrackedReadWriteChunkJob<TRead, TWrite>
    {
        EnsureOwnerThread();
        SystemScheduler scheduler = GetSchedulerForRegistration();
        ArchetypeReadWriteQueryPlan<NativeChunkArena, TRead, TWrite> prepared = plan.GetPlan();
        if (prepared.WorldId != Id)
            throw new ArgumentException("The job plan belongs to a different world.", nameof(plan));
        SystemId id = scheduler.GetNextRegistrationId();
        var node = new TrackedReadWriteSystemNode<TSystem, TRead, TWrite, TJob>(
            id, definition, runtime, scheduler.ExecutionConcurrency, _core, prepared);
        Span<ComponentId> reads = stackalloc ComponentId[1] { prepared.ReadComponentId };
        Span<ComponentId> writes = stackalloc ComponentId[1] { prepared.WriteComponentId };
        scheduler.RegisterNode(node, reads, writes);
        return id;
    }

    /// <summary>Prepares a cold world-owned 1-read/1-write generated chunk route.</summary>
    /// <remarks>
    /// Prepare before sealing. Runtime visits only occupied chunks in matching
    /// archetypes; it does not perform per-row entity or component lookup.
    /// </remarks>
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

    /// <summary>Synchronously executes a dense generated 1-read/1-write chunk job.</summary>
    /// <remarks>The writable range is conservatively published as changed.</remarks>
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

    /// <summary>Synchronously executes a conditional-write generated chunk job.</summary>
    /// <remarks>Only mutation through the tracked wrapper publishes exact touched rows.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ExecuteTrackedJob<TRead, TWrite, TJob>(
        in ReadWriteJobPlan<TRead, TWrite> plan,
        ref TJob job)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TJob : struct, ITrackedReadWriteChunkJob<TRead, TWrite>
    {
        EnsureRuntimeAccess();
        return _core.VisitTrackedReadWriteChunks(plan.GetPlan(), ref job);
    }
}
