using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public sealed class Archetype
    {
        private const int DefaultEntityCapacity = 1024;
        private const int MinGrowth = 64;

        // Entity storage - raw array for Span access
        private Entity[] _entities;
        private int _entityCount;

        // Column storage
        private readonly ComponentId[] _signatureIds;
        private IComponentColumn[] _columns;
        private int _columnCount;
        private bool _columnsInitialized;

        // Fast lookup: ComponentId.Value -> column index (-1 if not present)
        // Using array instead of Dictionary for O(1) lookup
        private int[] _componentToColumn;

        public ArchetypeSignature Signature { get; }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entityCount;
        }

        public ReadOnlySpan<ComponentId> ComponentIds
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _signatureIds;
        }

        internal Archetype(ArchetypeSignature signature, ComponentId[] componentIds)
        {
            Signature = signature;
            _signatureIds = componentIds;

            _entities = new Entity[DefaultEntityCapacity];
            _entityCount = 0;

            _columns = componentIds.Length > 0 ? new IComponentColumn[componentIds.Length] : Array.Empty<IComponentColumn>();
            _columnCount = 0;

            // Initialize component lookup table
            int maxComponentId = 0;
            for (int i = 0; i < componentIds.Length; i++)
            {
                int id = componentIds[i].Value;
                if (id > maxComponentId)
                    maxComponentId = id;
            }

            _componentToColumn = new int[maxComponentId + 1];
            _componentToColumn.AsSpan().Fill(-1);

            if (_signatureIds.Length == 0)
                _columnsInitialized = true;
        }

        internal void InitializeColumns(Func<ComponentId, int, IComponentColumn> columnFactory)
        {
            if (_columnsInitialized)
                return;

            for (int i = 0; i < _signatureIds.Length; i++)
            {
                var id = _signatureIds[i];
                if (_componentToColumn[id.Value] >= 0)
                    continue;

                var column = columnFactory(id, _entityCount);
                column.EnsureCapacity(_entityCount);
                for (int e = 0; e < _entityCount; e++)
                    column.SetDefaultAt(e);

                _columns[_columnCount] = column;
                _componentToColumn[id.Value] = _columnCount;
                _columnCount++;
            }

            if (_columnCount != _signatureIds.Length)
                ThrowInvalidOperation("Column factory did not initialize all signature components.");

            _columnsInitialized = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int AddEntity(Entity entity)
        {
            if (!_columnsInitialized)
                ThrowNotInitialized();

            int slot = _entityCount;
            EnsureEntityCapacity(slot + 1);
            _entities[slot] = entity;
            _entityCount = slot + 1;

            for (int i = 0; i < _columnCount; i++)
            {
                _columns[i].EnsureCapacity(_entityCount);
                _columns[i].SetDefaultAt(slot);
            }

            return slot;
        }

        internal void Reserve(int additionalCount)
        {
            if (additionalCount <= 0)
                return;

            if (!_columnsInitialized)
                ThrowNotInitialized();

            int needed = _entityCount + additionalCount;
            EnsureEntityCapacity(needed);

            for (int i = 0; i < _columnCount; i++)
                _columns[i].EnsureCapacity(needed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int AddEntityUninitialized(Entity entity)
        {
            if (!_columnsInitialized)
                ThrowNotInitialized();

            int slot = _entityCount;
            EnsureEntityCapacity(slot + 1);

            _entities[slot] = entity;
            _entityCount = slot + 1;

            for (int i = 0; i < _columnCount; i++)
                _columns[i].EnsureCapacity(_entityCount);

            return slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int AddEntityUninitializedAssumeCapacity(Entity entity)
        {
            if (!_columnsInitialized)
                ThrowNotInitialized();

            int slot = _entityCount;
            _entities[slot] = entity;
            _entityCount = slot + 1;

            return slot;
        }

        internal int AddEntitiesUninitialized(ReadOnlySpan<Entity> entities)
        {
            if (!_columnsInitialized)
                ThrowNotInitialized();

            int count = entities.Length;
            if (count == 0)
                return _entityCount;

            int start = _entityCount;
            int needed = start + count;
            EnsureEntityCapacity(needed);

            entities.CopyTo(_entities.AsSpan(start, count));
            _entityCount = needed;

            for (int i = 0; i < _columnCount; i++)
                _columns[i].EnsureCapacity(needed);

            return start;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotInitialized()
            => throw new InvalidOperationException("Archetype columns not initialized.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperation(string message)
            => throw new InvalidOperationException(message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool RemoveAtSwap(int slot, out Entity movedEntity, out int movedSlot)
        {
            movedEntity = default;
            movedSlot = -1;

            int last = _entityCount - 1;
            if ((uint)slot > (uint)last)
                return false;

            if (slot != last)
            {
                movedEntity = _entities[last];
                movedSlot = slot;
                _entities[slot] = movedEntity;

                for (int i = 0; i < _columnCount; i++)
                    _columns[i].SwapLastIntoSlot(slot, last);
            }

            _entityCount = last;
            return true;
        }

        internal void EnsureColumn<T>(ComponentId id) where T : unmanaged, IComponent<T>
        {
            int idVal = id.Value;

            if (idVal < 0)
                ThrowInvalidOperation("ComponentId.Invalid is not allowed.");

            if (idVal >= _componentToColumn.Length)
                GrowLookup(idVal);

            if (_componentToColumn[idVal] >= 0)
                return;

            if (!Signature.Has(id))
                ThrowInvalidOperation($"Component {id} is not part of this archetype signature.");

            var column = new ComponentColumn<T>(_entityCount);
            column.EnsureCapacity(_entityCount);
            for (int i = 0; i < _entityCount; i++)
                column.SetDefaultAt(i);

            if (_columnCount >= _columns.Length)
                Array.Resize(ref _columns, Math.Max(_columns.Length * 2, _columnCount + 1));

            _columns[_columnCount] = column;
            _componentToColumn[idVal] = _columnCount;
            _columnCount++;

            if (_columnCount == _signatureIds.Length)
                _columnsInitialized = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowLookup(int idVal)
        {
            int newLen = Math.Max(_componentToColumn.Length * 2, idVal + 1);
            var newLookup = new int[newLen];
            _componentToColumn.AsSpan().CopyTo(newLookup);
            newLookup.AsSpan(_componentToColumn.Length).Fill(-1);
            _componentToColumn = newLookup;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<Entity> GetEntitySpan() => _entities.AsSpan(0, _entityCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Entity GetEntityRef(int slot)
        {
            if ((uint)slot >= (uint)_entityCount)
                ThrowOutOfRange(slot);

            return ref _entities[slot];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowOutOfRange(int slot)
            => throw new ArgumentOutOfRangeException(nameof(slot), $"Slot {slot} out of range [0, {_entityCount})");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetComponentSpan<T>(ComponentId id) where T : unmanaged, IComponent<T>
        {
            int idVal = id.Value;
            if ((uint)idVal < (uint)_componentToColumn.Length)
            {
                int colIdx = _componentToColumn[idVal];
                if (colIdx >= 0 && _columns[colIdx] is ComponentColumn<T> typed)
                    return typed.AsSpan(_entityCount);
            }

            ThrowColumnNotFound(id);
            return default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowColumnNotFound(ComponentId id)
            => throw new InvalidOperationException($"Component column not found for id {id}.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetComponentSpan<T>(ComponentId id, out Span<T> span) where T : unmanaged, IComponent<T>
        {
            int idVal = id.Value;
            if ((uint)idVal < (uint)_componentToColumn.Length)
            {
                int colIdx = _componentToColumn[idVal];
                if (colIdx >= 0 && _columns[colIdx] is ComponentColumn<T> typed)
                {
                    span = typed.AsSpan(_entityCount);
                    return true;
                }
            }

            span = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponentRef<T>(ComponentId id, int slot) where T : unmanaged, IComponent<T>
        {
            var span = GetComponentSpan<T>(id);
            if ((uint)slot >= (uint)span.Length)
                ThrowOutOfRange(slot);

            return ref span[slot];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(ComponentId id)
        {
            int idVal = id.Value;
            return (uint)idVal < (uint)_componentToColumn.Length && _componentToColumn[idVal] >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetColumnIndex(ComponentId id)
        {
            int idVal = id.Value;
            return (uint)idVal < (uint)_componentToColumn.Length ? _componentToColumn[idVal] : -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IComponentColumn GetColumnAt(int columnIndex) => _columns[columnIndex];

#if DEBUG
        internal void DebugValidateInvariants()
        {
            if (_entityCount < 0 || _entityCount > _entities.Length)
                throw new InvalidOperationException($"Archetype entity count out of range: {_entityCount}/{_entities.Length}.");

            if (_columnCount < 0 || _columnCount > _columns.Length)
                throw new InvalidOperationException($"Archetype column count out of range: {_columnCount}/{_columns.Length}.");

            if (_columnsInitialized && _columnCount != _signatureIds.Length)
                throw new InvalidOperationException($"Archetype initialized column count mismatch: {_columnCount} vs signature {_signatureIds.Length}.");

            for (int i = 0; i < _columnCount; i++)
            {
                var column = _columns[i];
                if (column == null)
                    throw new InvalidOperationException($"Archetype column {i} is null.");

                if (column.DebugCapacity < _entityCount)
                    throw new InvalidOperationException($"Archetype column {i} capacity {column.DebugCapacity} is below entity count {_entityCount}.");
            }

            for (int i = 0; i < _signatureIds.Length; i++)
            {
                int id = _signatureIds[i].Value;
                if ((uint)id >= (uint)_componentToColumn.Length)
                    throw new InvalidOperationException($"Signature component {id} is out of lookup range.");

                int col = _componentToColumn[id];
                if ((uint)col >= (uint)_columnCount)
                    throw new InvalidOperationException($"Signature component {id} maps to invalid column {col}.");
            }

            for (int id = 0; id < _componentToColumn.Length; id++)
            {
                int col = _componentToColumn[id];
                if (col >= 0 && (uint)col >= (uint)_columnCount)
                    throw new InvalidOperationException($"Lookup component {id} maps to invalid column {col}.");
            }
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetComponentSpanAt<T>(int columnIndex) where T : unmanaged, IComponent<T>
        {
            if ((uint)columnIndex < (uint)_columnCount && _columns[columnIndex] is ComponentColumn<T> typed)
                return typed.AsSpan(_entityCount);

            ThrowInvalidColumnIndex(columnIndex);
            return default;
        }

        internal void CopySlot(int sourceSlot, int destinationSlot)
        {
            if ((uint)sourceSlot >= (uint)_entityCount)
                ThrowOutOfRange(sourceSlot);
            if ((uint)destinationSlot >= (uint)_entityCount)
                ThrowOutOfRange(destinationSlot);
            if (sourceSlot == destinationSlot)
                return;

            for (int i = 0; i < _columnCount; i++)
                _columns[i].CopySlot(sourceSlot, destinationSlot);
        }

        internal void CopySlotRange(int sourceSlot, int destinationStartSlot, int count)
        {
            if (count <= 0)
                return;

            if ((uint)sourceSlot >= (uint)_entityCount)
                ThrowOutOfRange(sourceSlot);

            if ((uint)destinationStartSlot >= (uint)_entityCount)
                ThrowOutOfRange(destinationStartSlot);

            int destinationEnd = destinationStartSlot + count;
            if ((uint)destinationEnd > (uint)_entityCount)
                ThrowOutOfRange(destinationEnd - 1);

            for (int i = 0; i < _columnCount; i++)
                _columns[i].CopySlotRange(sourceSlot, destinationStartSlot, count);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidColumnIndex(int idx)
            => throw new InvalidOperationException($"Component column index {idx} is invalid or wrong type.");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureEntityCapacity(int needed)
        {
            if (needed <= _entities.Length)
                return;
            GrowEntityArray(needed);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowEntityArray(int needed)
        {
            int newSize = Math.Max(_entities.Length * 2, Math.Max(needed, _entities.Length + MinGrowth));
            var newArray = new Entity[newSize];
            _entities.AsSpan(0, _entityCount).CopyTo(newArray);
            _entities = newArray;
        }
    }

    internal interface IComponentColumn
    {
        void EnsureCapacity(int capacity);
        void SetDefaultAt(int slot);
        void SwapLastIntoSlot(int slot, int last);
        void CopySlot(int source, int destination);
        void CopySlotRange(int source, int destinationStart, int count);
        void CopyFrom(IComponentColumn source, int sourceSlot, int destinationSlot);
#if DEBUG
        int DebugCapacity { get; }
#endif
    }

    internal sealed class ComponentColumn<T> : IComponentColumn where T : unmanaged, IComponent<T>
    {
        private T[] _data;

        public ComponentColumn(int capacity)
        {
            _data = capacity > 0 ? new T[capacity] : Array.Empty<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDefaultAt(int slot)
        {
            _data[slot] = default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EnsureCapacity(int capacity)
        {
            if (capacity <= _data.Length)
                return;

            int newSize = _data.Length == 0
                ? Math.Max(64, capacity)
                : Math.Max(_data.Length * 2, capacity);

            var newArray = new T[newSize];
            _data.AsSpan().CopyTo(newArray);
            _data = newArray;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SwapLastIntoSlot(int slot, int last)
        {
            if (slot != last)
                _data[slot] = _data[last];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopySlot(int source, int destination)
        {
            _data[destination] = _data[source];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopySlotRange(int source, int destinationStart, int count)
        {
            _data.AsSpan(destinationStart, count).Fill(_data[source]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyFrom(IComponentColumn source, int sourceSlot, int destinationSlot)
        {
            _data[destinationSlot] = ((ComponentColumn<T>)source)._data[sourceSlot];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> AsSpan(int count) => _data.AsSpan(0, count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(int index) => ref _data[index];

#if DEBUG
        public int DebugCapacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data.Length;
        }
#endif
    }
}
