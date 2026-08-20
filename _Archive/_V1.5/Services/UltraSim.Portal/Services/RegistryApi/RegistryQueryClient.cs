using Microsoft.Extensions.Options;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Options;

namespace UltraSim.Portal.Services.RegistryApi;

public sealed class RegistryQueryClient
{
    private readonly RegistryPublicClient _publicClient;
    private readonly RegistryOwnerClient _ownerClient;
    private readonly RegistryAdminClient _adminClient;

    public RegistryQueryClient(HttpClient httpClient, IOptions<RegistryOptions> options)
    {
        RegistryApiClientCore core = new(httpClient, options);
        _publicClient = new RegistryPublicClient(core);
        _ownerClient = new RegistryOwnerClient(core);
        _adminClient = new RegistryAdminClient(core);
    }

    public async Task<IReadOnlyList<ServerDiscoveryRow>> GetServersAsync(
        OfficialRegistryServerQuery? query,
        CancellationToken cancellationToken = default)
    {
        return await _publicClient.GetServersAsync(query, cancellationToken);
    }

    public Task<ServerDiscoveryDetail?> GetServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        return _publicClient.GetServerAsync(serverId, cancellationToken);
    }

    public async Task<PortalRegistryRuntimeSnapshot?> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return await _adminClient.GetRuntimeSnapshotAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OfficialRegistryAdminServerRecord>> GetAdminServersAsync(CancellationToken cancellationToken = default)
    {
        return await _adminClient.GetAdminServersAsync(cancellationToken);
    }

    public async Task<OfficialRegistryAdminServerRecord?> GetAdminServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        return await _adminClient.GetAdminServerAsync(serverId, cancellationToken);
    }

    public async Task<IReadOnlyList<OfficialRegistryAdminHostBanRecord>> GetAdminHostBansAsync(CancellationToken cancellationToken = default)
    {
        return await _adminClient.GetAdminHostBansAsync(cancellationToken);
    }

    public async Task<ServerDiscoveryDetail> SetAdminLifecycleAsync(
        string serverId,
        OfficialRegistryLifecycleUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _adminClient.SetAdminLifecycleAsync(serverId, request, cancellationToken);
    }

    public async Task DeleteAdminServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await _adminClient.DeleteAdminServerAsync(serverId, cancellationToken);
    }

    public async Task<OfficialRegistryAdminHostBanRecord> BanAdminServerHostAsync(
        string serverId,
        OfficialRegistryAdminHostBanRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _adminClient.BanAdminServerHostAsync(serverId, request, cancellationToken);
    }

    public async Task LiftAdminHostBanAsync(string host, CancellationToken cancellationToken = default)
    {
        await _adminClient.LiftAdminHostBanAsync(host, cancellationToken);
    }

    public async Task<ServerDiscoveryDetail> ResetAdminRetentionAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        return await _adminClient.ResetAdminRetentionAsync(serverId, cancellationToken);
    }

    public async Task<OfficialRegistryOwnerClaimResponse> SubmitOwnerClaimAsync(
        OfficialRegistryOwnerClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _ownerClient.SubmitOwnerClaimAsync(request, cancellationToken);
    }

    public async Task<ServerDiscoveryDetail> UpdateOwnerMetadataAsync(
        string serverId,
        OfficialRegistryOwnerMetadataUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _ownerClient.UpdateOwnerMetadataAsync(serverId, request, cancellationToken);
    }

    public async Task<ServerDiscoveryDetail> UpdateOwnerTokenAsync(
        string serverId,
        OfficialRegistryOwnerTokenUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _ownerClient.UpdateOwnerTokenAsync(serverId, request, cancellationToken);
    }

    public async Task<ServerDiscoveryDetail> UpdateVoteCountAsync(
        string serverId,
        OfficialRegistryVoteCountUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _ownerClient.UpdateVoteCountAsync(serverId, request, cancellationToken);
    }

    public async Task DeleteOwnerClaimedServerAsync(
        string serverId,
        OfficialRegistryOwnerDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        await _ownerClient.DeleteOwnerClaimedServerAsync(serverId, request, cancellationToken);
    }
}

public sealed record PortalRegistryRuntimeSnapshot(
    DateTimeOffset CapturedAtUtc,
    PortalRegistryStoreRuntimeSnapshot Store,
    PortalRegistryRuntimeMetricsSnapshot Metrics,
    PortalRegistryAggregateMetricsSnapshot Aggregates);

public sealed record PortalRegistryStoreRuntimeSnapshot(
    int TotalRecords,
    int ActiveRecords,
    int DelistedRecords,
    int DisabledRecords,
    int OnlineRecords,
    int StaleRecords,
    int OfflineRecords,
    int PendingRecords,
    int ActiveLeases,
    int PollCandidates,
    int VisibleRows,
    bool PersistenceEnabled,
    bool PendingPersistence);

public sealed record PortalRegistryRuntimeMetricsSnapshot(
    long RegisterCalls,
    long HeartbeatCalls,
    long PresenceCalls,
    long MetadataUpdateCalls,
    long DeleteCalls,
    long LeasePruneCalls,
    long LeasePrunedRecords,
    long PollRounds,
    long PollCandidates,
    long PollSuccesses,
    long PollFailures,
    long PollRoundDurationMsTotal,
    long LastPollRoundDurationMs,
    DateTimeOffset? LastPollRoundCompletedUtc,
    long LifecycleSweeps,
    long LifecycleDelists,
    long LifecyclePurges,
    long LifecycleSweepDurationMsTotal,
    long LastLifecycleSweepDurationMs,
    DateTimeOffset? LastLifecycleSweepUtc,
    long SaveStateCalls,
    long SaveStateFailures,
    long SaveStateBytesTotal,
    long SaveStateDurationMsTotal,
    long LastSaveStateBytes,
    long LastSaveStateDurationMs,
    DateTimeOffset? LastSaveStateUtc);

public sealed record PortalRegistryAggregateMetricsSnapshot(
    int CurrentPlayersTotal,
    int TotalVotes,
    int ServersWithHistory,
    int HistoryBucketCount,
    DateTimeOffset? HistoryWindowStartUtc,
    DateTimeOffset? HistoryWindowEndUtc,
    double AveragePlayersRetained,
    int PeakPlayersRetained,
    double OnlineRatioRetained,
    double OfflineRatioRetained,
    int ServersWithLatency,
    double AverageLatencyMs,
    int MaxLatencyMs,
    int PeakPlayers24Hours,
    double AveragePlayers7Days,
    double AveragePlayers30Days,
    double OnlineRatio7Days,
    double OnlineRatio30Days,
    int AllTimePeakPlayers,
    DateTimeOffset? AllTimePeakPlayersUtc,
    IReadOnlyList<PortalRegistryAggregateHistoryPoint>? RecentAggregatePoints = null);

public sealed record PortalRegistryAggregateHistoryPoint(
    DateTimeOffset CapturedAtUtc,
    int CurrentPlayersTotal,
    double OnlineRatio = 0,
    double OfflineRatio = 0,
    int SampleCount = 0);
