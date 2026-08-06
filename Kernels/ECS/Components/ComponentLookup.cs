namespace UltraSim.ECS;

/// <summary>
/// Prepared world-wide random access to one component type.
/// </summary>
/// <remarks>
/// Prepare lookups before sealing. Their per-archetype routes are compiled once
/// by <see cref="EcsWorld.Seal"/> and remain valid when entities migrate. Repeated
/// access performs no managed allocation and never returns a mutable reference.
/// The value deliberately does not retain its owning world; pass the world to a
/// safe operation or acquire a stack-only accessor for one execution phase.
/// </remarks>
public readonly struct ComponentLookup<T> : IEquatable<ComponentLookup<T>>
    where T : unmanaged, IComponent<T>
{
    private readonly ComponentLookupPlan<NativeChunkArena, T>? _plan;

    internal ComponentLookup(ComponentLookupPlan<NativeChunkArena, T> plan)
    {
        _plan = plan;
    }

    public bool IsValid => _plan is not null;

    public ComponentId ComponentId => GetPlan().ComponentId;

    public bool HasData => GetPlan().HasData;

    public bool IsEnableable => GetPlan().IsEnableable;

    public int MatchedArchetypeCount => GetPlan().MatchedArchetypeCount;

    public long RouteBytes => GetPlan().RouteBytes;

    /// <summary>
    /// Validates this lookup once and opens a stack-only hot path for the current
    /// single-owner ECS phase.
    /// </summary>
    public ComponentAccessor<T> OpenAccess(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.OpenAccess(this);
    }

    public bool Has(EcsWorld world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Has(entity, this);
    }

    public bool TryRead(EcsWorld world, Entity entity, out T value)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TryRead(entity, this, out value);
    }

    public bool TryWrite(EcsWorld world, Entity entity, in T value)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TryWrite(entity, this, value);
    }

    public bool TryGetEnabled(EcsWorld world, Entity entity, out bool enabled)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TryGetEnabled(entity, this, out enabled);
    }

    public bool TrySetEnabled(EcsWorld world, Entity entity, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TrySetEnabled(entity, this, enabled);
    }

    internal ComponentLookupPlan<NativeChunkArena, T> Plan => GetPlan();

    public bool Equals(ComponentLookup<T> other) =>
        ReferenceEquals(_plan, other._plan);

    public override bool Equals(object? obj) => obj is ComponentLookup<T> other && Equals(other);

    public override int GetHashCode() => _plan?.GetHashCode() ?? 0;

    public static bool operator ==(ComponentLookup<T> left, ComponentLookup<T> right) => left.Equals(right);

    public static bool operator !=(ComponentLookup<T> left, ComponentLookup<T> right) => !left.Equals(right);

    private ComponentLookupPlan<NativeChunkArena, T> GetPlan() => _plan
        ?? throw new InvalidOperationException("The component lookup is invalid or default initialized.");
}
