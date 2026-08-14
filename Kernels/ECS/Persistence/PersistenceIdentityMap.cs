namespace UltraSim.ECS;

internal sealed class PersistenceIdentityMap
{
    private readonly EntityPool _entities;
    private readonly ulong[] _ids;
    private readonly uint[] _generations;
    private readonly ulong[] _prefabKeyHashes;
    private readonly int[] _lookup;
    private readonly Dictionary<ulong, string> _prefabKeys = new();
    private ulong _highWater;

    internal PersistenceIdentityMap(EntityPool entities, PersistenceIdentityOptions options)
    {
        _entities = entities;
        WorldId = options.WorldId;
        _highWater = options.AllocatorHighWater;
        int capacity = entities.Capacity.MaximumEntities;
        _ids = new ulong[capacity];
        _generations = new uint[capacity];
        _prefabKeyHashes = new ulong[capacity];
        int lookupSize = 2;
        while (lookupSize < checked(capacity * 2)) lookupSize <<= 1;
        _lookup = new int[lookupSize];
    }

    internal PersistentWorldId WorldId { get; }
    internal ulong AllocatorHighWater => _highWater;
    internal int RegisteredPrefabKeyCount => _prefabKeys.Count;
    internal long ReservedBytes => checked(
        (long)_ids.Length * sizeof(ulong) +
        (long)_generations.Length * sizeof(uint) +
        (long)_prefabKeyHashes.Length * sizeof(ulong) +
        (long)_lookup.Length * sizeof(int));

    internal static long EstimateReservedBytes(EntityCapacity capacity)
    {
        int lookupSize = 2;
        while (lookupSize < checked(capacity.MaximumEntities * 2)) lookupSize <<= 1;
        return checked((long)capacity.MaximumEntities * (sizeof(ulong) + sizeof(uint) + sizeof(ulong)) +
            (long)lookupSize * sizeof(int));
    }

    internal PersistentEntityId GetOrAssign(Entity entity)
    {
        if (!_entities.IsAlive(entity)) throw new ArgumentException("Entity is not live.", nameof(entity));
        int index = checked((int)entity.Index);
        if (_ids[index] != 0 && _generations[index] == entity.Generation) return new(_ids[index]);
        if (_ids[index] != 0) Remove(_ids[index], index);
        if (_highWater == ulong.MaxValue) throw new InvalidOperationException("Persistent entity identity exhausted.");
        ulong id = ++_highWater;
        _ids[index] = id;
        _generations[index] = entity.Generation;
        _prefabKeyHashes[index] = 0;
        Insert(id, index);
        return new PersistentEntityId(id);
    }

    internal void AssignRestored(Entity entity, PersistentEntityId id, ulong prefabKeyHash)
    {
        if (!_entities.IsAlive(entity) || !id.IsValid) throw new InvalidOperationException("Invalid restored durable identity.");
        int index = checked((int)entity.Index);
        if (_ids[index] != 0 || TryResolve(id, out _)) throw new InvalidOperationException($"Duplicate persistent entity ID {id.Value}.");
        _ids[index] = id.Value;
        _generations[index] = entity.Generation;
        _prefabKeyHashes[index] = prefabKeyHash;
        Insert(id.Value, index);
    }

    internal void SetAllocatorHighWater(ulong value)
    {
        if (value < _highWater) throw new InvalidOperationException("Persistent identity high-water cannot move backward.");
        _highWater = value;
    }

    internal bool TryGet(Entity entity, out PersistentEntityId id)
    {
        int index = unchecked((int)entity.Index);
        if (entity.World == _entities.WorldId && unchecked((uint)index) < unchecked((uint)_ids.Length) &&
            _entities.IsAlive(entity) && _generations[index] == entity.Generation && _ids[index] != 0)
        {
            id = new PersistentEntityId(_ids[index]);
            return true;
        }
        id = default;
        return false;
    }

    internal bool TryResolve(PersistentEntityId id, out Entity entity)
    {
        if (!id.IsValid) { entity = Entity.Null; return false; }
        int mask = _lookup.Length - 1;
        int slot = GetSlot(id.Value, mask);
        while (_lookup[slot] != 0)
        {
            int entry = _lookup[slot];
            if (entry > 0)
            {
                int index = entry - 1;
                if (_ids[index] == id.Value && _entities.TryGetEntity(unchecked((uint)index), out entity) &&
                    _generations[index] == entity.Generation) return true;
            }
            slot = (slot + 1) & mask;
        }
        entity = Entity.Null;
        return false;
    }

    internal ulong GetPrefabKeyHash(Entity entity) => TryGet(entity, out _) ? _prefabKeyHashes[entity.Index] : 0;
    internal void SetPrefabKeyHash(Entity entity, ulong hash) { _ = GetOrAssign(entity); _prefabKeyHashes[entity.Index] = hash; }
    internal void SetPrefabKey(Entity entity, PrefabKey key)
    {
        if (!key.IsValid) throw new ArgumentException("A valid prefab key is required.", nameof(key));
        SetPrefabKeyHash(entity, key.StableHash);
        if (_prefabKeys.TryGetValue(key.StableHash, out string? existing) && !StringComparer.Ordinal.Equals(existing, key.Value))
            throw new PersistenceValidationException("Prefab stable-key hash collision.");
        _prefabKeys[key.StableHash] = key.Value;
    }
    internal void RegisterRestoredPrefabKey(ulong hash, string key)
    {
        if (hash == 0 || StableHash64.Compute(key) != hash) throw new PersistenceValidationException("Restored prefab key/hash mismatch.");
        if (_prefabKeys.TryGetValue(hash, out string? existing) && !StringComparer.Ordinal.Equals(existing, key))
            throw new PersistenceValidationException("Restored prefab stable-key hash collision.");
        _prefabKeys[hash] = key;
    }
    internal bool TryGetPrefabKey(ulong hash, out string key) => _prefabKeys.TryGetValue(hash, out key!);

    private void Insert(ulong id, int entityIndex)
    {
        int mask = _lookup.Length - 1, slot = GetSlot(id, mask);
        while (_lookup[slot] != 0)
            slot = (slot + 1) & mask;
        _lookup[slot] = entityIndex + 1;
    }

    private void Remove(ulong id, int entityIndex)
    {
        int mask = _lookup.Length - 1, slot = GetSlot(id, mask);
        while (_lookup[slot] != 0)
        {
            if (_lookup[slot] == entityIndex + 1)
            {
                // Back-shift by reinserting the remainder of this probe cluster.
                // Persistent IDs can churn without ever saturating the bounded
                // lookup with historical tombstones.
                _lookup[slot] = 0;
                int next = (slot + 1) & mask;
                while (_lookup[next] != 0)
                {
                    int movedIndex = _lookup[next] - 1;
                    _lookup[next] = 0;
                    Insert(_ids[movedIndex], movedIndex);
                    next = (next + 1) & mask;
                }
                return;
            }
            slot = (slot + 1) & mask;
        }
    }

    private static int GetSlot(ulong value, int mask)
    {
        value ^= value >> 33; value = unchecked(value * 0xff51afd7ed558ccdUL); value ^= value >> 33;
        return unchecked((int)value) & mask;
    }
}
