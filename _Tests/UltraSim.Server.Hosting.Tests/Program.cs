using UltraSim.ECS;
using UltraSim.Server.Hosting;
using UltraSim.Server.Runtime;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

var tests = new (string Name, Action Body)[]
{
    ("HostsUseTheSameConcreteRuntimeType", HostsUseTheSameConcreteRuntimeType),
    ("HostSurfaceDoesNotExposeGameFacet", HostSurfaceDoesNotExposeGameFacet),
    ("HostLifecyclePreservesRuntimeRejections", HostLifecyclePreservesRuntimeRejections),
    ("HostControlRemainsSingleOwner", HostControlRemainsSingleOwner),
    ("InternalAndHeadlessProduceIdenticalCanonicalState", InternalAndHeadlessProduceIdenticalCanonicalState),
    ("HeadlessRunHonorsCancellationWithoutOwningShutdown", HeadlessRunHonorsCancellationWithoutOwningShutdown),
    ("HeadlessShutdownDisposesGenerationCleanly", HeadlessShutdownDisposesGenerationCleanly),
    ("HostLoggingDoesNotAlterSimulationState", HostLoggingDoesNotAlterSimulationState),
    ("ThrowingLogSinkCannotChangeLifecycle", ThrowingLogSinkCannotChangeLifecycle),
    ("InternalHostHasNoImplicitLoggingCapability", InternalHostHasNoImplicitLoggingCapability),
    ("WarmedHostDrivingAllocatesZeroManagedBytes", WarmedHostDrivingAllocatesZeroManagedBytes),
    ("HostWrapperBenchmarkIsCaptured", HostWrapperBenchmarkIsCaptured),
    ("StaticContentHttpHostServesExactImmutableBytes", StaticContentHttpHostServesExactImmutableBytes),
    ("StaticContentHttpHostBoundsAndRejectsRequests", StaticContentHttpHostBoundsAndRejectsRequests),
    ("StalledContentResponseCannotBlockIndependentDownload", StalledContentResponseCannotBlockIndependentDownload),
    ("ExpiredContentResponseReleasesItsWorker", ExpiredContentResponseReleasesItsWorker),
    ("ContentResponseQueueRejectsOverflowAndShutdownTerminatesActiveWrite",
        ContentResponseQueueRejectsOverflowAndShutdownTerminatesActiveWrite),
    ("StaticContentHttpHostShutdownInterleavingsRetireEveryOwner",
        StaticContentHttpHostShutdownInterleavingsRetireEveryOwner),
    ("StaticContentHttpHostConcurrentDisposeIsIdempotent",
        StaticContentHttpHostConcurrentDisposeIsIdempotent),
    ("StaticContentHttpHostShutdownPreservesUnexpectedWorkerFault",
        StaticContentHttpHostShutdownPreservesUnexpectedWorkerFault),
    ("StaticContentHttpHostShutdownPreservesEveryResponseWorkerFault",
        StaticContentHttpHostShutdownPreservesEveryResponseWorkerFault),
    ("StaticContentHttpHostShutdownPreservesPreStoppingAcceptFault",
        StaticContentHttpHostShutdownPreservesPreStoppingAcceptFault),
    ("StaticContentHttpHostRetainsActualAcceptUntilTeardown", StaticContentHttpHostRetainsActualAcceptUntilTeardown),
    ("StaticContentHttpHostDoesNotAbortInitializingResponse", StaticContentHttpHostDoesNotAbortInitializingResponse),
    ("StaticContentHttpHostPartialRequestsAndPortReuse", StaticContentHttpHostPartialRequestsAndPortReuse),
    ("CancelledHttpListenerWrapperDoesNotRetireAccept", CancelledHttpListenerWrapperDoesNotRetireAccept),
    ("ManagedHttpListenerTeardownCanOverlapUnregisteredHeaderMutation",
        ManagedHttpListenerTeardownCanOverlapUnregisteredHeaderMutation),
    ("StaticContentHttpHostShutdownStressIsBounded",
        StaticContentHttpHostShutdownStressIsBounded),
};

if (args.Contains("--http-only", StringComparer.Ordinal))
    tests = tests.Where(static test => !test.Name.StartsWith("Host", StringComparison.Ordinal) &&
        !test.Name.StartsWith("Internal", StringComparison.Ordinal) &&
        !test.Name.StartsWith("Headless", StringComparison.Ordinal) &&
        !test.Name.StartsWith("Throwing", StringComparison.Ordinal) &&
        !test.Name.StartsWith("Warmed", StringComparison.Ordinal)).ToArray();
string? filter = args.FirstOrDefault(static argument => argument.StartsWith("--test=", StringComparison.Ordinal));
// This pinned-runtime investigation deliberately gates framework internals. Keep it opt-in:
// the normal product gate uses only public APIs and host-owned lifecycle observations.
if (filter is null)
    tests = tests.Where(static test => test.Name != "ManagedHttpListenerTeardownCanOverlapUnregisteredHeaderMutation").ToArray();
if (filter is not null)
{
    tests = tests.Where(test => test.Name == filter["--test=".Length..]).ToArray();
    if (tests.Length == 0) throw new ArgumentException("Unknown hosting test.");
}

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

static void HostControlRemainsSingleOwner()
{
    using var internalHost = new InternalHost(new FixtureComposition(), CreateRuntimeOptions());
    using var headlessHost = new HeadlessHost(
        new FixtureComposition(), CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));
    internalHost.Start();
    headlessHost.Start();

    Assert.Throws<InvalidOperationException>(() =>
        Task.Run(() => internalHost.Tick()).GetAwaiter().GetResult());
    Assert.Throws<InvalidOperationException>(() =>
        Task.Run(() => _ = internalHost.State).GetAwaiter().GetResult());
    Assert.Throws<InvalidOperationException>(() =>
        Task.Run(() => headlessHost.RunFixedSteps(1)).GetAwaiter().GetResult());

    Assert.Equal(ServerRuntimeState.Running, internalHost.State);
    Assert.Equal(ServerRuntimeState.Running, headlessHost.State);
    internalHost.Tick();
    Assert.Equal(1, headlessHost.RunFixedSteps(1));
}

