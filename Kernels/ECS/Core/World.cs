namespace UltraSim.ECS;

public sealed class World
{
    public World(EcsSchema schema, EcsWorldServices? services = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        Services = services ?? new EcsWorldServices();
        EntityStore = new EntityManager();
        Archetypes = new ArchetypeManager(schema);
    }

    public EcsSchema Schema { get; }
    public EcsWorldServices Services { get; }
    public IEntityReadOnly Entities => EntityStore;

    internal EntityManager EntityStore { get; }
    internal ArchetypeManager Archetypes { get; }

    public ComponentId GetComponentId<T>() where T : unmanaged, IComponent<T>
        => Schema.GetComponentId<T>();
}
