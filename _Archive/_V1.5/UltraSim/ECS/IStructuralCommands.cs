using System;

namespace UltraSim.ECS
{
    public interface IStructuralCommands
    {
        void EnqueueCreateDefault(ArchetypeSignature signature);
        void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures);
        void EnqueueSpawn(ArchetypeSignature signature, PrefabApply apply);
        void EnqueueSpawnTemplate(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData);
        void EnqueueSpawn(PrefabId prefabId);
        void EnqueueSpawn(PrefabId prefabId, int count);
        void EnqueueSpawn(ReadOnlySpan<PrefabId> prefabIds);
        void EnqueueSpawn(string prefabKey);
        void EnqueueDestroy(Entity entity);
        void EnqueueDestroy(ReadOnlySpan<Entity> entities);
        void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>;
        void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>;
        void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>;
        void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>;
        void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>;
    }
}
