using UltraSim.ECS;
using UltraSim.Server.Runtime;
using System.Diagnostics;
using System.Reflection;

var tests = new (string Name, Action Body)[]
{
    ("Lifecycle_IsExplicitAndIdempotent", Lifecycle_IsExplicitAndIdempotent),
    ("RuntimeTicksARealScheduledEcsSystem", RuntimeTicksARealScheduledEcsSystem),
    ("EmptyCompositionDoesNotBecomeForbiddenRuntimePolicy", EmptyCompositionDoesNotBecomeForbiddenRuntimePolicy),
    ("HostSurfaceDoesNotExposeGenerationReplacement", HostSurfaceDoesNotExposeGenerationReplacement),
    ("GameFacetIsCallbackScopedRunningAndOwnerBound", GameFacetIsCallbackScopedRunningAndOwnerBound),
    ("GameFacetFailureTerminallyFaultsRuntime", GameFacetFailureTerminallyFaultsRuntime),
    ("GameFacetRejectsReentrantControlWithoutDisposingBorrowedRoot", GameFacetRejectsReentrantControlWithoutDisposingBorrowedRoot),
    ("ReentrantPublicationRejectsAndReclaimsPrivateCandidate", ReentrantPublicationRejectsAndReclaimsPrivateCandidate),
    ("GenerationPublicationObserversAreIsolated", GenerationPublicationObserversAreIsolated),
    ("OwnerThreadBoundaryRejectsForeignHostControl", OwnerThreadBoundaryRejectsForeignHostControl),
    ("InvalidElapsedRejectsTransactionallyWithoutFaulting", InvalidElapsedRejectsTransactionallyWithoutFaulting),
    ("FailedReplacementLeavesActiveGenerationUnchanged", FailedReplacementLeavesActiveGenerationUnchanged),
    ("SuccessfulReplacementPublishesOneCoherentRoot", SuccessfulReplacementPublishesOneCoherentRoot),
    ("GenerationOwnedGameStatePublishesAndRetiresAtomically", GenerationOwnedGameStatePublishesAndRetiresAtomically),
    ("PublicationFailureIsTerminalAfterRootSwap", PublicationFailureIsTerminalAfterRootSwap),
    ("FixedWorldClockIsDeterministicAndSnapshotRestores", FixedWorldClockIsDeterministicAndSnapshotRestores),
    ("PlaybackChangesHostReachTimeNotTickMeaning", PlaybackChangesHostReachTimeNotTickMeaning),
    ("WarmedRuntimeTickAllocatesZeroManagedBytes", WarmedRuntimeTickAllocatesZeroManagedBytes),
    ("RuntimeWrapperBenchmarkIsCaptured", RuntimeWrapperBenchmarkIsCaptured),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} Server Runtime S0.1 tests passed.");

static void Lifecycle_IsExplicitAndIdempotent()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    Assert.Throws<InvalidOperationException>(() => runtime.Start());
    Assert.Throws<InvalidOperationException>(() => runtime.Tick());
    Assert.Throws<InvalidOperationException>(() => runtime.SetPlaybackRate(2));

    runtime.Initialize();
    Assert.Equal(ServerRuntimeState.Initialized, runtime.State);
    runtime.Start();
    Assert.Equal(ServerRuntimeState.Running, runtime.State);
    runtime.Stop();
    runtime.Stop();
    Assert.Equal(ServerRuntimeState.Stopped, runtime.State);
    Assert.Throws<InvalidOperationException>(() => runtime.Tick());
    Assert.Throws<InvalidOperationException>(() => runtime.Start());

    runtime.Dispose();
    runtime.Dispose();
    Assert.Equal(ServerRuntimeState.Disposed, runtime.State);
    Assert.Throws<ObjectDisposedException>(() => _ = runtime.CurrentGeneration);
}

static void RuntimeTicksARealScheduledEcsSystem()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    runtime.Tick();
    runtime.Tick(TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 30) / 60));

    Assert.Equal(2UL, runtime.CurrentGeneration.Clock.SimulationTick);
    Assert.Equal(2UL, runtime.CurrentGeneration.EcsWorld.TickCount);
    TickCounter counter = composition.GetCounter(runtime.CurrentGeneration.GenerationId);
    Assert.Equal(2, counter.Value);
    Assert.Equal(1UL, counter.LastTick);
}

