using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

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
    public double DeltaSeconds { get; }
    public SystemId SystemId { get; }
    public bool IsManual { get; }
}

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
    public double DeltaSeconds { get; }
    public int ParticipantIndex => _participant;
    public int LogicalWorkOrdinal => _logicalWorkOrdinal;
    public SystemStructuralWriter Commands => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEntity(uint localEntityIndex, out Entity entity) =>
        GetScheduler().TryResolveEntity(this, localEntityIndex, out entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(Entity entity, in ComponentLookup<T> lookup)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().Has(this, entity, lookup);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead<T>(Entity entity, in ComponentLookup<T> lookup, out T value)
        where T : unmanaged, IComponent<T> =>
        GetScheduler().TryRead(this, entity, lookup, out value);

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
public readonly struct SystemStructuralWriter
{
    private readonly SystemJobContext _context;

    internal SystemStructuralWriter(in SystemJobContext context) => _context = context;

    public bool TryCreateDefault(ArchetypeHandle archetype, out DeferredEntity entity) =>
        _context.GetScheduler().TryEnqueueCreate(_context, archetype, out entity);

    public DeferredEntity CreateDefault(ArchetypeHandle archetype)
    {
        if (!TryCreateDefault(archetype, out DeferredEntity entity))
            throw new StructuralCommandCapacityExceededException(
                _context.GetScheduler().StructuralCommandsPerProducer);
        return entity;
    }

    public bool TryDestroy(Entity entity) =>
        _context.GetScheduler().TryEnqueueDestroy(_context, entity);

    public bool TryDestroy(DeferredEntity entity) =>
        _context.GetScheduler().TryEnqueueDestroy(_context, entity);

    public bool TryTransition(Entity entity, in ArchetypeTransition transition) =>
        _context.GetScheduler().TryEnqueueTransition(_context, entity, transition);

    public bool TryTransition(DeferredEntity entity, in ArchetypeTransition transition) =>
        _context.GetScheduler().TryEnqueueTransition(_context, entity, transition);

    public bool TryAdd<T>(Entity entity, in T value)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueAdd(_context, entity, value);

    public bool TryAdd<T>(DeferredEntity entity, in T value)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueAdd(_context, entity, value);

    public bool TryRemove<T>(Entity entity)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueRemove<T>(_context, entity);

    public bool TryRemove<T>(DeferredEntity entity)
        where T : unmanaged, IComponent<T> =>
        _context.GetScheduler().TryEnqueueRemove<T>(_context, entity);
}
