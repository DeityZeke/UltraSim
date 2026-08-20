namespace UltraSim.Server.Network;

public readonly record struct ServerEcsWorldPublishResult(
    int WorldCount,
    int SystemCount,
    int DirtyLatestStateCount);
