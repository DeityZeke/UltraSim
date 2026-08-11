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

public enum SystemExecutionPolicy : byte
{
    Serial = 0,
    Parallel = 1,
    Auto = 2,
}

public enum SchedulerRunMode : byte
{
    Serial = 0,
    Parallel = 1,
}

/// <summary>Proof supplied for supplemental random access during parallel execution.</summary>
public enum ParallelRandomAccessPolicy : byte
{
    /// <summary>Any random mutation or read/write overlap forces serial execution.</summary>
    SerialOnly = 0,

    /// <summary>
    /// Each participant's supplemental reads are disjoint from every other
    /// participant's generated/supplemental writes, and its supplemental writes
    /// are disjoint from every other participant's generated/supplemental reads
    /// and writes.
    /// </summary>
    PartitionDisjoint = 1,
}
