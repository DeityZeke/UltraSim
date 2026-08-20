using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;

LoadToolOptions options = LoadToolOptions.Parse(args);

using SocketsHttpHandler handler = new()
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    MaxConnectionsPerServer = Math.Max(16, options.Concurrency),
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
};

using HttpClient client = new(handler)
{
    BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)),
};

Console.WriteLine("UltraSim Registry Load Tool");
Console.WriteLine($"Base URL: {client.BaseAddress}");
Console.WriteLine($"Mode: {options.Mode}");
Console.WriteLine($"Concurrency: {options.Concurrency}");
Console.WriteLine($"Duration: {options.DurationSeconds}s");
Console.WriteLine($"Bypass Header: {!string.IsNullOrWhiteSpace(options.BypassToken)}");

if (options.Mode == LoadMode.Read)
{
    Console.WriteLine($"Path: {options.Path}");
    Console.WriteLine($"Conditional GET: {options.UseConditionalGets}");
}
else
{
    Console.WriteLine($"Publish Sequence: register -> {(options.IncludePresence ? "presence -> " : string.Empty)}{(options.IncludeHeartbeat ? "heartbeat" : "done")}");
    Console.WriteLine($"Poll Target Base: {options.PollTargetBaseUrl ?? "(none)"}");
}

Console.WriteLine();

string? etag = null;
if (options.Mode == LoadMode.Read && options.UseConditionalGets)
{
    using HttpResponseMessage etagResponse = await CreateReadRequestAsync(client, options, includeIfNoneMatch: false, etag: null);
    etagResponse.EnsureSuccessStatusCode();
    etag = etagResponse.Headers.ETag?.Tag;
}

ConcurrentBag<double> latenciesMs = new();
ConcurrentDictionary<HttpStatusCode, long> statusCounts = new();
long totalRequests = 0;
long totalFailures = 0;
long totalBytes = 0;
long nextServerOrdinal = 0;

DateTimeOffset stopAtUtc = DateTimeOffset.UtcNow.AddSeconds(options.DurationSeconds);
Task[] workers = new Task[options.Concurrency];

for (int i = 0; i < workers.Length; i++)
    workers[i] = options.Mode == LoadMode.Read
        ? RunReadWorkerAsync()
        : RunPublishWorkerAsync();

await Task.WhenAll(workers);

double[] orderedLatencies = latenciesMs.OrderBy(static value => value).ToArray();
double p50 = Percentile(orderedLatencies, 0.50);
double p95 = Percentile(orderedLatencies, 0.95);
double p99 = Percentile(orderedLatencies, 0.99);
double throughput = options.DurationSeconds > 0
    ? totalRequests / (double)options.DurationSeconds
    : 0d;

Console.WriteLine("Results");
Console.WriteLine($"Requests: {totalRequests}");
Console.WriteLine($"Failures: {totalFailures}");
Console.WriteLine($"Throughput: {throughput:F1} req/s");
Console.WriteLine($"Bytes: {totalBytes}");
Console.WriteLine($"Latency p50: {p50:F2} ms");
Console.WriteLine($"Latency p95: {p95:F2} ms");
Console.WriteLine($"Latency p99: {p99:F2} ms");
Console.WriteLine("Status counts:");

foreach ((HttpStatusCode statusCode, long count) in statusCounts.OrderBy(static pair => (int)pair.Key))
    Console.WriteLine($"  {(int)statusCode} {statusCode}: {count}");

if (options.FetchRuntimeSnapshot)
{
    Console.WriteLine();
    Console.WriteLine("Registry runtime snapshot");
    try
    {
        using HttpRequestMessage runtimeRequest = CreateRequest(HttpMethod.Get, "/api/v1/admin/runtime", options);
        using HttpResponseMessage runtimeResponse = await client.SendAsync(runtimeRequest);
        string payload = await runtimeResponse.Content.ReadAsStringAsync();
        Console.WriteLine(payload);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to fetch runtime snapshot: {ex.GetType().Name}: {ex.Message}");
    }
}

async Task RunReadWorkerAsync()
{
    while (DateTimeOffset.UtcNow < stopAtUtc)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await CreateReadRequestAsync(client, options, options.UseConditionalGets, etag);
            byte[] payload = await response.Content.ReadAsByteArrayAsync();
            stopwatch.Stop();

            latenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            statusCounts.AddOrUpdate(response.StatusCode, 1, static (_, count) => count + 1);
            Interlocked.Increment(ref totalRequests);
            Interlocked.Add(ref totalBytes, payload.Length);
        }
        catch
        {
            stopwatch.Stop();
            latenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            Interlocked.Increment(ref totalRequests);
            Interlocked.Increment(ref totalFailures);
        }
    }
}

