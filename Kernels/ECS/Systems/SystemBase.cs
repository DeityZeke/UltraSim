namespace UltraSim.ECS;

public abstract class SystemBase
{
    public SystemId Id { get; internal set; } = SystemId.Invalid;
    public abstract SystemDescriptor Descriptor { get; }
    public virtual SystemSettings? Settings => null;
    public virtual IReadOnlyList<SystemControlDescriptor> ControlDescriptors => Array.Empty<SystemControlDescriptor>();
    public bool Enabled { get; internal set; } = true;

    public virtual void OnInitialize(World world)
    {
    }

    public abstract void Update(in SystemContext ctx);

    public virtual void OnShutdown(World world)
    {
    }

    public virtual void Serialize(IDataWriter writer)
    {
    }

    public virtual void Deserialize(IDataReader reader)
    {
    }
}
