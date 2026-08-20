using System.Net.Http.Json;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Portal.Services.RegistryApi;

internal sealed class RegistryOwnerClient
{
    private readonly RegistryApiClientCore _core;

    public RegistryOwnerClient(RegistryApiClientCore core)
    {
        _core = core;
    }

    public async Task<OfficialRegistryOwnerClaimResponse> SubmitOwnerClaimAsync(
        OfficialRegistryOwnerClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, "/api/v1/servers/claims")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddPortalTokenHeader(httpRequest);

        OfficialRegistryOwnerClaimResponse? payload = await _core.SendForJsonAsync<OfficialRegistryOwnerClaimResponse>(
            httpRequest,
            cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry claim response was empty.");

        return payload;
    }

    public async Task<ServerDiscoveryDetail> UpdateOwnerMetadataAsync(
        string serverId,
        OfficialRegistryOwnerMetadataUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Put, $"/api/v1/servers/{Uri.EscapeDataString(serverId)}/owner")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddPortalTokenHeader(httpRequest);

        ServerDiscoveryDetail? payload = await _core.SendForJsonAsync<ServerDiscoveryDetail>(httpRequest, cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry update response was empty.");

        return payload;
    }

    public async Task<ServerDiscoveryDetail> UpdateOwnerTokenAsync(
        string serverId,
        OfficialRegistryOwnerTokenUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Put, $"/api/v1/servers/{Uri.EscapeDataString(serverId)}/owner-token")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddPortalTokenHeader(httpRequest);

        ServerDiscoveryDetail? payload = await _core.SendForJsonAsync<ServerDiscoveryDetail>(httpRequest, cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry token update response was empty.");

        return payload;
    }

    public async Task<ServerDiscoveryDetail> UpdateVoteCountAsync(
        string serverId,
        OfficialRegistryVoteCountUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Put, $"/api/v1/servers/{Uri.EscapeDataString(serverId)}/vote-count")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddPortalTokenHeader(httpRequest);

        ServerDiscoveryDetail? payload = await _core.SendForJsonAsync<ServerDiscoveryDetail>(httpRequest, cancellationToken);
        if (payload == null)
            throw new InvalidOperationException("Registry vote count response was empty.");

        return payload;
    }

    public async Task DeleteOwnerClaimedServerAsync(
        string serverId,
        OfficialRegistryOwnerDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = new(HttpMethod.Delete, $"/api/v1/servers/{Uri.EscapeDataString(serverId)}/owner")
        {
            Content = JsonContent.Create(request)
        };

        _core.AddPortalTokenHeader(httpRequest);

        using HttpResponseMessage response = await _core.HttpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
