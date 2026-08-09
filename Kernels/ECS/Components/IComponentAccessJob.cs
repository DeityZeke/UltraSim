namespace UltraSim.ECS;

/// <summary>
/// Allocation-free, callback-bounded random access to one declared component.
/// </summary>
/// <remarks>
/// The accessor exists only for the duration of <see cref="EcsWorld.ExecuteAccess"/>.
/// Mutation outside the supplied accessor, structural mutation, nested ECS
/// execution, disposal, and lifecycle transitions are rejected until the callback
/// returns. Implementations cannot retain the stack-only accessor.
/// </remarks>
public interface IComponentAccessJob<T>
    where T : unmanaged, IComponent<T>
{
    void Execute(in ComponentAccessor<T> access);
}
