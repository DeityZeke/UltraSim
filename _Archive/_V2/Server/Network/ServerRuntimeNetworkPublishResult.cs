namespace UltraSim.Server.Network;

public readonly record struct ServerRuntimeNetworkPublishResult(
    int StatusCount,
    int MetricsCount,
    int SystemCount,
    int PlayerCount,
    int AccountCount,
    int EventCount);
