namespace UltraSim.ECS;

/// <summary>
/// Compile-time marker for unmanaged ECS component data.
/// </summary>
/// <typeparam name="TSelf">The component type itself.</typeparam>
public interface IComponent<TSelf>
    where TSelf : unmanaged, IComponent<TSelf>
{
}
