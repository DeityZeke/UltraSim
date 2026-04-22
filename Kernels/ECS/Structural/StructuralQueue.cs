using System.Runtime.InteropServices;

namespace UltraSim.ECS;

internal sealed class StructuralQueue
{
    private readonly object _sync = new();
    private List<ArchetypeSignature> _createDefaultsWrite = new();
    private List<ArchetypeSignature> _createDefaultsRead = new();
    private List<PrefabDefinition> _prefabDefinitionsWrite = new();
    private List<PrefabDefinition> _prefabDefinitionsRead = new();
    private List<Entity> _destroysWrite = new();
    private List<Entity> _destroysRead = new();
    private readonly List<IStructuralPrefabBuffer> _prefabBuffers = new();
    private readonly Dictionary<Type, IStructuralPrefabBuffer> _prefabByType = new();
    private readonly List<IStructuralAddBuffer> _addBuffers = new();
    private readonly Dictionary<Type, IStructuralAddBuffer> _addByType = new();
    private readonly List<IStructuralRemoveBuffer> _removeBuffers = new();
    private readonly Dictionary<Type, IStructuralRemoveBuffer> _removeByType = new();
    private int _prefabCount;
    private int _addCount;
    private int _removeCount;

    public bool HasPending
    {
        get
        {
            lock (_sync)
                return _createDefaultsWrite.Count > 0
                    || _createDefaultsRead.Count > 0
                    || _prefabDefinitionsWrite.Count > 0
                    || _prefabDefinitionsRead.Count > 0
                    || _destroysWrite.Count > 0
                    || _destroysRead.Count > 0
                    || _prefabCount > 0
                    || _addCount > 0
                    || _removeCount > 0;
        }
    }

    public void EnqueueCreateDefault(ArchetypeSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        lock (_sync)
            _createDefaultsWrite.Add(signature);
    }

    public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
    {
        lock (_sync)
        {
            for (int i = 0; i < signatures.Length; i++)
                ArgumentNullException.ThrowIfNull(signatures[i]);

            AppendRange(_createDefaultsWrite, signatures);
        }
    }

    public void EnqueueCreatePrefab(PrefabDefinition prefab)
    {
        lock (_sync)
        {
            _prefabDefinitionsWrite.Add(prefab);
            _prefabCount++;
        }
    }

    public void EnqueueCreatePrefab(ReadOnlySpan<PrefabDefinition> prefabs)
    {
        lock (_sync)
        {
            AppendRange(_prefabDefinitionsWrite, prefabs);
            _prefabCount += prefabs.Length;
        }
    }

