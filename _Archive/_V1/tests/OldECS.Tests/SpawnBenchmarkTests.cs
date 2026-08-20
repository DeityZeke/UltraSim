using System.Diagnostics;
using System.Threading;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using Xunit.Abstractions;

namespace OldECS.Tests;

public sealed class SpawnBenchmarkTests
{
    private static int _warmupDone;
    private readonly ITestOutputHelper _output;

    public SpawnBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(250_000)]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void EnqueueCreateEntityBuilder_Scaling(int entityCount)
    {
        var result = RunSpawnBenchmark(entityCount);

        Assert.True(result.EntityCount >= entityCount);
        _output.WriteLine(
            $"BENCHMARK|version=old|profile=scaling|mode=deferred|test=spawn_enqueue_builder|entities={result.InputCount}|enqueue_ms={result.EnqueueMilliseconds:F3}|apply_ms={result.ApplyMilliseconds:F3}|total_ms={result.TotalMilliseconds:F3}|entity_us={result.MicrosecondsPerEntity:F3}");
    }

    private static SpawnBenchmarkResult RunSpawnBenchmark(int entityCount)
    {
        WarmupOnce();
        Logging.Clear();
        var host = new SpawnBenchmarkHost();
        Logging.Host = host;

        var world = new World(host);
        world.Initialize();

        var enqueueSw = Stopwatch.StartNew();
        for (int i = 0; i < entityCount; i++)
        {
            var builder = world.CreateEntityBuilder()
                .Add(new Position(i, i * 0.5f, 0f))
                .Add(new Velocity(i & 7, (i & 3) - 1f, 0f));

            world.EnqueueCreateEntity(builder);
        }
        enqueueSw.Stop();

        var applySw = Stopwatch.StartNew();
        world.Tick(0d);
        applySw.Stop();

        return new SpawnBenchmarkResult(
            InputCount: entityCount,
            EntityCount: world.EntityCount,
            EnqueueMilliseconds: enqueueSw.Elapsed.TotalMilliseconds,
            ApplyMilliseconds: applySw.Elapsed.TotalMilliseconds);
    }

    private static void WarmupOnce()
    {
        if (Interlocked.Exchange(ref _warmupDone, 1) != 0)
            return;

        Logging.Clear();
        var host = new SpawnBenchmarkHost();
        Logging.Host = host;

        var world = new World(host);
        world.Initialize();
        world.EnqueueCreateEntity(
            world.CreateEntityBuilder()
                .Add(new Position(1f, 2f, 3f))
                .Add(new Velocity(1f, 0f, 0f)));
        world.Tick(0d);
    }

    private readonly record struct SpawnBenchmarkResult(
        int InputCount,
        int EntityCount,
        double EnqueueMilliseconds,
        double ApplyMilliseconds)
    {
        public double TotalMilliseconds => EnqueueMilliseconds + ApplyMilliseconds;
        public double MicrosecondsPerEntity => InputCount <= 0 ? 0d : (TotalMilliseconds * 1000d) / InputCount;
    }

    private sealed class SpawnBenchmarkHost : IHost
    {
        public RuntimeContext Runtime { get; } = new(HostEnvironment.Capture(), "OldEcsSpawnPerf");
        public EnvironmentType Environment => EnvironmentType.Server;
        public object? GetRootHandle() => null;
        public void Log(LogEntry entry) => _ = entry;
    }
}
