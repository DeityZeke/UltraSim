namespace UltraSim.ECS;

/// <summary>Identifies one successful logical entity or component-membership commit.</summary>
/// <remarks>
/// Lifecycle does not report component value writes, enable-state changes, or
/// physical row relocation.
/// </remarks>
public enum LifecycleKind : byte
{
    /// <summary>A new runtime entity identity became live.</summary>
    Created = 1,

    /// <summary>The recorded runtime entity generation ceased to be live.</summary>
    Destroyed = 2,

    /// <summary>A component became a member of an existing entity.</summary>
    ComponentAdded = 3,

    /// <summary>A component ceased to be a member of an existing entity.</summary>
    ComponentRemoved = 4,
}
