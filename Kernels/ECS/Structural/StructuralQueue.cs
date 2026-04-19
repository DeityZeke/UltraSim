namespace UltraSim.ECS;

internal sealed class StructuralQueue
{
    private readonly List<ArchetypeSignature> _createDefaults = new();
    private readonly List<Entity> _destroys = new();
    private readonly List<IStructuralAddOperation> _adds = new();
    private readonly List<IStructuralRemoveOperation> _removes = new();

    public bool HasPending
        => _createDefaults.Count > 0 || _destroys.Count > 0 || _adds.Count > 0 || _removes.Count > 0;

    public void EnqueueCreateDefault(ArchetypeSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        _createDefaults.Add(signature);
    }

    public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
    {
        for (int i = 0; i < signatures.Length; i++)
            EnqueueCreateDefault(signatures[i]);
    }

    public void EnqueueDestroy(Entity entity) => _destroys.Add(entity);

    public void EnqueueDestroy(ReadOnlySpan<Entity> entities)
    {
        for (int i = 0; i < entities.Length; i++)
            _destroys.Add(entities[i]);
    }

    public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
        => _adds.Add(new StructuralAddOperation<T>(entity, value));

    public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
        => _removes.Add(new StructuralRemoveOperation<T>(entity));

    public void Apply(World world)
    {
        foreach (Entity entity in _destroys)
            world.DestroyImmediate(entity);

        foreach (ArchetypeSignature signature in _createDefaults)
            world.CreateDefaultImmediate(signature);

        foreach (IStructuralRemoveOperation operation in _removes)
            operation.Apply(world);

        foreach (IStructuralAddOperation operation in _adds)
            operation.Apply(world);

        Clear();
    }

    public void Clear()
    {
        _createDefaults.Clear();
        _destroys.Clear();
        _adds.Clear();
        _removes.Clear();
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
