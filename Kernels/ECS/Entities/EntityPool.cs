namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity, single-owner entity slot allocator.
/// </summary>
/// <remarks>
/// This type performs no resizing. Construct it before the world allocation
/// boundary and mutate it only from the owning structural thread.
/// </remarks>
internal sealed class EntityPool
{
    private const byte SlotAvailable = 0;
    private const byte SlotAlive = 1;
    private const byte SlotRetired = 2;

    private readonly WorldId _worldId;
    private readonly EntityCapacity _capacity;
    private readonly uint[] _generations;
    private readonly byte[] _states;
    private readonly int[] _freeIndices;
    private int _nextUnusedIndex;
    private int _freeCount;
    private int _aliveCount;
    private int _retiredCount;

    internal EntityPool(WorldId worldId, EntityCapacity capacity)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (capacity.MaximumEntities <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Entity capacity must be positive.");

        _worldId = worldId;
        _capacity = capacity;
        _generations = new uint[capacity.MaximumEntities];
        _states = new byte[capacity.MaximumEntities];
        _freeIndices = new int[capacity.MaximumEntities];
    }

    internal WorldId WorldId => _worldId;

    internal EntityCapacity Capacity => _capacity;

    internal int AliveCount => _aliveCount;

    internal int RetiredCount => _retiredCount;

    internal int HighWatermark => _nextUnusedIndex;

    internal int AvailableCount => _capacity.MaximumEntities - _aliveCount - _retiredCount;

    internal Entity Allocate()
    {
        if (!TryAllocate(out Entity entity))
            throw new EntityCapacityExceededException(_worldId, _capacity);

        return entity;
    }

    internal bool TryAllocate(out Entity entity)
    {
        int index;
        if (_freeCount != 0)
        {
            index = _freeIndices[--_freeCount];
        }
        else
        {
            if (_nextUnusedIndex == _capacity.MaximumEntities)
            {
                entity = Entity.Null;
                return false;
            }

            index = _nextUnusedIndex++;
            _generations[index] = EntityGeneration.First;
        }

        _states[index] = SlotAlive;
        _aliveCount++;
        entity = new Entity(_worldId, (uint)index, _generations[index]);
        return true;
    }

    internal int Allocate(Span<Entity> destination)
    {
        int written = 0;
        while (written < destination.Length && TryAllocate(out Entity entity))
            destination[written++] = entity;

        return written;
    }

    internal bool IsAlive(Entity entity)
    {
        if (entity.World != _worldId)
            return false;

        uint index = entity.Index;
        return index < (uint)_nextUnusedIndex
            && _states[index] == SlotAlive
            && _generations[index] == entity.Generation;
    }

    internal bool TryFree(Entity entity)
    {
        if (!IsAlive(entity))
            return false;

        int index = (int)entity.Index;
        _aliveCount--;

        if (!EntityGeneration.TryAdvance(_generations[index], out uint nextGeneration))
        {
            _states[index] = SlotRetired;
            _retiredCount++;
            return true;
        }

        _generations[index] = nextGeneration;
        _states[index] = SlotAvailable;
        _freeIndices[_freeCount++] = index;
        return true;
    }

    internal int Free(ReadOnlySpan<Entity> entities)
    {
        int freed = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            if (TryFree(entities[i]))
                freed++;
        }

        return freed;
    }

    internal SlotSnapshot GetSlotSnapshot(int index)
    {
        if ((uint)index >= (uint)_nextUnusedIndex)
            throw new ArgumentOutOfRangeException(nameof(index));

        return new SlotSnapshot(_generations[index], _states[index] == SlotAlive, _states[index] == SlotRetired);
    }

    internal readonly record struct SlotSnapshot(uint Generation, bool IsAlive, bool IsRetired);
}
