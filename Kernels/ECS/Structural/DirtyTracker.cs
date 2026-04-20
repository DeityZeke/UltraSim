using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

internal sealed class DirtyTracker
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(Entity entity)
    {
        if (!_captureEnabled)
            return false;

        if (_captureWorkerCount <= 0)
        {
            if (Environment.CurrentManagedThreadId == _serialCaptureThreadId)
            {
                MarkNoLock(entity);
                return true;
            }

            return false;
        }

        if (!WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
            return false;

        LocalDirtyState[] states = _localStates;
        if ((uint)workerIndex >= (uint)states.Length)
            return false;

        states[workerIndex].Mark(entity);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCapturedUnique(ReadOnlySpan<Entity> entities)
    {
        if (!_captureEnabled)
            return false;

        if (_captureWorkerCount <= 0)
        {
            if (Environment.CurrentManagedThreadId == _serialCaptureThreadId)
            {
                MarkUniqueNoLock(entities);
                return true;
            }

            return false;
        }

        if (!WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
            return false;

        LocalDirtyState[] states = _localStates;
        if ((uint)workerIndex >= (uint)states.Length)
            return false;

        states[workerIndex].MarkUnique(entities);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(ComponentId componentId, Entity entity)
    {
        if (!_captureEnabled)
            return false;

        if (_captureWorkerCount <= 0)
        {
            if (Environment.CurrentManagedThreadId == _serialCaptureThreadId)
            {
                MarkNoLock(componentId, entity);
                return true;
            }

            return false;
        }

        if (!WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
            return false;

        LocalDirtyState[] states = _localStates;
        if ((uint)workerIndex >= (uint)states.Length)
            return false;

        states[workerIndex].Mark(componentId, entity);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCaptured(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        if (!_captureEnabled)
            return false;

        if (_captureWorkerCount <= 0)
        {
            if (Environment.CurrentManagedThreadId == _serialCaptureThreadId)
            {
                MarkNoLock(componentId, entities);
                return true;
            }

            return false;
        }

        if (!WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
            return false;

        LocalDirtyState[] states = _localStates;
        if ((uint)workerIndex >= (uint)states.Length)
            return false;

        states[workerIndex].Mark(componentId, entities);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMarkCapturedUnique(ComponentId componentId, ReadOnlySpan<Entity> entities)
    {
        if (!_captureEnabled)
            return false;

        if (_captureWorkerCount <= 0)
        {
            if (Environment.CurrentManagedThreadId == _serialCaptureThreadId)
            {
                MarkUniqueNoLock(componentId, entities);
                return true;
            }

            return false;
        }

        if (!WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
            return false;

        LocalDirtyState[] states = _localStates;
        if ((uint)workerIndex >= (uint)states.Length)
            return false;

        states[workerIndex].MarkUnique(componentId, entities);
        return true;
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

            DirtySet?[] byComponent = state.ByComponent;
            for (int c = 0; c < byComponent.Length; c++)
            {
                DirtySet? localSet = byComponent[c];
                if (localSet is null || localSet.Count == 0)
                    continue;

                EnsureComponentCapacity(c + 1);
                _byComponent[c] ??= new DirtySet(DefaultEntityCapacity);
                DirtySet globalSet = _byComponent[c]!;
                ReadOnlySpan<Entity> localEntities = localSet.EntitiesSpan;
                for (int e = 0; e < localEntities.Length; e++)
                    globalSet.Mark(localEntities[e]);
            }

            state.Clear();
        }
    }

    private sealed class LocalDirtyState
    {
        public readonly DirtySet All;
        public DirtySet?[] ByComponent;

        public LocalDirtyState(int capacity)
        {
            All = new DirtySet(capacity);
            ByComponent = new DirtySet?[32];
        }

        public bool HasPending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (All.Count > 0)
                    return true;

                for (int i = 0; i < ByComponent.Length; i++)
                {
                    DirtySet? set = ByComponent[i];
                    if (set is not null && set.Count > 0)
                        return true;
                }

                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Mark(Entity entity) => All.Mark(entity);

        public void MarkUnique(ReadOnlySpan<Entity> entities)
        {
            All.MarkKnownUniqueRange(entities);
        }

        public void Mark(ComponentId componentId, Entity entity)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            All.Mark(entity);
            EnsureComponentCapacity(index + 1);
            ByComponent[index] ??= new DirtySet(DefaultEntityCapacity);
            ByComponent[index]!.Mark(entity);
        }

        public void Mark(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            EnsureComponentCapacity(index + 1);
            ByComponent[index] ??= new DirtySet(DefaultEntityCapacity);
            DirtySet componentSet = ByComponent[index]!;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                All.Mark(entity);
                componentSet.Mark(entity);
            }
        }

        public void MarkUnique(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            int index = componentId.Value;
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(componentId), "ComponentId.Invalid is not allowed.");

            EnsureComponentCapacity(index + 1);
            ByComponent[index] ??= new DirtySet(DefaultEntityCapacity);
            DirtySet componentSet = ByComponent[index]!;
            All.MarkKnownUniqueRange(entities);
            componentSet.MarkKnownUniqueRange(entities);
        }

        public void Clear()
        {
            All.Clear();
            for (int i = 0; i < ByComponent.Length; i++)
                ByComponent[i]?.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureComponentCapacity(int needed)
        {
            if (needed <= ByComponent.Length)
                return;

            int newSize = Math.Max(ByComponent.Length * 2, needed);
            Array.Resize(ref ByComponent, newSize);
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

        public void Mark(Entity entity)
        {
            int index = (int)entity.Index;
            EnsureIndexCapacity(index + 1);

            ref DirtyEntry entry = ref _entriesByIndex[index];
            if (entry.Epoch == _epoch && entry.Packed == entity.Packed)
                return;

            EnsureEntityCapacity(_count + 1);
            entry.Epoch = _epoch;
            entry.Packed = entity.Packed;
            _entities[_count++] = entity;
        }

        public void MarkKnownUniqueRange(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0)
                return;

            EnsureEntityCapacity(_count + entities.Length);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                int index = (int)entity.Index;
                EnsureIndexCapacity(index + 1);

                ref DirtyEntry entry = ref _entriesByIndex[index];
                entry.Epoch = _epoch;
                entry.Packed = entity.Packed;
                _entities[_count++] = entity;
            }
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
