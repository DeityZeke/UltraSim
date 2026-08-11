namespace UltraSim.ECS;

/// <summary>Cold system identity, scheduling, dependency, and extra-access metadata.</summary>
public sealed class SystemDefinition
{
    public SystemDefinition(
        string key,
        SystemStage stage,
        SystemSchedule schedule = default,
        SystemExecutionPolicy executionPolicy = SystemExecutionPolicy.Auto,
        bool manual = false,
        ulong persistenceId = 0,
        ushort stateSchemaVersion = 0,
        IReadOnlyList<SystemId>? requires = null,
        IReadOnlyList<ComponentId>? additionalReads = null,
        IReadOnlyList<ComponentId>? additionalWrites = null,
        IReadOnlyList<ComponentId>? structuralComponents = null,
        int parallelRowThreshold = 16_384,
        int minimumRowsPerPartition = 4_096,
        bool randomWriteTargetsAreDisjoint = false,
        bool automaticallyStagger = false)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A nonempty canonical system key is required.", nameof(key));
        if ((uint)stage >= (uint)SystemStage.Count)
            throw new ArgumentOutOfRangeException(nameof(stage));
        if (schedule.PeriodTicks == 0)
            schedule = new SystemSchedule(enabled: true, periodTicks: 1, startOffset: 0);
        if (parallelRowThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(parallelRowThreshold));
        if (minimumRowsPerPartition < 1)
            throw new ArgumentOutOfRangeException(nameof(minimumRowsPerPartition));
        if (persistenceId != 0 && stateSchemaVersion == 0)
            throw new ArgumentException("Persistent system state requires a nonzero schema version.", nameof(stateSchemaVersion));
        if (persistenceId == 0 && stateSchemaVersion != 0)
            throw new ArgumentException("A state schema version requires a persistence ID.", nameof(persistenceId));

        Key = key;
        Stage = stage;
        Schedule = schedule;
        ExecutionPolicy = executionPolicy;
        Manual = manual;
        PersistenceId = persistenceId;
        StateSchemaVersion = stateSchemaVersion;
        Requires = Copy(requires);
        AdditionalReads = Copy(additionalReads);
        AdditionalWrites = Copy(additionalWrites);
        StructuralComponents = Copy(structuralComponents);
        ParallelRowThreshold = parallelRowThreshold;
        MinimumRowsPerPartition = minimumRowsPerPartition;
        RandomWriteTargetsAreDisjoint = randomWriteTargetsAreDisjoint;
        AutomaticallyStagger = automaticallyStagger;
    }

    public string Key { get; }
    public SystemStage Stage { get; }
    public SystemSchedule Schedule { get; }
    public SystemExecutionPolicy ExecutionPolicy { get; }
    public bool Manual { get; }
    public ulong PersistenceId { get; }
    public ushort StateSchemaVersion { get; }
    public IReadOnlyList<SystemId> Requires { get; }
    public IReadOnlyList<ComponentId> AdditionalReads { get; }
    public IReadOnlyList<ComponentId> AdditionalWrites { get; }
    public IReadOnlyList<ComponentId> StructuralComponents { get; }
    public int ParallelRowThreshold { get; }
    public int MinimumRowsPerPartition { get; }
    public bool RandomWriteTargetsAreDisjoint { get; }
    public bool AutomaticallyStagger { get; }

    private static T[] Copy<T>(IReadOnlyList<T>? values)
    {
        if (values is null || values.Count == 0)
            return [];
        var copy = new T[values.Count];
        for (int index = 0; index < copy.Length; index++)
            copy[index] = values[index];
        return copy;
    }
}
