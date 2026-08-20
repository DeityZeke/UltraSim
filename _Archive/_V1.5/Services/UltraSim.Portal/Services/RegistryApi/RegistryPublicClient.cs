using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Json;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Portal.Services.RegistryApi;

internal sealed class RegistryPublicClient
{
    private readonly RegistryApiClientCore _core;

    public RegistryPublicClient(RegistryApiClientCore core)
    {
        _core = core;
    }

    public async Task<IReadOnlyList<ServerDiscoveryRow>> GetServersAsync(
        OfficialRegistryServerQuery? query,
        CancellationToken cancellationToken = default)
    {
        string url = BuildServersUrl(query);
        return await _core.HttpClient.GetFromJsonAsync<List<ServerDiscoveryRow>>(url, cancellationToken)
            ?? [];
    }

    public Task<ServerDiscoveryDetail?> GetServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        return _core.HttpClient.GetFromJsonAsync<ServerDiscoveryDetail>(
            $"/api/v1/servers/{Uri.EscapeDataString(serverId)}",
            cancellationToken);
    }

    private static string BuildServersUrl(OfficialRegistryServerQuery? query)
    {
        if (query == null)
            return "/api/v1/servers";

        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            ["search"] = query.Search,
            ["region"] = query.Region,
            ["language"] = query.Language,
            ["trustLevel"] = query.TrustLevel?.ToString(),
            ["passwordRequired"] = query.PasswordRequired?.ToString(),
            ["whitelistOnly"] = query.WhitelistOnly?.ToString(),
            ["compatibilityId"] = query.CompatibilityId,
            ["sortBy"] = query.SortBy,
            ["descending"] = query.Descending ? "true" : null,
            ["offset"] = query.Offset > 0 ? query.Offset.ToString() : null,
            ["limit"] = query.Limit > 0 ? query.Limit.ToString() : null
        };

        return QueryHelpers.AddQueryString("/api/v1/servers", values!);
    }
}
