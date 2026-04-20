using System.Text;
using UltraSim.ECS;
using UltraSim.ECS.Benchmarks.Core;
using UltraSim.ECS.Persistence;

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "ECS Kernel Performance Suite",
    kernelName: "new",
    scenarios: NewKernelBenchmarks.CreateScenarios());

string outputPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results", "new-ecs-benchmarks.json");

MicroBenchmark.WriteReport(report, Path.GetFullPath(outputPath));
Console.WriteLine(NewKernelBenchmarks.FormatSummary(report, outputPath));

internal static class NewKernelBenchmarks
{
    private static readonly int[] CommonScales = [1_000, 10_000, 50_000];
    private static readonly ArchetypeSignature EmptySignature = new([]);

    public static IReadOnlyList<BenchmarkScenario> CreateScenarios()
    {
        var scenarios = new List<BenchmarkScenario>();

        scenarios.Add(new BenchmarkScenario
        {
            Name = "SchemaBuild_4Components",
            Category = "Bootstrap",
            Comparable = false,
            Scale = 4,
            Execute = BenchmarkSchemaBuild4,
        });

        foreach (int scale in CommonScales)
        {
            int capturedScale = scale;
            scenarios.Add(MakeScenario("CreateDefaultDeferred_Position", "Structural", true, capturedScale, () => BenchmarkCreateDefaultDeferred(capturedScale)));
            scenarios.Add(MakeScenario("SetComponent_Position", "Mutation", true, capturedScale, () => BenchmarkSetComponent(capturedScale)));
            scenarios.Add(MakeScenario("TryGetComponent_Position", "Read", true, capturedScale, () => BenchmarkTryGetComponent(capturedScale)));
            scenarios.Add(MakeScenario("Query1_Read_Position", "Query", true, capturedScale, () => BenchmarkQuery1(capturedScale)));
            scenarios.Add(MakeScenario("Query2_Read_PositionVelocity", "Query", true, capturedScale, () => BenchmarkQuery2(capturedScale)));
            scenarios.Add(MakeScenario("Query3_Read_PositionVelocityHealth", "Query", true, capturedScale, () => BenchmarkQuery3(capturedScale)));
            scenarios.Add(MakeScenario("DeferredAdd_Velocity", "Structural", true, capturedScale, () => BenchmarkDeferredAdd(capturedScale)));
            scenarios.Add(MakeScenario("DeferredRemove_Velocity", "Structural", true, capturedScale, () => BenchmarkDeferredRemove(capturedScale)));
            scenarios.Add(MakeScenario("DeferredDestroy", "Structural", true, capturedScale, () => BenchmarkDeferredDestroy(capturedScale)));
            scenarios.Add(MakeScenario("DeferredDestroy_SparseVictims", "Structural", true, capturedScale, () => BenchmarkDeferredDestroySparseVictims(capturedScale)));
            scenarios.Add(MakeScenario("MigrationCycle_AddRemoveVelocity", "Structural", true, capturedScale, () => BenchmarkMigrationCycleVelocity(capturedScale)));
            scenarios.Add(MakeScenario("CreateDestroyReuseChurn_Position", "Structural", true, capturedScale, () => BenchmarkCreateDestroyReuseChurn(capturedScale)));
            scenarios.Add(MakeScenario("DirtyDrain_Position", "DirtyTracking", true, capturedScale, () => BenchmarkDirtyDrain(capturedScale)));
            scenarios.Add(MakeScenario("PersistenceView_Filtered_Position", "PersistenceSeam", true, capturedScale, () => BenchmarkPersistenceView(capturedScale)));
            scenarios.Add(MakeScenario("CreateDefaultImmediate_Position", "Structural", false, capturedScale, () => BenchmarkCreateImmediate(capturedScale)));
            scenarios.Add(MakeScenario("PatchComponent_Position", "Mutation", false, capturedScale, () => BenchmarkPatchComponent(capturedScale)));
        }

        scenarios.Add(MakeScenario("TickSerial_QuerySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickSerial(10_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_QuerySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickParallel(10_000, 200)));
        scenarios.Add(MakeScenario("TickSerial_ReadOnlySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickSerialReadOnly(10_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_ReadOnlySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickParallelReadOnly(10_000, 200)));
        scenarios.Add(MakeScenario("TickSerial_DirtyAwareWriteSystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickSerialDirtyAware(10_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_DirtyAwareWriteSystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickParallelDirtyAware(10_000, 200)));
        scenarios.Add(MakeScenario("TickSerial_OscillatingMovement_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickSerialOscillatingMovement(10_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_OscillatingMovement_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickParallelOscillatingMovement(10_000, 200)));
        scenarios.Add(MakeScenario("TickSerial_OscillatingMovement_100k_200Ticks", "Scheduler", true, 100_000, () => BenchmarkTickSerialOscillatingMovement(100_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_OscillatingMovement_100k_200Ticks", "Scheduler", true, 100_000, () => BenchmarkTickParallelOscillatingMovement(100_000, 200)));
        scenarios.Add(MakeScenario("TickSerial_Active35_Shuffle10_OscillatingMovement_100k_200Ticks", "Scheduler", true, 100_000, () => BenchmarkTickSerialOscillatingMovement(100_000, 200, activeSubsetOnly: true)));
        scenarios.Add(MakeScenario("TickParallel_Active35_Shuffle10_OscillatingMovement_100k_200Ticks", "Scheduler", true, 100_000, () => BenchmarkTickParallelOscillatingMovement(100_000, 200, activeSubsetOnly: true)));
        scenarios.Add(MakeScenario("TickSerial_V1PulseMovement_100k_50Ticks", "Scheduler", true, 100_000, () => BenchmarkTickSerialV1PulseMovement(100_000, 50)));
        scenarios.Add(MakeScenario("TickParallel_V1PulseMovement_100k_50Ticks", "Scheduler", true, 100_000, () => BenchmarkTickParallelV1PulseMovement(100_000, 50)));
        scenarios.Add(MakeScenario("TickSerial_Active35_Shuffle10_V1PulseMovement_100k_200Ticks", "Scheduler", true, 100_000, () => BenchmarkTickSerialV1PulseMovement(100_000, 200, activeSubsetOnly: true)));
        scenarios.Add(MakeScenario("TickParallel_Active35_Shuffle10_V1PulseMovement_100k_200Ticks", "Scheduler", true, 100_000, () => BenchmarkTickParallelV1PulseMovement(100_000, 200, activeSubsetOnly: true)));
        scenarios.Add(CreateHotQuery1Scenario(50_000));
        scenarios.Add(CreateHotSetComponentScenario(50_000));
        scenarios.Add(CreateHotTryGetScenario(50_000));
        scenarios.Add(CreateHotDirtyDrainScenario(50_000));
        scenarios.Add(CreateHotDeferredAddScenario(50_000));
        scenarios.Add(CreateHotTickSerialScenario(10_000, 200));
        scenarios.Add(CreateHotTickParallelScenario(10_000, 200));
        scenarios.Add(CreateHotTickSerialReadOnlyScenario(10_000, 200));
        scenarios.Add(CreateHotTickParallelReadOnlyScenario(10_000, 200));
        scenarios.Add(CreateHotTickSerialDirtyAwareScenario(10_000, 200));
        scenarios.Add(CreateHotTickParallelDirtyAwareScenario(10_000, 200));
        scenarios.Add(CreateHotTickSerialOscillatingMovementScenario(10_000, 200));
        scenarios.Add(CreateHotTickParallelOscillatingMovementScenario(10_000, 200));
        scenarios.Add(CreateHotTickSerialOscillatingMovementScenario(100_000, 200));
        scenarios.Add(CreateHotTickParallelOscillatingMovementScenario(100_000, 200));
        return scenarios;
    }

    public static string FormatSummary(BenchmarkRunReport report, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Kernel: {report.KernelName}");
        sb.AppendLine($"Configuration: {report.Configuration}");
        sb.AppendLine($"ProcessorCount: {report.ProcessorCount}");
        sb.AppendLine($"Scenarios: {report.Results.Count}");
        sb.AppendLine($"Results: {Path.GetFullPath(outputPath)}");
        foreach (BenchmarkResult result in report.Results.Take(8))
            sb.AppendLine($"{result.Name} [{result.Scale}] mean={result.MeanMilliseconds:F3} ms ns/op={result.MeanNanosecondsPerOperation:F1} alloc={result.MeanAllocatedBytes}");
        return sb.ToString();
    }

    private static BenchmarkScenario MakeScenario(string name, string category, bool comparable, int scale, Func<ScenarioMeasurement> execute)
        => new()
        {
            Name = name,
            Category = category,
            Comparable = comparable,
            Scale = scale,
            Execute = execute,
        };

    private static BenchmarkScenario CreateHotQuery1Scenario(int scale)
    {
        HotReadFixture? fixture = null;
        return MakeScenario($"HotLoop_Query1_Read_Position", "HotLoop", true, scale, () =>
        {
            fixture ??= HotReadFixture.Create(scale);
            long checksum = 0;
            fixture.World.Query<Position>((Entity entity, in Position position) =>
            {
                checksum += entity.Index + position.X + position.Y;
            });

            return new ScenarioMeasurement(scale, checksum);
        });
    }

    private static BenchmarkScenario CreateHotSetComponentScenario(int scale)
    {
        HotMutationFixture? fixture = null;
        return MakeScenario($"HotLoop_SetComponent_Position", "HotLoop", true, scale, () =>
        {
            fixture ??= HotMutationFixture.Create(scale);
            long checksum = 0;
            Entity[] entities = fixture.Entities;
            World world = fixture.World;

            for (int i = 0; i < entities.Length; i++)
            {
                world.SetComponent(entities[i], new Position(i + fixture.Pass, i + fixture.Pass + 1));
                checksum += i;
            }

            fixture.Pass++;
            return new ScenarioMeasurement(entities.Length, checksum);
        });
    }

    private static BenchmarkScenario CreateHotTryGetScenario(int scale)
    {
        HotReadFixture? fixture = null;
        return MakeScenario($"HotLoop_TryGetComponent_Position", "HotLoop", true, scale, () =>
        {
            fixture ??= HotReadFixture.Create(scale);
            long checksum = 0;
            Entity[] entities = fixture.Entities;
            World world = fixture.World;

            for (int i = 0; i < entities.Length; i++)
            {
                if (world.TryGetComponent(entities[i], out Position position))
                    checksum += position.X + position.Y;
            }

            return new ScenarioMeasurement(entities.Length, checksum);
        });
    }

    private static BenchmarkScenario CreateHotDirtyDrainScenario(int scale)
    {
        HotMutationFixture? fixture = null;
        return MakeScenario($"HotLoop_DirtyDrain_Position", "HotLoop", true, scale, () =>
        {
            fixture ??= HotMutationFixture.Create(scale);
            long checksum = 0;
            Entity[] entities = fixture.Entities;
            World world = fixture.World;

            for (int i = 0; i < entities.Length; i++)
                world.SetComponent(entities[i], new Position(i + fixture.Pass + 3, i + fixture.Pass + 5));

            int drained = world.DrainDirtyTo<Position>(fixture.DirtyBuffer);
            for (int i = 0; i < drained; i++)
                checksum += fixture.DirtyBuffer[i].Index;

            fixture.Pass++;
            return new ScenarioMeasurement(entities.Length, checksum);
        });
    }

    private static BenchmarkScenario CreateHotDeferredAddScenario(int scale)
    {
        HotStructuralAddPool? pool = null;
        return MakeScenario($"HotLoop_DeferredAdd_Velocity", "HotLoop", true, scale, () =>
        {
            pool ??= HotStructuralAddPool.Create(scale, poolSize: 8);
            (World world, Entity[] entities) = pool.Next();

            for (int i = 0; i < entities.Length; i++)
                world.EnqueueAdd(entities[i], new Velocity(i, i + 1));

            world.ProcessStructuralQueues();

            long checksum = 0;
            for (int i = 0; i < entities.Length; i++)
                checksum += world.GetComponent<Velocity>(entities[i]).X;

            return new ScenarioMeasurement(entities.Length, checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickSerialScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickSerial_QuerySystems", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateSchedulerWorld(entityCount, SystemExecutionMode.Serial);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickParallelScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickParallel_QuerySystems", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateSchedulerWorld(entityCount, SystemExecutionMode.ParallelBatches);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickSerialReadOnlyScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickSerial_ReadOnlySystems", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateReadOnlySchedulerWorld(entityCount, SystemExecutionMode.Serial);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickParallelReadOnlyScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickParallel_ReadOnlySystems", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateReadOnlySchedulerWorld(entityCount, SystemExecutionMode.ParallelBatches);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickSerialDirtyAwareScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickSerial_DirtyAwareWriteSystems", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateSchedulerWorld(entityCount, SystemExecutionMode.Serial);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickParallelDirtyAwareScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickParallel_DirtyAwareWriteSystems", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateSchedulerWorld(entityCount, SystemExecutionMode.ParallelBatches);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickSerialOscillatingMovementScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickSerial_OscillatingMovement", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateOscillatingMovementWorld(entityCount, SystemExecutionMode.Serial);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static BenchmarkScenario CreateHotTickParallelOscillatingMovementScenario(int entityCount, int ticks)
    {
        World? world = null;
        return MakeScenario($"HotLoop_TickParallel_OscillatingMovement", "HotLoop", true, entityCount, () =>
        {
            world ??= CreateOscillatingMovementWorld(entityCount, SystemExecutionMode.ParallelBatches);
            SchedulerBenchmarkState.Reset();
            for (int i = 0; i < ticks; i++)
                world.Tick(0.016f);

            return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
        });
    }

    private static ScenarioMeasurement BenchmarkSchemaBuild4()
    {
        var builder = new EcsSchemaBuilder();
        builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        builder.RegisterComponent<Health>();
        builder.RegisterComponent<Active>();
        EcsSchema schema = builder.Build();
        return new ScenarioMeasurement(schema.ComponentCount, schema.ComponentCount);
    }

    private static ScenarioMeasurement BenchmarkCreateDefaultDeferred(int scale)
    {
        World world = CreateWorld(includeVelocity: false, includeHealth: false);
        ComponentId positionId = world.GetComponentId<Position>();
        var signature = new ArchetypeSignature([positionId]);
        var signatures = Enumerable.Repeat(signature, scale).ToArray();

        world.CreateDefault(signatures);
        world.ProcessStructuralQueues();

        return new ScenarioMeasurement(scale, world.Entities.AliveCount);
    }

    private static ScenarioMeasurement BenchmarkCreateImmediate(int scale)
    {
        World world = CreateWorld(includeVelocity: false, includeHealth: false);
        ComponentId positionId = world.GetComponentId<Position>();
        var signature = new ArchetypeSignature([positionId]);
        long checksum = 0;
        for (int i = 0; i < scale; i++)
            checksum ^= world.CreateDefault(signature).Index;

        return new ScenarioMeasurement(scale, checksum ^ world.Entities.AliveCount);
    }

    private static ScenarioMeasurement BenchmarkSetComponent(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        long checksum = 0;

        for (int i = 0; i < entities.Length; i++)
        {
            world.SetComponent(entities[i], new Position(i, i * 2));
            checksum += i * 3L;
        }

        return new ScenarioMeasurement(entities.Length, checksum);
    }

    private static ScenarioMeasurement BenchmarkPatchComponent(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        long checksum = 0;

        for (int i = 0; i < entities.Length; i++)
        {
            world.PatchComponent<Position>(entities[i], static (ref Position value) =>
            {
                value = new Position(value.X + 1, value.Y + 2);
            });
            checksum += i;
        }

        return new ScenarioMeasurement(entities.Length, checksum);
    }

    private static ScenarioMeasurement BenchmarkTryGetComponent(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        long checksum = 0;

        for (int i = 0; i < entities.Length; i++)
        {
            if (world.TryGetComponent(entities[i], out Position position))
                checksum += position.X + position.Y;
        }

        return new ScenarioMeasurement(entities.Length, checksum);
    }

    private static ScenarioMeasurement BenchmarkQuery1(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        long checksum = 0;

        world.Query<Position>((Entity entity, in Position position) =>
        {
                checksum += entity.Index + position.X + position.Y;
        });

        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkQuery2(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: true, includeHealth: false);
        long checksum = 0;

        world.Query<Position, Velocity>((Entity entity, in Position position, in Velocity velocity) =>
        {
                checksum += entity.Index + position.X + velocity.X;
        });

        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkQuery3(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: true, includeHealth: true);
        long checksum = 0;

        world.Query<Position, Velocity, Health>((Entity entity, in Position position, in Velocity velocity, in Health health) =>
        {
                checksum += entity.Index + position.X + velocity.X + health.Value;
        });

        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkDeferredAdd(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        for (int i = 0; i < entities.Length; i++)
            world.EnqueueAdd(entities[i], new Velocity(i, i + 1));

        world.ProcessStructuralQueues();

        long checksum = 0;
        for (int i = 0; i < entities.Length; i++)
            checksum += world.GetComponent<Velocity>(entities[i]).X;

        return new ScenarioMeasurement(entities.Length, checksum);
    }

    private static ScenarioMeasurement BenchmarkDeferredRemove(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: true, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        world.EnqueueRemove<Velocity>(entities);
        world.ProcessStructuralQueues();

        long checksum = 0;
        for (int i = 0; i < entities.Length; i++)
            checksum += world.HasComponent<Velocity>(entities[i]) ? 1 : 0;

        return new ScenarioMeasurement(entities.Length, checksum);
    }

    private static ScenarioMeasurement BenchmarkDeferredDestroy(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        world.EnqueueDestroy(entities);
        world.ProcessStructuralQueues();
        return new ScenarioMeasurement(entities.Length, world.Entities.AliveCount);
    }

    private static ScenarioMeasurement BenchmarkDeferredDestroySparseVictims(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        Entity[] victims = BuildSparseVictimSet(entities);
        world.EnqueueDestroy(victims);
        world.ProcessStructuralQueues();
        return new ScenarioMeasurement(victims.Length, world.Entities.AliveCount);
    }

    private static ScenarioMeasurement BenchmarkMigrationCycleVelocity(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        var velocities = new Velocity[entities.Length];
        for (int i = 0; i < velocities.Length; i++)
            velocities[i] = new Velocity(i + 100, i + 101);

        world.EnqueueAdd<Velocity>((ReadOnlySpan<Entity>)entities, (ReadOnlySpan<Velocity>)velocities);
        world.ProcessStructuralQueues();
        world.EnqueueRemove<Velocity>(entities);
        world.ProcessStructuralQueues();

        long checksum = 0;
        for (int i = 0; i < entities.Length; i++)
            checksum += world.HasComponent<Velocity>(entities[i]) ? 1 : 0;

        return new ScenarioMeasurement(entities.Length * 2L, checksum);
    }

    private static ScenarioMeasurement BenchmarkCreateDestroyReuseChurn(int scale)
    {
        World world = CreateWorld(includeVelocity: false, includeHealth: false);
        ComponentId positionId = world.GetComponentId<Position>();
        var signature = new ArchetypeSignature([positionId]);
        int batchSize = Math.Clamp(scale / 10, 32, 512);
        int rounds = Math.Max(1, scale / batchSize);
        long checksum = 0;
        int reusedIndexMatches = 0;
        Entity? previousBatchFirst = null;

        for (int round = 0; round < rounds; round++)
        {
            for (int i = 0; i < batchSize; i++)
                world.EnqueueCreateDefault(signature);

            world.ProcessStructuralQueues();
            Entity[] created = CaptureEntities(world, batchSize);

            if (previousBatchFirst.HasValue && created.Length > 0)
            {
                if (created[0].Index == previousBatchFirst.Value.Index && created[0].Version > previousBatchFirst.Value.Version)
                    reusedIndexMatches++;
            }

            previousBatchFirst = created.Length > 0 ? created[0] : null;

            for (int i = 0; i < created.Length; i++)
            {
                world.SetComponent(created[i], new Position(round + i, round + i + 1));
                checksum += created[i].Index + created[i].Version;
            }

            world.EnqueueDestroy(created);
            world.ProcessStructuralQueues();
        }

        return new ScenarioMeasurement((long)rounds * batchSize * 2, checksum ^ reusedIndexMatches ^ world.Entities.AliveCount);
    }

    private static ScenarioMeasurement BenchmarkDirtyDrain(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        for (int i = 0; i < entities.Length; i++)
            world.SetComponent(entities[i], new Position(i + 7, i + 9));

        var destination = new Entity[scale];
        int drained = world.DrainDirtyTo<Position>(destination);
        long checksum = 0;
        for (int i = 0; i < drained; i++)
            checksum += destination[i].Index;

        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkPersistenceView(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: true, includeHealth: true);
        ComponentId positionId = world.GetComponentId<Position>();
        var view = new WorldPersistenceView(world, [positionId]);
        long checksum = 0;

        for (int i = 0; i < view.Archetypes.Count; i++)
        {
            PersistenceArchetypeView archetype = view.Archetypes[i];
            ReadOnlySpan<ComponentId> componentIds = archetype.ComponentIds;
            checksum += archetype.EntityCount;
            for (int j = 0; j < componentIds.Length; j++)
                checksum += componentIds[j].Value;
        }

        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkTickSerial(int entityCount, int ticks)
    {
        World world = CreateSchedulerWorld(entityCount, SystemExecutionMode.Serial);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickParallel(int entityCount, int ticks)
    {
        World world = CreateSchedulerWorld(entityCount, SystemExecutionMode.ParallelBatches);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickSerialReadOnly(int entityCount, int ticks)
    {
        World world = CreateReadOnlySchedulerWorld(entityCount, SystemExecutionMode.Serial);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickParallelReadOnly(int entityCount, int ticks)
    {
        World world = CreateReadOnlySchedulerWorld(entityCount, SystemExecutionMode.ParallelBatches);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickSerialDirtyAware(int entityCount, int ticks)
    {
        World world = CreateSchedulerWorld(entityCount, SystemExecutionMode.Serial);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickParallelDirtyAware(int entityCount, int ticks)
    {
        World world = CreateSchedulerWorld(entityCount, SystemExecutionMode.ParallelBatches);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickSerialOscillatingMovement(int entityCount, int ticks, bool activeSubsetOnly = false)
    {
        World world = CreateOscillatingMovementWorld(entityCount, SystemExecutionMode.Serial, activeSubsetOnly);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickParallelOscillatingMovement(int entityCount, int ticks, bool activeSubsetOnly = false)
    {
        World world = CreateOscillatingMovementWorld(entityCount, SystemExecutionMode.ParallelBatches, activeSubsetOnly);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 4 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickSerialV1PulseMovement(int entityCount, int ticks, bool activeSubsetOnly = false)
    {
        World world = CreateV1PulseMovementWorld(entityCount, SystemExecutionMode.Serial, activeSubsetOnly);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 3 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static ScenarioMeasurement BenchmarkTickParallelV1PulseMovement(int entityCount, int ticks, bool activeSubsetOnly = false)
    {
        World world = CreateV1PulseMovementWorld(entityCount, SystemExecutionMode.ParallelBatches, activeSubsetOnly);
        for (int i = 0; i < ticks; i++)
            world.Tick(0.016f);

        return new ScenarioMeasurement((long)entityCount * 3 * ticks, SchedulerBenchmarkState.Checksum);
    }

    private static World CreateSchedulerWorld(int entityCount, SystemExecutionMode mode)
    {
        SchedulerBenchmarkState.Reset();

        World world = SeedWorld(entityCount, includeVelocity: true, includeHealth: false);
        ComponentId positionId = world.GetComponentId<Position>();
        ComponentId velocityId = world.GetComponentId<Velocity>();
        world.SetSystemExecutionMode(mode);
        world.RegisterSystems(
            new SchedulerQueryReadSystem<ReadTagA>("ReadA", positionId),
            new SchedulerQueryReadSystem<ReadTagB>("ReadB", positionId),
            new SchedulerQueryWriteSystem<WriteTagA>("WriteA", positionId, velocityId, writePosition: true),
            new SchedulerQueryWriteSystem<WriteTagB>("WriteB", positionId, velocityId, writePosition: false));
        world.SealSystems();
        return world;
    }

    private static World CreateReadOnlySchedulerWorld(int entityCount, SystemExecutionMode mode)
    {
        SchedulerBenchmarkState.Reset();

        World world = SeedWorld(entityCount, includeVelocity: true, includeHealth: false);
        ComponentId positionId = world.GetComponentId<Position>();
        ComponentId velocityId = world.GetComponentId<Velocity>();
        world.SetSystemExecutionMode(mode);
        world.RegisterSystems(
            new SchedulerReadOnlySystem<ReadOnlyTagA>("ReadOnlyA", positionId, velocityId),
            new SchedulerReadOnlySystem<ReadOnlyTagB>("ReadOnlyB", positionId, velocityId),
            new SchedulerReadOnlySystem<ReadOnlyTagC>("ReadOnlyC", positionId, velocityId),
            new SchedulerReadOnlySystem<ReadOnlyTagD>("ReadOnlyD", positionId, velocityId));
        world.SealSystems();
        return world;
    }

    private static World CreateOscillatingMovementWorld(int entityCount, SystemExecutionMode mode, bool activeSubsetOnly = false)
    {
        SchedulerBenchmarkState.Reset();

        World world = SeedOscillatingWorld(entityCount);
        ComponentId positionId = world.GetComponentId<Position>();
        ComponentId velocityId = world.GetComponentId<Velocity>();
        ComponentId oscillatorId = world.GetComponentId<Oscillator>();
        world.SetSystemExecutionMode(mode);
        world.RegisterSystems(
            new OscillatingReadSystem<OscReadTagA>("OscReadA", positionId, velocityId, oscillatorId, activeSubsetOnly),
            new OscillatingReadSystem<OscReadTagB>("OscReadB", positionId, velocityId, oscillatorId, activeSubsetOnly),
            new OscillatingWriteSystem<OscWriteTagA>("OscWriteA", velocityId, oscillatorId, positionId, writePosition: true, activeSubsetOnly),
            new OscillatingWriteSystem<OscWriteTagB>("OscWriteB", positionId, oscillatorId, velocityId, writePosition: false, activeSubsetOnly));
        world.SealSystems();
        return world;
    }

    private static World CreateV1PulseMovementWorld(int entityCount, SystemExecutionMode mode, bool activeSubsetOnly = false)
    {
        SchedulerBenchmarkState.Reset();

        World world = SeedV1PulseWorld(entityCount);
        ComponentId positionId = world.GetComponentId<PulsePosition>();
        ComponentId velocityId = world.GetComponentId<PulseVelocity>();
        ComponentId motionId = world.GetComponentId<PulseMotion>();
        world.SetSystemExecutionMode(mode);
        world.RegisterSystems(
            new V1PulsePhaseAdvanceSystem("V1PulsePhase", motionId, activeSubsetOnly),
            new V1PulseVelocitySystem("V1PulseVelocity", positionId, motionId, velocityId, activeSubsetOnly),
            new V1MovementSystem("V1Movement", positionId, velocityId, activeSubsetOnly));
        world.SealSystems();
        return world;
    }

    private static World CreateWorld(bool includeVelocity, bool includeHealth)
    {
        var builder = new EcsSchemaBuilder();
        builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        builder.RegisterComponent<Health>();
        builder.RegisterComponent<Active>();
        builder.RegisterComponent<Oscillator>();
        builder.RegisterComponent<PulsePosition>();
        builder.RegisterComponent<PulseVelocity>();
        builder.RegisterComponent<PulseMotion>();
        return new World(builder.Build());
    }

    private static World SeedOscillatingWorld(int count)
    {
        World world = CreateWorld(includeVelocity: true, includeHealth: false);
        ComponentId[] componentIds =
        [
            world.GetComponentId<Position>(),
            world.GetComponentId<Velocity>(),
            world.GetComponentId<Oscillator>(),
        ];

        var signature = new ArchetypeSignature(componentIds);
        for (int i = 0; i < count; i++)
        {
            Entity entity = world.CreateDefault(signature);
            world.SetComponent(entity, new Position(i, i + 1));
            world.SetComponent(entity, new Velocity((i % 97) + 2, (i % 89) + 3));
            world.SetComponent(entity, new Oscillator(
                Phase: i * 0.0131f,
                Frequency: 0.75f + ((i % 7) * 0.11f),
                Amplitude: 1.5f + ((i % 5) * 0.25f),
                Bias: (i % 13) * 0.1f));
        }

        Span<Entity> drain = stackalloc Entity[Math.Min(count, 256)];
        while (world.DrainDirtyTo(drain) > 0) { }
        while (world.DrainDirtyTo<Position>(drain) > 0) { }
        while (world.DrainDirtyTo<Velocity>(drain) > 0) { }
        while (world.DrainDirtyTo<Oscillator>(drain) > 0) { }
        return world;
    }

    private static World SeedV1PulseWorld(int count)
    {
        World world = CreateWorld(includeVelocity: true, includeHealth: false);
        ComponentId[] componentIds =
        [
            world.GetComponentId<PulsePosition>(),
            world.GetComponentId<PulseVelocity>(),
            world.GetComponentId<PulseMotion>(),
        ];

        var signature = new ArchetypeSignature(componentIds);
        for (int i = 0; i < count; i++)
        {
            Entity entity = world.CreateDefault(signature);
            world.SetComponent(entity, new PulsePosition(
                X: (i % 1024) - 512f,
                Y: ((i / 1024) % 1024) - 512f,
                Z: (i % 257) - 128f));
            world.SetComponent(entity, new PulseVelocity(0f, 0f, 0f));
            world.SetComponent(entity, new PulseMotion(
                Speed: 2f + ((i % 5) * 0.1f),
                Frequency: 0.5f + ((i % 7) * 0.05f),
                Phase: (i % 360) * (MathF.PI / 180f)));
        }

        Span<Entity> drain = stackalloc Entity[Math.Min(count, 256)];
        while (world.DrainDirtyTo(drain) > 0) { }
        while (world.DrainDirtyTo<PulsePosition>(drain) > 0) { }
        while (world.DrainDirtyTo<PulseVelocity>(drain) > 0) { }
        while (world.DrainDirtyTo<PulseMotion>(drain) > 0) { }
        return world;
    }

    private static World SeedWorld(int count, bool includeVelocity, bool includeHealth)
    {
        World world = CreateWorld(includeVelocity, includeHealth);
        ComponentId positionId = world.GetComponentId<Position>();
        ComponentId[] componentIds;
        if (includeVelocity && includeHealth)
            componentIds = [positionId, world.GetComponentId<Velocity>(), world.GetComponentId<Health>()];
        else if (includeVelocity)
            componentIds = [positionId, world.GetComponentId<Velocity>()];
        else
            componentIds = [positionId];

        var signature = new ArchetypeSignature(componentIds);
        for (int i = 0; i < count; i++)
        {
            Entity entity = world.CreateDefault(signature);
            world.SetComponent(entity, new Position(i, i + 1));
            if (includeVelocity)
                world.SetComponent(entity, new Velocity(i + 2, i + 3));
            if (includeHealth)
                world.SetComponent(entity, new Health(i + 4));
        }

        Span<Entity> drain = stackalloc Entity[Math.Min(count, 256)];
        while (world.DrainDirtyTo(drain) > 0) { }
        while (world.DrainDirtyTo<Position>(drain) > 0) { }
        if (includeVelocity)
            while (world.DrainDirtyTo<Velocity>(drain) > 0) { }
        if (includeHealth)
            while (world.DrainDirtyTo<Health>(drain) > 0) { }

        return world;
    }

    private static Entity[] CaptureEntities(World world, int expectedCount)
    {
        ComponentId positionId = world.GetComponentId<Position>();
        var entities = new Entity[expectedCount];
        int write = 0;

        foreach (Archetype archetype in world.QueryArchetypes([positionId]))
        {
            ReadOnlySpan<Entity> span = archetype.GetEntitySpan();
            for (int i = 0; i < span.Length; i++)
                entities[write++] = span[i];
        }

        if (write != entities.Length)
            Array.Resize(ref entities, write);

        return entities;
    }

    private static Entity[] BuildSparseVictimSet(Entity[] entities)
    {
        int victimCount = entities.Length / 2;
        var victims = new Entity[victimCount];
        int write = 0;
        for (int i = entities.Length - 2; i >= 0 && write < victimCount; i -= 2)
            victims[write++] = entities[i];

        return victims;
    }

    private readonly record struct Position(int X, int Y) : IComponent<Position>;
    private readonly record struct Velocity(int X, int Y) : IComponent<Velocity>;
    private readonly record struct Health(int Value) : IComponent<Health>;
    private readonly record struct Active(byte Value) : IComponent<Active>;
    private readonly record struct Oscillator(float Phase, float Frequency, float Amplitude, float Bias) : IComponent<Oscillator>;
    private readonly record struct PulsePosition(float X, float Y, float Z) : IComponent<PulsePosition>;
    private readonly record struct PulseVelocity(float X, float Y, float Z) : IComponent<PulseVelocity>;
    private readonly record struct PulseMotion(float Speed, float Frequency, float Phase) : IComponent<PulseMotion>;

    private sealed class SchedulerQueryReadSystem<TTag> : SystemBase
    {
        public SchedulerQueryReadSystem(string name, ComponentId positionId)
        {
            Descriptor = new SystemDescriptor(name, SystemStage.AI, 1, 0, ComponentMask.FromIds([positionId]), ComponentMask.Empty);
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.World.ReadChunks<Position>((ReadOnlySpan<Entity> _, ReadOnlySpan<Position> positions) =>
            {
                long local = 0;
                for (int i = 0; i < positions.Length; i++)
                    local += positions[i].X;

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, local);
            });
        }
    }

    private sealed class SchedulerQueryWriteSystem<TTag> : SystemBase
    {
        private readonly bool _writePosition;

        public SchedulerQueryWriteSystem(string name, ComponentId positionId, ComponentId velocityId, bool writePosition)
        {
            _writePosition = writePosition;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, velocityId]),
                ComponentMask.FromIds([writePosition ? positionId : velocityId]));
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            World world = ctx.World;
            if (_writePosition)
            {
                world.PatchQuery<Position, Velocity, Position>((Entity _, in Position position, in Velocity velocity, ref Position write) =>
                {
                    write = new Position(position.X + 1, position.Y + 1);
                    Interlocked.Add(ref SchedulerBenchmarkState.Checksum, position.X + velocity.X);
                });
                return;
            }

            world.PatchQuery<Position, Velocity, Velocity>((Entity _, in Position position, in Velocity velocity, ref Velocity write) =>
            {
                write = new Velocity(velocity.X + 1, velocity.Y + 1);
                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, position.X + velocity.X);
            });
        }
    }

    private sealed class SchedulerReadOnlySystem<TTag> : SystemBase
    {
        public SchedulerReadOnlySystem(string name, ComponentId positionId, ComponentId velocityId)
        {
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, velocityId]),
                ComponentMask.Empty);
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.World.ReadChunks<Position, Velocity>((ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, ReadOnlySpan<Velocity> velocities) =>
            {
                long local = 0;
                for (int i = 0; i < entities.Length; i++)
                    local += entities[i].Index + positions[i].X + velocities[i].X;

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, local);
            });
        }
    }

    private sealed class OscillatingReadSystem<TTag> : SystemBase
    {
        private readonly bool _activeSubsetOnly;

        public OscillatingReadSystem(string name, ComponentId positionId, ComponentId velocityId, ComponentId oscillatorId, bool activeSubsetOnly)
        {
            _activeSubsetOnly = activeSubsetOnly;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, velocityId, oscillatorId]),
                ComponentMask.Empty);
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            float time = ctx.Tick * ctx.DeltaSeconds;
            uint tick = ctx.Tick;
            ctx.World.ReadChunks<Position, Velocity, Oscillator>((ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, ReadOnlySpan<Velocity> velocities, ReadOnlySpan<Oscillator> oscillators) =>
            {
                long local = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (_activeSubsetOnly && !MovementActivity.IsActive(entity, tick))
                        continue;

                    Position position = positions[i];
                    Velocity velocity = velocities[i];
                    Oscillator oscillator = oscillators[i];
                    float phase = (time * oscillator.Frequency) + oscillator.Phase;
                    float sample = MathF.Sin(phase + (position.X * 0.001f))
                        + MathF.Cos((phase * 0.5f) + (velocity.Y * 0.001f))
                        + (MathF.Sin((phase * 1.7f) - oscillator.Bias) * oscillator.Amplitude);
                    int value = (int)MathF.Round((sample * 512f) + (oscillator.Amplitude * 32f) + (oscillator.Bias * 16f));
                    local += entity.Index + value;
                }

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, local);
            });
        }
    }

    private sealed class OscillatingWriteSystem<TTag> : SystemBase
    {
        private const int BlockSize = 4096;
        private readonly World.PatchChunkPlanCache<Velocity, Oscillator, Position> _positionPlan = new();
        private readonly World.PatchChunkPlanCache<Position, Oscillator, Velocity> _velocityPlan = new();
        private readonly bool _activeSubsetOnly;
        private readonly bool _writePosition;

        public OscillatingWriteSystem(string name, ComponentId readId1, ComponentId readId2, ComponentId writeId, bool writePosition, bool activeSubsetOnly)
        {
            _activeSubsetOnly = activeSubsetOnly;
            _writePosition = writePosition;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([readId1, readId2, writeId]),
                ComponentMask.FromIds([writeId]));
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            float time = ctx.Tick * ctx.DeltaSeconds;
            uint tick = ctx.Tick;

            if (_writePosition)
            {
                if (_activeSubsetOnly)
                {
                    ctx.PatchChunksParallelSparse<Velocity, Oscillator, Position>(BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<Velocity> velocities, ReadOnlySpan<Oscillator> oscillators, Span<Position> writes, Span<int> dirtyIndices) =>
                    {
                        long checksum = 0;
                        int dirtyCount = 0;
                        for (int i = 0; i < writes.Length; i++)
                        {
                            if (!MovementActivity.IsActive(entities[i], tick))
                                continue;

                            Velocity velocity = velocities[i];
                            Oscillator oscillator = oscillators[i];
                            Position write = writes[i];
                            float phase = (time * oscillator.Frequency) + oscillator.Phase;
                            float waveA = MathF.Sin(phase);
                            float waveB = MathF.Cos((phase * 0.5f) + oscillator.Bias);
                            float pulse = MathF.Sin((phase * 2.3f) - oscillator.Bias) * oscillator.Amplitude;
                            int dx = (int)MathF.Round(((waveA + waveB) * oscillator.Amplitude * 8f) + pulse + (velocity.X * 0.125f));
                            int dy = (int)MathF.Round(((MathF.Cos(phase * 1.7f) - MathF.Sin(phase * 0.33f)) * oscillator.Amplitude * 6f) + (velocity.Y * 0.125f));
                            write = new Position(write.X + dx, write.Y + dy);
                            writes[i] = write;
                            dirtyIndices[dirtyCount++] = i;
                            checksum += write.X + write.Y + dx + dy;
                        }

                        Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
                        return dirtyCount;
                    });
                    return;
                }

                ctx.PatchChunksParallel(_positionPlan, BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<Velocity> velocities, ReadOnlySpan<Oscillator> oscillators, Span<Position> writes) =>
                {
                    long checksum = 0;
                    for (int i = 0; i < writes.Length; i++)
                    {
                        if (_activeSubsetOnly && !MovementActivity.IsActive(entities[i], tick))
                            continue;

                        Velocity velocity = velocities[i];
                        Oscillator oscillator = oscillators[i];
                        Position write = writes[i];
                        float phase = (time * oscillator.Frequency) + oscillator.Phase;
                        float waveA = MathF.Sin(phase);
                        float waveB = MathF.Cos((phase * 0.5f) + oscillator.Bias);
                        float pulse = MathF.Sin((phase * 2.3f) - oscillator.Bias) * oscillator.Amplitude;
                        int dx = (int)MathF.Round(((waveA + waveB) * oscillator.Amplitude * 8f) + pulse + (velocity.X * 0.125f));
                        int dy = (int)MathF.Round(((MathF.Cos(phase * 1.7f) - MathF.Sin(phase * 0.33f)) * oscillator.Amplitude * 6f) + (velocity.Y * 0.125f));
                        write = new Position(write.X + dx, write.Y + dy);
                        writes[i] = write;
                        checksum += write.X + write.Y + dx + dy;
                    }

                    Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
                });
                return;
            }

            if (_activeSubsetOnly)
            {
                ctx.PatchChunksParallelSparse<Position, Oscillator, Velocity>(BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, ReadOnlySpan<Oscillator> oscillators, Span<Velocity> writes, Span<int> dirtyIndices) =>
                {
                    long checksum = 0;
                    int dirtyCount = 0;
                    for (int i = 0; i < writes.Length; i++)
                    {
                        if (!MovementActivity.IsActive(entities[i], tick))
                            continue;

                        Position position = positions[i];
                        Oscillator oscillator = oscillators[i];
                        float phase = (time * (oscillator.Frequency * 1.13f)) + oscillator.Phase + (position.X * 0.0005f);
                        float waveA = MathF.Sin(phase);
                        float waveB = MathF.Cos((phase * 0.75f) - oscillator.Bias);
                        float waveC = MathF.Sin((phase * 1.91f) + oscillator.Amplitude);
                        int vx = (int)MathF.Round(((waveA + waveB) * oscillator.Amplitude * 4f) + (position.X & 7));
                        int vy = (int)MathF.Round(((waveC - waveA) * oscillator.Amplitude * 4f) + (position.Y & 7));
                        Velocity write = new(vx, vy);
                        writes[i] = write;
                        dirtyIndices[dirtyCount++] = i;
                        checksum += write.X + write.Y + vx + vy;
                    }

                    Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
                    return dirtyCount;
                });
                return;
            }

            ctx.PatchChunksParallel(_velocityPlan, BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, ReadOnlySpan<Oscillator> oscillators, Span<Velocity> writes) =>
            {
                long checksum = 0;
                for (int i = 0; i < writes.Length; i++)
                {
                    if (_activeSubsetOnly && !MovementActivity.IsActive(entities[i], tick))
                        continue;

                    Position position = positions[i];
                    Oscillator oscillator = oscillators[i];
                    float phase = (time * (oscillator.Frequency * 1.13f)) + oscillator.Phase + (position.X * 0.0005f);
                    float waveA = MathF.Sin(phase);
                    float waveB = MathF.Cos((phase * 0.75f) - oscillator.Bias);
                    float waveC = MathF.Sin((phase * 1.91f) + oscillator.Amplitude);
                    int vx = (int)MathF.Round(((waveA + waveB) * oscillator.Amplitude * 4f) + (position.X & 7));
                    int vy = (int)MathF.Round(((waveC - waveA) * oscillator.Amplitude * 4f) + (position.Y & 7));
                    Velocity write = new(vx, vy);
                    writes[i] = write;
                    checksum += write.X + write.Y + vx + vy;
                }

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
            });
        }
    }

    private sealed class V1PulsePhaseAdvanceSystem : SystemBase
    {
        private const int BlockSize = 4096;
        private readonly World.PatchChunkPlanCache<PulseMotion> _plan = new();
        private readonly bool _activeSubsetOnly;

        public V1PulsePhaseAdvanceSystem(string name, ComponentId motionId, bool activeSubsetOnly)
        {
            _activeSubsetOnly = activeSubsetOnly;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([motionId]),
                ComponentMask.FromIds([motionId]));
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            float delta = ctx.DeltaSeconds;
            uint tick = ctx.Tick;
            if (_activeSubsetOnly)
            {
                ctx.PatchChunksParallelSparse<PulseMotion>(BlockSize, (ReadOnlySpan<Entity> entities, Span<PulseMotion> writes, Span<int> dirtyIndices) =>
                {
                    long checksum = 0;
                    int dirtyCount = 0;
                    for (int i = 0; i < writes.Length; i++)
                    {
                        if (!MovementActivity.IsActive(entities[i], tick))
                            continue;

                        PulseMotion motion = writes[i];
                        float phase = motion.Phase + (motion.Frequency * delta);
                        if (phase > PulseMath.TwoPi)
                            phase -= PulseMath.TwoPi;

                        writes[i] = new PulseMotion(motion.Speed, motion.Frequency, phase);
                        dirtyIndices[dirtyCount++] = i;
                        checksum += (long)(phase * 1024f);
                    }

                    Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
                    return dirtyCount;
                });
                return;
            }

            ctx.PatchChunksParallel(_plan, BlockSize, (ReadOnlySpan<Entity> entities, Span<PulseMotion> writes) =>
            {
                long checksum = 0;
                for (int i = 0; i < writes.Length; i++)
                {
                    if (_activeSubsetOnly && !MovementActivity.IsActive(entities[i], tick))
                        continue;

                    PulseMotion motion = writes[i];
                    float phase = motion.Phase + (motion.Frequency * delta);
                    if (phase > PulseMath.TwoPi)
                        phase -= PulseMath.TwoPi;

                    writes[i] = new PulseMotion(motion.Speed, motion.Frequency, phase);
                    checksum += (long)(phase * 1024f);
                }

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
            });
        }
    }

    private sealed class V1PulseVelocitySystem : SystemBase
    {
        private const int BlockSize = 4096;
        private readonly World.PatchChunkPlanCache<PulsePosition, PulseMotion, PulseVelocity> _plan = new();
        private readonly bool _activeSubsetOnly;

        public V1PulseVelocitySystem(string name, ComponentId positionId, ComponentId motionId, ComponentId velocityId, bool activeSubsetOnly)
        {
            _activeSubsetOnly = activeSubsetOnly;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, motionId, velocityId]),
                ComponentMask.FromIds([velocityId]));
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            uint tick = ctx.Tick;
            if (_activeSubsetOnly)
            {
                ctx.PatchChunksParallelSparse<PulsePosition, PulseMotion, PulseVelocity>(BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<PulsePosition> positions, ReadOnlySpan<PulseMotion> motions, Span<PulseVelocity> writes, Span<int> dirtyIndices) =>
                {
                    long checksum = 0;
                    int dirtyCount = 0;
                    for (int i = 0; i < writes.Length; i++)
                    {
                        if (!MovementActivity.IsActive(entities[i], tick))
                            continue;

                        PulsePosition position = positions[i];
                        PulseMotion motion = motions[i];
                        float pulseDirection = PulseMath.FastSin(motion.Phase);
                        float distSq = (position.X * position.X) + (position.Y * position.Y) + (position.Z * position.Z);

                        if (distSq > 0.0001f)
                        {
                            float invDist = 1f / MathF.Sqrt(distSq);
                            float speedFactor = pulseDirection * motion.Speed;
                            writes[i] = new PulseVelocity(
                                position.X * invDist * speedFactor,
                                position.Y * invDist * speedFactor,
                                position.Z * invDist * speedFactor);
                        }
                        else
                        {
                            float fallback = motion.Speed * 0.35355338f;
                            writes[i] = new PulseVelocity(fallback, -fallback, fallback);
                        }

                        dirtyIndices[dirtyCount++] = i;
                        PulseVelocity velocity = writes[i];
                        checksum += (long)((velocity.X + velocity.Y + velocity.Z) * 256f);
                    }

                    Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
                    return dirtyCount;
                });
                return;
            }

            ctx.PatchChunksParallel(_plan, BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<PulsePosition> positions, ReadOnlySpan<PulseMotion> motions, Span<PulseVelocity> writes) =>
            {
                long checksum = 0;
                for (int i = 0; i < writes.Length; i++)
                {
                    if (_activeSubsetOnly && !MovementActivity.IsActive(entities[i], tick))
                        continue;

                    PulsePosition position = positions[i];
                    PulseMotion motion = motions[i];
                    float pulseDirection = PulseMath.FastSin(motion.Phase);
                    float distSq = (position.X * position.X) + (position.Y * position.Y) + (position.Z * position.Z);

                    if (distSq > 0.0001f)
                    {
                        float invDist = 1f / MathF.Sqrt(distSq);
                        float speedFactor = pulseDirection * motion.Speed;
                        writes[i] = new PulseVelocity(
                            position.X * invDist * speedFactor,
                            position.Y * invDist * speedFactor,
                            position.Z * invDist * speedFactor);
                    }
                    else
                    {
                        float fallback = motion.Speed * 0.35355338f;
                        writes[i] = new PulseVelocity(fallback, -fallback, fallback);
                    }

                    PulseVelocity velocity = writes[i];
                    checksum += (long)((velocity.X + velocity.Y + velocity.Z) * 256f);
                }

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
            });
        }
    }

    private sealed class V1MovementSystem : SystemBase
    {
        private const int BlockSize = 4096;
        private readonly World.PatchChunkPlanCache<PulseVelocity, PulsePosition> _plan = new();
        private readonly bool _activeSubsetOnly;

        public V1MovementSystem(string name, ComponentId positionId, ComponentId velocityId, bool activeSubsetOnly)
        {
            _activeSubsetOnly = activeSubsetOnly;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([positionId, velocityId]),
                ComponentMask.FromIds([positionId]));
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            float delta = ctx.DeltaSeconds;
            uint tick = ctx.Tick;
            if (_activeSubsetOnly)
            {
                ctx.PatchChunksParallelSparse<PulseVelocity, PulsePosition>(BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<PulseVelocity> velocities, Span<PulsePosition> writes, Span<int> dirtyIndices) =>
                {
                    long checksum = 0;
                    int dirtyCount = 0;
                    for (int i = 0; i < writes.Length; i++)
                    {
                        if (!MovementActivity.IsActive(entities[i], tick))
                            continue;

                        PulsePosition position = writes[i];
                        PulseVelocity velocity = velocities[i];
                        position = new PulsePosition(
                            position.X + (velocity.X * delta),
                            position.Y + (velocity.Y * delta),
                            position.Z + (velocity.Z * delta));
                        writes[i] = position;
                        dirtyIndices[dirtyCount++] = i;
                        checksum += (long)((position.X + position.Y + position.Z) * 128f);
                    }

                    Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
                    return dirtyCount;
                });
                return;
            }

            ctx.PatchChunksParallel(_plan, BlockSize, (ReadOnlySpan<Entity> entities, ReadOnlySpan<PulseVelocity> velocities, Span<PulsePosition> writes) =>
            {
                long checksum = 0;
                for (int i = 0; i < writes.Length; i++)
                {
                    if (_activeSubsetOnly && !MovementActivity.IsActive(entities[i], tick))
                        continue;

                    PulsePosition position = writes[i];
                    PulseVelocity velocity = velocities[i];
                    position = new PulsePosition(
                        position.X + (velocity.X * delta),
                        position.Y + (velocity.Y * delta),
                        position.Z + (velocity.Z * delta));
                    writes[i] = position;
                    checksum += (long)((position.X + position.Y + position.Z) * 128f);
                }

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, checksum);
            });
        }
    }

    private sealed class ReadOnlyTagA;
    private sealed class ReadOnlyTagB;
    private sealed class ReadOnlyTagC;
    private sealed class ReadOnlyTagD;
    private sealed class OscReadTagA;
    private sealed class OscReadTagB;
    private sealed class OscWriteTagA;
    private sealed class OscWriteTagB;

    private static class SchedulerBenchmarkState
    {
        public static long Checksum;

        public static void Reset() => Checksum = 0;
    }

    private static class PulseMath
    {
        private const int LookupSize = 1024;
        private const float LookupScale = LookupSize / TwoPi;
        public const float TwoPi = MathF.PI * 2f;
        private static readonly float[] SinLookup = BuildLookup();

        public static float FastSin(float angle)
        {
            float wrapped = angle % TwoPi;
            if (wrapped < 0f)
                wrapped += TwoPi;

            int index = (int)(wrapped * LookupScale) & (LookupSize - 1);
            return SinLookup[index];
        }

        private static float[] BuildLookup()
        {
            var values = new float[LookupSize];
            for (int i = 0; i < LookupSize; i++)
                values[i] = MathF.Sin((i / (float)LookupSize) * TwoPi);

            return values;
        }
    }

    private static class MovementActivity
    {
        private const uint ActiveThresholdPermille = 350;
        private const uint ReshufflePeriodTicks = 10;

        public static bool IsActive(Entity entity, uint tick)
        {
            uint epoch = tick / ReshufflePeriodTicks;
            uint seed = unchecked((uint)entity.Index) ^ (epoch * 0x9E3779B9u);
            uint mixed = Mix(seed);
            return (mixed % 1000u) < ActiveThresholdPermille;
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    private sealed class HotReadFixture
    {
        public required World World { get; init; }
        public required Entity[] Entities { get; init; }

        public static HotReadFixture Create(int scale)
        {
            World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
            return new HotReadFixture
            {
                World = world,
                Entities = CaptureEntities(world, scale),
            };
        }
    }

    private sealed class HotMutationFixture
    {
        public required World World { get; init; }
        public required Entity[] Entities { get; init; }
        public required Entity[] DirtyBuffer { get; init; }
        public int Pass { get; set; }

        public static HotMutationFixture Create(int scale)
        {
            World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
            return new HotMutationFixture
            {
                World = world,
                Entities = CaptureEntities(world, scale),
                DirtyBuffer = new Entity[scale],
                Pass = 0,
            };
        }
    }

    private sealed class HotStructuralAddPool
    {
        private readonly (World World, Entity[] Entities)[] _items;
        private int _index;

        private HotStructuralAddPool((World World, Entity[] Entities)[] items)
        {
            _items = items;
        }

        public static HotStructuralAddPool Create(int scale, int poolSize)
        {
            var items = new (World World, Entity[] Entities)[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
                items[i] = (world, CaptureEntities(world, scale));
            }

            return new HotStructuralAddPool(items);
        }

        public (World World, Entity[] Entities) Next()
        {
            if (_index >= _items.Length)
                _index = 0;

            return _items[_index++];
        }
    }

    private sealed class ReadTagA { }
    private sealed class ReadTagB { }
    private sealed class WriteTagA { }
    private sealed class WriteTagB { }
}
