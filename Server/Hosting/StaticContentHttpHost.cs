using System.Net;
using System.Threading.Channels;

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
    public bool IsValid => StaticContentHttpHost.IsValidRoute(Route) && !Bytes.IsEmpty;
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
    private readonly Channel<HttpListenerContext> _pending;
    private readonly Task _acceptWorker;
    private readonly Task[] _responseWorkers;
    private readonly int _maximumQueuedResponses;
    private readonly int _responseTimeoutMilliseconds;
    private readonly long _reservedResourceBytes;
    private long _requests;
    private long _served;
    private long _rejected;
    private long _bytesServed;
    private int _activeResponses;
    private int _peakActiveResponses;
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
        _maximumQueuedResponses = limits.MaximumQueuedResponses;
        _responseTimeoutMilliseconds = limits.ResponseTimeoutMilliseconds;
        _pending = Channel.CreateBounded<HttpListenerContext>(new BoundedChannelOptions(
            limits.MaximumQueuedResponses)
        {
            SingleReader = limits.MaximumConcurrentResponses == 1,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        string prefix = $"http://{endpoint.Address}:{endpoint.Port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        BaseUri = new Uri(prefix, UriKind.Absolute);
        _responseWorkers = new Task[limits.MaximumConcurrentResponses];
        for (int index = 0; index < _responseWorkers.Length; index++)
            _responseWorkers[index] = Task.Run(RunResponseWorkerAsync);
        _acceptWorker = Task.Run(RunAcceptAsync);
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
            Interlocked.Read(ref _bytesServed),
            Volatile.Read(ref _activeResponses),
            Volatile.Read(ref _peakActiveResponses),
            _responseWorkers.Length,
            _maximumQueuedResponses);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping.Cancel();
        _listener.Stop();
        _listener.Close();
        _pending.Writer.TryComplete();
        try { _acceptWorker.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        try { Task.WhenAll(_responseWorkers).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        while (_pending.Reader.TryRead(out HttpListenerContext? context))
        {
            try { context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable; }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { }
            try { context.Response.Close(); }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { }
        }
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

    private async Task RunAcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(_stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (_stopping.IsCancellationRequested) { break; }
            Interlocked.Increment(ref _requests);
            if (!_pending.Writer.TryWrite(context))
                RejectOverCapacity(context);
        }
        _pending.Writer.TryComplete();
    }

    private async Task RunResponseWorkerAsync()
    {
        try
        {
            while (await _pending.Reader.WaitToReadAsync(_stopping.Token).ConfigureAwait(false))
                while (_pending.Reader.TryRead(out HttpListenerContext? context))
                    await HandleBoundedAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task HandleBoundedAsync(HttpListenerContext context)
    {
        int active = Interlocked.Increment(ref _activeResponses);
        PublishPeak(active);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        Task response = HandleAsync(context, timeout.Token);
        try
        {
            await response.WaitAsync(
                TimeSpan.FromMilliseconds(_responseTimeoutMilliseconds),
                _stopping.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timeout.Cancel();
            AbortResponse(context);
            await response.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            timeout.Cancel();
            AbortResponse(context);
            await response.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeResponses);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellation)
    {
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
            await context.Response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _served);
            Interlocked.Add(ref _bytesServed, bytes.Length);
        }
        catch (OperationCanceledException)
        {
            if (!_stopping.IsCancellationRequested)
                Interlocked.Increment(ref _rejected);
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException or
            ObjectDisposedException)
        {
            Interlocked.Increment(ref _rejected);
        }
        finally
        {
            try { context.Response.Close(); }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { }
        }
    }

    private static void AbortResponse(HttpListenerContext context)
    {
        try { context.Response.Abort(); }
        catch (Exception exception) when (exception is HttpListenerException or
            InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private void RejectOverCapacity(HttpListenerContext context)
    {
        Interlocked.Increment(ref _rejected);
        try
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            context.Response.ContentLength64 = 0;
        }
        catch (Exception exception) when (exception is IOException or HttpListenerException or
            ObjectDisposedException)
        {
        }
        finally
        {
            try { context.Response.Close(); }
            catch (ObjectDisposedException) { }
        }
    }

    private void PublishPeak(int active)
    {
        int observed = Volatile.Read(ref _peakActiveResponses);
        while (active > observed)
        {
            int prior = Interlocked.CompareExchange(
                ref _peakActiveResponses, active, observed);
            if (prior == observed) return;
            observed = prior;
        }
    }

    private static ulong ToUnsigned(long value) => value <= 0 ? 0UL : checked((ulong)value);
}
