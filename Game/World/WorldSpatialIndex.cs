using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Spatial;

namespace UltraSim.Game.World;

/// <summary>Cold/current diagnostics for one generation-owned dynamic spatial index.</summary>
public readonly record struct WorldSpatialIndexSnapshot(
    int EntityCapacity,
    int ChunkCapacity,
    int EntityCount,
    int ChunkCount,
    int ChunkHighWater,
    long Insertions,
    long Removals,
    long SameChunkObservations,
    long CrossChunkMigrations,
    long ReservedBytes);

/// <summary>
/// Bounded home-chunk membership derived from authoritative ECS position components.
/// </summary>
/// <remarks>
/// One intrusive home record exists per ECS entity slot. Chunk keys use a fixed
/// open-addressed table; ordinary movement allocates no managed memory and never
/// exposes internal membership slots.
/// </remarks>
public sealed class WorldSpatialIndex
{
    private readonly HomeRecord[] _homes;
    private readonly ChunkRecord[] _chunks;
    private readonly int[] _freeChunks;
    private readonly SpatialChunkKey[] _mapKeys;
    private readonly int[] _mapValues;
    private readonly byte[] _mapStates;
    private readonly int _mapMask;
    private int _freeChunkCount;
    private int _entityCount;
    private int _chunkCount;
    private int _chunkHighWater;
    private long _insertions;
    private long _removals;
    private long _sameChunkObservations;
    private long _crossChunkMigrations;

    public WorldSpatialIndex(int entityCapacity, int? chunkCapacity = null)
    {
        if (entityCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(entityCapacity));
        int chunks = chunkCapacity ?? entityCapacity;
        if (chunks < entityCapacity)
            throw new ArgumentOutOfRangeException(nameof(chunkCapacity),
                "Chunk capacity must admit the theoretical one-chunk-per-spatial-entity maximum.");

        int mapCapacity = GetMapCapacity(chunks);
        _homes = new HomeRecord[entityCapacity];
        _chunks = new ChunkRecord[chunks];
        _freeChunks = new int[chunks];
        _mapKeys = new SpatialChunkKey[mapCapacity];
        _mapValues = new int[mapCapacity];
        _mapStates = new byte[mapCapacity];
        _mapMask = mapCapacity - 1;
        ResetStorage();
    }

    public int EntityCapacity => _homes.Length;
    public int ChunkCapacity => _chunks.Length;

    public WorldSpatialIndexSnapshot GetSnapshot() => new(
        _homes.Length,
        _chunks.Length,
        _entityCount,
        _chunkCount,
        _chunkHighWater,
        _insertions,
        _removals,
        _sameChunkObservations,
        _crossChunkMigrations,
        CalculateReservedBytes());

    public bool TryGetHome(Entity entity, out SpatialChunkKey key)
    {
        if (!entity.IsValid || entity.Index >= (uint)_homes.Length)
        {
            key = default;
            return false;
        }

        ref readonly HomeRecord home = ref _homes[entity.Index];
        if (!home.Occupied || home.Entity != entity)
        {
            key = default;
            return false;
        }

        key = _chunks[home.ChunkSlot].Key;
        return true;
    }

    public int GetMemberCount(SpatialChunkKey key) =>
        TryFindChunk(key, out int slot) ? _chunks[slot].Count : 0;

    /// <summary>Copies exact runtime identities in stable current intrusive order.</summary>
    public int CopyMembers(SpatialChunkKey key, Span<Entity> destination)
    {
        if (!TryFindChunk(key, out int slot))
            return 0;
        ref readonly ChunkRecord chunk = ref _chunks[slot];
        if (destination.Length < chunk.Count)
            throw new ArgumentException("The member destination is too small.", nameof(destination));
        int count = 0;
        for (int homeIndex = chunk.Head; homeIndex >= 0; homeIndex = _homes[homeIndex].Next)
            destination[count++] = _homes[homeIndex].Entity;
        return count;
    }

