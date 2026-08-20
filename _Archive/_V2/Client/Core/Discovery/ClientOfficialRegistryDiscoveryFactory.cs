using UltraSim.Discovery;
using UltraSim.Discovery.Official;

namespace UltraSim.Client;

public static class ClientOfficialRegistryDiscoveryFactory
{
    public static ServerBrowserProviderDefinition CreateProvider(
        ClientOfficialRegistryDiscoveryOptions? options = null,
        HttpClient? httpClient = null)
    {
        options ??= new ClientOfficialRegistryDiscoveryOptions();
        Uri registryUri = NormalizeRegistryUri(options.RegistryUrl);
        HttpClient client = httpClient ?? new HttpClient();
        client.BaseAddress ??= registryUri;

        var query = new OfficialRegistryDiscoveryQuery(client);
        query.SetQuery(new OfficialRegistryServerQuery
        {
            Limit = Math.Clamp(options.Limit, 1, 250),
            SortBy = string.IsNullOrWhiteSpace(options.SortBy) ? "activity" : options.SortBy.Trim(),
            Descending = options.Descending
        });

        return new ServerBrowserProviderDefinition(
            "official",
            "Official",
            ServerDiscoveryProviderKind.Official,
            query);
    }

    private static Uri NormalizeRegistryUri(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? new ClientOfficialRegistryDiscoveryOptions().RegistryUrl
            : value.Trim();

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Official registry URL must be an absolute http or https URL.", nameof(value));
        }

        return uri;
    }
}