static void EmptyCompositionDoesNotBecomeForbiddenRuntimePolicy()
{
    Assert.Equal(1, WorldClockOptions.Default.WorldTimeRate);
    using var runtime = new ServerRuntime(new EmptyComposition(), new ServerRuntimeOptions(
        CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    runtime.Tick();

    Assert.Equal(1UL, runtime.SimulationTick);
    Assert.Equal(1UL, runtime.CurrentGeneration.EcsWorld.TickCount);
}

static void HostSurfaceDoesNotExposeGenerationReplacement()
{
    const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;
    Assert.True(typeof(ServerRuntime).GetProperty("CurrentGeneration", publicInstance) is null);
    Assert.True(typeof(ServerRuntime).GetMethod("ReplaceGeneration", publicInstance) is null);
    Assert.True(typeof(ServerRuntime).GetProperty(nameof(ServerRuntime.ActiveGenerationId), publicInstance) is not null);
    Assert.True(typeof(ServerRuntime).GetProperty(nameof(ServerRuntime.SimulationTick), publicInstance) is not null);
    Assert.True(typeof(ServerRuntime).GetProperty(nameof(ServerRuntime.Game), publicInstance) is not null);
    Assert.True(typeof(ServerGameFacet).GetProperty("World", publicInstance) is null);
    Assert.True(typeof(ServerGenerationBuilder).GetMethod("RestoreClockSnapshot", publicInstance) is null);
}

static void GameFacetIsCallbackScopedRunningAndOwnerBound()
{
    var composition = new EmptyComposition();
    using var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(
        CreateWorldOptions(), WorldClockOptions.Default));

    Assert.False(runtime.Game.TryUseActiveGeneration(
        static (world, generationId) => (world.Id, generationId),
        out _));
    runtime.Initialize();
    Assert.False(runtime.Game.TryUseActiveGeneration(
        static (world, generationId) => (world.Id, generationId),
        out _));
    runtime.Start();
    Assert.True(runtime.Game.TryUseActiveGeneration(
        static (world, generationId) => (world.Id, generationId),
        out (WorldId World, ulong Generation) active));
    Assert.True(active.World.IsValid);
    Assert.Equal(runtime.ActiveGenerationId, active.Generation);

    Exception? observed = null;
    var thread = new Thread(() =>
    {
        try
        {
            runtime.Game.TryUseActiveGeneration(
                static (_, generationId) => generationId,
                out _);
        }
        catch (Exception exception)
        {
            observed = exception;
        }
    });
    thread.Start();
    thread.Join();
    Assert.True(observed is InvalidOperationException);
}

static void GameFacetFailureTerminallyFaultsRuntime()
{
    using var runtime = new ServerRuntime(new EmptyComposition(), new ServerRuntimeOptions(
        CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();

    Assert.Throws<InvalidOperationException>(() => runtime.Game.TryUseActiveGeneration<int>(
        static (_, _) => throw new InvalidOperationException("injected game failure"),
        out _));

    Assert.Equal(ServerRuntimeState.Faulted, runtime.State);
    Assert.Throws<InvalidOperationException>(() => runtime.Tick());
}

static void GameFacetRejectsReentrantControlWithoutDisposingBorrowedRoot()
{
    using var runtime = new ServerRuntime(new EmptyComposition(), new ServerRuntimeOptions(
        CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    EcsWorld borrowed = runtime.CurrentGeneration.EcsWorld;

    Assert.Throws<InvalidOperationException>(() => runtime.Game.TryUseActiveGeneration<int>(
        (_, _) =>
        {
            runtime.Dispose();
            return 1;
        },
        out _));

    Assert.Equal(ServerRuntimeState.Faulted, runtime.State);
    Assert.False(borrowed.IsDisposed);
}

static void ReentrantPublicationRejectsAndReclaimsPrivateCandidate()
{
    var composition = new EmptyComposition();
    using var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(
        CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    EcsWorld active = runtime.CurrentGeneration.EcsWorld;
    var prepared = new EcsWorld(composition.Schema, CreateWorldOptions());
    _ = prepared.RegisterArchetype(ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
    prepared.Seal();
    prepared.Initialize();
    prepared.BeginRun();
    var state = new GenerationStateProbe(99);

    Assert.Throws<InvalidOperationException>(() => runtime.Game.TryUseActiveGeneration<int>(
        (_, _) =>
        {
            runtime.Game.TryPublishPreparedGeneration(prepared, state, out _);
            return 1;
        },
        out _));

    Assert.Equal(ServerRuntimeState.Faulted, runtime.State);
    Assert.False(active.IsDisposed);
    Assert.True(prepared.IsDisposed);
    Assert.True(state.IsDisposed);
}

static void GenerationPublicationObserversAreIsolated()
{
    using var runtime = new ServerRuntime(new EmptyComposition(), new ServerRuntimeOptions(
        CreateWorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();
    int successfulObserver = 0;
    int reconciledObserver = 0;
    int observerFaults = 0;
    runtime.Game.GenerationPublished += _ => throw new InvalidOperationException("observer");
    runtime.Game.GenerationPublished += _ => runtime.Stop();
    runtime.Game.GenerationPublished += _ =>
    {
        Assert.True(runtime.Game.TryUseActiveGeneration(
            static (_, generationId) => generationId,
            out ulong active));
        Assert.True(active > 0);
        reconciledObserver++;
    };
    runtime.Game.GenerationPublished += _ => successfulObserver++;
    runtime.Game.ObserverFaulted += _ => observerFaults++;

    runtime.ReplaceGeneration();

    Assert.Equal(ServerRuntimeState.Running, runtime.State);
    Assert.Equal(1, successfulObserver);
    Assert.Equal(1, reconciledObserver);
    Assert.Equal(2, observerFaults);
}

static void OwnerThreadBoundaryRejectsForeignHostControl()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    Assert.Throws<InvalidOperationException>(() =>
        Task.Run(() => runtime.Tick()).GetAwaiter().GetResult());
    Assert.Throws<InvalidOperationException>(() =>
        Task.Run(() => _ = runtime.State).GetAwaiter().GetResult());
    Assert.Throws<InvalidOperationException>(() =>
        Task.Run(() => runtime.Dispose()).GetAwaiter().GetResult());

    Assert.Equal(ServerRuntimeState.Running, runtime.State);
    runtime.Tick();
    Assert.Equal(1UL, runtime.SimulationTick);
}

static void InvalidElapsedRejectsTransactionallyWithoutFaulting()
{
    WorldClockOptions clock = new(maximumSimulationStepsPerAdvance: 4);
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition, clock);
    runtime.Initialize();
    runtime.Start();

    Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Tick(TimeSpan.FromTicks(-1)));
    Assert.Equal(ServerRuntimeState.Running, runtime.State);
    Assert.Equal(0UL, runtime.SimulationTick);

    Assert.Throws<InvalidOperationException>(() => runtime.Tick(TimeSpan.FromSeconds(1)));
    Assert.Equal(ServerRuntimeState.Running, runtime.State);
    Assert.Equal(0UL, runtime.SimulationTick);

    // The rejected one-second request did not poison the host accumulator.
    Assert.Equal(1, runtime.Tick(TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 30) / 60)));
    Assert.Equal(1UL, runtime.SimulationTick);
}

static void FailedReplacementLeavesActiveGenerationUnchanged()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    ServerGeneration active = runtime.CurrentGeneration;
    WorldId activeWorld = active.EcsWorld.Id;
    composition.FailNextConfiguration = true;

    Assert.Throws<InvalidOperationException>(() => runtime.ReplaceGeneration());

    Assert.Equal(ServerRuntimeState.Running, runtime.State);
    Assert.True(ReferenceEquals(active, runtime.CurrentGeneration));
    Assert.Equal(activeWorld, runtime.CurrentGeneration.EcsWorld.Id);
    Assert.False(active.IsDisposed);
    Assert.True(composition.LastConfiguredWorld is not null);
    Assert.True(composition.LastConfiguredWorld!.IsDisposed);
}

static void SuccessfulReplacementPublishesOneCoherentRoot()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    ServerGeneration oldGeneration = runtime.CurrentGeneration;
    TickCounter oldCounter = composition.GetCounter(oldGeneration.GenerationId);
    runtime.Tick();
    WorldClockSnapshot oldClock = oldGeneration.Clock.CaptureSnapshot();
    Entity oldEntity = CreateEntity(oldGeneration, composition);
    bool oldWasAliveDuringPublication = false;
    runtime.OptionsForTesting.PublicationBoundaryHook = (candidate, previous) =>
    {
        oldWasAliveDuringPublication = previous is not null &&
            !previous.IsDisposed &&
            previous.EcsWorld.IsAlive(oldEntity) &&
            ReferenceEquals(candidate, runtime.CurrentGeneration);
    };

    runtime.ReplaceGeneration();
    ServerGeneration replacement = runtime.CurrentGeneration;

    Assert.False(ReferenceEquals(oldGeneration, replacement));
    Assert.False(oldGeneration.EcsWorld.Id == replacement.EcsWorld.Id);
    Assert.True(oldWasAliveDuringPublication);
    Assert.True(oldGeneration.IsDisposed);
    Assert.True(oldGeneration.EcsWorld.IsDisposed);
    Assert.False(replacement.IsDisposed);
    Assert.False(replacement.EcsWorld.IsAlive(oldEntity));
    Assert.Equal(2UL, replacement.GenerationId);
    TickCounter replacementCounter = composition.GetCounter(replacement.GenerationId);
    Assert.False(ReferenceEquals(oldCounter, replacementCounter));
    Assert.Equal(1, oldCounter.Value);
    Assert.Equal(1UL, oldClock.SimulationTick);
    Assert.Equal(0UL, replacement.Clock.SimulationTick);
    runtime.Tick();
    Assert.Equal(1, oldCounter.Value);
    Assert.Equal(1, replacementCounter.Value);
}

static void PublicationFailureIsTerminalAfterRootSwap()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    ServerGeneration oldGeneration = runtime.CurrentGeneration;
    ServerGeneration? publishedCandidate = null;
    runtime.OptionsForTesting.PublicationBoundaryHook = (candidate, _) =>
    {
        publishedCandidate = candidate;
        throw new InvalidOperationException("injected publication failure");
    };

    InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => runtime.ReplaceGeneration());

    Assert.Equal("injected publication failure", failure.Message);
    Assert.Equal(ServerRuntimeState.Faulted, runtime.State);
    Assert.True(publishedCandidate is not null);
    Assert.True(ReferenceEquals(publishedCandidate, runtime.CurrentGeneration));
    Assert.True(publishedCandidate!.IsPublished);
    Assert.False(publishedCandidate.IsDisposed);
    Assert.True(oldGeneration.IsDisposed);
    Assert.Throws<InvalidOperationException>(() => runtime.Tick());
}

