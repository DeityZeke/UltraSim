using System;

namespace UltraSim.Discovery
{
    public sealed record ServerDiscoveryAdvancedMetricsSummary(
        int PeakPlayers24Hours = 0,
        double AveragePlayers7Days = 0,
        double AveragePlayers30Days = 0,
        double OnlineRatio7Days = 0,
        double OnlineRatio30Days = 0,
        int AllTimePeakPlayers = 0,
        DateTimeOffset? AllTimePeakPlayersUtc = null);
}