async Task RunPublishWorkerAsync()
{
    while (DateTimeOffset.UtcNow < stopAtUtc)
    {
        long serverOrdinal = Interlocked.Increment(ref nextServerOrdinal);
        string serverId = $"load-{serverOrdinal}";
        string connectHint = $"load-{serverOrdinal}.test:{options.ConnectPort}";
        string? statusEndpointUrl = BuildStatusEndpointUrl(options.PollTargetBaseUrl, serverOrdinal);

        OfficialRegistryRegisterRequest registerRequest = new(
            DiscoveryPublishMode.Unverified,
            new ServerAdvertisedInfo(
                ServerId: serverId,
                DisplayName: $"Load Server {serverOrdinal}",
                ConnectHint: connectHint,
                Region: "load-test",
                PrimaryLanguage: "en",
                CompatibilityId: "load",
                StatusEndpointUrl: statusEndpointUrl),
            new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: true,
                CurrentUsers: (int)(serverOrdinal % 50),
                PeakUsers: (int)(serverOrdinal % 100),
                Uptime: TimeSpan.FromSeconds(serverOrdinal % 3600),
                Status: "Running"));

        Stopwatch registerStopwatch = Stopwatch.StartNew();
        OfficialRegistryRegisterResponse? registerResponseBody = null;

        try
        {
            using HttpRequestMessage registerRequestMessage = CreateRequest(HttpMethod.Post, "/api/v1/servers/register", options);
            registerRequestMessage.Content = JsonContent.Create(registerRequest);
            using HttpResponseMessage registerResponse = await client.SendAsync(registerRequestMessage);
            byte[] registerPayload = await registerResponse.Content.ReadAsByteArrayAsync();
            registerStopwatch.Stop();

            latenciesMs.Add(registerStopwatch.Elapsed.TotalMilliseconds);
            statusCounts.AddOrUpdate(registerResponse.StatusCode, 1, static (_, count) => count + 1);
            Interlocked.Increment(ref totalRequests);
            Interlocked.Add(ref totalBytes, registerPayload.Length);

            if (!registerResponse.IsSuccessStatusCode)
                continue;

            registerResponseBody = await registerResponse.Content.ReadFromJsonAsync<OfficialRegistryRegisterResponse>();
            if (registerResponseBody == null)
            {
                Interlocked.Increment(ref totalFailures);
                continue;
            }
        }
        catch
        {
            registerStopwatch.Stop();
            latenciesMs.Add(registerStopwatch.Elapsed.TotalMilliseconds);
            Interlocked.Increment(ref totalRequests);
            Interlocked.Increment(ref totalFailures);
            continue;
        }

        if (options.IncludePresence)
        {
            await SendPublishStepAsync(
                client,
                CreateRequest(
                    HttpMethod.Post,
                    $"/api/v1/servers/{Uri.EscapeDataString(registerResponseBody.ServerId)}/presence",
                    options),
                new OfficialRegistryPresenceRequest(
                    registerResponseBody.HeartbeatToken,
                    OfficialRegistryPresenceSignalKind.Online));
        }

        if (options.IncludeHeartbeat)
        {
            await SendPublishStepAsync(
                client,
                CreateRequest(
                    HttpMethod.Put,
                    $"/api/v1/servers/{Uri.EscapeDataString(registerResponseBody.LeaseId)}",
                    options),
                new OfficialRegistryHeartbeatRequest(
                    registerResponseBody.HeartbeatToken,
                    new ServerDiscoveryStatusInfo(
                        IsAcceptingConnections: true,
                        CurrentUsers: (int)(serverOrdinal % 75),
                        PeakUsers: (int)(serverOrdinal % 125),
                        Uptime: TimeSpan.FromSeconds((serverOrdinal % 3600) + 30),
                        Status: "Busy")));
        }
    }
}

static async Task<HttpResponseMessage> CreateReadRequestAsync(
    HttpClient client,
    LoadToolOptions options,
    bool includeIfNoneMatch,
    string? etag)
{
    using HttpRequestMessage request = CreateRequest(HttpMethod.Get, options.Path, options);
    if (includeIfNoneMatch && !string.IsNullOrWhiteSpace(etag))
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));

    return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
}

async Task SendPublishStepAsync<TBody>(
    HttpClient client,
    HttpRequestMessage request,
    TBody body)
{
    using (request)
    {
        request.Content = JsonContent.Create(body);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request);
            byte[] payload = await response.Content.ReadAsByteArrayAsync();
            stopwatch.Stop();

            latenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            statusCounts.AddOrUpdate(response.StatusCode, 1, static (_, count) => count + 1);
            Interlocked.Increment(ref totalRequests);
            Interlocked.Add(ref totalBytes, payload.Length);

            if (!response.IsSuccessStatusCode)
                Interlocked.Increment(ref totalFailures);
        }
        catch
        {
            stopwatch.Stop();
            latenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            Interlocked.Increment(ref totalRequests);
            Interlocked.Increment(ref totalFailures);
        }
    }
}

