namespace UltraSim.ECS;

public interface IStructuralCommands
{
    void EnqueueCreateDefault(ArchetypeSignature signature);
    void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures);
    void EnqueueDestroy(Entity entity);
    void EnqueueDestroy(ReadOnlySpan<Entity> entities);
    void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>;
    void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>;
}
