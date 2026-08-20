using System.Diagnostics;
using System.Runtime.InteropServices;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.ECS.SIMD;
using UltraSim.ECS.Systems;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OldECS.Tests;

public sealed class MovementBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public MovementBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void OptimizedMovement_100k_50Ticks()
    {
        var result = RunMovementBenchmark(100_000, 10, 50);
        _output.WriteLine(
            $"BENCHMARK|version=old|mode=parallel-ish|test=movement_only|entities={result.EntityCount}|ticks={result.MeasureTicks}|total_ms={result.TotalMilliseconds:F3}|tick_ms={result.TickMilliseconds:F3}");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void OptimizedPulsingAndMovement_100k_50Ticks()
    {
        var result = RunPulsingBenchmark(100_000, 10, 50);
        _output.WriteLine(
            $"BENCHMARK|version=old|mode=parallel-ish|test=pulsing_movement|entities={result.EntityCount}|ticks={result.MeasureTicks}|total_ms={result.TotalMilliseconds:F3}|tick_ms={result.TickMilliseconds:F3}");
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(250_000)]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void OptimizedMovement_Scaling(int entityCount)
    {
        var result = RunMovementBenchmark(entityCount, 5, 20);
        _output.WriteLine(
            $"BENCHMARK|version=old|profile=scaling|mode=parallel-ish|test=movement_only|entities={result.EntityCount}|ticks={result.MeasureTicks}|total_ms={result.TotalMilliseconds:F3}|tick_ms={result.TickMilliseconds:F3}");
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(250_000)]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void OptimizedPulsingAndMovement_Scaling(int entityCount)
    {
        var result = RunPulsingBenchmark(entityCount, 5, 20);
        _output.WriteLine(
            $"BENCHMARK|version=old|profile=scaling|mode=parallel-ish|test=pulsing_movement|entities={result.EntityCount}|ticks={result.MeasureTicks}|total_ms={result.TotalMilliseconds:F3}|tick_ms={result.TickMilliseconds:F3}");
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(250_000)]
    [InlineData(500_000)]
    public void MixedSystemsAndStructuralChurn_Scaling(int entityCount)
    {
        var result = RunBroadWorkloadBenchmark(entityCount, 10, 50);
        _output.WriteLine(
            $"BENCHMARK|version=old|profile=broad|mode=deferred|test=system_workload|entities={result.EntityCount}|ticks={result.MeasureTicks}|total_ms={result.TotalMilliseconds:F3}|tick_ms={result.TickMilliseconds:F3}|spawned={result.Spawned}|destroyed={result.Destroyed}|migrated={result.Migrated}");
    }

    private static BenchmarkResult RunMovementBenchmark(int entityCount, int warmupTicks, int measureTicks)
    {
        Logging.Clear();
        var host = new BenchmarkHost();
        Logging.Host = host;
        SimdManager.Initialize(host.Runtime.Environment.SimdSupport);

        var world = new World(host);
        world.Initialize();
        world.EnqueueSystemCreate<BenchMovementSystem>();
        world.EnqueueSystemEnable<BenchMovementSystem>();

        for (int i = 0; i < entityCount; i++)
        {
            var builder = world.CreateEntityBuilder()
                .Add(new Position(i, i * 0.5f, 0f))
                .Add(new Velocity(1f, -0.5f, 0f));
            world.EnqueueCreateEntity(builder);
        }

        for (int i = 0; i < warmupTicks; i++)
            world.Tick(0.016f);

        var movement = world.Systems.GetSystem<BenchMovementSystem>() as BenchMovementSystem;
        Assert.NotNull(movement);
        Assert.True(movement!.UpdateCalls > 0);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureTicks; i++)
            world.Tick(0.016f);
        sw.Stop();

        Assert.True(world.EntityCount >= entityCount);

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double tickMs = totalMs / measureTicks;
        return new BenchmarkResult(entityCount, measureTicks, totalMs, tickMs);
    }

    private static BenchmarkResult RunPulsingBenchmark(int entityCount, int warmupTicks, int measureTicks)
    {
        Logging.Clear();
        var host = new BenchmarkHost();
        Logging.Host = host;
        SimdManager.Initialize(host.Runtime.Environment.SimdSupport);

        var world = new World(host);
        world.Initialize();
        world.EnqueueSystemCreate<OptimizedPulsingMovementSystem>();
        world.EnqueueSystemCreate<OptimizedMovementSystem>();
        world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();
        world.EnqueueSystemEnable<OptimizedMovementSystem>();

        for (int i = 0; i < entityCount; i++)
        {
            var builder = world.CreateEntityBuilder()
                .Add(new Position((i % 1024) - 512f, ((i / 1024) % 1024) - 512f, (i % 257) - 128f))
                .Add(new Velocity(0f, 0f, 0f))
                .Add(new PulseData(
                    speed: 2f + (i % 5) * 0.1f,
                    frequency: 0.5f + (i % 7) * 0.05f,
                    phase: (i % 360) * (MathF.PI / 180f)));
            world.EnqueueCreateEntity(builder);
        }

        for (int i = 0; i < warmupTicks; i++)
            world.Tick(0.016f);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measureTicks; i++)
            world.Tick(0.016f);
        sw.Stop();

        Assert.True(world.EntityCount >= entityCount);

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double tickMs = totalMs / measureTicks;
        return new BenchmarkResult(entityCount, measureTicks, totalMs, tickMs);
    }

    private static BroadWorkloadResult RunBroadWorkloadBenchmark(int entityCount, int warmupTicks, int measureTicks)
    {
        Logging.Clear();
        var host = new BenchmarkHost();
        Logging.Host = host;
        SimdManager.Initialize(host.Runtime.Environment.SimdSupport);

        var world = new World(host);
        world.Initialize();
        world.EnqueueSystemCreate<OptimizedPulsingMovementSystem>();
        world.EnqueueSystemCreate<OptimizedMovementSystem>();
        world.EnqueueSystemEnable<OptimizedPulsingMovementSystem>();
        world.EnqueueSystemEnable<OptimizedMovementSystem>();

        int buffTypeId = ComponentManager.GetTypeId<BenchBroadBuffOld>();

        for (int i = 0; i < entityCount; i++)
            EnqueuePulseSpawn(world, i);

        world.Tick(0.016f);

        int churnPerTick = Math.Max(64, entityCount / 2000);
        int mutatePerTick = Math.Max(128, entityCount / 1000);
        int snapshotCapacity = entityCount + Math.Max(churnPerTick + mutatePerTick, 1024);

        var snapshot = new Entity[snapshotCapacity];
        var destroyBuffer = new Entity[churnPerTick];
        var mutateBuffer = new Entity[mutatePerTick];
        bool addPhase = true;

        int spawned = 0;
        int destroyed = 0;
        int migrated = 0;

        RunTicks(warmupTicks, measure: false);

        var sw = Stopwatch.StartNew();
        RunTicks(measureTicks, measure: true);
        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        return new BroadWorkloadResult(entityCount, measureTicks, totalMs, totalMs / measureTicks, spawned, destroyed, migrated);

        void RunTicks(int ticks, bool measure)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                int liveCount = CaptureEntities(world, snapshot);
                int destroyCount = Math.Min(churnPerTick, liveCount);
                int mutateCount = Math.Min(mutatePerTick, Math.Max(0, liveCount - destroyCount));

                if (destroyCount > 0)
                {
                    snapshot.AsSpan(0, destroyCount).CopyTo(destroyBuffer);
                    for (int i = 0; i < destroyCount; i++)
                        world.EnqueueDestroyEntity(destroyBuffer[i]);

                    for (int i = 0; i < destroyCount; i++)
                        EnqueuePulseSpawn(world, tick + i);

                    if (measure)
                    {
                        spawned += destroyCount;
                        destroyed += destroyCount;
                    }
                }

                if (mutateCount > 0)
                {
                    snapshot.AsSpan(destroyCount, mutateCount).CopyTo(mutateBuffer);
                    for (int i = 0; i < mutateCount; i++)
                    {
                        if (addPhase)
                            world.EnqueueComponentAdd(mutateBuffer[i], buffTypeId, new BenchBroadBuffOld { Stacks = 1 });
                        else
                            world.EnqueueComponentRemove(mutateBuffer[i], buffTypeId);
                    }

                    if (measure)
                        migrated += mutateCount;

                    addPhase = !addPhase;
                }

                world.Tick(0.016f);
            }
        }
    }

    private static void EnqueuePulseSpawn(World world, int seed)
    {
        var builder = world.CreateEntityBuilder()
            .Add(new Position((seed % 1024) - 512f, ((seed / 1024) % 1024) - 512f, (seed % 257) - 128f))
            .Add(new Velocity(1.5f, -0.7f, 0.2f))
            .Add(new PulseData(
                speed: 2f + (seed % 5) * 0.1f,
                frequency: 0.5f + (seed % 7) * 0.05f,
                phase: (seed % 360) * (MathF.PI / 180f)));

        world.EnqueueCreateEntity(builder);
    }

    private static int CaptureEntities(World world, Span<Entity> destination)
    {
        int written = 0;
        using var archetypes = world.QueryArchetypes(typeof(Position), typeof(Velocity));
        foreach (var archetype in archetypes)
        {
            var entities = archetype.GetEntitySpan();
            int copy = Math.Min(entities.Length, destination.Length - written);
            if (copy <= 0)
                break;

            entities.Slice(0, copy).CopyTo(destination.Slice(written, copy));
            written += copy;
        }

        return written;
    }

    private readonly record struct BenchmarkResult(int EntityCount, int MeasureTicks, double TotalMilliseconds, double TickMilliseconds);

    private readonly record struct BroadWorkloadResult(
        int EntityCount,
        int MeasureTicks,
        double TotalMilliseconds,
        double TickMilliseconds,
        int Spawned,
        int Destroyed,
        int Migrated);

    public sealed class BenchMovementSystem : BaseSystem
    {
        private static readonly int PositionId = ComponentManager.GetTypeId<Position>();
        private static readonly int VelocityId = ComponentManager.GetTypeId<Velocity>();
        private static readonly Type[] ReadTypes = { typeof(Position), typeof(Velocity) };

        public int UpdateCalls;

        public override int SystemId => typeof(BenchMovementSystem).GetHashCode();
        public override string Name => nameof(BenchMovementSystem);
        public override Type[] ReadSet => ReadTypes;
        public override Type[] WriteSet => ReadTypes;

        public override void Update(World world, double delta)
        {
            UpdateCalls++;

            using var archetypes = world.QueryArchetypes(typeof(Position), typeof(Velocity));
            float dt = (float)delta;
            foreach (var archetype in archetypes)
            {
                var pos = archetype.GetComponentListTyped<Position>(PositionId);
                var vel = archetype.GetComponentListTyped<Velocity>(VelocityId);
                if (pos == null || vel == null)
                    continue;

                var posList = pos.GetList();
                var velList = vel.GetList();
                int count = Math.Min(posList.Count, velList.Count);
                var posSpan = CollectionsMarshal.AsSpan(posList);
                var velSpan = CollectionsMarshal.AsSpan(velList);
                for (int i = 0; i < count; i++)
                {
                    var p = posSpan[i];
                    var v = velSpan[i];
                    p.X += v.X * dt;
                    p.Y += v.Y * dt;
                    p.Z += v.Z * dt;
                    posSpan[i] = p;
                }
            }
        }
    }

    private sealed class BenchmarkHost : IHost
    {
        public RuntimeContext Runtime { get; } = new(HostEnvironment.Capture(), "OldEcsPerf");

        public EnvironmentType Environment => EnvironmentType.Server;

        public object? GetRootHandle() => null;

        public void Log(LogEntry entry)
        {
            _ = entry;
        }
    }
}

internal struct BenchBroadBuffOld
{
    public byte Stacks;
}
