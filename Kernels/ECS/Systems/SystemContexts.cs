using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Cold initialization context; valid only during one runtime initialization callback.</summary>
/// <remarks>
/// The world may be used for cold/bootstrap work during this callback. Retaining
/// it does not grant graph-time access; direct facade access is rejected while a
/// scheduler graph executes.
/// </remarks>
public readonly struct SystemInitializationContext
{
    internal SystemInitializationContext(EcsWorld world, SystemId systemId)
    {
        World = world;
        SystemId = systemId;
    }

    public EcsWorld World { get; }
    public SystemId SystemId { get; }
}

/// <summary>Owner-thread shutdown context valid only during runtime shutdown.</summary>
public readonly struct SystemShutdownContext
{
    internal SystemShutdownContext(EcsWorld world, SystemId systemId)
    {
        World = world;
        SystemId = systemId;
    }

    public EcsWorld World { get; }
    public SystemId SystemId { get; }
}

/// <summary>Read-only invocation metadata supplied before a job is dispatched.</summary>
/// <remarks>No direct world capability is exposed during graph execution.</remarks>
public readonly struct SystemPrepareContext
{
    internal SystemPrepareContext(ulong tick, double deltaSeconds, SystemId systemId, bool manual)
    {
        Tick = tick;
        DeltaSeconds = deltaSeconds;
        SystemId = systemId;
        IsManual = manual;
    }

    public ulong Tick { get; }
    /// <summary>Elapsed simulation seconds represented by this authoritative step.</summary>
    public double DeltaSeconds { get; }
    public SystemId SystemId { get; }
    public bool IsManual { get; }
}

/// <summary>Read-only result metadata supplied after all invocation work items complete.</summary>
/// <remarks>Row, chunk, and work-item counts describe the complete logical invocation.</remarks>
public readonly struct SystemCompleteContext
{
    internal SystemCompleteContext(
        ulong tick,
        double deltaSeconds,
        SystemId systemId,
        int rows,
        int chunks,
        int workItems,
        bool parallel,
        bool manual)
    {
        Tick = tick;
        DeltaSeconds = deltaSeconds;
        SystemId = systemId;
        Rows = rows;
        Chunks = chunks;
        WorkItems = workItems;
        WasParallel = parallel;
        IsManual = manual;
    }

    public ulong Tick { get; }
    /// <summary>Elapsed simulation seconds represented by this authoritative step.</summary>
    public double DeltaSeconds { get; }
    public SystemId SystemId { get; }
    public int Rows { get; }
    public int Chunks { get; }
    public int WorkItems { get; }
    public bool WasParallel { get; }
    public bool IsManual { get; }
}

