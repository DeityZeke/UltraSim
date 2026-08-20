using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using Xunit;

namespace UltraSim.Registry.Tests;

public sealed class OfficialRegistryApiTests
{
    [Fact]
    public async Task RegisterUnverified_ExposesListingInQuery()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest request = new(
            DiscoveryPublishMode.Unverified,
            new ServerAdvertisedInfo(
                ServerId: string.Empty,
                DisplayName: "Community Server",
                ConnectHint: "community.test:42042",
                Region: "NA-Central",
                PrimaryLanguage: "en"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 7,
                PeakUsers: 18,
                Uptime: TimeSpan.FromMinutes(22),
                Status: "Running"));

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/v1/servers/register", request);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        OfficialRegistryRegisterResponse? register = await registerResponse.Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>();
        Assert.NotNull(register);
        Assert.Equal(ServerListingTrustLevel.Unverified, register.TrustLevel);
        Assert.False(string.IsNullOrWhiteSpace(register.ServerId));

        ServerDiscoveryDetail? pendingDetail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(pendingDetail);
        Assert.Equal(ServerListingHealthState.Pending, pendingDetail.Row.HealthState);
        Assert.Equal(ServerListingLifecycleState.Active, pendingDetail.Row.LifecycleState);
        Assert.Equal("community.test:42042", pendingDetail.Row.ConnectHint);
        Assert.Equal("en", pendingDetail.Row.PrimaryLanguage);

