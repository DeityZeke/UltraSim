namespace UltraSim.ECS;

public sealed class World
{
    public World(EcsSchema schema, EcsWorldServices? services = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        Services = services ?? new EcsWorldServices();
    }

    public EcsSchema Schema { get; }
    public EcsWorldServices Services { get; }

    public ComponentId GetComponentId<T>() where T : unmanaged, IComponent<T>
        => Schema.GetComponentId<T>();
}