static HttpRequestMessage CreateRequest(HttpMethod method, string path, LoadToolOptions options)
{
    HttpRequestMessage request = new(method, path);
    if (!string.IsNullOrWhiteSpace(options.BypassToken))
        request.Headers.Add(options.BypassHeaderName, options.BypassToken);

    return request;
}

static string? BuildStatusEndpointUrl(string? baseUrl, long serverOrdinal)
{
    if (string.IsNullOrWhiteSpace(baseUrl))
        return null;

    string normalized = baseUrl.Trim();
    if (normalized.Contains("{id}", StringComparison.Ordinal))
        return normalized.Replace("{id}", serverOrdinal.ToString(), StringComparison.Ordinal);

    return $"{normalized.TrimEnd('/')}/{serverOrdinal}";
}

static double Percentile(double[] values, double percentile)
{
    if (values.Length == 0)
        return 0d;

    int index = (int)Math.Ceiling(percentile * values.Length) - 1;
    index = Math.Clamp(index, 0, values.Length - 1);
    return values[index];
}

file enum LoadMode
{
    Read,
    Publish,
}

file sealed record LoadToolOptions
{
    public string BaseUrl { get; private init; } = "http://127.0.0.1:5180";
    public string Path { get; private init; } = "/api/v1/servers";
    public int Concurrency { get; private init; } = 50;
    public int DurationSeconds { get; private init; } = 30;
    public int TimeoutSeconds { get; private init; } = 10;
    public bool UseConditionalGets { get; private init; }
    public string BypassHeaderName { get; private init; } = "X-Registry-Benchmark-Bypass";
    public string? BypassToken { get; private init; }
    public LoadMode Mode { get; private init; } = LoadMode.Read;
    public bool IncludePresence { get; private init; } = true;
    public bool IncludeHeartbeat { get; private init; } = true;
    public bool FetchRuntimeSnapshot { get; private init; }
    public string? PollTargetBaseUrl { get; private init; }
    public int ConnectPort { get; private init; } = 42042;

    public static LoadToolOptions Parse(string[] args)
    {
        LoadToolOptions options = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? next = i + 1 < args.Length ? args[i + 1] : null;

            switch (arg)
            {
                case "--base-url" when !string.IsNullOrWhiteSpace(next):
                    options = options with { BaseUrl = next };
                    i++;
                    break;
                case "--path" when !string.IsNullOrWhiteSpace(next):
                    options = options with { Path = next };
                    i++;
                    break;
                case "--concurrency" when int.TryParse(next, out int concurrency):
                    options = options with { Concurrency = Math.Max(1, concurrency) };
                    i++;
                    break;
                case "--duration-seconds" when int.TryParse(next, out int durationSeconds):
                    options = options with { DurationSeconds = Math.Max(1, durationSeconds) };
                    i++;
                    break;
                case "--timeout-seconds" when int.TryParse(next, out int timeoutSeconds):
                    options = options with { TimeoutSeconds = Math.Max(1, timeoutSeconds) };
                    i++;
                    break;
                case "--conditional":
                    options = options with { UseConditionalGets = true };
                    break;
                case "--bypass-header" when !string.IsNullOrWhiteSpace(next):
                    options = options with { BypassHeaderName = next };
                    i++;
                    break;
                case "--bypass-token" when !string.IsNullOrWhiteSpace(next):
                    options = options with { BypassToken = next };
                    i++;
                    break;
                case "--mode" when !string.IsNullOrWhiteSpace(next):
                    options = options with
                    {
                        Mode = next.Equals("publish", StringComparison.OrdinalIgnoreCase)
                            ? LoadMode.Publish
                            : LoadMode.Read,
                    };
                    i++;
                    break;
                case "--skip-presence":
                    options = options with { IncludePresence = false };
                    break;
                case "--skip-heartbeat":
                    options = options with { IncludeHeartbeat = false };
                    break;
                case "--runtime-snapshot":
                    options = options with { FetchRuntimeSnapshot = true };
                    break;
                case "--poll-target-base-url" when !string.IsNullOrWhiteSpace(next):
                    options = options with { PollTargetBaseUrl = next };
                    i++;
                    break;
                case "--connect-port" when int.TryParse(next, out int connectPort):
                    options = options with { ConnectPort = Math.Max(1, connectPort) };
                    i++;
                    break;
            }
        }

        return options;
    }
}
