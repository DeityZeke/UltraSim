namespace UltraSim.ECS;

/// <summary>
/// Low-level hook for schedule-oriented system configuration.
/// </summary>
public abstract class SystemSettings
{
    /// <summary>
    /// Applies schedule changes before the system is sealed into the runtime.
    /// </summary>
    public virtual void Apply(ref SystemSchedule schedule)
    {
    }
}
