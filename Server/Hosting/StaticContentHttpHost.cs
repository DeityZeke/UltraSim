using System.Net;
using System.Runtime.ExceptionServices;
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

internal sealed class StaticContentHttpHostTestHooks
{
    internal Action? BeforeAcceptWait { get; init; }
    internal Action<Task<HttpListenerContext>>? AcceptWaitStarted { get; init; }
    internal Action? AfterAccept { get; init; }
    internal Action? BeforeListenerStop { get; init; }
    internal Action? AfterActiveResponseAbort { get; init; }
    internal Action? BeforeResponseInitialization { get; init; }
    internal Action? BeforeResponseWrite { get; init; }
    internal Action? AfterResponseWrite { get; init; }
}

internal readonly record struct StaticContentHttpHostLifecycleSnapshot(
    int State,
    bool AcceptWorkerCompleted,
    bool ResponseWorkersCompleted,
    int PendingResponses,
    int ActiveResponses,
    bool ListenerClosed,
    bool CancellationDisposed,
    long AcceptOperationsStarted,
    long AcceptOperationsCompleted,
    long ResponseCloseOperations);

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
    private readonly object _disposeGate = new();
    private readonly object _responseOwnershipGate = new();
    private readonly object _acceptOwnershipGate = new();
    private readonly ManualResetEventSlim _acceptPublicationRetired = new(initialState: true);
    private bool _acceptAdmissionStopped;
    private readonly TaskCompletionSource _listenerTeardownCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StaticContentHttpHostTestHooks? _testHooks;
    private readonly HttpListenerContext?[] _activeContexts;
    // 0 unused, 1 initialization, 2 immutable headers/write, 3 closer claimed.
    private readonly int[] _responseStates;
    private long _acceptOperationsStarted;
    private long _acceptOperationsCompleted;
    private long _responseCloseOperations;
    private long _requests;
    private long _served;
    private long _rejected;
    private long _bytesServed;
    private int _activeResponses;
    private int _peakActiveResponses;
    private int _lifecycle;
    private bool _listenerClosed;
    private bool _cancellationDisposed;
    private ExceptionDispatchInfo? _disposeFailure;

    public StaticContentHttpHost(
        IPEndPoint endpoint,
        IEnumerable<StaticContentHttpResource> resources,
        StaticContentHttpHostLimits limits = default)
        : this(endpoint, resources, limits, null)
    {
    }

    internal StaticContentHttpHost(
        IPEndPoint endpoint,
        IEnumerable<StaticContentHttpResource> resources,
        StaticContentHttpHostLimits limits,
        StaticContentHttpHostTestHooks? testHooks)
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
        _testHooks = testHooks;
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
        _activeContexts = new HttpListenerContext?[limits.MaximumConcurrentResponses];
        _responseStates = new int[limits.MaximumConcurrentResponses];
        for (int index = 0; index < _responseWorkers.Length; index++)
        {
            int workerIndex = index;
            _responseWorkers[index] = Task.Run(() => RunResponseWorkerAsync(workerIndex));
        }
        _acceptWorker = Task.Run(RunAcceptAsync);
    }

    public Uri BaseUri { get; }

    public Uri GetUri(string route)
    {
        ThrowIfNotRunning();
        if (!IsValidRoute(route) || !_resources.ContainsKey(route))
            throw new ArgumentException("The route is not one hosted resource.", nameof(route));
        return new Uri(BaseUri, route[1..]);
    }

    public StaticContentHttpHostSnapshot GetSnapshot()
    {
        ThrowIfNotRunning();
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
        lock (_disposeGate)
        {
            while (_lifecycle == 1) Monitor.Wait(_disposeGate);
            if (_lifecycle == 2)
            {
                _disposeFailure?.Throw();
                return;
            }
            _lifecycle = 1;
        }

        List<Exception>? failures = null;
        try
        {
            lock (_acceptOwnershipGate) _acceptAdmissionStopped = true;
            TryCleanup(_stopping.Cancel, ref failures);
            _pending.Writer.TryComplete();
            AbortActiveResponses(ref failures);
            TryCleanup(() => _testHooks?.AfterActiveResponseAbort?.Invoke(), ref failures);
            TryJoinAll(_responseWorkers, ref failures);
            DrainPending(ref failures);
            // Only the short ownership transition is locked; rejection/close and joining are not.
            _acceptPublicationRetired.Wait();
            try
            {
                TryCleanup(() => _testHooks?.BeforeListenerStop?.Invoke(), ref failures);
                TryCleanup(_listener.Stop, ref failures, IsExpectedListenerShutdownException);
                TryCleanup(_listener.Close, ref failures, IsExpectedListenerShutdownException);
                _listenerClosed = true;
            }
            finally { _listenerTeardownCompleted.TrySetResult(); }
            TryJoin(_acceptWorker, ref failures);
            DrainPending(ref failures);
            TryCleanup(_acceptPublicationRetired.Dispose, ref failures);
            TryCleanup(_stopping.Dispose, ref failures);
            _cancellationDisposed = true;
        }
        finally
        {
            ExceptionDispatchInfo? failure = failures switch
            {
                null or { Count: 0 } => null,
                { Count: 1 } => ExceptionDispatchInfo.Capture(failures[0]),
                _ => ExceptionDispatchInfo.Capture(new AggregateException(
                    "Static content HTTP shutdown encountered multiple unexpected failures.", failures)),
            };
            lock (_disposeGate)
            {
                _disposeFailure = failure;
                _lifecycle = 2;
                Monitor.PulseAll(_disposeGate);
            }
        }
        _disposeFailure?.Throw();
    }

    internal StaticContentHttpHostLifecycleSnapshot GetLifecycleSnapshotForTesting() => new(
        Volatile.Read(ref _lifecycle),
        _acceptWorker.IsCompleted,
        _responseWorkers.All(static worker => worker.IsCompleted),
        _pending.Reader.CanCount ? _pending.Reader.Count : -1,
        Volatile.Read(ref _activeResponses),
        _listenerClosed,
        _cancellationDisposed,
        Interlocked.Read(ref _acceptOperationsStarted),
        Interlocked.Read(ref _acceptOperationsCompleted),
        Interlocked.Read(ref _responseCloseOperations));

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
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                _testHooks?.BeforeAcceptWait?.Invoke();
                HttpListenerContext context;
                try
                {
                    // WaitAsync(cancellation) retires only its wrapper, not the listener accept.
                    // This worker owns/observes the actual operation through listener teardown.
                    Task<HttpListenerContext> accept = _listener.GetContextAsync();
                    Interlocked.Increment(ref _acceptOperationsStarted);
                    try
                    {
                        _testHooks?.AcceptWaitStarted?.Invoke(accept);
                        context = await accept.ConfigureAwait(false);
                    }
                    finally { Interlocked.Increment(ref _acceptOperationsCompleted); }
                }
                catch (OperationCanceledException) when (IsStopping) { break; }
                catch (HttpListenerException) when (IsStopping) { break; }
                catch (ObjectDisposedException) when (IsStopping) { break; }
                catch (InvalidOperationException) when (IsStopping) { break; }
                _testHooks?.AfterAccept?.Invoke();
                Interlocked.Increment(ref _requests);
                bool stopping;
                lock (_acceptOwnershipGate)
                {
                    stopping = _acceptAdmissionStopped;
                    if (!stopping) _acceptPublicationRetired.Reset();
                }
                if (stopping)
                {
                    // Do not mutate framework response state concurrently with listener teardown.
                    await _listenerTeardownCompleted.Task.ConfigureAwait(false);
                    Interlocked.Increment(ref _rejected);
                    AbortResponse(context);
                }
                else
                {
                    try { if (!_pending.Writer.TryWrite(context)) RejectOverCapacity(context); }
                    finally { _acceptPublicationRetired.Set(); }
                }
            }
        }
        finally
        {
            _pending.Writer.TryComplete();
        }
    }

    private async Task RunResponseWorkerAsync(int workerIndex)
    {
        try
        {
            while (await _pending.Reader.WaitToReadAsync(_stopping.Token).ConfigureAwait(false))
                while (_pending.Reader.TryRead(out HttpListenerContext? context))
                {
                    bool stopping;
                    lock (_responseOwnershipGate)
                    {
                        stopping = IsStopping;
                        if (!stopping)
                        {
                            _activeContexts[workerIndex] = context;
                            Volatile.Write(ref _responseStates[workerIndex], 1);
                        }
                    }
                    if (stopping) { RejectOverCapacity(context); continue; }
                    try
                    {
                        await HandleBoundedAsync(context, workerIndex).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (_responseOwnershipGate)
                        {
                            _activeContexts[workerIndex] = null;
                            Volatile.Write(ref _responseStates[workerIndex], 0);
                        }
                    }
                }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task HandleBoundedAsync(HttpListenerContext context, int workerIndex)
    {
        int active = Interlocked.Increment(ref _activeResponses);
        PublishPeak(active);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        Task response = HandleAsync(context, timeout.Token, workerIndex);
        try
        {
            await response.WaitAsync(
                TimeSpan.FromMilliseconds(_responseTimeoutMilliseconds),
                _stopping.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timeout.Cancel();
            CloseOwnedResponse(context, workerIndex, abort: true);
            await response.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            timeout.Cancel();
            CloseOwnedResponse(context, workerIndex, abort: true);
            await response.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeResponses);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellation, int workerIndex)
    {
        try
        {
            _testHooks?.BeforeResponseInitialization?.Invoke();
            cancellation.ThrowIfCancellationRequested();
            string route = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (context.Request.HttpMethod != "GET" || context.Request.Url?.Query.Length > 0 ||
                !_resources.TryGetValue(route, out byte[]? bytes))
            {
                Interlocked.Increment(ref _rejected);
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.ContentLength64 = 0;
                }
                catch (InvalidOperationException) when (IsStopping)
                {
                }
                return;
            }
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = ContentType;
                context.Response.ContentLength64 = bytes.Length;
                context.Response.Headers["Cache-Control"] = "public, immutable, max-age=31536000";
            }
            catch (InvalidOperationException) when (IsStopping)
            {
                Interlocked.Increment(ref _rejected);
                return;
            }
            Volatile.Write(ref _responseStates[workerIndex], 2);
            _testHooks?.BeforeResponseWrite?.Invoke();
            cancellation.ThrowIfCancellationRequested();
            await context.Response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            _testHooks?.AfterResponseWrite?.Invoke();
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
            CloseOwnedResponse(context, workerIndex, abort: IsStopping || cancellation.IsCancellationRequested);
        }
    }

    private void CloseOwnedResponse(HttpListenerContext context, int workerIndex, bool abort)
    {
        int state = Volatile.Read(ref _responseStates[workerIndex]);
        while (state is 1 or 2)
        {
            int prior = Interlocked.CompareExchange(ref _responseStates[workerIndex], 3, state);
            if (prior != state) { state = prior; continue; }
            Interlocked.Increment(ref _responseCloseOperations);
            try
            {
                if (abort) context.Response.Abort();
                else context.Response.Close();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException ||
                exception is InvalidOperationException && (abort || IsStopping)) { }
            return;
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
            ObjectDisposedException || exception is InvalidOperationException && IsStopping)
        {
        }
        finally
        {
            try { context.Response.Close(); }
            catch (Exception exception) when (exception is HttpListenerException or
                ObjectDisposedException || exception is InvalidOperationException && IsStopping) { }
        }
    }

    private bool IsStopping => Volatile.Read(ref _lifecycle) != 0;

    private void ThrowIfNotRunning()
    {
        if (Volatile.Read(ref _lifecycle) != 0)
            throw new ObjectDisposedException(nameof(StaticContentHttpHost));
    }

    private void DrainPending(ref List<Exception>? failures)
    {
        while (_pending.Reader.TryRead(out HttpListenerContext? context))
            TryCleanup(() => RejectOverCapacity(context), ref failures);
    }

    private void AbortActiveResponses(ref List<Exception>? failures)
    {
        for (int index = 0; index < _activeContexts.Length; index++)
        {
            HttpListenerContext? context;
            lock (_responseOwnershipGate)
            {
                context = _activeContexts[index];
                // Header initialization has no I/O. Its owner observes cancellation and closes
                // after initialization exits; Abort must not mutate that response concurrently.
                if (context is null || Volatile.Read(ref _responseStates[index]) != 2) continue;
                if (Interlocked.CompareExchange(ref _responseStates[index], 3, 2) != 2) continue;
            }
            Interlocked.Increment(ref _responseCloseOperations);
            TryCleanup(() => context.Response.Abort(), ref failures, IsExpectedResponseShutdownException);
        }
    }

    private static void TryJoin(Task worker, ref List<Exception>? failures)
    {
        try { worker.GetAwaiter().GetResult(); }
        catch (Exception observed)
        {
            if (worker.Exception is AggregateException aggregate)
            {
                foreach (Exception exception in aggregate.Flatten().InnerExceptions)
                    AddFailure(ref failures, exception);
            }
            else
            {
                AddFailure(ref failures, observed);
            }
        }
    }

    private static void TryJoinAll(Task[] workers, ref List<Exception>? failures)
    {
        foreach (Task worker in workers) TryJoin(worker, ref failures);
    }

    private static void TryCleanup(
        Action cleanup,
        ref List<Exception>? failures,
        Func<Exception, bool>? expected = null)
    {
        try { cleanup(); }
        catch (Exception exception) when (expected?.Invoke(exception) == true) { }
        catch (Exception exception) { AddFailure(ref failures, exception); }
    }

    private static bool IsExpectedListenerShutdownException(Exception exception) =>
        exception is HttpListenerException or ObjectDisposedException;

    private static bool IsExpectedResponseShutdownException(Exception exception) =>
        exception is HttpListenerException or InvalidOperationException or ObjectDisposedException;

    private static void AddFailure(ref List<Exception>? failures, Exception exception) =>
        (failures ??= []).Add(exception);

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
