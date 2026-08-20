using System;

namespace UltraSim.Discovery
{
    public sealed record ServerDiscoveryHistorySummary(
        DateTimeOffset? WindowStartUtc = null,
        DateTimeOffset? WindowEndUtc = null,
        int SampleCount = 0,
        double AverageCurrentUsers = 0,
        int MaxCurrentUsers = 0,
        int MaxPeakUsers = 0,
        double OnlineRatio = 0,
        double OfflineRatio = 0);
}
