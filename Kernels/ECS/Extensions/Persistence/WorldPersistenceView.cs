namespace UltraSim.ECS.Persistence;

/// <summary>
/// Captures a read-only archetype set for persistence-style traversal.
/// The archetype list is stable after construction, while the exposed spans remain
/// valid only until the next structural change in the source world.
/// </summary>
public sealed class WorldPersistenceView
{
    private readonly PersistenceArchetypeView[] _archetypes;

    public WorldPersistenceView(World world)
        : this(world, ReadOnlySpan<ComponentId>.Empty, excludeEmpty: true)
    {
    }

    public WorldPersistenceView(World world, ReadOnlySpan<ComponentId> required, bool excludeEmpty = true)
    {
        ArgumentNullException.ThrowIfNull(world);

        World = world;

        var views = new List<PersistenceArchetypeView>();
        int entityCount = 0;

        foreach (Archetype archetype in world.QueryArchetypes(required))
        {
            if (excludeEmpty && archetype.Count == 0)
                continue;

            views.Add(new PersistenceArchetypeView(archetype));
            entityCount += archetype.Count;
        }

        _archetypes = views.ToArray();
        EntityCount = entityCount;
    }

    public World World { get; }

    public int ArchetypeCount => _archetypes.Length;

    public int EntityCount { get; }

    public IReadOnlyList<PersistenceArchetypeView> Archetypes => _archetypes;
}