static void HostLifecyclePreservesRuntimeRejections()
{
    using var internalHost = new InternalHost(new FixtureComposition(), CreateRuntimeOptions());
    using var headlessHost = new HeadlessHost(
        new FixtureComposition(), CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));

    Assert.Throws<InvalidOperationException>(() => internalHost.Tick());
    Assert.Throws<InvalidOperationException>(() => internalHost.Stop());
    Assert.Throws<InvalidOperationException>(() => headlessHost.RunFixedSteps(0));

    using var neverStarted = new HeadlessHost(
        new FixtureComposition(), CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));
    neverStarted.Shutdown();
    neverStarted.Shutdown();
    Assert.Equal(ServerRuntimeState.Disposed, neverStarted.State);

    internalHost.Start();
    headlessHost.Start();
    Assert.Throws<InvalidOperationException>(() => internalHost.Start());
    Assert.Throws<InvalidOperationException>(() => headlessHost.Start());
    internalHost.Stop();
    headlessHost.Stop();
    internalHost.Stop();
    headlessHost.Stop();
    Assert.Throws<InvalidOperationException>(() => headlessHost.RunFixedSteps(0));
}

Console.WriteLine($"All {tests.Length} Server Hosting S0.2 tests passed.");

static void StaticContentHttpHostServesExactImmutableBytes()
{
    byte[] original = "canonical-package-bytes"u8.ToArray();
    int port = ReserveLoopbackPort();
    using var host = new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, port),
        [new StaticContentHttpResource("/content/package.uspkg", original)]);
    original[0] = (byte)'X';
    using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
    using HttpResponseMessage response = client.GetAsync(host.GetUri("/content/package.uspkg"))
        .GetAwaiter().GetResult();
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    byte[] received = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    Assert.True(received.AsSpan().SequenceEqual("canonical-package-bytes"u8));
    Assert.Equal("application/vnd.ultrasim.content-package", response.Content.Headers.ContentType?.MediaType);
    StaticContentHttpHostSnapshot snapshot = host.GetSnapshot();
    Assert.Equal(1, snapshot.ResourceCount);
    Assert.Equal(1UL, snapshot.Requests);
    Assert.Equal(1UL, snapshot.Served);
    Assert.Equal((long)received.Length, snapshot.BytesServed);
}

static void StaticContentHttpHostBoundsAndRejectsRequests()
{
    Assert.Throws<ArgumentOutOfRangeException>(() => new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, 0),
        [new StaticContentHttpResource("/one", "x"u8.ToArray())]));
    Assert.Throws<ArgumentException>(() => new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [new StaticContentHttpResource("/../escape", "x"u8.ToArray())]));
    Assert.Throws<ArgumentException>(() => new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [new StaticContentHttpResource("/one", "too-large"u8.ToArray())],
        new StaticContentHttpHostLimits(1, 2)));

    using var host = new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [new StaticContentHttpResource("/content/package.uspkg", "ok"u8.ToArray())]);
    using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
    using HttpResponseMessage missing = client.GetAsync(new Uri(host.BaseUri, "content/missing.uspkg"))
        .GetAwaiter().GetResult();
    Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    using var post = new HttpRequestMessage(HttpMethod.Post, host.GetUri("/content/package.uspkg"));
    using HttpResponseMessage rejected = client.Send(post);
    Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    StaticContentHttpHostSnapshot snapshot = host.GetSnapshot();
    Assert.Equal(2UL, snapshot.Requests);
    Assert.Equal(0UL, snapshot.Served);
    Assert.Equal(2UL, snapshot.Rejected);
}

static void StalledContentResponseCannotBlockIndependentDownload()
{
    byte[] large = GC.AllocateUninitializedArray<byte>(32 * 1024 * 1024);
    Random.Shared.NextBytes(large);
    var limits = new StaticContentHttpHostLimits(
        maximumResources: 2,
        maximumResourceBytes: large.Length + 16L,
        maximumConcurrentResponses: 2,
        maximumQueuedResponses: 1,
        responseTimeoutMilliseconds: 10_000);
    var host = new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [
            new StaticContentHttpResource("/content/large.uspkg", large),
            new StaticContentHttpResource("/content/small.uspkg", "ok"u8.ToArray()),
        ],
        limits);
    using var client = new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 4,
    });
    using HttpResponseMessage stalled = client.Send(
        new HttpRequestMessage(HttpMethod.Get, host.GetUri("/content/large.uspkg")),
        HttpCompletionOption.ResponseHeadersRead);
    Assert.Equal(HttpStatusCode.OK, stalled.StatusCode);
    WaitUntil(() => host.GetSnapshot().ActiveResponses == 1,
        TimeSpan.FromSeconds(2), "The large response did not occupy one bounded worker.");

    Task<byte[]> independent = client.GetByteArrayAsync(
        host.GetUri("/content/small.uspkg"));
    Assert.True(independent.Wait(TimeSpan.FromSeconds(2)));
    Assert.True(independent.Result.AsSpan().SequenceEqual("ok"u8));
    StaticContentHttpHostSnapshot snapshot = host.GetSnapshot();
    Assert.Equal(2, snapshot.MaximumConcurrentResponses);
    Assert.Equal(1, snapshot.MaximumQueuedResponses);
    Assert.True(snapshot.PeakActiveResponses >= 2);
    Assert.True(snapshot.PeakActiveResponses <= snapshot.MaximumConcurrentResponses);

    long started = Stopwatch.GetTimestamp();
    host.Dispose();
    Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2));
}

