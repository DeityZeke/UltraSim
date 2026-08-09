using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// Engine-neutral, single-owner public facade over the fixed-capacity ECS kernel.
/// </summary>
/// <remarks>
/// Construction and registration are cold. <see cref="Seal"/> is the explicit
/// allocation/plan-compilation boundary, <see cref="Initialize"/> opens bootstrap
/// mutation, and <see cref="BeginRun"/> publishes the running state. This first
/// public slice is deliberately single-threaded; the future scheduler will issue
/// bounded worker access tokens rather than weakening this ownership boundary.
/// </remarks>
public sealed class EcsWorld : IDisposable
{
    private readonly EcsWorld<NativeChunkArena> _core;
    private readonly StructuralCommandBuffer _structuralCommands;
    private readonly int _ownerThreadId;

    public EcsWorld(ComponentSchema schema, EcsWorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (!options.IsValid)
            throw new ArgumentException("World options are invalid or default initialized.", nameof(options));

        Options = options;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _core = new EcsWorld<NativeChunkArena>(
            schema,
            options.EntityCapacity,
            options.MaximumArchetypes,
            static capacity => new NativeChunkArena(capacity),
            options.MaximumPreparedPlans,
            maximumPublicTransitions: options.MaximumPreparedTransitions);
        _structuralCommands = new StructuralCommandBuffer(
            _core.Id,
            _ownerThreadId,
            options.MaximumStructuralCommands);
    }

    public WorldId Id => _core.Id;

    public ComponentSchema Schema => _core.Schema;

    public EcsWorldOptions Options { get; }

    public EcsWorldState State
    {
        get
        {
            EnsureOwnerThread();
            return _core.State;
        }
    }

    public bool IsSealed
    {
        get
        {
            EnsureOwnerThread();
            return _core.IsSealed;
        }
    }

    public bool IsInitialized
    {
        get
        {
            EnsureOwnerThread();
            return _core.IsInitialized;
        }
    }

    public bool IsRunning
    {
        get
        {
            EnsureOwnerThread();
            return _core.IsRunning;
        }
    }

    public bool IsFaulted
    {
        get
        {
            EnsureOwnerThread();
            return _core.State == EcsWorldState.Faulted;
        }
    }

    public bool IsDisposed
    {
        get
        {
            EnsureOwnerThread();
            return _core.IsDisposed;
        }
    }

    public Exception? FaultException
    {
        get
        {
            EnsureOwnerThread();
            return _core.FaultException;
        }
    }

    public int OwnerThreadId => _ownerThreadId;

    public int AliveCount
    {
        get
        {
            EnsureOwnerThread();
            return _core.AliveCount;
        }
    }

    public int ArchetypeCount
    {
        get
        {
            EnsureOwnerThread();
            return _core.ArchetypeCount;
        }
    }

    public int PreparedPlanCount
    {
        get
        {
            EnsureOwnerThread();
            return _core.QueryPlanCount;
        }
    }

    public int PreparedTransitionCount
    {
        get
        {
            EnsureOwnerThread();
            return _core.PublicTransitionCount;
        }
    }

    public ChangeVersion CurrentChangeVersion
    {
        get
        {
            EnsureOwnerThread();
            return _core.CurrentChangeVersion;
        }
    }

    /// <summary>Fixed main-thread command arena reserved by this world.</summary>
    public StructuralCommandBuffer StructuralCommands
    {
        get
        {
            EnsureRuntimeAccess();
            return _structuralCommands;
        }
    }

    public ArchetypeHandle RegisterArchetype(
        ReadOnlySpan<ComponentId> canonicalSignature,
        ChunkLayoutOptions layoutOptions,
        int maximumChunks)
    {
        EnsureOwnerThread();
        ArchetypeId id = _core.RegisterArchetype(canonicalSignature, layoutOptions, maximumChunks);
        return new ArchetypeHandle(Id, id);
    }

