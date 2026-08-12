namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>Registers a serial independent-cursor exact changed-row consumer.</summary>
    /// <remarks>
    /// This observes current rows whose watched component value changed after the
    /// runtime cursor. It does not publish entity creation/destruction, component
    /// membership changes, tombstones, or every intermediate value.
    /// Acknowledgement occurs only after successful execution.
    /// </remarks>
    public SystemId RegisterChangedRowsSystem<TRead, TChanged, TSystem, TJob>(
        SystemDefinition definition,
        in ChangedRowsJobPlan<TRead, TChanged> plan,
        TSystem runtime)
        where TRead : unmanaged, IComponent<TRead>
        where TChanged : unmanaged, IComponent<TChanged>
        where TSystem : class, IChangedRowsSystemRuntime<TJob>
        where TJob : struct, IScheduledChangedRowsChunkJob<TRead>
    {
        EnsureOwnerThread();
        SystemScheduler scheduler = GetSchedulerForRegistration();
        ArchetypeChangedReadQueryPlan<NativeChunkArena, TRead> prepared = plan.GetPlan();
        if (prepared.WorldId != Id)
            throw new ArgumentException("The changed-row plan belongs to a different world.", nameof(plan));
        SystemId id = scheduler.GetNextRegistrationId();
        var node = new ChangedRowsSystemNode<TSystem, TRead, TChanged, TJob>(
            id, definition, runtime, scheduler.ExecutionConcurrency, _core, prepared);
        Span<ComponentId> reads = stackalloc ComponentId[2]
        {
            prepared.ReadComponentId,
            prepared.ChangedComponentId,
        };
        scheduler.RegisterNode(node, reads, ReadOnlySpan<ComponentId>.Empty);
        return id;
    }

    /// <summary>
    /// Prepares an exact-row independent-consumer plan during world construction.
    /// Routes and bounded row-history storage are compiled by <see cref="Seal"/>.
    /// </summary>
    /// <remarks>
    /// Preparing exact observation commits bounded row-history storage for the
    /// watched component routes. This is value-change observation, not lifecycle
    /// history.
    /// </remarks>
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
    /// <remarks>
    /// On exception the cursor is unchanged and the consumer may retry. Copying a
    /// cursor creates another independent consumer at the same position.
    /// </remarks>
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