static void ExpiredContentResponseReleasesItsWorker()
{
    byte[] large = GC.AllocateUninitializedArray<byte>(32 * 1024 * 1024);
    Random.Shared.NextBytes(large);
    var limits = new StaticContentHttpHostLimits(
        maximumResources: 2,
        maximumResourceBytes: large.Length + 16L,
        maximumConcurrentResponses: 1,
        maximumQueuedResponses: 1,
        responseTimeoutMilliseconds: 100);
    using var host = new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [
            new StaticContentHttpResource("/content/large.uspkg", large),
            new StaticContentHttpResource("/content/small.uspkg", "ok"u8.ToArray()),
        ],
        limits);
    using var stalled = new TcpClient
    {
        ReceiveBufferSize = 1_024,
        NoDelay = true,
    };
    stalled.Connect(IPAddress.Loopback, host.BaseUri.Port);
    byte[] request = System.Text.Encoding.ASCII.GetBytes(
        "GET /content/large.uspkg HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: keep-alive\r\n\r\n");
    stalled.GetStream().Write(request);
    WaitUntil(() => host.GetSnapshot().ActiveResponses == 1,
        TimeSpan.FromSeconds(2), "The stalled response did not occupy the only worker.");

    using var client = new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        MaxConnectionsPerServer = 1,
    });
    Task<byte[]> independent = client.GetByteArrayAsync(
        host.GetUri("/content/small.uspkg"));
    if (!independent.Wait(TimeSpan.FromMilliseconds(1_500)))
        throw new InvalidOperationException(
            "The response deadline did not release the stalled worker.");
    Assert.True(independent.Result.AsSpan().SequenceEqual("ok"u8));
    WaitUntil(() => host.GetSnapshot().ActiveResponses == 0,
        TimeSpan.FromSeconds(1), "Response worker accounting did not settle.");
    StaticContentHttpHostSnapshot snapshot = host.GetSnapshot();
    Assert.Equal(1UL, snapshot.Served);
    Assert.True(snapshot.Rejected >= 1);
}

static void ContentResponseQueueRejectsOverflowAndShutdownTerminatesActiveWrite()
{
    byte[] large = GC.AllocateUninitializedArray<byte>(32 * 1024 * 1024);
    Random.Shared.NextBytes(large);
    var limits = new StaticContentHttpHostLimits(
        maximumResources: 2,
        maximumResourceBytes: large.Length + 16L,
        maximumConcurrentResponses: 1,
        maximumQueuedResponses: 1,
        responseTimeoutMilliseconds: 10_000);
    var host = new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [
            new StaticContentHttpResource("/content/large.uspkg", large),
            new StaticContentHttpResource("/content/small.uspkg", "ok"u8.ToArray()),
        ],
        limits);
    using var stalled = new TcpClient
    {
        ReceiveBufferSize = 1_024,
        NoDelay = true,
    };
    try
    {
        stalled.Connect(IPAddress.Loopback, host.BaseUri.Port);
        byte[] request = System.Text.Encoding.ASCII.GetBytes(
            "GET /content/large.uspkg HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: keep-alive\r\n\r\n");
        stalled.GetStream().Write(request);
        WaitUntil(() => host.GetSnapshot().ActiveResponses == 1,
            TimeSpan.FromSeconds(2), "The stalled response did not occupy the only worker.");

        using var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 4,
        });
        Task<byte[]> queued = client.GetByteArrayAsync(host.GetUri("/content/small.uspkg"));
        WaitUntil(() => host.GetSnapshot().Requests >= 2,
            TimeSpan.FromSeconds(2), "The bounded pending response was not admitted.");
        using HttpResponseMessage overflow = client.Send(
            new HttpRequestMessage(HttpMethod.Get, host.GetUri("/content/small.uspkg")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, overflow.StatusCode);
        Assert.True(host.GetSnapshot().Rejected >= 1);

        long started = Stopwatch.GetTimestamp();
        host.Dispose();
        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2));
        AssertHttpHostRetired(host);
        host.Dispose();
        try { _ = queued.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }
    finally
    {
        host.Dispose();
    }
}

static void StaticContentHttpHostShutdownInterleavingsRetireEveryOwner()
{
    using (var host = CreateHttpHost())
    {
        DisposeWithinBound(host);
        AssertHttpHostRetired(host);
    }

    foreach (bool concurrent in new[] { false, true })
    foreach (int workers in new[] { 1, 3 })
    {
        RunGatedShutdown(gate => new StaticContentHttpHostTestHooks { BeforeAcceptWait = gate },
            sendRequest: false, concurrent: concurrent, workers: workers);
        RunGatedShutdown(gate => new StaticContentHttpHostTestHooks { BeforeAcceptWait = gate },
            concurrent: concurrent, workers: workers);
        RunGatedShutdown(gate => new StaticContentHttpHostTestHooks { AfterAccept = gate },
            concurrent: concurrent, workers: workers);
        RunGatedShutdown(gate => new StaticContentHttpHostTestHooks { BeforeResponseInitialization = gate },
            concurrent: concurrent, workers: workers);
        RunGatedShutdown(gate => new StaticContentHttpHostTestHooks { BeforeResponseWrite = gate },
            concurrent: concurrent, workers: workers);
        RunGatedShutdown(gate => new StaticContentHttpHostTestHooks { AfterResponseWrite = gate },
            concurrent: concurrent, workers: workers);
    }

    using (var host = CreateHttpHost())
    using (var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }))
    {
        byte[] bytes = client.GetByteArrayAsync(host.GetUri("/content/test.uspkg"))
            .GetAwaiter().GetResult();
        Assert.True(bytes.AsSpan().SequenceEqual("response"u8));
        DisposeWithinBound(host);
        AssertHttpHostRetired(host);
    }

    using (var entered = new ManualResetEventSlim())
    using (var release = new ManualResetEventSlim())
    using (var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        BeforeResponseWrite = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
        },
    }, 8 * 1024 * 1024))
    {
        var disconnected = new TcpClient { ReceiveBufferSize = 1_024, NoDelay = true };
        disconnected.Connect(IPAddress.Loopback, host.BaseUri.Port);
        SendRawGet(disconnected, "/content/test.uspkg");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        disconnected.Dispose();
        Task disposing = Task.Run(host.Dispose);
        WaitUntil(() => host.GetLifecycleSnapshotForTesting().State == 1,
            TimeSpan.FromSeconds(1), "HTTP host did not enter Stopping.");
        release.Set();
        Assert.True(disposing.Wait(TimeSpan.FromSeconds(2)));
        disposing.GetAwaiter().GetResult();
        AssertHttpHostRetired(host);
    }
}