        HttpResponseMessage onlineResponse = await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(register.HeartbeatToken, OfficialRegistryPresenceSignalKind.Online));
        onlineResponse.EnsureSuccessStatusCode();

        ServerDiscoveryRow[]? rows = await client.GetFromJsonAsync<ServerDiscoveryRow[]>("/api/v1/servers");
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal("Community Server", rows[0].DisplayName);
        Assert.Equal(ServerListingTrustLevel.Unverified, rows[0].TrustLevel);
        Assert.Equal(ServerListingHealthState.Online, rows[0].HealthState);
        Assert.Equal(ServerListingLifecycleState.Active, rows[0].LifecycleState);
        Assert.Equal("community.test:42042", rows[0].ConnectHint);
        Assert.Equal("en", rows[0].PrimaryLanguage);
    }

    [Fact]
    public async Task RegisterVerified_WithConfiguredToken_MarksVerified()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:VerifiedPublishTokens:alpha"] = "secret-token",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest request = new(
            DiscoveryPublishMode.Verified,
            new ServerAdvertisedInfo(
                ServerId: "alpha",
                DisplayName: "Verified Alpha",
                ConnectHint: "alpha.test:42042"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 3,
                PeakUsers: 8,
                Uptime: TimeSpan.FromMinutes(10),
                Status: "Running"),
            PublishToken: "secret-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/servers/register", request);
        response.EnsureSuccessStatusCode();

        OfficialRegistryRegisterResponse? register = await response.Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>();
        Assert.NotNull(register);
        Assert.Equal(ServerListingTrustLevel.Verified, register.TrustLevel);

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal(ServerListingTrustLevel.Verified, detail.Row.TrustLevel);
    }

    [Fact]
    public async Task RegisterVerified_WithInvalidToken_IsRejected()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:VerifiedPublishTokens:alpha"] = "secret-token",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest request = new(
            DiscoveryPublishMode.Verified,
            new ServerAdvertisedInfo(
                ServerId: "alpha",
                DisplayName: "Verified Alpha"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 1,
                PeakUsers: 1,
                Uptime: TimeSpan.Zero,
                Status: "Running"),
            PublishToken: "wrong-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/servers/register", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HeartbeatThenDelete_UpdatesAndRemovesLease()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest registerRequest = new(
            DiscoveryPublishMode.Unverified,
            new ServerAdvertisedInfo(
                ServerId: string.Empty,
                DisplayName: "Pulse Server",
                ConnectHint: "pulse.test:42042"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 2,
                PeakUsers: 4,
                Uptime: TimeSpan.FromMinutes(5),
                Status: "Running"));

        OfficialRegistryRegisterResponse register = (await (await client.PostAsJsonAsync("/api/v1/servers/register", registerRequest))
            .Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>())!;

        OfficialRegistryHeartbeatRequest heartbeat = new(
            HeartbeatToken: register.HeartbeatToken,
            StatusInfo: new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 9,
                PeakUsers: 10,
                Uptime: TimeSpan.FromMinutes(8),
                Status: "Busy",
                QueueLength: 2));

        HttpResponseMessage heartbeatResponse = await client.PutAsJsonAsync($"/api/v1/servers/{register.LeaseId}", heartbeat);
        heartbeatResponse.EnsureSuccessStatusCode();

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal(9, detail.Row.CurrentUsers);
        Assert.Equal(2, detail.Row.QueueLength);
        Assert.Equal(ServerListingHealthState.Online, detail.Row.HealthState);

        using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"/api/v1/servers/{register.LeaseId}");
        deleteRequest.Headers.Add("X-Heartbeat-Token", register.HeartbeatToken);
        HttpResponseMessage deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDelete = await client.GetAsync($"/api/v1/servers/{register.ServerId}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task OfficialQuery_ReturnsRegistryRows()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest registerRequest = new(
            DiscoveryPublishMode.Unverified,
            new ServerAdvertisedInfo(
                ServerId: string.Empty,
                DisplayName: "Query Server",
                ConnectHint: "query.test:42042",
                Region: "EU-West"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 5,
                PeakUsers: 9,
                Uptime: TimeSpan.FromMinutes(3),
                Status: "Running"));

        OfficialRegistryRegisterResponse register = (await (await client.PostAsJsonAsync("/api/v1/servers/register", registerRequest))
            .Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>())!;

        (await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(register.HeartbeatToken, OfficialRegistryPresenceSignalKind.Online)))
            .EnsureSuccessStatusCode();

        OfficialRegistryDiscoveryQuery query = new(client);
        IReadOnlyList<ServerDiscoveryRow> rows = await query.QueryAsync();
        Assert.Single(rows);
        Assert.Equal("Query Server", rows[0].DisplayName);
        Assert.Equal(ServerListingHealthState.Online, rows[0].HealthState);
    }

    [Fact]
    public async Task OfficialPublisher_RegisterHeartbeatAndUnpublish_WorksAgainstRegistry()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:VerifiedPublishTokens:alpha"] = "secret-token",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryDiscoveryPublisher publisher = new(client, DiscoveryPublishMode.Verified, "secret-token");
        ServerAdvertisedInfo advertised = new(
            ServerId: "alpha",
            DisplayName: "Publisher Alpha",
            ConnectHint: "alpha.test:42042");

        await publisher.PublishAsync(
            advertised,
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 1,
                PeakUsers: 2,
                Uptime: TimeSpan.FromMinutes(1),
                Status: "Running"));

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>("/api/v1/servers/alpha");
        Assert.NotNull(detail);
        Assert.Equal("Publisher Alpha", detail.Row.DisplayName);
        Assert.Equal(ServerListingHealthState.Pending, detail.Row.HealthState);

        await publisher.PublishAsync(
            advertised,
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 6,
                PeakUsers: 7,
                Uptime: TimeSpan.FromMinutes(2),
                Status: "Busy"));

        ServerDiscoveryDetail? updated = await client.GetFromJsonAsync<ServerDiscoveryDetail>("/api/v1/servers/alpha");
        Assert.NotNull(updated);
        Assert.Equal(6, updated.Row.CurrentUsers);
        Assert.Equal(ServerListingHealthState.Online, updated.Row.HealthState);

        await publisher.UnpublishAsync("alpha");
        HttpResponseMessage afterDelete = await client.GetAsync("/api/v1/servers/alpha");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task PresenceEndpoint_TransitionsRecordOfflineAndOnline()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest registerRequest = new(
            DiscoveryPublishMode.Unverified,
            new ServerAdvertisedInfo(
                ServerId: string.Empty,
                DisplayName: "Presence Server",
                ConnectHint: "presence.test:42042"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 2,
                PeakUsers: 4,
                Uptime: TimeSpan.FromMinutes(5),
                Status: "Running"));

        OfficialRegistryRegisterResponse register = (await (await client.PostAsJsonAsync("/api/v1/servers/register", registerRequest))
            .Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>())!;

        HttpResponseMessage offlineResponse = await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(register.HeartbeatToken, OfficialRegistryPresenceSignalKind.Offline));
        offlineResponse.EnsureSuccessStatusCode();

        ServerDiscoveryDetail? offline = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(offline);
        Assert.Equal(ServerListingHealthState.Offline, offline.Row.HealthState);

        HttpResponseMessage onlineResponse = await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(
                register.HeartbeatToken,
                OfficialRegistryPresenceSignalKind.Online,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 3,
                    PeakUsers: 4,
                    Uptime: TimeSpan.FromMinutes(6),
                    Status: "Running")));
        onlineResponse.EnsureSuccessStatusCode();

        ServerDiscoveryDetail? online = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(online);
        Assert.Equal(ServerListingHealthState.Online, online.Row.HealthState);
        Assert.NotNull(online.Row.LastSeenUtc);
    }

    [Fact]
    public async Task MetadataEndpoint_UpdatesAdvertisedFields()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:VerifiedPublishTokens:alpha"] = "secret-token",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = (await (await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Verified,
                new ServerAdvertisedInfo(
                    ServerId: "alpha",
                    DisplayName: "Verified Alpha",
                    ConnectHint: "alpha.test:42042",
                    Region: "NA",
                    PrimaryLanguage: "en"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running"),
                PublishToken: "secret-token")))
            .Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>())!;

        HttpResponseMessage metadataResponse = await client.PutAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/metadata",
            new OfficialRegistryMetadataUpdateRequest(
                register.HeartbeatToken,
                new ServerAdvertisedInfo(
                    ServerId: "wrong-id-ignored",
                    DisplayName: "Verified Alpha Updated",
                    ConnectHint: "alpha-updated.test:42042",
                    Region: "EU-West",
                    PrimaryLanguage: "fr",
                    Languages: new[] { "fr", "en" },
                    Visibility: ServerListingVisibility.Unlisted,
                    Description: "Updated metadata",
                    WebsiteUrl: "https://example.test/alpha",
                    DiscordUrl: "https://discord.gg/example")));
        metadataResponse.EnsureSuccessStatusCode();

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal("Verified Alpha Updated", detail.Row.DisplayName);
        Assert.Equal("alpha-updated.test:42042", detail.Row.ConnectHint);
        Assert.Equal("EU-West", detail.Row.Region);
        Assert.Equal("fr", detail.Row.PrimaryLanguage);
        Assert.Equal(ServerListingVisibility.Unlisted, detail.Row.Visibility);
        Assert.Equal("alpha", detail.Row.ServerId);
        Assert.Equal("Updated metadata", detail.Description);
        Assert.Equal("https://example.test/alpha", detail.WebsiteUrl);
        Assert.Equal("https://discord.gg/example", detail.DiscordUrl);
        Assert.Equal(2, detail.LanguagesOrEmpty.Count);
    }

    [Fact]
    public async Task DetailEndpoint_ExposesRecentHistorySamples()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:HistorySampleIntervalSeconds"] = "0",
            ["OfficialRegistry:HistorySampleLimit"] = "8",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = (await (await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "History Server",
                    ConnectHint: "history.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Starting"))))
            .Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>())!;

        (await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(
                register.HeartbeatToken,
                OfficialRegistryPresenceSignalKind.Online,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 3,
                    PeakUsers: 4,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running"))))
            .EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(
            $"/api/v1/servers/{register.LeaseId}",
            new OfficialRegistryHeartbeatRequest(
                register.HeartbeatToken,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 5,
                    PeakUsers: 6,
                    Uptime: TimeSpan.FromMinutes(3),
                    Status: "Busy"))))
            .EnsureSuccessStatusCode();

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.True(detail.RecentSamplesOrEmpty.Count >= 3);
        Assert.Equal(5, detail.RecentSamplesOrEmpty[^1].CurrentUsers);
        Assert.Equal(ServerListingHealthState.Online, detail.RecentSamplesOrEmpty[^1].HealthState);
        Assert.NotNull(detail.HistorySummary);
        Assert.Equal(detail.RecentSamplesOrEmpty.Count, detail.HistorySummary!.SampleCount);
        Assert.Equal(5, detail.HistorySummary.MaxCurrentUsers);
        Assert.Equal(6, detail.HistorySummary.MaxPeakUsers);
        Assert.True(detail.HistorySummary.AverageCurrentUsers >= 3.0);
        Assert.True(detail.HistorySummary.OnlineRatio > 0.0);
    }

    [Fact]
    public async Task ServerQuery_SupportsFilteringSortingAndPagination()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse alpha = await RegisterAndActivateAsync(
            client,
            "Alpha Server",
            "alpha.test:42042",
            "EU-West",
            "fr",
            currentUsers: 8,
            passwordRequired: false);
        OfficialRegistryRegisterResponse beta = await RegisterAndActivateAsync(
            client,
            "Beta Server",
            "beta.test:42042",
            "EU-West",
            "fr",
            currentUsers: 12,
            passwordRequired: true);
        _ = await RegisterAndActivateAsync(
            client,
            "Gamma Server",
            "gamma.test:42042",
            "NA-Central",
            "en",
            currentUsers: 20,
            passwordRequired: false);

        ServerDiscoveryRow[]? filtered = await client.GetFromJsonAsync<ServerDiscoveryRow[]>(
            "/api/v1/servers?region=EU-West&language=fr&passwordRequired=true&sortBy=players&descending=true&limit=5");

        Assert.NotNull(filtered);
        Assert.Single(filtered);
        Assert.Equal("Beta Server", filtered[0].DisplayName);
        Assert.Equal(beta.ServerId, filtered[0].ServerId);

        ServerDiscoveryRow[]? paged = await client.GetFromJsonAsync<ServerDiscoveryRow[]>(
            "/api/v1/servers?search=server&sortBy=players&descending=true&offset=1&limit=1");

        Assert.NotNull(paged);
        Assert.Single(paged);
        Assert.Equal("Beta Server", paged[0].DisplayName);
        Assert.NotEqual(alpha.ServerId, paged[0].ServerId);
    }

    [Fact]
    public async Task RegisterRejectsInvalidStatusEndpointUrl()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Invalid Status Endpoint",
                    ConnectHint: "invalid.test:42042",
                    StatusEndpointUrl: "/discovery/status"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterRejectsBlockedStatusEndpointHost_Localhost()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Blocked Localhost",
                    ConnectHint: "blocked.test:42042",
                    StatusEndpointUrl: "http://localhost:42043/discovery/status"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterRejectsBlockedStatusEndpointHost_PrivateIpLiteral()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Blocked Private IP",
                    ConnectHint: "blocked.test:42042",
                    StatusEndpointUrl: "http://192.168.1.25:42043/discovery/status"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterAllowsPrivateStatusEndpointHost_WhenConfigured()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AllowPrivateStatusEndpointTargets"] = "true",
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Allowed Private IP",
                    ConnectHint: "127.0.0.1:42042",
                    StatusEndpointUrl: "http://127.0.0.1:42043/discovery/status"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ServerListEndpoint_IsRateLimited()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PublicReadRateLimitPermitLimit"] = "1",
            ["OfficialRegistry:PublicReadRateLimitWindowSeconds"] = "60",
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage firstResponse = await client.GetAsync("/api/v1/servers");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        HttpResponseMessage secondResponse = await client.GetAsync("/api/v1/servers");
        Assert.Equal((HttpStatusCode)429, secondResponse.StatusCode);
    }

    [Fact]
    public async Task ServerListEndpoint_BenchmarkBypassHeader_SkipsRateLimit_WhenEnabled()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PublicReadRateLimitPermitLimit"] = "1",
            ["OfficialRegistry:PublicReadRateLimitWindowSeconds"] = "60",
            ["OfficialRegistry:EnableBenchmarkRateLimitBypass"] = "true",
            ["OfficialRegistry:BenchmarkRateLimitBypassToken"] = "local-benchmark",
        });
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage firstRequest = new(HttpMethod.Get, "/api/v1/servers");
        firstRequest.Headers.Add("X-Registry-Benchmark-Bypass", "local-benchmark");
        HttpResponseMessage firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using HttpRequestMessage secondRequest = new(HttpMethod.Get, "/api/v1/servers");
        secondRequest.Headers.Add("X-Registry-Benchmark-Bypass", "local-benchmark");
        HttpResponseMessage secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }

    [Fact]
    public async Task ServerListEndpoint_ReturnsEtagAndSupportsConditionalGet()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        _ = await RegisterAndActivateAsync(
            client,
            "Cached Server",
            "cached.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        HttpResponseMessage firstResponse = await client.GetAsync("/api/v1/servers");
        firstResponse.EnsureSuccessStatusCode();
        Assert.True(firstResponse.Headers.ETag != null);

        using HttpRequestMessage conditionalRequest = new(HttpMethod.Get, "/api/v1/servers");
        conditionalRequest.Headers.IfNoneMatch.Add(firstResponse.Headers.ETag!);

        HttpResponseMessage secondResponse = await client.SendAsync(conditionalRequest);
        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
    }

    [Fact]
    public async Task AdminLifecycleEndpoint_DisablesAndReactivatesListing()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Admin Managed Server",
            "admin.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        using HttpRequestMessage disableRequest = new(HttpMethod.Put, $"/api/v1/admin/servers/{register.ServerId}/lifecycle")
        {
            Content = JsonContent.Create(new OfficialRegistryLifecycleUpdateRequest(ServerListingLifecycleState.Disabled)),
        };
        disableRequest.Headers.Add("X-Admin-Token", "admin-secret");
        HttpResponseMessage disableResponse = await client.SendAsync(disableRequest);
        disableResponse.EnsureSuccessStatusCode();

        ServerDiscoveryRow[]? hiddenRows = await client.GetFromJsonAsync<ServerDiscoveryRow[]>("/api/v1/servers");
        Assert.NotNull(hiddenRows);
        Assert.Empty(hiddenRows);

        ServerDiscoveryDetail? disabledDetail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(disabledDetail);
        Assert.Equal(ServerListingLifecycleState.Disabled, disabledDetail.Row.LifecycleState);

        using HttpRequestMessage reactivateRequest = new(HttpMethod.Put, $"/api/v1/admin/servers/{register.ServerId}/lifecycle")
        {
            Content = JsonContent.Create(new OfficialRegistryLifecycleUpdateRequest(ServerListingLifecycleState.Active)),
        };
        reactivateRequest.Headers.Add("X-Admin-Token", "admin-secret");
        HttpResponseMessage reactivateResponse = await client.SendAsync(reactivateRequest);
        reactivateResponse.EnsureSuccessStatusCode();

        ServerDiscoveryRow[]? visibleRows = await client.GetFromJsonAsync<ServerDiscoveryRow[]>("/api/v1/servers");
        Assert.NotNull(visibleRows);
        Assert.Single(visibleRows);

        ServerDiscoveryDetail? activeDetail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(activeDetail);
        Assert.Equal(ServerListingLifecycleState.Active, activeDetail.Row.LifecycleState);
    }

    [Fact]
    public async Task AdminLifecycleEndpoint_RequiresValidAdminToken()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Admin Guarded Server",
            "guarded.test:42042",
            "NA-Central",
            "en",
            currentUsers: 2,
            passwordRequired: false);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/admin/servers/{register.ServerId}/lifecycle",
            new OfficialRegistryLifecycleUpdateRequest(ServerListingLifecycleState.Disabled));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminServersEndpoint_RequiresValidAdminToken()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
        });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/servers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminServersEndpoint_ReturnsDisabledListings()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Disabled Listing",
            "disabled.test:42042",
            "NA-Central",
            "en",
            currentUsers: 3,
            passwordRequired: false);

        using HttpRequestMessage disableRequest = new(HttpMethod.Put, $"/api/v1/admin/servers/{register.ServerId}/lifecycle")
        {
            Content = JsonContent.Create(new OfficialRegistryLifecycleUpdateRequest(ServerListingLifecycleState.Disabled))
        };
        disableRequest.Headers.Add("X-Admin-Token", "admin-secret");
        (await client.SendAsync(disableRequest)).EnsureSuccessStatusCode();

        using HttpRequestMessage listRequest = new(HttpMethod.Get, "/api/v1/admin/servers");
        listRequest.Headers.Add("X-Admin-Token", "admin-secret");
        using HttpResponseMessage listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();

        OfficialRegistryAdminServerRecord[]? records = await listResponse.Content.ReadFromJsonAsync<OfficialRegistryAdminServerRecord[]>();
        Assert.NotNull(records);
        Assert.Contains(records, static record => record.Row.ServerId == "unverified" || record.Row.DisplayName == "Disabled Listing" || record.Row.ConnectHint == "disabled.test:42042");
        Assert.Contains(records, static record => record.Row.LifecycleState == ServerListingLifecycleState.Disabled);
    }

    [Fact]
    public async Task AdminDeleteEndpoint_RemovesListing()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Delete Listing",
            "delete.test:42042",
            "NA-Central",
            "en",
            currentUsers: 2,
            passwordRequired: false);

        using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"/api/v1/admin/servers/{register.ServerId}");
        deleteRequest.Headers.Add("X-Admin-Token", "admin-secret");
        using HttpResponseMessage deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage detailResponse = await client.GetAsync($"/api/v1/servers/{register.ServerId}");
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [Fact]
    public async Task AdminRetentionResetEndpoint_RequiresValidAdminToken()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsync("/api/v1/admin/servers/example/retention-reset", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminBansEndpoints_RequireValidAdminToken()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage listResponse = await client.GetAsync("/api/v1/admin/bans");
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        using HttpResponseMessage liftResponse = await client.DeleteAsync("/api/v1/admin/bans/hosts/127.0.0.1");
        Assert.Equal(HttpStatusCode.Unauthorized, liftResponse.StatusCode);
    }

    [Fact]
    public async Task AdminRuntimeEndpoint_RequiresAdminToken()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
        });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/runtime");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthzEndpoint_ReturnsHealthySnapshot()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();

        JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using (payload)
        {
            JsonElement root = payload.RootElement;
            Assert.Equal("Healthy", root.GetProperty("status").GetString());
            Assert.True(root.TryGetProperty("entries", out JsonElement entries));
            Assert.True(entries.TryGetProperty("registry-store", out JsonElement registryStore));
            Assert.True(registryStore.TryGetProperty("data", out JsonElement data));
            Assert.True(data.TryGetProperty("totalRecords", out _));
            Assert.True(data.TryGetProperty("activeRecords", out _));
            Assert.True(data.TryGetProperty("onlineRecords", out _));
            Assert.True(data.TryGetProperty("activeLeases", out _));
            Assert.True(data.TryGetProperty("pendingPersistence", out _));
        }
    }

    [Fact]
    public async Task AdminRuntimeEndpoint_ExposesStoreAndPersistenceMetrics()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:AdminApiToken"] = "admin-secret",
            ["OfficialRegistry:EnableStatePersistence"] = "true",
            ["OfficialRegistry:StateFilePath"] = "data/test-runtime-metrics.json",
            ["OfficialRegistry:PersistenceFlushIntervalMilliseconds"] = "25",
        });
        using HttpClient client = factory.CreateClient();

        _ = await RegisterAndActivateAsync(
            client,
            "Metrics Server",
            "metrics.test:42042",
            "NA-Central",
            "en",
            currentUsers: 3,
            passwordRequired: false);

        JsonDocument? payload = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/admin/runtime");
            request.Headers.Add("X-Admin-Token", "admin-secret");
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            payload?.Dispose();
            payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = payload.RootElement;

            if (root.GetProperty("metrics").GetProperty("saveStateCalls").GetInt64() >= 1)
                break;

            await Task.Delay(25);
        }

        Assert.NotNull(payload);
        using (payload)
        {
            JsonElement root = payload.RootElement;
            Assert.True(root.GetProperty("store").GetProperty("totalRecords").GetInt32() >= 1);
            Assert.True(root.GetProperty("metrics").GetProperty("registerCalls").GetInt64() >= 1);
            Assert.True(root.GetProperty("metrics").GetProperty("saveStateCalls").GetInt64() >= 1);
            Assert.True(root.GetProperty("store").GetProperty("persistenceEnabled").GetBoolean());
            Assert.True(root.GetProperty("aggregates").GetProperty("currentPlayersTotal").GetInt32() >= 3);
            Assert.True(root.GetProperty("aggregates").GetProperty("serversWithHistory").GetInt32() >= 1);
            Assert.True(root.GetProperty("aggregates").GetProperty("historyBucketCount").GetInt32() >= 1);
            Assert.True(root.GetProperty("aggregates").GetProperty("recentAggregatePoints").GetArrayLength() >= 1);
        }
    }

    [Fact]
    public async Task OwnerClaimEndpoint_MatchesExistingListing_AndReturnsPendingVerification()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Claimable Server",
            "claim.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        using HttpRequestMessage claimRequest = new(HttpMethod.Post, "/api/v1/servers/claims")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"))
        };
        claimRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");

        HttpResponseMessage response = await client.SendAsync(claimRequest);
        response.EnsureSuccessStatusCode();

        OfficialRegistryOwnerClaimResponse? claim = await response.Content.ReadFromJsonAsync<OfficialRegistryOwnerClaimResponse>();
        Assert.NotNull(claim);
        Assert.Equal(OfficialRegistryOwnerClaimState.PendingVerification, claim.State);
        Assert.Equal(register.ServerId, claim.ServerId);
        Assert.Equal("claim.test:42042", claim.ConnectHint);
    }

    [Fact]
    public async Task OwnerClaimEndpoint_RequiresPortalToken_WhenConfigured()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/servers/claims",
            new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OwnerMetadataEndpoint_UpdatesClaimedListingAdvertisedFields()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Claimable Server",
            "claim.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        using HttpRequestMessage claimRequest = new(HttpMethod.Post, "/api/v1/servers/claims")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"))
        };
        claimRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");
        (await client.SendAsync(claimRequest)).EnsureSuccessStatusCode();

        using HttpRequestMessage updateRequest = new(HttpMethod.Put, $"/api/v1/servers/{register.ServerId}/owner")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerMetadataUpdateRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Updated Claim Name",
                TargetHost: "claim.test",
                TargetPort: 42042,
                Description: "Updated description",
                WebsiteUrl: "https://example.test",
                DiscordUrl: "https://discord.gg/example"))
        };
        updateRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");
        HttpResponseMessage updateResponse = await client.SendAsync(updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal("Updated Claim Name", detail.Row.DisplayName);
        Assert.Equal("Updated description", detail.Description);
        Assert.Equal("https://example.test", detail.WebsiteUrl);
        Assert.Equal("https://discord.gg/example", detail.DiscordUrl);
    }

    [Fact]
    public async Task OwnerDeleteEndpoint_RemovesClaimedListing()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Claimable Server",
            "claim.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        using HttpRequestMessage claimRequest = new(HttpMethod.Post, "/api/v1/servers/claims")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"))
        };
        claimRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");
        (await client.SendAsync(claimRequest)).EnsureSuccessStatusCode();

        using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"/api/v1/servers/{register.ServerId}/owner")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerDeleteRequest("user-123"))
        };
        deleteRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");

        HttpResponseMessage deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage detailResponse = await client.GetAsync($"/api/v1/servers/{register.ServerId}");
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [Fact]
    public async Task OwnerTokenEndpoint_ClearsVerificationForRevalidation()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Claimable Server",
            "claim.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        using HttpRequestMessage claimRequest = new(HttpMethod.Post, "/api/v1/servers/claims")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"))
        };
        claimRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");
        (await client.SendAsync(claimRequest)).EnsureSuccessStatusCode();

        using HttpRequestMessage tokenRequest = new(HttpMethod.Put, $"/api/v1/servers/{register.ServerId}/owner-token")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerTokenUpdateRequest(
                OwnerAccountId: "user-123",
                OwnershipToken: "rotated-portal-token"))
        };
        tokenRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");

        HttpResponseMessage tokenResponse = await client.SendAsync(tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal(ServerListingTrustLevel.Unverified, detail.Row.TrustLevel);
        Assert.Null(detail.VerifiedAtUtc);
        Assert.Null(detail.OwnershipVerificationError);
        Assert.NotNull(detail.OwnershipClaimRequestedUtc);
    }

    [Fact]
    public async Task VoteCountEndpoint_RequiresPortalToken_WhenConfigured()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Vote Server",
            "vote.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/vote-count",
            new OfficialRegistryVoteCountUpdateRequest(12));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VoteCountEndpoint_UpdatesDetail_AndVoteSortUsesProjectedCount()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse first = await RegisterAndActivateAsync(
            client,
            "Alpha Server",
            "alpha.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        OfficialRegistryRegisterResponse second = await RegisterAndActivateAsync(
            client,
            "Beta Server",
            "beta.test:42042",
            "NA-Central",
            "en",
            currentUsers: 8,
            passwordRequired: false);

        using HttpRequestMessage voteRequest = new(HttpMethod.Put, $"/api/v1/servers/{first.ServerId}/vote-count")
        {
            Content = JsonContent.Create(new OfficialRegistryVoteCountUpdateRequest(25))
        };
        voteRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");
        (await client.SendAsync(voteRequest)).EnsureSuccessStatusCode();

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{first.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal(25, detail.Row.VoteCount);

        ServerDiscoveryRow[]? rows = await client.GetFromJsonAsync<ServerDiscoveryRow[]>("/api/v1/servers?sortBy=votes&descending=true");
        Assert.NotNull(rows);
        Assert.True(rows.Length >= 2);
        Assert.Equal(first.ServerId, rows[0].ServerId);
        Assert.Equal(25, rows[0].VoteCount);
        Assert.Equal(second.ServerId, rows[1].ServerId);
    }

    [Fact]
    public async Task RegisterUnverified_ReusesClaimedListing_WithoutCreatingDuplicate()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:PortalApiToken"] = "portal-secret",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterResponse register = await RegisterAndActivateAsync(
            client,
            "Boot Listing",
            "claim.test:42042",
            "NA-Central",
            "en",
            currentUsers: 4,
            passwordRequired: false);

        using HttpRequestMessage claimRequest = new(HttpMethod.Post, "/api/v1/servers/claims")
        {
            Content = JsonContent.Create(new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimed Listing",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token",
                Description: "Owner managed description"))
        };
        claimRequest.Headers.Add("X-Portal-Api-Token", "portal-secret");
        (await client.SendAsync(claimRequest)).EnsureSuccessStatusCode();

        HttpResponseMessage secondRegisterResponse = await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Fresh Boot Name",
                    ConnectHint: "claim.test:42042",
                    Region: "NA-Central",
                    PrimaryLanguage: "en"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running")));
        secondRegisterResponse.EnsureSuccessStatusCode();

        OfficialRegistryRegisterResponse? secondRegister = await secondRegisterResponse.Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>();
        Assert.NotNull(secondRegister);
        Assert.Equal(register.ServerId, secondRegister.ServerId);

        (await client.PostAsJsonAsync(
            $"/api/v1/servers/{secondRegister.ServerId}/presence",
            new OfficialRegistryPresenceRequest(
                secondRegister.HeartbeatToken,
                OfficialRegistryPresenceSignalKind.Online,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running"))))
            .EnsureSuccessStatusCode();

        ServerDiscoveryRow[]? rows = await client.GetFromJsonAsync<ServerDiscoveryRow[]>("/api/v1/servers");
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal(register.ServerId, rows[0].ServerId);
        Assert.Equal("Claimed Listing", rows[0].DisplayName);

        ServerDiscoveryDetail? detail = await client.GetFromJsonAsync<ServerDiscoveryDetail>($"/api/v1/servers/{register.ServerId}");
        Assert.NotNull(detail);
        Assert.Equal("Claimed Listing", detail.Row.DisplayName);
        Assert.Equal("Owner managed description", detail.Description);
    }

    [Fact]
    public async Task Query_IncludesOfflineVerifiedListing_UntilDelisted()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(new Dictionary<string, string?>
        {
            ["OfficialRegistry:VerifiedPublishTokens:verified-alpha"] = "secret-token",
        });
        using HttpClient client = factory.CreateClient();

        OfficialRegistryRegisterRequest request = new(
            DiscoveryPublishMode.Verified,
            new ServerAdvertisedInfo(
                ServerId: "verified-alpha",
                DisplayName: "Verified Alpha",
                ConnectHint: "alpha.test:42042"),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: 2,
                PeakUsers: 2,
                Uptime: TimeSpan.FromMinutes(2),
                Status: "Running"),
            PublishToken: "secret-token");

        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/v1/servers/register", request);
        registerResponse.EnsureSuccessStatusCode();

        OfficialRegistryRegisterResponse? register = await registerResponse.Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>();
        Assert.NotNull(register);

        HttpResponseMessage offlineResponse = await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(
                register.HeartbeatToken,
                OfficialRegistryPresenceSignalKind.Offline));
        offlineResponse.EnsureSuccessStatusCode();

        ServerDiscoveryRow[]? rows = await client.GetFromJsonAsync<ServerDiscoveryRow[]>("/api/v1/servers");
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal(ServerListingHealthState.Offline, rows[0].HealthState);
        Assert.Equal(ServerListingTrustLevel.Verified, rows[0].TrustLevel);
    }

    [Fact]
    public void OwnerClaim_PromotesListingToVerified_OnMatchingPollProof()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        RegistryRuntimeMetrics metrics = new();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), metrics);
        DateTimeOffset nowUtc = new(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Claimable Server",
                    ConnectHint: "claim.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            nowUtc);

        OfficialRegistryOwnerClaimResponse claim = store.ClaimServer(
            new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"),
            nowUtc.AddMinutes(1));

        Assert.Equal(OfficialRegistryOwnerClaimState.PendingVerification, claim.State);
        Assert.False(string.IsNullOrWhiteSpace(register.RegistrationId));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running"),
                OwnershipTokenHash: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("portal-token")))),
            nowUtc.AddMinutes(2),
            latencyMs: 10));

        Assert.True(store.TryGetByServerId(register.ServerId, nowUtc.AddMinutes(2), out ServerDiscoveryDetail? detail));
        Assert.NotNull(detail);
        Assert.Equal(ServerListingTrustLevel.Verified, detail.Row.TrustLevel);
        Assert.NotNull(detail.VerifiedAtUtc);
        Assert.Null(detail.OwnershipVerificationError);
    }

    [Fact]
    public void HeartbeatOwnershipHashHint_MarksVerifiedClaimForRevalidation()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        RegistryRuntimeMetrics metrics = new();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), metrics);
        DateTimeOffset nowUtc = new(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Claimable Server",
                    ConnectHint: "claim.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            nowUtc);

        store.ClaimServer(
            new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"),
            nowUtc.AddMinutes(1));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running"),
                OwnershipTokenHash: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("portal-token")))),
            nowUtc.AddMinutes(2),
            latencyMs: 10));

        Assert.True(store.TryHeartbeat(
            register.LeaseId,
            new OfficialRegistryHeartbeatRequest(
                register.HeartbeatToken,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 3,
                    PeakUsers: 3,
                    Uptime: TimeSpan.FromMinutes(3),
                    Status: "Running"),
                OwnershipTokenHashHint: "DIFFERENT-HASH"),
            options,
            nowUtc.AddMinutes(3),
            out _));

        Assert.True(store.TryGetByServerId(register.ServerId, nowUtc.AddMinutes(3), out ServerDiscoveryDetail? detail));
        Assert.NotNull(detail);
        Assert.Equal(ServerListingTrustLevel.Unverified, detail.Row.TrustLevel);
        Assert.Null(detail.VerifiedAtUtc);
        Assert.Equal("Server ownership token changed. Update the portal token and wait for the next poll.", detail.OwnershipVerificationError);
    }

    [Fact]
    public void PollProofMismatch_DemotesVerifiedListingToUnverified()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        RegistryRuntimeMetrics metrics = new();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), metrics);
        DateTimeOffset nowUtc = new(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Claimable Server",
                    ConnectHint: "claim.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            nowUtc);

        store.ClaimServer(
            new OfficialRegistryOwnerClaimRequest(
                OwnerAccountId: "user-123",
                DisplayName: "Claimable Server",
                TargetHost: "claim.test",
                TargetPort: 42042,
                OwnershipToken: "portal-token"),
            nowUtc.AddMinutes(1));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running"),
                OwnershipTokenHash: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("portal-token")))),
            nowUtc.AddMinutes(2),
            latencyMs: 10));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 3,
                    PeakUsers: 3,
                    Uptime: TimeSpan.FromMinutes(3),
                    Status: "Running"),
                OwnershipTokenHash: "WRONG-HASH"),
            nowUtc.AddMinutes(3),
            latencyMs: 12));

        Assert.True(store.TryGetByServerId(register.ServerId, nowUtc.AddMinutes(3), out ServerDiscoveryDetail? detail));
        Assert.NotNull(detail);
        Assert.Equal(ServerListingTrustLevel.Unverified, detail.Row.TrustLevel);
        Assert.Equal("Ownership token mismatch.", detail.OwnershipVerificationError);
        Assert.Null(detail.VerifiedAtUtc);
    }

    [Fact]
    public void LifecycleSweep_DelistsAndPurgesUnverifiedRecord_AfterConfiguredInactivity()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), new RegistryRuntimeMetrics());
        DateTimeOffset registeredAtUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Temporary Server",
                    ConnectHint: "temp.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            registeredAtUtc);

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(12));
        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(12), out ServerDiscoveryDetail? delisted));
        Assert.NotNull(delisted);
        Assert.Equal(ServerListingLifecycleState.Delisted, delisted.Row.LifecycleState);

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(24));
        Assert.False(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(24), out _));
    }

    [Fact]
    public void RetentionReset_ExtendsActiveRecordDelistDeadline()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), new RegistryRuntimeMetrics());
        DateTimeOffset registeredAtUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Retention Server",
                    ConnectHint: "retain.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            registeredAtUtc);

        Assert.True(store.TryResetRetention(register.ServerId, registeredAtUtc.AddHours(11), out _));

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(12));
        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(12), out ServerDiscoveryDetail? stillActive));
        Assert.NotNull(stillActive);
        Assert.Equal(ServerListingLifecycleState.Active, stillActive.Row.LifecycleState);

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(23));
        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(23), out ServerDiscoveryDetail? delisted));
        Assert.NotNull(delisted);
        Assert.Equal(ServerListingLifecycleState.Delisted, delisted.Row.LifecycleState);
    }

    [Fact]
    public void RetentionReset_ExtendsDelistedRecordPurgeDeadline()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), new RegistryRuntimeMetrics());
        DateTimeOffset registeredAtUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Retention Server",
                    ConnectHint: "retain.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            registeredAtUtc);

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(12));
        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(12), out ServerDiscoveryDetail? delistedAtThreshold));
        Assert.NotNull(delistedAtThreshold);
        Assert.Equal(ServerListingLifecycleState.Delisted, delistedAtThreshold.Row.LifecycleState);

        Assert.True(store.TryResetRetention(register.ServerId, registeredAtUtc.AddHours(23), out _));

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(24));
        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(24), out ServerDiscoveryDetail? retained));
        Assert.NotNull(retained);
        Assert.Equal(ServerListingLifecycleState.Delisted, retained.Row.LifecycleState);

        store.RunLifecycleSweep(options, registeredAtUtc.AddHours(35));
        Assert.False(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddHours(35), out _));
    }

    [Fact]
    public void HostBan_DisablesMatchingListing_AndSkipsPolling()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), new RegistryRuntimeMetrics());
        DateTimeOffset nowUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Bannable Server",
                    ConnectHint: "198.51.100.10:42042",
                    StatusEndpointUrl: "https://198.51.100.10:42043/discovery/status"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "198.51.100.10",
            options,
            nowUtc);

        Assert.True(store.TryBanHostForServer(register.ServerId, "abuse", nowUtc.AddMinutes(1), out OfficialRegistryAdminHostBanRecord? banRecord));
        Assert.NotNull(banRecord);
        Assert.Equal("198.51.100.10", banRecord.Host);

        Assert.True(store.TryGetByServerId(register.ServerId, nowUtc.AddMinutes(1), out ServerDiscoveryDetail? detail));
        Assert.NotNull(detail);
        Assert.Equal(ServerListingLifecycleState.Disabled, detail.Row.LifecycleState);

        IReadOnlyList<InMemoryOfficialRegistryStore.RegistryPollCandidate> pollCandidates = store.GetPollCandidates(nowUtc.AddMinutes(1));
        Assert.DoesNotContain(pollCandidates, x => x.ServerId == register.ServerId);
    }

    [Fact]
    public void HostBan_BlocksNewRegistrationFromBannedPublisherFingerprint()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), new RegistryRuntimeMetrics());
        DateTimeOffset nowUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse initial = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Seed Server",
                    ConnectHint: "198.51.100.10:42042",
                    StatusEndpointUrl: "https://198.51.100.10:42043/discovery/status"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "198.51.100.10",
            options,
            nowUtc);

        Assert.True(store.TryBanHostForServer(initial.ServerId, "abuse", nowUtc.AddMinutes(1), out _));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Blocked Server",
                    ConnectHint: "198.51.100.10:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "198.51.100.10",
            options,
            nowUtc.AddMinutes(2)));

        Assert.Equal("Registration host is banned.", ex.Message);
    }

    [Fact]
    public void LifecycleSweep_DelistedVerifiedRecord_ReactivatesOnVerifiedReregister()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        options.VerifiedPublishTokens["alpha"] = "secret-token";

        InMemoryOfficialRegistryStore store = new(Options.Create(options), new RegistryRuntimeMetrics());
        DateTimeOffset registeredAtUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Verified,
                new ServerAdvertisedInfo(
                    ServerId: "alpha",
                    DisplayName: "Verified Alpha",
                    ConnectHint: "alpha.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running"),
                PublishToken: "secret-token"),
            publisherFingerprint: "verified-publisher",
            options,
            registeredAtUtc);

        store.RunLifecycleSweep(options, registeredAtUtc.AddDays(30));
        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddDays(30), out ServerDiscoveryDetail? delisted));
        Assert.NotNull(delisted);
        Assert.Equal(ServerListingLifecycleState.Delisted, delisted.Row.LifecycleState);

        store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Verified,
                new ServerAdvertisedInfo(
                    ServerId: "alpha",
                    DisplayName: "Verified Alpha",
                    ConnectHint: "alpha.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 2,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running"),
                PublishToken: "secret-token"),
            publisherFingerprint: "verified-publisher",
            options,
            registeredAtUtc.AddDays(31));

        Assert.True(store.TryGetByServerId(register.ServerId, registeredAtUtc.AddDays(31), out ServerDiscoveryDetail? reactivated));
        Assert.NotNull(reactivated);
        Assert.Equal(ServerListingLifecycleState.Active, reactivated.Row.LifecycleState);
        Assert.Equal(ServerListingHealthState.Pending, reactivated.Row.HealthState);
    }

    [Fact]
    public void DetailProjection_ExposesPollLatencySummary()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        RegistryRuntimeMetrics metrics = new();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), metrics);
        DateTimeOffset nowUtc = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Latency Server",
                    ConnectHint: "latency.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "127.0.0.1",
            options,
            nowUtc);
        Assert.False(string.IsNullOrWhiteSpace(register.RegistrationId));
        string registrationId = register.RegistrationId!;

        Assert.True(store.ApplyPollSuccess(
            registrationId,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 2,
                    PeakUsers: 3,
                    Uptime: TimeSpan.FromMinutes(2),
                    Status: "Running")),
            nowUtc.AddSeconds(10),
            latencyMs: 12));

        Assert.True(store.ApplyPollSuccess(
            registrationId,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 4,
                    PeakUsers: 5,
                    Uptime: TimeSpan.FromMinutes(3),
                    Status: "Busy")),
            nowUtc.AddSeconds(20),
            latencyMs: 28));

        Assert.True(store.ApplyPollFailure(registrationId, nowUtc.AddSeconds(30), failureThreshold: 3));

        Assert.True(store.TryGetByServerId(register.ServerId, nowUtc.AddSeconds(30), out ServerDiscoveryDetail? detail));
        Assert.NotNull(detail);
        Assert.NotNull(detail.LatencySummary);
        Assert.Equal(2, detail.LatencySummary!.SampleCount);
        Assert.Equal(20, detail.LatencySummary.AverageLatencyMs, 0);
        Assert.Equal(28, detail.LatencySummary.MaxLatencyMs);
        Assert.Equal(28, detail.LatencySummary.LastLatencyMs);
        Assert.Equal(1, detail.LatencySummary.FailureCount);
    }

    [Fact]
    public void AggregateMetricsSnapshot_ComputesTrueWindowedPlayerMetrics()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        options.AggregateHistorySampleIntervalSeconds = 0;
        options.AggregateHistorySampleLimit = 1024;

        RegistryRuntimeMetrics metrics = new();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), metrics);
        DateTimeOffset nowUtc = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset registeredAtUtc = nowUtc.AddDays(-40);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Metrics Server",
                    ConnectHint: "metrics.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 1,
                    PeakUsers: 1,
                    Uptime: TimeSpan.FromMinutes(1),
                    Status: "Running")),
            publisherFingerprint: "198.51.100.10",
            options,
            registeredAtUtc);

        Assert.False(string.IsNullOrWhiteSpace(register.RegistrationId));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 10,
                    PeakUsers: 10,
                    Uptime: TimeSpan.FromDays(11),
                    Status: "Running")),
            nowUtc.AddDays(-29),
            latencyMs: 11));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 20,
                    PeakUsers: 20,
                    Uptime: TimeSpan.FromDays(33),
                    Status: "Running")),
            nowUtc.AddDays(-7),
            latencyMs: 22));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 30,
                    PeakUsers: 30,
                    Uptime: TimeSpan.FromDays(39),
                    Status: "Busy")),
            nowUtc.AddDays(-1),
            latencyMs: 33));

        RegistryAggregateMetricsSnapshot snapshot = store.GetAggregateMetricsSnapshot(nowUtc);
        Assert.Equal(30, snapshot.PeakPlayers24Hours);
        Assert.Equal(25d, snapshot.AveragePlayers7Days, 3);
        Assert.Equal(20d, snapshot.AveragePlayers30Days, 3);
        Assert.Equal(30, snapshot.AllTimePeakPlayers);
        Assert.Equal(1d, snapshot.OnlineRatio7Days, 3);
        Assert.Equal(1d, snapshot.OnlineRatio30Days, 3);
    }

    [Fact]
    public void DetailProjection_ExposesPerServerAdvancedMetrics()
    {
        OfficialRegistryOptions options = CreateStoreOptions();
        options.AggregateHistorySampleIntervalSeconds = 0;
        options.AggregateHistorySampleLimit = 1024;

        RegistryRuntimeMetrics metrics = new();
        InMemoryOfficialRegistryStore store = new(Options.Create(options), metrics);
        DateTimeOffset nowUtc = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset registeredAtUtc = nowUtc.AddDays(-40);

        OfficialRegistryRegisterResponse register = store.Register(
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: "Detail Metrics Server",
                    ConnectHint: "detail.test:42042"),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 5,
                    PeakUsers: 5,
                    Uptime: TimeSpan.FromMinutes(5),
                    Status: "Running")),
            publisherFingerprint: "198.51.100.11",
            options,
            registeredAtUtc);

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 12,
                    PeakUsers: 12,
                    Uptime: TimeSpan.FromDays(11),
                    Status: "Running")),
            nowUtc.AddDays(-29),
            latencyMs: 11));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 18,
                    PeakUsers: 18,
                    Uptime: TimeSpan.FromDays(33),
                    Status: "Running")),
            nowUtc.AddDays(-7),
            latencyMs: 22));

        Assert.True(store.ApplyPollSuccess(
            register.RegistrationId!,
            new ServerDiscoveryPollStatusResponse(
                register.ServerId,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: 24,
                    PeakUsers: 24,
                    Uptime: TimeSpan.FromDays(39),
                    Status: "Busy")),
            nowUtc.AddDays(-1),
            latencyMs: 33));

        Assert.True(store.TryGetByServerId(register.ServerId, nowUtc, out ServerDiscoveryDetail? detail));
        Assert.NotNull(detail);
        Assert.NotNull(detail.AdvancedMetrics);
        Assert.Equal(24, detail.AdvancedMetrics!.PeakPlayers24Hours);
        Assert.Equal(21d, detail.AdvancedMetrics.AveragePlayers7Days, 3);
        Assert.Equal(18d, detail.AdvancedMetrics.AveragePlayers30Days, 3);
        Assert.Equal(24, detail.AdvancedMetrics.AllTimePeakPlayers);
        Assert.NotNull(detail.AdvancedMetrics.AllTimePeakPlayersUtc);
        Assert.True(detail.AggregateSamplesOrEmpty.Count >= 3);
    }

    private static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? settings = null)
    {
        Dictionary<string, string?> mergedSettings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["OfficialRegistry:EnableStatePersistence"] = "false",
            ["OfficialRegistry:EnablePolling"] = "false",
            ["OfficialRegistry:UnverifiedRegisterCooldownSeconds"] = "0",
            ["OfficialRegistry:AllowPrivateStatusEndpointTargets"] = "false",
        };

        if (settings != null)
        {
            foreach ((string key, string? value) in settings)
                mergedSettings[key] = value;
        }

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configBuilder) =>
                {
                    configBuilder.AddInMemoryCollection(mergedSettings);
                });
            });
    }

    private static async Task<OfficialRegistryRegisterResponse> RegisterAndActivateAsync(
        HttpClient client,
        string displayName,
        string connectHint,
        string region,
        string primaryLanguage,
        int currentUsers,
        bool passwordRequired)
    {
        OfficialRegistryRegisterResponse register = (await (await client.PostAsJsonAsync(
            "/api/v1/servers/register",
            new OfficialRegistryRegisterRequest(
                DiscoveryPublishMode.Unverified,
                new ServerAdvertisedInfo(
                    ServerId: string.Empty,
                    DisplayName: displayName,
                    ConnectHint: connectHint,
                    Region: region,
                    PrimaryLanguage: primaryLanguage,
                    PasswordRequired: passwordRequired),
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: currentUsers,
                    PeakUsers: currentUsers,
                    Uptime: TimeSpan.FromMinutes(currentUsers),
                    Status: "Running"))))
            .Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>())!;

        (await client.PostAsJsonAsync(
            $"/api/v1/servers/{register.ServerId}/presence",
            new OfficialRegistryPresenceRequest(
                register.HeartbeatToken,
                OfficialRegistryPresenceSignalKind.Online,
                new ServerDiscoveryStatusInfo(
                    IsAcceptingConnections: true,
                    CurrentUsers: currentUsers,
                    PeakUsers: currentUsers,
                    Uptime: TimeSpan.FromMinutes(currentUsers),
                    Status: "Running"))))
            .EnsureSuccessStatusCode();

        return register;
    }

    private static OfficialRegistryOptions CreateStoreOptions()
    {
        return new OfficialRegistryOptions
        {
            EnableStatePersistence = false,
            EnablePolling = false,
            UnverifiedRegisterCooldownSeconds = 0,
            VerifiedDelistAfterDays = 30,
            VerifiedPurgeAfterDelistedDays = 30,
            UnverifiedDelistAfterHours = 12,
            UnverifiedPurgeAfterDelistedHours = 12,
        };
    }
}
