using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public delegate void PrefabApply(Archetype archetype, int slot);

    internal sealed class StructuralQueue
    {
        private const int SmallGroupThreshold = 8;
        private readonly object _sync = new();

        private readonly QueueBuffer<ArchetypeSignature> _createDefaultsWrite = new(64);
        private readonly QueueBuffer<ArchetypeSignature> _createDefaultsRead = new(64);

        private readonly QueueBuffer<PrefabSpawnRequest> _directSpawnsWrite = new(64);
        private readonly QueueBuffer<PrefabSpawnRequest> _directSpawnsRead = new(64);

        private readonly QueueBuffer<PrefabId> _prefabSpawnsWrite = new(64);
        private readonly QueueBuffer<PrefabId> _prefabSpawnsRead = new(64);

        private readonly QueueBuffer<Entity> _destroysWrite = new(64);
        private readonly QueueBuffer<Entity> _destroysRead = new(64);

        private LocalQueueState[] _localStates = Array.Empty<LocalQueueState>();
        private volatile bool _localCaptureEnabled;

        private readonly Dictionary<Archetype, int> _reserveCounts = new(64);
        private readonly Dictionary<Archetype, int> _stableGroupIndices = new(64);
        private readonly Dictionary<ColumnCopyMapKey, ColumnCopyMap> _columnCopyMapCache = new(128);
        private Archetype?[] _stableGroupArchetypes = new Archetype?[64];
        private int[] _stableGroupCounts = new int[64];
        private readonly Archetype?[] _smallGroupArchetypes = new Archetype?[SmallGroupThreshold];
        private readonly int[] _smallGroupCounts = new int[SmallGroupThreshold];
        private IComponentAddQueue?[] _addQueues = new IComponentAddQueue?[32];
        private IComponentRemoveQueue?[] _removeQueues = new IComponentRemoveQueue?[32];
        private int _maxAddIndex;
        private int _maxRemoveIndex;
        private int _addsCount;
        private int _removesCount;

        internal int CreateDefaultCount
        {
            get
            {
                lock (_sync)
                    return _createDefaultsWrite.Count;
            }
        }

        internal int SpawnCount
        {
            get
            {
                lock (_sync)
                    return _directSpawnsWrite.Count + _prefabSpawnsWrite.Count;
            }
        }

        internal int DestroyCount
        {
            get
            {
                lock (_sync)
                    return _destroysWrite.Count;
            }
        }

        internal int AddCount
        {
            get
            {
                lock (_sync)
                    return _addsCount;
            }
        }

        internal int RemoveCount
        {
            get
            {
                lock (_sync)
                    return _removesCount;
            }
        }

        internal bool HasPending
        {
            get
            {
                lock (_sync)
                {
                    return _createDefaultsWrite.Count > 0
                        || _directSpawnsWrite.Count > 0
                        || _prefabSpawnsWrite.Count > 0
                        || _destroysWrite.Count > 0
                        || _addsCount > 0
                        || _removesCount > 0
                        || _localCaptureEnabled;
                }
            }
        }

        internal void BeginLocalCapture(int workerCount)
        {
            if (workerCount <= 0)
                return;

            lock (_sync)
            {
                EnsureLocalStateCapacity(workerCount);
                _localCaptureEnabled = true;
            }
        }

        internal void EndLocalCapture()
        {
            if (!_localCaptureEnabled)
                return;

            lock (_sync)
            {
                _localCaptureEnabled = false;
                MergeLocalWritesNoLock();
            }
        }

        internal void EnqueueCreateDefault(ArchetypeSignature signature)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));

            if (TryGetLocalState(out var local))
            {
                local.CreateDefaults.Add(signature);
                return;
            }

            lock (_sync)
                _createDefaultsWrite.Add(signature);
        }

        internal void EnqueueCreateDefaultRange(ReadOnlySpan<ArchetypeSignature> signatures)
        {
            if (signatures.Length == 0)
                return;

            for (int i = 0; i < signatures.Length; i++)
            {
                if (signatures[i] == null)
                    throw new ArgumentNullException(nameof(signatures), "Null archetype signature is not allowed.");
            }

            if (TryGetLocalState(out var local))
            {
                local.CreateDefaults.Append(signatures);
                return;
            }

            lock (_sync)
                _createDefaultsWrite.Append(signatures);
        }

        internal void EnqueueSpawn(ArchetypeSignature signature, PrefabApply apply)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));

            if (TryGetLocalState(out var local))
            {
                local.DirectSpawns.Add(new PrefabSpawnRequest(signature, apply));
                return;
            }

            lock (_sync)
                _directSpawnsWrite.Add(new PrefabSpawnRequest(signature, apply));
        }

        internal void EnqueueSpawnRepeated(ArchetypeSignature signature, PrefabApply apply, int count)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (count <= 0)
                return;

            var request = new PrefabSpawnRequest(signature, apply);
            if (TryGetLocalState(out var local))
            {
                local.DirectSpawns.AppendRepeated(request, count);
                return;
            }

            lock (_sync)
                _directSpawnsWrite.AppendRepeated(request, count);
        }

        internal void EnqueueSpawnTemplate(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyTemplate == null)
                throw new ArgumentNullException(nameof(applyTemplate));

            if (TryGetLocalState(out var local))
            {
                local.DirectSpawns.Add(new PrefabSpawnRequest(signature, applyTemplate, applyPerInstance, hasPerInstanceData));
                return;
            }

            lock (_sync)
                _directSpawnsWrite.Add(new PrefabSpawnRequest(signature, applyTemplate, applyPerInstance, hasPerInstanceData));
        }

        internal void EnqueueSpawnTemplateRepeated(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData, int count)
        {
            if (signature == null)
                throw new ArgumentNullException(nameof(signature));
            if (applyTemplate == null)
                throw new ArgumentNullException(nameof(applyTemplate));
            if (count <= 0)
                return;

            var request = new PrefabSpawnRequest(signature, applyTemplate, applyPerInstance, hasPerInstanceData);
            if (TryGetLocalState(out var local))
            {
                local.DirectSpawns.AppendRepeated(request, count);
                return;
            }

            lock (_sync)
                _directSpawnsWrite.AppendRepeated(request, count);
        }

        internal void EnqueueSpawn(PrefabId prefabId)
        {
            if (prefabId == PrefabId.Invalid)
                throw new ArgumentOutOfRangeException(nameof(prefabId), "PrefabId.Invalid is not allowed.");

            if (TryGetLocalState(out var local))
            {
                local.PrefabSpawns.Add(prefabId);
                return;
            }

            lock (_sync)
                _prefabSpawnsWrite.Add(prefabId);
        }

        internal void EnqueueSpawnRepeated(PrefabId prefabId, int count)
        {
            if (prefabId == PrefabId.Invalid)
                throw new ArgumentOutOfRangeException(nameof(prefabId), "PrefabId.Invalid is not allowed.");
            if (count <= 0)
                return;

            if (TryGetLocalState(out var local))
            {
                local.PrefabSpawns.AppendRepeated(prefabId, count);
                return;
            }

            lock (_sync)
                _prefabSpawnsWrite.AppendRepeated(prefabId, count);
        }

        internal void EnqueueSpawnRange(ReadOnlySpan<PrefabId> prefabIds)
        {
            if (prefabIds.Length == 0)
                return;

            for (int i = 0; i < prefabIds.Length; i++)
            {
                if (prefabIds[i] == PrefabId.Invalid)
                    throw new ArgumentOutOfRangeException(nameof(prefabIds), "PrefabId.Invalid is not allowed.");
            }

            if (TryGetLocalState(out var local))
            {
                local.PrefabSpawns.Append(prefabIds);
                return;
            }

            lock (_sync)
                _prefabSpawnsWrite.Append(prefabIds);
        }

        internal void EnqueueDestroy(Entity entity)
        {
            if (TryGetLocalState(out var local))
            {
                local.Destroys.Add(entity);
                return;
            }

            lock (_sync)
                _destroysWrite.Add(entity);
        }

        internal void EnqueueDestroyRange(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0)
                return;

            if (TryGetLocalState(out var local))
            {
                local.Destroys.Append(entities);
                return;
            }

            lock (_sync)
                _destroysWrite.Append(entities);
        }

        internal void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
        {
            var id = ComponentManager.GetId<T>();
            int index = id.Value;
            if (index < 0)
                throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

            if (TryGetLocalState(out var local))
            {
                local.EnqueueAdd(index, id, entity, value);
                return;
            }

            lock (_sync)
            {
                if (index >= _addQueues.Length)
                    Array.Resize(ref _addQueues, Math.Max(_addQueues.Length * 2, index + 1));

                var queue = _addQueues[index] as ComponentAddQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentAddQueue<T>(this, id);
                    _addQueues[index] = queue;
                }

                queue.Enqueue(entity, value);
                _addsCount++;

                if (index + 1 > _maxAddIndex)
                    _maxAddIndex = index + 1;
            }
        }

        internal void EnqueueAddRange<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
        {
            if (entities.Length == 0)
                return;
            if (entities.Length != values.Length)
                throw new ArgumentException("Entities and values length must match.");

            var id = ComponentManager.GetId<T>();
            int index = id.Value;
            if (index < 0)
                throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

            if (TryGetLocalState(out var local))
            {
                local.EnqueueAddRange(index, id, entities, values);
                return;
            }

            lock (_sync)
            {
                if (index >= _addQueues.Length)
                    Array.Resize(ref _addQueues, Math.Max(_addQueues.Length * 2, index + 1));

                var queue = _addQueues[index] as ComponentAddQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentAddQueue<T>(this, id);
                    _addQueues[index] = queue;
                }

                queue.EnqueueRange(entities, values);
                _addsCount += entities.Length;

                if (index + 1 > _maxAddIndex)
                    _maxAddIndex = index + 1;
            }
        }

        internal void EnqueueAddRepeated<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>
        {
            if (entities.Length == 0)
                return;

            var id = ComponentManager.GetId<T>();
            int index = id.Value;
            if (index < 0)
                throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

            if (TryGetLocalState(out var local))
            {
                local.EnqueueAddRepeated(index, id, entities, value);
                return;
            }

            lock (_sync)
            {
                if (index >= _addQueues.Length)
                    Array.Resize(ref _addQueues, Math.Max(_addQueues.Length * 2, index + 1));

                var queue = _addQueues[index] as ComponentAddQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentAddQueue<T>(this, id);
                    _addQueues[index] = queue;
                }

                queue.EnqueueRepeated(entities, value);
                _addsCount += entities.Length;

                if (index + 1 > _maxAddIndex)
                    _maxAddIndex = index + 1;
            }
        }

        internal void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
        {
            var id = ComponentManager.GetId<T>();
            int index = id.Value;
            if (index < 0)
                throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

            if (TryGetLocalState(out var local))
            {
                local.EnqueueRemove<T>(index, id, entity);
                return;
            }

            lock (_sync)
            {
                if (index >= _removeQueues.Length)
                    Array.Resize(ref _removeQueues, Math.Max(_removeQueues.Length * 2, index + 1));

                var queue = _removeQueues[index] as ComponentRemoveQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentRemoveQueue<T>(this, id);
                    _removeQueues[index] = queue;
                }

                queue.Enqueue(entity);
                _removesCount++;

                if (index + 1 > _maxRemoveIndex)
                    _maxRemoveIndex = index + 1;
            }
        }

        internal void EnqueueRemoveRange<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
        {
            if (entities.Length == 0)
                return;

            var id = ComponentManager.GetId<T>();
            int index = id.Value;
            if (index < 0)
                throw new InvalidOperationException("ComponentId.Invalid is not allowed.");

            if (TryGetLocalState(out var local))
            {
                local.EnqueueRemoveRange<T>(index, id, entities);
                return;
            }

            lock (_sync)
            {
                if (index >= _removeQueues.Length)
                    Array.Resize(ref _removeQueues, Math.Max(_removeQueues.Length * 2, index + 1));

                var queue = _removeQueues[index] as ComponentRemoveQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentRemoveQueue<T>(this, id);
                    _removeQueues[index] = queue;
                }

                queue.EnqueueRange(entities);
                _removesCount += entities.Length;

                if (index + 1 > _maxRemoveIndex)
                    _maxRemoveIndex = index + 1;
            }
        }

        internal void Clear()
        {
            lock (_sync)
            {
                _createDefaultsWrite.Clear();
                _createDefaultsRead.Clear();
                _directSpawnsWrite.Clear();
                _directSpawnsRead.Clear();
                _prefabSpawnsWrite.Clear();
                _prefabSpawnsRead.Clear();
                _destroysWrite.Clear();
                _destroysRead.Clear();

                for (int i = 0; i < _addQueues.Length; i++)
                    _addQueues[i]?.ClearAll();

                for (int i = 0; i < _removeQueues.Length; i++)
                    _removeQueues[i]?.ClearAll();

                _maxAddIndex = 0;
                _maxRemoveIndex = 0;
                _addsCount = 0;
                _removesCount = 0;
                _localCaptureEnabled = false;
                _columnCopyMapCache.Clear();

                for (int i = 0; i < _localStates.Length; i++)
                {
                    var local = _localStates[i];
                    if (local == null)
                        continue;

                    local.CreateDefaults.Clear();
                    local.DirectSpawns.Clear();
                    local.PrefabSpawns.Clear();
                    local.Destroys.Clear();
                    local.ResetIndices();

                    for (int a = 0; a < local.AddQueues.Length; a++)
                        local.AddQueues[a]?.ClearAll();

                    for (int r = 0; r < local.RemoveQueues.Length; r++)
                        local.RemoveQueues[r]?.ClearAll();
                }
            }
        }

        internal void Apply(
            World world,
            Span<Entity> createdEntities,
            out int createdEntityCount,
            out int createsApplied,
            out int spawnsApplied,
            out int destroysApplied,
            out int removesApplied,
            out int addsApplied)
        {
            createdEntityCount = 0;
            createsApplied = 0;
            spawnsApplied = 0;
            destroysApplied = 0;
            removesApplied = 0;
            addsApplied = 0;

            IComponentAddQueue[]? addWork = null;
            int addWorkCount = 0;
            IComponentRemoveQueue[]? removeWork = null;
            int removeWorkCount = 0;

            lock (_sync)
            {
                if (!_localCaptureEnabled)
                    MergeLocalWritesNoLock();

                if (_createDefaultsWrite.Count == 0
                    && _directSpawnsWrite.Count == 0
                    && _prefabSpawnsWrite.Count == 0
                    && _destroysWrite.Count == 0
                    && _addsCount == 0
                    && _removesCount == 0)
                {
                    return;
                }

                _createDefaultsWrite.SwapWith(_createDefaultsRead);
                _directSpawnsWrite.SwapWith(_directSpawnsRead);
                _prefabSpawnsWrite.SwapWith(_prefabSpawnsRead);
                _destroysWrite.SwapWith(_destroysRead);

                int addScan = _maxAddIndex;
                int removeScan = _maxRemoveIndex;
                _maxAddIndex = 0;
                _maxRemoveIndex = 0;
                _addsCount = 0;
                _removesCount = 0;

                if (removeScan > 0)
                {
                    removeWork = ArrayPool<IComponentRemoveQueue>.Shared.Rent(removeScan);
                    for (int i = 0; i < removeScan; i++)
                    {
                        var queue = _removeQueues[i];
                        if (queue != null && queue.SwapForApply())
                            removeWork[removeWorkCount++] = queue;
                    }
                }

                if (addScan > 0)
                {
                    addWork = ArrayPool<IComponentAddQueue>.Shared.Rent(addScan);
                    for (int i = 0; i < addScan; i++)
                    {
                        var queue = _addQueues[i];
                        if (queue != null && queue.SwapForApply())
                            addWork[addWorkCount++] = queue;
                    }
                }
            }

            try
            {
                if (world.DirtyTrackingEnabled)
                {
                    lock (world.DirtySyncRoot)
                    {
                        ApplyCore(world, createdEntities, true, ref createdEntityCount, ref createsApplied, ref spawnsApplied, ref destroysApplied, ref removesApplied, ref addsApplied, removeWork, removeWorkCount, addWork, addWorkCount);
                    }
                }
                else
                {
                    ApplyCore(world, createdEntities, false, ref createdEntityCount, ref createsApplied, ref spawnsApplied, ref destroysApplied, ref removesApplied, ref addsApplied, removeWork, removeWorkCount, addWork, addWorkCount);
                }
            }
            finally
            {
                _createDefaultsRead.Clear();
                _directSpawnsRead.Clear();
                _prefabSpawnsRead.Clear();
                _destroysRead.Clear();

                if (removeWork != null)
                {
                    for (int i = 0; i < removeWorkCount; i++)
                    {
                        removeWork[i].ClearRead();
                        removeWork[i] = null!;
                    }

                    ArrayPool<IComponentRemoveQueue>.Shared.Return(removeWork, clearArray: false);
                }

                if (addWork != null)
                {
                    for (int i = 0; i < addWorkCount; i++)
                    {
                        addWork[i].ClearRead();
                        addWork[i] = null!;
                    }

                    ArrayPool<IComponentAddQueue>.Shared.Return(addWork, clearArray: false);
                }
            }

            if (createdEntityCount > createdEntities.Length)
                createdEntityCount = createdEntities.Length;
        }

