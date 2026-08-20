using System;

namespace UltraSim.Discovery
{
    public sealed record ServerDiscoveryHistorySample(
        DateTimeOffset CapturedAtUtc,
        int CurrentUsers,
        int PeakUsers,
        TimeSpan? Uptime = null,
        string? Status = null,
        ServerListingHealthState HealthState = ServerListingHealthState.Online);
}
