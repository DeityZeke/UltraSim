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
            scenarios.Add(MakeScenario("DirtyDrain_Position", "DirtyTracking", true, capturedScale, () => BenchmarkDirtyDrain(capturedScale)));
            scenarios.Add(MakeScenario("PersistenceView_Filtered_Position", "PersistenceSeam", true, capturedScale, () => BenchmarkPersistenceView(capturedScale)));
            scenarios.Add(MakeScenario("CreateDefaultImmediate_Position", "Structural", false, capturedScale, () => BenchmarkCreateImmediate(capturedScale)));
            scenarios.Add(MakeScenario("PatchComponent_Position", "Mutation", false, capturedScale, () => BenchmarkPatchComponent(capturedScale)));
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

    private static World CreateWorld(bool includeVelocity, bool includeHealth)
    {
        var builder = new EcsSchemaBuilder();
        builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        builder.RegisterComponent<Health>();
        builder.RegisterComponent<Active>();
        return new World(builder.Build());
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

    private readonly record struct Position(int X, int Y) : IComponent<Position>;
    private readonly record struct Velocity(int X, int Y) : IComponent<Velocity>;
    private readonly record struct Health(int Value) : IComponent<Health>;
    private readonly record struct Active(byte Value) : IComponent<Active>;

    private sealed class SchedulerQueryReadSystem<TTag> : SystemBase
    {
        public SchedulerQueryReadSystem(string name, ComponentId positionId)
        {
            Descriptor = new SystemDescriptor(name, SystemStage.AI, 1, 0, ComponentMask.FromIds([positionId]), ComponentMask.Empty);
        }

        public override SystemDescriptor Descriptor { get; }

        public override void Update(in SystemContext ctx)
        {
            ctx.World.Query<Position>((Entity _, in Position position) =>
            {
                Interlocked.Add(ref SchedulerBenchmarkState.Checksum, position.X);
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
            ctx.World.Query<Position, Velocity>((Entity entity, in Position position, in Velocity velocity) =>
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