static void StaticContentHttpHostConcurrentDisposeIsIdempotent()
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        BeforeResponseWrite = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
        },
    });
    using var client = new TcpClient();
    client.Connect(IPAddress.Loopback, host.BaseUri.Port);
    SendRawGet(client, "/content/test.uspkg");
    Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

    Task first = Task.Run(host.Dispose);
    WaitUntil(() => host.GetLifecycleSnapshotForTesting().State == 1,
        TimeSpan.FromSeconds(1), "HTTP host did not enter Stopping.");
    Task second = Task.Run(host.Dispose);
    release.Set();
    Assert.True(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(2)));
    first.GetAwaiter().GetResult();
    second.GetAwaiter().GetResult();
    host.Dispose();
    AssertHttpHostRetired(host);
}

static void StaticContentHttpHostShutdownPreservesUnexpectedWorkerFault()
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        BeforeResponseInitialization = () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
            throw new InjectedHttpResponseException();
        },
    });
    using var client = new TcpClient();
    try
    {
        client.Connect(IPAddress.Loopback, host.BaseUri.Port);
        SendRawGet(client, "/content/test.uspkg");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        Task disposing = Task.Run(host.Dispose);
        WaitUntil(() => host.GetLifecycleSnapshotForTesting().State == 1,
            TimeSpan.FromSeconds(1), "HTTP host did not enter Stopping.");
        release.Set();
        Assert.Throws<InjectedHttpResponseException>(() =>
            disposing.GetAwaiter().GetResult());
        AssertHttpHostRetired(host);
        Assert.Throws<InjectedHttpResponseException>(host.Dispose);
    }
    finally
    {
        try { host.Dispose(); }
        catch (InjectedHttpResponseException) { }
    }
}

static void StaticContentHttpHostShutdownPreservesPreStoppingAcceptFault()
{
    using var entered = new ManualResetEventSlim();
    var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        BeforeAcceptWait = () =>
        {
            entered.Set();
            throw new ObjectDisposedException("injected accept ownership");
        },
    });
    try
    {
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        WaitUntil(() => host.GetLifecycleSnapshotForTesting().AcceptWorkerCompleted,
            TimeSpan.FromSeconds(1), "Injected accept fault did not retire its worker.");
        Assert.Throws<ObjectDisposedException>(host.Dispose);
        AssertHttpHostRetired(host);
        Assert.Throws<ObjectDisposedException>(host.Dispose);
    }
    finally
    {
        try { host.Dispose(); }
        catch (ObjectDisposedException) { }
    }
}

static void StaticContentHttpHostShutdownPreservesEveryResponseWorkerFault()
{
    using var bothEntered = new CountdownEvent(2);
    int nextWorker = 0;
    var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        BeforeResponseInitialization = () =>
        {
            int worker = Interlocked.Increment(ref nextWorker);
            bothEntered.Signal();
            Assert.True(bothEntered.Wait(TimeSpan.FromSeconds(2)));
            throw new InjectedHttpResponseException(worker);
        },
    }, maximumConcurrentResponses: 2, maximumQueuedResponses: 2);
    using var firstClient = new TcpClient();
    using var secondClient = new TcpClient();
    try
    {
        firstClient.Connect(IPAddress.Loopback, host.BaseUri.Port);
        secondClient.Connect(IPAddress.Loopback, host.BaseUri.Port);
        SendRawGet(firstClient, "/content/test.uspkg");
        SendRawGet(secondClient, "/content/test.uspkg");
        Assert.True(bothEntered.Wait(TimeSpan.FromSeconds(2)));
        WaitUntil(() => host.GetLifecycleSnapshotForTesting().ResponseWorkersCompleted,
            TimeSpan.FromSeconds(1), "Injected response faults did not retire both workers.");

        Task<Exception?> firstDispose = Task.Run(() => CaptureFailure(host.Dispose));
        Task<Exception?> secondDispose = Task.Run(() => CaptureFailure(host.Dispose));
        Assert.True(Task.WhenAll(firstDispose, secondDispose).Wait(TimeSpan.FromSeconds(2)));
        AssertCompleteResponseFailureSet(firstDispose.Result);
        AssertCompleteResponseFailureSet(secondDispose.Result);
        Assert.True(ReferenceEquals(firstDispose.Result, secondDispose.Result));
        AssertHttpHostRetired(host);
        Exception? repeated = CaptureFailure(host.Dispose);
        AssertCompleteResponseFailureSet(repeated);
        Assert.True(ReferenceEquals(firstDispose.Result, repeated));
    }
    finally
    {
        _ = CaptureFailure(host.Dispose);
    }
}

static Exception? CaptureFailure(Action action)
{
    try
    {
        action();
        return null;
    }
    catch (Exception exception)
    {
        return exception;
    }
}

static void AssertCompleteResponseFailureSet(Exception? failure)
{
    Assert.True(failure is AggregateException);
    int[] workers = ((AggregateException)failure!).Flatten().InnerExceptions
        .OfType<InjectedHttpResponseException>()
        .Select(static exception => exception.Worker)
        .Order()
        .ToArray();
    Assert.True(workers.AsSpan().SequenceEqual([1, 2]));
}

