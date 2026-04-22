namespace UltraSim.Server.Network;

public readonly record struct ServerEcsWorldSnapshot(
    uint WorldTick,
    int SystemCount,
    string ExecutionMode,
    bool ProfilingEnabled,
    uint ProfileTick,
    int PreSystemsWorkCount,
    int PostSystemsWorkCount,
    double PreSystemsMilliseconds,
    double SystemsMilliseconds,
    double PostSystemsMilliseconds);
