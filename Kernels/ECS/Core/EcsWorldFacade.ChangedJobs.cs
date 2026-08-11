namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>
    /// Prepares an exact-row independent-consumer plan during world construction.
    /// Routes and bounded row-history storage are compiled by <see cref="Seal"/>.
    /// </summary>
    public ChangedRowsJobPlan<TRead, TChanged> PrepareChangedRowsJob<TRead, TChanged>()
        where TRead : unmanaged, IComponent<TRead>
        where TChanged : unmanaged, IComponent<TChanged>
    {
        EnsureOwnerThread();
        if (!Schema.TryGetComponentId<TRead>(out ComponentId readComponentId))
            throw new ArgumentException($"Component type {typeof(TRead)} is not registered in this world schema.");
        if (!Schema.TryGetComponentId<TChanged>(out ComponentId changedComponentId))
            throw new ArgumentException($"Component type {typeof(TChanged)} is not registered in this world schema.");

        return new ChangedRowsJobPlan<TRead, TChanged>(
            _core.PrepareChangedRowsQuery<TRead>(readComponentId, changedComponentId));
    }

    /// <summary>
    /// Executes an exact-row changed job and acknowledges the cursor only after
    /// every chunk callback succeeds.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public int ExecuteChangedRowsJob<TRead, TChanged, TJob>(
        in ChangedRowsJobPlan<TRead, TChanged> plan,
        ref ChangeCursor cursor,
        ref TJob job)
        where TRead : unmanaged, IComponent<TRead>
        where TChanged : unmanaged, IComponent<TChanged>
        where TJob : struct, IReadOnlyChunkJob<TRead>
    {
        EnsureRuntimeAccess();
        return _core.VisitChangedReadRows(plan.GetPlan(), ref cursor, ref job);
    }
}