static void GenerationOwnedGameStatePublishesAndRetiresAtomically()
{
    var composition = new GameStateComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();

    ServerGeneration oldGeneration = runtime.CurrentGeneration;
    var oldState = (GenerationStateProbe)oldGeneration.GameState!;
    Assert.True(runtime.Game.TryUseActiveGeneration<GenerationStateProbe, bool>(
        (world, generationId, state) =>
            ReferenceEquals(world, oldGeneration.EcsWorld) &&
            generationId == oldGeneration.GenerationId &&
            ReferenceEquals(state, oldState),
        out bool coherent));
    Assert.True(coherent);

    composition.FailAfterState = true;
    Assert.Throws<InvalidOperationException>(() => runtime.ReplaceGeneration());
    Assert.True(ReferenceEquals(oldGeneration, runtime.CurrentGeneration));
    Assert.False(oldState.IsDisposed);
    Assert.True(composition.LastCreatedState!.IsDisposed);

    bool observedOneRoot = false;
    runtime.OptionsForTesting.PublicationBoundaryHook = (candidate, previous) =>
    {
        observedOneRoot =
            ReferenceEquals(candidate, runtime.CurrentGeneration) &&
            candidate.GameState is GenerationStateProbe candidateState &&
            candidateState.GenerationId == candidate.GenerationId &&
            !candidateState.IsDisposed &&
            ReferenceEquals(previous, oldGeneration) &&
            !oldState.IsDisposed;
    };

    runtime.ReplaceGeneration();

    Assert.True(observedOneRoot);
    Assert.True(oldGeneration.IsDisposed);
    Assert.True(oldState.IsDisposed);
    var replacementState = runtime.CurrentGeneration.GameState as GenerationStateProbe ??
        throw new InvalidOperationException("Replacement generation has no game state.");
    Assert.False(replacementState.IsDisposed);
}

