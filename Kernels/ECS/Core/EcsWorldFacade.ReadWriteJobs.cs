using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
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
