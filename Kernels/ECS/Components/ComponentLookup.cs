namespace UltraSim.ECS;

/// <summary>
/// Prepared world-wide random access to one component type.
/// </summary>
/// <remarks>
/// Prepare lookups before sealing. Their per-archetype routes are compiled once
/// by <see cref="EcsWorld.Seal"/> and remain valid when entities migrate. Repeated
/// access performs no managed allocation and never returns a mutable reference,
/// but each operation still validates entity generation/liveness, current
/// location, archetype route, and row.
///
/// Use this for sparse or irregular entities reached through relationships,
/// targets, parents, or ownership links. It is not a replacement for generated
/// contiguous chunk traversal. Scanning a population through this API repeats
/// checked random-access work for every row.
///
/// The value deliberately does not retain its owning world; pass the world to a
/// safe scalar operation or execute a callback-bounded access job for one
/// exclusive execution phase.
/// </remarks>
/// <example>Read the current Health component of the entity referenced by a Target.</example>
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
    /// Executes one callback-bounded, stack-only sparse random-access job.
    /// </summary>
    /// <remarks>Use to amortize the outer world/plan boundary across several irregular accesses.</remarks>
    public void Execute<TJob>(EcsWorld world, ref TJob job)
        where TJob : struct, IComponentAccessJob<T>
    {
        ArgumentNullException.ThrowIfNull(world);
        world.ExecuteAccess(this, ref job);
    }

    /// <summary>Checks whether one entity currently has this component.</summary>
    public bool Has(EcsWorld world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Has(entity, this);
    }

    /// <summary>Copies the current value for one entity through checked random access.</summary>
    public bool TryRead(EcsWorld world, Entity entity, out T value)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TryRead(entity, this, out value);
    }

    /// <summary>Copies and publishes one value through checked random access.</summary>
    /// <remarks>Prefer a generated chunk job for dense or composition-selected mutation.</remarks>
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
