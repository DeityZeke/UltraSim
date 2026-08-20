namespace UltraSim.Server;

public readonly record struct ServerStatusSnapshot(
    string ProfileName,
    ServerApplicationState State,
    bool IsInitialized,
    bool IsRunning,
    bool IsAcceptingConnections,
    long TickCount,
    int ActiveSessions,
    int PeakSessions,
    DateTimeOffset? StartedAtUtc,
    TimeSpan Uptime);
