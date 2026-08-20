using System;
using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS
{
    public sealed partial class World
    {
        private static class ComponentIdCache<T> where T : unmanaged, IComponent<T>
        {
            public static readonly ComponentId Id = ComponentManager.GetId<T>();
        }

        private static class QueryRequiredCache<T1> where T1 : unmanaged, IComponent<T1>
        {
            public static readonly ComponentId[] Required = new[] { ComponentIdCache<T1>.Id };
        }

        private static class QueryRequiredCache<T1, T2>
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
        {
            public static readonly ComponentId[] Required = new[] { ComponentIdCache<T1>.Id, ComponentIdCache<T2>.Id };
        }

        private static class QueryRequiredCache<T1, T2, T3>
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
            where T3 : unmanaged, IComponent<T3>
        {
            public static readonly ComponentId[] Required = new[]
            {
                ComponentIdCache<T1>.Id,
                ComponentIdCache<T2>.Id,
                ComponentIdCache<T3>.Id,
            };
        }

        private sealed class WorldStructuralCommands : IStructuralCommands
        {
            private readonly World _world;

            public WorldStructuralCommands(World world) => _world = world;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueCreateDefault(ArchetypeSignature signature) => _world.EnqueueCreateDefault(signature);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures) => _world.EnqueueCreateDefault(signatures);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueSpawn(ArchetypeSignature signature, PrefabApply apply) => _world.EnqueueSpawn(signature, apply);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueSpawnTemplate(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData)
                => _world.EnqueueSpawnTemplate(signature, applyTemplate, applyPerInstance, hasPerInstanceData);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueSpawn(PrefabId prefabId) => _world.EnqueueSpawn(prefabId);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueSpawn(PrefabId prefabId, int count) => _world.EnqueueSpawn(prefabId, count);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueSpawn(ReadOnlySpan<PrefabId> prefabIds) => _world.EnqueueSpawn(prefabIds);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueSpawn(string prefabKey) => _world.EnqueueSpawn(prefabKey);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueDestroy(Entity entity) => _world.EnqueueDestroy(entity);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueDestroy(ReadOnlySpan<Entity> entities) => _world.EnqueueDestroy(entities);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T> => _world.EnqueueAdd(entity, value);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
                => _world.EnqueueAdd(entities, values);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>
                => _world.EnqueueAdd(entities, value);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T> => _world.EnqueueRemove<T>(entity);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T> => _world.EnqueueRemove<T>(entities);
        }

        private readonly ArchetypeManager _archetypes = new ArchetypeManager();
        private readonly EntityManager _entities = new EntityManager();
        private readonly SystemManager _systems = new SystemManager();
        private readonly StructuralQueue _structural = new StructuralQueue();
        private readonly IStructuralCommands _commands;
        private readonly IHost _host;
        private readonly IWorldEventSink _eventSink;

        private readonly bool _statsEnabled;
        private readonly bool _debugEnabled;
        private readonly bool _dirtyTrackingEnabled;
        private readonly DirtyTracker _dirty = new DirtyTracker();
        private EcsStats _stats;
        private uint _worldTick;
        private WorldLifecycleState _state;

        public World(EcsOptions options = default)
            : this(NoOpHost.Instance, NoOpWorldEventSink.Instance, options)
        {
        }

        public World(IHost host, EcsOptions options = default)
            : this(host, NoOpWorldEventSink.Instance, options)
        {
        }

        public World(IHost host, IWorldEventSink eventSink, EcsOptions options = default)
        {
            _host = host ?? NoOpHost.Instance;
            _eventSink = eventSink ?? NoOpWorldEventSink.Instance;
            _statsEnabled = options.EnableStatistics;
            _debugEnabled = options.EnableDebug;
            _dirtyTrackingEnabled = options.EnableDirtyTracking;
            _systems.SetExecutionMode(options.SystemExecutionMode);
            _systems.ConfigurePhaseStaggering(options.EnablePhaseStaggering, options.MaxNonCriticalStagger);
            _commands = new WorldStructuralCommands(this);
            _state = WorldLifecycleState.Created;
        }

        public IEntityReadOnly Entities => _entities;
        /// <summary>
        /// Internal ECS store path used by queue/barrier plumbing.
        /// Not part of supported public API usage.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal EntityManager EntityStore => _entities;

        /// <summary>
        /// Internal archetype manager access for ECS runtime internals only.
        /// Not part of supported public API usage.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal ArchetypeManager Archetypes => _archetypes;

        /// <summary>
        /// Internal system manager access for ECS runtime internals only.
        /// Not part of supported public API usage.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal SystemManager Systems => _systems;
        public IStructuralCommands Commands => _commands;
        public IHost Host => _host;
        public IWorldEventSink EventSink => _eventSink;
        public uint WorldTick => _worldTick;
        public WorldLifecycleState State => _state;
        /// <summary>
        /// Internal dirty-tracker access for ECS runtime internals only.
        /// Not part of supported public API usage.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal DirtyTracker Dirty => _dirty;

        public bool StatisticsEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _statsEnabled;
        }

        public bool DebugEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _debugEnabled;
        }

        public bool DirtyTrackingEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _dirtyTrackingEnabled;
        }

        public bool IsSealed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state >= WorldLifecycleState.Sealed;
        }

        public bool IsInitialized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state >= WorldLifecycleState.Initialized && _state < WorldLifecycleState.Shutdown;
        }

        public ref EcsStats Stats
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _stats;
        }

        public int AliveEntityCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities.AliveCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(Entity entity) => _entities.IsAlive(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(Entity entity) => _entities.IsValid(entity);

        public int SystemCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _systems.Count;
        }

        public SystemExecutionMode SystemExecutionMode
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _systems.ExecutionMode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RunManualSystem(SystemId id, float deltaSeconds)
        {
            EnsureReadyForExecution();
            _systems.RunManual(id, this, _worldTick, deltaSeconds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RunManualSystem<T>(float deltaSeconds) where T : SystemBase
        {
            EnsureReadyForExecution();
            _systems.RunManual<T>(this, _worldTick, deltaSeconds);
        }

        public int[] GetSystemStartOffsetsSnapshot()
        {
            var offsets = new int[_systems.Count];
            _systems.CopyStartOffsets(offsets);
            return offsets;
        }

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

        public void Seal()
        {
            EnsureNotShutdown();

            if (_state >= WorldLifecycleState.Sealed)
                return;

            ComponentManager.Seal();
            PrefabRegistry.Seal();
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

        /// <summary>
        /// Calls Serialize on every registered system in registration order.
        /// The host injects the writer — Godot .res, JSON, binary, etc.
        /// Systems with no persistent state leave the default no-op in place.
        /// </summary>
        public void SaveAll(IDataWriter writer) => _systems.SaveAllSystems(writer);

        /// <summary>
        /// Calls Deserialize on every registered system in registration order.
        /// Must be called with a reader whose position matches the SaveAll output.
        /// </summary>
        public void LoadAll(IDataReader reader) => _systems.LoadAllSystems(reader);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueCreateDefault(ArchetypeSignature signature)
        {
            EnsureNotShutdown();
            _structural.EnqueueCreateDefault(signature);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.CreatesEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueCreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
        {
            EnsureNotShutdown();
            if (signatures.Length == 0)
                return;

            _structural.EnqueueCreateDefaultRange(signatures);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.CreatesEnqueued, signatures.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawn(ArchetypeSignature signature, PrefabApply apply)
        {
            EnsureNotShutdown();
            _structural.EnqueueSpawn(signature, apply);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.SpawnsEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawn(ArchetypeSignature signature, PrefabApply apply, int count)
        {
            EnsureNotShutdown();
            if (count <= 0)
                return;

            _structural.EnqueueSpawnRepeated(signature, apply, count);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.SpawnsEnqueued, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawnTemplate(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData)
        {
            EnsureNotShutdown();
            _structural.EnqueueSpawnTemplate(signature, applyTemplate, applyPerInstance, hasPerInstanceData);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.SpawnsEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawnTemplate(ArchetypeSignature signature, PrefabApply applyTemplate, PrefabApply? applyPerInstance, bool hasPerInstanceData, int count)
        {
            EnsureNotShutdown();
            if (count <= 0)
                return;

            _structural.EnqueueSpawnTemplateRepeated(signature, applyTemplate, applyPerInstance, hasPerInstanceData, count);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.SpawnsEnqueued, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawn(PrefabId prefabId)
        {
            EnsureNotShutdown();
            _structural.EnqueueSpawn(prefabId);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.SpawnsEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawn(PrefabId prefabId, int count)
        {
            EnsureNotShutdown();
            if (count <= 0)
                return;

            _structural.EnqueueSpawnRepeated(prefabId, count);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.SpawnsEnqueued, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawn(ReadOnlySpan<PrefabId> prefabIds)
        {
            EnsureNotShutdown();
            if (prefabIds.Length == 0)
                return;

            _structural.EnqueueSpawnRange(prefabIds);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.SpawnsEnqueued, prefabIds.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueSpawn(string prefabKey)
        {
            var id = PrefabRegistry.GetId(prefabKey);
            EnqueueSpawn(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueDestroy(Entity entity)
        {
            EnsureNotShutdown();
            _structural.EnqueueDestroy(entity);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.DestroysEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueDestroy(ReadOnlySpan<Entity> entities)
        {
            EnsureNotShutdown();
            if (entities.Length == 0)
                return;

            _structural.EnqueueDestroyRange(entities);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.DestroysEnqueued, entities.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueAdd<T>(Entity entity, T value) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            _structural.EnqueueAdd(entity, value);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.AddsEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, ReadOnlySpan<T> values) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            if (entities.Length == 0)
                return;
            if (entities.Length != values.Length)
                throw new ArgumentException("Entities and values length must match.");

            _structural.EnqueueAddRange(entities, values);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.AddsEnqueued, entities.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueAdd<T>(ReadOnlySpan<Entity> entities, in T value) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            if (entities.Length == 0)
                return;

            _structural.EnqueueAddRepeated(entities, value);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.AddsEnqueued, entities.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueRemove<T>(Entity entity) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            _structural.EnqueueRemove<T>(entity);
            if (_statsEnabled)
                Interlocked.Increment(ref _stats.RemovesEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueRemove<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            if (entities.Length == 0)
                return;

            _structural.EnqueueRemoveRange<T>(entities);
            if (_statsEnabled)
                Interlocked.Add(ref _stats.RemovesEnqueued, entities.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateDefault(ArchetypeSignature signature)
        {
            EnqueueCreateDefault(signature);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateDefault(ReadOnlySpan<ArchetypeSignature> signatures)
        {
            EnqueueCreateDefault(signatures);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate(ArchetypeSignature signature, PrefabApply apply)
        {
            EnqueueSpawn(signature, apply);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate(PrefabId prefabId)
        {
            EnqueueSpawn(prefabId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate(PrefabId prefabId, int count)
        {
            EnqueueSpawn(prefabId, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate(ReadOnlySpan<PrefabId> prefabIds)
        {
            EnqueueSpawn(prefabIds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate(string prefabKey)
        {
            EnqueueSpawn(prefabKey);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate(string prefabKey, int count)
        {
            var id = PrefabRegistry.GetId(prefabKey);
            EnqueueSpawn(id, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate<TPrefab>() where TPrefab : unmanaged, IPrefab<TPrefab>
        {
            EnsureNotShutdown();
            PrefabSpawnCache<TPrefab>.Enqueue(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Instantiate<TPrefab>(int count) where TPrefab : unmanaged, IPrefab<TPrefab>
        {
            EnsureNotShutdown();
            PrefabSpawnCache<TPrefab>.Enqueue(this, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InstantiateTemplate<TPrefab>() where TPrefab : unmanaged, ITemplatePrefab<TPrefab>
        {
            EnsureNotShutdown();
            TemplatePrefabSpawnCache<TPrefab>.Enqueue(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InstantiateTemplate<TPrefab>(int count) where TPrefab : unmanaged, ITemplatePrefab<TPrefab>
        {
            EnsureNotShutdown();
            TemplatePrefabSpawnCache<TPrefab>.Enqueue(this, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy(Entity entity) => EnqueueDestroy(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy(ReadOnlySpan<Entity> entities) => EnqueueDestroy(entities);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(Entity entity) where T : unmanaged, IComponent<T> => EnqueueRemove<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T> => EnqueueRemove<T>(entities);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetOrAddComponent<T>(Entity entity, in T value) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            if (!TrySetComponent(entity, value))
                EnqueueAdd(entity, value);
        }

        public bool HasComponent<T>(Entity entity) where T : unmanaged, IComponent<T>
        {
            var id = ComponentIdCache<T>.Id;
            return _entities.TryGetLocation(entity, out var archetype, out _) && archetype.HasComponent(id);
        }

        public T GetComponent<T>(Entity entity) where T : unmanaged, IComponent<T>
        {
            var id = ComponentIdCache<T>.Id;
            if (!_entities.TryGetLocation(entity, out var archetype, out var slot))
                throw new InvalidOperationException($"Entity not alive: {entity}");

            return archetype.GetComponentSpan<T>(id)[slot];
        }

        public ref T GetComponentRef<T>(Entity entity) where T : unmanaged, IComponent<T>
        {
            var id = ComponentIdCache<T>.Id;
            if (!_entities.TryGetLocation(entity, out var archetype, out var slot))
                throw new InvalidOperationException($"Entity not alive: {entity}");

            return ref archetype.GetComponentRef<T>(id, slot);
        }

        public bool TryGetComponent<T>(Entity entity, out T value) where T : unmanaged, IComponent<T>
        {
            var id = ComponentIdCache<T>.Id;
            if (!_entities.TryGetLocation(entity, out var archetype, out var slot) || !archetype.HasComponent(id))
            {
                value = default;
                return false;
            }

            value = archetype.GetComponentSpan<T>(id)[slot];
            return true;
        }

        public bool TrySetComponent<T>(Entity entity, in T value) where T : unmanaged, IComponent<T>
        {
            EnsureNotShutdown();
            var id = ComponentIdCache<T>.Id;
            if (!_entities.TryGetLocation(entity, out var archetype, out var slot) || !archetype.HasComponent(id))
                return false;

            archetype.GetComponentSpan<T>(id)[slot] = value;
            MarkDirty(id, entity);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeManager.ArchetypeEnumerator QueryArchetypes(ReadOnlySpan<ComponentId> required)
            => _archetypes.EnumerateWith(required);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerator<T> QueryChunks<T>() where T : unmanaged, IComponent<T>
            => new QueryChunkEnumerator<T>(_archetypes.EnumerateWith(QueryRequiredCache<T>.Required), ComponentIdCache<T>.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerator<T1, T2> QueryChunks<T1, T2>()
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
            => new QueryChunkEnumerator<T1, T2>(
                _archetypes.EnumerateWith(QueryRequiredCache<T1, T2>.Required),
                ComponentIdCache<T1>.Id,
                ComponentIdCache<T2>.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public QueryChunkEnumerator<T1, T2, T3> QueryChunks<T1, T2, T3>()
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
            where T3 : unmanaged, IComponent<T3>
            => new QueryChunkEnumerator<T1, T2, T3>(
                _archetypes.EnumerateWith(QueryRequiredCache<T1, T2, T3>.Required),
                ComponentIdCache<T1>.Id,
                ComponentIdCache<T2>.Id,
                ComponentIdCache<T3>.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ParallelFor(int count, Action<int> action)
        {
            EnsureReadyForExecution();
            _systems.ParallelFor(count, action);
        }

        public int Query<T>(QueryAction<T> action) where T : unmanaged, IComponent<T>
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var id = ComponentIdCache<T>.Id;
            var enumerator = _archetypes.EnumerateWith(QueryRequiredCache<T>.Required);
            int matched = 0;

            while (enumerator.MoveNext())
            {
                var archetype = enumerator.Current;
                int columnIndex = archetype.GetColumnIndex(id);
                if (columnIndex < 0)
                    continue;

                var entities = archetype.GetEntitySpan();
                var values = archetype.GetComponentSpanAt<T>(columnIndex);
                int count = entities.Length;
                for (int i = 0; i < count; i++)
                {
                    action(entities[i], ref values[i]);
                    matched++;
                }
            }

            return matched;
        }

        public int Query<T1, T2>(QueryAction<T1, T2> action)
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var id1 = ComponentIdCache<T1>.Id;
            var id2 = ComponentIdCache<T2>.Id;

            var enumerator = _archetypes.EnumerateWith(QueryRequiredCache<T1, T2>.Required);
            int matched = 0;

            while (enumerator.MoveNext())
            {
                var archetype = enumerator.Current;
                int col1 = archetype.GetColumnIndex(id1);
                int col2 = archetype.GetColumnIndex(id2);
                if (col1 < 0 || col2 < 0)
                    continue;

                var entities = archetype.GetEntitySpan();
                var values1 = archetype.GetComponentSpanAt<T1>(col1);
                var values2 = archetype.GetComponentSpanAt<T2>(col2);
                int count = entities.Length;

                for (int i = 0; i < count; i++)
                {
                    action(entities[i], ref values1[i], ref values2[i]);
                    matched++;
                }
            }

            return matched;
        }

        public int Query<T1, T2, T3>(QueryAction<T1, T2, T3> action)
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
            where T3 : unmanaged, IComponent<T3>
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var id1 = ComponentIdCache<T1>.Id;
            var id2 = ComponentIdCache<T2>.Id;
            var id3 = ComponentIdCache<T3>.Id;

            var enumerator = _archetypes.EnumerateWith(QueryRequiredCache<T1, T2, T3>.Required);
            int matched = 0;

            while (enumerator.MoveNext())
            {
                var archetype = enumerator.Current;
                int col1 = archetype.GetColumnIndex(id1);
                int col2 = archetype.GetColumnIndex(id2);
                int col3 = archetype.GetColumnIndex(id3);
                if (col1 < 0 || col2 < 0 || col3 < 0)
                    continue;

                var entities = archetype.GetEntitySpan();
                var values1 = archetype.GetComponentSpanAt<T1>(col1);
                var values2 = archetype.GetComponentSpanAt<T2>(col2);
                var values3 = archetype.GetComponentSpanAt<T3>(col3);
                int count = entities.Length;

                for (int i = 0; i < count; i++)
                {
                    action(entities[i], ref values1[i], ref values2[i], ref values3[i]);
                    matched++;
                }
            }

            return matched;
        }

        public ref struct QueryChunk<T> where T : unmanaged, IComponent<T>
        {
            public Span<Entity> Entities;
            public Span<T> Component1;

            internal QueryChunk(Span<Entity> entities, Span<T> c1)
            {
                Entities = entities;
                Component1 = c1;
            }
        }

        public ref struct QueryChunk<T1, T2>
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
        {
            public Span<Entity> Entities;
            public Span<T1> Component1;
            public Span<T2> Component2;

            internal QueryChunk(Span<Entity> entities, Span<T1> c1, Span<T2> c2)
            {
                Entities = entities;
                Component1 = c1;
                Component2 = c2;
            }
        }

        public ref struct QueryChunk<T1, T2, T3>
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
            where T3 : unmanaged, IComponent<T3>
        {
            public Span<Entity> Entities;
            public Span<T1> Component1;
            public Span<T2> Component2;
            public Span<T3> Component3;

            internal QueryChunk(Span<Entity> entities, Span<T1> c1, Span<T2> c2, Span<T3> c3)
            {
                Entities = entities;
                Component1 = c1;
                Component2 = c2;
                Component3 = c3;
            }
        }

        public ref struct QueryChunkEnumerator<T> where T : unmanaged, IComponent<T>
        {
            private ArchetypeManager.ArchetypeEnumerator _inner;
            private readonly ComponentId _id;
            private QueryChunk<T> _current;

            internal QueryChunkEnumerator(ArchetypeManager.ArchetypeEnumerator inner, ComponentId id)
            {
                _inner = inner;
                _id = id;
                _current = default;
            }

            public QueryChunk<T> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (_inner.MoveNext())
                {
                    var archetype = _inner.Current;
                    int col = archetype.GetColumnIndex(_id);
                    if (col < 0)
                        continue;

                    _current = new QueryChunk<T>(
                        archetype.GetEntitySpan(),
                        archetype.GetComponentSpanAt<T>(col));
                    return true;
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public QueryChunkEnumerator<T> GetEnumerator() => this;
        }

        public ref struct QueryChunkEnumerator<T1, T2>
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
        {
            private ArchetypeManager.ArchetypeEnumerator _inner;
            private readonly ComponentId _id1;
            private readonly ComponentId _id2;
            private QueryChunk<T1, T2> _current;

            internal QueryChunkEnumerator(ArchetypeManager.ArchetypeEnumerator inner, ComponentId id1, ComponentId id2)
            {
                _inner = inner;
                _id1 = id1;
                _id2 = id2;
                _current = default;
            }

            public QueryChunk<T1, T2> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (_inner.MoveNext())
                {
                    var archetype = _inner.Current;
                    int col1 = archetype.GetColumnIndex(_id1);
                    int col2 = archetype.GetColumnIndex(_id2);
                    if (col1 < 0 || col2 < 0)
                        continue;

                    _current = new QueryChunk<T1, T2>(
                        archetype.GetEntitySpan(),
                        archetype.GetComponentSpanAt<T1>(col1),
                        archetype.GetComponentSpanAt<T2>(col2));
                    return true;
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public QueryChunkEnumerator<T1, T2> GetEnumerator() => this;
        }

        public ref struct QueryChunkEnumerator<T1, T2, T3>
            where T1 : unmanaged, IComponent<T1>
            where T2 : unmanaged, IComponent<T2>
            where T3 : unmanaged, IComponent<T3>
        {
            private ArchetypeManager.ArchetypeEnumerator _inner;
            private readonly ComponentId _id1;
            private readonly ComponentId _id2;
            private readonly ComponentId _id3;
            private QueryChunk<T1, T2, T3> _current;

            internal QueryChunkEnumerator(ArchetypeManager.ArchetypeEnumerator inner, ComponentId id1, ComponentId id2, ComponentId id3)
            {
                _inner = inner;
                _id1 = id1;
                _id2 = id2;
                _id3 = id3;
                _current = default;
            }

            public QueryChunk<T1, T2, T3> Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _current;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                while (_inner.MoveNext())
                {
                    var archetype = _inner.Current;
                    int col1 = archetype.GetColumnIndex(_id1);
                    int col2 = archetype.GetColumnIndex(_id2);
                    int col3 = archetype.GetColumnIndex(_id3);
                    if (col1 < 0 || col2 < 0 || col3 < 0)
                        continue;

                    _current = new QueryChunk<T1, T2, T3>(
                        archetype.GetEntitySpan(),
                        archetype.GetComponentSpanAt<T1>(col1),
                        archetype.GetComponentSpanAt<T2>(col2),
                        archetype.GetComponentSpanAt<T3>(col3));
                    return true;
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public QueryChunkEnumerator<T1, T2, T3> GetEnumerator() => this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty(Entity entity)
        {
            if (_dirtyTrackingEnabled)
            {
                _dirty.Mark(entity);
                if (_statsEnabled)
                {
                    Interlocked.Increment(ref _stats.DirtyMarkCalls);
                    Interlocked.Add(ref _stats.DirtyMarkedEntities, 1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty(ComponentId componentId, Entity entity)
        {
            if (_dirtyTrackingEnabled)
            {
                _dirty.Mark(componentId, entity);
                if (_statsEnabled)
                {
                    Interlocked.Increment(ref _stats.DirtyMarkCalls);
                    Interlocked.Add(ref _stats.DirtyMarkedEntities, 1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            if (_dirtyTrackingEnabled)
            {
                _dirty.Mark(componentId, entities);
                if (_statsEnabled && entities.Length > 0)
                {
                    Interlocked.Increment(ref _stats.DirtyMarkBatchCalls);
                    Interlocked.Add(ref _stats.DirtyMarkedEntities, entities.Length);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirtyUnique(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            if (_dirtyTrackingEnabled)
            {
                _dirty.MarkUnique(componentId, entities);
                if (_statsEnabled && entities.Length > 0)
                {
                    Interlocked.Increment(ref _stats.DirtyMarkTrustedBatchCalls);
                    Interlocked.Add(ref _stats.DirtyMarkedEntities, entities.Length);
                    Interlocked.Add(ref _stats.DirtyTrustedMarkedEntities, entities.Length);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirty<T>(Entity entity) where T : unmanaged, IComponent<T>
        {
            if (_dirtyTrackingEnabled)
            {
                _dirty.Mark(ComponentIdCache<T>.Id, entity);
                if (_statsEnabled)
                {
                    Interlocked.Increment(ref _stats.DirtyMarkCalls);
                    Interlocked.Add(ref _stats.DirtyMarkedEntities, 1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkDirtyUnique<T>(ReadOnlySpan<Entity> entities) where T : unmanaged, IComponent<T>
        {
            if (_dirtyTrackingEnabled)
            {
                _dirty.MarkUnique(ComponentIdCache<T>.Id, entities);
                if (_statsEnabled && entities.Length > 0)
                {
                    Interlocked.Increment(ref _stats.DirtyMarkTrustedBatchCalls);
                    Interlocked.Add(ref _stats.DirtyMarkedEntities, entities.Length);
                    Interlocked.Add(ref _stats.DirtyTrustedMarkedEntities, entities.Length);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CopyDirtyTo(Span<Entity> destination)
            => _dirtyTrackingEnabled ? _dirty.CopyAllTo(destination) : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DrainDirtyTo(Span<Entity> destination)
            => _dirtyTrackingEnabled ? _dirty.CopyAndClearAllTo(destination) : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CopyDirtyTo(ComponentId componentId, Span<Entity> destination)
            => _dirtyTrackingEnabled ? _dirty.CopyComponentTo(componentId, destination) : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DrainDirtyTo(ComponentId componentId, Span<Entity> destination)
            => _dirtyTrackingEnabled ? _dirty.CopyAndClearComponentTo(componentId, destination) : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CopyDirtyTo<T>(Span<Entity> destination) where T : unmanaged, IComponent<T>
            => _dirtyTrackingEnabled ? _dirty.CopyComponentTo(ComponentIdCache<T>.Id, destination) : 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DrainDirtyTo<T>(Span<Entity> destination) where T : unmanaged, IComponent<T>
            => _dirtyTrackingEnabled ? _dirty.CopyAndClearComponentTo(ComponentIdCache<T>.Id, destination) : 0;

        internal object DirtySyncRoot => _dirty.SyncRoot;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkDirtyNoLock(Entity entity)
        {
            if (_dirtyTrackingEnabled)
                _dirty.MarkNoLock(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkDirtyNoLock(ReadOnlySpan<Entity> entities)
        {
            if (_dirtyTrackingEnabled)
                _dirty.MarkNoLock(entities);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkDirtyUniqueNoLock(ReadOnlySpan<Entity> entities)
        {
            if (_dirtyTrackingEnabled)
                _dirty.MarkUniqueNoLock(entities);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkDirtyNoLock(ComponentId componentId, Entity entity)
        {
            if (_dirtyTrackingEnabled)
                _dirty.MarkNoLock(componentId, entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkDirtyNoLock(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            if (_dirtyTrackingEnabled)
                _dirty.MarkNoLock(componentId, entities);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void MarkDirtyUniqueNoLock(ComponentId componentId, ReadOnlySpan<Entity> entities)
        {
            if (_dirtyTrackingEnabled)
                _dirty.MarkUniqueNoLock(componentId, entities);
        }

        public void ProcessStructuralQueues(StructuralBarrierPhase phase = StructuralBarrierPhase.Manual)
        {
            EnsureNotShutdown();
            FlushLogs();

            if (!_structural.HasPending)
                return;

            var sw = _statsEnabled ? ValueStopwatch.StartNew() : default;

            Entity[]? createdBuffer = null;
            int createdCapacity = _structural.CreateDefaultCount + _structural.SpawnCount;
            Span<Entity> createdSpan = Span<Entity>.Empty;

            if (_eventSink.WantsEntityCreatedBatches && createdCapacity > 0)
            {
                createdBuffer = ArrayPool<Entity>.Shared.Rent(createdCapacity);
                createdSpan = createdBuffer.AsSpan(0, createdCapacity);
            }

            _structural.Apply(
                this,
                createdSpan,
                out var createdEntityCount,
                out var created,
                out var spawned,
                out var destroyed,
                out var removed,
                out var added);

#if DEBUG
            DebugValidatePostStructuralApply();
#endif

            if (_statsEnabled)
            {
                _stats.CreatesApplied += created;
                _stats.SpawnsApplied += spawned;
                _stats.DestroysApplied += destroyed;
                _stats.RemovesApplied += removed;
                _stats.AddsApplied += added;
                _stats.RecordStructural(sw.ElapsedTicks());
            }

            if (createdBuffer != null)
            {
                if (createdEntityCount > 0)
                    _eventSink.OnEntitiesCreated(phase, createdBuffer.AsSpan(0, createdEntityCount));

                ArrayPool<Entity>.Shared.Return(createdBuffer, clearArray: false);
            }

            _eventSink.OnStructuralBarrierApplied(
                new StructuralBarrierEvent(_worldTick, phase, created, spawned, destroyed, removed, added));

            FlushLogs();
        }

        public void Tick(float deltaSeconds)
        {
            EnsureReadyForExecution();
            FlushLogs();

            var tickSw = _statsEnabled ? ValueStopwatch.StartNew() : default;

            ProcessStructuralQueues(StructuralBarrierPhase.PreSystems); // Apply Networking and Inputs

            var sysSw = _statsEnabled ? ValueStopwatch.StartNew() : default;
            int localCaptureWorkers = _systems.StructuralCaptureWorkerCount;
            bool dirtyCaptureEnabled = _dirtyTrackingEnabled;

            if (dirtyCaptureEnabled)
                _dirty.BeginCapture(localCaptureWorkers);

            if (localCaptureWorkers > 0)
                _structural.BeginLocalCapture(localCaptureWorkers);

            try
            {
                _systems.Tick(this, _worldTick, deltaSeconds);
            }
            finally
            {
                if (localCaptureWorkers > 0)
                    _structural.EndLocalCapture();

                if (dirtyCaptureEnabled)
                    _dirty.EndCapture();
            }

            if (_statsEnabled)
                _stats.RecordSystems(sysSw.ElapsedTicks());

            ProcessStructuralQueues(StructuralBarrierPhase.PostSystems); // Apply System Changes on the Same Frame

            _worldTick++;

            if (_statsEnabled)
                _stats.RecordTick(tickSw.ElapsedTicks());

            FlushLogs();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCanMutateConfiguration()
        {
            if (_state >= WorldLifecycleState.Sealed)
                throw new InvalidOperationException("World configuration is sealed.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureReadyForExecution()
        {
            EnsureNotShutdown();

            if (_state < WorldLifecycleState.Initialized)
                Initialize();

            if (_state == WorldLifecycleState.Initialized)
                _state = WorldLifecycleState.Running;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureNotShutdown()
        {
            if (_state == WorldLifecycleState.Shutdown)
                throw new InvalidOperationException("World is shutdown.");
        }

#if DEBUG
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DebugValidatePostStructuralApply()
        {
            _structural.DebugValidatePostApply();
            _entities.DebugValidateLocations();

            var archetypes = _archetypes.AsSpan();
            for (int i = 0; i < archetypes.Length; i++)
                archetypes[i].DebugValidateInvariants();
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FlushLogs(int maxEntries = 8192)
            => EcsLog.DrainTo(_host, maxEntries);

    }
}
