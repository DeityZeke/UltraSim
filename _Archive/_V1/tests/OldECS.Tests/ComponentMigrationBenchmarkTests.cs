using System.Diagnostics;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using Xunit.Abstractions;

namespace OldECS.Tests;

public sealed class ComponentMigrationBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public ComponentMigrationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(250_000)]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void AddRemoveMigration_CacheWarmup_Scaling(int entityCount)
    {
        var result = RunBenchmark(entityCount);

        _output.WriteLine(
            $"BENCHMARK|version=old|profile=scaling|mode=deferred|test=structural_migration|entities={entityCount}" +
            $"|add_cold_enqueue_ms={result.AddCold.EnqueueMs:F3}|add_cold_apply_ms={result.AddCold.ApplyMs:F3}|add_cold_total_ms={result.AddCold.TotalMs:F3}" +
            $"|remove_cold_enqueue_ms={result.RemoveCold.EnqueueMs:F3}|remove_cold_apply_ms={result.RemoveCold.ApplyMs:F3}|remove_cold_total_ms={result.RemoveCold.TotalMs:F3}" +
            $"|add_warm_enqueue_ms={result.AddWarm.EnqueueMs:F3}|add_warm_apply_ms={result.AddWarm.ApplyMs:F3}|add_warm_total_ms={result.AddWarm.TotalMs:F3}" +
            $"|remove_warm_enqueue_ms={result.RemoveWarm.EnqueueMs:F3}|remove_warm_apply_ms={result.RemoveWarm.ApplyMs:F3}|remove_warm_total_ms={result.RemoveWarm.TotalMs:F3}");
    }

    private static Result RunBenchmark(int entityCount)
    {
        Logging.Clear();
        var host = new BenchmarkHost();
        Logging.Host = host;

        var world = new World(host);
        world.Initialize();

        int extraTypeId = ComponentManager.GetTypeId<BenchExtraOld>();

        for (int i = 0; i < entityCount; i++)
        {
            var builder = world.CreateEntityBuilder()
                .Add(new Position(i, i * 0.5f, 0f))
                .Add(new Velocity(1f, -0.5f, 0f));
            world.EnqueueCreateEntity(builder);
        }

        world.Tick(0.016);

        var entities = new Entity[entityCount];
        int read = 0;
        using (var archetypes = world.QueryArchetypes(typeof(Position), typeof(Velocity)))
        {
            foreach (var archetype in archetypes)
            {
                var span = archetype.GetEntitySpan();
                span.CopyTo(entities.AsSpan(read));
                read += span.Length;
            }
        }

        if (read != entityCount)
            throw new InvalidOperationException($"Entity capture mismatch. expected={entityCount}, read={read}");

        var value = new BenchExtraOld { Value = 7 };

        var addCold = MeasureAdd(world, entities, extraTypeId, value);
        var removeCold = MeasureRemove(world, entities, extraTypeId);
        var addWarm = MeasureAdd(world, entities, extraTypeId, value);
        var removeWarm = MeasureRemove(world, entities, extraTypeId);

        return new Result(addCold, removeCold, addWarm, removeWarm);
    }

    private static PhaseTiming MeasureAdd(World world, Entity[] entities, int extraTypeId, in BenchExtraOld value)
    {
        var swEnqueue = Stopwatch.StartNew();
        for (int i = 0; i < entities.Length; i++)
            world.EnqueueComponentAdd(entities[i], extraTypeId, value);
        swEnqueue.Stop();

        var swApply = Stopwatch.StartNew();
        world.Tick(0.016);
        swApply.Stop();

        return new PhaseTiming(swEnqueue.Elapsed.TotalMilliseconds, swApply.Elapsed.TotalMilliseconds);
    }

    private static PhaseTiming MeasureRemove(World world, Entity[] entities, int extraTypeId)
    {
        var swEnqueue = Stopwatch.StartNew();
        for (int i = 0; i < entities.Length; i++)
            world.EnqueueComponentRemove(entities[i], extraTypeId);
        swEnqueue.Stop();

        var swApply = Stopwatch.StartNew();
        world.Tick(0.016);
        swApply.Stop();

        return new PhaseTiming(swEnqueue.Elapsed.TotalMilliseconds, swApply.Elapsed.TotalMilliseconds);
    }

    private readonly record struct Result(PhaseTiming AddCold, PhaseTiming RemoveCold, PhaseTiming AddWarm, PhaseTiming RemoveWarm);

    private readonly record struct PhaseTiming(double EnqueueMs, double ApplyMs)
    {
        public double TotalMs => EnqueueMs + ApplyMs;
    }

    private sealed class BenchmarkHost : IHost
    {
        public RuntimeContext Runtime { get; } = new(HostEnvironment.Capture(), "OldEcsMigrationPerf");

        public EnvironmentType Environment => EnvironmentType.Server;

        public object? GetRootHandle() => null;

        public void Log(LogEntry entry)
        {
            _ = entry;
        }
    }
}

internal struct BenchExtraOld
{
    public int Value;
}
