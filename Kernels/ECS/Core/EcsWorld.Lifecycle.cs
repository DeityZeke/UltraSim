using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

internal sealed partial class EcsWorld<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal LifecycleCursor CaptureLifecycleCursorFromBeginning()
    {
        EnsureSealed();
        return _lifecycleHistory?.CaptureFromBeginning(Id) ?? new LifecycleCursor(Id, 0);
    }

    internal LifecycleCursor CaptureLifecycleCursorFromNow()
    {
        EnsureSealed();
        return _lifecycleHistory?.CaptureFromNow(Id) ?? new LifecycleCursor(Id, 0);
    }

    internal LifecycleReadResult ReadLifecycle(
        ref LifecycleCursor cursor,
        Span<LifecycleRecord> destination)
    {
        EnsureSealed();
        if (!cursor.IsValid)
            throw new ArgumentException("The lifecycle cursor is invalid or default initialized.", nameof(cursor));
        if (cursor.World != Id)
            throw new ArgumentException("The lifecycle cursor belongs to a different world.", nameof(cursor));
        return _lifecycleHistory?.Read(ref cursor, destination) ??
            new LifecycleReadResult(
                LifecycleReadStatus.NoData,
                0,
                0,
                0,
                0);
    }

    internal LifecycleHistorySnapshot GetLifecycleHistorySnapshot()
    {
        EnsureSealed();
        return _lifecycleHistory?.GetSnapshot() ?? default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PublishCreated(Entity entity) =>
        _lifecycleHistory?.Publish(
            LifecycleKind.Created,
            entity,
            ComponentId.Invalid);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PublishDestroyed(Entity entity) =>
        _lifecycleHistory?.Publish(
            LifecycleKind.Destroyed,
            entity,
            ComponentId.Invalid);

    internal void PublishDestroyed(ReadOnlySpan<Entity> entities)
    {
        LifecycleHistory? history = _lifecycleHistory;
        if (history is null)
            return;
        for (int index = 0; index < entities.Length; index++)
        {
            history.Publish(
                LifecycleKind.Destroyed,
                entities[index],
                ComponentId.Invalid);
        }
    }

    internal void PublishTransitionLifecycle(
        Entity entity,
        PreparedArchetypeTransition transition)
    {
        System.Diagnostics.Debug.Assert(transition.WorldId == Id);
        PublishTransitionLifecycle(
            entity,
            transition.RemovedComponents,
            transition.AddedComponents);
    }

    internal void PublishTransitionLifecycle(
        Entity entity,
        ArchetypeTransitionPlan<TArena> transition)
    {
        PublishTransitionLifecycle(
            entity,
            transition.RemovedComponents,
            transition.AddedComponents);
    }

    private void PublishTransitionLifecycle(
        Entity entity,
        ReadOnlySpan<ComponentId> removedComponents,
        ReadOnlySpan<ComponentId> addedComponents)
    {
        LifecycleHistory? history = _lifecycleHistory;
        if (history is null)
            return;

        for (int index = 0; index < removedComponents.Length; index++)
            history.Publish(LifecycleKind.ComponentRemoved, entity, removedComponents[index]);
        for (int index = 0; index < addedComponents.Length; index++)
            history.Publish(LifecycleKind.ComponentAdded, entity, addedComponents[index]);
    }

    internal void PublishTransitionLifecycle(
        ReadOnlySpan<Entity> entities,
        PreparedArchetypeTransition transition)
    {
        LifecycleHistory? history = _lifecycleHistory;
        if (history is null)
            return;
        ReadOnlySpan<ComponentId> removed = transition.RemovedComponents;
        ReadOnlySpan<ComponentId> added = transition.AddedComponents;
        for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
        {
            Entity entity = entities[entityIndex];
            for (int componentIndex = 0; componentIndex < removed.Length; componentIndex++)
            {
                history.Publish(
                    LifecycleKind.ComponentRemoved,
                    entity,
                    removed[componentIndex]);
            }
            for (int componentIndex = 0; componentIndex < added.Length; componentIndex++)
            {
                history.Publish(
                    LifecycleKind.ComponentAdded,
                    entity,
                    added[componentIndex]);
            }
        }
    }

    internal void SetLifecycleNextSequenceForTest(ulong sequence)
    {
        EnsureSealed();
        (_lifecycleHistory ?? throw new InvalidOperationException(
            "Lifecycle history is disabled for this world."))
            .SetNextSequenceForTest(sequence);
    }
}
