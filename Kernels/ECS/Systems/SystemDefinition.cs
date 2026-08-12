namespace UltraSim.ECS;

/// <summary>Cold system identity, cadence, dependency, and capability declaration.</summary>
/// <remarks>
/// Generated job columns are registered automatically by their typed system
/// method. <see cref="AdditionalReads"/> and <see cref="AdditionalWrites"/> mean
/// supplemental random lookup capabilities; they do not describe generated
/// chunk columns. <see cref="StructuralComponents"/> authorizes deferred command
/// recording and does not make topology visible during the same graph.
/// </remarks>
public sealed class SystemDefinition
{
    /// <summary>Creates one cold scheduler declaration.</summary>
    /// <param name="key">Nonempty canonical identity unique within the world.</param>
    /// <param name="stage">Coarse deterministic stage.</param>
    /// <param name="schedule">Cadence; default means enabled every tick at offset zero.</param>
    /// <param name="executionPolicy">Serial, parallel, or measured-threshold automatic execution.</param>
    /// <param name="manual">Whether the system runs only through <see cref="EcsWorld.RunManual"/>.</param>
    /// <param name="persistenceId">Stable persistence identity for runtime state, or zero for none.</param>
    /// <param name="stateSchemaVersion">Nonzero schema version when <paramref name="persistenceId"/> is nonzero.</param>
    /// <param name="requires">Explicit system predecessors whose completed value work must be ordered first.</param>
    /// <param name="additionalReads">Supplemental random-lookup read component IDs.</param>
    /// <param name="additionalWrites">Supplemental random-lookup write component IDs.</param>
    /// <param name="structuralComponents">Components this system may add or remove through its structural writer.</param>
    /// <param name="parallelRowThreshold">Minimum logical rows considered for automatic parallel execution.</param>
    /// <param name="minimumRowsPerPartition">Lower bound used when forming parallel work partitions.</param>
    /// <param name="parallelRandomAccessPolicy">Caller-supplied ownership proof for supplemental random access.</param>
    /// <param name="automaticallyStagger">Whether the scheduler may select a deterministic cadence offset.</param>
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
        ParallelRandomAccessPolicy parallelRandomAccessPolicy = ParallelRandomAccessPolicy.SerialOnly,
        bool automaticallyStagger = false)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A nonempty canonical system key is required.", nameof(key));
        if ((uint)stage >= (uint)SystemStage.Count)
            throw new ArgumentOutOfRangeException(nameof(stage));
        if ((uint)executionPolicy > (uint)SystemExecutionPolicy.Auto)
            throw new ArgumentOutOfRangeException(nameof(executionPolicy));
        if ((uint)parallelRandomAccessPolicy > (uint)ParallelRandomAccessPolicy.PartitionDisjoint)
            throw new ArgumentOutOfRangeException(nameof(parallelRandomAccessPolicy));
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
        ParallelRandomAccessPolicy = parallelRandomAccessPolicy;
        AutomaticallyStagger = automaticallyStagger;
    }

    public string Key { get; }
    public SystemStage Stage { get; }
    public SystemSchedule Schedule { get; }
    public SystemExecutionPolicy ExecutionPolicy { get; }
    public bool Manual { get; }
    public ulong PersistenceId { get; }
    public ushort StateSchemaVersion { get; }
    /// <summary>Gets explicit graph predecessors; structural visibility still waits for the barrier.</summary>
    public IReadOnlyList<SystemId> Requires { get; }

    /// <summary>Gets component IDs authorized for supplemental random lookup reads.</summary>
    public IReadOnlyList<ComponentId> AdditionalReads { get; }

    /// <summary>Gets component IDs authorized for supplemental random lookup writes.</summary>
    public IReadOnlyList<ComponentId> AdditionalWrites { get; }

    /// <summary>Gets component IDs authorized for deferred add/remove commands.</summary>
    public IReadOnlyList<ComponentId> StructuralComponents { get; }
    public int ParallelRowThreshold { get; }
    public int MinimumRowsPerPartition { get; }
    /// <summary>Gets the caller's supplemental random-access ownership policy.</summary>
    public ParallelRandomAccessPolicy ParallelRandomAccessPolicy { get; }
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