    public bool TryGetArchetype(
        ReadOnlySpan<ComponentId> canonicalSignature,
        out ArchetypeHandle archetype)
    {
        EnsureOwnerThread();
        if (_core.TryGetArchetypeId(canonicalSignature, out ArchetypeId id))
        {
            archetype = new ArchetypeHandle(Id, id);
            return true;
        }

        archetype = ArchetypeHandle.Invalid;
        return false;
    }

    public ArchetypeTransition PrepareTransition(
        ArchetypeHandle source,
        ArchetypeHandle destination)
    {
        EnsureOwnerThread();
        ArchetypeId sourceId = ValidateArchetype(source, nameof(source));
        ArchetypeId destinationId = ValidateArchetype(destination, nameof(destination));
        uint transitionId = _core.PreparePublicTransition(
            sourceId,
            destinationId,
            out StructuralTransitionPhase phase);
        return new ArchetypeTransition(Id, transitionId, source, destination, phase);
    }

    public ComponentLookup<T> PrepareLookup<T>()
        where T : unmanaged, IComponent<T>
    {
        EnsureOwnerThread();
        ComponentLookupPlan<NativeChunkArena, T> plan = _core.PrepareComponentLookup<T>();
        return new ComponentLookup<T>(plan);
    }

    /// <summary>Compiles every prepared plan and permanently closes registration.</summary>
    public void Seal()
    {
        EnsureOwnerThread();
        _core.Seal();
    }

    /// <summary>Opens single-owner bootstrap entity mutation after explicit sealing.</summary>
    public void Initialize()
    {
        EnsureOwnerThread();
        _core.Initialize();
    }

    /// <summary>Publishes the initialized world as running. This call is idempotent.</summary>
    public void BeginRun()
    {
        EnsureOwnerThread();
        _core.BeginRun();
    }

    public bool IsAlive(Entity entity)
    {
        EnsureRuntimeAccess();
        return _core.IsAlive(entity);
    }

    public bool TryGetArchetype(Entity entity, out ArchetypeHandle archetype)
    {
        EnsureRuntimeAccess();
        if (_core.TryGetLocation(entity, out EntityLocation location))
        {
            archetype = new ArchetypeHandle(Id, location.ArchetypeId);
            return true;
        }

        archetype = ArchetypeHandle.Invalid;
        return false;
    }

    public bool TryCreate(ArchetypeHandle archetype, out Entity entity)
    {
        EnsureRuntimeAccess();
        return _core.TryCreateDefault(ValidateArchetype(archetype, nameof(archetype)), out entity);
    }

