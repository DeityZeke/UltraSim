namespace UltraSim.ECS;

public interface IStructuralCommands
{
    void EnqueueCreateDefault(ArchetypeSignature signature);
    void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures);
    void EnqueueCreatePrefab(PrefabDefinition prefab);
    void EnqueueCreatePrefab(ReadOnlySpan<PrefabDefinition> prefabs);
    void EnqueueCreatePrefab<TPrefab>(in TPrefab prefab) where TPrefab : unmanaged, IPrefab<TPrefab>;
    void EnqueueCreatePrefab<TPrefab>(ReadOnlySpan<TPrefab> prefabs) where TPrefab : unmanaged, IPrefab<TPrefab>;
    void EnqueueCreatePrefab(string key);
    bool TryEnqueueCreatePrefab(string key);
    void EnqueueDestroy(Entity entity);
    void EnqueueDestroy(ReadOnlySpan<Entity> entities);
    void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>;
    void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>;
    void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>;
    void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>;
    void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>;
}
