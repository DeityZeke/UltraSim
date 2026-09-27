using System.Net;
using UltraSim.Framework.Http.Endpoint;
using UltraSim.Bridges.Http.Kestrel;

namespace UltraSim.Server.Hosting;

public readonly record struct StaticContentHttpHostLimits
{
    public StaticContentHttpHostLimits(int maximumResources, long maximumResourceBytes)
        : this(maximumResources, maximumResourceBytes,
            maximumConcurrentResponses: 4,
            maximumQueuedResponses: 8,
            responseTimeoutMilliseconds: 30_000)
    {
    }

    public StaticContentHttpHostLimits(
        int maximumResources,
        long maximumResourceBytes,
        int maximumConcurrentResponses,
        int maximumQueuedResponses,
        int responseTimeoutMilliseconds)
    {
        if (maximumResources is < 1 or > 65_536 ||
            maximumResourceBytes is < 1 or > int.MaxValue ||
            maximumConcurrentResponses is < 1 or > 256 ||
            maximumQueuedResponses is < 1 or > 4_096 ||
            responseTimeoutMilliseconds is < 1 or > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(maximumResources));
        MaximumResources = maximumResources;
        MaximumResourceBytes = maximumResourceBytes;
        MaximumConcurrentResponses = maximumConcurrentResponses;
        MaximumQueuedResponses = maximumQueuedResponses;
        ResponseTimeoutMilliseconds = responseTimeoutMilliseconds;
    }

    public static StaticContentHttpHostLimits Default { get; } = new(
        maximumResources: 4_096,
        maximumResourceBytes: 768L * 1024 * 1024,
        maximumConcurrentResponses: 4,
        maximumQueuedResponses: 8,
        responseTimeoutMilliseconds: 30_000);

    public int MaximumResources { get; }
    public long MaximumResourceBytes { get; }
    public int MaximumConcurrentResponses { get; }
    public int MaximumQueuedResponses { get; }
    public int ResponseTimeoutMilliseconds { get; }

    internal static StaticContentHttpHostLimits Resolve(StaticContentHttpHostLimits limits) =>
        limits.MaximumResources == 0 ? Default : limits;
}

public readonly record struct StaticContentHttpResource(string Route, ReadOnlyMemory<byte> Bytes)
{
    public bool IsValid => HttpEndpointSyntax.IsPath(Route) && !Bytes.IsEmpty;
}

public readonly record struct StaticContentHttpHostSnapshot(
    int ResourceCount,
    long ReservedResourceBytes,
    ulong Requests,
    ulong Served,
    ulong Rejected,
    long BytesServed,
    int ActiveResponses,
    int PeakActiveResponses,
    int MaximumConcurrentResponses,
    int MaximumQueuedResponses);

/// <summary>Fixed immutable content routes; the endpoint owns scheduling and listener lifetime.</summary>
public sealed class StaticContentHttpService
{
    public const string ServiceId = "static-content";
    public HttpServiceDefinition Definition { get; }
    public int ResourceCount { get; }
    public long ReservedResourceBytes { get; }
    private readonly HashSet<string> _routes;
    public StaticContentHttpService(IEnumerable<StaticContentHttpResource> resources, StaticContentHttpHostLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(resources);
        limits = StaticContentHttpHostLimits.Resolve(limits);
        StaticContentHttpResource[] source = resources.Take(limits.MaximumResources + 1).ToArray();
        if (source.Length is 0 || source.Length > limits.MaximumResources ||
            source.Any(static r => !r.IsValid) ||
            source.Select(static r => r.Route).Distinct(StringComparer.Ordinal).Count() != source.Length)
            throw new ArgumentException("Invalid bounded immutable content set.", nameof(resources));
        var routes = new List<HttpRouteDefinition>(source.Length);
        long reserved = 0;
        foreach (StaticContentHttpResource resource in source)
        {
            reserved = checked(reserved + resource.Bytes.Length);
            if (reserved > limits.MaximumResourceBytes)
                throw new ArgumentException("Content byte limit exceeded.", nameof(resources));
            byte[] owned = resource.Bytes.ToArray();
            routes.Add(new("GET", resource.Route, (_, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new HttpEndpointResponse(200, owned,
                    "application/vnd.ultrasim.content-package", "public, immutable, max-age=31536000"));
            }));
        }
        ResourceCount = source.Length;
        ReservedResourceBytes = reserved;
        _routes = source.Select(static r => r.Route).ToHashSet(StringComparer.Ordinal);
        Definition = new(ServiceId, new(limits.MaximumConcurrentResponses, limits.MaximumQueuedResponses,
            limits.ResponseTimeoutMilliseconds), routes);
    }
    public Uri GetUri(Uri baseUri, string route)
    {
        if (!_routes.Contains(route)) throw new ArgumentException("Unknown resource route.", nameof(route));
        return new Uri(baseUri, route[1..]);
    }
    public StaticContentHttpHostSnapshot GetSnapshot(HttpEndpointHost endpoint)
    {
        HttpEndpointSnapshot host = endpoint.GetSnapshot();
        HttpServiceSnapshot service = endpoint.GetServiceSnapshot(ServiceId);
        return new(ResourceCount, ReservedResourceBytes, checked((ulong)host.Requests),
            checked((ulong)service.Completed), checked((ulong)(host.Rejected + service.Aborted + service.Faults)),
            service.BytesWritten, service.Active, service.PeakActive, service.MaximumActive, service.MaximumQueued);
    }
}

/// <summary>Compatibility composition facade. New consumers compose endpoint + service explicitly.
/// Owns no listener implementation or response scheduling.</summary>
public sealed class StaticContentHttpHost : IDisposable
{
    private readonly StaticContentHttpService _service;
    private readonly HttpEndpointHost _endpoint;
    public StaticContentHttpHost(IPEndPoint endpoint, IEnumerable<StaticContentHttpResource> resources,
        StaticContentHttpHostLimits limits = default)
    {
        _service = new(resources, limits);
        _endpoint = new(endpoint, [_service.Definition], new KestrelHttpEndpointBackend());
    }
    public Uri BaseUri => _endpoint.BaseUri;
    public Uri GetUri(string route)
    {
        if (_endpoint.GetSnapshot().State != HttpEndpointState.Running)
            throw new ObjectDisposedException(nameof(StaticContentHttpHost));
        return _service.GetUri(BaseUri, route);
    }
    public StaticContentHttpHostSnapshot GetSnapshot() => _service.GetSnapshot(_endpoint);
    public void Dispose() => _endpoint.Dispose();
}