static void StaticContentHttpHostShutdownStressIsBounded()
{
    const int cycles = 1_024;
    for (int cycle = 0; cycle < cycles; cycle++)
    {
        using var host = CreateHttpHost();
        using var client = new TcpClient();
        if ((cycle & 1) == 0)
        {
            client.Connect(IPAddress.Loopback, host.BaseUri.Port);
            SendRawGet(client, "/content/test.uspkg");
        }
        Task first = Task.Run(host.Dispose);
        Task second = Task.Run(host.Dispose);
        Assert.True(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(2)));
        first.GetAwaiter().GetResult();
        second.GetAwaiter().GetResult();
        AssertHttpHostRetired(host);
    }
}

static void CancelledHttpListenerWrapperDoesNotRetireAccept()
{
    using var listener = new HttpListener();
    int port = ReserveLoopbackPort();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();
    Task<HttpListenerContext> accept = listener.GetContextAsync();
    using var cancellation = new CancellationTokenSource();
    Task<HttpListenerContext> wrapper = accept.WaitAsync(cancellation.Token);
    cancellation.Cancel();
    Assert.Throws<OperationCanceledException>(() => wrapper.GetAwaiter().GetResult());
    Assert.True(!accept.IsCompleted);
    using var client = new TcpClient();
    client.Connect(IPAddress.Loopback, port);
    SendRawGet(client, "/owned");
    Assert.True(accept.Wait(TimeSpan.FromSeconds(2)));
    HttpListenerContext context = accept.GetAwaiter().GetResult();
    context.Response.ContentLength64 = 0;
    context.Response.Close();
    listener.Stop();
}

static void StaticContentHttpHostRetainsActualAcceptUntilTeardown()
{
    using var acceptStarted = new ManualResetEventSlim();
    using var beforeStop = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    Task<HttpListenerContext>? accept = null;
    using var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        AcceptWaitStarted = operation => { accept = operation; acceptStarted.Set(); },
        BeforeListenerStop = () =>
        {
            beforeStop.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
        },
    });
    Assert.True(acceptStarted.Wait(TimeSpan.FromSeconds(2)));
    Task first = Task.Run(host.Dispose);
    Task second = Task.Run(host.Dispose);
    try
    {
        Assert.True(beforeStop.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(!accept!.IsCompleted);
        Assert.True(!host.GetLifecycleSnapshotForTesting().AcceptWorkerCompleted);
        Assert.Equal(1L, host.GetLifecycleSnapshotForTesting().AcceptOperationsStarted);
        Assert.Equal(0L, host.GetLifecycleSnapshotForTesting().AcceptOperationsCompleted);
    }
    finally { release.Set(); }
    Assert.True(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(2)));
    Assert.True(accept!.IsCompleted);
    AssertHttpHostRetired(host);
}

static void StaticContentHttpHostDoesNotAbortInitializingResponse()
{
    using var initializing = new ManualResetEventSlim();
    using var abortSweep = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var host = CreateHttpHost(new StaticContentHttpHostTestHooks
    {
        BeforeResponseInitialization = () =>
        {
            initializing.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
        },
        AfterActiveResponseAbort = abortSweep.Set,
    });
    using var client = new TcpClient();
    client.Connect(IPAddress.Loopback, host.BaseUri.Port);
    SendRawGet(client, "/content/test.uspkg");
    Assert.True(initializing.Wait(TimeSpan.FromSeconds(2)));
    Task first = Task.Run(host.Dispose);
    Task second = Task.Run(host.Dispose);
    try
    {
        Assert.True(abortSweep.Wait(TimeSpan.FromSeconds(2)));
        // Independent of telemetry: counts actual Close/Abort invocations, not disabled counters.
        Assert.Equal(0L, host.GetLifecycleSnapshotForTesting().ResponseCloseOperations);
    }
    finally { release.Set(); }
    Assert.True(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(2)));
    Assert.Equal(1L, host.GetLifecycleSnapshotForTesting().ResponseCloseOperations);
    AssertHttpHostRetired(host);
}

static void StaticContentHttpHostPartialRequestsAndPortReuse()
{
    int port = ReserveLoopbackPort();
    for (int cycle = 0; cycle < 16; cycle++)
    {
        using var host = new StaticContentHttpHost(new IPEndPoint(IPAddress.Loopback, port),
            [new StaticContentHttpResource("/one", "x"u8.ToArray())],
            new StaticContentHttpHostLimits(1, 1, cycle % 2 == 0 ? 1 : 3, cycle % 2 == 0 ? 1 : 4, 1_000));
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        if ((cycle & 2) != 0) client.GetStream().Write("GET /one HTTP/1.1\r\nHost: 127.0.0.1\r\n"u8);
        Task first = Task.Run(host.Dispose);
        Task second = Task.Run(host.Dispose);
        Assert.True(Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(2)));
        AssertHttpHostRetired(host);
    }
}

