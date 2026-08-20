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
        ReadOnlySpan<Archetype> archetypes = world.Archetypes.AsSpan();
        int archetypeCount = 0;
        int entityCount = 0;

        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            if (required.Length != 0 && !archetype.Signature.HasAll(required))
                continue;

            if (excludeEmpty && archetype.Count == 0)
                continue;

            archetypeCount++;
            entityCount += archetype.Count;
        }

        if (archetypeCount == 0)
        {
            _archetypes = Array.Empty<PersistenceArchetypeView>();
            EntityCount = 0;
            return;
        }

        _archetypes = new PersistenceArchetypeView[archetypeCount];
        int write = 0;
        for (int i = 0; i < archetypes.Length; i++)
        {
            Archetype archetype = archetypes[i];
            if (required.Length != 0 && !archetype.Signature.HasAll(required))
                continue;

            if (excludeEmpty && archetype.Count == 0)
                continue;

            _archetypes[write++] = new PersistenceArchetypeView(archetype);
        }

        EntityCount = entityCount;
    }

    public World World { get; }

    public int ArchetypeCount => _archetypes.Length;

    public int EntityCount { get; }

    public IReadOnlyList<PersistenceArchetypeView> Archetypes => _archetypes;
}
