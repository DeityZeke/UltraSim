namespace UltraSim.ECS;

/// <summary>
/// Stack-only prepared hot-path access to one world-wide component lookup.
/// </summary>
/// <remarks>
/// Acquisition validates world state, owner thread, and lookup ownership once.
/// Individual operations retain entity generation, location, archetype route,
/// row, and change-tracking checks but do not repeat facade/plan validation.
/// Do not retain an accessor across an ECS barrier or world lifecycle change.
/// </remarks>
public readonly ref struct ComponentAccessor<T>
    where T : unmanaged, IComponent<T>
{
    private readonly EcsWorld<NativeChunkArena> _core;
    private readonly ComponentLookupPlan<NativeChunkArena, T> _plan;

    internal ComponentAccessor(
        EcsWorld<NativeChunkArena> core,
        ComponentLookupPlan<NativeChunkArena, T> plan)
    {
        _core = core;
        _plan = plan;
    }

    public ComponentId ComponentId => _plan.ComponentId;

    public bool HasData => _plan.HasData;

    public bool IsEnableable => _plan.IsEnableable;

    public bool Has(Entity entity) => _core.HasComponentPrepared(entity, _plan);

    public bool TryRead(Entity entity, out T value) =>
        _core.TryGetComponentPrepared(entity, _plan, out value);

    public bool TryWrite(Entity entity, in T value) =>
        _core.TrySetComponentPrepared(entity, _plan, value);

    public bool TryGetEnabled(Entity entity, out bool enabled) =>
        _core.TryGetComponentEnabledPrepared(entity, _plan, out enabled);

    public bool TrySetEnabled(Entity entity, bool enabled) =>
        _core.TrySetComponentEnabledPrepared(entity, _plan, enabled);
}