// Runtime-source control, not a production reflection dependency. On managed HttpListener,
// endpoint teardown closes partial (not yet application-visible) requests. Its header formatter
// may overlap the framework read callback's SendError header mutation after prefix removal.
static void ManagedHttpListenerTeardownCanOverlapUnregisteredHeaderMutation()
{
    if (OperatingSystem.IsWindows()) return; // HTTP.sys is a different implementation.
    foreach (bool abort in new[] { false, true })
    foreach (bool keepAlive in new[] { false, true })
    {
    using var listener = new HttpListener();
    int port = ReserveLoopbackPort();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();
    Task<HttpListenerContext> accept = listener.GetContextAsync();
    using var client = new TcpClient();
    client.Connect(IPAddress.Loopback, port);
    client.GetStream().Write(System.Text.Encoding.ASCII.GetBytes(
        "GET /pending HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: " +
        (keepAlive ? "keep-alive" : "close") + "\r\n"));
    Type manager = typeof(HttpListener).Assembly.GetType("System.Net.HttpEndPointManager", true)!;
    var endpoints = (System.Collections.IDictionary)manager.GetField("s_ipEndPoints",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    object endpoint = ((System.Collections.IDictionary)endpoints[IPAddress.Loopback]!)[port]!;
    var connections = (System.Collections.IDictionary)Field(endpoint, "_unregisteredConnections");
    object? connection = null;
    WaitUntil(() =>
    {
        lock (connections)
            foreach (object key in connections.Keys) { connection = key; break; }
        return connection is not null;
    }, TimeSpan.FromSeconds(2), "Managed endpoint did not publish the partial connection.");
    var context = (HttpListenerContext)Field(connection!, "_context");
    using var formatting = new ManualResetEventSlim();
    using var mutated = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var headers = new TeardownObservedHeaders(formatting, mutated, release);
    typeof(HttpListenerResponse).GetField("_webHeaders", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(context.Response, headers);
    Task<Exception?> stop = Task.Run(() => CaptureFailure(abort ? listener.Abort : listener.Stop));
    try
    {
        Assert.True(formatting.Wait(TimeSpan.FromSeconds(2)));
        // The prefix is now removed, but the accepted socket remains open inside endpoint.Close.
        // Complete the real request: the runtime's BindContext failure executes SendError(404).
        client.GetStream().Write("\r\n"u8);
        Assert.True(mutated.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(!accept.IsCompleted); // no application owner ever receives this context.
    }
    finally { release.Set(); }
    Assert.True(stop.Wait(TimeSpan.FromSeconds(2)));
    if (stop.Result is not null) throw stop.Result;
    Assert.Throws<ObjectDisposedException>(() => accept.GetAwaiter().GetResult());
    Console.WriteLine($"CONTROL {(abort ? "Abort" : "Stop")} keepAlive={keepAlive}: " +
        "managed endpoint teardown / framework SendError header mutation overlap proven; " +
        "no application-visible context; no exception injected.");
    }

    static object Field(object instance, string name) => instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}

static void RunGatedShutdown(
    Func<Action, StaticContentHttpHostTestHooks> createHooks,
    bool sendRequest = true,
    bool concurrent = false,
    int workers = 1)
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    Action gate = () =>
    {
        entered.Set();
        Assert.True(release.Wait(TimeSpan.FromSeconds(2)));
    };
    using var host = CreateHttpHost(createHooks(gate), maximumConcurrentResponses: workers,
        maximumQueuedResponses: workers == 1 ? 1 : 4);
    using var client = new TcpClient();
    if (sendRequest)
    {
        client.Connect(IPAddress.Loopback, host.BaseUri.Port);
        SendRawGet(client, "/content/test.uspkg");
    }
    Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
    Task disposing = Task.Run(host.Dispose);
    Task second = concurrent ? Task.Run(host.Dispose) : Task.CompletedTask;
    WaitUntil(() => host.GetLifecycleSnapshotForTesting().State == 1,
        TimeSpan.FromSeconds(1), "HTTP host did not enter Stopping.");
    release.Set();
    Assert.True(Task.WhenAll(disposing, second).Wait(TimeSpan.FromSeconds(2)));
    disposing.GetAwaiter().GetResult();
    AssertHttpHostRetired(host);
}

static StaticContentHttpHost CreateHttpHost(
    StaticContentHttpHostTestHooks? hooks = null,
    int responseBytes = 8,
    int maximumConcurrentResponses = 1,
    int maximumQueuedResponses = 1)
{
    byte[] bytes = responseBytes == 8
        ? "response"u8.ToArray()
        : GC.AllocateUninitializedArray<byte>(responseBytes);
    return new StaticContentHttpHost(
        new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
        [new StaticContentHttpResource("/content/test.uspkg", bytes)],
        new StaticContentHttpHostLimits(
            1, bytes.Length, maximumConcurrentResponses, maximumQueuedResponses, 10_000),
        hooks);
}

static void SendRawGet(TcpClient client, string route)
{
    byte[] request = System.Text.Encoding.ASCII.GetBytes(
        $"GET {route} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: keep-alive\r\n\r\n");
    client.GetStream().Write(request);
}

static void DisposeWithinBound(StaticContentHttpHost host)
{
    long started = Stopwatch.GetTimestamp();
    host.Dispose();
    Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2));
}

static void AssertHttpHostRetired(StaticContentHttpHost host)
{
    StaticContentHttpHostLifecycleSnapshot snapshot = host.GetLifecycleSnapshotForTesting();
    Assert.Equal(2, snapshot.State);
    Assert.True(snapshot.AcceptWorkerCompleted);
    Assert.True(snapshot.ResponseWorkersCompleted);
    Assert.Equal(0, snapshot.PendingResponses);
    Assert.Equal(0, snapshot.ActiveResponses);
    Assert.True(snapshot.ListenerClosed);
    Assert.True(snapshot.CancellationDisposed);
    Assert.Equal(snapshot.AcceptOperationsStarted, snapshot.AcceptOperationsCompleted);
}

static void WaitUntil(Func<bool> condition, TimeSpan timeout, string failure)
{
    DateTime deadline = DateTime.UtcNow + timeout;
    while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(1);
    if (!condition()) throw new InvalidOperationException(failure);
}

static int ReserveLoopbackPort()
{
    using var reservation = new TcpListener(IPAddress.Loopback, 0);
    reservation.Start();
    int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
    reservation.Stop();
    return port;
}

