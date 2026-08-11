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
    private const int MaximumSupportedPublicTransitions = 1 << 20;

    private readonly ComponentSchema _schema;
    private readonly EntityPool _entities;
    private readonly ArchetypeRegistry<TArena> _archetypes;
    private readonly IArchetypeQueryPlan<TArena>?[] _queryPlans;
    private readonly PreparedArchetypeTransition?[] _publicTransitions;
    private int _queryPlanCount;
    private int _publicTransitionCount;
    private ChangeVersion _changeVersion;
    private EcsWorldState _state;
    private Exception? _faultException;
    private bool _hasSealed;
    private bool _isExecutingScope;

    internal EcsWorld(
        ComponentSchema schema,
        EntityCapacity entityCapacity,
        int maximumArchetypes,
        Func<ChunkArenaCapacity, TArena> arenaFactory,
        int maximumQueryPlans,
        ChangeVersion initialChangeVersion = default,
        int maximumPublicTransitions = 0)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(arenaFactory);
        if (maximumQueryPlans < 0 || maximumQueryPlans > MaximumSupportedQueryPlans)
            throw new ArgumentOutOfRangeException(nameof(maximumQueryPlans));
        if (maximumPublicTransitions < 0 || maximumPublicTransitions > MaximumSupportedPublicTransitions)
            throw new ArgumentOutOfRangeException(nameof(maximumPublicTransitions));

        _schema = schema;
        _entities = new EntityPool(WorldId.Create(), entityCapacity);
        _archetypes = new ArchetypeRegistry<TArena>(schema, maximumArchetypes, arenaFactory);
        _queryPlans = new IArchetypeQueryPlan<TArena>[maximumQueryPlans];
        _publicTransitions = new PreparedArchetypeTransition[maximumPublicTransitions];
        _changeVersion = initialChangeVersion;
        _state = EcsWorldState.Created;
    }

    internal WorldId Id => _entities.WorldId;

    internal ComponentSchema Schema => _schema;

    internal EntityCapacity EntityCapacity => _entities.Capacity;

    internal EcsWorldState State => _state;

    internal bool IsSealed => _hasSealed && _state != EcsWorldState.Disposed;

    internal bool IsInitialized => _state is EcsWorldState.Initialized or EcsWorldState.Running;

    internal bool IsRunning => _state == EcsWorldState.Running;

    internal bool IsDisposed => _state == EcsWorldState.Disposed;

    internal Exception? FaultException => _faultException;

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

    internal int PublicTransitionCount
    {
        get
        {
            ThrowIfDisposed();
            return _publicTransitionCount;
        }
    }

    internal ChangeVersion CurrentChangeVersion
    {
        get
        {
            ThrowIfDisposed();
            return _changeVersion;
        }
    }

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

    internal uint PreparePublicTransition(
        ArchetypeId sourceId,
        ArchetypeId destinationId,
        out StructuralTransitionPhase phase)
    {
        EnsureCreated();
        if (_publicTransitionCount == _publicTransitions.Length)
        {
            throw new InvalidOperationException(
                $"Public transition capacity {_publicTransitions.Length} is exhausted.");
        }

        ArchetypeTransitionPlan<TArena> plan = PrepareTransition(sourceId, destinationId);
        PreparedArchetypeTransition transition = plan.ToPrepared(Id);
        _publicTransitions[_publicTransitionCount] = transition;
        _publicTransitionCount++;
        phase = transition.Phase;
        return checked((uint)_publicTransitionCount);
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

    internal ComponentLookupPlan<TArena, T> PrepareComponentLookup<T>()
        where T : unmanaged, IComponent<T>
    {
        EnsureCreated();
        if (!_schema.TryGetComponentId<T>(out ComponentId componentId))
        {
            throw new ArgumentException(
                "The requested component type is not registered in this world schema.",
                nameof(T));
        }
        if (_queryPlanCount == _queryPlans.Length)
        {
            throw new InvalidOperationException(
                $"Query-plan capacity {_queryPlans.Length} is exhausted.");
        }

        ref readonly ComponentMetadata metadata = ref _schema.GetMetadata(componentId);
        var plan = new ComponentLookupPlan<TArena, T>(
            Id,
            _queryPlanCount,
            componentId,
            metadata.Flags);
        _queryPlans[_queryPlanCount++] = plan;
        return plan;
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

    internal ChangeCursor CreateChangeCursorFromBeginning()
    {
        EnsureSealed();
        return new ChangeCursor(Id, ChangeVersion.None);
    }

    internal ChangeCursor CreateChangeCursorFromNow()
    {
        EnsureSealed();
        return new ChangeCursor(Id, _changeVersion);
    }

    internal ArchetypeChangedReadQueryPlan<TArena, TRead> PrepareChangedReadQuery<TRead>(
        ComponentId generatedReadComponentId,
        ComponentId changedComponentId)
        where TRead : unmanaged, IComponent<TRead>
    {
        ArchetypeQueryFilter filter = ArchetypeQueryFilter.Create(
            Id,
            _schema,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty);
        return PrepareChangedReadQuery<TRead>(
            generatedReadComponentId,
            changedComponentId,
            filter,
            tracksExactRows: false);
    }

    internal ArchetypeChangedReadQueryPlan<TArena, TRead> PrepareChangedReadQuery<TRead>(
        ComponentId generatedReadComponentId,
        ComponentId changedComponentId,
        in ArchetypeQueryFilter filter)
        where TRead : unmanaged, IComponent<TRead>
        => PrepareChangedReadQuery<TRead>(
            generatedReadComponentId,
            changedComponentId,
            filter,
            tracksExactRows: false);

    internal ArchetypeChangedReadQueryPlan<TArena, TRead> PrepareChangedRowsQuery<TRead>(
        ComponentId generatedReadComponentId,
        ComponentId changedComponentId)
        where TRead : unmanaged, IComponent<TRead>
    {
        ArchetypeQueryFilter filter = ArchetypeQueryFilter.Create(
            Id,
            _schema,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty,
            ReadOnlySpan<ComponentId>.Empty);
        return PrepareChangedReadQuery<TRead>(
            generatedReadComponentId,
            changedComponentId,
            filter,
            tracksExactRows: true);
    }

    internal ArchetypeChangedReadQueryPlan<TArena, TRead> PrepareChangedRowsQuery<TRead>(
        ComponentId generatedReadComponentId,
        ComponentId changedComponentId,
        in ArchetypeQueryFilter filter)
        where TRead : unmanaged, IComponent<TRead> =>
        PrepareChangedReadQuery<TRead>(
            generatedReadComponentId,
            changedComponentId,
            filter,
            tracksExactRows: true);

    private ArchetypeChangedReadQueryPlan<TArena, TRead> PrepareChangedReadQuery<TRead>(
        ComponentId generatedReadComponentId,
        ComponentId changedComponentId,
        in ArchetypeQueryFilter filter,
        bool tracksExactRows)
        where TRead : unmanaged, IComponent<TRead>
    {
        EnsureCreated();
        if (!filter.IsValid || filter.WorldId != Id)
            throw new ArgumentException("The query filter belongs to a different world.", nameof(filter));
        ref readonly ComponentMetadata readMetadata = ref ValidateGeneratedDataComponent<TRead>(
            generatedReadComponentId,
            nameof(generatedReadComponentId));
        ref readonly ComponentMetadata changedMetadata = ref ValidateTrackedComponent(
            changedComponentId,
            nameof(changedComponentId));
        if (filter.Excludes(generatedReadComponentId) || filter.Excludes(changedComponentId))
        {
            throw new ArgumentException(
                "A read or changed component cannot also be excluded by its query filter.",
                nameof(filter));
        }
        if (_queryPlanCount == _queryPlans.Length)
        {
            throw new InvalidOperationException(
                $"Query-plan capacity {_queryPlans.Length} is exhausted.");
        }

        var plan = new ArchetypeChangedReadQueryPlan<TArena, TRead>(
            Id,
            _queryPlanCount,
            generatedReadComponentId,
            readMetadata.Flags,
            changedComponentId,
            changedMetadata.Flags,
            tracksExactRows,
            filter);
        _queryPlans[_queryPlanCount++] = plan;
        return plan;
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
        if (_state is EcsWorldState.Sealed or EcsWorldState.Initialized or EcsWorldState.Running)
            return;
        if (_state == EcsWorldState.Faulted)
            throw new InvalidOperationException("A faulted world cannot be sealed.", _faultException);
        if (_state != EcsWorldState.Created)
            ThrowWorldInvariant("The world has an unknown lifecycle state.");

        try
        {
            for (int index = 0; index < _queryPlanCount; index++)
            {
                IArchetypeQueryPlan<TArena> plan = _queryPlans[index]
                    ?? throw new InvalidOperationException("A registered query-plan slot is empty.");
                plan.Compile(_archetypes);
            }
            _archetypes.Seal();
            _hasSealed = true;
            _state = EcsWorldState.Sealed;
        }
        catch (Exception exception)
        {
            _faultException = exception;
            _state = EcsWorldState.Faulted;
            throw;
        }
    }

    internal void Initialize()
    {
        ThrowIfDisposed();
        if (_state is EcsWorldState.Initialized or EcsWorldState.Running)
            return;
        if (_state == EcsWorldState.Faulted)
            throw new InvalidOperationException("A faulted world cannot be initialized.", _faultException);
        if (_state != EcsWorldState.Sealed)
            throw new InvalidOperationException("World initialization requires an explicitly sealed world.");

        _state = EcsWorldState.Initialized;
    }

    internal void BeginRun()
    {
        ThrowIfDisposed();
        if (_state == EcsWorldState.Running)
            return;
        if (_state == EcsWorldState.Faulted)
            throw new InvalidOperationException("A faulted world cannot begin running.", _faultException);
        if (_state != EcsWorldState.Initialized)
            throw new InvalidOperationException("A world must be initialized before it can begin running.");

        _state = EcsWorldState.Running;
    }

    internal void MarkFaulted(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ThrowIfDisposed();
        if (_isExecutingScope)
            throw new InvalidOperationException("The world cannot enter a faulted state during an ECS execution scope.");
        if (_state == EcsWorldState.Faulted)
            return;
        if (_state == EcsWorldState.Created)
            throw new InvalidOperationException("An unsealed world cannot enter runtime fault handling.");

        _faultException = exception;
        _state = EcsWorldState.Faulted;
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

    /// <summary>Creates directly into a row reserved by this structural barrier.</summary>
    internal bool TryCreateDefaultInTemporaryHole(
        ArchetypeId archetypeId,
        ChunkRow row,
        out Entity entity)
    {
        EnsureRuntimeMutationAllowed();
        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        if (!_entities.TryAllocate(out entity))
        {
            entity = Entity.Null;
            return false;
        }

        if (!storage.Store.TryFillTemporaryHole(row, entity.Index))
        {
            if (!_entities.TryFree(entity))
                ThrowWorldInvariant("A failed temporary-hole entity could not be returned.");
            entity = Entity.Null;
            ThrowWorldInvariant("A validated temporary hole rejected publication.");
        }

        var location = new EntityLocation(archetypeId, row);
        if (_entities.TryBindLocation(entity, location))
            return true;

        if (!storage.Store.TryMarkTemporaryHole(row, entity.Index) ||
            !_entities.TryFree(entity))
        {
            ThrowWorldInvariant("Temporary-hole creation rollback failed.");
        }
        entity = Entity.Null;
        ThrowWorldInvariant("A new temporary-hole entity rejected its first location.");
        return false;
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
        storage.Store.StampSparseChangeVersion(
            location.ChunkRow.ChunkIndex,
            binding.ColumnIndex,
            location.ChunkRow.Row,
            rowCount: 1,
            AdvanceChangeVersion());
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
        storage.Store.StampSparseChangeVersion(
            row.ChunkIndex,
            binding.ColumnIndex,
            row.Row,
            rowCount: 1,
            AdvanceChangeVersion());
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
        storage.Store.StampSparseChangeVersion(
            row.ChunkIndex,
            binding.ColumnIndex,
            row.Row,
            rowCount: 1,
            AdvanceChangeVersion());
        return true;
    }

    internal bool HasComponent<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateComponentLookupPlan(lookup, requireData: false, requireEnableable: false);
        return HasComponentPrepared(entity, lookup);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasComponentPrepared<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup)
        where T : unmanaged, IComponent<T> =>
        _entities.TryGetLocation(entity, out EntityLocation location) &&
        lookup.TryGetColumnIndex(location.ArchetypeId, out _);

    internal bool TryGetComponent<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        out T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateComponentLookupPlan(lookup, requireData: true, requireEnableable: false);
        return TryGetComponentPrepared(entity, lookup, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetComponentPrepared<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        out T value)
        where T : unmanaged, IComponent<T>
    {
        if (!lookup.HasData)
            ThrowComponentLookupHasNoData();
        if (!TryResolveLookup(entity, lookup, out ArchetypeStorage<TArena>? storage, out ChunkRow row, out int columnIndex))
        {
            value = default;
            return false;
        }

        value = storage.Store.GetBoundComponentRef<T>(row, columnIndex);
        return true;
    }

    internal bool TrySetComponent<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        in T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateComponentLookupPlan(lookup, requireData: true, requireEnableable: false);
        return TrySetComponentPrepared(entity, lookup, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TrySetComponentPrepared<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        in T value)
        where T : unmanaged, IComponent<T>
    {
        if (!lookup.HasData)
            ThrowComponentLookupHasNoData();
        if (!TryResolveLookup(entity, lookup, out ArchetypeStorage<TArena>? storage, out ChunkRow row, out int columnIndex))
            return false;

        storage.Store.GetBoundComponentRef<T>(row, columnIndex) = value;
        storage.Store.StampSparseChangeVersion(
            row.ChunkIndex,
            columnIndex,
            row.Row,
            rowCount: 1,
            AdvanceChangeVersion());
        return true;
    }

    internal bool TryGetComponentEnabled<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        out bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateComponentLookupPlan(lookup, requireData: false, requireEnableable: true);
        return TryGetComponentEnabledPrepared(entity, lookup, out enabled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetComponentEnabledPrepared<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        out bool enabled)
        where T : unmanaged, IComponent<T>
    {
        if (!lookup.IsEnableable)
            ThrowComponentLookupIsNotEnableable();
        if (!TryResolveLookup(entity, lookup, out ArchetypeStorage<TArena>? storage, out ChunkRow row, out int columnIndex))
        {
            enabled = false;
            return false;
        }

        enabled = storage.Store.IsBoundEnabled(row, columnIndex);
        return true;
    }

    internal bool TrySetComponentEnabled<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateComponentLookupPlan(lookup, requireData: false, requireEnableable: true);
        return TrySetComponentEnabledPrepared(entity, lookup, enabled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TrySetComponentEnabledPrepared<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        bool enabled)
        where T : unmanaged, IComponent<T>
    {
        if (!lookup.IsEnableable)
            ThrowComponentLookupIsNotEnableable();
        if (!TryResolveLookup(entity, lookup, out ArchetypeStorage<TArena>? storage, out ChunkRow row, out int columnIndex))
            return false;

        storage.Store.SetBoundEnabled(row, columnIndex, enabled);
        storage.Store.StampSparseChangeVersion(
            row.ChunkIndex,
            columnIndex,
            row.Row,
            rowCount: 1,
            AdvanceChangeVersion());
        return true;
    }

    internal bool TryCreateInitialized<T>(
        ArchetypeId archetypeId,
        ComponentLookupPlan<TArena, T> lookup,
        in T initialValue,
        out Entity entity)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateComponentLookupPlan(lookup, requireData: true, requireEnableable: false);
        if (!lookup.TryGetColumnIndex(archetypeId, out int columnIndex))
        {
            throw new ArgumentException(
                "The initialization archetype does not contain the lookup component.",
                nameof(archetypeId));
        }
        if (!TryCreateDefaultCore(archetypeId, out entity, out EntityLocation location))
            return false;

        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        storage.Store.GetBoundComponentRef<T>(location.ChunkRow, columnIndex) = initialValue;
        storage.Store.StampSparseChangeVersion(
            location.ChunkRow.ChunkIndex,
            columnIndex,
            location.ChunkRow.Row,
            rowCount: 1,
            AdvanceChangeVersion());
        return true;
    }

    internal ChangeVersion GetChunkChangeVersion<T>(
        in ArchetypeComponentBinding<T> binding,
        uint chunkIndex)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: false);
        return _archetypes.GetStorage(binding.ArchetypeId).Store.GetChangeVersion(
            chunkIndex,
            binding.ColumnIndex);
    }

    internal bool HasChunkChangedSince<T>(
        in ArchetypeComponentBinding<T> binding,
        uint chunkIndex,
        ChangeVersion baseline)
        where T : unmanaged, IComponent<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: false);
        return _archetypes.GetStorage(binding.ArchetypeId).Store.HasChangedSince(
            chunkIndex,
            binding.ColumnIndex,
            baseline);
    }

    /// <summary>
    /// Visits only chunks whose selected tracked component is newer than the
    /// supplied independent cursor. The cursor advances to the observation
    /// boundary only after every visitor returns successfully.
    /// </summary>
    internal int VisitChangedReadChunks<TRead, TVisitor>(
        ArchetypeChangedReadQueryPlan<TArena, TRead> plan,
        ref ChangeCursor cursor,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TVisitor : struct, IComponentChunkReadVisitor<TRead>
    {
        EnsureSealed();
        ValidateChangedReadQueryPlan(plan);
        ValidateChangeCursor(cursor);
        ChangeVersion observationVersion = _changeVersion;
        ChangeVersion baseline = cursor.Version;
        EnterExecutionScope();
        try
        {
            int visitedRows = 0;
            ReadOnlySpan<ArchetypeChangedReadQueryRoute<TArena>> routes = plan.Routes;
            for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
            {
                ref readonly ArchetypeChangedReadQueryRoute<TArena> route = ref routes[routeIndex];
                ArchetypeChunkStore<TArena> store = route.Storage.Store;
                for (int chunkIndex = store.GetFirstOccupiedChunkIndex();
                     chunkIndex >= 0;
                     chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex))
                {
                    uint occupiedChunkIndex = (uint)chunkIndex;
                    if (!store.HasBoundColumnChangedSince(
                        occupiedChunkIndex,
                        route.ChangedColumnIndex,
                        baseline))
                    {
                        continue;
                    }

                    BoundComponentChunk<TRead> bound = store.GetBoundComponentChunk<TRead>(
                        chunkIndex,
                        route.ReadColumnIndex,
                        plan.ReadIsEnableable);
                    if (!route.HasRowFilters)
                    {
                        var chunk = new ReadOnlyComponentChunk<TRead>(
                            Id,
                            route.Storage.Id,
                            bound.ChunkIndex,
                            rowOffset: 0,
                            bound.EntityIndices,
                            bound.Components,
                            bound.EnableWords);
                        visitor.Execute(in chunk);
                        visitedRows += chunk.Count;
                    }
                    else
                    {
                        visitedRows += VisitFilteredChangedReadChunk(
                            route,
                            bound,
                            ref visitor);
                    }
                }
            }

            cursor = cursor.AdvanceTo(observationVersion);
            return visitedRows;
        }
        finally
        {
            ExitExecutionScope();
        }
    }

    /// <summary>
    /// Visits only exact physical rows changed since the supplied cursor. A cheap
    /// per-chunk version gate runs first; dense writes return the occupied chunk,
    /// while scalar and filtered writes are refined through preallocated row stamps.
    /// </summary>
    internal int VisitChangedReadRows<TRead, TVisitor>(
        ArchetypeChangedReadQueryPlan<TArena, TRead> plan,
        ref ChangeCursor cursor,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TVisitor : struct, IComponentChunkReadVisitor<TRead>
    {
        EnsureSealed();
        ValidateChangedReadQueryPlan(plan);
        if (!plan.TracksExactRows)
        {
            throw new ArgumentException(
                "Exact changed-row traversal requires a plan prepared with PrepareChangedRowsQuery.",
                nameof(plan));
        }
        ValidateChangeCursor(cursor);
        ChangeVersion observationVersion = _changeVersion;
        ChangeVersion baseline = cursor.Version;
        EnterExecutionScope();
        try
        {
            int visitedRows = 0;
            ReadOnlySpan<ArchetypeChangedReadQueryRoute<TArena>> routes = plan.Routes;
            for (int routeIndex = 0; routeIndex < routes.Length; routeIndex++)
            {
                ref readonly ArchetypeChangedReadQueryRoute<TArena> route = ref routes[routeIndex];
                if (!route.HasSparseRowChangeVersions)
                    ThrowWorldInvariant("An exact changed-row route has no sparse row state.");
                ArchetypeChunkStore<TArena> store = route.Storage.Store;
                for (int chunkIndex = store.GetFirstOccupiedChunkIndex();
                     chunkIndex >= 0;
                     chunkIndex = store.GetNextOccupiedChunkIndex(chunkIndex))
                {
                    uint occupiedChunkIndex = (uint)chunkIndex;
                    if (!store.HasBoundColumnChangedSince(
                        occupiedChunkIndex,
                        route.ChangedColumnIndex,
                        baseline))
                    {
                        continue;
                    }

                    BoundComponentChunk<TRead> bound = store.GetBoundComponentChunk<TRead>(
                        chunkIndex,
                        route.ReadColumnIndex,
                        plan.ReadIsEnableable);
                    visitedRows += VisitExactChangedReadChunk(
                        route,
                        bound,
                        baseline,
                        ref visitor);
                }
            }

            cursor = cursor.AdvanceTo(observationVersion);
            return visitedRows;
        }
        finally
        {
            ExitExecutionScope();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int VisitExactChangedReadChunk<TRead, TVisitor>(
        in ArchetypeChangedReadQueryRoute<TArena> route,
        in BoundComponentChunk<TRead> bound,
        ChangeVersion baseline,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TVisitor : struct, IComponentChunkReadVisitor<TRead>
    {
        int rowCount = bound.EntityIndices.Length;
        int pendingStart = -1;
        int pendingEnd = -1;
        int matchedRows = 0;
        int wordCount = (rowCount + 63) >> 6;
        for (int wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            int wordStart = wordIndex << 6;
            int activeRows = Math.Min(64, rowCount - wordStart);
            ulong mask = route.GetChangedRowMask(
                bound.ChunkIndex,
                wordIndex,
                activeRows,
                baseline);
            if (route.HasRowFilters)
                mask &= route.GetMatchingRowMask(bound.ChunkBytes, wordIndex);

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
                    ExecuteChangedReadRange(
                        route,
                        bound,
                        pendingStart,
                        pendingEnd - pendingStart,
                        ref visitor);
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
            ExecuteChangedReadRange(
                route,
                bound,
                pendingStart,
                pendingEnd - pendingStart,
                ref visitor);
            matchedRows += pendingEnd - pendingStart;
        }
        return matchedRows;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int VisitFilteredChangedReadChunk<TRead, TVisitor>(
        in ArchetypeChangedReadQueryRoute<TArena> route,
        in BoundComponentChunk<TRead> bound,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TVisitor : struct, IComponentChunkReadVisitor<TRead>
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
                    ExecuteChangedReadRange(
                        route,
                        bound,
                        pendingStart,
                        pendingEnd - pendingStart,
                        ref visitor);
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
            ExecuteChangedReadRange(
                route,
                bound,
                pendingStart,
                pendingEnd - pendingStart,
                ref visitor);
            matchedRows += pendingEnd - pendingStart;
        }
        return matchedRows;
    }

    [MethodImpl(
        MethodImplOptions.AggressiveInlining |
        MethodImplOptions.AggressiveOptimization)]
    private void ExecuteChangedReadRange<TRead, TVisitor>(
        in ArchetypeChangedReadQueryRoute<TArena> route,
        in BoundComponentChunk<TRead> bound,
        int rowOffset,
        int rowCount,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TVisitor : struct, IComponentChunkReadVisitor<TRead>
    {
        var chunk = new ReadOnlyComponentChunk<TRead>(
            Id,
            route.Storage.Id,
            bound.ChunkIndex,
            rowOffset,
            bound.EntityIndices.Slice(rowOffset, rowCount),
            bound.Components.Slice(rowOffset, rowCount),
            bound.EnableWords);
        visitor.Execute(in chunk);
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
        EnterExecutionScope();
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
                    rowOffset: 0,
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
            ExitExecutionScope();
        }
    }

    /// <summary>
    /// Synchronously visits writable occupied chunks of one exact-archetype data
    /// binding. Dense change tracking marks each visited column once per chunk.
    /// </summary>
    internal int VisitWriteChunks<T, TVisitor>(
        in ArchetypeComponentBinding<T> binding,
        ref TVisitor visitor)
        where T : unmanaged, IComponent<T>
        where TVisitor : struct, IComponentChunkWriteVisitor<T>
    {
        EnsureSealed();
        ValidateBinding(binding, requireData: true);
        EnterExecutionScope();
        try
        {
            ArchetypeChunkStore<TArena> store = _archetypes.GetStorage(binding.ArchetypeId).Store;
            int visitedRows = 0;
            ChangeVersion writeVersion = default;
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
                if (!writeVersion.IsValid)
                    writeVersion = AdvanceChangeVersion();
                store.StampDenseChangeVersion(bound.ChunkIndex, binding.ColumnIndex, writeVersion);
                visitor.Execute(in chunk);
                visitedRows += chunk.Count;
            }

            return visitedRows;
        }
        finally
        {
            ExitExecutionScope();
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
        EnterExecutionScope();
        try
        {
            int visitedRows = 0;
            ChangeVersion writeVersion = default;
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
                        if (!writeVersion.IsValid)
                            writeVersion = AdvanceChangeVersion();
                        store.StampDenseChangeVersion(
                            bound.ChunkIndex,
                            route.WriteColumnIndex,
                            writeVersion);
                        visitor.Execute(in chunk);
                        visitedRows += chunk.Count;
                    }
                    else
                    {
                        visitedRows += VisitFilteredReadWriteChunk(
                            route,
                            bound,
                            ref writeVersion,
                            ref visitor);
                    }
                }
            }

            return visitedRows;
        }
        finally
        {
            ExitExecutionScope();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int VisitFilteredReadWriteChunk<TRead, TWrite, TVisitor>(
        in ArchetypeReadWriteQueryRoute<TArena> route,
        in BoundReadWriteComponentChunk<TRead, TWrite> bound,
        ref ChangeVersion writeVersion,
        ref TVisitor visitor)
        where TRead : unmanaged, IComponent<TRead>
        where TWrite : unmanaged, IComponent<TWrite>
        where TVisitor : struct, IComponentChunkReadWriteVisitor<TRead, TWrite>
    {
        int rowCount = bound.EntityIndices.Length;
        int pendingStart = -1;
        int pendingEnd = -1;
        int matchedRows = 0;
        bool stamped = false;
        bool tracksExactRows = route.Storage.Store.HasSparseRowChangeTracking(
            route.WriteColumnIndex);
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
                    StampFilteredReadWriteRange(
                        route,
                        bound.ChunkIndex,
                        pendingStart,
                        pendingEnd - pendingStart,
                        ref writeVersion,
                        ref stamped,
                        tracksExactRows);
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
            StampFilteredReadWriteRange(
                route,
                bound.ChunkIndex,
                pendingStart,
                pendingEnd - pendingStart,
                ref writeVersion,
                ref stamped,
                tracksExactRows);
            ExecuteReadWriteRange(route, bound, pendingStart, pendingEnd - pendingStart, ref visitor);
            matchedRows += pendingEnd - pendingStart;
        }
        return matchedRows;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StampFilteredReadWriteRange(
        in ArchetypeReadWriteQueryRoute<TArena> route,
        uint chunkIndex,
        int rowOffset,
        int rowCount,
        ref ChangeVersion writeVersion,
        ref bool stamped,
        bool tracksExactRows)
    {
        if (stamped && !tracksExactRows)
            return;
        if (!writeVersion.IsValid)
            writeVersion = AdvanceChangeVersion();
        route.Storage.Store.StampFilteredChangeVersion(
            chunkIndex,
            route.WriteColumnIndex,
            rowOffset,
            rowCount,
            writeVersion,
            stampChunk: !stamped);
        stamped = true;
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

    internal bool TryTransition(
        Entity entity,
        uint transitionId,
        ArchetypeId expectedSourceId,
        ArchetypeId expectedDestinationId,
        out EntityLocation destinationLocation)
    {
        EnsureRuntimeMutationAllowed();
        PreparedArchetypeTransition transition = GetPublicTransition(transitionId);
        if (transition.SourceId != expectedSourceId ||
            transition.DestinationId != expectedDestinationId)
        {
            throw new ArgumentException(
                "The archetype transition handle does not match its prepared route.",
                nameof(transitionId));
        }

        ArchetypeStorage<TArena> source = _archetypes.GetStorage(transition.SourceId);
        ArchetypeStorage<TArena> destination = _archetypes.GetStorage(transition.DestinationId);
        return ArchetypeMigrator.TryMigrate(
            _entities,
            entity,
            source,
            destination,
            transition.Transfers,
            out destinationLocation);
    }

    internal bool TryTransition(Entity entity, uint transitionId, out EntityLocation destinationLocation)
    {
        PreparedArchetypeTransition transition = GetPublicTransition(transitionId);
        return TryTransition(
            entity,
            transitionId,
            transition.SourceId,
            transition.DestinationId,
            out destinationLocation);
    }

    internal PreparedArchetypeTransition GetPublicTransitionForPlayback(
        uint transitionId,
        StructuralTransitionPhase expectedPhase)
    {
        PreparedArchetypeTransition transition = GetPublicTransition(transitionId);
        if (transition.Phase != expectedPhase)
        {
            throw new ArgumentException(
                "The public transition was recorded in the wrong structural phase.",
                nameof(transitionId));
        }
        return transition;
    }

    internal int TransitionBatch(
        ReadOnlySpan<Entity> entities,
        PreparedArchetypeTransition transition,
        out bool capacityExhausted)
    {
        EnsureRuntimeMutationAllowed();
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.WorldId != Id)
        {
            throw new ArgumentException(
                "The prepared transition belongs to a different world.",
                nameof(transition));
        }

        ArchetypeStorage<TArena> source = _archetypes.GetStorage(transition.SourceId);
        ArchetypeStorage<TArena> destination = _archetypes.GetStorage(transition.DestinationId);
        return ArchetypeMigrator.MigrateBatch(
            _entities,
            entities,
            source,
            destination,
            transition.Transfers,
            out capacityExhausted);
    }

    /// <summary>
    /// Performs the cheap eligibility check for a route that may evacuate its
    /// complete source population. Exact destination capacity is required so a
    /// partial-prefix result retains the established grouped fallback semantics.
    /// </summary>
    internal bool TryGetWholeSourceTransitionPopulation(
        Entity firstEntity,
        PreparedArchetypeTransition transition,
        out int population)
    {
        EnsureRuntimeMutationAllowed();
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.WorldId != Id ||
            !_entities.TryGetLocation(firstEntity, out EntityLocation firstLocation) ||
            firstLocation.ArchetypeId != transition.SourceId)
        {
            population = 0;
            return false;
        }

        ArchetypeStorage<TArena> source = _archetypes.GetStorage(transition.SourceId);
        ArchetypeStorage<TArena> destination = _archetypes.GetStorage(transition.DestinationId);
        population = source.Store.Count;
        return population > ArchetypeMigrator.MaximumBatchSize &&
            destination.Store.CanReserveDefault(population);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetTransitionSourceRow(
        Entity entity,
        PreparedArchetypeTransition transition,
        out ChunkRow row)
    {
        if (!_entities.TryGetLocation(entity, out EntityLocation location) ||
            location.ArchetypeId != transition.SourceId)
        {
            row = ChunkRow.Invalid;
            return false;
        }

        row = location.ChunkRow;
        return true;
    }

    internal void TransitionPrevalidatedWholeSourceBlock(
        ReadOnlySpan<Entity> entities,
        PreparedArchetypeTransition transition)
    {
        EnsureRuntimeMutationAllowed();
        ArchetypeStorage<TArena> source = _archetypes.GetStorage(transition.SourceId);
        ArchetypeStorage<TArena> destination = _archetypes.GetStorage(transition.DestinationId);
        ArchetypeMigrator.MigratePrevalidatedWholeSourceBlock(
            _entities,
            entities,
            source,
            destination,
            transition.Transfers);
    }

    internal void CompleteWholeSourceTransition(
        PreparedArchetypeTransition transition,
        int expectedPopulation)
    {
        EnsureRuntimeMutationAllowed();
        ArchetypeStorage<TArena> source = _archetypes.GetStorage(transition.SourceId);
        if (source.Store.Count != expectedPopulation)
            ThrowWorldInvariant("A whole-source transition changed occupancy before finalization.");
        source.Store.ClearAllOccupiedRows();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PreparedArchetypeTransition GetPublicTransition(uint transitionId)
    {
        uint index = unchecked(transitionId - 1U);
        if (index >= unchecked((uint)_publicTransitionCount) ||
            _publicTransitions[index] is not { } transition)
        {
            throw new ArgumentException(
                "The public archetype transition is invalid or was not prepared by this world.",
                nameof(transitionId));
        }

        return transition;
    }

    internal void EnterPreparedComponentAccess<T>(ComponentLookupPlan<TArena, T> lookup)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeMutationAllowed();
        ValidateComponentLookupPlan(lookup, requireData: false, requireEnableable: false);
        EnterExecutionScope();
    }

    internal void ExitPreparedComponentAccess() => ExitExecutionScope();

    internal void ValidateStructuralPlayback() => EnsureRuntimeMutationAllowed();

    internal int GetArchetypeFixedRowBytesForPlayback(ArchetypeId archetypeId)
    {
        EnsureRuntimeMutationAllowed();
        return _archetypes.GetStorage(archetypeId).Layout.FixedRowBytes;
    }

    internal bool IsWholeArchetypeDestroyCandidate(Entity entity, int commandCount)
    {
        if (commandCount <= 0 ||
            !_entities.TryGetLocation(entity, out EntityLocation location) ||
            !_archetypes.TryGetStorage(location.ArchetypeId, out ArchetypeStorage<TArena>? storage))
        {
            return false;
        }
        return storage!.Store.Count == commandCount;
    }

    internal bool IsArchetypeTailDestroyCandidate(
        Entity first,
        Entity last,
        int commandCount)
    {
        if (commandCount <= 0 ||
            !_entities.TryGetLocation(first, out EntityLocation firstLocation) ||
            !_entities.TryGetLocation(last, out EntityLocation lastLocation) ||
            firstLocation.ArchetypeId != lastLocation.ArchetypeId ||
            !_archetypes.TryGetStorage(
                firstLocation.ArchetypeId,
                out ArchetypeStorage<TArena>? storage))
        {
            return false;
        }

        ArchetypeChunkStore<TArena> store = storage!.Store;
        return commandCount < store.Count &&
            store.IsCurrentDonorTail(lastLocation.ChunkRow);
    }

    /// <summary>
    /// Destroys an exact, ordered complete archetype population after validating
    /// the entire logical set. Slot retirement follows supplied command order.
    /// </summary>
    internal bool TryDestroyWholeArchetype(ReadOnlySpan<Entity> entities)
    {
        EnsureRuntimeMutationAllowed();
        if (entities.IsEmpty ||
            !_entities.TryGetLocation(entities[0], out EntityLocation firstLocation) ||
            !_archetypes.TryGetStorage(
                firstLocation.ArchetypeId,
                out ArchetypeStorage<TArena>? storage) ||
            storage!.Store.Count != entities.Length)
        {
            return false;
        }

        ArchetypeStorage<TArena> ownedStorage = storage!;
        ChunkRow previous = ChunkRow.Invalid;
        int chunkDirection = 0;
        for (int index = 0; index < entities.Length; index++)
        {
            Entity entity = entities[index];
            if (!_entities.TryGetLocation(entity, out EntityLocation location) ||
                location.ArchetypeId != ownedStorage.Id ||
                !IsStrictOrderedDestroyRow(
                    previous,
                    location.ChunkRow,
                    ref chunkDirection,
                    index == 0))
            {
                return false;
            }
            System.Diagnostics.Debug.Assert(
                ownedStorage.Store.Contains(location.ChunkRow, entity.Index),
                "A whole-archetype destroy location does not match row ownership.");
            previous = location.ChunkRow;
        }

        ownedStorage.Store.ClearAllOccupiedRows();
        _entities.RetireValidatedLocated(entities);
        return true;
    }

    /// <summary>
    /// Destroys one exact contiguous suffix ending at the current packed donor
    /// tail. Validation completes before storage or entity-pool publication.
    /// </summary>
    internal bool TryDestroyArchetypeTail(ReadOnlySpan<Entity> entities)
    {
        EnsureRuntimeMutationAllowed();
        if (entities.IsEmpty ||
            !_entities.TryGetLocation(entities[0], out EntityLocation firstLocation) ||
            !_archetypes.TryGetStorage(
                firstLocation.ArchetypeId,
                out ArchetypeStorage<TArena>? storage) ||
            entities.Length >= storage!.Store.Count)
        {
            return false;
        }

        ArchetypeStorage<TArena> ownedStorage = storage!;
        if (!ownedStorage.Store.TryCreateTailCursor(
                out int expectedChunkIndex,
                out int expectedRow))
        {
            return false;
        }

        for (int index = entities.Length - 1; index >= 0; index--)
        {
            Entity entity = entities[index];
            if (!_entities.TryGetLocation(entity, out EntityLocation location) ||
                location.ArchetypeId != ownedStorage.Id ||
                !ownedStorage.Store.MatchAndAdvanceTail(
                    location.ChunkRow,
                    ref expectedChunkIndex,
                    ref expectedRow))
            {
                return false;
            }
            System.Diagnostics.Debug.Assert(
                ownedStorage.Store.Contains(location.ChunkRow, entity.Index),
                "A donor-tail destroy location does not match row ownership.");
        }

        ownedStorage.Store.RemoveTailRows(entities.Length);
        _entities.RetireValidatedLocated(entities);
        return true;
    }

    internal bool TryDestroy(Entity entity)
    {
        EnsureRuntimeMutationAllowed();
        if (!_entities.TryGetLocation(entity, out EntityLocation location))
            return false;
        return TryDestroyLocated(entity, location);
    }

    /// <summary>
    /// Retires the logical entity but leaves its row reserved for same-barrier
    /// reuse. The caller owns mandatory dense finalization.
    /// </summary>
    internal bool TryDestroyIntoTemporaryHoleValidated(
        Entity entity,
        EntityLocation location,
        out int fixedRowBytes)
    {
        EnsureRuntimeMutationAllowed();
        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(location.ArchetypeId);
        if (!storage.Store.TryMarkTemporaryHole(
                location.ChunkRow,
                entity.Index))
        {
            fixedRowBytes = 0;
            return false;
        }

        fixedRowBytes = storage.Layout.FixedRowBytes;
        _entities.RetireLocatedIntoTemporaryHoleValidated(entity, location);
        return true;
    }

    internal bool TryCompactTemporaryHoleTail(ArchetypeId archetypeId)
    {
        EnsureRuntimeMutationAllowed();
        return _archetypes.GetStorage(archetypeId).Store.TryRemoveTemporaryHoleTail();
    }

    internal bool TryCompactTemporaryHole(
        ArchetypeId archetypeId,
        ChunkRow row,
        out bool relocatedDonor)
    {
        EnsureRuntimeMutationAllowed();
        ArchetypeStorage<TArena> storage = _archetypes.GetStorage(archetypeId);
        if (!storage.Store.ContainsTemporaryHole(row))
        {
            relocatedDonor = false;
            return false;
        }
        if (!storage.Store.TryRemoveTemporaryHole(row, out ChunkRowRelocation relocation))
            ThrowWorldInvariant("A validated temporary hole rejected dense finalization.");
        _entities.ApplyRelocation(archetypeId, relocation);
        relocatedDonor = relocation.HasValue;
        return true;
    }

    private bool TryDestroyLocated(Entity entity, EntityLocation location)
    {
        // A live pool location is a world-owned invariant. TryRemove remains the
        // authoritative row/entity check, so avoid a registry branch plus a
        // duplicate Contains resolution on every scalar destroy.
        ArchetypeStorage<TArena> ownedStorage =
            _archetypes.GetStorage(location.ArchetypeId);
        if (!ownedStorage.Store.TryRemove(
                location.ChunkRow,
                entity.Index,
                out ChunkRowRelocation relocation))
        {
            ThrowWorldInvariant("A validated archetype row rejected entity destruction.");
        }

        _entities.ApplyRelocationAndRetireValidated(
            entity,
            location,
            ownedStorage.Id,
            relocation);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStrictOrderedDestroyRow(
        ChunkRow previous,
        ChunkRow current,
        ref int chunkDirection,
        bool first)
    {
        if (first)
            return current.IsValid;
        if (current.ChunkIndex == previous.ChunkIndex)
            return current.Row > previous.Row;

        int direction = current.ChunkIndex > previous.ChunkIndex ? 1 : -1;
        if (chunkDirection == 0)
            chunkDirection = direction;
        return direction == chunkDirection;
    }

    public void Dispose()
    {
        if (_state == EcsWorldState.Disposed)
            return;
        if (_isExecutingScope)
            throw new InvalidOperationException("The world cannot be disposed during an ECS execution scope.");

        try
        {
            _archetypes.Dispose();
        }
        finally
        {
            for (int index = 0; index < _queryPlanCount; index++)
                _queryPlans[index]?.Release();
            Array.Clear(_queryPlans);
            Array.Clear(_publicTransitions);
            _faultException = null;
            _state = EcsWorldState.Disposed;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveLookup<T>(
        Entity entity,
        ComponentLookupPlan<TArena, T> lookup,
        [NotNullWhen(true)] out ArchetypeStorage<TArena>? storage,
        out ChunkRow row,
        out int columnIndex)
        where T : unmanaged, IComponent<T>
    {
        columnIndex = -1;
        if (!_entities.TryGetLocation(entity, out EntityLocation location) ||
            !lookup.TryGetColumnIndex(location.ArchetypeId, out columnIndex))
        {
            storage = null;
            row = ChunkRow.Invalid;
            return false;
        }

        storage = _archetypes.GetStorage(location.ArchetypeId);
        row = location.ChunkRow;
        return true;
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

    private ref readonly ComponentMetadata ValidateTrackedComponent(
        ComponentId componentId,
        string parameterName)
    {
        if (!_schema.TryGetMetadata(componentId, out _))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                componentId,
                "The changed component ID is not in this world schema.");
        }
        return ref _schema.GetMetadata(componentId);
    }

    private void ValidateChangeCursor(in ChangeCursor cursor)
    {
        if (!cursor.IsValid)
            throw new ArgumentException("A valid change cursor is required.", nameof(cursor));
        if (cursor.WorldId != Id)
            throw new ArgumentException("The change cursor belongs to a different world.", nameof(cursor));
    }

    private void ValidateChangedReadQueryPlan<TRead>(
        ArchetypeChangedReadQueryPlan<TArena, TRead> plan)
        where TRead : unmanaged, IComponent<TRead>
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.WorldId != Id ||
            unchecked((uint)plan.PlanIndex) >= unchecked((uint)_queryPlanCount) ||
            !ReferenceEquals(_queryPlans[plan.PlanIndex], plan))
        {
            throw new ArgumentException("The changed-read plan belongs to a different world.", nameof(plan));
        }
        if (!plan.IsCompiled)
            ThrowWorldInvariant("A sealed world owns an uncompiled changed-read plan.");
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

    private void ValidateComponentLookupPlan<T>(
        ComponentLookupPlan<TArena, T> lookup,
        bool requireData,
        bool requireEnableable)
        where T : unmanaged, IComponent<T>
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (lookup.WorldId != Id ||
            unchecked((uint)lookup.PlanIndex) >= unchecked((uint)_queryPlanCount) ||
            !ReferenceEquals(_queryPlans[lookup.PlanIndex], lookup))
        {
            throw new ArgumentException("The component lookup belongs to a different world.", nameof(lookup));
        }
        if (!lookup.IsCompiled)
            ThrowWorldInvariant("A sealed world owns an uncompiled component lookup.");
        if (requireData && !lookup.HasData)
            throw new InvalidOperationException("Storage-free tag components cannot be read or written as data.");
        if (requireEnableable && !lookup.IsEnableable)
            throw new InvalidOperationException("The component lookup is not enableable.");
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
        if (_state is EcsWorldState.Sealed or EcsWorldState.Initialized or EcsWorldState.Running)
            return;
        if (_state == EcsWorldState.Faulted)
            throw new InvalidOperationException("A faulted world cannot execute ECS operations.", _faultException);

        throw new InvalidOperationException("World ECS operations require a sealed world.");
    }

    private void EnsureRuntimeMutationAllowed()
    {
        EnsureSealed();
        if (_isExecutingScope)
        {
            throw new InvalidOperationException(
                "World mutation is forbidden while an ECS execution scope is active.");
        }
    }

    private void EnterExecutionScope()
    {
        if (_isExecutingScope)
            throw new InvalidOperationException("ECS execution scopes cannot be nested or re-entered.");
        _isExecutingScope = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ChangeVersion AdvanceChangeVersion()
    {
        _changeVersion = _changeVersion.Next();
        return _changeVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitExecutionScope() => _isExecutingScope = false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowWorldInvariant(string message) =>
        throw new InvalidOperationException(message);

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowComponentLookupHasNoData() =>
        throw new InvalidOperationException("Storage-free tag components cannot be read or written as data.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private static void ThrowComponentLookupIsNotEnableable() =>
        throw new InvalidOperationException("The component lookup is not enableable.");
}
