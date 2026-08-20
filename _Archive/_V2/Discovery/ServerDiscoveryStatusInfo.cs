using System;

namespace UltraSim.Discovery
{
    public readonly record struct ServerDiscoveryStatusInfo(
        bool IsAcceptingConnections,
        int CurrentUsers,
        int PeakUsers,
        TimeSpan Uptime,
        string Status,
        int? Rolling24HourUsers = null,
        int? QueueLength = null,
        DateTimeOffset? LastHeartbeatUtc = null);
}
