namespace UltraSim.ECS.Persistence;

/// <summary>
/// Read-only view of a runtime archetype for persistence-oriented traversal.
/// The view remains valid only while the source world stays structurally unchanged.
/// </summary>
public sealed class PersistenceArchetypeView
{
    internal PersistenceArchetypeView(Archetype archetype)
    {
        Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
    }

    internal Archetype Archetype { get; }

    public int EntityCount => Archetype.Count;

    public ReadOnlySpan<Entity> Entities => Archetype.GetEntitySpan();

    public ReadOnlySpan<ComponentId> ComponentIds => Archetype.ComponentIds;

    public bool HasComponent(ComponentId id) => Archetype.HasComponent(id);

    public ReadOnlySpan<T> GetComponentSpan<T>(ComponentId id)
        where T : unmanaged, IComponent<T>
    {
        return Archetype.GetComponentSpan<T>(id);
    }
}
