namespace UltraSim.ECS;

/// <summary>Fixed reservations chosen before an ECS world allocates storage.</summary>
public readonly struct EcsWorldOptions : IEquatable<EcsWorldOptions>
{
    private const int MaximumSupportedPreparedTransitions = 1 << 20;
    private const int MaximumSupportedStructuralCommands = 1 << 24;
    private const int MaximumSupportedLifecycleRecords = 1 << 28;

    public EcsWorldOptions(
        EntityCapacity entityCapacity,
        int maximumArchetypes,
        int maximumPreparedPlans,
        int maximumPreparedTransitions = 0,
        int maximumStructuralCommands = 0,
        EcsSchedulerOptions scheduler = default,
        int lifecycleRecordCapacity = 0,
        PersistenceIdentityOptions persistence = default)
    {
        if (entityCapacity.MaximumEntities <= 0)
            throw new ArgumentException("Entity capacity is invalid or default initialized.", nameof(entityCapacity));
        if (maximumArchetypes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumArchetypes));
        if (maximumPreparedPlans < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPreparedPlans));
        if (maximumPreparedTransitions < 0 ||
            maximumPreparedTransitions > MaximumSupportedPreparedTransitions)
            throw new ArgumentOutOfRangeException(nameof(maximumPreparedTransitions));
        if (maximumStructuralCommands < 0 ||
            maximumStructuralCommands > MaximumSupportedStructuralCommands)
            throw new ArgumentOutOfRangeException(nameof(maximumStructuralCommands));
        if (lifecycleRecordCapacity < 0 ||
            lifecycleRecordCapacity > MaximumSupportedLifecycleRecords)
            throw new ArgumentOutOfRangeException(nameof(lifecycleRecordCapacity));

        EntityCapacity = entityCapacity;
        MaximumArchetypes = maximumArchetypes;
        MaximumPreparedPlans = maximumPreparedPlans;
        MaximumPreparedTransitions = maximumPreparedTransitions;
        MaximumStructuralCommands = maximumStructuralCommands;
        Scheduler = scheduler;
        LifecycleRecordCapacity = lifecycleRecordCapacity;
        Persistence = persistence;
    }

    public EntityCapacity EntityCapacity { get; }

    public int MaximumArchetypes { get; }

    /// <summary>
    /// Maximum number of seal-compiled query and random-lookup plans. The backing
    /// table is allocated by world construction and never grows.
    /// </summary>
    public int MaximumPreparedPlans { get; }

    /// <summary>Maximum number of cold-prepared public structural transitions.</summary>
    public int MaximumPreparedTransitions { get; }

    /// <summary>Capacity of the fixed main-thread deferred structural arena.</summary>
    public int MaximumStructuralCommands { get; }

    /// <summary>
    /// Fixed system/scheduler reservations. A default value leaves the direct
    /// 3.1.1 world completely scheduler-free.
    /// </summary>
    public EcsSchedulerOptions Scheduler { get; }

    /// <summary>
    /// Capacity of the fixed lifecycle-history ring. Zero disables lifecycle
    /// retention and preserves the pre-3.2 publication cost model.
    /// </summary>
    public int LifecycleRecordCapacity { get; }

    /// <summary>Optional fixed durable-identity storage used by full-world persistence.</summary>
    public PersistenceIdentityOptions Persistence { get; }

    public bool IsValid =>
        EntityCapacity.MaximumEntities > 0 &&
        MaximumArchetypes > 0 &&
        MaximumPreparedPlans >= 0 &&
        MaximumPreparedTransitions is >= 0 and <= MaximumSupportedPreparedTransitions &&
        MaximumStructuralCommands is >= 0 and <= MaximumSupportedStructuralCommands &&
        LifecycleRecordCapacity is >= 0 and <= MaximumSupportedLifecycleRecords &&
        (!Scheduler.IsEnabled || Scheduler.ExecutionConcurrency > 0);

    public bool Equals(EcsWorldOptions other) =>
        EntityCapacity == other.EntityCapacity &&
        MaximumArchetypes == other.MaximumArchetypes &&
        MaximumPreparedPlans == other.MaximumPreparedPlans &&
        MaximumPreparedTransitions == other.MaximumPreparedTransitions &&
        MaximumStructuralCommands == other.MaximumStructuralCommands &&
        Scheduler == other.Scheduler &&
        LifecycleRecordCapacity == other.LifecycleRecordCapacity &&
        Persistence == other.Persistence;

    public override bool Equals(object? obj) => obj is EcsWorldOptions other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EntityCapacity);
        hash.Add(MaximumArchetypes);
        hash.Add(MaximumPreparedPlans);
        hash.Add(MaximumPreparedTransitions);
        hash.Add(MaximumStructuralCommands);
        hash.Add(Scheduler);
        hash.Add(LifecycleRecordCapacity);
        hash.Add(Persistence);
        return hash.ToHashCode();
    }

    public static bool operator ==(EcsWorldOptions left, EcsWorldOptions right) => left.Equals(right);

    public static bool operator !=(EcsWorldOptions left, EcsWorldOptions right) => !left.Equals(right);
}
