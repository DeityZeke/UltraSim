using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Services.RegistryApi;
using Xunit;

using RegistryOptions = UltraSim.Portal.Options.RegistryOptions;

namespace UltraSim.Portal.Tests.Services;

public sealed class RegistryQueryClientTests
{
    private static RegistryQueryClient CreateClient(
        HttpMessageHandler handler,
        string baseUrl = "http://localhost:5180",
        string? portalApiToken = null,
        string? adminApiToken = null)
    {
        HttpClient httpClient = new(handler);
        IOptions<RegistryOptions> options = Microsoft.Extensions.Options.Options.Create(new RegistryOptions
        {
            BaseUrl = baseUrl,
            PortalApiToken = portalApiToken,
            AdminApiToken = adminApiToken,
        });
        return new RegistryQueryClient(httpClient, options);
    }

    [Fact]
    public void Constructor_InvalidBaseUrl_ThrowsInvalidOperationException()
    {
        HttpClient httpClient = new(new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));

        IOptions<RegistryOptions> options = Microsoft.Extensions.Options.Options.Create(new RegistryOptions
        {
            BaseUrl = "not-a-url",
        });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new RegistryQueryClient(httpClient, options));

        Assert.Contains("not-a-url", ex.Message);
        Assert.Contains("Registry:BaseUrl", ex.Message);
    }

    [Fact]
    public void Constructor_ValidBaseUrl_DoesNotThrow()
    {
        RegistryQueryClient client = CreateClient(
            new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.NotNull(client);
    }

    [Fact]
    public async Task GetAdminServersAsync_SendsAdminTokenHeader()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new List<OfficialRegistryAdminServerRecord>()),
                };
            }),
            adminApiToken: "test-admin-token");

        await client.GetAdminServersAsync();

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("X-Admin-Token"));
        Assert.Equal("test-admin-token", captured.Headers.GetValues("X-Admin-Token").First());
    }

    [Fact]
    public async Task GetAdminServersAsync_NoAdminToken_DoesNotSendHeader()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new List<OfficialRegistryAdminServerRecord>()),
                };
            }),
            adminApiToken: null);

        await client.GetAdminServersAsync();

        Assert.NotNull(captured);
        Assert.False(captured!.Headers.Contains("X-Admin-Token"));
    }

    [Fact]
    public async Task SubmitOwnerClaimAsync_SendsPortalTokenHeader()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new OfficialRegistryOwnerClaimResponse(
                        OfficialRegistryOwnerClaimState.PendingVerification,
                        "srv-1", "reg-1", "Pending")),
                };
            }),
            portalApiToken: "test-portal-token");

        await client.SubmitOwnerClaimAsync(new OfficialRegistryOwnerClaimRequest(
            "owner-1", "Server Name", "1.2.3.4", 7777, "token123"));

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.Contains("X-Portal-Api-Token"));
        Assert.Equal("test-portal-token", captured.Headers.GetValues("X-Portal-Api-Token").First());
    }

    [Fact]
    public async Task SubmitOwnerClaimAsync_DoesNotSendAdminTokenHeader()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new OfficialRegistryOwnerClaimResponse(
                        OfficialRegistryOwnerClaimState.PendingVerification,
                        "srv-1", "reg-1", "Pending")),
                };
            }),
            adminApiToken: "admin-token",
            portalApiToken: "portal-token");

        await client.SubmitOwnerClaimAsync(new OfficialRegistryOwnerClaimRequest(
            "owner-1", "Server Name", "1.2.3.4", 7777, "token123"));

        Assert.NotNull(captured);
        Assert.False(captured!.Headers.Contains("X-Admin-Token"));
    }

    [Fact]
    public async Task GetRuntimeSnapshotAsync_SendsAdminTokenHeader()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create<PortalRegistryRuntimeSnapshot?>(null),
                };
            }),
            adminApiToken: "admin-xyz");

        await client.GetRuntimeSnapshotAsync();

        Assert.True(captured!.Headers.Contains("X-Admin-Token"));
        Assert.Equal("admin-xyz", captured.Headers.GetValues("X-Admin-Token").First());
    }

    [Fact]
    public async Task GetServersAsync_NoQuery_UsesBaseServersPath()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new List<UltraSim.Discovery.ServerDiscoveryRow>()),
                };
            }));

        await client.GetServersAsync(null);

        Assert.NotNull(captured);
        Assert.Equal("/api/v1/servers", captured!.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetServersAsync_WithQuery_AppendsQueryString()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new List<UltraSim.Discovery.ServerDiscoveryRow>()),
                };
            }));

        await client.GetServersAsync(new OfficialRegistryServerQuery { Search = "test" });

        Assert.NotNull(captured);
        Assert.Equal("/api/v1/servers", captured!.RequestUri?.AbsolutePath);
        Assert.Contains("search=test", captured.RequestUri?.Query);
    }

    [Fact]
    public async Task SubmitOwnerClaimAsync_NoPortalToken_DoesNotSendHeader()
    {
        HttpRequestMessage? captured = null;
        RegistryQueryClient client = CreateClient(
            new FakeHandler(req =>
            {
                captured = req;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new OfficialRegistryOwnerClaimResponse(
                        OfficialRegistryOwnerClaimState.PendingVerification,
                        "srv-1", "reg-1", "Pending")),
                };
            }),
            portalApiToken: null);

        await client.SubmitOwnerClaimAsync(new OfficialRegistryOwnerClaimRequest(
            "owner-1", "Server Name", "1.2.3.4", 7777, "token123"));

        Assert.NotNull(captured);
        Assert.False(captured!.Headers.Contains("X-Portal-Api-Token"));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_handler(request));
    }
}