static void HostsUseTheSameConcreteRuntimeType()
{
    using var internalHost = new InternalHost(new FixtureComposition(), CreateRuntimeOptions());
    using var headlessHost = new HeadlessHost(
        new FixtureComposition(), CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));

    Assert.Equal(typeof(ServerRuntime), internalHost.RuntimeForTesting.GetType());
    Assert.Equal(typeof(ServerRuntime), headlessHost.RuntimeForTesting.GetType());
    Assert.False(ReferenceEquals(internalHost.RuntimeForTesting, headlessHost.RuntimeForTesting));

    internalHost.Start();
    headlessHost.Start();
    Assert.Equal(internalHost.State, headlessHost.State);
    Assert.Equal(internalHost.ActiveGenerationId, headlessHost.ActiveGenerationId);
    internalHost.Stop();
    headlessHost.Stop();
}

static void HostSurfaceDoesNotExposeGameFacet()
{
    Assert.True(typeof(InternalHost).GetProperty(nameof(ServerRuntime.Game)) is null);
    Assert.True(typeof(HeadlessHost).GetProperty(nameof(ServerRuntime.Game)) is null);
    Assert.True(typeof(InternalHost).GetProperty("Runtime") is null);
    Assert.True(typeof(HeadlessHost).GetProperty("Runtime") is null);
}

static void InternalAndHeadlessProduceIdenticalCanonicalState()
{
    const int steps = 240;
    var internalComposition = new FixtureComposition();
    var headlessComposition = new FixtureComposition();
    using var internalHost = new InternalHost(internalComposition, CreateRuntimeOptions());
    using var headlessHost = new HeadlessHost(
        headlessComposition,
        CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));

    internalHost.Start();
    headlessHost.Start();
    for (int index = 0; index < steps; index++)
        internalHost.Tick();
    Assert.Equal(steps, headlessHost.RunFixedSteps(steps));

    Assert.Equal(internalHost.State, headlessHost.State);
    Assert.Equal(internalHost.ActiveGenerationId, headlessHost.ActiveGenerationId);
    Assert.Equal(internalHost.SimulationTick, headlessHost.SimulationTick);
    Assert.Equal(internalHost.WorldTimeMicroseconds, headlessHost.WorldTimeMicroseconds);
    Assert.Equal(internalHost.SimulationFrequencyHz, headlessHost.SimulationFrequencyHz);
    Assert.Equal(internalHost.SimulationDeltaSeconds, headlessHost.SimulationDeltaSeconds);
    Assert.Equal(internalComposition.Counter.Value, headlessComposition.Counter.Value);
    Assert.Equal(internalComposition.Counter.LastTick, headlessComposition.Counter.LastTick);
}

static void HeadlessRunHonorsCancellationWithoutOwningShutdown()
{
    var composition = new FixtureComposition();
    using var host = new HeadlessHost(
        composition,
        CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));
    host.Start();

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Assert.Equal(0, host.RunFixedSteps(100, cancelled.Token));
    Assert.Equal(ServerRuntimeState.Running, host.State);

    Assert.Equal(3, host.RunFixedSteps(3));
    Assert.Equal(3UL, host.SimulationTick);
}

static void HeadlessShutdownDisposesGenerationCleanly()
{
    var composition = new FixtureComposition();
    using var host = new HeadlessHost(
        composition,
        CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));
    host.Start();

    host.Shutdown();
    host.Shutdown();

    Assert.Equal(ServerRuntimeState.Disposed, host.State);
    Assert.Equal(1, composition.Counter.ShutdownCount);
}

static void ThrowingLogSinkCannotChangeLifecycle()
{
    var composition = new FixtureComposition();
    using var host = new HeadlessHost(
        composition,
        CreateRuntimeOptions(),
        new HostCapabilities(new ThrowingLogSink()));

    host.Start();
    host.Tick();
    host.Stop();
    host.Dispose();

    Assert.Equal(ServerRuntimeState.Disposed, host.State);
    Assert.Equal(1, composition.Counter.Value);
    Assert.Equal(1, composition.Counter.ShutdownCount);
}

static void HostLoggingDoesNotAlterSimulationState()
{
    var quietComposition = new FixtureComposition();
    var loggedComposition = new FixtureComposition();
    var sink = new RecordingLogSink();
    using var quiet = new InternalHost(quietComposition, CreateRuntimeOptions());
    using var logged = new HeadlessHost(
        loggedComposition,
        CreateRuntimeOptions(),
        new HostCapabilities(sink));

    quiet.Start();
    logged.Start();
    for (int index = 0; index < 60; index++)
    {
        quiet.Tick();
        logged.Tick();
    }
    quiet.Stop();
    logged.Stop();

    Assert.True(sink.Messages.Count >= 2);
    Assert.Equal(quiet.SimulationTick, logged.SimulationTick);
    Assert.Equal(quiet.WorldTimeMicroseconds, logged.WorldTimeMicroseconds);
    Assert.Equal(quietComposition.Counter.Value, loggedComposition.Counter.Value);
    Assert.Equal(quietComposition.Counter.LastTick, loggedComposition.Counter.LastTick);
}

static void InternalHostHasNoImplicitLoggingCapability()
{
    var composition = new FixtureComposition();
    using var host = new InternalHost(composition, CreateRuntimeOptions());
    host.Start();
    host.Stop();
    Assert.Equal(ServerRuntimeState.Stopped, host.State);
}

