using System;
#if DEBUG
using System.Collections.Generic;
#endif
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    /// <summary>
    /// ECS internal entity storage/lookup implementation.
    /// Not intended as a direct mutation API for gameplay code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
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
                && _locations[index].Archetype != null;
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

            ref var location = ref _locations[index];
            archetype = location.Archetype!;
            if (archetype == null)
            {
                slot = -1;
                return false;
            }

            slot = location.Slot;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetLocation(Entity entity, Archetype archetype, int slot)
        {
            int index = (int)entity.Index;
            if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
                ThrowInvalidEntity(entity);

            ref var location = ref _locations[index];
            if (location.Archetype == null)
                _aliveCount++;

            location.Archetype = archetype;
            location.Slot = slot;
        }

        internal void SetLocationsBulk(ReadOnlySpan<Entity> entities, Archetype archetype, int startSlot)
        {
            int slot = startSlot;
            for (int i = 0; i < entities.Length; i++, slot++)
            {
                var entity = entities[i];
                int index = (int)entity.Index;
                if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
                    ThrowInvalidEntity(entity);

                ref var location = ref _locations[index];
                if (location.Archetype == null)
                    _aliveCount++;

                location.Archetype = archetype;
                location.Slot = slot;
            }
        }

        internal void SetLocationsBulkAssumeValid(ReadOnlySpan<Entity> entities, Archetype archetype, int startSlot)
        {
            int slot = startSlot;
            for (int i = 0; i < entities.Length; i++, slot++)
            {
                int index = (int)entities[i].Index;
                ref var location = ref _locations[index];
                if (location.Archetype == null)
                    _aliveCount++;

                location.Archetype = archetype;
                location.Slot = slot;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ClearLocation(Entity entity)
        {
            int index = (int)entity.Index;
            if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
                ThrowInvalidEntity(entity);

            if (_locations[index].Archetype != null)
                _aliveCount--;

            _locations[index] = default;
            _locations[index].Slot = -1;
        }

        internal void Free(Entity entity)
        {
            int index = (int)entity.Index;
            if ((uint)index >= (uint)_nextIndex || entity.Version == 0 || _versions[index] != entity.Version)
                return;

            if (_locations[index].Archetype != null)
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

#if DEBUG
        internal void DebugValidateLocations()
        {
            int alive = 0;
            for (int i = 0; i < _nextIndex; i++)
            {
                ref var location = ref _locations[i];
                if (location.Archetype == null)
                    continue;

                alive++;

                int slot = location.Slot;
                if ((uint)slot >= (uint)location.Archetype.Count)
                    throw new InvalidOperationException($"Entity slot out of range for index={i}, slot={slot}.");

                var expected = new Entity((uint)i, _versions[i]);
                var actual = location.Archetype.GetEntityRef(slot);
                if (actual != expected)
                    throw new InvalidOperationException($"Entity location mismatch for index={i}. expected={expected} actual={actual}.");
            }

            if (alive != _aliveCount)
                throw new InvalidOperationException($"Alive count mismatch. tracked={_aliveCount} computed={alive}.");

            var seenFree = new HashSet<int>(_freeCount);
            for (int i = 0; i < _freeCount; i++)
            {
                int index = _freeList[i];
                if ((uint)index >= (uint)_nextIndex)
                    throw new InvalidOperationException($"Free list index out of range: {index}.");

                if (_locations[index].Archetype != null)
                    throw new InvalidOperationException($"Free list index still has archetype location: {index}.");

                if (!seenFree.Add(index))
                    throw new InvalidOperationException($"Duplicate index in free list: {index}.");
            }
        }
#endif
    }
}
