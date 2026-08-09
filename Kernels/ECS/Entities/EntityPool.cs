using System.Runtime.CompilerServices;

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
    private readonly EntityLocation[] _locations;
    private int _nextUnusedIndex;
    private int _freeCount;
    private int _aliveCount;
    private int _retiredCount;
    private int _locatedCount;

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
        _locations = new EntityLocation[capacity.MaximumEntities];
    }

    internal WorldId WorldId => _worldId;

    internal EntityCapacity Capacity => _capacity;

    internal int AliveCount => _aliveCount;

    internal int RetiredCount => _retiredCount;

    internal int LocatedCount => _locatedCount;

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

        // TryFree refuses located entities, so this is a construction invariant.
        // Keep the diagnostic in debug builds without paying another location-table
        // read on every production allocation.
        System.Diagnostics.Debug.Assert(
            !_locations[index].IsValid,
            "An available entity slot retained a storage location.");

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
        if (_locations[index].IsValid)
            return false;

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

    /// <summary>
    /// Unbinds and retires a world-validated complete storage population in the
    /// supplied order. The caller proves every handle is live, unique, and
    /// located before clearing the owning store.
    /// </summary>
    internal void RetireValidatedLocated(ReadOnlySpan<Entity> entities)
    {
        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            int entityIndex = checked((int)entity.Index);
            System.Diagnostics.Debug.Assert(
                entity.World == _worldId &&
                entityIndex < _nextUnusedIndex &&
                _states[entityIndex] == SlotAlive &&
                _generations[entityIndex] == entity.Generation &&
                _locations[entityIndex].IsValid,
                "A trusted whole-store retirement received an invalid entity.");

            RetireValidatedIndex(entityIndex);
        }
    }

    /// <summary>
    /// Publishes the donor repair and retires one entity after its owning store
    /// has validated and removed the row. This is the scalar destroy commit:
    /// expected public rejection has already happened, so release builds avoid
    /// repeating liveness, generation, and location checks three more times.
    /// </summary>
    internal void ApplyRelocationAndRetireValidated(
        Entity entity,
        EntityLocation removedLocation,
        ArchetypeId archetypeId,
        in ChunkRowRelocation relocation)
    {
        int entityIndex = checked((int)entity.Index);
        System.Diagnostics.Debug.Assert(
            entity.World == _worldId &&
            entityIndex < _nextUnusedIndex &&
            _states[entityIndex] == SlotAlive &&
            _generations[entityIndex] == entity.Generation &&
            _locations[entityIndex] == removedLocation &&
            removedLocation.ArchetypeId == archetypeId,
            "A trusted scalar destroy commit received an invalid removed entity.");

        if (relocation.HasValue)
        {
            uint donorIndex = relocation.EntityIndex;
            System.Diagnostics.Debug.Assert(
                donorIndex < unchecked((uint)_nextUnusedIndex) &&
                _states[donorIndex] == SlotAlive &&
                _locations[donorIndex] == new EntityLocation(archetypeId, relocation.Source),
                "A trusted scalar destroy commit received an invalid donor relocation.");
            _locations[donorIndex] = new EntityLocation(archetypeId, relocation.Destination);
        }

        RetireValidatedIndex(entityIndex);
    }

    internal bool TryBindLocation(Entity entity, EntityLocation location)
    {
        if (!location.IsValid)
            throw new ArgumentException("A valid entity location is required.", nameof(location));
        if (!IsAlive(entity))
            return false;

        int index = checked((int)entity.Index);
        if (_locations[index].IsValid)
            return false;

        _locations[index] = location;
        _locatedCount++;
        return true;
    }

    internal bool TryGetLocation(Entity entity, out EntityLocation location)
    {
        if (!IsAlive(entity))
        {
            location = EntityLocation.Invalid;
            return false;
        }

        location = _locations[entity.Index];
        return location.IsValid;
    }

    internal bool TryUnbindLocation(Entity entity, EntityLocation expected)
    {
        if (!expected.IsValid)
            throw new ArgumentException("A valid expected entity location is required.", nameof(expected));
        if (!IsAlive(entity))
            return false;

        int index = checked((int)entity.Index);
        if (_locations[index] != expected)
            return false;

        _locations[index] = EntityLocation.Invalid;
        _locatedCount--;
        return true;
    }

    internal bool TryReplaceLocation(
        Entity entity,
        EntityLocation expected,
        EntityLocation replacement)
    {
        if (!expected.IsValid)
            throw new ArgumentException("A valid expected entity location is required.", nameof(expected));
        if (!replacement.IsValid)
            throw new ArgumentException("A valid replacement entity location is required.", nameof(replacement));
        if (!IsAlive(entity))
            return false;

        int index = checked((int)entity.Index);
        if (_locations[index] != expected)
            return false;

        _locations[index] = replacement;
        return true;
    }

    /// <summary>
    /// Transactionally validates and replaces one structural batch. Validation
    /// completes before any location changes, so rejection leaves the table intact.
    /// </summary>
    internal bool TryReplaceLocations(
        ReadOnlySpan<Entity> entities,
        ArchetypeId expectedArchetypeId,
        ReadOnlySpan<ChunkRow> expectedRows,
        ArchetypeId replacementArchetypeId,
        ReadOnlySpan<ChunkRow> replacementRows)
    {
        if (entities.Length != expectedRows.Length || entities.Length != replacementRows.Length)
            throw new ArgumentException("Entity and location batch lengths must match.");
        if (!expectedArchetypeId.IsValid || !replacementArchetypeId.IsValid)
            throw new ArgumentException("Batch location replacement requires valid archetype IDs.");

        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            if (!IsAlive(entity) ||
                _locations[entity.Index] != new EntityLocation(expectedArchetypeId, expectedRows[index]))
            {
                return false;
            }
        }

        for (int index = 0; index < entities.Length; index++)
        {
            _locations[entities[index].Index] =
                new EntityLocation(replacementArchetypeId, replacementRows[index]);
        }

        return true;
    }

    /// <summary>
    /// Applies a trusted row-store swap relocation to the moved live entity.
    /// Any mismatch is an internal structural invariant failure, not a stale
    /// public-handle result.
    /// </summary>
    internal void ApplyRelocation(ArchetypeId archetypeId, in ChunkRowRelocation relocation)
    {
        if (!relocation.HasValue)
            return;
        if (!archetypeId.IsValid)
            throw new ArgumentException("A valid archetype ID is required.", nameof(archetypeId));
        if (!relocation.Source.IsValid || !relocation.Destination.IsValid)
            ThrowLocationInvariant("A row relocation contains an invalid source or destination.");

        uint entityIndex = relocation.EntityIndex;
        if (entityIndex >= (uint)_nextUnusedIndex || _states[entityIndex] != SlotAlive)
            ThrowLocationInvariant("A row relocation refers to an entity slot that is not alive.");

        var expected = new EntityLocation(archetypeId, relocation.Source);
        if (_locations[entityIndex] != expected)
            ThrowLocationInvariant("A row relocation source does not match the entity location table.");

        _locations[entityIndex] = new EntityLocation(archetypeId, relocation.Destination);
    }

    /// <summary>Applies a validated structural compaction's survivor moves.</summary>
    internal void ApplyRelocations(
        ArchetypeId archetypeId,
        ReadOnlySpan<ChunkRowRelocation> relocations)
    {
        if (!archetypeId.IsValid)
            throw new ArgumentException("A valid archetype ID is required.", nameof(archetypeId));

        for (int index = 0; index < relocations.Length; index++)
        {
            ref readonly ChunkRowRelocation relocation = ref relocations[index];
            if (!relocation.HasValue ||
                !relocation.Source.IsValid ||
                !relocation.Destination.IsValid)
            {
                ThrowLocationInvariant("A planned row relocation is invalid.");
            }

            uint entityIndex = relocation.EntityIndex;
            if (entityIndex >= (uint)_nextUnusedIndex || _states[entityIndex] != SlotAlive)
                ThrowLocationInvariant("A planned row relocation refers to an entity slot that is not alive.");

            var expected = new EntityLocation(archetypeId, relocation.Source);
            if (_locations[entityIndex] != expected)
                ThrowLocationInvariant("A planned row relocation source does not match the entity location table.");
        }

        for (int index = 0; index < relocations.Length; index++)
        {
            ref readonly ChunkRowRelocation relocation = ref relocations[index];
            _locations[relocation.EntityIndex] =
                new EntityLocation(archetypeId, relocation.Destination);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RetireValidatedIndex(int entityIndex)
    {
        _locations[entityIndex] = EntityLocation.Invalid;
        _locatedCount--;
        _aliveCount--;
        if (!EntityGeneration.TryAdvance(
                _generations[entityIndex],
                out uint nextGeneration))
        {
            _states[entityIndex] = SlotRetired;
            _retiredCount++;
            return;
        }

        _generations[entityIndex] = nextGeneration;
        _states[entityIndex] = SlotAvailable;
        _freeIndices[_freeCount++] = entityIndex;
    }

    internal SlotSnapshot GetSlotSnapshot(int index)
    {
        if (unchecked((uint)index) >= unchecked((uint)_nextUnusedIndex))
            throw new ArgumentOutOfRangeException(nameof(index));

        return new SlotSnapshot(
            _generations[index],
            _states[index] == SlotAlive,
            _states[index] == SlotRetired,
            _locations[index]);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowLocationInvariant(string message) => throw new InvalidOperationException(message);

    internal readonly record struct SlotSnapshot(
        uint Generation,
        bool IsAlive,
        bool IsRetired,
        EntityLocation Location);
}
