namespace UltraSim.ECS;

public sealed partial class World
{
    /// <summary>
    /// Creates a prefab-backed entity immediately.
    /// </summary>
    public Entity CreatePrefab(PrefabDefinition prefab)
    {
        EnsureNotShutdown();

        Entity entity = CreateDefaultImmediate(prefab.Signature);
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            throw new InvalidOperationException("Created prefab entity could not be located.");

        prefab.ApplyTo(archetype, slot);

        ReadOnlySpan<ComponentId> componentIds = archetype.ComponentIds;
        for (int i = 0; i < componentIds.Length; i++)
            _dirty.Mark(componentIds[i], entity);

        return entity;
    }

    /// <summary>
    /// Creates a typed prefab-backed entity immediately.
    /// </summary>
    public Entity CreatePrefab<TPrefab>(in TPrefab prefab)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        EnsureNotShutdown();

        Entity entity = CreateDefaultImmediate(prefab.Signature);
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            throw new InvalidOperationException("Created prefab entity could not be located.");

        PrefabDefinition.PrefabTypeCache<TPrefab>.Apply(in prefab, archetype, slot);

        ReadOnlySpan<ComponentId> componentIds = archetype.ComponentIds;
        for (int i = 0; i < componentIds.Length; i++)
            _dirty.Mark(componentIds[i], entity);

        return entity;
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

        entity = CreatePrefab(prefab);
        return true;
    }
}
