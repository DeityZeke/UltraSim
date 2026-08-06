using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// World-owned random-access route compiled for one component across every
/// registered archetype. Each route consumes two bytes per archetype.
/// </summary>
internal sealed class ComponentLookupPlan<TArena, T> : IArchetypeQueryPlan<TArena>
    where TArena : class, IChunkArenaBackend
    where T : unmanaged, IComponent<T>
{
    private const ushort MissingColumn = ushort.MaxValue;

    private readonly ComponentFlags _flags;
    private ushort[] _columnByArchetype = [];
    private int _compiledArchetypeCount = -1;

    internal ComponentLookupPlan(
        WorldId worldId,
        int planIndex,
        ComponentId componentId,
        ComponentFlags flags)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (planIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(planIndex));
        if (!componentId.IsValid)
            throw new ArgumentException("A valid component ID is required.", nameof(componentId));

        WorldId = worldId;
        PlanIndex = planIndex;
        ComponentId = componentId;
        _flags = flags;
    }

    internal WorldId WorldId { get; }

    internal int PlanIndex { get; }

    internal ComponentId ComponentId { get; }

    internal bool HasData => (_flags & ComponentFlags.Tag) == 0;

    internal bool IsEnableable => (_flags & ComponentFlags.Enableable) != 0;

    internal bool IsCompiled => _compiledArchetypeCount >= 0;

    internal int MatchedArchetypeCount { get; private set; }

    internal long RouteBytes => checked((long)_columnByArchetype.Length * sizeof(ushort));

    public void Compile(ArchetypeRegistry<TArena> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_compiledArchetypeCount == registry.Count)
            return;

        var routes = new ushort[registry.Count];
        routes.AsSpan().Fill(MissingColumn);
        int matchedArchetypes = 0;
        for (int index = 0; index < registry.Count; index++)
        {
            ArchetypeStorage<TArena> storage = registry.GetStorage(new ArchetypeId((uint)index + 1));
            int columnIndex = storage.Store.GetColumnIndex(ComponentId);
            if (columnIndex < 0)
                continue;
            if (unchecked((uint)columnIndex) >= MissingColumn)
                ThrowLookupInvariant("A component column cannot be encoded by the lookup route table.");

            ref readonly ComponentColumnLayout column = ref storage.Columns[columnIndex];
            ArchetypeQueryPlanUtilities.ValidateTrackedColumn(column, ComponentId, _flags);
            if (HasData && column.ElementSizeBytes != Unsafe.SizeOf<T>())
                ThrowLookupInvariant("Generated component metadata and archetype storage disagree.");
            routes[index] = (ushort)columnIndex;
            matchedArchetypes++;
        }

        _columnByArchetype = routes;
        _compiledArchetypeCount = registry.Count;
        MatchedArchetypeCount = matchedArchetypes;
    }

    public void Release()
    {
        _columnByArchetype = [];
        _compiledArchetypeCount = -1;
        MatchedArchetypeCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetColumnIndex(ArchetypeId archetypeId, out int columnIndex)
    {
        if (!IsCompiled)
            throw new InvalidOperationException("The component lookup has not been compiled by world sealing.");

        uint routeIndex = unchecked(archetypeId.Value - 1);
        if (routeIndex >= (uint)_columnByArchetype.Length)
        {
            columnIndex = -1;
            return false;
        }

        ushort encoded = _columnByArchetype[routeIndex];
        columnIndex = encoded;
        return encoded != MissingColumn;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowLookupInvariant(string message) =>
        throw new InvalidOperationException(message);
}
