using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace UltraSim.ECS;

internal sealed partial class DirtyTracker
{
    private const int DefaultEntityCapacity = 1024;

    private readonly object _sync = new();
    private readonly DirtySet _all = new(DefaultEntityCapacity);
    private DirtySet?[] _byComponent = new DirtySet[32];
    private LocalDirtyState[] _localStates = Array.Empty<LocalDirtyState>();
    private volatile bool _captureEnabled;
    private int _captureWorkerCount;
    private int _serialCaptureThreadId;

    public void Mark(Entity entity)
    {
        if (TryMarkCaptured(entity))
            return;

        lock (_sync)
            MarkNoLock(entity);
    }

    public void Mark(ComponentId componentId, Entity entity)
    {
        if (TryMarkCaptured(componentId, entity))
            return;

        lock (_sync)
            MarkNoLock(componentId, entity);
    }

    public void Mark(ReadOnlySpan<Entity> entities)
    {
        if (entities.Length == 0)
            return;

        if (TryMarkCapturedUnique(entities))
            return;

        lock (_sync)
        {
            for (int i = 0; i < entities.Length; i++)
                _all.Mark(entities[i]);
        }
    }

    public void MarkUnique(ReadOnlySpan<Entity> entities)
    {
        if (entities.Length == 0)
            return;

        if (TryMarkCapturedUnique(entities))
            return;

        lock (_sync)
            MarkUniqueNoLock(entities);
    }

    public void Mark(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        if (entities.Length == 0)
            return;

        if (TryMarkCaptured(componentId, entities))
            return;

        lock (_sync)
            MarkNoLock(componentId, entities);
    }

    public void Mark(ComponentId componentId, ReadOnlySpan<Entity> entities, ReadOnlySpan<int> dirtyIndices)
    {
        if (dirtyIndices.Length == 0)
            return;

        if (TryMarkCaptured(componentId, entities, dirtyIndices))
            return;

        lock (_sync)
            MarkNoLock(componentId, entities, dirtyIndices);
    }

    public void MarkUnique(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        if (entities.Length == 0)
            return;

        if (TryMarkCapturedUnique(componentId, entities))
            return;

        lock (_sync)
            MarkUniqueNoLock(componentId, entities);
    }

    public int CopyAllTo(Span<Entity> destination)
    {
        lock (_sync)
            return _all.CopyTo(destination);
    }

    public int CopyAndClearAllTo(Span<Entity> destination)
    {
        lock (_sync)
            return _all.CopyAndClearTo(destination);
    }

    public int CopyComponentTo(ComponentId componentId, Span<Entity> destination)
    {
        int index = componentId.Value;
        if ((uint)index >= (uint)_byComponent.Length || _byComponent[index] is null)
            return 0;

        lock (_sync)
        {
            DirtySet? set = _byComponent[index];
            return set is null ? 0 : set.CopyTo(destination);
        }
    }

    public int CopyAndClearComponentTo(ComponentId componentId, Span<Entity> destination)
    {
        int index = componentId.Value;
        if ((uint)index >= (uint)_byComponent.Length || _byComponent[index] is null)
            return 0;

        lock (_sync)
        {
            DirtySet? set = _byComponent[index];
            return set is null ? 0 : set.CopyAndClearTo(destination);
        }
    }

    public int CountComponent(ComponentId componentId)
    {
        int index = componentId.Value;
        if ((uint)index >= (uint)_byComponent.Length || _byComponent[index] is null)
            return 0;

        lock (_sync)
        {
            DirtySet? set = _byComponent[index];
            return set is null ? 0 : set.Count;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _all.Clear();
            for (int i = 0; i < _byComponent.Length; i++)
                _byComponent[i]?.Clear();
        }
    }

    internal void BeginCapture(int workerCount)
    {
        lock (_sync)
        {
            if (workerCount > 0)
                EnsureLocalStateCapacity(workerCount);

            _captureWorkerCount = Math.Max(0, workerCount);
            _serialCaptureThreadId = Environment.CurrentManagedThreadId;
            _captureEnabled = true;
        }
    }

