using System;

namespace UltraSim.Server
{
    public readonly record struct ServerRuntimeStatusSnapshot(
        string ProfileName,
        ServerRuntimeState State,
        bool IsAcceptingConnections,
        int CurrentSessions,
        int PeakSessions,
        DateTimeOffset? StartedAtUtc,
        TimeSpan Uptime,
        string? SaveDirectory);
}
