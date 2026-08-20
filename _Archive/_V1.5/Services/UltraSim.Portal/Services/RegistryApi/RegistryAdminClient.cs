using System.Net.Http.Json;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Portal.Services.RegistryApi;

internal sealed class RegistryAdminClient
{
    private readonly RegistryApiClientCore _core;

    public RegistryAdminClient(RegistryApiClientCore core)
    {
        _core = core;
    }

    public Task<PortalRegistryRuntimeSnapshot?> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, "/api/v1/admin/runtime");
        _core.AddAdminTokenHeader(httpRequest);
        return _core.SendForJsonAsync<PortalRegistryRuntimeSnapshot>(httpRequest, cancellationToken);
    }

    public Task<IReadOnlyList<OfficialRegistryAdminServerRecord>> GetAdminServersAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, "/api/v1/admin/servers");
        _core.AddAdminTokenHeader(httpRequest);
        return _core.SendForJsonListAsync<OfficialRegistryAdminServerRecord>(httpRequest, cancellationToken);
    }

    public Task<OfficialRegistryAdminServerRecord?> GetAdminServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, $"/api/v1/admin/servers/{Uri.EscapeDataString(serverId)}");
        _core.AddAdminTokenHeader(httpRequest);
        return _core.SendForJsonAsync<OfficialRegistryAdminServerRecord>(httpRequest, cancellationToken);
    }

    public Task<IReadOnlyList<OfficialRegistryAdminHostBanRecord>> GetAdminHostBansAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Get, "/api/v1/admin/bans");
        _core.AddAdminTokenHeader(httpRequest);
        return _core.SendForJsonListAsync<OfficialRegistryAdminHostBanRecord>(httpRequest, cancellationToken);
    }

    public async Task<ServerDiscoveryDetail> SetAdminLifecycleAsync(
        string serverId,
        OfficialRegistryLifecycleUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Put, $"/api/v1/admin/servers/{Uri.EscapeDataString(serverId)}/lifecycle")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddAdminTokenHeader(httpRequest);

        ServerDiscoveryDetail? payload = await _core.SendForJsonAsync<ServerDiscoveryDetail>(httpRequest, cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry lifecycle update response was empty.");

        return payload;
    }

    public async Task DeleteAdminServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Delete, $"/api/v1/admin/servers/{Uri.EscapeDataString(serverId)}");
        _core.AddAdminTokenHeader(httpRequest);

        using HttpResponseMessage response = await _core.HttpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<OfficialRegistryAdminHostBanRecord> BanAdminServerHostAsync(
        string serverId,
        OfficialRegistryAdminHostBanRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, $"/api/v1/admin/servers/{Uri.EscapeDataString(serverId)}/host-ban")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddAdminTokenHeader(httpRequest);

        OfficialRegistryAdminHostBanRecord? payload = await _core.SendForJsonAsync<OfficialRegistryAdminHostBanRecord>(
            httpRequest,
            cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry host ban response was empty.");

        return payload;
    }

    public async Task LiftAdminHostBanAsync(string host, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Delete, $"/api/v1/admin/bans/hosts/{Uri.EscapeDataString(host)}");
        _core.AddAdminTokenHeader(httpRequest);

        using HttpResponseMessage response = await _core.HttpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ServerDiscoveryDetail> ResetAdminRetentionAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, $"/api/v1/admin/servers/{Uri.EscapeDataString(serverId)}/retention-reset");
        _core.AddAdminTokenHeader(httpRequest);

        ServerDiscoveryDetail? payload = await _core.SendForJsonAsync<ServerDiscoveryDetail>(httpRequest, cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry retention reset response was empty.");

        return payload;
    }
}
