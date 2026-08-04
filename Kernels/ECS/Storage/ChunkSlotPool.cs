namespace UltraSim.ECS;

/// <summary>Fixed, single-owner generational slot lifecycle shared by arena prototypes.</summary>
internal sealed class ChunkSlotPool
{
    private const byte SlotAvailable = 0;
    private const byte SlotAlive = 1;
    private const byte SlotRetired = 2;

    private readonly ChunkArenaId _arenaId;
    private readonly uint[] _generations;
    private readonly byte[] _states;
    private readonly int[] _freeIndices;
    private int _nextUnusedIndex;
    private int _freeCount;
    private int _aliveCount;
    private int _retiredCount;

    internal ChunkSlotPool(ChunkArenaId arenaId, int capacity)
    {
        if (!arenaId.IsValid)
            throw new ArgumentException("A valid arena ID is required.", nameof(arenaId));
        if (capacity <= 0 || capacity > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _arenaId = arenaId;
        _generations = new uint[capacity];
        _states = new byte[capacity];
        _freeIndices = new int[capacity];
    }

    internal ChunkArenaId ArenaId => _arenaId;
    internal int Capacity => _generations.Length;
    internal int AliveCount => _aliveCount;
    internal int RetiredCount => _retiredCount;
    internal int HighWatermark => _nextUnusedIndex;
    internal int AvailableCount => Capacity - _aliveCount - _retiredCount;

    internal ChunkHandle Acquire()
    {
        if (!TryAcquire(out ChunkHandle handle))
            throw new InvalidOperationException($"Chunk arena {_arenaId.Value} exhausted its fixed capacity of {Capacity} chunks.");
        return handle;
    }

    internal bool TryAcquire(out ChunkHandle handle)
    {
        int index;
        if (_freeCount != 0)
        {
            index = _freeIndices[--_freeCount];
        }
        else
        {
            if (_nextUnusedIndex == Capacity)
            {
                handle = ChunkHandle.Null;
                return false;
            }

            index = _nextUnusedIndex++;
            _generations[index] = EntityGeneration.First;
        }

        _states[index] = SlotAlive;
        _aliveCount++;
        handle = new ChunkHandle(_arenaId, (uint)index, _generations[index]);
        return true;
    }

    internal bool IsAlive(ChunkHandle handle)
    {
        if (handle.ArenaId != _arenaId)
            return false;

        uint index = handle.Index;
        return index < (uint)_nextUnusedIndex &&
            _states[index] == SlotAlive &&
            _generations[index] == handle.Generation;
    }

    internal bool TryRelease(ChunkHandle handle)
    {
        if (!IsAlive(handle))
            return false;

        int index = (int)handle.Index;
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

    internal void ValidateAlive(ChunkHandle handle)
    {
        if (!IsAlive(handle))
            throw new ArgumentException("Chunk handle is stale, foreign, released, or otherwise invalid.", nameof(handle));
    }

    internal SlotSnapshot GetSlotSnapshot(int index)
    {
        if ((uint)index >= (uint)_nextUnusedIndex)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new SlotSnapshot(_generations[index], _states[index] == SlotAlive, _states[index] == SlotRetired);
    }

    internal readonly record struct SlotSnapshot(uint Generation, bool IsAlive, bool IsRetired);
}
