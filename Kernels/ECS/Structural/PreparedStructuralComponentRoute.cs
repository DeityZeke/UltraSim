namespace UltraSim.ECS;

internal sealed class PreparedStructuralComponentRoute<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal PreparedStructuralComponentRoute(
        uint id,
        ComponentId componentId,
        int payloadSize,
        int payloadAlignment,
        StructuralComponentArchetypeRoute<TArena>[] routes)
    {
        Id = id;
        ComponentId = componentId;
        PayloadSize = payloadSize;
        PayloadAlignment = payloadAlignment;
        Routes = routes;
    }

    internal uint Id { get; }
    internal ComponentId ComponentId { get; }
    internal int PayloadSize { get; }
    internal int PayloadAlignment { get; }
    internal StructuralComponentArchetypeRoute<TArena>[] Routes { get; }
}

internal readonly struct StructuralComponentArchetypeRoute<TArena>
    where TArena : class, IChunkArenaBackend
{
    internal StructuralComponentArchetypeRoute(
        bool sourceHasComponent,
        int sourceColumnIndex,
        int destinationColumnIndex,
        ArchetypeTransitionPlan<TArena>? addTransition,
        ArchetypeTransitionPlan<TArena>? removeTransition)
    {
        SourceHasComponent = sourceHasComponent;
        SourceColumnIndex = sourceColumnIndex;
        DestinationColumnIndex = destinationColumnIndex;
        AddTransition = addTransition;
        RemoveTransition = removeTransition;
    }

    internal bool SourceHasComponent { get; }
    internal int SourceColumnIndex { get; }
    internal int DestinationColumnIndex { get; }
    internal ArchetypeTransitionPlan<TArena>? AddTransition { get; }
    internal ArchetypeTransitionPlan<TArena>? RemoveTransition { get; }
}