static void FixedWorldClockIsDeterministicAndSnapshotRestores()
{
    WorldClockOptions options = new(simulationFrequencyHz: 60, worldTimeRate: 24);
    var first = new TestComposition();
    using var runtime = CreateRuntime(first, options);
    runtime.Initialize();
    runtime.Start();
    for (int index = 0; index < 216_000; index++)
        runtime.Tick();

    WorldClockSnapshot snapshot = runtime.CurrentGeneration.Clock.CaptureSnapshot();
    Assert.Equal(216_000UL, snapshot.SimulationTick);
    Assert.Equal(24L * 60L * 60L * WorldClock.WorldTimeUnitsPerSecond,
        snapshot.WorldTimeMicroseconds);

    var restoredClock = new WorldClockForTest(options);
    restoredClock.Restore(snapshot);
    Assert.Equal(snapshot.SimulationTick, restoredClock.Snapshot.SimulationTick);
    Assert.Equal(snapshot.WorldTimeMicroseconds, restoredClock.Snapshot.WorldTimeMicroseconds);
    Assert.Throws<ArgumentException>(() => restoredClock.Restore(new WorldClockSnapshot(
        simulationFrequencyHz: 30, worldTimeRate: 24, simulationTick: 1,
        worldTimeMicroseconds: 1, worldTimeRemainder: 0, playbackRate: 1)));
    Assert.Throws<ArgumentException>(() => restoredClock.Restore(new WorldClockSnapshot(
        simulationFrequencyHz: 60, worldTimeRate: 24, simulationTick: 1,
        worldTimeMicroseconds: 1, worldTimeRemainder: 60, playbackRate: 1)));

    var persistent = new PersistentWorldClockState(
        simulationFrequencyHz: 60,
        worldTimeRate: 24,
        simulationTick: 17,
        worldTimeMicroseconds: 6_800_001,
        worldTimeRemainder: 37);
    restoredClock.RestorePersistent(persistent);
    Assert.Equal(persistent, restoredClock.PersistentState);
    Assert.Equal(WorldClockOptions.DefaultPlaybackRate, restoredClock.Snapshot.PlaybackRate);
}

