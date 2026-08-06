using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Instance-owned composition root for one bounded entity pool, archetype registry,
/// and set of prepared structural transitions.
/// </summary>
/// <remarks>
/// Registration is permitted only while created. <see cref="Seal"/> closes every
/// allocation/plan-construction boundary and enables immediate single-owner runtime
/// mutation. This is the minimal kernel world, not the later scheduler/host facade.
/// </remarks>
internal sealed partial class EcsWorld<TArena> : IDisposable
    where TArena : class, IChunkArenaBackend
{
    private const int MaximumSupportedQueryPlans = 1 << 20;

    private readonly ComponentSchema _schema;
    private readonly EntityPool _entities;
    private readonly ArchetypeRegistry<TArena> _archetypes;
    private readonly IArchetypeQueryPlan<TArena>?[] _queryPlans;
    private int _queryPlanCount;
    private EcsWorldState _state;
    private bool _isVisitingChunks;

    internal EcsWorld(
        ComponentSchema schema,
        EntityCapacity entityCapacity,
        int maximumArchetypes,
        Func<ChunkArenaCapacity, TArena> arenaFactory,
        int maximumQueryPlans)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(arenaFactory);
        if (maximumQueryPlans < 0 || maximumQueryPlans > MaximumSupportedQueryPlans)
            throw new ArgumentOutOfRangeException(nameof(maximumQueryPlans));

        _schema = schema;
        _entities = new EntityPool(WorldId.Create(), entityCapacity);
        _archetypes = new ArchetypeRegistry<TArena>(schema, maximumArchetypes, arenaFactory);
        _queryPlans = new IArchetypeQueryPlan<TArena>[maximumQueryPlans];
        _state = EcsWorldState.Created;
    }

    internal WorldId Id => _entities.WorldId;

    internal ComponentSchema Schema => _schema;

    internal EntityCapacity EntityCapacity => _entities.Capacity;

    internal EcsWorldState State => _state;

    internal bool IsSealed => _state == EcsWorldState.Sealed;

    internal bool IsDisposed => _state == EcsWorldState.Disposed;

    internal int AliveCount
    {
        get
        {
            ThrowIfDisposed();
            return _entities.AliveCount;
        }
    }

    internal int LocatedCount
    {
        get
        {
            ThrowIfDisposed();
            return _entities.LocatedCount;
        }
    }

    internal int ArchetypeCount
    {
        get
        {
            ThrowIfDisposed();
            return _archetypes.Count;
        }
    }

    internal int QueryPlanCount
    {
        get
        {
            ThrowIfDisposed();
            return _queryPlanCount;
        }
    }

    internal int MaximumQueryPlans => _queryPlans.Length;

    internal ArchetypeId RegisterArchetype(
        ReadOnlySpan<ComponentId> canonicalSignature,
        ChunkLayoutOptions layoutOptions,
        int maximumChunks)
    {
        EnsureCreated();
        return _archetypes.Register(canonicalSignature, layoutOptions, maximumChunks);
    }

    internal ArchetypeTransitionPlan<TArena> PrepareTransition(
        ArchetypeId sourceId,
        ArchetypeId destinationId)
    {
        EnsureCreated();
        return _archetypes.PrepareTransition(sourceId, destinationId);
    }

    internal ArchetypeComponentBinding<T> BindComponent<T>(
        ArchetypeId archetypeId,
        ComponentId generatedComponentId)
        where T : unmanaged, IComponent<T>
    {
        EnsureCreated();
        if (!_schema.TryGetComponentId<T>(out ComponentId actualComponentId) ||
            actualComponentId != generatedComponentId)
        {
            throw new ArgumentException(
                "The generated component ID does not identify the requested type in this world schema.",
                nameof(generatedComponentId));
        }

        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        int columnIndex = storage.Store.GetColumnIndex(generatedComponentId);
        if (columnIndex < 0)
        {
            throw new ArgumentException(
                "The archetype does not contain the requested generated component.",
                nameof(archetypeId));
        }

        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(generatedComponentId);
        ref readonly ComponentColumnLayout column = ref storage.Columns[columnIndex];
        if (metadata.Flags != column.Flags ||
            metadata.Size != column.ElementSizeBytes ||
            (!metadata.IsTag && metadata.Size != Unsafe.SizeOf<T>()))
        {
            ThrowWorldInvariant("Generated component metadata and archetype storage disagree.");
        }

        return new ArchetypeComponentBinding<T>(
            Id,
            archetypeId,
            generatedComponentId,
            columnIndex,
            metadata.Flags);
    }

    internal ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> PrepareReadWriteQuery<TRead, TWrite>(
        ComponentId generatedReadComponentId,
        ComponentId generatedWriteComponentId)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArchetypeQueryFilter filter = ArchetypeQueryFilter.Create(
            Id,
            _schema,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty);
        return PrepareReadWriteQuery<TRead, TWrite>(
            generatedReadComponentId,
            generatedWriteComponentId,
            filter);
    }

    internal ArchetypeQueryFilter CreateQueryFilter(
        ReadOnlySpan<ComponentId> requiredComponents,
        ReadOnlySpan<ComponentId> excludedComponents,
        ReadOnlySpan<ComponentId> enabledComponents,
        ReadOnlySpan<ComponentId> disabledComponents)
    {
        EnsureCreated();
        return ArchetypeQueryFilter.Create(
            Id,
            _schema,
            requiredComponents,
            excludedComponents,
            enabledComponents,
            disabledComponents);
    }

    internal ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> PrepareReadWriteQuery<TRead, TWrite>(
        ComponentId generatedReadComponentId,
        ComponentId generatedWriteComponentId,
        in ArchetypeQueryFilter filter)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        EnsureCreated();
        if (!filter.IsValid || filter.WorldId != Id)
            throw new ArgumentException("The query filter belongs to a different world.", nameof(filter));
        ref readonly ComponentMetadata readMetadata = ref ValidateGeneratedDataComponent<TRead>(
            generatedReadComponentId,
            nameof(generatedReadComponentId));
        ref readonly ComponentMetadata writeMetadata = ref ValidateGeneratedDataComponent<TWrite>(
            generatedWriteComponentId,
            nameof(generatedWriteComponentId));
        if (generatedReadComponentId == generatedWriteComponentId)
        {
            throw new ArgumentException(
                "A query cannot declare the same component for both read and write access.",
                nameof(generatedWriteComponentId));
        }
        if (filter.Excludes(generatedReadComponentId) || filter.Excludes(generatedWriteComponentId))
        {
            throw new ArgumentException(
                "A read or write component cannot also be excluded by its query filter.",
                nameof(filter));
        }
        if (_queryPlanCount == _queryPlans.Length)
        {
            throw new InvalidOperationException(
                $"Query-plan capacity {_queryPlans.Length} is exhausted.");
        }

        var plan = new ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite>(
            Id,
            _queryPlanCount,
            generatedReadComponentId,
            readMetadata.Flags,
            generatedWriteComponentId,
            writeMetadata.Flags,
            filter);
        _queryPlans[_queryPlanCount++] = plan;
        return plan;
    }

    internal bool TryGetArchetypeId(
        ReadOnlySpan<ComponentId> canonicalSignature,
        out ArchetypeId archetypeId)
    {
        ThrowIfDisposed();
        return _archetypes.TryGetId(canonicalSignature, out archetypeId);
    }

    internal ArchetypeStorage<TArena> GetArchetypeStorage(ArchetypeId archetypeId)
    {
        ThrowIfDisposed();
        return _archetypes.GetStorage(archetypeId);
    }

    internal void Seal()
    {
        ThrowIfDisposed();
        if (_state == EcsWorldState.Sealed)
            return;

        for (int index = 0; index < _queryPlanCount; index++)
        {
            IArchetypeQueryPlan<TArena> plan = _queryPlans[index]
                ?? throw new InvalidOperationException("A registered query-plan slot is empty.");
            plan.Compile(_archetypes);
        }
        _archetypes.Seal();
        _state = EcsWorldState.Sealed;
    }

    internal bool IsAlive(Entity entity)
    {
        EnsureSealed();
        return _entities.IsAlive(entity);
    }

    internal bool TryGetLocation(Entity entity, out EntityLocation location)
    {
        EnsureSealed();
        return _entities.TryGetLocation(entity, out location);
    }

    internal bool TryCreateDefault(ArchetypeId archetypeId, out Entity entity)
    {
        EnsureRuntimeMutationAllowed();
        return TryCreateDefaultCore(archetypeId, out entity, out _);
    }

    internal bool TryCreateInitialized<T>(
        ArchetypeId archetypeId,
        in ArchetypeComponentBinding<T> binding,
        in T initialValue,
        out Entity entity)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateBinding(binding, requireData: true);
        if (binding.ArchetypeId != archetypeId)
        {
            throw new ArgumentException(
                "The initialization binding belongs to a different archetype.",
                nameof(binding));
        }
        if (!TryCreateDefaultCore(archetypeId, out entity, out EntityLocation location))
            return false;

        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        storage.Store.GetBoundComponentRef<T>(location.ChunkRow, binding.ColumnIndex) = initialValue;
        return true;
    }

    internal bool HasComponent<T>(Entity entity, in ArchetypeComponentBinding<T> binding)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: false);
        return _entities.TryGetLocation(entity, out EntityLocation location) &&
            location.ArchetypeId == binding.ArchetypeId;
    }

    internal bool TryGetComponent<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        out T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
        {
            value = default;
            return false;
        }

        value = storage.Store.GetBoundComponentRef<T>(row, binding.ColumnIndex);
        return true;
    }

    internal bool TrySetComponent<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        in T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateBinding(binding, requireData: true);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
            return false;

        storage.Store.GetBoundComponentRef<T>(row, binding.ColumnIndex) = value;
        return true;
    }

    internal bool TryGetComponentEnabled<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        out bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateEnableBinding(binding);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
        {
            enabled = false;
            return false;
        }

        enabled = storage.Store.IsBoundEnabled(row, binding.ColumnIndex);
        return true;
    }

    internal bool TrySetComponentEnabled<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateEnableBinding(binding);
        if (!TryResolveBinding(entity, binding, out ArchetypeStorage<TArena>? storage, out ChunkRow row))
            return false;

        storage.Store.SetBoundEnabled(row, binding.ColumnIndex, enabled);
        return true;
    }

    /// <summary>
    /// Synchronously visits the occupied chunks of one exact-archetype data binding.
    /// Structural or nested world mutation is rejected until the visitor returns.
    /// </summary>
    internal int VisitReadChunks<T, TVisitor>(
        in ArchetypeComponentBinding<T> binding,
        ref TVisitor visitor)
        where T : unmanaged, IComponent<T>
        where TVisitor : struct, IComponentChunkReadVisitor<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        EnterChunkVisit();
        try
        {
            ArchetypeChunkStore<TArena> store = _archetypes.GetStorage(binding.ArchetypeId).Store;
            int visitedRows = 0;
            for (int chunkIndex = store.GetFirstOccupiedChunkIndex();
                 chunkIndex >= 0;
                 chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex))
            {
                BoundComponentChunk<T> bound = store.GetBoundComponentChunk<T>(
                    chunkIndex,
                    binding.ColumnIndex,
                    binding.IsEnableable);
                var chunk = new ReadOnlyComponentChunk<T>(
                    Id,
                    binding.ArchetypeId,
                    bound.ChunkIndex,
                    bound.EntityIndices,
                    bound.Components,
                    bound.EnableWords);
                visitor.Execute(in chunk);
                visitedRows += chunk.Count;
            }

            return visitedRows;
        }
        finally
        {
            ExitChunkVisit();
        }
    }

    /// <summary>
    /// Synchronously visits writable occupied chunks of one exact-archetype data
    /// binding. Later change tracking will mark each visited column once per chunk.
    /// </summary>
    internal int VisitWriteChunks<T, TVisitor>(
        in ArchetypeComponentBinding<T> binding,
        ref TVisitor visitor)
        where T : unmanaged, IComponent<T>
        where TVisitor : struct, IComponentChunkWriteVisitor<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        EnterChunkVisit();
        try
        {
            ArchetypeChunkStore<TArena> store = _archetypes.GetStorage(binding.ArchetypeId).Store;
            int visitedRows = 0;
            for (int chunkIndex = store.GetFirstOccupiedChunkIndex();
                 chunkIndex >= 0;
                 chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex))
            {
                BoundComponentChunk<T> bound = store.GetBoundComponentChunk<T>(
                    chunkIndex,
                    binding.ColumnIndex,
                    binding.IsEnableable);
                var chunk = new WritableComponentChunk<T>(
                    Id,
                    binding.ArchetypeId,
                    bound.ChunkIndex,
                    bound.EntityIndices,
                    bound.Components,
                    bound.EnableWords);
                visitor.Execute(in chunk);
                visitedRows += chunk.Count;
            }

            return visitedRows;
        }
        finally
        {
            ExitChunkVisit();
        }
    }

    /// <summary>
    /// Executes a seal-compiled query across every matched archetype while exposing
    /// one read-only and one distinct writable component column.
    /// </summary>
    internal int VisitReadWriteChunks<TRead, TWrite, TVisitor>(
        ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> plan,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TVisitor : struct, IComponentChunkReadWriteVisitor<TRead, TWrite>
    {
        EnsureSealed();
        ValidateQueryPlan(plan);
        EnterChunkVisit();
        try
        {
            int visitedRows = 0;
            ReadOnlySpan<ArchetypeReadWriteQueryRoute<TArena>> routes = plan.Routes;
            for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
            {
                ref readonly ArchetypeReadWriteQueryRoute<TArena> route = ref routes[routeIndex];
                ArchetypeChunkStore<TArena> store = route.Storage.Store;
                for (int chunkIndex = store.GetFirstOccupiedChunkIndex();
                     chunkIndex >= 0;
                     chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex))
                {
                    BoundReadWriteComponentChunk<TRead, TWrite> bound =
                        store.GetBoundReadWriteComponentChunk<TRead, TWrite>(
                            chunkIndex,
                            route.ReadColumnIndex,
                            route.WriteColumnIndex,
                            plan.ReadIsEnableable,
                            plan.WriteIsEnableable);
                    var chunk = new ReadWriteComponentChunk<TRead, TWrite>(
                        Id,
                        route.Storage.Id,
                        bound.ChunkIndex,
                        rowOffset: 0,
                        bound.EntityIndices,
                        bound.ReadComponents,
                        bound.WriteComponents,
                        bound.ReadEnableWords,
                        bound.WriteEnableWords);
                    if (!route.HasRowFilters)
                    {
                        visitor.Execute(in chunk);
                        visitedRows += chunk.Count;
                    }
                    else
                    {
                        visitedRows += VisitFilteredReadWriteChunk(
                            route,
                            bound,
                            ref visitor);
                    }
                }
            }

            return visitedRows;
        }
        finally
        {
            ExitChunkVisit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int VisitFilteredReadWriteChunk<TRead, TWrite, TVisitor>(
        in ArchetypeReadWriteQueryRoute<TArena> route,
        in BoundReadWriteComponentChunk<TRead, TWrite> bound,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TVisitor : struct, IComponentChunkReadWriteVisitor<TRead, TWrite>
    {
        int rowCount = bound.EntityIndices.Length;
        int pendingStart = -1;
        int pendingEnd = -1;
        int matchedRows = 0;
        int wordCount = (rowCount + 63) >> 6;
        for (int wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            ulong mask = route.GetMatchingRowMask(bound.ChunkBytes, wordIndex);
            int wordStart = wordIndex << 6;
            int activeRows = Math.Min(64, rowCount - wordStart);
            if (activeRows != 64)
                mask &= (1UL << activeRows) - 1UL;

            while (mask != 0)
            {
                int startBit = BitOperations.TrailingZeroCount(mask);
                ulong shifted = mask >> startBit;
                int runLength = BitOperations.TrailingZeroCount(~shifted);
                int runStart = wordStart + startBit;
                int runEnd = runStart + runLength;
                if (pendingStart < 0)
                {
                    pendingStart = runStart;
                    pendingEnd = runEnd;
                }
                else if (pendingEnd == runStart)
                {
                    pendingEnd = runEnd;
                }
                else
                {
                    ExecuteReadWriteRange(route, bound, pendingStart, pendingEnd - pendingStart, ref visitor);
                    matchedRows += pendingEnd - pendingStart;
                    pendingStart = runStart;
                    pendingEnd = runEnd;
                }

                ulong runMask = runLength == 64
                    ? ulong.MaxValue
                    : ((1UL << runLength) - 1UL) << startBit;
                mask &= ~runMask;
            }
        }

        if (pendingStart >= 0)
        {
            ExecuteReadWriteRange(route, bound, pendingStart, pendingEnd - pendingStart, ref visitor);
            matchedRows += pendingEnd - pendingStart;
        }
        return matchedRows;
    }

    [MethodImpl(
        MethodImplOptions.AggressiveInlining |
        MethodImplOptions.AggressiveOptimization)]
    private void ExecuteReadWriteRange<TRead, TWrite, TVisitor>(
        in ArchetypeReadWriteQueryRoute<TArena> route,
        in BoundReadWriteComponentChunk<TRead, TWrite> bound,
        int rowOffset,
        int rowCount,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TVisitor : struct, IComponentChunkReadWriteVisitor<TRead, TWrite>
    {
        var chunk = new ReadWriteComponentChunk<TRead, TWrite>(
            Id,
            route.Storage.Id,
            bound.ChunkIndex,
            rowOffset,
            bound.EntityIndices.Slice(rowOffset, rowCount),
            bound.ReadComponents.Slice(rowOffset, rowCount),
            bound.WriteComponents.Slice(rowOffset, rowCount),
            bound.ReadEnableWords,
            bound.WriteEnableWords);
        visitor.Execute(in chunk);
    }

    private bool TryCreateDefaultCore(
        ArchetypeId archetypeId,
        out Entity entity,
        out EntityLocation location)
    {
        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);

        // Avoid consuming/recycling an entity generation when the target store is
        // already known to be full. Structural mutation is single-owner here.
        if ((long)storage.Store.Count >= storage.Store.MaximumRows ||
            !_entities.TryAllocate(out entity))
        {
            entity = Entity.Null;
            location = EntityLocation.Invalid;
            return false;
        }

        if (!storage.Store.TryAdd(entity.Index, out ChunkRow row))
        {
            if (!_entities.TryFree(entity))
                ThrowWorldInvariant("An unlocated entity could not be returned after row-capacity rejection.");
            entity = Entity.Null;
            location = EntityLocation.Invalid;
            return false;
        }

        location = new EntityLocation(archetypeId, row);
        if (_entities.TryBindLocation(entity, location))
            return true;

        RollBackJustAddedRow(storage.Store, row, entity.Index);
        if (!_entities.TryFree(entity))
            ThrowWorldInvariant("An entity could not be returned after location publication rejection.");
        entity = Entity.Null;
        location = EntityLocation.Invalid;
        ThrowWorldInvariant("A newly allocated entity rejected its first location publication.");
        return false;
    }

    internal bool TryTransition(
        Entity entity,
        ArchetypeTransitionPlan<TArena> plan,
        out EntityLocation destinationLocation)
    {
        EnsureRuntimeMutationAllowed();
        ArgumentNullException.ThrowIfNull(plan);
        if (!_archetypes.Owns(plan))
        {
            throw new ArgumentException(
                "The archetype transition plan belongs to a different world registry.",
                nameof(plan));
        }

        return ArchetypeMigrator.TryMigrate(
            _entities,
            entity,
            plan,
            out destinationLocation);
    }

    internal bool TryDestroy(Entity entity)
    {
        EnsureRuntimeMutationAllowed();
        if (!_entities.TryGetLocation(entity, out EntityLocation location))
            return false;
        if (!_archetypes.TryGetStorage(location.ArchetypeId, out ArchetypeStorage<TArena>? storage))
            ThrowWorldInvariant("A live entity location refers to an unknown archetype.");
        ArchetypeStorage<TArena> ownedStorage = storage!;
        if (!ownedStorage.Store.Contains(location.ChunkRow, entity.Index))
            ThrowWorldInvariant("A live entity location does not match its archetype row.");
        if (!ownedStorage.Store.TryRemove(
                location.ChunkRow,
                entity.Index,
                out ChunkRowRelocation relocation))
        {
            ThrowWorldInvariant("A validated archetype row rejected entity destruction.");
        }

        _entities.ApplyRelocation(ownedStorage.Id, relocation);
        if (!_entities.TryUnbindLocation(entity, location))
            ThrowWorldInvariant("A destroyed entity rejected its expected location removal.");
        if (!_entities.TryFree(entity))
            ThrowWorldInvariant("An unlocated destroyed entity could not return to the entity pool.");
        return true;
    }

    public void Dispose()
    {
        if (_state == EcsWorldState.Disposed)
            return;
        if (_isVisitingChunks)
            throw new InvalidOperationException("The world cannot be disposed during a chunk visitor.");

        try
        {
            _archetypes.Dispose();
        }
        finally
        {
            for (int index = 0; index < _queryPlanCount; index++)
                _queryPlans[index]?.Release();
            Array.Clear(_queryPlans);
            _state = EcsWorldState.Disposed;
        }
    }

    private bool TryResolveBinding<T>(
        Entity entity,
        in ArchetypeComponentBinding<T> binding,
        [NotNullWhen(true)] out ArchetypeStorage<TArena>? storage,
        out ChunkRow row)
        where T : unmanaged, IComponent<T>
    {
        if (!_entities.TryGetLocation(entity, out EntityLocation location) ||
            location.ArchetypeId != binding.ArchetypeId)
        {
            storage = null;
            row = ChunkRow.Invalid;
            return false;
        }

        storage = _archetypes.GetStorage(binding.ArchetypeId);
        row = location.ChunkRow;
        return true;
    }

    private void ValidateBinding<T>(
        in ArchetypeComponentBinding<T> binding,
        bool requireData)
        where T : unmanaged, IComponent<T>
    {
        if (!binding.IsValid)
            throw new ArgumentException("A valid archetype component binding is required.", nameof(binding));
        if (binding.WorldId != Id)
        {
            throw new ArgumentException(
                "The archetype component binding belongs to a different world.",
                nameof(binding));
        }
        if (requireData && !binding.HasData)
            throw new InvalidOperationException("Tag components do not contain readable or writable data.");
    }

    private void ValidateEnableBinding<T>(in ArchetypeComponentBinding<T> binding)
        where T : unmanaged, IComponent<T>
    {
        ValidateBinding(binding, requireData: false);
        if (!binding.IsEnableable)
            throw new InvalidOperationException("The bound component is not enableable.");
    }

    private ref readonly ComponentMetadata ValidateGeneratedDataComponent<T>(
        ComponentId generatedComponentId,
        string parameterName)
        where T : unmanaged, IComponent<T>
    {
        if (!_schema.TryGetComponentId<T>(out ComponentId actualComponentId) ||
            actualComponentId != generatedComponentId)
        {
            throw new ArgumentException(
                "The generated component ID does not identify the requested type in this world schema.",
                parameterName);
        }

        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(generatedComponentId);
        if (metadata.IsTag)
            throw new InvalidOperationException("Storage-free tags cannot be data-query columns.");
        if (metadata.Size != Unsafe.SizeOf<T>())
            ThrowWorldInvariant("Generated component metadata and runtime type size disagree.");
        return ref metadata;
    }

    private void ValidateQueryPlan<TRead, TWrite>(
        ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> plan)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.WorldId != Id ||
            unchecked((uint)plan.PlanIndex) >= unchecked((uint)_queryPlanCount) ||
            !ReferenceEquals(_queryPlans[plan.PlanIndex], plan))
        {
            throw new ArgumentException("The query plan belongs to a different world.", nameof(plan));
        }
        if (!plan.IsCompiled)
            ThrowWorldInvariant("A sealed world owns an uncompiled query plan.");
    }

    private static void RollBackJustAddedRow(
        ArchetypeChunkStore<TArena> store,
        ChunkRow row,
        uint entityIndex)
    {
        if (!store.TryRemove(row, entityIndex, out ChunkRowRelocation relocation) ||
            relocation.HasValue)
        {
            ThrowWorldInvariant("A just-added archetype row could not be rolled back in place.");
        }
    }

    private void EnsureCreated()
    {
        ThrowIfDisposed();
        if (_state != EcsWorldState.Created)
            throw new InvalidOperationException("World registration is closed after sealing.");
    }

    private void EnsureSealed()
    {
        ThrowIfDisposed();
        if (_state != EcsWorldState.Sealed)
            throw new InvalidOperationException("World runtime mutation requires a sealed world.");
    }

    private void EnsureRuntimeMutationAllowed()
    {
        EnsureSealed();
        if (_isVisitingChunks)
        {
            throw new InvalidOperationException(
                "World mutation is forbidden while a synchronous chunk visitor is active.");
        }
    }

    private void EnterChunkVisit()
    {
        if (_isVisitingChunks)
            throw new InvalidOperationException("Chunk visitors cannot be nested or re-entered.");
        _isVisitingChunks = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitChunkVisit() => _isVisitingChunks = false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowWorldInvariant(string message) =>
        throw new InvalidOperationException(message);
}
