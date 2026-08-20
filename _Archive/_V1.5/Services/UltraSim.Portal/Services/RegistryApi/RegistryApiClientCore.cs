using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using UltraSim.Portal.Options;

namespace UltraSim.Portal.Services.RegistryApi;

internal sealed class RegistryApiClientCore
{
    public HttpClient HttpClient { get; }
    public RegistryOptions Options { get; }

    public RegistryApiClientCore(HttpClient httpClient, IOptions<RegistryOptions> options)
    {
        HttpClient = httpClient;
        Options = options.Value;

        if (!Uri.TryCreate(Options.BaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException(
                $"Registry:BaseUrl '{Options.BaseUrl}' is not a valid absolute URI. Check the portal configuration.");
        }

        HttpClient.BaseAddress = baseUri;
    }

    public void AddAdminTokenHeader(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrWhiteSpace(Options.AdminApiToken))
            httpRequest.Headers.Add("X-Admin-Token", Options.AdminApiToken);
    }

    public void AddPortalTokenHeader(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrWhiteSpace(Options.PortalApiToken))
            httpRequest.Headers.Add("X-Portal-Api-Token", Options.PortalApiToken);
    }

    public async Task<T?> SendForJsonAsync<T>(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<T>> SendForJsonListAsync<T>(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: cancellationToken) ?? [];
    }
}
