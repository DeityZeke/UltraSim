namespace UltraSim.ECS;

public sealed partial class World
{
    /// <summary>
    /// Creates a prefab-backed entity immediately.
    /// </summary>
    public Entity CreatePrefab(PrefabDefinition prefab)
    {
        EnsureNotShutdown();
        return CreatePrefabImmediate(prefab);
    }

    /// <summary>
    /// Creates a typed prefab-backed entity immediately.
    /// </summary>
    public Entity CreatePrefab<TPrefab>(in TPrefab prefab)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        EnsureNotShutdown();
        return CreatePrefabImmediate(in prefab);
    }

    /// <summary>
    /// Resolves and creates a prefab-backed entity immediately.
    /// </summary>
    public Entity CreatePrefab(string key)
    {
        if (TryCreatePrefab(key, out Entity entity))
            return entity;

        throw new InvalidOperationException($"Prefab '{key}' could not be resolved for this world.");
    }

    /// <summary>
    /// Resolves and creates a prefab-backed entity immediately when a resolver is configured.
    /// </summary>
    public bool TryCreatePrefab(string key, out Entity entity)
    {
        EnsureNotShutdown();

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Prefab key is required.", nameof(key));

        IEcsPrefabResolver? resolver = Services.PrefabResolver;
        if (resolver is null || !resolver.TryResolvePrefab(key, out PrefabDefinition prefab))
        {
            entity = Entity.Null;
            return false;
        }

        entity = CreatePrefabImmediate(prefab);
        return true;
    }

    internal Entity CreatePrefabImmediate(PrefabDefinition prefab)
    {
        Entity entity = CreateDefaultImmediate(prefab.Signature, out Archetype archetype, out int slot);
        prefab.ApplyTo(archetype, slot);
        MarkPrefabComponentsDirty(archetype, entity);
        return entity;
    }

    internal Entity CreatePrefabImmediate<TPrefab>(in TPrefab prefab)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        Entity entity = CreateDefaultImmediate(prefab.Signature, out Archetype archetype, out int slot);
        PrefabDefinition.PrefabTypeCache<TPrefab>.Apply(in prefab, archetype, slot);
        MarkPrefabComponentsDirty(archetype, entity);
        return entity;
    }

    internal int CreatePrefabImmediateBatch(ReadOnlySpan<PrefabDefinition> prefabs)
    {
        int created = 0;
        for (int i = 0; i < prefabs.Length; i++)
        {
            _ = CreatePrefabImmediate(prefabs[i]);
            created++;
        }

        return created;
    }

    internal int CreatePrefabImmediateBatch<TPrefab>(ReadOnlySpan<TPrefab> prefabs)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        int created = 0;
        for (int i = 0; i < prefabs.Length; i++)
        {
            _ = CreatePrefabImmediate(in prefabs[i]);
            created++;
        }

        return created;
    }

    private void MarkPrefabComponentsDirty(Archetype archetype, Entity entity)
    {
        ReadOnlySpan<ComponentId> componentIds = archetype.ComponentIds;
        for (int i = 0; i < componentIds.Length; i++)
            _dirty.Mark(componentIds[i], entity);
    }
}
