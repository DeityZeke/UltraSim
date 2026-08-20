using System.Net;
using System.Text;
using System.Text.Json;
using UltraSim.Client;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Server;
using UltraSim.Server.Discovery;

await RegistrySmoke_PublisherAndClientOfficialQueryShareListing();
Console.WriteLine("UltraSim.Registry.Tests passed");

static async Task RegistrySmoke_PublisherAndClientOfficialQueryShareListing()
{
    DateTimeOffset nowUtc = new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);
    using var handler = new InMemoryRegistryHandler(() => nowUtc);
    using var httpClient = new HttpClient(handler)
    {
        BaseAddress = new Uri("http://registry.local"),
    };

    ServerDiscoveryPublicationPlan plan = ServerDiscoveryPublicationFactory.CreateOfficialRegistryPlan(
        new ServerDiscoveryPublicationSettings(
            Enabled: true,
            RegistryUrl: "http://registry.local",
            PublishMode: "unverified",
            ServerId: "smoke-shard",
            DisplayName: "Smoke Shard",
            AdvertisedHost: "play.smoke.test",
            Port: 42042,
            MaxUsers: 500,
            Description: "Registry integration smoke",
            Region: "NA",
            PrimaryLanguage: "en-US",
            Tags: "smoke, integration",
            HeartbeatIntervalSeconds: 1.0,
            UnpublishOnStop: true,
            ProtocolVersion: "proto-smoke",
            ContentVersion: "content-smoke",
            CompatibilityId: "compat-smoke"),
        httpClient);

    var coordinator = new ServerDiscoveryPublisherCoordinator(
        plan.Publisher,
        plan.AdvertisedSettings,
        () => new ServerStatusSnapshot(
            ProfileName: "smoke",
            State: ServerApplicationState.Running,
            IsInitialized: true,
            IsRunning: true,
            IsAcceptingConnections: true,
            TickCount: 144,
            ActiveSessions: 7,
            PeakSessions: 9,
            StartedAtUtc: nowUtc.AddMinutes(-5),
            Uptime: TimeSpan.FromMinutes(5)),
        plan.PublisherOptions,
        () => nowUtc);

    await coordinator.PublishStartupAsync();

    ServerBrowserProviderDefinition provider = ClientOfficialRegistryDiscoveryFactory.CreateProvider(
        new ClientOfficialRegistryDiscoveryOptions(
            RegistryUrl: "http://registry.local",
            Limit: 50,
            SortBy: "name",
            Descending: false),
        httpClient);

    IReadOnlyList<ServerDiscoveryRow> rows = await provider.Query.QueryAsync();
    Assert.Equal(1, rows.Count);
    ServerDiscoveryRow row = rows[0];
    Assert.Equal("official", row.ProviderId);
    Assert.Equal(ServerDiscoveryProviderKind.Official, row.ProviderKind);
    Assert.Equal("smoke-shard", row.ServerId);
    Assert.Equal("Smoke Shard", row.DisplayName);
    Assert.Equal("play.smoke.test", row.Host);
    Assert.Equal(42042, row.Port);
    Assert.Equal(7, row.CurrentUsers);
    Assert.Equal(500, row.MaxUsers);
    Assert.Equal(ServerListingHealthState.Online, row.HealthState);
    Assert.Equal("compat-smoke", row.CompatibilityId);
    Assert.Contains("smoke", row.TagsOrEmpty);

    await coordinator.PublishShutdownAsync();

    IReadOnlyList<ServerDiscoveryRow> afterShutdown = await provider.Query.QueryAsync();
    Assert.Equal(0, afterShutdown.Count);
}