    public bool TryCreate<T>(
        ArchetypeHandle archetype,
        in ComponentLookup<T> initializationLookup,
        in T initialValue,
        out Entity entity)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeAccess();
        return _core.TryCreateInitialized(
            ValidateArchetype(archetype, nameof(archetype)),
            initializationLookup.Plan,
            initialValue,
            out entity);
    }

    public bool TryTransition(Entity entity, in ArchetypeTransition transition)
    {
        EnsureRuntimeAccess();
        if (!transition.IsValid)
            throw new ArgumentException("The archetype transition is invalid or default initialized.", nameof(transition));
        if (transition.WorldId != Id)
            throw new ArgumentException("The archetype transition belongs to a different world.", nameof(transition));

        return _core.TryTransition(
            entity,
            transition.TransitionId,
            transition.SourceId,
            transition.DestinationId,
            out _);
    }

    public bool TryDestroy(Entity entity)
    {
        EnsureRuntimeAccess();
        return _core.TryDestroy(entity);
    }

    /// <summary>
    /// Consumes the current structural epoch in deterministic broad-phase order.
    /// Expected stale/capacity failures are returned as rejection counts. An
    /// unexpected exception consumes the arena and terminally faults the world.
    /// </summary>
    public StructuralPlaybackResult PlaybackStructuralCommands() =>
        PlaybackStructuralCommandsCore(usePhaseIndex: true);

    /// <summary>
    /// Scalar reference implementation retained for differential verification.
    /// It is not part of the public runtime surface.
    /// </summary>
    internal StructuralPlaybackResult PlaybackStructuralCommandsScalarOracle() =>
        PlaybackStructuralCommandsCore(usePhaseIndex: false);

    private StructuralPlaybackResult PlaybackStructuralCommandsCore(bool usePhaseIndex)
    {
        EnsureRuntimeAccess();
        _core.ValidateStructuralPlayback();
        try
        {
            return usePhaseIndex
                ? _structuralCommands.Playback(_core)
                : _structuralCommands.PlaybackScalarOracle(_core);
        }
        catch (Exception exception)
        {
            try
            {
                _core.MarkFaulted(exception);
            }
            finally
            {
                _structuralCommands.Invalidate();
            }
            throw;
        }
    }

    /// <summary>
    /// Executes one callback-bounded sequence of prepared random component
    /// operations. The world rejects mutation outside the supplied accessor,
    /// structural mutation, nested execution, disposal, and lifecycle changes
    /// until the callback returns.
    /// </summary>
    public void ExecuteAccess<T, TJob>(in ComponentLookup<T> lookup, ref TJob job)
        where T : unmanaged, IComponent<T>
        where TJob : struct, IComponentAccessJob<T>
    {
        EnsureRuntimeAccess();
        ComponentLookupPlan<NativeChunkArena, T> plan = lookup.Plan;
        _core.EnterPreparedComponentAccess(plan);
        try
        {
            var access = new ComponentAccessor<T>(_core, plan);
            job.Execute(in access);
        }
        finally
        {
            _core.ExitPreparedComponentAccess();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(Entity entity, in ComponentLookup<T> lookup)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeAccess();
        return _core.HasComponent(entity, lookup.Plan);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead<T>(Entity entity, in ComponentLookup<T> lookup, out T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeAccess();
        return _core.TryGetComponent(entity, lookup.Plan, out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite<T>(Entity entity, in ComponentLookup<T> lookup, in T value)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeAccess();
        return _core.TrySetComponent(entity, lookup.Plan, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEnabled<T>(Entity entity, in ComponentLookup<T> lookup, out bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeAccess();
        return _core.TryGetComponentEnabled(entity, lookup.Plan, out enabled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetEnabled<T>(Entity entity, in ComponentLookup<T> lookup, bool enabled)
        where T : unmanaged, IComponent<T>
    {
        EnsureRuntimeAccess();
        return _core.TrySetComponentEnabled(entity, lookup.Plan, enabled);
    }

    public void Dispose()
    {
        if (_core.IsDisposed)
            return;

        EnsureOwnerThread();
        _core.Dispose();
        _structuralCommands.Invalidate();
    }

    internal void MarkFaulted(Exception exception)
    {
        EnsureOwnerThread();
        _core.MarkFaulted(exception);
        if (_core.State == EcsWorldState.Faulted)
            _structuralCommands.Invalidate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureRuntimeAccess()
    {
        EnsureOwnerThread();
        EcsWorldState state = _core.State;
        if (state is EcsWorldState.Initialized or EcsWorldState.Running)
            return;
        if (state == EcsWorldState.Disposed)
            throw new ObjectDisposedException(nameof(EcsWorld));
        if (state == EcsWorldState.Faulted)
            throw new InvalidOperationException("A faulted world cannot execute ECS operations.", _core.FaultException);

        throw new InvalidOperationException("Public ECS operations require an initialized world.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "This ECS facade is single-owner. Access from a non-owner thread requires a future scheduler token.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArchetypeId ValidateArchetype(ArchetypeHandle archetype, string parameterName)
    {
        if (!archetype.IsValid || archetype.World != Id)
            throw new ArgumentException("The archetype handle belongs to a different world.", parameterName);

        _ = _core.GetArchetypeStorage(archetype.Id);
        return archetype.Id;
    }

}
