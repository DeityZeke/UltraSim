using System;
using System.Collections.Generic;

namespace UltraSim.Discovery
{
    public sealed record ServerDiscoveryDetail(
        ServerDiscoveryRow Row,
        string? Description = null,
        IReadOnlyList<string>? Languages = null,
        string? WebsiteUrl = null,
        string? DiscordUrl = null,
        bool IsAcceptingConnections = false,
        string? Status = null,
        int? Rolling24HourUsers = null,
        int RecentSampleCapacity = 0,
        IReadOnlyList<ServerDiscoveryHistorySample>? RecentSamples = null,
        IReadOnlyList<ServerDiscoveryHistorySample>? AggregateSamples = null,
        ServerDiscoveryHistorySummary? HistorySummary = null,
        ServerDiscoveryAdvancedMetricsSummary? AdvancedMetrics = null,
        ServerDiscoveryLatencySummary? LatencySummary = null,
        DateTimeOffset? LastSuccessfulPollUtc = null,
        DateTimeOffset? LastFailedPollUtc = null,
        DateTimeOffset? LastExplicitOfflineUtc = null,
        DateTimeOffset? OwnershipClaimRequestedUtc = null,
        string? OwnershipVerificationError = null,
        DateTimeOffset? VerifiedAtUtc = null)
    {
        public IReadOnlyList<string> LanguagesOrEmpty => Languages ?? Array.Empty<string>();
        public IReadOnlyList<ServerDiscoveryHistorySample> RecentSamplesOrEmpty => RecentSamples ?? Array.Empty<ServerDiscoveryHistorySample>();
        public IReadOnlyList<ServerDiscoveryHistorySample> AggregateSamplesOrEmpty => AggregateSamples ?? Array.Empty<ServerDiscoveryHistorySample>();
    }
}
