namespace UltraSim.ECS;

internal sealed class StructuralQueue
{
    private readonly object _sync = new();
    private readonly List<ArchetypeSignature> _createDefaults = new();
    private readonly List<Entity> _destroys = new();
    private readonly List<IStructuralAddOperation> _adds = new();
    private readonly List<IStructuralRemoveOperation> _removes = new();

    public bool HasPending
    {
        get
        {
            lock (_sync)
                return _createDefaults.Count > 0 || _destroys.Count > 0 || _adds.Count > 0 || _removes.Count > 0;
        }
    }

    public void EnqueueCreateDefault(ArchetypeSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        lock (_sync)
            _createDefaults.Add(signature);
    }

    public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
    {
        lock (_sync)
        {
            for (int i = 0; i < signatures.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(signatures[i]);
                _createDefaults.Add(signatures[i]);
            }
        }
    }

    public void EnqueueDestroy(Entity entity)
    {
        lock (_sync)
            _destroys.Add(entity);
    }

    public void EnqueueDestroy(ReadOnlySpan<Entity> entities)
    {
        lock (_sync)
        {
            for (int i = 0; i < entities.Length; i++)
                _destroys.Add(entities[i]);
        }
    }

    public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
            _adds.Add(new StructuralAddOperation<T>(entity, value));
    }

    public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
    {
        if (entities.Length != values.Length)
            throw new ArgumentException("Entities and values length must match.");

        lock (_sync)
        {
            for (int i = 0; i < entities.Length; i++)
                _adds.Add(new StructuralAddOperation<T>(entities[i], values[i]));
        }
    }

    public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
        {
            for (int i = 0; i < entities.Length; i++)
                _adds.Add(new StructuralAddOperation<T>(entities[i], value));
        }
    }

    public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
            _removes.Add(new StructuralRemoveOperation<T>(entity));
    }

    public void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
    {
        lock (_sync)
        {
            for (int i = 0; i < entities.Length; i++)
                _removes.Add(new StructuralRemoveOperation<T>(entities[i]));
        }
    }

    public void Apply(World world)
    {
        ArchetypeSignature[] createDefaults;
        Entity[] destroys;
        IStructuralAddOperation[] adds;
        IStructuralRemoveOperation[] removes;

        lock (_sync)
        {
            if (_createDefaults.Count == 0 && _destroys.Count == 0 && _adds.Count == 0 && _removes.Count == 0)
                return;

            createDefaults = _createDefaults.ToArray();
            destroys = _destroys.ToArray();
            adds = _adds.ToArray();
            removes = _removes.ToArray();

            _createDefaults.Clear();
            _destroys.Clear();
            _adds.Clear();
            _removes.Clear();
        }

        foreach (Entity entity in destroys)
            world.DestroyImmediate(entity);

        foreach (ArchetypeSignature signature in createDefaults)
            world.CreateDefaultImmediate(signature);

        foreach (IStructuralRemoveOperation operation in removes)
            operation.Apply(world);

        foreach (IStructuralAddOperation operation in adds)
            operation.Apply(world);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _createDefaults.Clear();
            _destroys.Clear();
            _adds.Clear();
            _removes.Clear();
        }
    }

    private interface IStructuralAddOperation
    {
        void Apply(World world);
    }

    private interface IStructuralRemoveOperation
    {
        void Apply(World world);
    }

    private sealed class StructuralAddOperation<T> : IStructuralAddOperation
        where T : unmanaged, IComponent<T>
    {
        private readonly Entity _entity;
        private readonly T _value;

        public StructuralAddOperation(Entity entity, T value)
        {
            _entity = entity;
            _value = value;
        }

        public void Apply(World world) => world.AddComponentImmediate(_entity, _value);
    }

    private sealed class StructuralRemoveOperation<T> : IStructuralRemoveOperation
        where T : unmanaged, IComponent<T>
    {
        private readonly Entity _entity;

        public StructuralRemoveOperation(Entity entity)
        {
            _entity = entity;
        }

        public void Apply(World world) => world.RemoveComponentImmediate<T>(_entity);
    }
}
