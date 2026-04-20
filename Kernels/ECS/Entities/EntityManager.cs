using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

public sealed class EntityManager : IEntityReadOnly
{
    private const int DefaultCapacity = 1024;

    private Location[] _locations;
    private uint[] _versions;
    private int[] _freeList;
    private int _freeCount;
    private int _nextIndex;
    private int _aliveCount;

    public int AliveCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _aliveCount;
    }

    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _versions.Length;
    }

    internal EntityManager(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            capacity = DefaultCapacity;

        _locations = new Location[capacity];
        _versions = new uint[capacity];
        _freeList = new int[capacity];

        FillSlots(_locations, 0, capacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Entity Allocate()
    {
        int index;
        uint version;

        if (_freeCount > 0)
        {
            index = _freeList[--_freeCount];
            version = _versions[index];
        }
        else
        {
            index = _nextIndex;
            EnsureCapacity(index + 1);
            _nextIndex++;

            version = _versions[index];
            if (version == 0)
                version = 1;

            _versions[index] = version;
        }

        return new Entity((uint)index, version);
    }

    internal int AllocateBulk(Span<Entity> destination)
    {
        int written = 0;

        while (written < destination.Length && _freeCount > 0)
        {
            int index = _freeList[--_freeCount];
            destination[written++] = new Entity((uint)index, _versions[index]);
        }

        int remaining = destination.Length - written;
        if (remaining <= 0)
            return written;

        int start = _nextIndex;
        int end = start + remaining;
        EnsureCapacity(end);

        for (int i = start; i < end; i++)
        {
            uint version = _versions[i];
            if (version == 0)
            {
                version = 1;
                _versions[i] = version;
            }

            destination[written++] = new Entity((uint)i, version);
        }

        _nextIndex = end;
        return written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsValid(Entity entity)
    {
        int index = (int)entity.Index;
        return (uint)index < (uint)_nextIndex && entity.Version != 0 && _versions[index] == entity.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Entity entity)
    {
        int index = (int)entity.Index;
        return (uint)index < (uint)_nextIndex
            && entity.Version != 0
            && _versions[index] == entity.Version
            && _locations[index].Archetype is not null;
    }

    public bool TryGetLocation(Entity entity, out Archetype archetype, out int slot)
    {
        int index = (int)entity.Index;
        if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
        {
            archetype = null!;
            slot = -1;
            return false;
        }

        ref Location location = ref _locations[index];
        archetype = location.Archetype!;
        if (archetype is null)
        {
            slot = -1;
            return false;
        }

        slot = location.Slot;
        return true;
    }

    internal void SetLocation(Entity entity, Archetype archetype, int slot)
    {
        int index = (int)entity.Index;
        if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
            ThrowInvalidEntity(entity);

        ref Location location = ref _locations[index];
        if (location.Archetype is null)
            _aliveCount++;

        location.Archetype = archetype;
        location.Slot = slot;
    }

    internal void SetLocationsBulkAssumeValid(ReadOnlySpan<Entity> entities, Archetype archetype, int startSlot)
    {
        int slot = startSlot;
        for (int i = 0; i < entities.Length; i++, slot++)
        {
            int index = (int)entities[i].Index;
            ref Location location = ref _locations[index];
            if (location.Archetype is null)
                _aliveCount++;

            location.Archetype = archetype;
            location.Slot = slot;
        }
    }

    internal void Free(Entity entity)
    {
        int index = (int)entity.Index;
        if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
            return;

        FreeAtValidatedIndex(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void FreeAssumeAlive(Entity entity)
    {
        FreeAtValidatedIndex((int)entity.Index);
    }

    internal void FreeBulkAssumeAlive(ReadOnlySpan<Entity> entities)
    {
        if (entities.Length == 0)
            return;

        int neededFreeCapacity = _freeCount + entities.Length;
        if (neededFreeCapacity > _freeList.Length)
            Array.Resize(ref _freeList, Math.Max(_freeList.Length * 2, neededFreeCapacity));

        int freeWrite = _freeCount;
        for (int i = 0; i < entities.Length; i++)
        {
            int index = (int)entities[i].Index;
            if (_locations[index].Archetype is not null)
                _aliveCount--;

            _locations[index] = default;
            _locations[index].Slot = -1;

            uint nextVersion = _versions[index] + 1;
            if (nextVersion == 0)
                nextVersion = 1;

            _versions[index] = nextVersion;
            _freeList[freeWrite++] = index;
        }

        _freeCount = freeWrite;
    }

    private void FreeAtValidatedIndex(int index)
    {
        // Callers guarantee the index/version pair is already validated against the current store state.

        if (_locations[index].Archetype is not null)
            _aliveCount--;

        _locations[index] = default;
        _locations[index].Slot = -1;

        uint nextVersion = _versions[index] + 1;
        if (nextVersion == 0)
            nextVersion = 1;

        _versions[index] = nextVersion;

        if (_freeCount == _freeList.Length)
            Array.Resize(ref _freeList, _freeList.Length * 2);

        _freeList[_freeCount++] = index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int needed)
    {
        if (needed <= _versions.Length)
            return;

        int oldSize = _versions.Length;
        int newSize = Math.Max(oldSize * 2, needed);

        Array.Resize(ref _versions, newSize);
        Array.Resize(ref _locations, newSize);
        Array.Resize(ref _freeList, newSize);

        FillSlots(_locations, oldSize, newSize - oldSize);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidEntity(Entity entity)
        => throw new InvalidOperationException($"Invalid entity: {entity}");

    private static void FillSlots(Location[] locations, int start, int length)
    {
        int end = start + length;
        for (int i = start; i < end; i++)
            locations[i].Slot = -1;
    }

    private struct Location
    {
        public Archetype? Archetype;
        public int Slot;
    }
}