internal sealed class InMemoryRegistryHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, Listing> _listingsByLeaseId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _leaseIdByServerId = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryRegistryHandler(Func<DateTimeOffset> clock)
    {
        _clock = clock;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri ?? new Uri("/", UriKind.Relative);
        string path = uri.AbsolutePath;

        try
        {
            if (request.Method == HttpMethod.Post && path == "/api/v1/servers/register")
                return await RegisterAsync(request, cancellationToken);

            if (request.Method == HttpMethod.Put && path.StartsWith("/api/v1/servers/", StringComparison.Ordinal))
                return await HeartbeatAsync(request, path, cancellationToken);

            if (request.Method == HttpMethod.Post &&
                path.StartsWith("/api/v1/servers/", StringComparison.Ordinal) &&
                path.EndsWith("/presence", StringComparison.Ordinal))
            {
                return await PresenceAsync(request, path, cancellationToken);
            }

            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v1/servers/", StringComparison.Ordinal))
                return Delete(request, path);

            if (request.Method == HttpMethod.Get && path == "/api/v1/servers")
                return Query(uri);
        }
        catch (Exception ex)
        {
            return Json(HttpStatusCode.BadRequest, new { error = ex.Message });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> RegisterAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        OfficialRegistryRegisterRequest? payload = await ReadAsync<OfficialRegistryRegisterRequest>(request, cancellationToken);
        if (payload is null)
            return Json(HttpStatusCode.BadRequest, new { error = "Missing register payload." });

        string leaseId = Guid.NewGuid().ToString("N");
        string heartbeatToken = Guid.NewGuid().ToString("N");
        string serverId = string.IsNullOrWhiteSpace(payload.AdvertisedInfo.ServerId)
            ? payload.AdvertisedInfo.DisplayName
            : payload.AdvertisedInfo.ServerId;
        DateTimeOffset expiresAtUtc = _clock().AddSeconds(60);
        ServerDiscoveryStatusInfo status = payload.StatusInfo with { LastHeartbeatUtc = _clock() };
        var listing = new Listing(
            LeaseId: leaseId,
            ServerId: serverId,
            HeartbeatToken: heartbeatToken,
            AdvertisedInfo: payload.AdvertisedInfo with { ServerId = serverId },
            StatusInfo: status,
            HealthState: ServerListingHealthState.Pending);

        _listingsByLeaseId[leaseId] = listing;
        _leaseIdByServerId[serverId] = leaseId;

        OfficialRegistryRegisterResponse response = new(
            leaseId,
            serverId,
            heartbeatToken,
            expiresAtUtc,
            ServerListingTrustLevel.Unverified,
            payload.AdvertisedInfo.Visibility,
            RegistrationId: serverId);
        return Json(HttpStatusCode.OK, response);
    }

    private async Task<HttpResponseMessage> HeartbeatAsync(
        HttpRequestMessage request,
        string path,
        CancellationToken cancellationToken)
    {
        string leaseId = Uri.UnescapeDataString(path["/api/v1/servers/".Length..]);
        OfficialRegistryHeartbeatRequest? payload = await ReadAsync<OfficialRegistryHeartbeatRequest>(request, cancellationToken);
        if (payload is null)
            return Json(HttpStatusCode.BadRequest, new { error = "Missing heartbeat payload." });

        if (!_listingsByLeaseId.TryGetValue(leaseId, out Listing? listing) ||
            !StringComparer.Ordinal.Equals(listing.HeartbeatToken, payload.HeartbeatToken))
        {
            return Json(HttpStatusCode.NotFound, new { error = "Lease not found." });
        }

        listing = listing with
        {
            AdvertisedInfo = payload.AdvertisedInfo ?? listing.AdvertisedInfo,
            StatusInfo = payload.StatusInfo with { LastHeartbeatUtc = _clock() },
            HealthState = ServerListingHealthState.Online,
        };
        _listingsByLeaseId[leaseId] = listing;

        OfficialRegistryRegisterResponse response = new(
            leaseId,
            listing.ServerId,
            listing.HeartbeatToken,
            _clock().AddSeconds(60),
            ServerListingTrustLevel.Unverified,
            listing.AdvertisedInfo.Visibility,
            RegistrationId: listing.ServerId);
        return Json(HttpStatusCode.OK, response);
    }

    private async Task<HttpResponseMessage> PresenceAsync(
        HttpRequestMessage request,
        string path,
        CancellationToken cancellationToken)
    {
        string withoutPrefix = path["/api/v1/servers/".Length..];
        string serverId = Uri.UnescapeDataString(withoutPrefix[..^"/presence".Length]);
        OfficialRegistryPresenceRequest? payload = await ReadAsync<OfficialRegistryPresenceRequest>(request, cancellationToken);
        if (payload is null)
            return Json(HttpStatusCode.BadRequest, new { error = "Missing presence payload." });

        if (!_leaseIdByServerId.TryGetValue(serverId, out string? leaseId) ||
            !_listingsByLeaseId.TryGetValue(leaseId, out Listing? listing) ||
            !StringComparer.Ordinal.Equals(listing.HeartbeatToken, payload.HeartbeatToken))
        {
            return Json(HttpStatusCode.NotFound, new { error = "Server not found." });
        }

        ServerListingHealthState healthState = payload.Signal switch
        {
            OfficialRegistryPresenceSignalKind.Startup or OfficialRegistryPresenceSignalKind.Online => ServerListingHealthState.Online,
            OfficialRegistryPresenceSignalKind.Offline or OfficialRegistryPresenceSignalKind.Shutdown => ServerListingHealthState.Offline,
            _ => listing.HealthState,
        };
        listing = listing with { HealthState = healthState };
        _listingsByLeaseId[leaseId] = listing;
        return Json(HttpStatusCode.OK, ToRow(listing));
    }

    private HttpResponseMessage Delete(HttpRequestMessage request, string path)
    {
        string leaseId = Uri.UnescapeDataString(path["/api/v1/servers/".Length..]);
        string heartbeatToken = request.Headers.TryGetValues("X-Heartbeat-Token", out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;

        if (!_listingsByLeaseId.TryGetValue(leaseId, out Listing? listing) ||
            !StringComparer.Ordinal.Equals(listing.HeartbeatToken, heartbeatToken))
        {
            return Json(HttpStatusCode.Unauthorized, new { error = "Invalid lease or heartbeat token." });
        }

        _listingsByLeaseId.Remove(leaseId);
        _leaseIdByServerId.Remove(listing.ServerId);
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private HttpResponseMessage Query(Uri uri)
    {
        IReadOnlyDictionary<string, string> query = ParseQuery(uri.Query);
        var serverQuery = new OfficialRegistryServerQuery
        {
            Search = Get(query, "search"),
            Region = Get(query, "region"),
            Language = Get(query, "language"),
            CompatibilityId = Get(query, "compatibilityId"),
            SortBy = Get(query, "sortBy") ?? "activity",
            Descending = ParseBool(Get(query, "descending")),
            Offset = ParseInt(Get(query, "offset")),
            Limit = ParseInt(Get(query, "limit"), 100),
        };

        IReadOnlyList<ServerDiscoveryRow> rows = _listingsByLeaseId.Values
            .Where(static listing =>
                listing.AdvertisedInfo.Visibility == ServerListingVisibility.Public &&
                listing.HealthState is ServerListingHealthState.Online or ServerListingHealthState.Stale)
            .Select(ToRow)
            .Where(row => Matches(row, serverQuery))
            .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(0, serverQuery.Offset))
            .Take(serverQuery.Limit <= 0 ? 100 : Math.Min(serverQuery.Limit, 200))
            .ToArray();
        return Json(HttpStatusCode.OK, rows);
    }

    private static bool Matches(ServerDiscoveryRow row, OfficialRegistryServerQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Region) &&
            !StringComparer.OrdinalIgnoreCase.Equals(row.Region, query.Region))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Language) &&
            !StringComparer.OrdinalIgnoreCase.Equals(row.PrimaryLanguage, query.Language))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(query.CompatibilityId) ||
            StringComparer.OrdinalIgnoreCase.Equals(row.CompatibilityId, query.CompatibilityId);
    }

    private static ServerDiscoveryRow ToRow(Listing listing)
    {
        ServerAdvertisedInfo advertised = listing.AdvertisedInfo;
        ServerDiscoveryStatusInfo status = listing.StatusInfo;
        return new ServerDiscoveryRow(
            ProviderId: "official",
            ProviderKind: ServerDiscoveryProviderKind.Official,
            ServerId: listing.ServerId,
            DisplayName: advertised.DisplayName,
            Host: ExtractHost(advertised.ConnectHint),
            Port: ExtractPort(advertised.ConnectHint),
            LeaseId: listing.LeaseId,
            TrustLevel: ServerListingTrustLevel.Unverified,
            HealthState: listing.HealthState,
            LifecycleState: ServerListingLifecycleState.Active,
            Visibility: advertised.Visibility,
            ConnectTargetKind: advertised.ConnectTargetKind,
            Region: advertised.Region,
            PrimaryLanguage: advertised.PrimaryLanguage,
            CurrentUsers: status.CurrentUsers,
            MaxUsers: advertised.MaxUsers,
            PeakUsers: status.PeakUsers,
            QueueLength: status.QueueLength,
            Uptime: status.Uptime,
            PasswordRequired: advertised.PasswordRequired,
            WhitelistOnly: advertised.WhitelistOnly,
            ConnectHint: advertised.ConnectHint,
            CompatibilityId: advertised.CompatibilityId,
            VersionLabel: advertised.ContentVersion ?? advertised.ProtocolVersion,
            Tags: advertised.TagsOrEmpty,
            CapabilityFlags: advertised.CapabilityFlags,
            LastSeenUtc: status.LastHeartbeatUtc);
    }

    private static string ExtractHost(string? connectHint)
    {
        if (string.IsNullOrWhiteSpace(connectHint))
            return string.Empty;

        int colon = connectHint.LastIndexOf(':');
        return colon > 0 ? connectHint[..colon] : connectHint;
    }

    private static int ExtractPort(string? connectHint)
    {
        if (string.IsNullOrWhiteSpace(connectHint))
            return 0;

        int colon = connectHint.LastIndexOf(':');
        return colon >= 0 && int.TryParse(connectHint[(colon + 1)..], out int port) ? port : 0;
    }

    private static async Task<T?> ReadAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
            return default;

        await using Stream stream = await request.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T value)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json"),
        };
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string normalized = query.StartsWith('?') ? query[1..] : query;
        foreach (string part in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split('=', 2);
            string key = Uri.UnescapeDataString(pieces[0]);
            string value = pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
            values[key] = value;
        }

        return values;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool ParseBool(string? value)
        => bool.TryParse(value, out bool parsed) && parsed;

    private static int ParseInt(string? value, int defaultValue = 0)
        => int.TryParse(value, out int parsed) ? parsed : defaultValue;

    private sealed record Listing(
        string LeaseId,
        string ServerId,
        string HeartbeatToken,
        ServerAdvertisedInfo AdvertisedInfo,
        ServerDiscoveryStatusInfo StatusInfo,
        ServerListingHealthState HealthState);
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Assert.Equal failed. Expected: {expected}; Actual: {actual}");
    }

    public static void Contains<T>(T expected, IEnumerable<T> values)
    {
        if (!values.Contains(expected))
            throw new InvalidOperationException($"Assert.Contains failed. Missing: {expected}");
    }
}