static void PlaybackChangesHostReachTimeNotTickMeaning()
{
    WorldClockOptions options = new(simulationFrequencyHz: 60, worldTimeRate: 24,
        maximumSimulationStepsPerAdvance: 1_000_000);
    var first = new TestComposition();
    var second = new TestComposition();
    using var normal = CreateRuntime(first, options, allowVariablePlayback: true);
    using var fast = CreateRuntime(second, options, allowVariablePlayback: true);
    normal.Initialize();
    normal.Start();
    fast.Initialize();
    fast.Start();
    fast.SetPlaybackRate(4);

    Assert.Equal(60_000, normal.Tick(TimeSpan.FromSeconds(1_000)));
    Assert.Equal(60_000, fast.Tick(TimeSpan.FromSeconds(250)));
    WorldClockSnapshot normalSnapshot = normal.CurrentGeneration.Clock.CaptureSnapshot();
    WorldClockSnapshot fastSnapshot = fast.CurrentGeneration.Clock.CaptureSnapshot();

    Assert.Equal(normalSnapshot.SimulationTick, fastSnapshot.SimulationTick);
    Assert.Equal(normalSnapshot.WorldTimeMicroseconds, fastSnapshot.WorldTimeMicroseconds);
    Assert.Equal(normalSnapshot.SimulationTick,
        first.GetCounter(normal.CurrentGeneration.GenerationId).ValueAsTickCount);
    Assert.Equal(fastSnapshot.SimulationTick,
        second.GetCounter(fast.CurrentGeneration.GenerationId).ValueAsTickCount);
}

static void WarmedRuntimeTickAllocatesZeroManagedBytes()
{
    var composition = new TestComposition();
    using var runtime = CreateRuntime(composition);
    runtime.Initialize();
    runtime.Start();
    for (int index = 0; index < 128; index++)
        runtime.Tick();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 2_000; index++)
        runtime.Tick();
    Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
}

