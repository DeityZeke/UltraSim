using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace UltraSim.Framework.Http.Endpoint;

/// <summary>
/// Fixed inbound route composition, per-service admission, and response ownership. Handlers must
/// honor cancellation; an uncooperative handler makes retirement fail, never falsely succeed.
/// No authority, content, outbound HTTP, or external server types belong to this runtime.
/// </summary>
public sealed class HttpEndpointHost : IDisposable
{
    private readonly IHttpEndpointBackend _backend;
    private readonly Service[] _services;
    private readonly Dictionary<(string Method, string Path), (Service Service, HttpRouteDefinition Route)> _routes = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _disposeGate = new();
    private ExceptionDispatchInfo? _failure;
    private int _state;
    private long _requests, _rejected;
    private bool _cancellationDisposed;
    public Uri BaseUri { get; }

    public HttpEndpointHost(IPEndPoint endpoint, IEnumerable<HttpServiceDefinition> services, IHttpEndpointBackend backend)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backend);
        if (endpoint.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || endpoint.Port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(endpoint));
        HttpServiceDefinition[] definitions = services.Take(33).ToArray();
        if (definitions.Length is 0 or > 32 || definitions.Any(static s => s is null) ||
            definitions.Select(static s => s.ServiceId).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new ArgumentException("Invalid bounded service set.", nameof(services));
        // Validate all ownership before allocating workers or binding a socket.
        var identities = new HashSet<(string, string)>();
        foreach (HttpServiceDefinition definition in definitions)
        foreach (HttpRouteDefinition route in definition.Routes)
            if (!identities.Add((route.Method, route.Path)))
                throw new ArgumentException("Duplicate HTTP method/path ownership.", nameof(services));
        _backend = backend;
        _services = definitions.Select(s => new Service(s, _stopping.Token)).ToArray();
        foreach (Service service in _services)
        foreach (HttpRouteDefinition route in service.Definition.Routes)
            _routes.Add((route.Method, route.Path), (service, route));
        BaseUri = new Uri($"http://{endpoint.Address}:{endpoint.Port}/");
        try { backend.Start(new IPEndPoint(endpoint.Address, endpoint.Port), DispatchAsync); }
        catch
        {
            _stopping.Cancel();
            foreach (Service service in _services) service.CloseAdmission();
            Task.WhenAll(_services.SelectMany(static s => s.Workers)).GetAwaiter().GetResult();
            backend.Dispose();
            _stopping.Dispose();
            throw;
        }
    }

    public HttpEndpointSnapshot GetSnapshot() => new(
        (HttpEndpointState)Volatile.Read(ref _state), Interlocked.Read(ref _requests),
        Interlocked.Read(ref _rejected), _cancellationDisposed, _backend.GetSnapshot());

    public HttpServiceSnapshot GetServiceSnapshot(string serviceId) =>
        (_services.FirstOrDefault(s => s.Definition.ServiceId == serviceId) ??
            throw new ArgumentException("Unknown service.", nameof(serviceId))).Snapshot();

    private async Task DispatchAsync(IHttpEndpointExchange exchange)
    {
        Interlocked.Increment(ref _requests);
        if (Volatile.Read(ref _state) != 0) { exchange.Abort(); return; }
        if (!exchange.Request.HasQuery && _routes.TryGetValue(
                (exchange.Request.Method, exchange.Request.Path), out var owned))
        {
            var work = new Work(exchange, owned.Route);
            if (owned.Service.TryAdmit(work))
            {
                // The native request remains owned until the real worker write/abort completes.
                await work.Retired.Task.ConfigureAwait(false);
                return;
            }
            Interlocked.Increment(ref _rejected);
            await RejectAsync(exchange, 503).ConfigureAwait(false);
        }
        else
        {
            Interlocked.Increment(ref _rejected);
            await RejectAsync(exchange, 404).ConfigureAwait(false);
        }
    }

    private async Task RejectAsync(IHttpEndpointExchange exchange, int status)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, exchange.Aborted);
        deadline.CancelAfter(1000);
        using CancellationTokenRegistration abort = deadline.Token.Register(exchange.Abort);
        try { await exchange.WriteResponseAsync(new(status, default), deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { exchange.Abort(); }
        catch (IOException) { exchange.Abort(); } // Native client termination; no service computation here.
    }

    public void Dispose()
    {
        lock (_disposeGate)
        {
            while (_state == 1) Monitor.Wait(_disposeGate);
            if (_state != 0) { _failure?.Throw(); return; }
            Volatile.Write(ref _state, 1);
        }
        List<Exception> failures = [];
        bool retired = false;
        try
        {
            foreach (Service service in _services) service.CloseAdmission();
            Capture(_stopping.Cancel, failures);
            // Stop native acceptance/connections through the supported backend lifecycle, not
            // an abandoned cancellation wrapper. Cancelled service writes are still awaited.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Capture(() => _backend.StopAsync(deadline.Token).GetAwaiter().GetResult(), failures);
            Task workers = Task.WhenAll(_services.SelectMany(static s => s.Workers));
            Capture(() => workers.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), failures);
            if (workers.Exception is { } workerFailures)
                foreach (Exception exception in workerFailures.Flatten().InnerExceptions)
                    if (!failures.Contains(exception)) failures.Add(exception);
            retired = workers.IsCompleted && _backend.GetSnapshot() is { Stopped: true, ActiveRequests: 0 };
            if (retired)
            {
                Capture(_backend.Dispose, failures);
                Capture(_stopping.Dispose, failures);
                _cancellationDisposed = true;
            }
            else failures.Add(new InvalidOperationException("HTTP endpoint ownership did not retire; resources remain owned."));
            foreach (Service service in _services) failures.AddRange(service.Failures());
        }
        catch (Exception exception) { failures.Add(exception); }
        finally
        {
            _failure = failures.Count switch
            {
                0 => null,
                1 => ExceptionDispatchInfo.Capture(failures[0]),
                _ => ExceptionDispatchInfo.Capture(new AggregateException("HTTP endpoint retirement failures.", failures)),
            };
            lock (_disposeGate)
            {
                Volatile.Write(ref _state, retired ? 2 : 3);
                Monitor.PulseAll(_disposeGate);
            }
        }
        _failure?.Throw();
    }

    private static void Capture(Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(exception); }
    }

    private sealed record Work(IHttpEndpointExchange Exchange, HttpRouteDefinition Route)
    {
        internal TaskCompletionSource Retired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Service
    {
        internal HttpServiceDefinition Definition { get; }
        internal Task[] Workers { get; }
        private readonly Channel<Work> _pending;
        private readonly CancellationToken _stopping;
        private readonly object _gate = new();
        private readonly Exception?[] _failures;
        private bool _closed, _faulted;
        private int _active, _peak;
        private long _admitted, _completed, _refused, _aborted, _faults, _bytes;

        internal Service(HttpServiceDefinition definition, CancellationToken stopping)
        {
            Definition = definition;
            _stopping = stopping;
            _pending = Channel.CreateBounded<Work>(new BoundedChannelOptions(definition.Limits.MaximumQueued)
            { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
            _failures = new Exception?[definition.Limits.MaximumActive];
            Workers = Enumerable.Range(0, _failures.Length).Select(i => Task.Run(() => RunAsync(i))).ToArray();
        }
        internal bool TryAdmit(Work work)
        {
            lock (_gate)
            {
                if (_closed || !_pending.Writer.TryWrite(work)) { Interlocked.Increment(ref _refused); return false; }
                Interlocked.Increment(ref _admitted);
                return true;
            }
        }
        internal void CloseAdmission()
        {
            lock (_gate) { _closed = true; _pending.Writer.TryComplete(); }
        }
        internal HttpServiceSnapshot Snapshot() => new(Definition.ServiceId,
            Definition.Limits.MaximumActive, Definition.Limits.MaximumQueued,
            Volatile.Read(ref _active), _pending.Reader.Count, Volatile.Read(ref _peak),
            Interlocked.Read(ref _admitted), Interlocked.Read(ref _completed), Interlocked.Read(ref _refused),
            Interlocked.Read(ref _aborted), Interlocked.Read(ref _faults), Interlocked.Read(ref _bytes),
            Workers.All(static w => w.IsCompleted));
        internal IEnumerable<Exception> Failures() => _failures.OfType<Exception>();

        private async Task RunAsync(int worker)
        {
            await foreach (Work work in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                lock (_gate) { _active++; _peak = Math.Max(_peak, _active); }
                try
                {
                    if (_stopping.IsCancellationRequested || Volatile.Read(ref _faulted))
                    { work.Exchange.Abort(); Interlocked.Increment(ref _aborted); continue; }
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stopping, work.Exchange.Aborted);
                    deadline.CancelAfter(Definition.Limits.TimeoutMilliseconds);
                    using CancellationTokenRegistration abort = deadline.Token.Register(work.Exchange.Abort);
                    try
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        HttpEndpointResponse response = await work.Route.Handler(work.Exchange.Request, deadline.Token).ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested();
                        // Keep the slot until the real write AND native completion have returned.
                        try { await work.Exchange.WriteResponseAsync(response, deadline.Token).ConfigureAwait(false); }
                        catch (IOException)
                        {
                            // Broken native writes are expected request failures; service/handler
                            // IOException is not swallowed by this boundary.
                            work.Exchange.Abort();
                            Interlocked.Increment(ref _aborted);
                            continue;
                        }
                        deadline.Token.ThrowIfCancellationRequested();
                        Interlocked.Increment(ref _completed);
                        Interlocked.Add(ref _bytes, response.Body.Length);
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                    { work.Exchange.Abort(); Interlocked.Increment(ref _aborted); }
                    catch (Exception exception)
                    {
                        // Stop admission after an unexpected request fault. Finite workers retain
                        // every independent failure; further queued work retires without handlers.
                        _failures[worker] = exception;
                        Volatile.Write(ref _faulted, true);
                        CloseAdmission();
                        Interlocked.Increment(ref _faults);
                        work.Exchange.Abort();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                    work.Retired.TrySetResult();
                }
            }
        }
    }
}