    internal void Reconcile(Entity entity, SpatialChunkKey key)
    {
        if (!entity.IsValid || entity.Index >= (uint)_homes.Length)
            throw new InvalidOperationException("Spatial authority referenced an entity outside the admitted capacity.");
        int homeIndex = checked((int)entity.Index);
        ref HomeRecord home = ref _homes[homeIndex];
        if (home.Occupied && home.Entity != entity)
            RemoveAt(homeIndex);

        if (!home.Occupied)
        {
            int destination = GetOrCreateChunk(key);
            Link(homeIndex, entity, destination);
            _entityCount++;
            _insertions++;
            return;
        }

        ref ChunkRecord current = ref _chunks[home.ChunkSlot];
        if (current.Key == key)
        {
            _sameChunkObservations++;
            return;
        }

        if (_freeChunkCount == 0 && current.Count == 1)
        {
            RemoveMap(current.Key);
            current.Key = key;
            if (!TryAddMap(key, home.ChunkSlot))
                throw new InvalidOperationException("Spatial chunk lookup lost guaranteed replacement capacity.");
            _crossChunkMigrations++;
            return;
        }

        int destinationSlot = GetOrCreateChunk(key);
        Unlink(homeIndex);
        LinkExisting(homeIndex, entity, destinationSlot);
        _crossChunkMigrations++;
    }

    internal void Remove(uint entityIndex)
    {
        if (entityIndex >= (uint)_homes.Length)
            throw new InvalidOperationException("Spatial lifecycle referenced an entity outside the admitted capacity.");
        if (_homes[entityIndex].Occupied)
            RemoveAt(checked((int)entityIndex));
    }

    private void RemoveAt(int homeIndex)
    {
        Unlink(homeIndex);
        _homes[homeIndex] = default;
        _entityCount--;
        _removals++;
    }

    private int GetOrCreateChunk(SpatialChunkKey key)
    {
        if (!key.Space.IsValid)
            throw new ArgumentException("A spatial chunk key must name a valid space.", nameof(key));
        if (TryFindChunk(key, out int existing))
            return existing;
        if (_freeChunkCount == 0)
            throw new InvalidOperationException("Spatial chunk capacity was exhausted despite the entity-capacity guarantee.");

        int slot = _freeChunks[--_freeChunkCount];
        _chunks[slot] = new ChunkRecord(key, -1, -1, 0);
        if (!TryAddMap(key, slot))
            throw new InvalidOperationException("Spatial chunk lookup capacity was exhausted.");
        _chunkCount++;
        _chunkHighWater = Math.Max(_chunkHighWater, _chunkCount);
        return slot;
    }

    private void Link(int homeIndex, Entity entity, int chunkSlot)
    {
        _homes[homeIndex] = new HomeRecord(entity, chunkSlot, -1, -1, true);
        LinkExisting(homeIndex, entity, chunkSlot);
    }

    private void LinkExisting(int homeIndex, Entity entity, int chunkSlot)
    {
        ref ChunkRecord chunk = ref _chunks[chunkSlot];
        int previousTail = chunk.Tail;
        _homes[homeIndex] = new HomeRecord(entity, chunkSlot, previousTail, -1, true);
        if (previousTail >= 0)
            _homes[previousTail].Next = homeIndex;
        else
            chunk.Head = homeIndex;
        chunk.Tail = homeIndex;
        chunk.Count++;
    }

    private void Unlink(int homeIndex)
    {
        ref HomeRecord home = ref _homes[homeIndex];
        int chunkSlot = home.ChunkSlot;
        ref ChunkRecord chunk = ref _chunks[chunkSlot];
        if (home.Previous >= 0)
            _homes[home.Previous].Next = home.Next;
        else
            chunk.Head = home.Next;
        if (home.Next >= 0)
            _homes[home.Next].Previous = home.Previous;
        else
            chunk.Tail = home.Previous;
        chunk.Count--;
        if (chunk.Count == 0)
        {
            RemoveMap(chunk.Key);
            chunk = default;
            _freeChunks[_freeChunkCount++] = chunkSlot;
            _chunkCount--;
        }
    }

