using System;
using System.Collections.Generic;

namespace UltraSim.Registry;

public sealed class OfficialRegistryOptions
{
    public const string SectionName = "OfficialRegistry";

    public bool EnableStatePersistence { get; set; } = false;
    public string StateFilePath { get; set; } = "data/official-registry-state.json";
    public int PersistenceFlushIntervalMilliseconds { get; set; } = 1000;
    public bool EnablePolling { get; set; } = false;
    public int PollIntervalSeconds { get; set; } = 60;
    public int PollTimeoutSeconds { get; set; } = 3;
    public int PollFailureThreshold { get; set; } = 3;
    public int PollMaxConcurrency { get; set; } = 16;
    public int LeaseSeconds { get; set; } = 45;
    public int UnverifiedRegisterCooldownSeconds { get; set; } = 0;
    public Dictionary<string, string> VerifiedPublishTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] OfficialServerIds { get; set; } = Array.Empty<string>();
    public int PublicReadRateLimitPermitLimit { get; set; } = 120;
    public int PublicReadRateLimitWindowSeconds { get; set; } = 10;
    public int PublisherWriteRateLimitPermitLimit { get; set; } = 60;
    public int PublisherWriteRateLimitWindowSeconds { get; set; } = 10;
    public int PublicQueryCacheSeconds { get; set; } = 5;
    public bool EnableBenchmarkRateLimitBypass { get; set; } = false;
    public string BenchmarkRateLimitBypassHeaderName { get; set; } = "X-Registry-Benchmark-Bypass";
    public string? BenchmarkRateLimitBypassToken { get; set; }
    public string? AdminApiToken { get; set; }
    public string? PortalApiToken { get; set; }
    public bool AllowPrivateStatusEndpointTargets { get; set; } = false;

    // Reverse proxy / forwarded headers
    public bool EnableForwardedHeaders { get; set; } = false;
    public string[] TrustedProxyAddresses { get; set; } = Array.Empty<string>();

    // CORS — allow browser clients to query public read endpoints
    public bool EnablePublicReadCors { get; set; } = false;
    public string[] CorsAllowedOrigins { get; set; } = Array.Empty<string>();

    // Request body size limit (bytes). Default 65536 (64 KB) — well above any valid
    // registry payload. Set to 0 to use the Kestrel default (30 MB).
    public long MaxRequestBodyBytes { get; set; } = 65536;
    public int HistorySampleIntervalSeconds { get; set; } = 300;
    public int HistorySampleLimit { get; set; } = 96;
    public int AggregateHistorySampleIntervalSeconds { get; set; } = 3600;
    public int AggregateHistorySampleLimit { get; set; } = 720;

    // Lifecycle / retention policy
    public int VerifiedDelistAfterDays { get; set; } = 30;
    public int VerifiedPurgeAfterDelistedDays { get; set; } = 30;
    public int UnverifiedDelistAfterHours { get; set; } = 12;
    public int UnverifiedPurgeAfterDelistedHours { get; set; } = 12; //24 hours total
}
