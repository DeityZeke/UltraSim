namespace UltraSim.Server.Runtime;

/// <summary>Cold management observation. Policy and idle state come from the owner, not an adapter's copy.</summary>
public readonly record struct ServerRuntimeControlSnapshot(ServerRuntimeState State, ulong ActiveGeneration,
    ulong SimulationTick, long WorldTimeMicroseconds, int SimulationFrequencyHz, int PlaybackRate,
    bool AllowVariablePlayback, bool AllowVariableSimulationFrequency, bool Idle);