static void WarmedHostDrivingAllocatesZeroManagedBytes()
{
    using var internalHost = new InternalHost(new FixtureComposition(), CreateRuntimeOptions());
    using var headlessHost = new HeadlessHost(
        new FixtureComposition(), CreateRuntimeOptions(),
        new HostCapabilities(new RecordingLogSink()));
    internalHost.Start();
    headlessHost.Start();
    for (int index = 0; index < 256; index++)
    {
        internalHost.Tick();
        headlessHost.Tick();
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 2_000; index++) internalHost.Tick();
    Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);

    before = GC.GetAllocatedBytesForCurrentThread();
    Assert.Equal(2_000, headlessHost.RunFixedSteps(2_000));
    Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static void HostWrapperBenchmarkIsCaptured()
{
    const int samples = 21;
    const int ticksPerSample = 2_000;
    var directComposition = new FixtureComposition();
    var hostedComposition = new FixtureComposition();
    using var direct = new ServerRuntime(directComposition, CreateRuntimeOptions());
    using var hosted = new InternalHost(hostedComposition, CreateRuntimeOptions());
    direct.Initialize();
    direct.Start();
    hosted.Start();
    for (int index = 0; index < 4_096; index++)
    {
        direct.Tick();
        hosted.Tick();
    }

    var directSamples = new double[samples];
    var hostedSamples = new double[samples];
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            hostedSamples[sample] = MeasureTicks(hosted.Tick, ticksPerSample);
            directSamples[sample] = MeasureTicks(direct.Tick, ticksPerSample);
        }
        else
        {
            directSamples[sample] = MeasureTicks(direct.Tick, ticksPerSample);
            hostedSamples[sample] = MeasureTicks(hosted.Tick, ticksPerSample);
        }
    }

    double directMean = directSamples.Average();
    double hostedMean = hostedSamples.Average();
    double[] pairedRatios = directSamples.Zip(
        hostedSamples,
        static (directSample, hostedSample) => hostedSample / Math.Max(1d, directSample)).ToArray();
    Array.Sort(pairedRatios);
    double ratio = pairedRatios[pairedRatios.Length / 2];
    double deltaPerTick = (hostedMean - directMean) / ticksPerSample;
    Assert.Equal(directComposition.Counter.Value, hostedComposition.Counter.Value);
    Assert.Equal(direct.SimulationTick, hosted.SimulationTick);
    Assert.True(ratio <= 1.05);
    Console.WriteLine(
        $"BASELINE S0.2 cadence=60Hz cadence_changes=off adaptive=off playback=1x " +
        $"samples={samples} ticks_per_sample={ticksPerSample} " +
        $"direct_runtime_mean={directMean:F0}ns direct_runtime_p95={Percentile95(directSamples):F0}ns " +
        $"internal_host_mean={hostedMean:F0}ns internal_host_p95={Percentile95(hostedSamples):F0}ns " +
        $"wrapper_ratio={ratio:F3} delta_per_tick={deltaPerTick:F2}ns");
}

static double MeasureTicks(Action tick, int count)
{
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++) tick();
    return Stopwatch.GetElapsedTime(started).TotalNanoseconds;
}

static double Percentile95(double[] values)
{
    double[] sorted = (double[])values.Clone();
    Array.Sort(sorted);
    return sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
}

static ServerRuntimeOptions CreateRuntimeOptions() => new(
    new EcsWorldOptions(
        new EntityCapacity(16),
        maximumArchetypes: 1,
        maximumPreparedPlans: 0,
        scheduler: new EcsSchedulerOptions(
            maximumSystems: 2,
            maximumDependencies: 2,
            maximumWorkItems: 4,
            maximumStructuralSegments: 4,
            backgroundWorkerCount: 0),
        maximumStructuralCommands: 0),
    WorldClockOptions.Default);

static class Assert
{
    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false.");
    }
}

sealed class RecordingLogSink : IHostLogSink
{
    public List<(HostLogLevel Level, string Message)> Messages { get; } = [];

    public void Write(HostLogLevel level, string message)
    {
        Messages.Add((level, message));
    }
}

sealed class ThrowingLogSink : IHostLogSink
{
    public void Write(HostLogLevel level, string message) =>
        throw new IOException("injected diagnostics failure");
}

sealed class InjectedHttpResponseException : Exception
{
    internal InjectedHttpResponseException()
    {
    }

    internal InjectedHttpResponseException(int worker)
        : base($"Injected response-worker failure {worker}.") => Worker = worker;

    internal int Worker { get; }
}

sealed class TeardownObservedHeaders(
    ManualResetEventSlim formatting,
    ManualResetEventSlim mutated,
    ManualResetEventSlim release) : WebHeaderCollection
{
    private int _formattingThread;
    public override string GetKey(int index)
    {
        if (Interlocked.CompareExchange(ref _formattingThread, Environment.CurrentManagedThreadId, 0) == 0)
        {
            formatting.Set();
            if (!release.Wait(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("Managed header-formatter control was not released.");
        }
        return base.GetKey(index)!;
    }

    public override void Set(string name, string? value)
    {
        base.Set(name, value);
        if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
            Volatile.Read(ref _formattingThread) != 0 &&
            Volatile.Read(ref _formattingThread) != Environment.CurrentManagedThreadId)
            mutated.Set();
    }
}

sealed class FixtureComposition : IServerComposition
{
    public FixtureComposition()
    {
        Schema = ComponentSchema.Create(new SchemaId(0x52, 0x01), ReadOnlySpan<ComponentRegistration>.Empty);
    }

    public ComponentSchema Schema { get; }

    public TickCounter Counter { get; } = new();

    public void Configure(ServerGenerationBuilder generation)
    {
        generation.World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
        generation.World.RegisterRangeSystem<TickSystem, TickJob>(
            new SystemDefinition("hosting.fixture.tick", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial),
            new TickSystem(Counter));
    }
}

sealed class TickCounter
{
    public int Value { get; set; }

    public ulong LastTick { get; set; }

    public int ShutdownCount { get; set; }
}

sealed class TickSystem : IRangeSystemRuntime<TickJob>
{
    private readonly TickCounter _counter;

    public TickSystem(TickCounter counter) => _counter = counter;

    public int GetRangeCount(in SystemPrepareContext context) => 1;

    public TickJob Prepare(in SystemPrepareContext context) => new(_counter);

    public void Shutdown(in SystemShutdownContext context) => _counter.ShutdownCount++;
}

struct TickJob : IRangeJob
{
    private readonly TickCounter _counter;

    public TickJob(TickCounter counter) => _counter = counter;

    public void Execute(in SystemJobContext context, int start, int count)
    {
        _counter.Value += count;
        _counter.LastTick = context.Tick;
    }
}