    private bool TryFindChunk(SpatialChunkKey key, out int slot)
    {
        int bucket = key.GetHashCode() & _mapMask;
        for (int probe = 0; probe < _mapStates.Length; probe++)
        {
            byte state = _mapStates[bucket];
            if (state == 0) break;
            if (state == 1 && _mapKeys[bucket] == key)
            {
                slot = _mapValues[bucket];
                return true;
            }
            bucket = (bucket + 1) & _mapMask;
        }
        slot = -1;
        return false;
    }

    private bool TryAddMap(SpatialChunkKey key, int slot)
    {
        int bucket = key.GetHashCode() & _mapMask;
        int firstTombstone = -1;
        for (int probe = 0; probe < _mapStates.Length; probe++)
        {
            byte state = _mapStates[bucket];
            if (state == 1)
            {
                if (_mapKeys[bucket] == key) return false;
            }
            else if (state == 2)
            {
                if (firstTombstone < 0) firstTombstone = bucket;
            }
            else
            {
                WriteMap(firstTombstone >= 0 ? firstTombstone : bucket, key, slot);
                return true;
            }
            bucket = (bucket + 1) & _mapMask;
        }
        if (firstTombstone < 0) return false;
        WriteMap(firstTombstone, key, slot);
        return true;
    }

    private void RemoveMap(SpatialChunkKey key)
    {
        int bucket = key.GetHashCode() & _mapMask;
        for (int probe = 0; probe < _mapStates.Length; probe++)
        {
            byte state = _mapStates[bucket];
            if (state == 0) break;
            if (state == 1 && _mapKeys[bucket] == key)
            {
                _mapStates[bucket] = 2;
                _mapValues[bucket] = 0;
                _mapKeys[bucket] = default;
                return;
            }
            bucket = (bucket + 1) & _mapMask;
        }
        throw new InvalidOperationException("Spatial chunk lookup and chunk storage diverged.");
    }

    private void WriteMap(int bucket, SpatialChunkKey key, int slot)
    {
        _mapKeys[bucket] = key;
        _mapValues[bucket] = slot;
        _mapStates[bucket] = 1;
    }

    private void ResetStorage()
    {
        Array.Clear(_homes);
        Array.Clear(_chunks);
        Array.Clear(_mapKeys);
        Array.Clear(_mapValues);
        Array.Clear(_mapStates);
        _freeChunkCount = _freeChunks.Length;
        for (int index = 0; index < _freeChunks.Length; index++)
            _freeChunks[index] = _freeChunks.Length - 1 - index;
        _entityCount = 0;
        _chunkCount = 0;
    }

    private long CalculateReservedBytes() => checked(
        (long)_homes.Length * Unsafe.SizeOf<HomeRecord>() +
        (long)_chunks.Length * Unsafe.SizeOf<ChunkRecord>() +
        (long)_freeChunks.Length * sizeof(int) +
        (long)_mapKeys.Length * Unsafe.SizeOf<SpatialChunkKey>() +
        (long)_mapValues.Length * sizeof(int) +
        _mapStates.Length);

    private static int GetMapCapacity(int chunks)
    {
        int required = checked(chunks * 2);
        int capacity = 1;
        while (capacity < required)
            capacity = checked(capacity << 1);
        return capacity;
    }

    private struct HomeRecord
    {
        internal HomeRecord(Entity entity, int chunkSlot, int previous, int next, bool occupied) =>
            (Entity, ChunkSlot, Previous, Next, Occupied) = (entity, chunkSlot, previous, next, occupied);
        internal Entity Entity;
        internal int ChunkSlot;
        internal int Previous;
        internal int Next;
        internal bool Occupied;
    }

    private struct ChunkRecord
    {
        internal ChunkRecord(SpatialChunkKey key, int head, int tail, int count) =>
            (Key, Head, Tail, Count) = (key, head, tail, count);
        internal SpatialChunkKey Key;
        internal int Head;
        internal int Tail;
        internal int Count;
    }
}
