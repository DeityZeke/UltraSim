using System.Text;
using UltraSim.ECS;
using UltraSim.ECS.Benchmarks.Core;
using UltraSim.ECS.Persistence;

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "ECS Kernel Performance Suite",
    kernelName: "old",
    scenarios: OldKernelBenchmarks.CreateScenarios());

string outputPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "results", "old-ecs-benchmarks.json");

MicroBenchmark.WriteReport(report, Path.GetFullPath(outputPath));
Console.WriteLine(OldKernelBenchmarks.FormatSummary(report, outputPath));

internal static class OldKernelBenchmarks
{
    private static readonly int[] CommonScales = [1_000, 10_000, 50_000];

    public static IReadOnlyList<BenchmarkScenario> CreateScenarios()
    {
        var scenarios = new List<BenchmarkScenario>();

        scenarios.Add(new BenchmarkScenario
        {
            Name = "ComponentRegister_4Components",
            Category = "Bootstrap",
            Comparable = false,
            Scale = 4,
            Execute = BenchmarkRegister4,
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
            scenarios.Add(MakeScenario("DirtyDrain_Position", "DirtyTracking", true, capturedScale, () => BenchmarkDirtyDrain(capturedScale)));
            scenarios.Add(MakeScenario("PersistenceView_Filtered_Position", "PersistenceSeam", true, capturedScale, () => BenchmarkPersistenceView(capturedScale)));
            scenarios.Add(MakeScenario("GetComponentRef_Write_Position", "Mutation", false, capturedScale, () => BenchmarkGetComponentRefWrite(capturedScale)));
        }

        scenarios.Add(MakeScenario("TickSerial_QuerySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickSerial(10_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_QuerySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickParallel(10_000, 200)));
        scenarios.Add(MakeScenario("TickSerial_ReadOnlySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickSerialReadOnly(10_000, 200)));
        scenarios.Add(MakeScenario("TickParallel_ReadOnlySystems_10k_200Ticks", "Scheduler", true, 10_000, () => BenchmarkTickParallelReadOnly(10_000, 200)));
        scenarios.Add(CreateHotQuery1Scenario(50_000));
        scenarios.Add(CreateHotSetComponentScenario(50_000));
        scenarios.Add(CreateHotTryGetScenario(50_000));
        scenarios.Add(CreateHotDirtyDrainScenario(50_000));
        scenarios.Add(CreateHotDeferredAddScenario(50_000));
        scenarios.Add(CreateHotTickSerialScenario(10_000, 200));
        scenarios.Add(CreateHotTickParallelScenario(10_000, 200));
        scenarios.Add(CreateHotTickSerialReadOnlyScenario(10_000, 200));
        scenarios.Add(CreateHotTickParallelReadOnlyScenario(10_000, 200));
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
            fixture.World.Query<Position>((Entity entity, ref Position position) =>
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
                world.TrySetComponent(entities[i], new Position(i + fixture.Pass, i + fixture.Pass + 1));
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
                world.TrySetComponent(entities[i], new Position(i + fixture.Pass + 3, i + fixture.Pass + 5));

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

    private static ScenarioMeasurement BenchmarkRegister4()
    {
        ResetStatics();
        ComponentManager.Register<Position>();
        ComponentManager.Register<Velocity>();
        ComponentManager.Register<Health>();
        ComponentManager.Register<Active>();
        ComponentManager.Seal();
        return new ScenarioMeasurement(ComponentManager.Count, ComponentManager.Count);
    }

    private static ScenarioMeasurement BenchmarkCreateDefaultDeferred(int scale)
    {
        World world = CreateWorld(includeVelocity: false, includeHealth: false);
        ComponentId positionId = ComponentManager.GetId<Position>();
        var signature = new ArchetypeSignature([positionId]);
        for (int i = 0; i < scale; i++)
            world.CreateDefault(signature);
        world.ProcessStructuralQueues();
        return new ScenarioMeasurement(scale, world.AliveEntityCount);
    }

    private static ScenarioMeasurement BenchmarkSetComponent(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        long checksum = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            world.TrySetComponent(entities[i], new Position(i, i * 2));
            checksum += i * 3L;
        }

        return new ScenarioMeasurement(entities.Length, checksum);
    }

    private static ScenarioMeasurement BenchmarkGetComponentRefWrite(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        long checksum = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            ref Position position = ref world.GetComponentRef<Position>(entities[i]);
            position.X += 1;
            position.Y += 2;
            checksum += position.X + position.Y;
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
        world.Query<Position>((Entity entity, ref Position position) =>
        {
            checksum += entity.Index + position.X + position.Y;
        });
        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkQuery2(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: true, includeHealth: false);
        long checksum = 0;
        world.Query<Position, Velocity>((Entity entity, ref Position position, ref Velocity velocity) =>
        {
            checksum += entity.Index + position.X + velocity.X;
        });
        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkQuery3(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: true, includeHealth: true);
        long checksum = 0;
        world.Query<Position, Velocity, Health>((Entity entity, ref Position position, ref Velocity velocity, ref Health health) =>
        {
            checksum += entity.Index + position.X + velocity.X + (int)health.Value;
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
            checksum += (int)world.GetComponent<Velocity>(entities[i]).X;

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
        return new ScenarioMeasurement(entities.Length, world.AliveEntityCount);
    }

    private static ScenarioMeasurement BenchmarkDirtyDrain(int scale)
    {
        World world = SeedWorld(scale, includeVelocity: false, includeHealth: false);
        Entity[] entities = CaptureEntities(world, scale);
        for (int i = 0; i < entities.Length; i++)
            world.TrySetComponent(entities[i], new Position(i + 7, i + 9));

        var destination = new Entity[scale];
        int drained = world.DrainDirtyTo<Position>(destination);
        long checksum = 0;
        for (int i = 0; i < drained; i++)
            checksum += destination[i].Index;

        return new ScenarioMeasurement(scale, checksum);
    }

    private static ScenarioMeasurement BenchmarkPersistenceView(int scale)
    {
        World world = SeedPersistentWorld(scale);
        var view = new WorldPersistenceView(world);
        IReadOnlyList<PersistenceArchetypeView> archetypes = view.GetPersistentArchetypes();
        long checksum = 0;

        for (int i = 0; i < archetypes.Count; i++)
        {
            PersistenceArchetypeView archetype = archetypes[i];
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

    private static World CreateSchedulerWorld(int entityCount, SystemExecutionMode mode)
    {
        SchedulerBenchmarkState.Reset();
        World world = SeedWorld(entityCount, includeVelocity: true, includeHealth: false, new EcsOptions(
            enableStatistics: false,
            enableDebug: false,
            enableDirtyTracking: true,
            systemExecutionMode: mode));
        world.RegisterSystems(
            new SchedulerQueryReadSystem<ReadTagA>("ReadA"),
            new SchedulerQueryReadSystem<ReadTagB>("ReadB"),
            new SchedulerQueryWriteSystem<WriteTagA>("WriteA", writePosition: true),
            new SchedulerQueryWriteSystem<WriteTagB>("WriteB", writePosition: false));
        world.Seal();
        return world;
    }

    private static World CreateReadOnlySchedulerWorld(int entityCount, SystemExecutionMode mode)
    {
        SchedulerBenchmarkState.Reset();
        World world = SeedWorld(entityCount, includeVelocity: true, includeHealth: false, new EcsOptions(
            enableStatistics: false,
            enableDebug: false,
            enableDirtyTracking: true,
            systemExecutionMode: mode));
        world.RegisterSystems(
            new SchedulerReadOnlySystem<ReadOnlyTagA>("ReadOnlyA"),
            new SchedulerReadOnlySystem<ReadOnlyTagB>("ReadOnlyB"),
            new SchedulerReadOnlySystem<ReadOnlyTagC>("ReadOnlyC"),
            new SchedulerReadOnlySystem<ReadOnlyTagD>("ReadOnlyD"));
        world.Seal();
        return world;
    }

    private static World CreateWorld(bool includeVelocity, bool includeHealth, EcsOptions options = default)
    {
        options = NormalizeOptions(options);
        ResetStatics();
        ComponentManager.Register<Position>();
        ComponentManager.Register<Velocity>();
        ComponentManager.Register<Health>();
        ComponentManager.Register<Active>();
        ComponentManager.Seal();
        return new World(options);
    }

    private static World SeedWorld(int count, bool includeVelocity, bool includeHealth, EcsOptions options = default)
    {
        options = NormalizeOptions(options);
        World world = CreateWorld(includeVelocity, includeHealth, options);
        ComponentId positionId = ComponentManager.GetId<Position>();
        ComponentId[] componentIds;
        if (includeVelocity && includeHealth)
            componentIds = [positionId, ComponentManager.GetId<Velocity>(), ComponentManager.GetId<Health>()];
        else if (includeVelocity)
            componentIds = [positionId, ComponentManager.GetId<Velocity>()];
        else
            componentIds = [positionId];

        var signature = new ArchetypeSignature(componentIds);
        for (int i = 0; i < count; i++)
            world.CreateDefault(signature);
        world.ProcessStructuralQueues();

        Entity[] entities = CaptureEntities(world, count);
        for (int i = 0; i < entities.Length; i++)
        {
            world.TrySetComponent(entities[i], new Position(i, i + 1));
            if (includeVelocity)
                world.TrySetComponent(entities[i], new Velocity(i + 2, i + 3));
            if (includeHealth)
                world.TrySetComponent(entities[i], new Health(i + 4));
        }

        var drain = new Entity[Math.Max(1, count)];
        world.DrainDirtyTo(drain);
        world.DrainDirtyTo<Position>(drain);
        if (includeVelocity)
            world.DrainDirtyTo<Velocity>(drain);
        if (includeHealth)
            world.DrainDirtyTo<Health>(drain);

        return world;
    }

    private static World SeedPersistentWorld(int count)
    {
        ResetStatics();
        ComponentManager.Register<Position>();
        ComponentManager.Register<Velocity>();
        ComponentManager.Register<Health>();
        ComponentManager.Register<Active>();
        ComponentManager.Register<PersistentTag>();
        ComponentManager.Seal();

        var world = new World(NormalizeOptions(default));
        ComponentId[] componentIds =
        [
            ComponentManager.GetId<Position>(),
            ComponentManager.GetId<Velocity>(),
            ComponentManager.GetId<Health>(),
            ComponentManager.GetId<PersistentTag>(),
        ];

        var signature = new ArchetypeSignature(componentIds);
        for (int i = 0; i < count; i++)
            world.CreateDefault(signature);
        world.ProcessStructuralQueues();

        Entity[] entities = CaptureEntities(world, count);
        for (int i = 0; i < entities.Length; i++)
        {
            world.TrySetComponent(entities[i], new Position(i, i + 1));
            world.TrySetComponent(entities[i], new Velocity(i + 2, i + 3));
            world.TrySetComponent(entities[i], new Health(i + 4));
        }

        var drain = new Entity[Math.Max(1, count)];
        world.DrainDirtyTo(drain);
        world.DrainDirtyTo<Position>(drain);
        world.DrainDirtyTo<Velocity>(drain);
        world.DrainDirtyTo<Health>(drain);
        return world;
    }

    private static Entity[] CaptureEntities(World world, int expectedCount)
    {
        ComponentId positionId = ComponentManager.GetId<Position>();
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

    private static void ResetStatics()
    {
        ComponentManager.Clear();
        PrefabRegistry.Clear();
    }

    private static EcsOptions NormalizeOptions(EcsOptions options)
    {
        if (!options.EnableStatistics
            && !options.EnableDebug
            && !options.EnableDirtyTracking
            && options.SystemExecutionMode == SystemExecutionMode.Serial
            && !options.EnablePhaseStaggering
            && options.MaxNonCriticalStagger == 0)
        {
            return new EcsOptions(
                enableStatistics: false,
                enableDebug: false,
                enableDirtyTracking: true);
        }

        return options;
    }

    private struct Position : IComponent<Position>
    {
        public int X;
        public int Y;

        public Position(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    private struct Velocity : IComponent<Velocity>
    {
        public int X;
        public int Y;

        public Velocity(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    private struct Health : IComponent<Health>
    {
        public int Value;

        public Health(int value)
        {
            Value = value;
        }
    }

    private struct Active : IComponent<Active>
    {
        public byte Value;
    }

    private sealed class SchedulerQueryReadSystem<TTag> : SystemBase
    {
        public SchedulerQueryReadSystem(string name)
        {
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([ComponentManager.GetId<Position>()]),
                ComponentMask.Empty);
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.World.Query<Position>((Entity _, ref Position position) =>
            {
                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, position.X);
            });
        }
    }

    private sealed class SchedulerQueryWriteSystem<TTag> : SystemBase
    {
        private readonly bool _writePosition;

        public SchedulerQueryWriteSystem(string name, bool writePosition)
        {
            _writePosition = writePosition;
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([ComponentManager.GetId<Position>(), ComponentManager.GetId<Velocity>()]),
                ComponentMask.FromIds([writePosition ? ComponentManager.GetId<Position>() : ComponentManager.GetId<Velocity>()]));
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.World.Query<Position, Velocity>((Entity _, ref Position position, ref Velocity velocity) =>
            {
                if (_writePosition)
                {
                    position.X += 1;
                    position.Y += 1;
                }
                else
                {
                    velocity.X += 1;
                    velocity.Y += 1;
                }

                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, position.X + velocity.X);
            });
        }
    }

    private sealed class SchedulerReadOnlySystem<TTag> : SystemBase
    {
        public SchedulerReadOnlySystem(string name)
        {
            Descriptor = new SystemDescriptor(
                name,
                SystemStage.AI,
                1,
                0,
                ComponentMask.FromIds([ComponentManager.GetId<Position>(), ComponentManager.GetId<Velocity>()]),
                ComponentMask.Empty);
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.World.Query<Position, Velocity>((Entity entity, ref Position position, ref Velocity velocity) =>
            {
                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, entity.Index + position.X + velocity.X);
            });
        }
    }

    private sealed class ReadOnlyTagA;
    private sealed class ReadOnlyTagB;
    private sealed class ReadOnlyTagC;
    private sealed class ReadOnlyTagD;

    private static class SchedulerBenchmarkState
    {
        public static long Checksum;

        public static void Reset() => Checksum = 0;
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