    public void EnqueueCreatePrefab<TPrefab>(in TPrefab prefab)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        lock (_sync)
        {
            GetPrefabBuffer<TPrefab>().Add(prefab);
            _prefabCount++;
        }
    }

    public void EnqueueCreatePrefab<TPrefab>(ReadOnlySpan<TPrefab> prefabs)
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        lock (_sync)
        {
            GetPrefabBuffer<TPrefab>().AddRange(prefabs);
            _prefabCount += prefabs.Length;
        }
    }

    public void EnqueueDestroy(Entity entity)
    {
        lock (_sync)
            _destroysWrite.Add(entity);
    }

    public void EnqueueDestroy(ReadOnlySpan<Entity> entities)
    {
        lock (_sync)
            AppendRange(_destroysWrite, entities);
    }

    public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
        {
            GetAddBuffer<T>().Add(entity, value);
            _addCount++;
        }
    }

    public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
    {
        if (entities.Length != values.Length)
            throw new ArgumentException("Entities and values length must match.");

        lock (_sync)
        {
            GetAddBuffer<T>().AddRange(entities, values);
            _addCount += entities.Length;
        }
    }

    public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
        {
            GetAddBuffer<T>().AddRepeated(entities, value);
            _addCount += entities.Length;
        }
    }

    public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
        {
            GetRemoveBuffer<T>().Add(entity);
            _removeCount++;
        }
    }

    public void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
        {
            GetRemoveBuffer<T>().AddRange(entities);
            _removeCount += entities.Length;
        }
    }

    public StructuralApplyStats Apply(World world)
    {
        IStructuralPrefabBuffer[] prefabBuffers;
        IStructuralAddBuffer[] addBuffers;
        IStructuralRemoveBuffer[] removeBuffers;
        int prefabCount;
        int addCount;
        int removeCount;

        lock (_sync)
        {
            if (_createDefaultsWrite.Count == 0
                && _createDefaultsRead.Count == 0
                && _prefabDefinitionsWrite.Count == 0
                && _prefabDefinitionsRead.Count == 0
                && _destroysWrite.Count == 0
                && _destroysRead.Count == 0
                && _prefabCount == 0
                && _addCount == 0
                && _removeCount == 0)
            {
                return default;
            }

            SwapCreateDefaultsForApply();
            SwapPrefabDefinitionsForApply();
            SwapDestroysForApply();
            prefabBuffers = SnapshotPrefabBuffers();
            addBuffers = SnapshotAddBuffers();
            removeBuffers = SnapshotRemoveBuffers();
            prefabCount = _prefabCount;
            addCount = _addCount;
            removeCount = _removeCount;

            _prefabCount = 0;
            _addCount = 0;
            _removeCount = 0;
        }

        _ = world.DestroyImmediateBatch(CollectionsMarshal.AsSpan(_destroysRead));

        _ = world.CreateDefaultImmediateBatch(CollectionsMarshal.AsSpan(_createDefaultsRead));

        _ = world.CreatePrefabImmediateBatch(CollectionsMarshal.AsSpan(_prefabDefinitionsRead));

        for (int i = 0; i < prefabBuffers.Length; i++)
            prefabBuffers[i].Apply(world);

        for (int i = 0; i < removeBuffers.Length; i++)
            removeBuffers[i].Apply(world);

        for (int i = 0; i < addBuffers.Length; i++)
            addBuffers[i].Apply(world);

        int createDefaultCount = _createDefaultsRead.Count;
        int destroyCount = _destroysRead.Count;
        _createDefaultsRead.Clear();
        _prefabDefinitionsRead.Clear();
        _destroysRead.Clear();

        return new StructuralApplyStats(
            CreateDefaultCount: createDefaultCount,
            PrefabCount: prefabCount,
            DestroyCount: destroyCount,
            AddCount: addCount,
            RemoveCount: removeCount);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _createDefaultsWrite.Clear();
            _createDefaultsRead.Clear();
            _prefabDefinitionsWrite.Clear();
            _prefabDefinitionsRead.Clear();
            _destroysWrite.Clear();
            _destroysRead.Clear();
            _prefabCount = 0;
            _addCount = 0;
            _removeCount = 0;

            for (int i = 0; i < _prefabBuffers.Count; i++)
                _prefabBuffers[i].ClearAll();

            for (int i = 0; i < _addBuffers.Count; i++)
                _addBuffers[i].ClearAll();

            for (int i = 0; i < _removeBuffers.Count; i++)
                _removeBuffers[i].ClearAll();
        }
    }

    private void SwapCreateDefaultsForApply()
    {
        if (_createDefaultsWrite.Count == 0)
            return;

        (_createDefaultsWrite, _createDefaultsRead) = (_createDefaultsRead, _createDefaultsWrite);
    }

    private void SwapPrefabDefinitionsForApply()
    {
        if (_prefabDefinitionsWrite.Count == 0)
            return;

        (_prefabDefinitionsWrite, _prefabDefinitionsRead) = (_prefabDefinitionsRead, _prefabDefinitionsWrite);
    }

    private void SwapDestroysForApply()
    {
        if (_destroysWrite.Count == 0)
            return;

        (_destroysWrite, _destroysRead) = (_destroysRead, _destroysWrite);
    }

    private IStructuralPrefabBuffer[] SnapshotPrefabBuffers()
    {
        if (_prefabCount == 0)
            return Array.Empty<IStructuralPrefabBuffer>();

        var buffers = new List<IStructuralPrefabBuffer>(_prefabBuffers.Count);
        for (int i = 0; i < _prefabBuffers.Count; i++)
        {
            IStructuralPrefabBuffer buffer = _prefabBuffers[i];
            if (buffer.SwapForApply())
                buffers.Add(buffer);
        }

        return buffers.Count == 0 ? Array.Empty<IStructuralPrefabBuffer>() : buffers.ToArray();
    }

    private IStructuralAddBuffer[] SnapshotAddBuffers()
    {
        if (_addCount == 0)
            return Array.Empty<IStructuralAddBuffer>();

        var buffers = new List<IStructuralAddBuffer>(_addBuffers.Count);
        for (int i = 0; i < _addBuffers.Count; i++)
        {
            IStructuralAddBuffer buffer = _addBuffers[i];
            if (buffer.SwapForApply())
                buffers.Add(buffer);
        }

        return buffers.Count == 0 ? Array.Empty<IStructuralAddBuffer>() : buffers.ToArray();
    }

    private IStructuralRemoveBuffer[] SnapshotRemoveBuffers()
    {
        if (_removeCount == 0)
            return Array.Empty<IStructuralRemoveBuffer>();

        var buffers = new List<IStructuralRemoveBuffer>(_removeBuffers.Count);
        for (int i = 0; i < _removeBuffers.Count; i++)
        {
            IStructuralRemoveBuffer buffer = _removeBuffers[i];
            if (buffer.SwapForApply())
                buffers.Add(buffer);
        }

        return buffers.Count == 0 ? Array.Empty<IStructuralRemoveBuffer>() : buffers.ToArray();
    }

    private StructuralPrefabBuffer<TPrefab> GetPrefabBuffer<TPrefab>()
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        Type type = typeof(TPrefab);
        if (_prefabByType.TryGetValue(type, out IStructuralPrefabBuffer? existing))
            return (StructuralPrefabBuffer<TPrefab>)existing;

        var created = new StructuralPrefabBuffer<TPrefab>();
        _prefabByType.Add(type, created);
        _prefabBuffers.Add(created);
        return created;
    }

    private StructuralAddBuffer<T> GetAddBuffer<T>() where T : unmanaged, IComponent<T>
    {
        Type type = typeof(T);
        if (_addByType.TryGetValue(type, out IStructuralAddBuffer? existing))
            return (StructuralAddBuffer<T>)existing;

        var created = new StructuralAddBuffer<T>();
        _addByType.Add(type, created);
        _addBuffers.Add(created);
        return created;
    }

    private StructuralRemoveBuffer<T> GetRemoveBuffer<T>() where T : unmanaged, IComponent<T>
    {
        Type type = typeof(T);
        if (_removeByType.TryGetValue(type, out IStructuralRemoveBuffer? existing))
            return (StructuralRemoveBuffer<T>)existing;

        var created = new StructuralRemoveBuffer<T>();
        _removeByType.Add(type, created);
        _removeBuffers.Add(created);
        return created;
    }

    private static void AppendRange<T>(List<T> list, ReadOnlySpan<T> values)
    {
        if (values.Length == 0)
            return;

        int start = list.Count;
        int newCount = start + values.Length;
        list.EnsureCapacity(newCount);
        CollectionsMarshal.SetCount(list, newCount);
        values.CopyTo(CollectionsMarshal.AsSpan(list)[start..newCount]);
    }

    private interface IStructuralAddBuffer
    {
        bool SwapForApply();
        void Apply(World world);
        void ClearAll();
    }

    private interface IStructuralPrefabBuffer
    {
        bool SwapForApply();
        void Apply(World world);
        void ClearAll();
    }

    private interface IStructuralRemoveBuffer
    {
        bool SwapForApply();
        void Apply(World world);
        void ClearAll();
    }

    private sealed class StructuralPrefabBuffer<TPrefab> : IStructuralPrefabBuffer
        where TPrefab : unmanaged, IPrefab<TPrefab>
    {
        private List<TPrefab> _writePrefabs = new();
        private List<TPrefab> _readPrefabs = new();

        public void Add(TPrefab prefab)
        {
            _writePrefabs.Add(prefab);
        }

        public void AddRange(ReadOnlySpan<TPrefab> prefabs)
        {
            AppendRange(_writePrefabs, prefabs);
        }

        public bool SwapForApply()
        {
            if (_writePrefabs.Count == 0)
                return false;

            (_writePrefabs, _readPrefabs) = (_readPrefabs, _writePrefabs);
            return _readPrefabs.Count > 0;
        }

        public void Apply(World world)
        {
            _ = world.CreatePrefabImmediateBatch((ReadOnlySpan<TPrefab>)CollectionsMarshal.AsSpan(_readPrefabs));
            _readPrefabs.Clear();
        }

        public void ClearAll()
        {
            _writePrefabs.Clear();
            _readPrefabs.Clear();
        }
    }

    private sealed class StructuralAddBuffer<T> : IStructuralAddBuffer where T : unmanaged, IComponent<T>
    {
        private List<Entity> _writeEntities = new();
        private List<T> _writeValues = new();
        private List<Entity> _readEntities = new();
        private List<T> _readValues = new();

        public void Add(Entity entity, T value)
        {
            _writeEntities.Add(entity);
            _writeValues.Add(value);
        }

        public void AddRange(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values)
        {
            AppendRange(_writeEntities, entities);
            AppendRange(_writeValues, values);
        }

        public void AddRepeated(ReadOnlySpan<Entity> entities, in T value)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                _writeEntities.Add(entities[i]);
                _writeValues.Add(value);
            }
        }

        public bool SwapForApply()
        {
            if (_writeEntities.Count == 0)
                return false;

            (_writeEntities, _readEntities) = (_readEntities, _writeEntities);
            (_writeValues, _readValues) = (_readValues, _writeValues);
            return _readEntities.Count > 0;
        }

        public void Apply(World world)
        {
            _ = world.AddComponentImmediateBatch<T>(CollectionsMarshal.AsSpan(_readEntities), CollectionsMarshal.AsSpan(_readValues));

            _readEntities.Clear();
            _readValues.Clear();
        }

        public void ClearAll()
        {
            _writeEntities.Clear();
            _writeValues.Clear();
            _readEntities.Clear();
            _readValues.Clear();
        }
    }

    private sealed class StructuralRemoveBuffer<T> : IStructuralRemoveBuffer where T : unmanaged, IComponent<T>
    {
        private List<Entity> _writeEntities = new();
        private List<Entity> _readEntities = new();

        public void Add(Entity entity)
        {
            _writeEntities.Add(entity);
        }

        public void AddRange(ReadOnlySpan<Entity> entities)
        {
            AppendRange(_writeEntities, entities);
        }

        public bool SwapForApply()
        {
            if (_writeEntities.Count == 0)
                return false;

            (_writeEntities, _readEntities) = (_readEntities, _writeEntities);
            return _readEntities.Count > 0;
        }

        public void Apply(World world)
        {
            _ = world.RemoveComponentImmediateBatch<T>(CollectionsMarshal.AsSpan(_readEntities));

            _readEntities.Clear();
        }

        public void ClearAll()
        {
            _writeEntities.Clear();
            _readEntities.Clear();
        }
    }
}