#if DEBUG
        internal void DebugValidatePostApply()
        {
            lock (_sync)
            {
                if (_createDefaultsRead.Count != 0
                    || _directSpawnsRead.Count != 0
                    || _prefabSpawnsRead.Count != 0
                    || _destroysRead.Count != 0)
                {
                    throw new InvalidOperationException("StructuralQueue read buffers are not empty after apply.");
                }

                if (_addsCount < 0 || _removesCount < 0)
                    throw new InvalidOperationException($"StructuralQueue counters invalid: adds={_addsCount}, removes={_removesCount}.");

                if (_maxAddIndex < 0 || _maxAddIndex > _addQueues.Length)
                    throw new InvalidOperationException($"StructuralQueue max add index invalid: {_maxAddIndex}.");

                if (_maxRemoveIndex < 0 || _maxRemoveIndex > _removeQueues.Length)
                    throw new InvalidOperationException($"StructuralQueue max remove index invalid: {_maxRemoveIndex}.");
            }
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetLocalState(out LocalQueueState local)
        {
            local = default!;

            if (!_localCaptureEnabled)
                return false;

            if (!WorkerThreadContext.TryGetWorkerIndex(out int workerIndex))
                return false;

            var states = _localStates;
            if ((uint)workerIndex >= (uint)states.Length)
                return false;

            local = states[workerIndex];
            return local != null;
        }

        private void EnsureLocalStateCapacity(int needed)
        {
            if (needed <= _localStates.Length)
                return;

            int oldLength = _localStates.Length;
            int newSize = Math.Max(oldLength == 0 ? 4 : oldLength * 2, needed);
            Array.Resize(ref _localStates, newSize);
            for (int i = oldLength; i < newSize; i++)
                _localStates[i] = new LocalQueueState(this);
        }

        private void MergeLocalWritesNoLock()
        {
            var states = _localStates;
            for (int s = 0; s < states.Length; s++)
            {
                var local = states[s];
                if (local == null || !local.HasPending)
                    continue;

                AppendQueue(local.CreateDefaults, _createDefaultsWrite);
                AppendQueue(local.DirectSpawns, _directSpawnsWrite);
                AppendQueue(local.PrefabSpawns, _prefabSpawnsWrite);
                AppendQueue(local.Destroys, _destroysWrite);

                if (local.MaxRemoveIndex > 0)
                {
                    if (local.MaxRemoveIndex > _removeQueues.Length)
                        Array.Resize(ref _removeQueues, Math.Max(_removeQueues.Length * 2, local.MaxRemoveIndex));

                    for (int i = 0; i < local.MaxRemoveIndex; i++)
                    {
                        var queue = local.RemoveQueues[i];
                        if (queue == null || queue.PendingWriteCount == 0)
                            continue;

                        int moved = queue.MoveWritesTo(ref _removeQueues[i]);
                        _removesCount += moved;
                    }

                    if (local.MaxRemoveIndex > _maxRemoveIndex)
                        _maxRemoveIndex = local.MaxRemoveIndex;
                }

                if (local.MaxAddIndex > 0)
                {
                    if (local.MaxAddIndex > _addQueues.Length)
                        Array.Resize(ref _addQueues, Math.Max(_addQueues.Length * 2, local.MaxAddIndex));

                    for (int i = 0; i < local.MaxAddIndex; i++)
                    {
                        var queue = local.AddQueues[i];
                        if (queue == null || queue.PendingWriteCount == 0)
                            continue;

                        int moved = queue.MoveWritesTo(ref _addQueues[i]);
                        _addsCount += moved;
                    }

                    if (local.MaxAddIndex > _maxAddIndex)
                        _maxAddIndex = local.MaxAddIndex;
                }

                local.ResetIndices();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendQueue<T>(QueueBuffer<T> source, QueueBuffer<T> destination)
        {
            if (source.Count == 0)
                return;

            destination.Append(source.AsSpan());
            source.Clear();
        }

        private void ApplyCore(
            World world,
            Span<Entity> createdEntities,
            bool markDirtyNoLock,
            ref int createdEntityCount,
            ref int createsApplied,
            ref int spawnsApplied,
            ref int destroysApplied,
            ref int removesApplied,
            ref int addsApplied,
            IComponentRemoveQueue[]? removeWork,
            int removeWorkCount,
            IComponentAddQueue[]? addWork,
            int addWorkCount)
        {
            // Destroy first.
            var destroySpan = _destroysRead.AsSpan();
            for (int i = 0; i < destroySpan.Length; i++)
            {
                var entity = destroySpan[i];
                if (!world.EntityStore.TryGetLocation(entity, out var archetype, out var slot))
                    continue;

                if (!archetype.RemoveAtSwap(slot, out var moved, out var movedSlot))
                    continue;

                if (movedSlot >= 0)
                    world.EntityStore.SetLocation(moved, archetype, movedSlot);

                world.EntityStore.Free(entity);
                destroysApplied++;
            }

            // Spawn with direct apply delegate.
            if (_directSpawnsRead.Count > 0)
            {
                var requests = _directSpawnsRead.AsSpan();
                var spawnArchetypes = ArrayPool<Archetype>.Shared.Rent(requests.Length);
                try
                {
                    for (int i = 0; i < requests.Length; i++)
                        spawnArchetypes[i] = world.Archetypes.GetOrCreate(requests[i].Signature);

                    SpawnDirectStableGrouped(world, requests, spawnArchetypes.AsSpan(0, requests.Length), createdEntities, markDirtyNoLock, ref createdEntityCount, ref spawnsApplied);
                }
                finally
                {
                    ArrayPool<Archetype>.Shared.Return(spawnArchetypes, clearArray: false);
                }
            }

            // Spawn via prefab registry.
            if (_prefabSpawnsRead.Count > 0)
            {
                var prefabIds = _prefabSpawnsRead.AsSpan();
                var prefabArchetypes = ArrayPool<Archetype>.Shared.Rent(prefabIds.Length);
                try
                {
                    for (int i = 0; i < prefabIds.Length; i++)
                    {
                        var entry = PrefabRegistry.Get(prefabIds[i]);
                        prefabArchetypes[i] = world.Archetypes.GetOrCreate(entry.Signature);
                    }

                    SpawnPrefabsStableGrouped(world, prefabIds, prefabArchetypes.AsSpan(0, prefabIds.Length), createdEntities, markDirtyNoLock, ref createdEntityCount, ref spawnsApplied);
                }
                finally
                {
                    ArrayPool<Archetype>.Shared.Return(prefabArchetypes, clearArray: false);
                }
            }

            // Create default rows.
            if (_createDefaultsRead.Count > 0)
            {
                var signatures = _createDefaultsRead.AsSpan();
                var createArchetypes = ArrayPool<Archetype>.Shared.Rent(signatures.Length);
                try
                {
                    for (int i = 0; i < signatures.Length; i++)
                        createArchetypes[i] = world.Archetypes.GetOrCreate(signatures[i]);

                    ReserveGrouped(createArchetypes, signatures.Length);

                    for (int i = 0; i < signatures.Length; i++)
                    {
                        var archetype = createArchetypes[i];
                        var entity = world.EntityStore.Allocate();
                        int slot = archetype.AddEntity(entity);
                        world.EntityStore.SetLocation(entity, archetype, slot);

                        if (markDirtyNoLock)
                            world.MarkDirtyNoLock(entity);
                        else
                            world.MarkDirty(entity);

                        RecordCreatedEntity(entity, createdEntities, ref createdEntityCount);
                        createsApplied++;
                    }
                }
                finally
                {
                    ArrayPool<Archetype>.Shared.Return(createArchetypes, clearArray: false);
                }
            }

            for (int i = 0; i < removeWorkCount; i++)
                removesApplied += removeWork![i].Apply(world, markDirtyNoLock);

            for (int i = 0; i < addWorkCount; i++)
                addsApplied += addWork![i].Apply(world, markDirtyNoLock);
        }

        private void SpawnDirectStableGrouped(
            World world,
            Span<PrefabSpawnRequest> requests,
            Span<Archetype> archetypes,
            Span<Entity> createdEntities,
            bool markDirtyNoLock,
            ref int createdEntityCount,
            ref int spawnsApplied)
        {
            int groupCount = BuildStableGroups(archetypes);
            if (groupCount == 0)
                return;

            var offsetsBuffer = ArrayPool<int>.Shared.Rent(groupCount);
            var cursorsBuffer = ArrayPool<int>.Shared.Rent(groupCount);
            var permutationBuffer = ArrayPool<int>.Shared.Rent(requests.Length);

            try
            {
                var offsets = offsetsBuffer.AsSpan(0, groupCount);
                var cursors = cursorsBuffer.AsSpan(0, groupCount);
                var permutation = permutationBuffer.AsSpan(0, requests.Length);

                int running = 0;
                for (int g = 0; g < groupCount; g++)
                {
                    var archetype = _stableGroupArchetypes[g]!;
                    int count = _stableGroupCounts[g];
                    archetype.Reserve(count);
                    offsets[g] = running;
                    cursors[g] = running;
                    running += count;
                }

                for (int i = 0; i < requests.Length; i++)
                {
                    int groupIndex = _stableGroupIndices[archetypes[i]];
                    permutation[cursors[groupIndex]++] = i;
                }

                for (int g = 0; g < groupCount; g++)
                {
                    var archetype = _stableGroupArchetypes[g]!;
                    int start = offsets[g];
                    int length = _stableGroupCounts[g];

                    var entitiesBuffer = ArrayPool<Entity>.Shared.Rent(length);
                    try
                    {
                        var entities = entitiesBuffer.AsSpan(0, length);
                        int allocated = world.EntityStore.AllocateBulk(entities);
                        int startSlot = archetype.AddEntitiesUninitialized(entities.Slice(0, allocated));
                        world.EntityStore.SetLocationsBulkAssumeValid(entities.Slice(0, allocated), archetype, startSlot);

                        int j = 0;
                        while (j < allocated)
                        {
                            int requestIndex = permutation[start + j];
                            ref readonly var request = ref requests[requestIndex];

                            if (!request.EnableTemplateCopy)
                            {
                                var entity = entities[j];
                                int slot = startSlot + j;
                                request.Apply(archetype, slot);
                                if (request.HasPerInstanceData)
                                    request.ApplyPerInstance!(archetype, slot);

                                RecordCreatedEntity(entity, createdEntities, ref createdEntityCount);
                                spawnsApplied++;
                                j++;
                                continue;
                            }

                            int runStart = j;
                            int runEndExclusive = runStart + 1;
                            while (runEndExclusive < allocated)
                            {
                                int nextRequestIndex = permutation[start + runEndExclusive];
                                ref readonly var next = ref requests[nextRequestIndex];
                                if (!next.EnableTemplateCopy
                                    || !ReferenceEquals(next.Apply, request.Apply)
                                    || !ReferenceEquals(next.ApplyPerInstance, request.ApplyPerInstance)
                                    || next.HasPerInstanceData != request.HasPerInstanceData)
                                {
                                    break;
                                }

                                runEndExclusive++;
                            }

                            int firstSlot = startSlot + runStart;
                            request.Apply(archetype, firstSlot);
                            int runLength = runEndExclusive - runStart;
                            if (runLength > 1)
                                archetype.CopySlotRange(firstSlot, firstSlot + 1, runLength - 1);

                            if (request.HasPerInstanceData)
                            {
                                for (int k = runStart; k < runEndExclusive; k++)
                                    request.ApplyPerInstance!(archetype, startSlot + k);
                            }

                            for (int k = runStart; k < runEndExclusive; k++)
                            {
                                var entity = entities[k];
                                RecordCreatedEntity(entity, createdEntities, ref createdEntityCount);
                                spawnsApplied++;
                            }

                            j = runEndExclusive;
                        }

                        if (markDirtyNoLock && allocated > 0)
                            world.MarkDirtyUniqueNoLock(entities.Slice(0, allocated));
                    }
                    finally
                    {
                        ArrayPool<Entity>.Shared.Return(entitiesBuffer, clearArray: false);
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(offsetsBuffer, clearArray: false);
                ArrayPool<int>.Shared.Return(cursorsBuffer, clearArray: false);
                ArrayPool<int>.Shared.Return(permutationBuffer, clearArray: false);
                ClearStableGroups(groupCount);
            }
        }

        private void SpawnPrefabsStableGrouped(
            World world,
            Span<PrefabId> prefabIds,
            Span<Archetype> archetypes,
            Span<Entity> createdEntities,
            bool markDirtyNoLock,
            ref int createdEntityCount,
            ref int spawnsApplied)
        {
            int groupCount = BuildStableGroups(archetypes);
            if (groupCount == 0)
                return;

            var offsetsBuffer = ArrayPool<int>.Shared.Rent(groupCount);
            var cursorsBuffer = ArrayPool<int>.Shared.Rent(groupCount);
            var permutationBuffer = ArrayPool<int>.Shared.Rent(prefabIds.Length);

            try
            {
                var offsets = offsetsBuffer.AsSpan(0, groupCount);
                var cursors = cursorsBuffer.AsSpan(0, groupCount);
                var permutation = permutationBuffer.AsSpan(0, prefabIds.Length);

                int running = 0;
                for (int g = 0; g < groupCount; g++)
                {
                    var archetype = _stableGroupArchetypes[g]!;
                    int count = _stableGroupCounts[g];
                    archetype.Reserve(count);
                    offsets[g] = running;
                    cursors[g] = running;
                    running += count;
                }

                for (int i = 0; i < prefabIds.Length; i++)
                {
                    int groupIndex = _stableGroupIndices[archetypes[i]];
                    permutation[cursors[groupIndex]++] = i;
                }

                for (int g = 0; g < groupCount; g++)
                {
                    var archetype = _stableGroupArchetypes[g]!;
                    int start = offsets[g];
                    int length = _stableGroupCounts[g];

                    var entitiesBuffer = ArrayPool<Entity>.Shared.Rent(length);
                    try
                    {
                        var entities = entitiesBuffer.AsSpan(0, length);
                        int allocated = world.EntityStore.AllocateBulk(entities);
                        int startSlot = archetype.AddEntitiesUninitialized(entities.Slice(0, allocated));
                        world.EntityStore.SetLocationsBulkAssumeValid(entities.Slice(0, allocated), archetype, startSlot);

                        int j = 0;
                        while (j < allocated)
                        {
                            int requestIndex = permutation[start + j];
                            var prefabId = prefabIds[requestIndex];
                            var entry = PrefabRegistry.Get(prefabId);

                            if (!entry.EnableTemplateCopy || entry.HasResolvedColumns)
                            {
                                var entity = entities[j];
                                int slot = startSlot + j;
                                PrefabRegistry.Apply(prefabId, archetype, slot);

                                RecordCreatedEntity(entity, createdEntities, ref createdEntityCount);
                                spawnsApplied++;
                                j++;
                                continue;
                            }

                            int runStart = j;
                            int runEndExclusive = runStart + 1;
                            while (runEndExclusive < allocated)
                            {
                                int nextRequestIndex = permutation[start + runEndExclusive];
                                if (prefabIds[nextRequestIndex] != prefabId)
                                    break;
                                runEndExclusive++;
                            }

                            int firstSlot = startSlot + runStart;
                            entry.Apply(archetype, firstSlot);
                            int runLength = runEndExclusive - runStart;
                            if (runLength > 1)
                                archetype.CopySlotRange(firstSlot, firstSlot + 1, runLength - 1);

                            if (entry.HasPerInstanceApply)
                            {
                                for (int k = runStart; k < runEndExclusive; k++)
                                    entry.ApplyPerInstance!(archetype, startSlot + k);
                            }

                            for (int k = runStart; k < runEndExclusive; k++)
                            {
                                var entity = entities[k];
                                RecordCreatedEntity(entity, createdEntities, ref createdEntityCount);
                                spawnsApplied++;
                            }

                            j = runEndExclusive;
                        }

                        if (markDirtyNoLock && allocated > 0)
                            world.MarkDirtyUniqueNoLock(entities.Slice(0, allocated));
                    }
                    finally
                    {
                        ArrayPool<Entity>.Shared.Return(entitiesBuffer, clearArray: false);
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(offsetsBuffer, clearArray: false);
                ArrayPool<int>.Shared.Return(cursorsBuffer, clearArray: false);
                ArrayPool<int>.Shared.Return(permutationBuffer, clearArray: false);
                ClearStableGroups(groupCount);
            }
        }

        private int BuildStableGroups(Span<Archetype> archetypes)
        {
            _stableGroupIndices.Clear();
            int groupCount = 0;

            for (int i = 0; i < archetypes.Length; i++)
            {
                var archetype = archetypes[i];

                int linearIndex = -1;
                if (groupCount <= SmallGroupThreshold)
                {
                    for (int g = 0; g < groupCount; g++)
                    {
                        if (ReferenceEquals(_stableGroupArchetypes[g], archetype))
                        {
                            linearIndex = g;
                            break;
                        }
                    }
                }

                if (linearIndex >= 0)
                {
                    _stableGroupCounts[linearIndex]++;
                    continue;
                }

                if (groupCount >= SmallGroupThreshold && _stableGroupIndices.Count == 0)
                {
                    for (int g = 0; g < groupCount; g++)
                        _stableGroupIndices[_stableGroupArchetypes[g]!] = g;
                }

                if (_stableGroupIndices.Count > 0 && _stableGroupIndices.TryGetValue(archetype, out int mappedIndex))
                {
                    _stableGroupCounts[mappedIndex]++;
                    continue;
                }

                EnsureStableGroupCapacity(groupCount + 1);
                if (_stableGroupIndices.Count > 0)
                    _stableGroupIndices[archetype] = groupCount;
                _stableGroupArchetypes[groupCount] = archetype;
                _stableGroupCounts[groupCount] = 1;
                groupCount++;
            }

            if (_stableGroupIndices.Count == 0 && groupCount > 0)
            {
                for (int g = 0; g < groupCount; g++)
                    _stableGroupIndices[_stableGroupArchetypes[g]!] = g;
            }

            return groupCount;
        }

        private void ClearStableGroups(int groupCount)
        {
            _stableGroupIndices.Clear();
            if (groupCount > 0)
                Array.Clear(_stableGroupArchetypes, 0, groupCount);
        }

        private void EnsureStableGroupCapacity(int needed)
        {
            if (needed <= _stableGroupArchetypes.Length)
                return;

            int newSize = Math.Max(_stableGroupArchetypes.Length * 2, needed);
            Array.Resize(ref _stableGroupArchetypes, newSize);
            Array.Resize(ref _stableGroupCounts, newSize);
        }

        private static void RecordCreatedEntity(Entity entity, Span<Entity> createdEntities, ref int createdEntityCount)
        {
            if ((uint)createdEntityCount < (uint)createdEntities.Length)
                createdEntities[createdEntityCount] = entity;

            createdEntityCount++;
        }

        private readonly struct PrefabSpawnRequest
        {
            public readonly ArchetypeSignature Signature;
            public readonly PrefabApply Apply;
            public readonly PrefabApply? ApplyPerInstance;
            public readonly bool EnableTemplateCopy;
            public readonly bool HasPerInstanceData;

            public PrefabSpawnRequest(ArchetypeSignature signature, PrefabApply apply)
            {
                Signature = signature;
                Apply = apply;
                ApplyPerInstance = null;
                EnableTemplateCopy = false;
                HasPerInstanceData = false;
            }

            public PrefabSpawnRequest(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData)
            {
                Signature = signature;
                Apply = applyTemplate;
                ApplyPerInstance = applyPerInstance;
                EnableTemplateCopy = true;
                HasPerInstanceData = hasPerInstanceData && applyPerInstance != null;
            }
        }

        private void ReserveGrouped(Archetype[] archetypes, int count)
        {
            int smallCount = 0;
            bool useDictionary = false;
            int fallbackStart = 0;

            for (int i = 0; i < count; i++)
            {
                var archetype = archetypes[i];
                int found = -1;

                for (int g = 0; g < smallCount; g++)
                {
                    if (ReferenceEquals(_smallGroupArchetypes[g], archetype))
                    {
                        found = g;
                        break;
                    }
                }

                if (found >= 0)
                {
                    _smallGroupCounts[found]++;
                    continue;
                }

                if (smallCount < SmallGroupThreshold)
                {
                    _smallGroupArchetypes[smallCount] = archetype;
                    _smallGroupCounts[smallCount] = 1;
                    smallCount++;
                    continue;
                }

                useDictionary = true;
                fallbackStart = i;
                break;
            }

            if (!useDictionary)
            {
                for (int i = 0; i < smallCount; i++)
                {
                    _smallGroupArchetypes[i]!.Reserve(_smallGroupCounts[i]);
                    _smallGroupArchetypes[i] = null;
                    _smallGroupCounts[i] = 0;
                }

                return;
            }

            _reserveCounts.Clear();
            for (int i = 0; i < smallCount; i++)
            {
                var archetype = _smallGroupArchetypes[i]!;
                _reserveCounts[archetype] = _smallGroupCounts[i];
                _smallGroupArchetypes[i] = null;
                _smallGroupCounts[i] = 0;
            }

            for (int i = fallbackStart; i < count; i++)
            {
                var archetype = archetypes[i];
                _reserveCounts[archetype] = _reserveCounts.TryGetValue(archetype, out var current) ? current + 1 : 1;
            }

            foreach (var pair in _reserveCounts)
                pair.Key.Reserve(pair.Value);

            _reserveCounts.Clear();
        }

        private sealed class LocalQueueState
        {
            private readonly StructuralQueue _owner;
            public readonly QueueBuffer<ArchetypeSignature> CreateDefaults = new(32);
            public readonly QueueBuffer<PrefabSpawnRequest> DirectSpawns = new(32);
            public readonly QueueBuffer<PrefabId> PrefabSpawns = new(32);
            public readonly QueueBuffer<Entity> Destroys = new(32);
            public IComponentAddQueue?[] AddQueues = new IComponentAddQueue?[16];
            public IComponentRemoveQueue?[] RemoveQueues = new IComponentRemoveQueue?[16];
            public int MaxAddIndex;
            public int MaxRemoveIndex;

            public LocalQueueState(StructuralQueue owner)
            {
                _owner = owner;
            }

            public bool HasPending
                => CreateDefaults.Count > 0
                   || DirectSpawns.Count > 0
                   || PrefabSpawns.Count > 0
                   || Destroys.Count > 0
                   || MaxAddIndex > 0
                   || MaxRemoveIndex > 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ResetIndices()
            {
                MaxAddIndex = 0;
                MaxRemoveIndex = 0;
            }

            public void EnqueueAdd<T>(int index, ComponentId id, Entity entity, T value) where T : unmanaged, IComponent<T>
            {
                if (index >= AddQueues.Length)
                    Array.Resize(ref AddQueues, Math.Max(AddQueues.Length * 2, index + 1));

                var queue = AddQueues[index] as ComponentAddQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentAddQueue<T>(_owner, id);
                    AddQueues[index] = queue;
                }

                queue.Enqueue(entity, value);
                if (index + 1 > MaxAddIndex)
                    MaxAddIndex = index + 1;
            }

            public void EnqueueAddRange<T>(int index, ComponentId id, ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values)
                where T : unmanaged, IComponent<T>
            {
                if (index >= AddQueues.Length)
                    Array.Resize(ref AddQueues, Math.Max(AddQueues.Length * 2, index + 1));

                var queue = AddQueues[index] as ComponentAddQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentAddQueue<T>(_owner, id);
                    AddQueues[index] = queue;
                }

                queue.EnqueueRange(entities, values);
                if (index + 1 > MaxAddIndex)
                    MaxAddIndex = index + 1;
            }

            public void EnqueueAddRepeated<T>(int index, ComponentId id, ReadOnlySpan<Entity> entities, in T value)
                where T : unmanaged, IComponent<T>
            {
                if (index >= AddQueues.Length)
                    Array.Resize(ref AddQueues, Math.Max(AddQueues.Length * 2, index + 1));

                var queue = AddQueues[index] as ComponentAddQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentAddQueue<T>(_owner, id);
                    AddQueues[index] = queue;
                }

                queue.EnqueueRepeated(entities, value);
                if (index + 1 > MaxAddIndex)
                    MaxAddIndex = index + 1;
            }

            public void EnqueueRemove<T>(int index, ComponentId id, Entity entity) where T : unmanaged, IComponent<T>
            {
                if (index >= RemoveQueues.Length)
                    Array.Resize(ref RemoveQueues, Math.Max(RemoveQueues.Length * 2, index + 1));

                var queue = RemoveQueues[index] as ComponentRemoveQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentRemoveQueue<T>(_owner, id);
                    RemoveQueues[index] = queue;
                }

                queue.Enqueue(entity);
                if (index + 1 > MaxRemoveIndex)
                    MaxRemoveIndex = index + 1;
            }

            public void EnqueueRemoveRange<T>(int index, ComponentId id, ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
            {
                if (index >= RemoveQueues.Length)
                    Array.Resize(ref RemoveQueues, Math.Max(RemoveQueues.Length * 2, index + 1));

                var queue = RemoveQueues[index] as ComponentRemoveQueue<T>;
                if (queue == null)
                {
                    queue = new ComponentRemoveQueue<T>(_owner, id);
                    RemoveQueues[index] = queue;
                }

                queue.EnqueueRange(entities);
                if (index + 1 > MaxRemoveIndex)
                    MaxRemoveIndex = index + 1;
            }
        }

        private interface IComponentQueue
        {
            bool SwapForApply();
            void ClearRead();
            void ClearAll();
        }

        private interface IComponentAddQueue : IComponentQueue
        {
            int PendingWriteCount { get; }
            int Apply(World world, bool markDirtyNoLock);
            int MoveWritesTo(ref IComponentAddQueue? destination);
        }

        private interface IComponentRemoveQueue : IComponentQueue
        {
            int PendingWriteCount { get; }
            int Apply(World world, bool markDirtyNoLock);
            int MoveWritesTo(ref IComponentRemoveQueue? destination);
        }

        private sealed class ComponentAddQueue<T> : IComponentAddQueue where T : unmanaged, IComponent<T>
        {
            private readonly StructuralQueue _owner;
            private readonly QueueBuffer<Entity> _writeEntities = new(64);
            private readonly QueueBuffer<Entity> _readEntities = new(64);
            private readonly QueueBuffer<T> _writeValues = new(64);
            private readonly QueueBuffer<T> _readValues = new(64);
            private readonly Dictionary<Archetype, TransitionPlan> _transitionCache = new(8);
            private readonly Dictionary<Archetype, int> _reserveCounts = new(8);

            public ComponentId ComponentId { get; }

            public int PendingWriteCount
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _writeEntities.Count;
            }

            public ComponentAddQueue(StructuralQueue owner, ComponentId id)
            {
                _owner = owner;
                ComponentId = id;
            }

            public void Enqueue(Entity entity, T value)
            {
                _writeEntities.Add(entity);
                _writeValues.Add(value);
            }

            public void EnqueueRange(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values)
            {
                _writeEntities.Append(entities);
                _writeValues.Append(values);
            }

            public void EnqueueRepeated(ReadOnlySpan<Entity> entities, in T value)
            {
                _writeEntities.Append(entities);
                _writeValues.AppendRepeated(value, entities.Length);
            }

            public bool SwapForApply()
            {
                if (_writeEntities.Count == 0)
                    return false;

                _writeEntities.SwapWith(_readEntities);
                _writeValues.SwapWith(_readValues);
                return _readEntities.Count > 0;
            }

            public int MoveWritesTo(ref IComponentAddQueue? destination)
            {
                int moved = _writeEntities.Count;
                if (moved == 0)
                    return 0;

                if (destination == null)
                    destination = new ComponentAddQueue<T>(_owner, ComponentId);

                var typed = destination as ComponentAddQueue<T>
                    ?? throw new InvalidOperationException($"Component add queue type mismatch for id {ComponentId}.");

                typed.AppendWrites(_writeEntities.AsSpan(), _writeValues.AsSpan());
                _writeEntities.Clear();
                _writeValues.Clear();
                return moved;
            }

            public int Apply(World world, bool markDirtyNoLock)
            {
                int applied = 0;
                var entities = _readEntities.AsSpan();
                var values = _readValues.AsSpan();

                ReserveDestinationCapacity(world, entities);

                Archetype? cachedSource = null;
                bool cachedHasComponent = false;
                bool cachedPlanReady = false;
                TransitionPlan cachedPlan = TransitionPlan.Empty;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (!world.EntityStore.TryGetLocation(entity, out var archetype, out var slot))
                        continue;

                    if (!ReferenceEquals(archetype, cachedSource))
                    {
                        cachedSource = archetype;
                        cachedHasComponent = archetype.HasComponent(ComponentId);
                        cachedPlanReady = false;
                    }

                    if (cachedHasComponent)
                    {
                        archetype.GetComponentSpan<T>(ComponentId)[slot] = values[i];
                        if (markDirtyNoLock)
                            world.MarkDirtyNoLock(ComponentId, entity);
                        else
                            world.MarkDirty(ComponentId, entity);

                        applied++;
                        continue;
                    }

                    if (!cachedPlanReady)
                    {
                        cachedPlan = GetTransitionPlan(archetype, world.Archetypes);
                        cachedPlanReady = true;
                    }

                    var plan = cachedPlan;
                    var newArchetype = plan.Destination;
                    int newSlot = newArchetype.AddEntityUninitializedAssumeCapacity(entity);

                    plan.CopyMap.Copy(slot, newSlot);

                    newArchetype.GetComponentSpan<T>(ComponentId)[newSlot] = values[i];
                    world.EntityStore.SetLocation(entity, newArchetype, newSlot);
                    RemoveFromArchetype(world, archetype, slot);

                    if (markDirtyNoLock)
                        world.MarkDirtyNoLock(ComponentId, entity);
                    else
                        world.MarkDirty(ComponentId, entity);

                    applied++;
                }

                return applied;
            }

            public void ClearRead()
            {
                _readEntities.Clear();
                _readValues.Clear();
            }

            public void ClearAll()
            {
                _writeEntities.Clear();
                _readEntities.Clear();
                _writeValues.Clear();
                _readValues.Clear();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void AppendWrites(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values)
            {
                _writeEntities.Append(entities);
                _writeValues.Append(values);
            }

            private TransitionPlan GetTransitionPlan(Archetype source, ArchetypeManager manager)
            {
                if (_transitionCache.TryGetValue(source, out var plan))
                    return plan;

                var destination = GetOrCreateWithAdded(source, ComponentId, manager);
                var copyMap = _owner.GetOrCreateColumnCopyMap(source, destination, ComponentId.Invalid);
                plan = new TransitionPlan(destination, copyMap);
                _transitionCache[source] = plan;
                return plan;
            }

            private void ReserveDestinationCapacity(World world, ReadOnlySpan<Entity> entities)
            {
                _reserveCounts.Clear();

                for (int i = 0; i < entities.Length; i++)
                {
                    if (!world.EntityStore.TryGetLocation(entities[i], out var archetype, out _))
                        continue;

                    if (archetype.HasComponent(ComponentId))
                        continue;

                    var destination = GetTransitionPlan(archetype, world.Archetypes).Destination;
                    _reserveCounts[destination] = _reserveCounts.TryGetValue(destination, out var count)
                        ? count + 1
                        : 1;
                }

                foreach (var pair in _reserveCounts)
                    pair.Key.Reserve(pair.Value);

                _reserveCounts.Clear();
            }

            private readonly struct TransitionPlan
            {
                public static readonly TransitionPlan Empty = new(null!, ColumnCopyMap.Empty);

                public readonly Archetype Destination;
                public readonly ColumnCopyMap CopyMap;

                public TransitionPlan(Archetype destination, ColumnCopyMap copyMap)
                {
                    Destination = destination;
                    CopyMap = copyMap;
                }
            }
        }

        private sealed class ComponentRemoveQueue<T> : IComponentRemoveQueue where T : unmanaged, IComponent<T>
        {
            private readonly StructuralQueue _owner;
            private readonly QueueBuffer<Entity> _writeEntities = new(64);
            private readonly QueueBuffer<Entity> _readEntities = new(64);
            private readonly Dictionary<Archetype, TransitionPlan> _transitionCache = new(8);
            private readonly Dictionary<Archetype, int> _reserveCounts = new(8);

            public ComponentId ComponentId { get; }

            public int PendingWriteCount
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _writeEntities.Count;
            }

            public ComponentRemoveQueue(StructuralQueue owner, ComponentId id)
            {
                _owner = owner;
                ComponentId = id;
            }

            public void Enqueue(Entity entity) => _writeEntities.Add(entity);

            public void EnqueueRange(ReadOnlySpan<Entity> entities) => _writeEntities.Append(entities);

            public bool SwapForApply()
            {
                if (_writeEntities.Count == 0)
                    return false;

                _writeEntities.SwapWith(_readEntities);
                return _readEntities.Count > 0;
            }

            public int MoveWritesTo(ref IComponentRemoveQueue? destination)
            {
                int moved = _writeEntities.Count;
                if (moved == 0)
                    return 0;

                if (destination == null)
                    destination = new ComponentRemoveQueue<T>(_owner, ComponentId);

                var typed = destination as ComponentRemoveQueue<T>
                    ?? throw new InvalidOperationException($"Component remove queue type mismatch for id {ComponentId}.");

                typed.AppendWrites(_writeEntities.AsSpan());
                _writeEntities.Clear();
                return moved;
            }

            public int Apply(World world, bool markDirtyNoLock)
            {
                int applied = 0;
                var entities = _readEntities.AsSpan();

                ReserveDestinationCapacity(world, entities);

                Archetype? cachedSource = null;
                bool cachedHasComponent = false;
                bool cachedPlanReady = false;
                TransitionPlan cachedPlan = TransitionPlan.Empty;

                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (!world.EntityStore.TryGetLocation(entity, out var archetype, out var slot))
                        continue;

                    if (!ReferenceEquals(archetype, cachedSource))
                    {
                        cachedSource = archetype;
                        cachedHasComponent = archetype.HasComponent(ComponentId);
                        cachedPlanReady = false;
                    }

                    if (!cachedHasComponent)
                        continue;

                    if (!cachedPlanReady)
                    {
                        cachedPlan = GetTransitionPlan(archetype, world.Archetypes);
                        cachedPlanReady = true;
                    }

                    var plan = cachedPlan;
                    var newArchetype = plan.Destination;
                    int newSlot = newArchetype.AddEntityUninitializedAssumeCapacity(entity);

                    plan.CopyMap.Copy(slot, newSlot);
                    world.EntityStore.SetLocation(entity, newArchetype, newSlot);
                    RemoveFromArchetype(world, archetype, slot);

                    if (markDirtyNoLock)
                        world.MarkDirtyNoLock(ComponentId, entity);
                    else
                        world.MarkDirty(ComponentId, entity);

                    applied++;
                }

                return applied;
            }

            public void ClearRead() => _readEntities.Clear();

            public void ClearAll()
            {
                _writeEntities.Clear();
                _readEntities.Clear();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void AppendWrites(ReadOnlySpan<Entity> entities)
            {
                _writeEntities.Append(entities);
            }

            private TransitionPlan GetTransitionPlan(Archetype source, ArchetypeManager manager)
            {
                if (_transitionCache.TryGetValue(source, out var plan))
                    return plan;

                var destination = GetOrCreateWithRemoved(source, ComponentId, manager);
                var copyMap = _owner.GetOrCreateColumnCopyMap(source, destination, ComponentId);
                plan = new TransitionPlan(destination, copyMap);
                _transitionCache[source] = plan;
                return plan;
            }

            private void ReserveDestinationCapacity(World world, ReadOnlySpan<Entity> entities)
            {
                _reserveCounts.Clear();

                for (int i = 0; i < entities.Length; i++)
                {
                    if (!world.EntityStore.TryGetLocation(entities[i], out var archetype, out _))
                        continue;

                    if (!archetype.HasComponent(ComponentId))
                        continue;

                    var destination = GetTransitionPlan(archetype, world.Archetypes).Destination;
                    _reserveCounts[destination] = _reserveCounts.TryGetValue(destination, out var count)
                        ? count + 1
                        : 1;
                }

                foreach (var pair in _reserveCounts)
                    pair.Key.Reserve(pair.Value);

                _reserveCounts.Clear();
            }

            private readonly struct TransitionPlan
            {
                public static readonly TransitionPlan Empty = new(null!, ColumnCopyMap.Empty);

                public readonly Archetype Destination;
                public readonly ColumnCopyMap CopyMap;

                public TransitionPlan(Archetype destination, ColumnCopyMap copyMap)
                {
                    Destination = destination;
                    CopyMap = copyMap;
                }
            }
        }

        private static Archetype GetOrCreateWithAdded(Archetype archetype, ComponentId addId, ArchetypeManager manager)
        {
            var ids = archetype.ComponentIds;
            int count = ids.Length;

            if (count == 0)
                return manager.GetOrCreate(stackalloc ComponentId[1] { addId });

            const int stackThreshold = 64;
            int needed = count + 1;

            if (needed <= stackThreshold)
            {
                Span<ComponentId> buffer = stackalloc ComponentId[stackThreshold];
                int write = MergeWithAdded(ids, addId, buffer);
                return manager.GetOrCreate(buffer.Slice(0, write));
            }

            var rented = ArrayPool<ComponentId>.Shared.Rent(needed);
            try
            {
                int write = MergeWithAdded(ids, addId, rented.AsSpan(0, needed));
                return manager.GetOrCreate(rented.AsSpan(0, write));
            }
            finally
            {
                ArrayPool<ComponentId>.Shared.Return(rented);
            }
        }

        private static Archetype GetOrCreateWithRemoved(Archetype archetype, ComponentId removeId, ArchetypeManager manager)
        {
            var ids = archetype.ComponentIds;
            int count = ids.Length;
            if (count == 1)
                return manager.GetOrCreateEmpty();

            const int stackThreshold = 64;
            int needed = count - 1;

            if (needed <= stackThreshold)
            {
                Span<ComponentId> buffer = stackalloc ComponentId[stackThreshold];
                int write = MergeWithRemoved(ids, removeId, buffer);
                if (write == 0)
                    return manager.GetOrCreateEmpty();
                return manager.GetOrCreate(buffer.Slice(0, write));
            }

            var rented = ArrayPool<ComponentId>.Shared.Rent(needed);
            try
            {
                int write = MergeWithRemoved(ids, removeId, rented.AsSpan(0, needed));
                if (write == 0)
                    return manager.GetOrCreateEmpty();
                return manager.GetOrCreate(rented.AsSpan(0, write));
            }
            finally
            {
                ArrayPool<ComponentId>.Shared.Return(rented);
            }
        }

        private static int MergeWithAdded(ReadOnlySpan<ComponentId> source, ComponentId addId, Span<ComponentId> destination)
        {
            int write = 0;
            bool inserted = false;

            for (int i = 0; i < source.Length; i++)
            {
                var current = source[i];
                if (!inserted && addId.Value < current.Value)
                {
                    destination[write++] = addId;
                    inserted = true;
                }
                destination[write++] = current;
            }

            if (!inserted)
                destination[write++] = addId;

            return write;
        }

        private static int MergeWithRemoved(ReadOnlySpan<ComponentId> source, ComponentId removeId, Span<ComponentId> destination)
        {
            int write = 0;
            for (int i = 0; i < source.Length; i++)
            {
                var current = source[i];
                if (current == removeId)
                    continue;
                destination[write++] = current;
            }
            return write;
        }

        private ColumnCopyMap GetOrCreateColumnCopyMap(Archetype source, Archetype destination, ComponentId skip)
        {
            int skipValue = skip.Value;
            var key = new ColumnCopyMapKey(source, destination, skipValue);
            if (!_columnCopyMapCache.TryGetValue(key, out var map))
            {
                map = BuildColumnCopyMap(source, destination, skipValue);
                _columnCopyMapCache[key] = map;
            }

            return map;
        }

        private static ColumnCopyMap BuildColumnCopyMap(Archetype source, Archetype destination, int skipValue)
        {
            var ids = source.ComponentIds;
            int pairCount = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id.Value == skipValue)
                    continue;

                int sourceIndex = source.GetColumnIndex(id);
                int destinationIndex = destination.GetColumnIndex(id);
                if (sourceIndex < 0 || destinationIndex < 0)
                    continue;

                pairCount++;
            }

            if (pairCount == 0)
                return ColumnCopyMap.Empty;

            var sourceColumns = new IComponentColumn[pairCount];
            var destinationColumns = new IComponentColumn[pairCount];

            int write = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id.Value == skipValue)
                    continue;

                int sourceIndex = source.GetColumnIndex(id);
                int destinationIndex = destination.GetColumnIndex(id);
                if (sourceIndex < 0 || destinationIndex < 0)
                    continue;

                sourceColumns[write] = source.GetColumnAt(sourceIndex);
                destinationColumns[write] = destination.GetColumnAt(destinationIndex);
                write++;
            }

            return new ColumnCopyMap(sourceColumns, destinationColumns, write);
        }

        private static void RemoveFromArchetype(World world, Archetype archetype, int slot)
        {
            if (!archetype.RemoveAtSwap(slot, out var moved, out var movedSlot))
                return;

            if (movedSlot >= 0)
                world.EntityStore.SetLocation(moved, archetype, movedSlot);
        }

        private readonly struct ColumnCopyMapKey : IEquatable<ColumnCopyMapKey>
        {
            private readonly Archetype _source;
            private readonly Archetype _destination;
            private readonly int _skipValue;

            public ColumnCopyMapKey(Archetype source, Archetype destination, int skipValue)
            {
                _source = source;
                _destination = destination;
                _skipValue = skipValue;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(ColumnCopyMapKey other)
                => ReferenceEquals(_source, other._source)
                && ReferenceEquals(_destination, other._destination)
                && _skipValue == other._skipValue;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override bool Equals(object? obj)
                => obj is ColumnCopyMapKey other && Equals(other);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode() => HashCode.Combine(_source, _destination, _skipValue);
        }

        private sealed class ColumnCopyMap
        {
            public static readonly ColumnCopyMap Empty = new(Array.Empty<IComponentColumn>(), Array.Empty<IComponentColumn>(), 0);

            private readonly IComponentColumn[] _sourceColumns;
            private readonly IComponentColumn[] _destinationColumns;
            private readonly int _count;

            public ColumnCopyMap(IComponentColumn[] sourceColumns, IComponentColumn[] destinationColumns, int count)
            {
                _sourceColumns = sourceColumns;
                _destinationColumns = destinationColumns;
                _count = count;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Copy(int sourceSlot, int destinationSlot)
            {
                for (int i = 0; i < _count; i++)
                    _destinationColumns[i].CopyFrom(_sourceColumns[i], sourceSlot, destinationSlot);
            }
        }

        private sealed class QueueBuffer<T>
        {
            private T[] _buffer;
            private int _count;

            public int Count
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _count;
            }

            public QueueBuffer(int initialCapacity)
            {
                _buffer = ArrayPool<T>.Shared.Rent(Math.Max(4, initialCapacity));
                _count = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(T value)
            {
                if (_count == _buffer.Length)
                    Grow(_count + 1);

                _buffer[_count++] = value;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Span<T> AsSpan() => _buffer.AsSpan(0, _count);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Append(ReadOnlySpan<T> values)
            {
                if (values.Length == 0)
                    return;

                int needed = _count + values.Length;
                if (needed > _buffer.Length)
                    Grow(needed);

                values.CopyTo(_buffer.AsSpan(_count, values.Length));
                _count = needed;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendRepeated(in T value, int count)
            {
                if (count <= 0)
                    return;

                int needed = _count + count;
                if (needed > _buffer.Length)
                    Grow(needed);

                var destination = _buffer.AsSpan(_count, count);
                for (int i = 0; i < destination.Length; i++)
                    destination[i] = value;

                _count = needed;
            }

            public void Clear()
            {
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                    _buffer.AsSpan(0, _count).Clear();

                _count = 0;
            }

            public void SwapWith(QueueBuffer<T> other)
            {
                (other._buffer, _buffer) = (_buffer, other._buffer);
                (other._count, _count) = (_count, other._count);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void Grow(int needed)
            {
                int newSize = Math.Max(_buffer.Length * 2, needed);
                var newBuffer = ArrayPool<T>.Shared.Rent(newSize);
                _buffer.AsSpan(0, _count).CopyTo(newBuffer);
                ArrayPool<T>.Shared.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
                _buffer = newBuffer;
            }
        }
    }
}
