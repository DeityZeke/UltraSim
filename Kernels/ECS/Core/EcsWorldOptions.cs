namespace UltraSim.ECS;

/// <summary>Fixed reservations chosen before an ECS world allocates storage.</summary>
public readonly struct EcsWorldOptions : IEquatable<EcsWorldOptions>
{
    public EcsWorldOptions(
        EntityCapacity entityCapacity,
        int maximumArchetypes,
        int maximumPreparedPlans)
    {
        if (entityCapacity.MaximumEntities <= 0)
            throw new ArgumentException("Entity capacity is invalid or default initialized.", nameof(entityCapacity));
        if (maximumArchetypes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumArchetypes));
        if (maximumPreparedPlans < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPreparedPlans));

        EntityCapacity = entityCapacity;
        MaximumArchetypes = maximumArchetypes;
        MaximumPreparedPlans = maximumPreparedPlans;
    }

    public EntityCapacity EntityCapacity { get; }

    public int MaximumArchetypes { get; }

    /// <summary>
    /// Maximum number of seal-compiled query and random-lookup plans. The backing
    /// table is allocated by world construction and never grows.
    /// </summary>
    public int MaximumPreparedPlans { get; }

    public bool IsValid =>
        EntityCapacity.MaximumEntities > 0 &&
        MaximumArchetypes > 0 &&
        MaximumPreparedPlans >= 0;

    public bool Equals(EcsWorldOptions other) =>
        EntityCapacity == other.EntityCapacity &&
        MaximumArchetypes == other.MaximumArchetypes &&
        MaximumPreparedPlans == other.MaximumPreparedPlans;

    public override bool Equals(object? obj) => obj is EcsWorldOptions other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        EntityCapacity,
        MaximumArchetypes,
        MaximumPreparedPlans);

    public static bool operator ==(EcsWorldOptions left, EcsWorldOptions right) => left.Equals(right);

    public static bool operator !=(EcsWorldOptions left, EcsWorldOptions right) => !left.Equals(right);
}