static void RuntimeWrapperBenchmarkIsCaptured()
{
    const int samples = 21;
    const int ticksPerSample = 2_000;
    var runtimeComposition = new TestComposition();
    var directComposition = new TestComposition();
    runtimeComposition.RangeCount = 1_024;
    directComposition.RangeCount = 1_024;
    using var runtime = CreateRuntime(runtimeComposition);
    using EcsWorld directWorld = CreateDirectWorld(directComposition);
    runtime.Initialize();
    runtime.Start();

    for (int index = 0; index < 4_096; index++)
    {
        runtime.Tick();
        directWorld.Tick(1d / 60d);
    }

    var runtimeSamples = new double[samples];
    var directSamples = new double[samples];
    for (int sample = 0; sample < samples; sample++)
    {
        if ((sample & 1) == 0)
        {
            runtimeSamples[sample] = MeasureRuntimeTicks(runtime, ticksPerSample);
            directSamples[sample] = MeasureDirectTicks(directWorld, ticksPerSample);
        }
        else
        {
            directSamples[sample] = MeasureDirectTicks(directWorld, ticksPerSample);
            runtimeSamples[sample] = MeasureRuntimeTicks(runtime, ticksPerSample);
        }
    }

    double runtimeMean = runtimeSamples.Average();
    double directMean = directSamples.Average();
    double ratio = runtimeMean / Math.Max(1d, directMean);
    double deltaPerTick = (runtimeMean - directMean) / ticksPerSample;

    Assert.Equal(
        directComposition.GetCounter(0).Value,
        runtimeComposition.GetCounter(runtime.CurrentGeneration.GenerationId).Value);
    Assert.Equal(directWorld.TickCount, runtime.SimulationTick);

    Console.WriteLine(
        $"BASELINE S0.1 samples={samples} ticks_per_sample={ticksPerSample} " +
        $"direct_mean={directMean:F0}ns direct_p95={Percentile95(directSamples):F0}ns " +
        $"runtime_mean={runtimeMean:F0}ns runtime_p95={Percentile95(runtimeSamples):F0}ns " +
        $"wrapper_ratio={ratio:F3} delta_per_tick={deltaPerTick:F2}ns");
    Assert.True(runtimeMean > 0d);
    Assert.True(directMean > 0d);
}

static double MeasureRuntimeTicks(ServerRuntime runtime, int count)
{
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++) runtime.Tick();
    return Stopwatch.GetElapsedTime(started).TotalNanoseconds;
}

static double MeasureDirectTicks(EcsWorld world, int count)
{
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++) world.Tick(1d / 60d);
    return Stopwatch.GetElapsedTime(started).TotalNanoseconds;
}

static double Percentile95(double[] values)
{
    double[] sorted = (double[])values.Clone();
    Array.Sort(sorted);
    return sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
}

static ServerRuntime CreateRuntime(
    IServerComposition composition,
    WorldClockOptions? clockOptions = null,
    bool allowVariablePlayback = false)
{
    return new ServerRuntime(composition, new ServerRuntimeOptions(
        CreateWorldOptions(),
        clockOptions ?? WorldClockOptions.Default,
        allowVariablePlayback));
}

static EcsWorldOptions CreateWorldOptions() => new(
    new EntityCapacity(16),
    maximumArchetypes: 1,
    maximumPreparedPlans: 0,
    scheduler: new EcsSchedulerOptions(
        maximumSystems: 2,
        maximumDependencies: 2,
        maximumWorkItems: 4,
        maximumStructuralSegments: 4,
        backgroundWorkerCount: 0),
    maximumStructuralCommands: 0);

