namespace UltraSim.ECS;

using System.Buffers;
using System.Diagnostics;

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
        public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T> => _world.EnqueueAdd(entities, values);
        public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T> => _world.EnqueueAdd(entities, value);
        public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T> => _world.EnqueueRemove<T>(entity);
        public void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T> => _world.EnqueueRemove<T>(entities);
    }

    private readonly StructuralQueue _structural;
    private readonly DirtyTracker _dirty;
    private readonly IStructuralCommands _commands;
    private readonly SystemManager _systems;
    private readonly Dictionary<TransitionKey, TransitionPlan> _addTransitions;
    private readonly Dictionary<TransitionKey, TransitionPlan> _removeTransitions;
    private uint _worldTick;
    private WorldLifecycleState _state;
    private WorldProfileSnapshot _lastProfile;

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
        _addTransitions = new Dictionary<TransitionKey, TransitionPlan>(16);
        _removeTransitions = new Dictionary<TransitionKey, TransitionPlan>(16);
        _queryCacheVersion = Archetypes.Version;
        _state = WorldLifecycleState.Created;
    }

    public EcsSchema Schema { get; }
    public EcsWorldServices Services { get; }
    public IEntityReadOnly Entities => EntityStore;
    public IStructuralCommands Commands => _commands;
    public uint WorldTick => _worldTick;
    public WorldLifecycleState State => _state;
    public int SystemCount => _systems.Count;
    public SystemExecutionMode SystemExecutionMode => _systems.ExecutionMode;
    public bool IsSealed => _state >= WorldLifecycleState.Sealed;
    public bool IsInitialized => _state >= WorldLifecycleState.Initialized && _state < WorldLifecycleState.Shutdown;
    public bool ProfilingEnabled { get; set; }
    public WorldProfileSnapshot LastProfile => _lastProfile;

    internal EntityManager EntityStore { get; }
    internal ArchetypeManager Archetypes { get; }

    public ComponentId GetComponentId<T>() where T : unmanaged, IComponent<T>
        => Schema.GetComponentId<T>();

    public Entity CreateDefault(ArchetypeSignature signature)
    {
        EnsureNotShutdown();
        return CreateDefaultImmediate(signature);
    }

    public void CreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
    {
        EnqueueCreateDefault(signatures);
    }

    public void EnqueueCreateDefault(ArchetypeSignature signature)
    {
        EnsureNotShutdown();
        _structural.EnqueueCreateDefault(signature);
    }

    public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
    {
        EnsureNotShutdown();
        _structural.EnqueueCreateDefault(signatures);
    }

    public void EnqueueDestroy(Entity entity)
    {
        EnsureNotShutdown();
        _structural.EnqueueDestroy(entity);
    }

    public void EnqueueDestroy(ReadOnlySpan<Entity> entities)
    {
        EnsureNotShutdown();
        _structural.EnqueueDestroy(entities);
    }

    public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        _structural.EnqueueAdd(entity, value);
    }

    public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        _structural.EnqueueAdd(entities, values);
    }

    public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        _structural.EnqueueAdd(entities, value);
    }

    public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        _structural.EnqueueRemove<T>(entity);
    }

    public void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        _structural.EnqueueRemove<T>(entities);
    }

    public void Seal()
    {
        EnsureNotShutdown();

        if (_state >= WorldLifecycleState.Sealed)
            return;

        _systems.Seal();
        _state = WorldLifecycleState.Sealed;
    }

    public void Initialize()
    {
        EnsureNotShutdown();

        if (_state < WorldLifecycleState.Sealed)
            Seal();

        if (_state >= WorldLifecycleState.Initialized)
            return;

        _systems.Initialize(this);
        _state = WorldLifecycleState.Initialized;
    }

    public void Shutdown()
    {
        if (_state == WorldLifecycleState.Shutdown)
            return;

        if (_state >= WorldLifecycleState.Initialized)
            _systems.Shutdown(this);

        _state = WorldLifecycleState.Shutdown;
    }

    public void SaveAll(IDataWriter writer)
    {
        EnsureNotShutdown();
        _systems.SaveAllSystems(writer);
    }

    public void LoadAll(IDataReader reader)
    {
        EnsureNotShutdown();
        _systems.LoadAllSystems(reader);
    }

    public void Save(IDataWriter writer)
    {
        EnsureNotShutdown();

        IEcsPersistenceAdapter adapter = Services.PersistenceAdapter
            ?? throw new InvalidOperationException("No persistence adapter is configured for this world.");

        adapter.Save(this, writer);
    }

    public void Load(IDataReader reader)
    {
        EnsureNotShutdown();

        IEcsPersistenceAdapter adapter = Services.PersistenceAdapter
            ?? throw new InvalidOperationException("No persistence adapter is configured for this world.");

        adapter.Load(this, reader);
    }

    public void ProcessStructuralQueues(StructuralBarrierPhase phase = StructuralBarrierPhase.Manual)
    {
        EnsureNotShutdown();
        _ = phase;

        if (!_structural.HasPending)
            return;

        _ = _structural.Apply(this);
    }

    public void Destroy(Entity entity)
    {
        EnsureNotShutdown();
        DestroyImmediate(entity);
    }

    public void Destroy(ReadOnlySpan<Entity> entities)
    {
        EnqueueDestroy(entities);
    }

    public bool HasComponent<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        return EntityStore.TryGetLocation(entity, out Archetype archetype, out _) && archetype.HasComponent(id);
    }

    public T GetComponent<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();

        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            throw new InvalidOperationException($"Entity not alive: {entity}");

        int columnIndex = archetype.GetColumnIndex(id);
        if (columnIndex < 0)
            throw new InvalidOperationException($"Entity {entity} does not contain component {typeof(T).Name}.");

        return archetype.GetComponentSpanAt<T>(columnIndex)[slot];
    }

    public bool TryGetComponent<T>(Entity entity, out T value) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
        {
            value = default;
            return false;
        }

        int columnIndex = archetype.GetColumnIndex(id);
        if (columnIndex < 0)
        {
            value = default;
            return false;
        }

        value = archetype.GetComponentSpanAt<T>(columnIndex)[slot];
        return true;
    }

    public bool PatchComponent<T>(Entity entity, ComponentUpdater<T> updater) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        ArgumentNullException.ThrowIfNull(updater);

        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            return false;

        int columnIndex = archetype.GetColumnIndex(id);
        if (columnIndex < 0)
            return false;

        Span<T> values = archetype.GetComponentSpanAt<T>(columnIndex);
        updater(ref values[slot]);
        _dirty.Mark(id, entity);
        return true;
    }

    public bool SetComponent<T>(Entity entity, in T value) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            return false;

        int columnIndex = archetype.GetColumnIndex(id);
        if (columnIndex < 0)
            return false;

        archetype.GetComponentSpanAt<T>(columnIndex)[slot] = value;
        _dirty.Mark(id, entity);
        return true;
    }

    public void SetOrAddComponent<T>(Entity entity, in T value) where T : unmanaged, IComponent<T>
    {
        EnsureNotShutdown();

        if (!SetComponent(entity, value))
            EnqueueAdd(entity, value);
    }

    public void RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        EnqueueRemove<T>(entity);
    }

    public void RemoveComponent<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
    {
        EnqueueRemove<T>(entities);
    }

    public int CopyDirtyTo(Span<Entity> destination) => _dirty.CopyAllTo(destination);

    public int DrainDirtyTo(Span<Entity> destination) => _dirty.CopyAndClearAllTo(destination);

    public int CopyDirtyTo<T>(Span<Entity> destination) where T : unmanaged, IComponent<T>
        => _dirty.CopyComponentTo(Schema.GetComponentId<T>(), destination);

    public int DrainDirtyTo<T>(Span<Entity> destination) where T : unmanaged, IComponent<T>
        => _dirty.CopyAndClearComponentTo(Schema.GetComponentId<T>(), destination);

    public void RegisterSystem(SystemBase system)
    {
        EnsureCanMutateConfiguration();
        _systems.Register(system);
    }

    public void RegisterSystems(params SystemBase[] systems)
    {
        EnsureCanMutateConfiguration();
        _systems.RegisterAll(systems);
    }

    public void SealSystems() => Seal();

    public void SetSystemExecutionMode(SystemExecutionMode mode)
    {
        EnsureCanMutateConfiguration();
        _systems.SetExecutionMode(mode);
    }

    public void ConfigurePhaseStaggering(bool enabled, int maxNonCriticalStagger = 10)
    {
        EnsureCanMutateConfiguration();
        _systems.ConfigurePhaseStaggering(enabled, maxNonCriticalStagger);
    }

    public void ConfigureIntraSystemParallelInSerialMode(bool enabled)
    {
        EnsureCanMutateConfiguration();
        _systems.ConfigureIntraSystemParallelInSerialMode(enabled);
    }

    public TSystem? GetSystem<TSystem>() where TSystem : SystemBase
        => (TSystem?)_systems.GetSystem<TSystem>();

    public void Tick(float deltaSeconds)
    {
        EnsureReadyForExecution();
        StructuralApplyStats preSystems = default;
        StructuralApplyStats postSystems = default;
        long preTicks = 0;
        long systemTicks = 0;
        long postTicks = 0;

        if (ProfilingEnabled)
        {
            long start = Stopwatch.GetTimestamp();
            preSystems = _structural.Apply(this);
            preTicks = Stopwatch.GetTimestamp() - start;

            int dirtyCaptureWorkers = _systems.DirtyCaptureWorkerCount;
            _dirty.BeginCapture(dirtyCaptureWorkers);
            try
            {
                start = Stopwatch.GetTimestamp();
                _systems.Tick(this, _worldTick, deltaSeconds, _commands);
                systemTicks = Stopwatch.GetTimestamp() - start;
            }
            finally
            {
                _dirty.EndCapture();
            }

            start = Stopwatch.GetTimestamp();
            postSystems = _structural.Apply(this);
            postTicks = Stopwatch.GetTimestamp() - start;

            _lastProfile = new WorldProfileSnapshot(_worldTick, preSystems, postSystems, preTicks, systemTicks, postTicks);
        }
        else
        {
            ProcessStructuralQueues(StructuralBarrierPhase.PreSystems);
            int dirtyCaptureWorkers = _systems.DirtyCaptureWorkerCount;
            _dirty.BeginCapture(dirtyCaptureWorkers);
            try
            {
                _systems.Tick(this, _worldTick, deltaSeconds, _commands);
            }
            finally
            {
                _dirty.EndCapture();
            }

            ProcessStructuralQueues(StructuralBarrierPhase.PostSystems);
        }

        _worldTick++;
    }

    public void RunManualSystem(SystemId id, float deltaSeconds)
    {
        EnsureReadyForExecution();
        _systems.RunManual(id, this, _worldTick, deltaSeconds, _commands);
    }

    public void RunManualSystem<TSystem>(float deltaSeconds) where TSystem : SystemBase
    {
        EnsureReadyForExecution();
        _systems.RunManual<TSystem>(this, _worldTick, deltaSeconds, _commands);
    }

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

    internal int CreateDefaultImmediateBatch(ReadOnlySpan<ArchetypeSignature> signatures)
    {
        if (signatures.Length == 0)
            return 0;

        if (TryCreateDefaultSingleSignatureFast(signatures, out int createdFast))
            return createdFast;

        var reserveCounts = new Dictionary<Archetype, int>(8);
        for (int i = 0; i < signatures.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(signatures[i]);
            Archetype archetype = Archetypes.GetOrCreate(signatures[i]);
            reserveCounts[archetype] = reserveCounts.TryGetValue(archetype, out int count) ? count + 1 : 1;
        }

        foreach ((Archetype archetype, int count) in reserveCounts)
            archetype.Reserve(count);

        foreach ((Archetype archetype, int count) in reserveCounts)
        {
            Entity[] rented = ArrayPool<Entity>.Shared.Rent(count);
            try
            {
                Span<Entity> entities = rented.AsSpan(0, count);
                int allocated = EntityStore.AllocateBulk(entities);
                int startSlot = archetype.AddEntitiesUninitialized(entities[..allocated]);
                EntityStore.SetLocationsBulkAssumeValid(entities[..allocated], archetype, startSlot);
                _dirty.MarkUnique(entities[..allocated]);
            }
            finally
            {
                ArrayPool<Entity>.Shared.Return(rented, clearArray: false);
            }
        }

        return signatures.Length;
    }

    private bool TryCreateDefaultSingleSignatureFast(ReadOnlySpan<ArchetypeSignature> signatures, out int created)
    {
        created = 0;
        if (signatures.Length == 0)
            return true;

        ArchetypeSignature first = signatures[0] ?? throw new ArgumentNullException(nameof(signatures));
        for (int i = 1; i < signatures.Length; i++)
        {
            ArchetypeSignature current = signatures[i] ?? throw new ArgumentNullException(nameof(signatures));
            if (!ReferenceEquals(current, first) && current != first)
                return false;
        }

        Archetype archetype = Archetypes.GetOrCreate(first);
        archetype.Reserve(signatures.Length);

        Entity[] rented = ArrayPool<Entity>.Shared.Rent(signatures.Length);
        try
        {
            Span<Entity> entities = rented.AsSpan(0, signatures.Length);
            int allocated = EntityStore.AllocateBulk(entities);
            int startSlot = archetype.AddEntitiesUninitialized(entities[..allocated]);
            EntityStore.SetLocationsBulkAssumeValid(entities[..allocated], archetype, startSlot);
            _dirty.MarkUnique(entities[..allocated]);
            created = allocated;
            return true;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(rented, clearArray: false);
        }
    }

    internal void DestroyImmediate(Entity entity)
    {
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
            return;

        if (archetype.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, archetype, movedSlot);

        EntityStore.FreeAssumeAlive(entity);
        _dirty.Mark(entity);
    }

    internal int DestroyImmediateBatch(ReadOnlySpan<Entity> entities)
    {
        if (entities.Length == 0)
            return 0;

        if (TryDestroyWholeArchetypeFast(entities, out int fullDestroyed))
            return fullDestroyed;

        if (TryDestroySingleSourceFast(entities, out int fastDestroyed))
            return fastDestroyed;

        Entity[] dirtyRented = ArrayPool<Entity>.Shared.Rent(entities.Length);
        int destroyed = 0;

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out int slot))
                    continue;

                if (archetype.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
                    EntityStore.SetLocation(moved, archetype, movedSlot);

                EntityStore.FreeAssumeAlive(entity);
                dirtyRented[destroyed++] = entity;
            }

            _dirty.MarkUnique(dirtyRented.AsSpan(0, destroyed));
            return destroyed;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(dirtyRented, clearArray: false);
        }
    }

    private bool TryDestroyWholeArchetypeFast(ReadOnlySpan<Entity> entities, out int destroyed)
    {
        destroyed = 0;
        if (entities.Length == 0)
            return true;

        if (!EntityStore.TryGetLocation(entities[0], out Archetype source, out int slot) || slot != 0)
            return false;

        ReadOnlySpan<Entity> current = source.GetEntitySpan();
        if (current.Length != entities.Length)
            return false;

        for (int i = 0; i < entities.Length; i++)
        {
            if (current[i] != entities[i])
                return false;
        }

        for (int i = 0; i < entities.Length; i++)
            EntityStore.FreeAssumeAlive(entities[i]);

        source.ClearEntities();
        _dirty.MarkUnique(entities);
        destroyed = entities.Length;
        return true;
    }

    private bool TryDestroySingleSourceFast(ReadOnlySpan<Entity> entities, out int destroyed)
    {
        destroyed = 0;
        if (entities.Length == 0)
            return true;

        int[] rentedSlots = ArrayPool<int>.Shared.Rent(entities.Length);
        try
        {
            Archetype? source = null;
            bool increasing = true;
            bool decreasing = true;
            int previousSlot = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityStore.TryGetLocation(entity, out Archetype currentSource, out int slot))
                    return false;

                if (source is null)
                {
                    source = currentSource;
                }
                else if (!ReferenceEquals(source, currentSource))
                {
                    return false;
                }

                rentedSlots[i] = slot;
                if (i > 0)
                {
                    if (slot <= previousSlot)
                        increasing = false;
                    if (slot >= previousSlot)
                        decreasing = false;
                }

                previousSlot = slot;
            }

            if (!increasing && !decreasing)
                return false;

            if (increasing)
            {
                for (int i = entities.Length - 1; i >= 0; i--)
                    ApplyDestroyFastEntry(entities[i], rentedSlots[i], source!, ref destroyed);
            }
            else
            {
                for (int i = 0; i < entities.Length; i++)
                    ApplyDestroyFastEntry(entities[i], rentedSlots[i], source!, ref destroyed);
            }

            _dirty.MarkUnique(entities);
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedSlots, clearArray: false);
        }
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

        TransitionPlan plan = GetAddTransition(source, id);
        Archetype destination = plan.Destination;
        int newSlot = destination.AddEntity(entity);
        plan.Copy(sourceSlot: slot, destinationSlot: newSlot);
        destination.GetComponentSpan<T>(id)[newSlot] = value;

        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.SetLocation(entity, destination, newSlot);
        _dirty.Mark(id, entity);
        return true;
    }

    internal int AddComponentImmediateBatch<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
    {
        if (entities.Length != values.Length)
            throw new ArgumentException("Entities and values length must match.");

        ComponentId id = Schema.GetComponentId<T>();
        if (TryAddComponentSingleSourceFast(entities, values, id, out int fastApplied))
            return fastApplied;

        var reserveCounts = new Dictionary<Archetype, int>(8);

        for (int i = 0; i < entities.Length; i++)
        {
            if (!EntityStore.TryGetLocation(entities[i], out Archetype source, out _) || source.HasComponent(id))
                continue;

            Archetype destination = GetAddTransition(source, id).Destination;
            reserveCounts[destination] = reserveCounts.TryGetValue(destination, out int count) ? count + 1 : 1;
        }

        foreach ((Archetype archetype, int count) in reserveCounts)
            archetype.Reserve(count);

        int applied = 0;
        Archetype? cachedSource = null;
        bool cachedHasComponent = false;
        bool cachedPlanReady = false;
        TransitionPlan cachedPlan = default;
        Span<T> cachedSourceValues = default;
        Entity[] dirtyRented = ArrayPool<Entity>.Shared.Rent(entities.Length);
        int dirtyCount = 0;

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityStore.TryGetLocation(entity, out Archetype source, out int slot))
                    continue;

                if (!ReferenceEquals(source, cachedSource))
                {
                    cachedSource = source;
                    cachedHasComponent = source.HasComponent(id);
                    cachedPlanReady = false;
                    cachedSourceValues = cachedHasComponent ? source.GetComponentSpan<T>(id) : default;
                }

                if (cachedHasComponent)
                {
                    cachedSourceValues[slot] = values[i];
                    dirtyRented[dirtyCount++] = entity;
                    applied++;
                    continue;
                }

                if (!cachedPlanReady)
                {
                    cachedPlan = GetAddTransition(source, id);
                    cachedPlanReady = true;
                }

                Archetype destination = cachedPlan.Destination;
                int newSlot = destination.AddEntityUninitializedAssumeCapacity(entity);
                cachedPlan.Copy(slot, newSlot);
                destination.GetComponentSpan<T>(id)[newSlot] = values[i];

                if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
                    EntityStore.SetLocation(moved, source, movedSlot);

                EntityStore.SetLocation(entity, destination, newSlot);
                dirtyRented[dirtyCount++] = entity;
                applied++;
            }

            _dirty.MarkUnique(id, dirtyRented.AsSpan(0, dirtyCount));
            return applied;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(dirtyRented, clearArray: false);
        }
    }

    internal bool RemoveComponentImmediate<T>(Entity entity) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (!EntityStore.TryGetLocation(entity, out Archetype source, out int slot) || !source.HasComponent(id))
            return false;

        TransitionPlan plan = GetRemoveTransition(source, id);
        Archetype destination = plan.Destination;
        int newSlot = destination.AddEntity(entity);
        plan.Copy(sourceSlot: slot, destinationSlot: newSlot);

        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.SetLocation(entity, destination, newSlot);
        _dirty.Mark(id, entity);
        return true;
    }

    internal int RemoveComponentImmediateBatch<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
    {
        ComponentId id = Schema.GetComponentId<T>();
        if (TryRemoveComponentSingleSourceFast<T>(entities, id, out int fastApplied))
            return fastApplied;

        var reserveCounts = new Dictionary<Archetype, int>(8);

        for (int i = 0; i < entities.Length; i++)
        {
            if (!EntityStore.TryGetLocation(entities[i], out Archetype source, out _) || !source.HasComponent(id))
                continue;

            Archetype destination = GetRemoveTransition(source, id).Destination;
            reserveCounts[destination] = reserveCounts.TryGetValue(destination, out int count) ? count + 1 : 1;
        }

        foreach ((Archetype archetype, int count) in reserveCounts)
            archetype.Reserve(count);

        int applied = 0;
        Archetype? cachedSource = null;
        bool cachedHasComponent = false;
        bool cachedPlanReady = false;
        TransitionPlan cachedPlan = default;
        Entity[] dirtyRented = ArrayPool<Entity>.Shared.Rent(entities.Length);
        int dirtyCount = 0;

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityStore.TryGetLocation(entity, out Archetype source, out int slot))
                    continue;

                if (!ReferenceEquals(source, cachedSource))
                {
                    cachedSource = source;
                    cachedHasComponent = source.HasComponent(id);
                    cachedPlanReady = false;
                }

                if (!cachedHasComponent)
                    continue;

                if (!cachedPlanReady)
                {
                    cachedPlan = GetRemoveTransition(source, id);
                    cachedPlanReady = true;
                }

                Archetype destination = cachedPlan.Destination;
                int newSlot = destination.AddEntityUninitializedAssumeCapacity(entity);
                cachedPlan.Copy(slot, newSlot);

                if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
                    EntityStore.SetLocation(moved, source, movedSlot);

                EntityStore.SetLocation(entity, destination, newSlot);
                dirtyRented[dirtyCount++] = entity;
                applied++;
            }

            _dirty.MarkUnique(id, dirtyRented.AsSpan(0, dirtyCount));
            return applied;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(dirtyRented, clearArray: false);
        }
    }

    private TransitionPlan GetAddTransition(Archetype source, ComponentId componentId)
    {
        var key = new TransitionKey(source, componentId);
        if (_addTransitions.TryGetValue(key, out TransitionPlan plan))
            return plan;

        Archetype destination = GetOrCreateTransitionArchetype(source.ComponentIds, componentId, remove: false);
        plan = BuildTransitionPlan(source, destination, ComponentId.Invalid);
        _addTransitions.Add(key, plan);
        return plan;
    }

    private TransitionPlan GetRemoveTransition(Archetype source, ComponentId componentId)
    {
        var key = new TransitionKey(source, componentId);
        if (_removeTransitions.TryGetValue(key, out TransitionPlan plan))
            return plan;

        Archetype destination = GetOrCreateTransitionArchetype(source.ComponentIds, componentId, remove: true);
        plan = BuildTransitionPlan(source, destination, componentId);
        _removeTransitions.Add(key, plan);
        return plan;
    }

    private Archetype GetOrCreateTransitionArchetype(ReadOnlySpan<ComponentId> source, ComponentId target, bool remove)
    {
        int needed = source.Length + (remove ? -1 : 1);
        if (needed <= 0)
            return Archetypes.GetOrCreateEmpty();

        const int stackThreshold = 32;
        if (needed <= stackThreshold)
        {
            Span<ComponentId> buffer = stackalloc ComponentId[stackThreshold];
            int write = WriteTransitionSignature(source, target, remove, buffer);
            return Archetypes.GetOrCreate(buffer[..write]);
        }

        ComponentId[] rented = ArrayPool<ComponentId>.Shared.Rent(needed);
        try
        {
            int write = WriteTransitionSignature(source, target, remove, rented);
            return Archetypes.GetOrCreate(rented.AsSpan(0, write));
        }
        finally
        {
            ArrayPool<ComponentId>.Shared.Return(rented);
        }
    }

    private static int WriteTransitionSignature(ReadOnlySpan<ComponentId> source, ComponentId target, bool remove, Span<ComponentId> destination)
    {
        int write = 0;
        bool inserted = false;

        for (int i = 0; i < source.Length; i++)
        {
            ComponentId current = source[i];

            if (remove && current == target)
                continue;

            if (!remove && !inserted && target.Value < current.Value)
            {
                destination[write++] = target;
                inserted = true;
            }

            destination[write++] = current;
        }

        if (!remove && !inserted)
            destination[write++] = target;

        return write;
    }

    private bool TryAddComponentSingleSourceFast<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values, ComponentId id, out int applied)
        where T : unmanaged, IComponent<T>
    {
        applied = 0;
        if (entities.Length == 0)
            return true;

        int[] rentedSlots = ArrayPool<int>.Shared.Rent(entities.Length);
        try
        {
            Archetype? source = null;
            bool increasing = true;
            bool decreasing = true;
            int previousSlot = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityStore.TryGetLocation(entity, out Archetype currentSource, out int slot))
                    return false;

                if (currentSource.HasComponent(id))
                    return false;

                if (source is null)
                {
                    source = currentSource;
                }
                else if (!ReferenceEquals(source, currentSource))
                {
                    return false;
                }

                rentedSlots[i] = slot;
                if (i > 0)
                {
                    if (slot <= previousSlot)
                        increasing = false;
                    if (slot >= previousSlot)
                        decreasing = false;
                }

                previousSlot = slot;
            }

            if (!increasing && !decreasing)
                return false;

            TransitionPlan plan = GetAddTransition(source!, id);
            Archetype destination = plan.Destination;
            destination.Reserve(entities.Length);

            if (increasing)
            {
                for (int i = entities.Length - 1; i >= 0; i--)
                    ApplyAddFastEntry(entities[i], values[i], rentedSlots[i], source!, destination, plan, ref applied);
            }
            else
            {
                for (int i = 0; i < entities.Length; i++)
                    ApplyAddFastEntry(entities[i], values[i], rentedSlots[i], source!, destination, plan, ref applied);
            }

            _dirty.MarkUnique(id, entities);
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedSlots, clearArray: false);
        }
    }

    private void ApplyAddFastEntry<T>(
        Entity entity,
        T value,
        int slot,
        Archetype source,
        Archetype destination,
        TransitionPlan plan,
        ref int applied)
        where T : unmanaged, IComponent<T>
    {
        int newSlot = destination.AddEntityUninitializedAssumeCapacity(entity);
        plan.Copy(slot, newSlot);
        destination.GetComponentSpan<T>(Schema.GetComponentId<T>())[newSlot] = value;

        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.SetLocation(entity, destination, newSlot);
        applied++;
    }

    private void ApplyDestroyFastEntry(
        Entity entity,
        int slot,
        Archetype source,
        ref int destroyed)
    {
        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.FreeAssumeAlive(entity);
        destroyed++;
    }

    private bool TryRemoveComponentSingleSourceFast<T>(ReadOnlySpan<Entity> entities, ComponentId id, out int applied)
        where T : unmanaged, IComponent<T>
    {
        applied = 0;
        if (entities.Length == 0)
            return true;

        int[] rentedSlots = ArrayPool<int>.Shared.Rent(entities.Length);
        try
        {
            Archetype? source = null;
            bool increasing = true;
            bool decreasing = true;
            int previousSlot = -1;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!EntityStore.TryGetLocation(entity, out Archetype currentSource, out int slot))
                    return false;

                if (!currentSource.HasComponent(id))
                    return false;

                if (source is null)
                {
                    source = currentSource;
                }
                else if (!ReferenceEquals(source, currentSource))
                {
                    return false;
                }

                rentedSlots[i] = slot;
                if (i > 0)
                {
                    if (slot <= previousSlot)
                        increasing = false;
                    if (slot >= previousSlot)
                        decreasing = false;
                }

                previousSlot = slot;
            }

            if (!increasing && !decreasing)
                return false;

            TransitionPlan plan = GetRemoveTransition(source!, id);
            Archetype destination = plan.Destination;
            destination.Reserve(entities.Length);

            if (increasing)
            {
                for (int i = entities.Length - 1; i >= 0; i--)
                    ApplyRemoveFastEntry(entities[i], rentedSlots[i], source!, destination, plan, ref applied);
            }
            else
            {
                for (int i = 0; i < entities.Length; i++)
                    ApplyRemoveFastEntry(entities[i], rentedSlots[i], source!, destination, plan, ref applied);
            }

            _dirty.MarkUnique(id, entities);
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedSlots, clearArray: false);
        }
    }

    private void ApplyRemoveFastEntry(
        Entity entity,
        int slot,
        Archetype source,
        Archetype destination,
        TransitionPlan plan,
        ref int applied)
    {
        int newSlot = destination.AddEntityUninitializedAssumeCapacity(entity);
        plan.Copy(slot, newSlot);

        if (source.RemoveAtSwap(slot, out Entity moved, out int movedSlot) && movedSlot >= 0)
            EntityStore.SetLocation(moved, source, movedSlot);

        EntityStore.SetLocation(entity, destination, newSlot);
        applied++;
    }

    private TransitionPlan BuildTransitionPlan(Archetype source, Archetype destination, ComponentId skip)
    {
        ReadOnlySpan<ComponentId> sourceIds = source.ComponentIds;
        int count = 0;

        for (int i = 0; i < sourceIds.Length; i++)
        {
            ComponentId id = sourceIds[i];
            if (id == skip)
                continue;

            if (destination.GetColumnIndex(id) >= 0)
                count++;
        }

        if (count == 0)
            return TransitionPlan.Empty(destination);

        var steps = new TransitionCopyStep[count];
        int write = 0;

        for (int i = 0; i < sourceIds.Length; i++)
        {
            ComponentId id = sourceIds[i];
            if (id == skip)
                continue;

            int destinationIndex = destination.GetColumnIndex(id);
            if (destinationIndex < 0)
                continue;

            int sourceIndex = source.GetColumnIndex(id);
            ComponentTypeInfo info = Schema.GetComponentInfo(id);
            steps[write] = new TransitionCopyStep(
                source.GetColumnAt(sourceIndex),
                destination.GetColumnAt(destinationIndex),
                info.ColumnCopier);
            write++;
        }

        return new TransitionPlan(destination, steps, write);
    }

    private readonly struct TransitionKey : IEquatable<TransitionKey>
    {
        private readonly Archetype _source;
        private readonly int _componentId;

        public TransitionKey(Archetype source, ComponentId componentId)
        {
            _source = source;
            _componentId = componentId.Value;
        }

        public bool Equals(TransitionKey other)
            => ReferenceEquals(_source, other._source) && _componentId == other._componentId;

        public override bool Equals(object? obj)
            => obj is TransitionKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_source, _componentId);
    }


    private readonly struct TransitionPlan
    {
        private readonly TransitionCopyStep[] _steps;
        private readonly int _count;

        public TransitionPlan(Archetype destination, TransitionCopyStep[] steps, int count)
        {
            Destination = destination;
            _steps = steps;
            _count = count;
        }

        public Archetype Destination { get; }

        public static TransitionPlan Empty(Archetype destination)
            => new(destination, Array.Empty<TransitionCopyStep>(), 0);

        public void Copy(int sourceSlot, int destinationSlot)
        {
            for (int i = 0; i < _count; i++)
                _steps[i].Copy(sourceSlot, destinationSlot);
        }
    }

    private readonly struct TransitionCopyStep
    {
        private readonly IComponentColumn _source;
        private readonly IComponentColumn _destination;
        private readonly ComponentColumnCopier _copier;

        public TransitionCopyStep(IComponentColumn source, IComponentColumn destination, ComponentColumnCopier copier)
        {
            _source = source;
            _destination = destination;
            _copier = copier;
        }

        public void Copy(int sourceSlot, int destinationSlot)
            => _copier(_source, _destination, sourceSlot, destinationSlot);
    }

    private void EnsureCanMutateConfiguration()
    {
        EnsureNotShutdown();

        if (_state >= WorldLifecycleState.Sealed)
            throw new InvalidOperationException("World configuration is sealed.");
    }

    private void EnsureReadyForExecution()
    {
        EnsureNotShutdown();

        if (_state < WorldLifecycleState.Initialized)
            Initialize();

        if (_state == WorldLifecycleState.Initialized)
            _state = WorldLifecycleState.Running;
    }

    private void EnsureNotShutdown()
    {
        if (_state == WorldLifecycleState.Shutdown)
            throw new InvalidOperationException("World is shutdown.");
    }
}
