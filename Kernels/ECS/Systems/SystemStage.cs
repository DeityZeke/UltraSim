namespace UltraSim.ECS;

/// <summary>Coarse, strictly ordered simulation stages.</summary>
public enum SystemStage : byte
{
    Input = 0,
    Movement = 1,
    SpatialIndex = 2,
    Perception = 3,
    Combat = 4,
    AI = 5,
    WorldSim = 6,
    Networking = 7,
    Cleanup = 8,
    Count = 9,
}

/// <summary>Requested execution mode for one system invocation.</summary>
public enum SystemExecutionPolicy : byte
{
    /// <summary>Execute on one participant.</summary>
    Serial = 0,

    /// <summary>Partition eligible work across configured participants.</summary>
    Parallel = 1,

    /// <summary>Use declared row/partition thresholds to avoid unprofitable dispatch.</summary>
    Auto = 2,
}

public enum SchedulerRunMode : byte
{
    Serial = 0,
    Parallel = 1,
}

/// <summary>Ownership proof supplied for supplemental random access during parallel execution.</summary>
/// <remarks>
/// This policy concerns access through prepared lookups, not generated chunk
/// columns. It is a correctness contract supplied by the caller, never a
/// performance hint.
/// </remarks>
public enum ParallelRandomAccessPolicy : byte
{
    /// <summary>Any random mutation or read/write overlap forces serial execution.</summary>
    SerialOnly = 0,

    /// <summary>
    /// Each participant's supplemental reads are disjoint from every other
    /// participant's generated/supplemental writes, and its supplemental writes
    /// are disjoint from every other participant's generated/supplemental reads
    /// and writes. Different loop indices or source entities do not establish
    /// this proof; the complete conflicting target-component domains must be
    /// disjoint for the whole invocation.
    /// </summary>
    PartitionDisjoint = 1,
}
