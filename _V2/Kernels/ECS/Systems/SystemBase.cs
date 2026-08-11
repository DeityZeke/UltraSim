namespace UltraSim.ECS;

/// <summary>
/// Base type for runtime systems registered against a <see cref="World"/>.
/// </summary>
public abstract class SystemBase
{
    /// <summary>
    /// Gets the runtime-assigned stable id for this system after registration.
    /// </summary>
    public SystemId Id { get; internal set; } = SystemId.Invalid;

    /// <summary>
    /// Gets the static scheduling and dependency metadata for this system.
    /// </summary>
    public abstract SystemDescriptor Descriptor { get; }

    /// <summary>
    /// Gets optional low-level schedule overrides that are applied when the system is registered or rebuilt.
    /// </summary>
    public virtual SystemSettings? Settings => null;

    /// <summary>
    /// Gets optional control descriptors for future higher-level host/operator surfaces.
    /// </summary>
    public virtual IReadOnlyList<SystemControlDescriptor> ControlDescriptors => Array.Empty<SystemControlDescriptor>();

    /// <summary>
    /// Gets or sets whether this system is scheduled automatically.
    /// </summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>
    /// Called after the world has been sealed and before ticks begin.
    /// </summary>
    public virtual void OnInitialize(World world)
    {
    }

    /// <summary>
    /// Executes this system for the current tick or manual invocation.
    /// </summary>
    public abstract void Update(in SystemContext ctx);

    /// <summary>
    /// Called when the owning world is shutting down.
    /// </summary>
    public virtual void OnShutdown(World world)
    {
    }

    /// <summary>
    /// Saves system-specific runtime state into the supplied writer.
    /// </summary>
    public virtual void Serialize(IDataWriter writer)
    {
    }

    /// <summary>
    /// Restores system-specific runtime state from the supplied reader.
    /// </summary>
    public virtual void Deserialize(IDataReader reader)
    {
    }
}
