using UltraSim.ECS;
using UltraSim.Server.Hosting;
using UltraSim.Server.Runtime;
using System.Diagnostics;

var tests = new (string Name, Action Body)[]
{
    ("HostsUseTheSameConcreteRuntimeType", HostsUseTheSameConcreteRuntimeType),
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
};

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
    double ratio = hostedMean / Math.Max(1d, directMean);
    double deltaPerTick = (hostedMean - directMean) / ticksPerSample;
    Assert.Equal(directComposition.Counter.Value, hostedComposition.Counter.Value);
    Assert.Equal(direct.SimulationTick, hosted.SimulationTick);
    Console.WriteLine(
        $"BASELINE S0.2 samples={samples} ticks_per_sample={ticksPerSample} " +
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