/// <summary>
/// Expiring scheduler-issued capability. It exposes identity, declared prepared
/// lookups, and deferred structural recording without exposing the world.
/// </summary>
/// <remarks>
/// Valid only during the callback that received it. Do not retain or use it after
/// that callback. Random lookup is intended for sparse referenced entities and is
/// authorized only by the system's supplemental access declarations. Generated
/// component access does not implicitly authorize random access to the same type.
/// </remarks>
public readonly struct SystemJobContext
{
    private readonly SystemScheduler? _scheduler;
    private readonly ulong _epoch;
    private readonly int _participant;
    private readonly int _logicalWorkOrdinal;

    internal SystemJobContext(
        SystemScheduler scheduler,
        ulong epoch,
        SystemId systemId,
        ulong tick,
        double deltaSeconds,
        int participant,
        int logicalWorkOrdinal)
    {
        _scheduler = scheduler;
        _epoch = epoch;
        SystemId = systemId;
        Tick = tick;
        DeltaSeconds = deltaSeconds;
        _participant = participant;
        _logicalWorkOrdinal = logicalWorkOrdinal;
    }

    public SystemId SystemId { get; }
    public ulong Tick { get; }
    /// <summary>Elapsed simulation seconds represented by this authoritative step.</summary>
    public double DeltaSeconds { get; }
    public int ParticipantIndex => _participant;
    public int LogicalWorkOrdinal => _logicalWorkOrdinal;
    /// <summary>Gets the callback-local deferred structural writer for this work item.</summary>
    public SystemStructuralWriter Commands => new(this);

    /// <summary>Reconstructs a full generation-safe entity only when a later operation needs it.</summary>
    /// <remarks>The local index alone is cheaper for chunk-local identity/checksum work.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEntity(uint localEntityIndex, out Entity entity) =>
        GetScheduler().TryResolveEntity(this, localEntityIndex, out entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(Entity entity, in ComponentLookup<T> lookup)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().Has(this, entity, lookup);

    /// <summary>Performs one declaration-checked sparse random read.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead<T>(Entity entity, in ComponentLookup<T> lookup, out T value)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().TryRead(this, entity, lookup, out value);

    /// <summary>Performs one declaration-checked sparse random write.</summary>
    /// <remarks>
    /// Parallel safety depends on <see cref="ParallelRandomAccessPolicy"/>. Prefer
    /// generated writable columns for topology-selected mutation.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite<T>(Entity entity, in ComponentLookup<T> lookup, in T value)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().TryWrite(this, entity, lookup, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEnabled<T>(Entity entity, in ComponentLookup<T> lookup, out bool enabled)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().TryGetEnabled(this, entity, lookup, out enabled);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetEnabled<T>(Entity entity, in ComponentLookup<T> lookup, bool enabled)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().TrySetEnabled(this, entity, lookup, enabled);

    internal ulong Epoch => _epoch;
    internal int Participant => _participant;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SystemScheduler GetScheduler() => _scheduler ??
        throw new InvalidOperationException("The system job context is invalid or default initialized.");
}

/// <summary>Restricted single-writer structural capability for one logical work item.</summary>
/// <remarks>
/// Commands are bounded, do not mutate topology immediately, and merge in stable
/// logical work order for the post-graph barrier. A deferred token created here
/// is local to this work segment and cannot be consumed by another segment.
/// </remarks>
/// <example>Record adding a Poisoned component after combat determines a successful hit.</example>
public readonly struct SystemStructuralWriter
{
    private readonly SystemJobContext _context;

    internal SystemStructuralWriter(in SystemJobContext context) => _context = context;

    /// <summary>Records a default create and returns a same-segment deferred token.</summary>
    public bool TryCreateDefault(ArchetypeHandle archetype, out DeferredEntity entity) =>
        _context.GetScheduler().TryEnqueueCreate(_context, archetype, out entity);

    public DeferredEntity CreateDefault(ArchetypeHandle archetype)
    {
        if (!TryCreateDefault(archetype, out DeferredEntity entity))
            throw new StructuralCommandCapacityExceededException(
                _context.GetScheduler().StructuralCommandsPerProducer);
        return entity;
    }

    /// <summary>Records one compiled prefab instantiation in this work segment.</summary>
    public bool TryInstantiate(PrefabHandle prefab, out DeferredEntity entity) =>
        _context.GetScheduler().TryEnqueueInstantiate(_context, prefab, out entity);

    public DeferredEntity Instantiate(PrefabHandle prefab)
    {
        if (!TryInstantiate(prefab, out DeferredEntity entity))
            throw new StructuralCommandCapacityExceededException(
                _context.GetScheduler().StructuralCommandsPerProducer);
        return entity;
    }

    /// <summary>Records destruction for the post-graph structural barrier.</summary>
    public bool TryDestroy(Entity entity) =>
        _context.GetScheduler().TryEnqueueDestroy(_context, entity);

    public bool TryDestroy(DeferredEntity entity) =>
        _context.GetScheduler().TryEnqueueDestroy(_context, entity);

    /// <summary>Records a prepared membership transition for the post-graph barrier.</summary>
    public bool TryTransition(Entity entity, in ArchetypeTransition transition) =>
        _context.GetScheduler().TryEnqueueTransition(_context, entity, transition);

    public bool TryTransition(DeferredEntity entity, in ArchetypeTransition transition) =>
        _context.GetScheduler().TryEnqueueTransition(_context, entity, transition);

    /// <summary>Records adding or replacing one component value after the graph.</summary>
    public bool TryAdd<T>(Entity entity, in T value)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueAdd(_context, entity, value);

    public bool TryAdd<T>(DeferredEntity entity, in T value)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueAdd(_context, entity, value);

    /// <summary>Records removing one component after the graph.</summary>
    public bool TryRemove<T>(Entity entity)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueRemove<T>(_context, entity);

    public bool TryRemove<T>(DeferredEntity entity)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueRemove<T>(_context, entity);
}
