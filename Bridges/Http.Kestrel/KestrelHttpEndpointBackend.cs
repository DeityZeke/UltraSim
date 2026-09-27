using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using UltraSim.Framework.Http.Endpoint;

namespace UltraSim.Bridges.Http.Kestrel;

/// <summary>Supported HTTP/1.1 listener realization. No semantic routing or process ownership.</summary>
public sealed class KestrelHttpEndpointBackend : IHttpEndpointBackend
{
    private IWebHost? _host;
    private int _active;
    private bool _stopped;
    private readonly object _failureGate = new();
    private readonly Exception?[] _failures = new Exception?[1024];
    private int _failureCount, _failureOverflow;
    private bool _faulted;

    public void Start(IPEndPoint endpoint, Func<IHttpEndpointExchange, Task> dispatch)
    {
        if (_host is not null) throw new InvalidOperationException("Backend already started.");
        _host = new WebHostBuilder()
            .UseKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxConcurrentConnections = 1024;
                options.Limits.MaxRequestBodySize = 0;
                options.Limits.MaxRequestHeadersTotalSize = 8192;
                options.Limits.MaxRequestHeaderCount = 32;
                options.Limits.MaxRequestLineSize = 2048;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
                options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                options.Listen(endpoint.Address, endpoint.Port, listen => listen.Protocols = HttpProtocols.Http1);
            })
            .UseShutdownTimeout(TimeSpan.FromMilliseconds(100))
            .ConfigureLogging(logging => logging.ClearProviders())
            .Configure(app => app.Run(async context =>
            {
                Interlocked.Increment(ref _active);
                try
                {
                    if (Volatile.Read(ref _faulted)) { context.Abort(); return; }
                    await dispatch(new Exchange(context)).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // Retain all already-running failures (bounded by the connection limit).
                    // Future requests abort without entering retired/broken dispatch state.
                    lock (_failureGate)
                    {
                        if (_failureCount < _failures.Length) _failures[_failureCount++] = exception;
                        else _failureOverflow++;
                        Volatile.Write(ref _faulted, true);
                    }
                    context.Abort();
                }
                finally { Interlocked.Decrement(ref _active); }
            })).Build();
        try { _host.StartAsync().GetAwaiter().GetResult(); }
        catch (Exception startupFailure)
        {
            try { _host.Dispose(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Kestrel startup and cleanup failures.", startupFailure, cleanupFailure);
            }
            finally { _host = null; }
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellation)
    {
        if (_host is null) { _stopped = true; return; }
        if (_stopped) return;
        await _host.StopAsync(cancellation).ConfigureAwait(false);
        if (Volatile.Read(ref _active) != 0)
            throw new InvalidOperationException("Kestrel returned with active endpoint requests.");
        _stopped = true;
    }
    public HttpEndpointBackendSnapshot GetSnapshot() => new(_host is not null, _stopped,
        Volatile.Read(ref _active), Volatile.Read(ref _failureCount));
    public void Dispose()
    {
        if (_host is not null && !_stopped)
            throw new InvalidOperationException("Stop and observe backend requests before disposal.");
        var failures = _failures.Take(_failureCount).OfType<Exception>().ToList();
        try { _host?.Dispose(); }
        catch (Exception cleanupFailure) { failures.Add(cleanupFailure); }
        if (_failureOverflow != 0)
            failures.Add(new InvalidOperationException($"Unretained backend failures: {_failureOverflow}."));
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Unexpected HTTP dispatch/cleanup failures.", failures);
    }

    private sealed class Exchange(HttpContext context) : IHttpEndpointExchange
    {
        private int _aborted;
        // RawTarget retains escaping/dot segments: never turn a noncanonical route into an
        // authorized canonical resource through native URL normalization.
        public HttpEndpointRequest Request
        {
            get
            {
                string raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
                int query = raw.IndexOf('?');
                return new(context.Request.Method, query < 0 ? raw : raw[..query], query >= 0);
            }
        }
        public CancellationToken Aborted => context.RequestAborted;
        public void Abort()
        {
            if (Interlocked.Exchange(ref _aborted, 1) == 0) context.Abort();
        }
        public async ValueTask WriteResponseAsync(HttpEndpointResponse response, CancellationToken cancellation)
        {
            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentLength = response.Body.Length;
            if (response.ContentType is not null) context.Response.ContentType = response.ContentType;
            if (response.CacheControl is not null) context.Response.Headers.CacheControl = response.CacheControl;
            await context.Response.Body.WriteAsync(response.Body, cancellation).ConfigureAwait(false);
            await context.Response.CompleteAsync().ConfigureAwait(false);
        }
    }
}