static EcsWorld CreateDirectWorld(TestComposition composition)
{
    var world = new EcsWorld(composition.Schema, CreateWorldOptions());
    composition.Archetype = world.RegisterArchetype(
        ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
    world.RegisterRangeSystem<TickSystem, TickJob>(
        new SystemDefinition("test.simulation.tick", SystemStage.WorldSim,
            executionPolicy: SystemExecutionPolicy.Serial),
        new TickSystem(composition.CreateCounter(0), composition.RangeCount));
    world.Seal();
    world.Initialize();
    world.BeginRun();
    return world;
}

static Entity CreateEntity(ServerGeneration generation, TestComposition composition)
{
    Assert.True(generation.EcsWorld.TryCreate(composition.Archetype, out Entity entity));
    return entity;
}

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

sealed class TestComposition : IServerComposition
{
    private readonly Dictionary<ulong, TickCounter> _counters = new();

    public TestComposition()
    {
        Schema = ComponentSchema.Create(new SchemaId(0x51, 0x01), ReadOnlySpan<ComponentRegistration>.Empty);
    }

    public ComponentSchema Schema { get; }

    public bool FailNextConfiguration { get; set; }

    public EcsWorld? LastConfiguredWorld { get; private set; }

    public ArchetypeHandle Archetype { get; set; }

    public int RangeCount { get; set; } = 1;

    public TickCounter CreateCounter(ulong generationId)
    {
        var counter = new TickCounter();
        _counters.Add(generationId, counter);
        return counter;
    }

    public TickCounter GetCounter(ulong generationId) => _counters[generationId];

    public void Configure(ServerGenerationBuilder generation)
    {
        LastConfiguredWorld = generation.World;
        if (FailNextConfiguration)
        {
            FailNextConfiguration = false;
            throw new InvalidOperationException("injected configuration failure");
        }

        Archetype = generation.World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);

        generation.World.RegisterRangeSystem<TickSystem, TickJob>(
            new SystemDefinition("test.simulation.tick", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial),
            new TickSystem(CreateCounter(generation.GenerationId), RangeCount));
    }
}

sealed class EmptyComposition : IServerComposition
{
    public ComponentSchema Schema { get; } = ComponentSchema.Create(
        new SchemaId(0x51, 0x02), ReadOnlySpan<ComponentRegistration>.Empty);

    public void Configure(ServerGenerationBuilder generation)
    {
        _ = generation.World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
    }
}

sealed class GameStateComposition : IServerComposition
{
    public ComponentSchema Schema { get; } = ComponentSchema.Create(
        new SchemaId(0x51, 0x03), ReadOnlySpan<ComponentRegistration>.Empty);

    public bool FailAfterState { get; set; }

    public GenerationStateProbe? LastCreatedState { get; private set; }

    public void Configure(ServerGenerationBuilder generation)
    {
        LastCreatedState = new GenerationStateProbe(generation.GenerationId);
        generation.SetGameState(LastCreatedState);
        if (FailAfterState)
        {
            FailAfterState = false;
            throw new InvalidOperationException("injected failure after game-state construction");
        }
        _ = generation.World.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
    }
}

sealed class GenerationStateProbe : IDisposable
{
    public GenerationStateProbe(ulong generationId) => GenerationId = generationId;

    public ulong GenerationId { get; }

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

sealed class TickCounter
{
    public int Value { get; set; }

    public ulong LastTick { get; set; }

    public ulong ValueAsTickCount => checked((ulong)Value);
}

sealed class TickSystem : IRangeSystemRuntime<TickJob>
{
    private readonly TickCounter _counter;
    private readonly int _rangeCount;

    public TickSystem(TickCounter counter, int rangeCount)
    {
        _counter = counter;
        _rangeCount = rangeCount;
    }

    public int GetRangeCount(in SystemPrepareContext context) => _rangeCount;

    public TickJob Prepare(in SystemPrepareContext context) => new(_counter);
}

struct TickJob : IRangeJob
{
    private readonly TickCounter _counter;

    public TickJob(TickCounter counter) => _counter = counter;

    public void Execute(in SystemJobContext context, int start, int count)
    {
        for (int index = 0; index < count; index++)
            _counter.Value++;
        _counter.LastTick = context.Tick;
    }
}

sealed class WorldClockForTest
{
    private readonly WorldClock _clock;

    public WorldClockForTest(WorldClockOptions options)
    {
        _clock = new WorldClock(options);
    }

    public WorldClockSnapshot Snapshot => _clock.CaptureSnapshot();

    public PersistentWorldClockState PersistentState => _clock.CapturePersistentState();

    public void Restore(WorldClockSnapshot snapshot) => _clock.RestoreSnapshot(snapshot);

    public void RestorePersistent(PersistentWorldClockState state) => _clock.RestorePersistentState(state);
}
