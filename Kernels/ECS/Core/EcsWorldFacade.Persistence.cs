namespace UltraSim.ECS;

public sealed partial class EcsWorld
{
    /// <summary>Gets the configured durable world namespace.</summary>
    public PersistentWorldId PersistentWorldId
    {
        get
        {
            EnsureRuntimeAccess();
            return _core.PersistenceIdentities.WorldId;
        }
    }

    /// <summary>Gets or monotonically assigns this live entity's world-local durable identity.</summary>
    public PersistentEntityId GetOrAssignPersistentId(Entity entity)
    {
        EnsureRuntimeAccess();
        return _core.PersistenceIdentities.GetOrAssign(entity);
    }

    /// <summary>Reads an already assigned durable identity without creating one.</summary>
    public bool TryGetPersistentId(Entity entity, out PersistentEntityId id)
    {
        EnsureRuntimeAccess();
        return _core.PersistenceIdentities.TryGet(entity, out id);
    }

    /// <summary>Resolves a live runtime entity from a world-local durable identity.</summary>
    public bool TryResolvePersistentEntity(PersistentEntityId id, out Entity entity)
    {
        EnsureRuntimeAccess();
        return _core.PersistenceIdentities.TryResolve(id, out entity);
    }

    /// <summary>Gets the number of distinct prefab provenance keys retained by this world.</summary>
    /// <remarks>
    /// Keys originate from the immutable compiled content catalog or the finite
    /// restored snapshot table. A key may remain registered after its final live
    /// entity is destroyed so provenance hashes never become ambiguous. The
    /// associated CLR dictionary/string overhead is intentionally excluded from
    /// <see cref="WorldReservedPayloadSnapshot"/> fixed-array byte accounting.
    /// </remarks>
    public int RegisteredPrefabKeyCount
    {
        get
        {
            EnsureRuntimeAccess();
            return _core.PersistenceIdentities.RegisteredPrefabKeyCount;
        }
    }

    public bool TryGetPrefabProvenance(Entity entity, out PrefabKey key)
    {
        EnsureRuntimeAccess();
        ulong hash = _core.PersistenceIdentities.GetPrefabKeyHash(entity);
        if (hash != 0 && _core.PersistenceIdentities.TryGetPrefabKey(hash, out string value))
        {
            key = new PrefabKey(value);
            return true;
        }
        key = PrefabKey.Invalid;
        return false;
    }
}