    internal void EndCapture()
    {
        if (!_captureEnabled)
            return;

        lock (_sync)
        {
            if (_captureWorkerCount > 0)
                MergeLocalNoLock();

            _captureWorkerCount = 0;
            _serialCaptureThreadId = 0;
            _captureEnabled = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkNoLock(Entity entity) => _all.Mark(entity);

    private void MarkUniqueNoLock(ReadOnlySpan<Entity> entities)
    {
        _all.MarkKnownUniqueRange(entities);
    }

    private void MarkNoLock(ComponentId componentId, Entity entity)
    {
        int index = componentId.Value;
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

        _all.Mark(entity);
        EnsureComponentCapacity(index + 1);
        _byComponent[index] ??= new DirtySet(DefaultEntityCapacity);
        _byComponent[index]!.Mark(entity);
    }

    private void MarkNoLock(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        int index = componentId.Value;
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

        EnsureComponentCapacity(index + 1);
        _byComponent[index] ??= new DirtySet(DefaultEntityCapacity);
        DirtySet componentSet = _byComponent[index]!;

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            _all.Mark(entity);
            componentSet.Mark(entity);
        }
    }

    private void MarkUniqueNoLock(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        int index = componentId.Value;
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

        EnsureComponentCapacity(index + 1);
        _byComponent[index] ??= new DirtySet(DefaultEntityCapacity);
        DirtySet componentSet = _byComponent[index]!;
        _all.MarkKnownUniqueRange(entities);
        componentSet.MarkKnownUniqueRange(entities);
    }

    private void MarkNoLock(ComponentId componentId, ReadOnlySpan<Entity> entities, ReadOnlySpan<int> dirtyIndices)
    {
        int index = componentId.Value;
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

        EnsureComponentCapacity(index + 1);
        _byComponent[index] ??= new DirtySet(DefaultEntityCapacity);
        DirtySet componentSet = _byComponent[index]!;

        for (int i = 0; i < dirtyIndices.Length; i++)
        {
            int dirtyIndex = dirtyIndices[i];
            if ((uint)dirtyIndex >= (uint)entities.Length)
                throw new ArgumentException("Dirty index is outside the entity span.", nameof(dirtyIndices));

            Entity entity = entities[dirtyIndex];
            _all.Mark(entity);
            componentSet.Mark(entity);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(Entity entity)
    {
        if (!_captureEnabled)
            return false;

        if (!TryGetCaptureState(out LocalDirtyState? state))
            return false;

        state.Mark(entity);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCapturedUnique(ReadOnlySpan<Entity> entities)
    {
        if (!_captureEnabled)
            return false;

        if (!TryGetCaptureState(out LocalDirtyState? state))
            return false;

        state.MarkUnique(entities);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(ComponentId componentId, Entity entity)
    {
        if (!_captureEnabled)
            return false;

        if (!TryGetCaptureState(out LocalDirtyState? state))
            return false;

        state.Mark(componentId, entity);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        if (!_captureEnabled)
            return false;

        if (!TryGetCaptureState(out LocalDirtyState? state))
            return false;

        state.Mark(componentId, entities);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCapturedUnique(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        if (!_captureEnabled)
            return false;

        if (!TryGetCaptureState(out LocalDirtyState? state))
            return false;

        state.MarkUnique(componentId, entities);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(ComponentId componentId, ReadOnlySpan<Entity> entities, ReadOnlySpan<int> dirtyIndices)
    {
        if (!_captureEnabled)
            return false;

        if (!TryGetCaptureState(out LocalDirtyState? state))
            return false;

        state.Mark(componentId, entities, dirtyIndices);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetCaptureState([NotNullWhen(true)] out LocalDirtyState? state)
    {
        state = null;
        if (_captureWorkerCount <= 0)
            return false;

        LocalDirtyState[] states = _localStates;
        if (WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
        {
            if ((uint)workerIndex < (uint)states.Length)
            {
                state = states[workerIndex];
                return true;
            }
        }

        if (Environment.CurrentManagedThreadId == _serialCaptureThreadId && states.Length > 0)
        {
            int serialStateIndex = Math.Min(_captureWorkerCount - 1, states.Length - 1);
            state = states[serialStateIndex];
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureComponentCapacity(int needed)
    {
        if (needed <= _byComponent.Length)
            return;

        int newSize = Math.Max(_byComponent.Length * 2, needed);
        Array.Resize(ref _byComponent, newSize);
    }

    private void EnsureLocalStateCapacity(int needed)
    {
        if (needed <= _localStates.Length)
            return;

        int oldLength = _localStates.Length;
        int newSize = Math.Max(oldLength == 0 ? 4 : oldLength * 2, needed);
        Array.Resize(ref _localStates, newSize);
        for (int i = oldLength; i < newSize; i++)
            _localStates[i] = new LocalDirtyState(DefaultEntityCapacity);
    }

    private void MergeLocalNoLock()
    {
        LocalDirtyState[] states = _localStates;
        for (int i = 0; i < states.Length; i++)
        {
            LocalDirtyState? state = states[i];
            if (state is null || !state.HasPending)
                continue;

            ReadOnlySpan<Entity> allEntities = state.All.EntitiesSpan;
            for (int e = 0; e < allEntities.Length; e++)
                _all.Mark(allEntities[e]);

            ReadOnlySpan<int> touched = state.TouchedComponents;
            EntityBuffer?[] byComponent = state.ByComponent;
            for (int c = 0; c < touched.Length; c++)
            {
                int componentIndex = touched[c];
                EntityBuffer? localSet = byComponent[componentIndex];
                if (localSet is null || localSet.Count == 0)
                    continue;

                EnsureComponentCapacity(componentIndex + 1);
                _byComponent[componentIndex] ??= new DirtySet(DefaultEntityCapacity);
                DirtySet globalSet = _byComponent[componentIndex]!;
                ReadOnlySpan<Entity> localEntities = localSet.EntitiesSpan;
                for (int e = 0; e < localEntities.Length; e++)
                    globalSet.Mark(localEntities[e]);
            }

            state.Clear();
        }
    }

    private sealed class LocalDirtyState
    {
        public readonly EntityBuffer All;
        public EntityBuffer?[] ByComponent;
        private int[] _touchedComponents;
        private int _touchedCount;
        private bool _hasPending;

        public LocalDirtyState(int capacity)
        {
            All = new EntityBuffer(capacity);
            ByComponent = new EntityBuffer?[32];
            _touchedComponents = new int[8];
            _touchedCount = 0;
            _hasPending = false;
        }

        public bool HasPending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _hasPending;
        }

        public ReadOnlySpan<int> TouchedComponents
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _touchedComponents.AsSpan(0, _touchedCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Mark(Entity entity)
        {
            All.Append(entity);
            _hasPending = true;
        }

        public void MarkUnique(ReadOnlySpan<Entity> entities)
        {
            All.AppendRange(entities);
            _hasPending = true;
        }

        public void Mark(ComponentId componentId, Entity entity)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            EnsureComponentCapacity(index + 1);
            EntityBuffer? set = ByComponent[index];
            if (set is null)
            {
                set = new EntityBuffer(DefaultEntityCapacity);
                ByComponent[index] = set;
            }

            if (set.Count == 0)
                TrackTouchedComponent(index);

            All.Append(entity);
            set.Append(entity);
            _hasPending = true;
        }

        public void Mark(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            EnsureComponentCapacity(index + 1);
            EntityBuffer? componentSet = ByComponent[index];
            if (componentSet is null)
            {
                componentSet = new EntityBuffer(DefaultEntityCapacity);
                ByComponent[index] = componentSet;
            }

            if (componentSet.Count == 0)
                TrackTouchedComponent(index);

            All.AppendRange(entities);
            componentSet.AppendRange(entities);

            _hasPending = true;
        }

        public void MarkUnique(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            EnsureComponentCapacity(index + 1);
            EntityBuffer? componentSet = ByComponent[index];
            if (componentSet is null)
            {
                componentSet = new EntityBuffer(DefaultEntityCapacity);
                ByComponent[index] = componentSet;
            }

            if (componentSet.Count == 0)
                TrackTouchedComponent(index);

            All.AppendRange(entities);
            componentSet.AppendRange(entities);
            _hasPending = true;
        }

        public void Mark(ComponentId componentId, ReadOnlySpan<Entity> entities, ReadOnlySpan<int> dirtyIndices)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            EnsureComponentCapacity(index + 1);
            EntityBuffer? componentSet = ByComponent[index];
            if (componentSet is null)
            {
                componentSet = new EntityBuffer(DefaultEntityCapacity);
                ByComponent[index] = componentSet;
            }

            if (componentSet.Count == 0)
                TrackTouchedComponent(index);

            for (int i = 0; i < dirtyIndices.Length; i++)
            {
                int dirtyIndex = dirtyIndices[i];
                if ((uint)dirtyIndex >= (uint)entities.Length)
                    throw new ArgumentException("Dirty index is outside the entity span.", nameof(dirtyIndices));

                Entity entity = entities[dirtyIndex];
                All.Append(entity);
                componentSet.Append(entity);
            }

            _hasPending = true;
        }

        public void Clear()
        {
            All.Clear();
            for (int i = 0; i < _touchedCount; i++)
                ByComponent[_touchedComponents[i]]?.Clear();

            _touchedCount = 0;
            _hasPending = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureComponentCapacity(int needed)
        {
            if (needed <= ByComponent.Length)
                return;

            int newSize = Math.Max(ByComponent.Length * 2, needed);
            Array.Resize(ref ByComponent, newSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrackTouchedComponent(int index)
        {
            if (_touchedCount == _touchedComponents.Length)
                Array.Resize(ref _touchedComponents, _touchedComponents.Length * 2);

            _touchedComponents[_touchedCount++] = index;
        }
    }

    private sealed class EntityBuffer
    {
        private Entity[] _entities;
        private int _count;

        public EntityBuffer(int capacity)
        {
            _entities = new Entity[capacity];
            _count = 0;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public ReadOnlySpan<Entity> EntitiesSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities.AsSpan(0, _count);
        }

        internal int EntityCapacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(Entity entity)
        {
            EnsureCapacity(_count + 1);
            _entities[_count++] = entity;
        }

        public void AppendRange(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0)
                return;

            EnsureCapacity(_count + entities.Length);
            entities.CopyTo(_entities.AsSpan(_count));
            _count += entities.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => _count = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int needed)
        {
            if (needed <= _entities.Length)
                return;

            int newSize = Math.Max(_entities.Length * 2, needed);
            Array.Resize(ref _entities, newSize);
        }
    }

    private sealed class DirtySet
    {
        private struct DirtyEntry
        {
            public int Epoch;
            public ulong Packed;
        }

        private Entity[] _entities;
        private DirtyEntry[] _entriesByIndex;
        private int _count;
        private int _epoch;

        public DirtySet(int capacity)
        {
            _entities = new Entity[capacity];
            _entriesByIndex = new DirtyEntry[capacity];
            _count = 0;
            _epoch = 1;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public ReadOnlySpan<Entity> EntitiesSpan
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities.AsSpan(0, _count);
        }

        internal int EntityCapacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities.Length;
        }

        internal int IndexCapacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entriesByIndex.Length;
        }

        public void Mark(Entity entity)
        {
            int index = (int)entity.Index;
            EnsureIndexCapacity(index + 1);
            EnsureEntityCapacity(_count + 1);

            ref DirtyEntry entry = ref _entriesByIndex[index];
            if (entry.Epoch == _epoch && entry.Packed == entity.Packed)
                return;

            entry.Epoch = _epoch;
            entry.Packed = entity.Packed;
            _entities[_count++] = entity;
        }

        public void MarkKnownUniqueRange(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0)
                return;

            int maxIndex = -1;
            for (int i = 0; i < entities.Length; i++)
            {
                int index = (int)entities[i].Index;
                if (index > maxIndex)
                    maxIndex = index;
            }

            EnsureEntityCapacity(_count + entities.Length);
            EnsureIndexCapacity(maxIndex + 1);

            DirtyEntry[] entriesByIndex = _entriesByIndex;
            Entity[] dirtyEntities = _entities;
            int epoch = _epoch;
            int count = _count;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                int index = (int)entity.Index;

                ref DirtyEntry entry = ref entriesByIndex[index];
                if (entry.Epoch == epoch && entry.Packed == entity.Packed)
                    continue;

                entry.Epoch = epoch;
                entry.Packed = entity.Packed;
                dirtyEntities[count++] = entity;
            }

            _count = count;
        }

        public int CopyTo(Span<Entity> destination)
        {
            int written = Math.Min(_count, destination.Length);
            _entities.AsSpan(0, written).CopyTo(destination);
            return written;
        }

        public int CopyAndClearTo(Span<Entity> destination)
        {
            int written = CopyTo(destination);
            Clear();
            return written;
        }

        public void Clear()
        {
            AdvanceEpoch();
            _count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureEntityCapacity(int needed)
        {
            if (needed <= _entities.Length)
                return;

            int newSize = Math.Max(_entities.Length * 2, needed);
            Array.Resize(ref _entities, newSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureIndexCapacity(int needed)
        {
            if (needed <= _entriesByIndex.Length)
                return;

            int newSize = Math.Max(_entriesByIndex.Length * 2, needed);
            Array.Resize(ref _entriesByIndex, newSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceEpoch()
        {
            if (_epoch == int.MaxValue)
            {
                Array.Clear(_entriesByIndex, 0, _entriesByIndex.Length);
                _epoch = 1;
                return;
            }

            _epoch++;
        }
    }
}
