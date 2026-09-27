using System.Net;
using System.Net.Sockets;
using UltraSim.Bridges.Http.Kestrel;
using UltraSim.Framework.Http.Endpoint;
using UltraSim.Server.Hosting;

internal static class R3HttpEndpointTests
{
    internal static void Run()
    {
        Check("FrameworkContractsAndDuplicateOwnership", Contracts);
        Check("TwoServicesIsolateSaturatedContent", Saturation);
        Check("GatedHandlerShutdownRetainsRealOwnership", GatedShutdown);
        Check("AllUnexpectedServiceFaultsSurviveConcurrentRetirement", Faults);
        Check("UnexpectedBackendFailureIsRetained", BackendFault);
        Check("StartupAndCleanupFailuresAreBothRetained", StartupFaults);
        Check("RealBridgeRetainsIndependentDispatchFaults", DispatchFaults);
        Check("KeepAliveDisconnectAndConnectionClose", Connections);
        Check("SamePortReuse16Cycles", Reuse);
        Check("KestrelShutdownStress1024Cycles", Stress);
    }
    private static void Check(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS F112-R3 {name}");
    }
    private static HttpServiceDefinition Tiny(string id = "status", string path = "/_test/status") =>
        new(id, new(1, 1, 1000), [new("GET", path, (_, _) =>
            ValueTask.FromResult(new HttpEndpointResponse(200, "status/proof projection"u8.ToArray(), "text/plain")))]);
    private static HttpEndpointHost Host(IEnumerable<HttpServiceDefinition> services, int port = 0) =>
        new(new IPEndPoint(IPAddress.Loopback, port == 0 ? Port() : port), services, new KestrelHttpEndpointBackend());
    private static void Contracts()
    {
        Assert.True(!typeof(HttpEndpointHost).Assembly.GetReferencedAssemblies().Any(static a =>
            a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)));
        foreach (string path in new[] { "/../bad", "/UPPER", "/bad%20path", "/bad?query", "/bad/", "/bad\\path" })
            Assert.False(HttpEndpointSyntax.IsPath(path));
        Assert.Throws<ArgumentException>(() => new HttpServiceDefinition("bad/id", new(1, 1, 1000), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpServiceLimits(0, 1, 1));
        var backend = new ControlledBackend();
        Assert.Throws<ArgumentException>(() => new HttpEndpointHost(new(IPAddress.Loopback, Port()),
            [Tiny("one"), Tiny("two")], backend));
        Assert.False(backend.Started); // duplicate ownership rejected before binding/workers.
        Assert.Throws<ArgumentException>(() => new HttpEndpointHost(new(IPAddress.Loopback, Port()),
            [Tiny(), Tiny()], backend));
    }
    private static void Saturation()
    {
        byte[] large = new byte[32 * 1024 * 1024];
        var content = new StaticContentHttpService(
            [new("/content/large.uspkg", large), new("/content/small.uspkg", "ok"u8.ToArray())],
            new(2, large.Length + 2L, 2, 2, 10_000));
        using var host = Host([content.Definition, Tiny()]);
        using var first = Stalled(host.BaseUri.Port, "/content/large.uspkg");
        using var second = Stalled(host.BaseUri.Port, "/content/large.uspkg");
        Until(() => host.GetServiceSnapshot(StaticContentHttpService.ServiceId).Active == 2);
        using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 8 });
        Task<byte[]> queued1 = client.GetByteArrayAsync(new Uri(host.BaseUri, "content/small.uspkg"));
        Task<byte[]> queued2 = client.GetByteArrayAsync(new Uri(host.BaseUri, "content/small.uspkg"));
        Until(() => host.GetServiceSnapshot(StaticContentHttpService.ServiceId).Queued == 2);
        using HttpResponseMessage overflow = client.GetAsync(new Uri(host.BaseUri, "content/small.uspkg"))
            .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, overflow.StatusCode);
        byte[] status = client.GetByteArrayAsync(new Uri(host.BaseUri, "_test/status"))
            .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert.True(status.AsSpan().SequenceEqual("status/proof projection"u8));
        Assert.Equal(2, host.GetServiceSnapshot(StaticContentHttpService.ServiceId).Active);
        Assert.Equal(2, host.GetServiceSnapshot(StaticContentHttpService.ServiceId).Queued);
        Console.WriteLine("EVIDENCE R3 content=2active/2queued overflow=503 status=200 sameEndpoint=true");
        host.Dispose();
        Retired(host, [StaticContentHttpService.ServiceId, "status"]);
        Observe(queued1); Observe(queued2);
    }
    private static void GatedShutdown()
    {
        foreach (int workers in new[] { 1, 3 })
        foreach (bool concurrent in new[] { false, true })
        {
            using var entered = new ManualResetEventSlim();
            var service = new HttpServiceDefinition("gated", new(workers, workers == 1 ? 1 : 4, 10_000),
                [new("GET", "/gated", async (_, cancellation) =>
                {
                    entered.Set();
                    await Task.Delay(Timeout.Infinite, cancellation).ConfigureAwait(false);
                    throw new InvalidOperationException("Unreachable.");
                })]);
            using var host = Host([service]);
            using var client = new HttpClient();
            Task<byte[]> response = client.GetByteArrayAsync(new Uri(host.BaseUri, "gated"));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Task a = Task.Run(host.Dispose);
            Task b = concurrent ? Task.Run(host.Dispose) : Task.CompletedTask;
            Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Observe(response);
            Retired(host, ["gated"]);
            Assert.Equal(1L, host.GetServiceSnapshot("gated").Aborted);
            host.Dispose();
        }
    }
    private static void Faults()
    {
        foreach (bool duringShutdown in new[] { false, true })
        {
            using var entered = new CountdownEvent(2);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int next = 0;
            var service = new HttpServiceDefinition("fault", new(2, 2, 10_000),
                [new("GET", "/fault", async (_, _) =>
                {
                    int identity = Interlocked.Increment(ref next);
                    entered.Signal();
                    await release.Task.ConfigureAwait(false);
                    throw new InjectedFailure(identity);
                })]);
            var host = Host([service]);
            using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 2 });
            Task<byte[]> r1 = client.GetByteArrayAsync(new Uri(host.BaseUri, "fault"));
            Task<byte[]> r2 = client.GetByteArrayAsync(new Uri(host.BaseUri, "fault"));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            if (!duringShutdown)
            {
                release.SetResult();
                Until(() => host.GetServiceSnapshot("fault").Faults == 2);
            }
            Task<Exception?> d1 = Task.Run(() => Failure(host.Dispose));
            Task<Exception?> d2 = Task.Run(() => Failure(host.Dispose));
            if (duringShutdown)
            {
                Until(() => host.GetSnapshot().State == HttpEndpointState.Stopping);
                release.SetResult();
            }
            Task.WhenAll(d1, d2).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Assert.True(d1.Result is AggregateException);
            int[] identities = ((AggregateException)d1.Result!).Flatten().InnerExceptions
                .OfType<InjectedFailure>().Select(static e => e.Identity).Order().ToArray();
            Assert.True(identities.AsSpan().SequenceEqual([1, 2]));
            Assert.True(ReferenceEquals(d1.Result, d2.Result));
            Assert.True(ReferenceEquals(d1.Result, Failure(host.Dispose)));
            Retired(host, ["fault"]);
            Observe(r1); Observe(r2);
        }
    }
    private static void BackendFault()
    {
        var backend = new ControlledBackend { StopFailure = new InvalidOperationException("unexpected backend failure") };
        var host = new HttpEndpointHost(new(IPAddress.Loopback, Port()), [Tiny()], backend);
        Exception? failure = Failure(host.Dispose);
        Assert.True(failure is InvalidOperationException);
        Assert.True(ReferenceEquals(failure, Failure(host.Dispose)));
        Assert.True(backend.Disposed);
        Retired(host, ["status"]);
    }
    private static void StartupFaults()
    {
        var startup = new InjectedFailure(1);
        var cleanup = new InjectedFailure(2);
        var backend = new ControlledBackend { StartFailure = startup, DisposeFailure = cleanup };
        Exception? failure = Failure(() => new HttpEndpointHost(new(IPAddress.Loopback, Port()), [Tiny()], backend));
        Assert.True(failure is AggregateException);
        Assert.True(((AggregateException)failure!).Flatten().InnerExceptions.SequenceEqual([startup, cleanup]));
        Assert.True(backend.Disposed && backend.GetSnapshot().Stopped);

        // Native bind failure must release its partially built host without disturbing the owner.
        int port = Port();
        using (var owner = Host([Tiny()], port))
        {
            Assert.True(Failure(() => Host([Tiny()], port)) is IOException);
            using var client = new HttpClient();
            Assert.True(client.GetByteArrayAsync(new Uri(owner.BaseUri, "_test/status"))
                .GetAwaiter().GetResult().AsSpan().SequenceEqual("status/proof projection"u8));
        }
        using var rebound = Host([Tiny()], port);
        rebound.Dispose();
        Retired(rebound, ["status"]);
    }
    private static void Connections()
    {
        foreach (bool keepAlive in new[] { false, true })
        {
            using var host = Host([Tiny()]);
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host.BaseUri, "_test/status"));
            request.Headers.ConnectionClose = !keepAlive;
            using HttpResponseMessage response = client.Send(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            host.Dispose();
            Retired(host, ["status"]);
        }
        using (var host = Host([Tiny()]))
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, host.BaseUri.Port);
            client.GetStream().Write("GET /_test/status HTTP/1.1\r\nHost: localhost\r\n"u8);
            client.Dispose();
            host.Dispose();
            Retired(host, ["status"]);
        }
    }
    private static void DispatchFaults()
    {
        using var both = new CountdownEvent(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new KestrelHttpEndpointBackend();
        int port = Port(), next = 0;
        backend.Start(new(IPAddress.Loopback, port), async _ =>
        {
            int identity = Interlocked.Increment(ref next);
            both.Signal();
            await release.Task.ConfigureAwait(false);
            throw new InjectedFailure(identity);
        });
        using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 2 });
        Task<byte[]> one = client.GetByteArrayAsync($"http://127.0.0.1:{port}/one");
        Task<byte[]> two = client.GetByteArrayAsync($"http://127.0.0.1:{port}/two");
        Assert.True(both.Wait(TimeSpan.FromSeconds(2)));
        release.SetResult();
        Observe(one); Observe(two);
        Assert.Equal(2, backend.GetSnapshot().UnexpectedFaults);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        backend.StopAsync(timeout.Token).GetAwaiter().GetResult();
        Exception? failure = Failure(backend.Dispose);
        Assert.True(failure is AggregateException);
        Assert.True(((AggregateException)failure!).Flatten().InnerExceptions.OfType<InjectedFailure>()
            .Select(static e => e.Identity).Order().SequenceEqual([1, 2]));
        Assert.Equal(0, backend.GetSnapshot().ActiveRequests);
        Assert.True(backend.GetSnapshot().Stopped);
    }
    private static void Reuse()
    {
        int port = Port();
        for (int cycle = 0; cycle < 16; cycle++)
        {
            using var host = Host([new HttpServiceDefinition("status", new(cycle % 2 == 0 ? 1 : 3,
                cycle % 2 == 0 ? 1 : 4, 1000), [new("GET", "/one", (_, _) => ValueTask.FromResult(new HttpEndpointResponse(200, "x"u8.ToArray())))])], port);
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            if ((cycle & 1) != 0) client.GetStream().Write("GET /one HTTP/1.1\r\nHost: localhost\r\n"u8);
            Task.WhenAll(Task.Run(host.Dispose), Task.Run(host.Dispose)).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Retired(host, ["status"]);
            // Complete the old partial request only AFTER supported retirement. It cannot
            // invoke a handler or publish a request into retired UltraSim state.
            HttpEndpointSnapshot frozen = host.GetSnapshot();
            try
            {
                client.GetStream().Write("\r\n"u8);
                byte[] probe = new byte[1];
                int read = client.GetStream().ReadAsync(probe).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                Assert.Equal(0, read);
            }
            catch (IOException) { }
            Assert.Equal(frozen, host.GetSnapshot());
            // Same exact port next iteration, without recovery sleeps or process restart.
        }
    }
    private static void Stress()
    {
        for (int cycle = 0; cycle < 1024; cycle++)
        {
            using var host = Host([Tiny()]);
            using var client = new TcpClient();
            if (cycle % 4 != 0)
            {
                client.Connect(IPAddress.Loopback, host.BaseUri.Port);
                if (cycle % 4 == 1) client.GetStream().Write("GET /_test/status HTTP/1.1\r\nHost: localhost\r\nConnection: keep-alive\r\n\r\n"u8);
                else if (cycle % 4 == 2) client.GetStream().Write("GET /_test/status HTTP/1.1\r\nHost: localhost\r\n"u8);
            }
            Task.WhenAll(Task.Run(host.Dispose), Task.Run(host.Dispose)).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Retired(host, ["status"]);
            host.Dispose();
        }
    }
    private static TcpClient Stalled(int port, string path)
    {
        var client = new TcpClient { ReceiveBufferSize = 1024, NoDelay = true };
        client.Connect(IPAddress.Loopback, port);
        client.GetStream().Write(System.Text.Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: keep-alive\r\n\r\n"));
        return client;
    }
    private static void Retired(HttpEndpointHost host, string[] services)
    {
        HttpEndpointSnapshot endpoint = host.GetSnapshot();
        Assert.Equal(HttpEndpointState.Disposed, endpoint.State);
        Assert.True(endpoint.CancellationDisposed);
        Assert.True(endpoint.Backend.Stopped);
        Assert.Equal(0, endpoint.Backend.ActiveRequests);
        foreach (string service in services)
        {
            HttpServiceSnapshot snapshot = host.GetServiceSnapshot(service);
            Assert.True(snapshot.WorkersRetired);
            Assert.Equal(0, snapshot.Active);
            Assert.Equal(0, snapshot.Queued);
            Assert.Equal(snapshot.Admitted, snapshot.Completed + snapshot.Aborted + snapshot.Faults);
        }
    }
    private static void Observe(Task response)
    {
        try { response.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) { }
    }
    private static Exception? Failure(Action action)
    { try { action(); return null; } catch (Exception exception) { return exception; } }
    private static void Until(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(2))) throw new TimeoutException("Controlled HTTP state not reached.");
    }
    private static int Port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private sealed class InjectedFailure(int identity) : Exception
    { internal int Identity { get; } = identity; }
    private sealed class ControlledBackend : IHttpEndpointBackend
    {
        internal bool Started, Disposed;
        internal Exception? StopFailure;
        internal Exception? StartFailure, DisposeFailure;
        private bool _stopped;
        public void Start(IPEndPoint endpoint, Func<IHttpEndpointExchange, Task> dispatch)
        { Started = true; if (StartFailure is not null) throw StartFailure; }
        public Task StopAsync(CancellationToken cancellation)
        { _stopped = true; return StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure); }
        public HttpEndpointBackendSnapshot GetSnapshot() => new(Started, _stopped, 0);
        public void Dispose() { Disposed = true; if (DisposeFailure is not null) throw DisposeFailure; }
    }
}
