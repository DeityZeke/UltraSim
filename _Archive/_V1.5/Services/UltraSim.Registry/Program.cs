using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Registry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OfficialRegistryOptions>(
    builder.Configuration.GetSection(OfficialRegistryOptions.SectionName));
builder.Services.AddSingleton<RegistryRuntimeMetrics>();
builder.Services.AddSingleton<InMemoryOfficialRegistryStore>();
builder.Services.AddMemoryCache();
builder.Services.AddTransient<SsrfProtectionHandler>();
builder.Services.AddHttpClient("registry-poller")
    .AddHttpMessageHandler<SsrfProtectionHandler>();
builder.Services.AddHostedService<RegistryPollingService>();
builder.Services.AddHostedService<RegistryPersistenceService>();
builder.Services.AddHealthChecks()
    .AddCheck<RegistryHealthCheck>("registry-store");
bool isDevelopment = builder.Environment.IsDevelopment();
OfficialRegistryOptions registryOptions = builder.Configuration
    .GetSection(OfficialRegistryOptions.SectionName)
    .Get<OfficialRegistryOptions>() ?? new OfficialRegistryOptions();

if (registryOptions.MaxRequestBodyBytes > 0)
{
    builder.WebHost.ConfigureKestrel(kestrel =>
        kestrel.Limits.MaxRequestBodySize = registryOptions.MaxRequestBodyBytes);
}

if (registryOptions.EnableForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(fwdOptions =>
    {
        fwdOptions.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        fwdOptions.ForwardLimit = 1;
        if (registryOptions.TrustedProxyAddresses.Length > 0)
        {
            fwdOptions.KnownNetworks.Clear();
            fwdOptions.KnownProxies.Clear();
            foreach (string addr in registryOptions.TrustedProxyAddresses)
            {
                if (IPAddress.TryParse(addr.Trim(), out IPAddress? ip))
                    fwdOptions.KnownProxies.Add(ip);
            }
        }
        // If no trusted addresses specified, ASP.NET Core defaults to loopback-only.
    });
}

if (registryOptions.EnablePublicReadCors)
{
    builder.Services.AddCors(corsOptions =>
    {
        corsOptions.AddPolicy("registry-public-read", policy =>
        {
            CorsPolicyBuilder policyBuilder = policy
                .WithMethods("GET")
                .WithHeaders("If-None-Match", "Accept");

            if (registryOptions.CorsAllowedOrigins.Length > 0)
                policyBuilder.WithOrigins(registryOptions.CorsAllowedOrigins);
            else
                policyBuilder.AllowAnyOrigin();
        });
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("registry-read", httpContext =>
    {
        OfficialRegistryOptions configured = httpContext.RequestServices
            .GetRequiredService<IOptions<OfficialRegistryOptions>>().Value;
        return BuildFixedWindowPolicy(httpContext, configured, isDevelopment,
            configured.PublicReadRateLimitPermitLimit, configured.PublicReadRateLimitWindowSeconds);
    });

    options.AddPolicy("registry-write", httpContext =>
    {
        OfficialRegistryOptions configured = httpContext.RequestServices
            .GetRequiredService<IOptions<OfficialRegistryOptions>>().Value;
        return BuildFixedWindowPolicy(httpContext, configured, isDevelopment,
            configured.PublisherWriteRateLimitPermitLimit, configured.PublisherWriteRateLimitWindowSeconds);
    });
});

var app = builder.Build();

// Startup config validation — warn early about security-sensitive gaps.
{
    ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("UltraSim.Registry.Startup");
    OfficialRegistryOptions configuredRegistryOptions = app.Services
        .GetRequiredService<IOptions<OfficialRegistryOptions>>()
        .Value;

    if (!isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(configuredRegistryOptions.AdminApiToken))
            startupLogger.LogWarning("AdminApiToken is not configured. The /api/v1/admin/* endpoints are inaccessible.");

        if (string.IsNullOrWhiteSpace(configuredRegistryOptions.PortalApiToken))
            startupLogger.LogWarning("PortalApiToken is not configured. Portal owner-claim endpoints will reject all requests.");
    }

    if (configuredRegistryOptions.AllowPrivateStatusEndpointTargets)
        startupLogger.LogWarning("AllowPrivateStatusEndpointTargets is enabled. Registry polling may reach loopback and private-network status endpoints.");

    if (configuredRegistryOptions.EnableForwardedHeaders && configuredRegistryOptions.TrustedProxyAddresses.Length == 0)
        startupLogger.LogWarning("EnableForwardedHeaders is true but TrustedProxyAddresses is empty. Forwarded headers will be trusted from loopback only.");

    if (configuredRegistryOptions.EnablePublicReadCors && configuredRegistryOptions.CorsAllowedOrigins.Length == 0)
        startupLogger.LogInformation("CORS is enabled with AllowAnyOrigin. Consider restricting CorsAllowedOrigins for production.");
}

