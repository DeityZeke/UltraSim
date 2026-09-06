using System.Net;

namespace UltraSim.Server.Hosting;

public readonly record struct StaticContentHttpHostLimits
{
    public StaticContentHttpHostLimits(int maximumResources, long maximumResourceBytes)
    {
        if (maximumResources is < 1 or > 65_536 || maximumResourceBytes is < 1 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maximumResources));
        MaximumResources = maximumResources;
        MaximumResourceBytes = maximumResourceBytes;
    }

    public static StaticContentHttpHostLimits Default { get; } = new(
        maximumResources: 4_096,
        maximumResourceBytes: 768L * 1024 * 1024);

    public int MaximumResources { get; }
    public long MaximumResourceBytes { get; }

    internal static StaticContentHttpHostLimits Resolve(StaticContentHttpHostLimits limits) =>
        limits.MaximumResources == 0 ? Default : limits;
}

public readonly record struct StaticContentHttpResource(string Route, ReadOnlyMemory<byte> Bytes)
{
    public bool IsValid => StaticContentHttpHost.IsValidRoute(Route) && !Bytes.IsEmpty;
}

public readonly record struct StaticContentHttpHostSnapshot(
    int ResourceCount,
    long ReservedResourceBytes,
    ulong Requests,
    ulong Served,
    ulong Rejected,
    long BytesServed);

/// <summary>
/// Small bounded immutable-byte HTTP host. It runs independently from authority/Network pumps and
/// knows nothing about packages, manifests, realms, or Game semantics.
/// </summary>
public sealed class StaticContentHttpHost : IDisposable
{
    private const string ContentType = "application/vnd.ultrasim.content-package";
    private readonly HttpListener _listener;
    private readonly Dictionary<string, byte[]> _resources;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private readonly long _reservedResourceBytes;
    private long _requests;
    private long _served;
    private long _rejected;
    private long _bytesServed;
    private bool _disposed;

    public StaticContentHttpHost(
        IPEndPoint endpoint,
        IEnumerable<StaticContentHttpResource> resources,
        StaticContentHttpHostLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(resources);
        limits = StaticContentHttpHostLimits.Resolve(limits);
        if (endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            endpoint.Port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(endpoint));

        StaticContentHttpResource[] source = resources.Take(limits.MaximumResources + 1).ToArray();
        if (source.Length is 0 || source.Length > limits.MaximumResources ||
            source.Any(static value => !value.IsValid) ||
            source.Select(static value => value.Route).Distinct(StringComparer.Ordinal).Count() != source.Length)
            throw new ArgumentException("The static content resource set is invalid.", nameof(resources));
        _resources = new Dictionary<string, byte[]>(source.Length, StringComparer.Ordinal);
        long reserved = 0;
        foreach (StaticContentHttpResource resource in source)
        {
            reserved = checked(reserved + resource.Bytes.Length);
            if (reserved > limits.MaximumResourceBytes)
                throw new ArgumentException("The static content resource set exceeds its byte limit.", nameof(resources));
            _resources.Add(resource.Route, resource.Bytes.ToArray());
        }
        _reservedResourceBytes = reserved;

        string prefix = $"http://{endpoint.Address}:{endpoint.Port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        BaseUri = new Uri(prefix, UriKind.Absolute);
        _worker = Task.Run(RunAsync);
    }

    public Uri BaseUri { get; }

    public Uri GetUri(string route)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsValidRoute(route) || !_resources.ContainsKey(route))
            throw new ArgumentException("The route is not one hosted resource.", nameof(route));
        return new Uri(BaseUri, route[1..]);
    }

    public StaticContentHttpHostSnapshot GetSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new StaticContentHttpHostSnapshot(
            _resources.Count,
            _reservedResourceBytes,
            ToUnsigned(Interlocked.Read(ref _requests)),
            ToUnsigned(Interlocked.Read(ref _served)),
            ToUnsigned(Interlocked.Read(ref _rejected)),
            Interlocked.Read(ref _bytesServed));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();
        _listener.Stop();
        _listener.Close();
        try { _worker.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        _stopping.Dispose();
    }

    internal static bool IsValidRoute(string? route)
    {
        if (string.IsNullOrEmpty(route) || route.Length > 512 || route[0] != '/' || route[^1] == '/' ||
            route.Contains('\\') || route.Contains(':') || route.Contains('?') || route.Contains('#') ||
            route.Contains('%'))
            return false;
        foreach (string segment in route[1..].Split('/'))
        {
            if (segment.Length is 0 or > 128 || segment is "." or ".." || segment[0] == '.') return false;
            foreach (char value in segment)
                if (!(value is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')) return false;
        }
        return true;
    }

    private async Task RunAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(_stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (_stopping.IsCancellationRequested) { break; }
            await HandleAsync(context).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        Interlocked.Increment(ref _requests);
        try
        {
            string route = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (context.Request.HttpMethod != "GET" || context.Request.Url?.Query.Length > 0 ||
                !_resources.TryGetValue(route, out byte[]? bytes))
            {
                Interlocked.Increment(ref _rejected);
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.ContentLength64 = 0;
                return;
            }
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = ContentType;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "public, immutable, max-age=31536000";
            await context.Response.OutputStream.WriteAsync(bytes, _stopping.Token).ConfigureAwait(false);
            Interlocked.Increment(ref _served);
            Interlocked.Add(ref _bytesServed, bytes.Length);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException)
        {
            Interlocked.Increment(ref _rejected);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static ulong ToUnsigned(long value) => value <= 0 ? 0UL : checked((ulong)value);
}
