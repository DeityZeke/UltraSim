namespace UltraSim.Network;

public readonly record struct SessionMetricsSnapshot(
    int TotalSessions,
    int PeakConcurrentSessions,
    int ConnectingSessions,
    int HandshakingSessions,
    int AuthenticatedSessions,
    int ReadySessions,
    int DisconnectingSessions,
    int DisconnectedSessions,
    int CreatedSessions,
    int RejectedSessions,
    int TimeoutDisconnects,
    int RemovedSessions);
