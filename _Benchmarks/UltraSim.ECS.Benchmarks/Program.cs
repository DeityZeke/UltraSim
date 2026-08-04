using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;

string outputPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..",
    "results",
    "next-ecs-phase1.json"));

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length)
        outputPath = Path.GetFullPath(args[++i]);
}

BenchmarkReport report = PrimitiveBenchmarks.Run();
string? outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(outputDirectory))
    Directory.CreateDirectory(outputDirectory);

File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
}));

Console.WriteLine($"ECS Next Phase 1 benchmark: {report.Results.Count} scenarios");
foreach (BenchmarkResult result in report.Results)
{
    Console.WriteLine(
        $"{result.Name}: mean={result.MeanNanosecondsPerOperation:F3} ns/op " +
        $"p95={result.P95Milliseconds:F3} ms alloc={result.MeanAllocatedBytes} B");
}
Console.WriteLine($"Results: {outputPath}");

internal static class PrimitiveBenchmarks
{
    internal static BenchmarkReport Run()
    {
        var allocate1K = new AllocateFreeFixture(1_024);
        var allocate100K = new AllocateFreeFixture(100_000);
        var live = AliveFixture.CreateLive(100_000, scanCount: 32);
        var stale = AliveFixture.CreateStale(100_000, scanCount: 32);
        var foreign = AliveFixture.CreateForeign(100_000, scanCount: 32);
        var handleScan1M = new HandleScanFixture(1_000_000, scanCount: 8);
        var handleScan10M = new HandleScanFixture(10_000_000, scanCount: 2);
        var schemaLookup = new SchemaLookupFixture(10_000_000);
        var layoutPlanner = new LayoutPlannerFixture(100_000);
        var archetypeCapacity = new ArchetypeCapacityFixture(1_000_000);
        var worldCapacity = new WorldCapacityFixture(1_000_000);

        BenchmarkScenario[] scenarios =
        [
            new("EntityPool_AllocateFree_1024", "EntityLifecycle", 1_024, allocate1K.Execute),
            new("EntityPool_AllocateFree_100000", "EntityLifecycle", 100_000, allocate100K.Execute),
            new("EntityPool_IsAlive_Live_100000", "EntityValidation", 100_000, live.Execute),
            new("EntityPool_IsAlive_Stale_100000", "EntityValidation", 100_000, stale.Execute),
            new("EntityPool_IsAlive_Foreign_100000", "EntityValidation", 100_000, foreign.Execute),
            new("EntityHandle_Scan_12Byte_1000000", "HandleLayoutCandidate", 1_000_000, handleScan1M.ExecuteTwelveByte),
            new("EntityHandle_Scan_8Byte_1000000", "HandleLayoutCandidate", 1_000_000, handleScan1M.ExecuteEightByte),
            new("EntityHandle_Scan_12Byte_10000000", "HandleLayoutCandidate", 10_000_000, handleScan10M.ExecuteTwelveByte),
            new("EntityHandle_Scan_8Byte_10000000", "HandleLayoutCandidate", 10_000_000, handleScan10M.ExecuteEightByte),
            new("ComponentId_GeneratedGeneric_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteGeneratedGeneric),
            new("ComponentId_InstanceType_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteInstanceType),
            new("ComponentId_CanonicalKey_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteCanonicalKey),
            new("ComponentId_StableHash_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteStableHash),
            new("ComponentId_PersistenceId_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecutePersistenceId),
            new("ComponentMetadata_ById_10000000", "GeneratedSchema", 10_000_000, schemaLookup.ExecuteMetadataById),
            new("ChunkLayoutPlanner_Normal_100000", "CapacityPlanning", 100_000, layoutPlanner.Execute),
            new("ArchetypeCapacityPlan_Normal_1000000", "CapacityPlanning", 1_000_000, archetypeCapacity.Execute),
            new("WorldCapacityPlanner_ThreeArchetypes_1000000", "CapacityPlanning", 1_000_000, worldCapacity.Execute),
        ];

        var results = new List<BenchmarkResult>(scenarios.Length);
        foreach (BenchmarkScenario scenario in scenarios)
            results.Add(RunScenario(scenario));

        return new BenchmarkReport
        {
            SuiteName = "UltraSim.ECS.Next.Phase1",
            Generation = "Next",
            SemanticProfile = "ecs-next-phase1-layout-v3",
            RuntimeVersion = Environment.Version.ToString(),
            Configuration = GetConfiguration(),
            ProcessorCount = Environment.ProcessorCount,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OperatingSystem = RuntimeInformation.OSDescription,
            EntitySizeBytes = Marshal.SizeOf<Entity>(),
            ComponentIdSizeBytes = Marshal.SizeOf<ComponentId>(),
            ComponentMetadataSizeBytes = Marshal.SizeOf<ComponentMetadata>(),
            SchemaIdSizeBytes = Marshal.SizeOf<SchemaId>(),
            EstimatedEntityPoolMetadataBytesPerSlot = new EntityCapacity(1).EstimatedMetadataBytes,
            TimestampUtc = DateTimeOffset.UtcNow,
            LayoutPlans = LayoutReportBuilder.Create(),
            Results = results,
        };
    }

    private static BenchmarkResult RunScenario(BenchmarkScenario scenario)
    {
        const int warmupCount = 3;
        const int iterationCount = 31;

        for (int i = 0; i < warmupCount; i++)
            _ = scenario.Execute();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var elapsedMilliseconds = new double[iterationCount];
        var allocatedBytes = new long[iterationCount];
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long checksum = 0;
        long operations = 0;

        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            ScenarioMeasurement measurement = scenario.Execute();
            long stopped = Stopwatch.GetTimestamp();
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            operations = measurement.Operations;
            checksum ^= measurement.Checksum;
            elapsedMilliseconds[iteration] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
            allocatedBytes[iteration] = allocatedAfter - allocatedBefore;
        }

        double[] sorted = elapsedMilliseconds.ToArray();
        Array.Sort(sorted);
        double meanMilliseconds = elapsedMilliseconds.Average();

        return new BenchmarkResult
        {
            Name = scenario.Name,
            Category = scenario.Category,
            Comparable = false,
            Scale = scenario.Scale,
            WarmupCount = warmupCount,
            IterationCount = iterationCount,
            OperationsPerIteration = operations,
            MeanMilliseconds = meanMilliseconds,
            MinMilliseconds = sorted[0],
            P50Milliseconds = Percentile(sorted, 0.50),
            P95Milliseconds = Percentile(sorted, 0.95),
            P99Milliseconds = Percentile(sorted, 0.99),
            MaxMilliseconds = sorted[^1],
            MeanNanosecondsPerOperation = meanMilliseconds * 1_000_000d / operations,
            OperationsPerSecond = operations / (meanMilliseconds / 1000d),
            MeanAllocatedBytes = (long)allocatedBytes.Average(),
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            Gen1Collections = GC.CollectionCount(1) - gen1Before,
            Gen2Collections = GC.CollectionCount(2) - gen2Before,
            Checksum = checksum,
        };
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string GetConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private sealed class AllocateFreeFixture
    {
        private readonly EntityPool _pool;
        private readonly Entity[] _entities;

        internal AllocateFreeFixture(int capacity)
        {
            _pool = new EntityPool(WorldId.Create(), new EntityCapacity(capacity));
            _entities = new Entity[capacity];
            if (_pool.Allocate(_entities) != capacity)
                throw new InvalidOperationException("Fixture initialization failed.");
        }

        internal ScenarioMeasurement Execute()
        {
            int freed = _pool.Free(_entities);
            int allocated = _pool.Allocate(_entities);
            long checksum = _entities[0].Generation ^ _entities[^1].Generation ^ (uint)_pool.AliveCount;
            return new ScenarioMeasurement(freed + allocated, checksum);
        }
    }

    private sealed class AliveFixture
    {
        private readonly EntityPool _owner;
        private readonly Entity[] _entities;
        private readonly int _scanCount;

        private AliveFixture(EntityPool owner, Entity[] entities, int scanCount)
        {
            _owner = owner;
            _entities = entities;
            _scanCount = scanCount;
        }

        internal static AliveFixture CreateLive(int count, int scanCount)
        {
            var pool = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var entities = new Entity[count];
            pool.Allocate(entities);
            return new AliveFixture(pool, entities, scanCount);
        }

        internal static AliveFixture CreateStale(int count, int scanCount)
        {
            var pool = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var stale = new Entity[count];
            var live = new Entity[count];
            pool.Allocate(stale);
            pool.Free(stale);
            pool.Allocate(live);
            return new AliveFixture(pool, stale, scanCount);
        }

        internal static AliveFixture CreateForeign(int count, int scanCount)
        {
            var owner = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var foreign = new EntityPool(WorldId.Create(), new EntityCapacity(count));
            var entities = new Entity[count];
            foreign.Allocate(entities);
            return new AliveFixture(owner, entities, scanCount);
        }

        internal ScenarioMeasurement Execute()
        {
            long alive = 0;
            for (int scan = 0; scan < _scanCount; scan++)
            {
                for (int i = 0; i < _entities.Length; i++)
                {
                    if (_owner.IsAlive(_entities[i]))
                        alive++;
                }
            }

            return new ScenarioMeasurement((long)_entities.Length * _scanCount, alive);
        }
    }

    private sealed class HandleScanFixture
    {
        private readonly Entity[] _entities;
        private readonly PackedEntityCandidate[] _packed;
        private readonly int _scanCount;

        internal HandleScanFixture(int count, int scanCount)
        {
            var worldId = new WorldId(1);
            _entities = new Entity[count];
            _packed = new PackedEntityCandidate[count];
            _scanCount = scanCount;

            for (int i = 0; i < count; i++)
            {
                uint index = (uint)i;
                uint generation = (uint)(i & 0x00FF_FFFF) + 1;
                _entities[i] = new Entity(worldId, index, generation);
                _packed[i] = PackedEntityCandidate.Create(world: 1, index, generation);
            }
        }

        internal ScenarioMeasurement ExecuteTwelveByte()
        {
            ulong checksum = 0;
            for (int scan = 0; scan < _scanCount; scan++)
            {
                for (int i = 0; i < _entities.Length; i++)
                    checksum += _entities[i].Index + _entities[i].Generation + _entities[i].World.Value;
            }

            return new ScenarioMeasurement((long)_entities.Length * _scanCount, unchecked((long)checksum));
        }

        internal ScenarioMeasurement ExecuteEightByte()
        {
            ulong checksum = 0;
            for (int scan = 0; scan < _scanCount; scan++)
            {
                for (int i = 0; i < _packed.Length; i++)
                    checksum += _packed[i].Index + _packed[i].Generation + _packed[i].World;
            }

            return new ScenarioMeasurement((long)_packed.Length * _scanCount, unchecked((long)checksum));
        }
    }

    private sealed class SchemaLookupFixture
    {
        private readonly ComponentSchema _schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
        private readonly ComponentId _velocityId =
            UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>();
        private readonly int _operations;
        private readonly ulong _stableHash = StableHash64.Compute("benchmark.velocity");

        internal SchemaLookupFixture(int operations)
        {
            _operations = operations;
        }

        internal ScenarioMeasurement ExecuteGeneratedGeneric()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
                checksum += UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>().Value;
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteInstanceType()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>(out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteCanonicalKey()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentId("benchmark.velocity", out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteStableHash()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentId(_stableHash, out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecutePersistenceId()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                _schema.TryGetComponentIdByPersistenceId(1, out ComponentId id);
                checksum += id.Value;
            }
            return new ScenarioMeasurement(_operations, checksum);
        }

        internal ScenarioMeasurement ExecuteMetadataById()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
                checksum += _schema.GetMetadata(_velocityId).Size;
            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class LayoutPlannerFixture
    {
        private readonly ComponentSchema _schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
        private readonly ComponentId[] _signature = LayoutReportBuilder.CreateNormalSignature();
        private readonly ComponentColumnLayout[] _columns;
        private readonly int _operations;

        internal LayoutPlannerFixture(int operations)
        {
            _operations = operations;
            _columns = new ComponentColumnLayout[_signature.Length];
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(
                    _schema,
                    _signature,
                    ChunkLayoutOptions.KiB32,
                    _columns);
                checksum += plan.RowCapacity + plan.BytesUsed + _columns[0].DataOffsetBytes;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class ArchetypeCapacityFixture
    {
        private readonly ChunkLayoutPlan _layout;
        private readonly int _operations;

        internal ArchetypeCapacityFixture(int operations)
        {
            _operations = operations;
            ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
            ComponentId[] signature = LayoutReportBuilder.CreateNormalSignature();
            var columns = new ComponentColumnLayout[signature.Length];
            _layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns);
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                var plan = new ArchetypeCapacityPlan(_layout, 100_000 + (i & 1));
                checksum += plan.RequiredChunkCount + plan.ReservedChunkBytes;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private sealed class WorldCapacityFixture
    {
        private readonly ArchetypeCapacityPlan[] _archetypes;
        private readonly EntityCapacity _entityCapacity = new(500_000);
        private readonly WorldArenaReservations _arenas = new(8 * 1024 * 1024, 4 * 1024 * 1024, 2 * 1024 * 1024);
        private readonly int _operations;

        internal WorldCapacityFixture(int operations)
        {
            _operations = operations;
            ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
            ComponentId[] signature = LayoutReportBuilder.CreateNormalSignature();
            var columns = new ComponentColumnLayout[signature.Length];
            ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB32, columns);
            _archetypes =
            [
                new ArchetypeCapacityPlan(layout, 100_000),
                new ArchetypeCapacityPlan(layout, 50_000),
                new ArchetypeCapacityPlan(layout, 25_000),
            ];
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                WorldCapacityPlan plan = WorldCapacityPlanner.Plan(_entityCapacity, _archetypes, _arenas);
                checksum += plan.RequiredChunkCount + plan.TotalReservedBytes;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }
    }

    private readonly struct PackedEntityCandidate
    {
        private const ulong IndexMask = 0x00FF_FFFFUL;
        private const ulong GenerationMask = 0x00FF_FFFFUL;
        private readonly ulong _value;

        private PackedEntityCandidate(ulong value)
        {
            _value = value;
        }

        internal uint Index => (uint)(_value & IndexMask);

        internal uint Generation => (uint)((_value >> 24) & GenerationMask);

        internal ushort World => (ushort)(_value >> 48);

        internal static PackedEntityCandidate Create(ushort world, uint index, uint generation)
        {
            ulong value = index & IndexMask;
            value |= ((ulong)generation & GenerationMask) << 24;
            value |= (ulong)world << 48;
            return new PackedEntityCandidate(value);
        }
    }
}

internal readonly record struct BenchmarkScenario(
    string Name,
    string Category,
    int Scale,
    Func<ScenarioMeasurement> Execute);

internal readonly record struct ScenarioMeasurement(long Operations, long Checksum);

internal sealed class BenchmarkReport
{
    public required string SuiteName { get; init; }
    public required string Generation { get; init; }
    public required string SemanticProfile { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string Configuration { get; init; }
    public required int ProcessorCount { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required string OperatingSystem { get; init; }
    public required int EntitySizeBytes { get; init; }
    public required int ComponentIdSizeBytes { get; init; }
    public required int ComponentMetadataSizeBytes { get; init; }
    public required int SchemaIdSizeBytes { get; init; }
    public required long EstimatedEntityPoolMetadataBytesPerSlot { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required IReadOnlyList<LayoutPlanFact> LayoutPlans { get; init; }
    public required IReadOnlyList<BenchmarkResult> Results { get; init; }
}

internal static class LayoutReportBuilder
{
    private const int ExpectedPopulation = 100_000;

    internal static IReadOnlyList<LayoutPlanFact> Create()
    {
        ComponentSchema schema = UltraSim.ECS.Benchmarks.BenchmarkSchema.CreateSchema();
        (string Name, ComponentId[] Signature)[] profiles =
        [
            ("Narrow", CreateSortedSignature(
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkPosition>())),
            ("Normal", CreateNormalSignature()),
            ("Wide", CreateSortedSignature(
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkDisabled>(),
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkPosition>(),
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>(),
                UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkWide>())),
        ];
        ChunkLayoutOptions[] candidates =
        [
            ChunkLayoutOptions.KiB16,
            ChunkLayoutOptions.KiB32,
            ChunkLayoutOptions.KiB64,
        ];
        var facts = new List<LayoutPlanFact>(profiles.Length * candidates.Length);
        foreach ((string profileName, ComponentId[] signature) in profiles)
        {
            foreach (ChunkLayoutOptions options in candidates)
            {
                var columns = new ComponentColumnLayout[signature.Length];
                ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, options, columns);
                var capacity = new ArchetypeCapacityPlan(layout, ExpectedPopulation);
                var columnFacts = new LayoutColumnFact[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                {
                    ComponentColumnLayout column = columns[i];
                    columnFacts[i] = new LayoutColumnFact
                    {
                        ComponentId = column.ComponentId.Value,
                        CanonicalKey = schema.GetCanonicalKey(column.ComponentId),
                        Flags = column.Flags.ToString(),
                        ElementSizeBytes = column.ElementSizeBytes,
                        EffectiveAlignmentBytes = column.EffectiveAlignmentBytes,
                        DataOffsetBytes = column.DataOffsetBytes,
                        DataBytes = column.DataBytes,
                        EnableBitsOffsetBytes = column.EnableBitsOffsetBytes,
                        EnableBitsBytes = column.EnableBitsBytes,
                    };
                }

                facts.Add(new LayoutPlanFact
                {
                    Profile = profileName,
                    ChunkSizeBytes = options.ChunkSizeBytes,
                    ReservedHeaderBytes = options.ReservedHeaderBytes,
                    MinimumColumnAlignmentBytes = options.MinimumColumnAlignmentBytes,
                    RequiredChunkAlignmentBytes = layout.RequiredChunkAlignmentBytes,
                    RowCapacity = layout.RowCapacity,
                    FixedRowBytes = layout.FixedRowBytes,
                    BytesUsed = layout.BytesUsed,
                    AlignmentPaddingBytes = layout.AlignmentPaddingBytes,
                    TailUnusedBytes = layout.TailUnusedBytes,
                    EnableBitsetBytes = layout.EnableBitsetBytes,
                    ExpectedPopulation = ExpectedPopulation,
                    RequiredChunkCount = capacity.RequiredChunkCount,
                    ReservedChunkBytes = capacity.ReservedChunkBytes,
                    UnusedRowSlots = capacity.UnusedRowSlots,
                    Columns = columnFacts,
                });
            }
        }

        return facts;
    }

    internal static ComponentId[] CreateNormalSignature() => CreateSortedSignature(
        UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkDisabled>(),
        UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkPosition>(),
        UltraSim.ECS.Benchmarks.BenchmarkSchema.GetComponentId<UltraSim.ECS.Benchmarks.BenchmarkVelocity>());

    private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
    {
        Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
        return componentIds;
    }
}

internal sealed class LayoutPlanFact
{
    public required string Profile { get; init; }
    public required int ChunkSizeBytes { get; init; }
    public required int ReservedHeaderBytes { get; init; }
    public required int MinimumColumnAlignmentBytes { get; init; }
    public required int RequiredChunkAlignmentBytes { get; init; }
    public required int RowCapacity { get; init; }
    public required int FixedRowBytes { get; init; }
    public required int BytesUsed { get; init; }
    public required int AlignmentPaddingBytes { get; init; }
    public required int TailUnusedBytes { get; init; }
    public required int EnableBitsetBytes { get; init; }
    public required int ExpectedPopulation { get; init; }
    public required int RequiredChunkCount { get; init; }
    public required long ReservedChunkBytes { get; init; }
    public required long UnusedRowSlots { get; init; }
    public required IReadOnlyList<LayoutColumnFact> Columns { get; init; }
}

internal sealed class LayoutColumnFact
{
    public required ushort ComponentId { get; init; }
    public required string CanonicalKey { get; init; }
    public required string Flags { get; init; }
    public required int ElementSizeBytes { get; init; }
    public required int EffectiveAlignmentBytes { get; init; }
    public required int DataOffsetBytes { get; init; }
    public required int DataBytes { get; init; }
    public required int EnableBitsOffsetBytes { get; init; }
    public required int EnableBitsBytes { get; init; }
}

internal sealed class BenchmarkResult
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool Comparable { get; init; }
    public required int Scale { get; init; }
    public required int WarmupCount { get; init; }
    public required int IterationCount { get; init; }
    public required long OperationsPerIteration { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MinMilliseconds { get; init; }
    public required double P50Milliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double P99Milliseconds { get; init; }
    public required double MaxMilliseconds { get; init; }
    public required double MeanNanosecondsPerOperation { get; init; }
    public required double OperationsPerSecond { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
}
