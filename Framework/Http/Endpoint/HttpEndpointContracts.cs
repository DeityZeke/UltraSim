using System.Net;

namespace UltraSim.Framework.Http.Endpoint;

/// <summary>A disclosed inbound request, not an authority or authentication context.</summary>
public readonly record struct HttpEndpointRequest(string Method, string Path, bool HasQuery);

/// <summary>Bounded, immutable response memory owned by the service through write completion.</summary>
public readonly record struct HttpEndpointResponse(
    int StatusCode, ReadOnlyMemory<byte> Body, string? ContentType = null, string? CacheControl = null);

/// <summary>One backend-owned request. Writing includes native response completion.</summary>
public interface IHttpEndpointExchange
{
    HttpEndpointRequest Request { get; }
    CancellationToken Aborted { get; }
    ValueTask WriteResponseAsync(HttpEndpointResponse response, CancellationToken cancellation);
    void Abort();
}

/// <summary>Instance-based inbound listener realization; Stop observes native request retirement.</summary>
public interface IHttpEndpointBackend : IDisposable
{
    void Start(IPEndPoint endpoint, Func<IHttpEndpointExchange, Task> dispatch);
    Task StopAsync(CancellationToken cancellation);
    HttpEndpointBackendSnapshot GetSnapshot();
}

public readonly record struct HttpEndpointBackendSnapshot(bool Started, bool Stopped, int ActiveRequests, int UnexpectedFaults = 0);

public readonly record struct HttpServiceLimits
{
    public HttpServiceLimits(int maximumActive, int maximumQueued, int timeoutMilliseconds)
    {
        if (maximumActive is < 1 or > 256 || maximumQueued is < 1 or > 4096 ||
            timeoutMilliseconds is < 1 or > 3_600_000)
            throw new ArgumentOutOfRangeException(nameof(maximumActive));
        MaximumActive = maximumActive;
        MaximumQueued = maximumQueued;
        TimeoutMilliseconds = timeoutMilliseconds;
    }
    public int MaximumActive { get; }
    public int MaximumQueued { get; }
    public int TimeoutMilliseconds { get; }
}

public sealed record HttpRouteDefinition(
    string Method, string Path,
    Func<HttpEndpointRequest, CancellationToken, ValueTask<HttpEndpointResponse>> Handler);

/// <summary>Fixed service-owned routes and independent scheduling policy. No dynamic registration.</summary>
public sealed class HttpServiceDefinition
{
    internal HttpRouteDefinition[] Routes { get; }
    public string ServiceId { get; }
    public HttpServiceLimits Limits { get; }
    public HttpServiceDefinition(string serviceId, HttpServiceLimits limits, IEnumerable<HttpRouteDefinition> routes)
    {
        if (!HttpEndpointSyntax.IsIdentifier(serviceId) || limits.MaximumActive == 0)
            throw new ArgumentException("Invalid HTTP service identity or limits.");
        ArgumentNullException.ThrowIfNull(routes);
        Routes = routes.Take(65_537).ToArray();
        if (Routes.Length is 0 or > 65_536 || Routes.Any(static route => route is null ||
            !HttpEndpointSyntax.IsPath(route.Path) || !HttpEndpointSyntax.IsMethod(route.Method) || route.Handler is null))
            throw new ArgumentException("Invalid bounded HTTP route set.", nameof(routes));
        ServiceId = serviceId;
        Limits = limits;
    }
}

public static class HttpEndpointSyntax
{
    public static bool IsIdentifier(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');
    public static bool IsMethod(string? value) => value is { Length: > 0 and <= 16 } &&
        value.All(static c => c is >= 'A' and <= 'Z');
    public static bool IsPath(string? path)
    {
        if (path is not { Length: > 1 and <= 512 } || path[0] != '/' || path[^1] == '/') return false;
        foreach (string segment in path[1..].Split('/'))
        {
            if (!IsIdentifier(segment) || segment[0] == '.') return false;
        }
        return true;
    }
}

public enum HttpEndpointState { Running, Stopping, Disposed, Faulted }
public readonly record struct HttpServiceSnapshot(
    string ServiceId, int MaximumActive, int MaximumQueued, int Active, int Queued, int PeakActive,
    long Admitted, long Completed, long Refused, long Aborted, long Faults, long BytesWritten, bool WorkersRetired);
public readonly record struct HttpEndpointSnapshot(
    HttpEndpointState State, long Requests, long Rejected, bool CancellationDisposed,
    HttpEndpointBackendSnapshot Backend);
