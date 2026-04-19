namespace UltraSim.ECS;

public sealed partial class World
{
    private sealed class WorldStructuralCommands : IStructuralCommands
    {
        private readonly World _world;

        public WorldStructuralCommands(World world)
        {
            _world = world;
        }

        public void EnqueueCreateDefault(ArchetypeSignature signature) => _world.EnqueueCreateDefault(signature);
        public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures) => _world.EnqueueCreateDefault(signatures);
        public void EnqueueDestroy(Entity entity) => _world.EnqueueDestroy(entity);
        public void EnqueueDestroy(ReadOnlySpan<Entity> entities) => _world.EnqueueDestroy(entities);
        public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T> => _world.EnqueueAdd(entity, value);
        public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T> => _world.EnqueueRemove<T>(entity);
    }

    private readonly StructuralQueue _structural;
    private readonly DirtyTracker _dirty;
    private readonly IStructuralCommands _commands;
    private readonly SystemManager _systems;
    private uint _worldTick;

    public World(EcsSchema schema, EcsWorldServices? services = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        Services = services ?? new EcsWorldServices();
        EntityStore = new EntityManager();
        Archetypes = new ArchetypeManager(schema);
        _structural = new StructuralQueue();
        _dirty = new DirtyTracker();
        _commands = new WorldStructuralCommands(this);
        _systems = new SystemManager();
    }

    public EcsSchema Schema { get; }
    public EcsWorldServices Services { get; }
    public IEntityReadOnly Entities => EntityStore;
    public IStructuralCommands Commands => _commands;
    public uint WorldTick => _worldTick;
    public int SystemCount => _systems.Count;
    public SystemExecutionMode SystemExecutionMode => _systems.ExecutionMode;

    internal EntityManager EntityStore { get; }
    internal ArchetypeManager Archetypes { get; }

    public ComponentId GetComponentId<T>() where T : unmanaged, IComponent<T>
        => Schema.GetComponentId<T>();

    public Entity CreateDefault(ArchetypeSignature signature)
        => CreateDefaultImmediate(signature);

    public void EnqueueCreateDefault(ArchetypeSignature signature) => _structural.EnqueueCreateDefault(signature);

    public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures) => _structural.EnqueueCreateDefault(signatures);

    public void EnqueueDestroy(Entity entity) => _structural.EnqueueDestroy(entity);

    public void EnqueueDestroy(ReadOnlySpan<Entity> entities) => _structural.EnqueueDestroy(entities);

    public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
        => _structural.EnqueueAdd(entity, value);

    public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
        => _structural.EnqueueRemove<T>(entity);

    public void ProcessStructuralQueues(StructuralBarrierPhase phase = StructuralBarrierPhase.Manual)
    {
        _ = phase;

        if (!_structural.HasPending)
            return;

        _structural.Apply(this);
    }

    public void Destroy(Entity entity) => DestroyImmediate(entity);

    public bool HasComponent<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        return EntityStore.TryGetLocation(entity, out Archetype archetype, out _) && archetype.HasComponent(id);
    }

    public bool TryGetComponent<T>(Entity entity, out T value) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot) || !archetype.HasComponent(id))
        {
            value = default;
            return false;
        }

        value = archetype.GetComponentSpan<T>(id)[slot];
        return true;
    }

    public bool SetComponent<T>(Entity entity, in T value) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot) || !archetype.HasComponent(id))
            return false;

        archetype.GetComponentSpan<T>(id)[slot] = value;
        _dirty.Mark(id, entity);
        return true;
    }

    public int CopyDirtyTo(Span<Entity> destination) => _dirty.CopyAllTo(destination);

    public int DrainDirtyTo(Span<Entity> destination) => _dirty.CopyAndClearAllTo(destination);

    public int CopyDirtyTo<T>(Span<Entity> destination) where T : unmanaged, IComponent<T>
        => _dirty.CopyComponentTo(Schema.GetComponentId<T>(), destination);

    public int DrainDirtyTo<T>(Span<Entity> destination) where T : unmanaged, IComponent<T>
        => _dirty.CopyAndClearComponentTo(Schema.GetComponentId<T>(), destination);

    public void RegisterSystem(SystemBase system) => _systems.Register(system);

    public void RegisterSystems(params SystemBase[] systems) => _systems.RegisterAll(systems);

    public void SealSystems() => _systems.Seal();

    public void SetSystemExecutionMode(SystemExecutionMode mode) => _systems.SetExecutionMode(mode);

    public void ConfigurePhaseStaggering(bool enabled, int maxNonCriticalStagger = 10)
        => _systems.ConfigurePhaseStaggering(enabled, maxNonCriticalStagger);

    public TSystem? GetSystem<TSystem>() where TSystem : SystemBase
        => (TSystem?)_systems.GetSystem<TSystem>();

    public void Tick(float deltaSeconds)
    {
        ProcessStructuralQueues(StructuralBarrierPhase.PreSystems);
        _systems.Tick(this, _worldTick, deltaSeconds, _commands);
        ProcessStructuralQueues(StructuralBarrierPhase.PostSystems);
        _worldTick++;
    }

    public void RunManualSystem(SystemId id, float deltaSeconds)
        => _systems.RunManual(id, this, _worldTick, deltaSeconds, _commands);

    public void RunManualSystem<TSystem>(float deltaSeconds) where TSystem : SystemBase
        => _systems.RunManual<TSystem>(this, _worldTick, deltaSeconds, _commands);

    public int[] GetSystemStartOffsetsSnapshot()
    {
        var offsets = new int[_systems.Count];
        _systems.CopyStartOffsets(offsets);
        return offsets;
    }

    internal SystemId[][] GetStageBatchesSnapshot(SystemStage stage) => _systems.GetStageBatchesSnapshot(stage);

    internal Entity CreateDefaultImmediate(ArchetypeSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        Archetype archetype = Archetypes.GetOrCreate(signature);
        Entity entity = EntityStore.Allocate();
        int slot = archetype.AddEntity(entity);
        EntityStore.SetLocation(entity, archetype, slot);
        _dirty.Mark(entity);
        return entity;
    }

    internal void DestroyImmediate(Entity entity)
    {
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            return;

        if (archetype.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, archetype, movedSlot);

        EntityStore.Free(entity);
        _dirty.Mark(entity);
    }

    internal bool AddComponentImmediate<T>(Entity entity, in T value) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype source, out int slot))
            return false;

        if (source.HasComponent(id))
        {
            source.GetComponentSpan<T>(id)[slot] = value;
            _dirty.Mark(id, entity);
            return true;
        }

        Archetype destination = Archetypes.GetOrCreate(CreateTransitionSignature(source.ComponentIds, id, remove: false));
        int newSlot = destination.AddEntity(entity);
        CopySharedComponents(source, slot, destination, newSlot, skip: ComponentId.Invalid);
        destination.GetComponentSpan<T>(id)[newSlot] = value;

        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.SetLocation(entity, destination, newSlot);
        _dirty.Mark(id, entity);
        return true;
    }

    internal bool RemoveComponentImmediate<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype source, out int slot) || !source.HasComponent(id))
            return false;

        Archetype destination = Archetypes.GetOrCreate(CreateTransitionSignature(source.ComponentIds, id, remove: true));
        int newSlot = destination.AddEntity(entity);
        CopySharedComponents(source, slot, destination, newSlot, skip: id);

        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.SetLocation(entity, destination, newSlot);
        _dirty.Mark(id, entity);
        return true;
    }

    private static ComponentId[] CreateTransitionSignature(ReadOnlySpan<ComponentId> source, ComponentId target, bool remove)
    {
        int delta = remove ? -1 : 1;
        var buffer = new ComponentId[source.Length + delta];
        int write = 0;
        bool inserted = false;

        for (int i = 0; i < source.Length; i++)
        {
            ComponentId current = source[i];

            if (remove && current == target)
                continue;

            if (!remove && !inserted && target.Value < current.Value)
            {
                buffer[write++] = target;
                inserted = true;
            }

            buffer[write++] = current;
        }

        if (!remove && !inserted)
            buffer[write++] = target;

        if (write != buffer.Length)
            Array.Resize(ref buffer, write);

        return buffer;
    }

    private static void CopySharedComponents(Archetype source, int sourceSlot, Archetype destination, int destinationSlot, ComponentId skip)
    {
        ReadOnlySpan<ComponentId> sourceIds = source.ComponentIds;
        for (int i = 0; i < sourceIds.Length; i++)
        {
            ComponentId id = sourceIds[i];
            if (id == skip)
                continue;

            int destinationIndex = destination.GetColumnIndex(id);
            if (destinationIndex < 0)
                continue;

            int sourceIndex = source.GetColumnIndex(id);
            destination.GetColumnAt(destinationIndex).CopyFrom(source.GetColumnAt(sourceIndex), sourceSlot, destinationSlot);
        }
    }
}