if (registryOptions.EnableForwardedHeaders)
    app.UseForwardedHeaders();

if (registryOptions.EnablePublicReadCors)
    app.UseCors();

app.UseRateLimiter();

app.MapGet("/", () => Results.Redirect("/api/v1/meta"));

app.MapPost("/api/v1/servers/register", (
    OfficialRegistryRegisterRequest request,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options,
    HttpContext httpContext) =>
{
    try
    {
        string publisherFingerprint = ResolvePublisherFingerprint(httpContext);
        OfficialRegistryRegisterResponse response = store.Register(
            request,
            publisherFingerprint,
            options.Value,
            DateTimeOffset.UtcNow);
        return Results.Ok(response);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapPut("/api/v1/servers/{leaseId}", (
    string leaseId,
    OfficialRegistryHeartbeatRequest request,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    try
    {
        if (!store.TryHeartbeat(leaseId, request, options.Value, DateTimeOffset.UtcNow, out OfficialRegistryRegisterResponse? response))
            return Results.Unauthorized();

        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapDelete("/api/v1/servers/{leaseId}", (
    string leaseId,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store) =>
{
    string heartbeatToken = httpContext.Request.Headers["X-Heartbeat-Token"].ToString();
    if (string.IsNullOrWhiteSpace(heartbeatToken))
        return Results.BadRequest(new { error = "Heartbeat token is required." });

    return store.TryDelete(leaseId, heartbeatToken)
        ? Results.NoContent()
        : Results.Unauthorized();
}).RequireRateLimiting("registry-write");

app.MapPost("/api/v1/servers/{serverId}/presence", (
    string serverId,
    OfficialRegistryPresenceRequest request,
    InMemoryOfficialRegistryStore store) =>
{
    try
    {
        return store.TryApplyPresence(serverId, request, DateTimeOffset.UtcNow, out ServerDiscoveryRow? row)
            ? Results.Ok(row)
            : Results.Unauthorized();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapPut("/api/v1/servers/{serverId}/metadata", (
    string serverId,
    OfficialRegistryMetadataUpdateRequest request,
    InMemoryOfficialRegistryStore store) =>
{
    try
    {
        return store.TryUpdateMetadata(serverId, request, DateTimeOffset.UtcNow, out ServerDiscoveryRow? row)
            ? Results.Ok(row)
            : Results.Unauthorized();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapPost("/api/v1/servers/claims", (
    OfficialRegistryOwnerClaimRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidPortalToken(httpContext, options.Value))
        return Results.Unauthorized();

    try
    {
        OfficialRegistryOwnerClaimResponse response = store.ClaimServer(request, DateTimeOffset.UtcNow);
        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapPut("/api/v1/servers/{serverId}/owner", (
    string serverId,
    OfficialRegistryOwnerMetadataUpdateRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidPortalToken(httpContext, options.Value))
        return Results.Unauthorized();

    try
    {
        return store.TryUpdateOwnerMetadata(serverId, request, DateTimeOffset.UtcNow, out ServerDiscoveryDetail? detail)
            ? Results.Ok(detail)
            : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapPut("/api/v1/servers/{serverId}/owner-token", (
    string serverId,
    OfficialRegistryOwnerTokenUpdateRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidPortalToken(httpContext, options.Value))
        return Results.Unauthorized();

    try
    {
        return store.TryUpdateOwnerToken(serverId, request, DateTimeOffset.UtcNow, out ServerDiscoveryDetail? detail)
            ? Results.Ok(detail)
            : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapDelete("/api/v1/servers/{serverId}/owner", (
    string serverId,
    [FromBody] OfficialRegistryOwnerDeleteRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidPortalToken(httpContext, options.Value))
        return Results.Unauthorized();

    return store.TryDeleteOwnerClaimedServer(serverId, request.OwnerAccountId)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireRateLimiting("registry-write");

app.MapPut("/api/v1/servers/{serverId}/vote-count", (
    string serverId,
    OfficialRegistryVoteCountUpdateRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidPortalToken(httpContext, options.Value))
        return Results.Unauthorized();

    return store.TrySetVoteCount(serverId, request.VoteCount, out ServerDiscoveryDetail? detail) && detail != null
        ? Results.Ok(detail)
        : Results.NotFound();
}).RequireRateLimiting("registry-write");

app.MapPut("/api/v1/admin/servers/{serverId}/lifecycle", (
    string serverId,
    OfficialRegistryLifecycleUpdateRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    try
    {
        return store.TrySetLifecycleState(serverId, request.LifecycleState, DateTimeOffset.UtcNow, out ServerDiscoveryDetail? detail)
            ? Results.Ok(detail)
            : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapGet("/api/v1/admin/servers", (
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    IReadOnlyList<OfficialRegistryAdminServerRecord> records = store.GetAdminServerRecords(DateTimeOffset.UtcNow);
    return Results.Ok(records);
}).RequireRateLimiting("registry-read");

app.MapGet("/api/v1/admin/servers/{serverId}", (
    string serverId,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    return store.TryGetAdminServerRecord(serverId, DateTimeOffset.UtcNow, out OfficialRegistryAdminServerRecord? record)
        ? Results.Ok(record)
        : Results.NotFound();
}).RequireRateLimiting("registry-read");

app.MapDelete("/api/v1/admin/servers/{serverId}", (
    string serverId,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    return store.TryDeleteAdminServer(serverId)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireRateLimiting("registry-write");

app.MapPost("/api/v1/admin/servers/{serverId}/host-ban", (
    string serverId,
    OfficialRegistryAdminHostBanRequest request,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    try
    {
        return store.TryBanHostForServer(serverId, request.Reason, DateTimeOffset.UtcNow, out OfficialRegistryAdminHostBanRecord? banRecord)
            ? Results.Ok(banRecord)
            : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

app.MapGet("/api/v1/admin/bans", (
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    return Results.Ok(store.GetHostBans());
}).RequireRateLimiting("registry-read");

app.MapDelete("/api/v1/admin/bans/hosts/{host}", (
    string host,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    return store.TryLiftHostBan(host)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireRateLimiting("registry-write");

app.MapPost("/api/v1/admin/servers/{serverId}/retention-reset", (
    string serverId,
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    try
    {
        return store.TryResetRetention(serverId, DateTimeOffset.UtcNow, out ServerDiscoveryDetail? detail)
            ? Results.Ok(detail)
            : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("registry-write");

var serversEndpoint = app.MapGet("/api/v1/servers", (
    string? search,
    string? region,
    string? language,
    ServerListingTrustLevel? trustLevel,
    bool? passwordRequired,
    bool? whitelistOnly,
    string? compatibilityId,
    string? sortBy,
    bool? descending,
    int? offset,
    int? limit,
    HttpContext httpContext,
    IMemoryCache cache,
    IOptions<OfficialRegistryOptions> options,
    InMemoryOfficialRegistryStore store) =>
{
    OfficialRegistryServerQuery query = new()
    {
        Search = search,
        Region = region,
        Language = language,
        TrustLevel = trustLevel,
        PasswordRequired = passwordRequired,
        WhitelistOnly = whitelistOnly,
        CompatibilityId = compatibilityId,
        SortBy = sortBy,
        Descending = descending ?? false,
        Offset = offset ?? 0,
        Limit = limit ?? 100,
    };

    string cacheKey = BuildCacheKey("servers", httpContext.Request.QueryString.Value, store.CurrentQueryRevision);
    CachedRegistryResponse<IReadOnlyList<ServerDiscoveryRow>> cached = cache.GetOrCreate(
        cacheKey,
        entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(1, options.Value.PublicQueryCacheSeconds));
            IReadOnlyList<ServerDiscoveryRow> rows = store.QueryVisibleRows(query, DateTimeOffset.UtcNow);
            return new CachedRegistryResponse<IReadOnlyList<ServerDiscoveryRow>>(rows, ComputeEtag(rows));
        })!;

    return CreateCachedJsonResult(httpContext, cached, options.Value.PublicQueryCacheSeconds);
}).RequireRateLimiting("registry-read");

var serverDetailEndpoint = app.MapGet("/api/v1/servers/{serverId}", (
    string serverId,
    InMemoryOfficialRegistryStore store) =>
{
    return store.TryGetByServerId(serverId, DateTimeOffset.UtcNow, out ServerDiscoveryDetail? detail)
        ? Results.Ok(detail)
        : Results.NotFound();
}).RequireRateLimiting("registry-read");

var metaEndpoint = app.MapGet("/api/v1/meta", (
    IOptions<OfficialRegistryOptions> options) =>
{
    return Results.Ok(new
    {
        name = "UltraSim Official Registry",
        apiVersion = 1,
        leaseSeconds = options.Value.LeaseSeconds,
    });
}).RequireRateLimiting("registry-read");

app.MapGet("/api/v1/admin/runtime", (
    HttpContext httpContext,
    InMemoryOfficialRegistryStore store,
    RegistryRuntimeMetrics metrics,
    IOptions<OfficialRegistryOptions> options) =>
{
    if (!HasValidAdminToken(httpContext, options.Value))
        return Results.Unauthorized();

    RegistryRuntimeSnapshot snapshot = new(
        DateTimeOffset.UtcNow,
        store.GetRuntimeSnapshot(DateTimeOffset.UtcNow),
        metrics.GetSnapshot(),
        store.GetAggregateMetricsSnapshot(DateTimeOffset.UtcNow));
    return Results.Ok(snapshot);
}).RequireRateLimiting("registry-read");

var regionsEndpoint = app.MapGet("/api/v1/regions", (
    HttpContext httpContext,
    IMemoryCache cache,
    IOptions<OfficialRegistryOptions> options,
    InMemoryOfficialRegistryStore store) =>
{
    string cacheKey = BuildCacheKey("regions", httpContext.Request.QueryString.Value, store.CurrentQueryRevision);
    CachedRegistryResponse<string[]> cached = cache.GetOrCreate(
        cacheKey,
        entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(1, options.Value.PublicQueryCacheSeconds));
            IReadOnlyList<ServerDiscoveryRow> rows = store.GetVisibleRows(DateTimeOffset.UtcNow);
            string[] regions = rows
                .Select(static row => row.Region)
                .Where(static region => !string.IsNullOrWhiteSpace(region))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static region => region, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();
            return new CachedRegistryResponse<string[]>(regions, ComputeEtag(regions));
        })!;

    return CreateCachedJsonResult(httpContext, cached, options.Value.PublicQueryCacheSeconds);
}).RequireRateLimiting("registry-read");

if (registryOptions.EnablePublicReadCors)
{
    serversEndpoint.RequireCors("registry-public-read");
    serverDetailEndpoint.RequireCors("registry-public-read");
    metaEndpoint.RequireCors("registry-public-read");
    regionsEndpoint.RequireCors("registry-public-read");
}

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = async (httpContext, report) =>
    {
        httpContext.Response.ContentType = "application/json";

        object payload = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                static pair => pair.Key,
                static pair => new
                {
                    status = pair.Value.Status.ToString(),
                    description = pair.Value.Description,
                    duration = pair.Value.Duration.TotalMilliseconds,
                    data = pair.Value.Data,
                },
                StringComparer.Ordinal),
        };

        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(payload));
    },
});

app.Run();

static string ResolvePublisherFingerprint(HttpContext httpContext)
{
    return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static string ResolveRateLimitPartitionKey(HttpContext httpContext)
{
    return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

static bool IsBenchmarkRateLimitBypassEnabled(HttpContext httpContext, OfficialRegistryOptions options)
{
    if (!options.EnableBenchmarkRateLimitBypass)
        return false;

    if (string.IsNullOrWhiteSpace(options.BenchmarkRateLimitBypassHeaderName) ||
        string.IsNullOrWhiteSpace(options.BenchmarkRateLimitBypassToken))
    {
        return false;
    }

    string headerValue = httpContext.Request.Headers[options.BenchmarkRateLimitBypassHeaderName].ToString();
    return RegistryAuth.ConstantTimeEquals(headerValue, options.BenchmarkRateLimitBypassToken!);
}

static RateLimitPartition<string> BuildFixedWindowPolicy(
    HttpContext httpContext,
    OfficialRegistryOptions options,
    bool isDev,
    int permitLimit,
    int windowSeconds)
{
    if (isDev && IsBenchmarkRateLimitBypassEnabled(httpContext, options))
        return RateLimitPartition.GetNoLimiter("benchmark-bypass");

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ResolveRateLimitPartitionKey(httpContext),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}

static bool HasValidAdminToken(HttpContext httpContext, OfficialRegistryOptions options)
{
    if (string.IsNullOrWhiteSpace(options.AdminApiToken))
        return false;

    string headerValue = httpContext.Request.Headers["X-Admin-Token"].ToString();
    return RegistryAuth.ConstantTimeEquals(headerValue, options.AdminApiToken);
}

static bool HasValidPortalToken(HttpContext httpContext, OfficialRegistryOptions options)
{
    if (string.IsNullOrWhiteSpace(options.PortalApiToken))
        return false;

    string headerValue = httpContext.Request.Headers["X-Portal-Api-Token"].ToString();
    return RegistryAuth.ConstantTimeEquals(headerValue, options.PortalApiToken);
}


static IResult CreateCachedJsonResult<T>(HttpContext httpContext, CachedRegistryResponse<T> cached, int cacheSeconds)
{
    httpContext.Response.Headers.ETag = cached.ETag;
    httpContext.Response.Headers.CacheControl = $"public,max-age={Math.Max(1, cacheSeconds)}";

    if (RequestHasMatchingEtag(httpContext, cached.ETag))
        return Results.StatusCode(StatusCodes.Status304NotModified);

    return Results.Json(cached.Payload);
}

static bool RequestHasMatchingEtag(HttpContext httpContext, string etag)
{
    string ifNoneMatch = httpContext.Request.Headers.IfNoneMatch.ToString();
    if (string.IsNullOrWhiteSpace(ifNoneMatch))
        return false;

    string[] values = ifNoneMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (int i = 0; i < values.Length; i++)
    {
        if (StringComparer.Ordinal.Equals(values[i], etag))
            return true;
    }

    return false;
}

static string BuildCacheKey(string prefix, string? queryString, long revision)
{
    return string.IsNullOrWhiteSpace(queryString)
        ? $"{prefix}:rev:{revision}"
        : $"{prefix}:rev:{revision}:{queryString}";
}

static string ComputeEtag<T>(T payload)
{
    byte[] bytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload));
    byte[] hash = SHA256.HashData(bytes);
    return $"\"{Convert.ToHexString(hash)}\"";
}

file sealed record CachedRegistryResponse<T>(T Payload, string ETag);

public partial class Program
{
}
