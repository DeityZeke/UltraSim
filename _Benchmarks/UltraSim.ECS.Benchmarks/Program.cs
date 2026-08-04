using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.ECS.Benchmarks;

string outputPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..",
    "results",
    "next-ecs-phase1.json"));
string? scenarioFilter = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length)
        outputPath = Path.GetFullPath(args[++i]);
    else if (args[i] == "--filter" && i + 1 < args.Length)
        scenarioFilter = args[++i];
}

BenchmarkReport report = PrimitiveBenchmarks.Run(scenarioFilter);
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
    internal static BenchmarkReport Run(string? scenarioFilter)
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
        using var nativeLifecycle16 = new NativeArenaLifecycleFixture(ChunkLayoutOptions.KiB16, 2_048);
        using var pinnedLifecycle16 = new PinnedArenaLifecycleFixture(ChunkLayoutOptions.KiB16, 2_048);
        using var nativeLifecycle32 = new NativeArenaLifecycleFixture(ChunkLayoutOptions.KiB32, 1_024);
        using var pinnedLifecycle32 = new PinnedArenaLifecycleFixture(ChunkLayoutOptions.KiB32, 1_024);
        using var nativeLifecycle64 = new NativeArenaLifecycleFixture(ChunkLayoutOptions.KiB64, 512);
        using var pinnedLifecycle64 = new PinnedArenaLifecycleFixture(ChunkLayoutOptions.KiB64, 512);
        using var nativeTraversal = new NativeArenaTraversalFixture(100_000);
        using var pinnedTraversal = new PinnedArenaTraversalFixture(100_000);
        using var nativeClear = new NativeArenaClearFixture(wholeChunkOperations: 1_024, activeRowOperations: 100_000);
        using var pinnedClear = new PinnedArenaClearFixture(wholeChunkOperations: 1_024, activeRowOperations: 100_000);
        using var nativeBulkRows = new ArchetypeBulkAddFixture<NativeChunkArena>(100_000);
        using var pinnedBulkRows = new ArchetypeBulkAddFixture<PinnedChunkArena>(100_000);
        using var nativeRowChurn = new ArchetypeSwapChurnFixture<NativeChunkArena>(100_000, 100_000);
        using var pinnedRowChurn = new ArchetypeSwapChurnFixture<PinnedChunkArena>(100_000, 100_000);
        using var nativeEnableToggle = new ArchetypeEnableToggleFixture<NativeChunkArena>(100_000, 1_000_000);
        using var pinnedEnableToggle = new ArchetypeEnableToggleFixture<PinnedChunkArena>(100_000, 1_000_000);

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
            new("NativeArena_AcquireClearRelease_16KiB", "ChunkArenaLifecycle", 2_048, nativeLifecycle16.Execute),
            new("PinnedArena_AcquireClearRelease_16KiB", "ChunkArenaLifecycle", 2_048, pinnedLifecycle16.Execute),
            new("NativeArena_AcquireClearRelease_32KiB", "ChunkArenaLifecycle", 1_024, nativeLifecycle32.Execute),
            new("PinnedArena_AcquireClearRelease_32KiB", "ChunkArenaLifecycle", 1_024, pinnedLifecycle32.Execute),
            new("NativeArena_AcquireClearRelease_64KiB", "ChunkArenaLifecycle", 512, nativeLifecycle64.Execute),
            new("PinnedArena_AcquireClearRelease_64KiB", "ChunkArenaLifecycle", 512, pinnedLifecycle64.Execute),
            new("NativeArena_PositionTraversal_100000", "ChunkTraversal", 100_000, nativeTraversal.Execute),
            new("PinnedArena_PositionTraversal_100000", "ChunkTraversal", 100_000, pinnedTraversal.Execute),
            new("NativeArena_ClearWhole_32KiB", "ChunkClear", 1_024, nativeClear.ExecuteWholeChunk),
            new("PinnedArena_ClearWhole_32KiB", "ChunkClear", 1_024, pinnedClear.ExecuteWholeChunk),
            new("NativeArena_ClearActiveRows_32KiB", "ChunkClear", 100_000, nativeClear.ExecuteActiveRows),
            new("PinnedArena_ClearActiveRows_32KiB", "ChunkClear", 100_000, pinnedClear.ExecuteActiveRows),
            new("NativeArchetype_BulkAddDefault_100000", "ArchetypeRows", 100_000, nativeBulkRows.Execute, nativeBulkRows.Prepare),
            new("PinnedArchetype_BulkAddDefault_100000", "ArchetypeRows", 100_000, pinnedBulkRows.Execute, pinnedBulkRows.Prepare),
            new("NativeArchetype_SwapRemoveAdd_100000Cycles", "ArchetypeRows", 100_000, nativeRowChurn.Execute),
            new("PinnedArchetype_SwapRemoveAdd_100000Cycles", "ArchetypeRows", 100_000, pinnedRowChurn.Execute),
            new("NativeArchetype_EnableToggle_1000000", "ArchetypeRows", 1_000_000, nativeEnableToggle.Execute),
            new("PinnedArchetype_EnableToggle_1000000", "ArchetypeRows", 1_000_000, pinnedEnableToggle.Execute),
        ];

        var results = new List<BenchmarkResult>(scenarios.Length);
        foreach (BenchmarkScenario scenario in scenarios)
        {
            if (!MatchesFilter(scenario, scenarioFilter))
                continue;
            results.Add(RunScenario(scenario));
        }
        if (results.Count == 0 && !string.IsNullOrWhiteSpace(scenarioFilter))
            throw new ArgumentException($"No benchmark scenario matched filter '{scenarioFilter}'.", nameof(scenarioFilter));

        return new BenchmarkReport
        {
            SuiteName = "UltraSim.ECS.Next.Phase1",
            Generation = "Next",
            SemanticProfile = "ecs-next-phase1-archetype-rows-v5",
            ScenarioFilter = scenarioFilter,
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
            ArenaFacts = ArenaFactBuilder.Create(),
            ArchetypeFacts = ArchetypeFactBuilder.Create(),
            Results = results,
        };
    }

    private static bool MatchesFilter(in BenchmarkScenario scenario, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        scenario.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        scenario.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static BenchmarkResult RunScenario(BenchmarkScenario scenario)
    {
        const int warmupCount = 8;
        const int iterationCount = 31;

        for (int i = 0; i < warmupCount; i++)
        {
            scenario.Prepare?.Invoke();
            _ = scenario.Execute();
        }

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
            scenario.Prepare?.Invoke();
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

    private sealed class NativeArenaLifecycleFixture : IDisposable
    {
        private readonly NativeChunkArena _arena;
        private readonly int _operations;
        private ChunkHandle _handle;

        internal NativeArenaLifecycleFixture(ChunkLayoutOptions options, int operations)
        {
            _arena = new NativeChunkArena(new ChunkArenaCapacity(
                options.ChunkSizeBytes,
                options.MinimumColumnAlignmentBytes,
                maximumChunks: 1));
            _handle = _arena.Acquire();
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            ulong checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                if (!_arena.TryRelease(_handle) || !_arena.TryAcquire(out _handle))
                    throw new InvalidOperationException("Native lifecycle fixture lost its only chunk.");
                checksum += _handle.Generation;
            }

            return new ScenarioMeasurement(_operations, unchecked((long)checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class PinnedArenaLifecycleFixture : IDisposable
    {
        private readonly PinnedChunkArena _arena;
        private readonly int _operations;
        private ChunkHandle _handle;

        internal PinnedArenaLifecycleFixture(ChunkLayoutOptions options, int operations)
        {
            _arena = new PinnedChunkArena(new ChunkArenaCapacity(
                options.ChunkSizeBytes,
                options.MinimumColumnAlignmentBytes,
                maximumChunks: 1));
            _handle = _arena.Acquire();
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            ulong checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                if (!_arena.TryRelease(_handle) || !_arena.TryAcquire(out _handle))
                    throw new InvalidOperationException("Pinned lifecycle fixture lost its only chunk.");
                checksum += _handle.Generation;
            }

            return new ScenarioMeasurement(_operations, unchecked((long)checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class NativeArenaTraversalFixture : IDisposable
    {
        private readonly NativeChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle[] _chunks;
        private readonly int[] _rowCounts;
        private readonly int _population;

        internal NativeArenaTraversalFixture(int population)
        {
            LayoutReportBuilder.CreateNormalLayout(
                ChunkLayoutOptions.KiB32,
                out _layout,
                out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            var capacityPlan = new ArchetypeCapacityPlan(_layout, population);
            _arena = new NativeChunkArena(ChunkArenaCapacity.FromLayout(_layout, capacityPlan.RequiredChunkCount));
            _chunks = new ChunkHandle[capacityPlan.RequiredChunkCount];
            _rowCounts = new int[_chunks.Length];
            _population = population;
            Initialize();
        }

        private void Initialize()
        {
            int remaining = _population;
            int ordinal = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                ChunkHandle chunk = _arena.Acquire();
                int rows = Math.Min(remaining, _layout.RowCapacity);
                Span<BenchmarkPosition> positions = _arena.GetColumn<BenchmarkPosition>(chunk, _layout, _positionColumn);
                for (int row = 0; row < rows; row++, ordinal++)
                    positions[row] = new BenchmarkPosition { X = ordinal, Y = ordinal * 0.5f };
                _chunks[chunkIndex] = chunk;
                _rowCounts[chunkIndex] = rows;
                remaining -= rows;
            }
        }

        internal ScenarioMeasurement Execute()
        {
            double checksum = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                Span<BenchmarkPosition> positions = _arena
                    .GetColumn<BenchmarkPosition>(_chunks[chunkIndex], _layout, _positionColumn)
                    [.._rowCounts[chunkIndex]];
                for (int row = 0; row < positions.Length; row++)
                    checksum += positions[row].X + positions[row].Y;
            }

            return new ScenarioMeasurement(_population, BitConverter.DoubleToInt64Bits(checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class PinnedArenaTraversalFixture : IDisposable
    {
        private readonly PinnedChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle[] _chunks;
        private readonly int[] _rowCounts;
        private readonly int _population;

        internal PinnedArenaTraversalFixture(int population)
        {
            LayoutReportBuilder.CreateNormalLayout(
                ChunkLayoutOptions.KiB32,
                out _layout,
                out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            var capacityPlan = new ArchetypeCapacityPlan(_layout, population);
            _arena = new PinnedChunkArena(ChunkArenaCapacity.FromLayout(_layout, capacityPlan.RequiredChunkCount));
            _chunks = new ChunkHandle[capacityPlan.RequiredChunkCount];
            _rowCounts = new int[_chunks.Length];
            _population = population;
            Initialize();
        }

        private void Initialize()
        {
            int remaining = _population;
            int ordinal = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                ChunkHandle chunk = _arena.Acquire();
                int rows = Math.Min(remaining, _layout.RowCapacity);
                Span<BenchmarkPosition> positions = _arena.GetColumn<BenchmarkPosition>(chunk, _layout, _positionColumn);
                for (int row = 0; row < rows; row++, ordinal++)
                    positions[row] = new BenchmarkPosition { X = ordinal, Y = ordinal * 0.5f };
                _chunks[chunkIndex] = chunk;
                _rowCounts[chunkIndex] = rows;
                remaining -= rows;
            }
        }

        internal ScenarioMeasurement Execute()
        {
            double checksum = 0;
            for (int chunkIndex = 0; chunkIndex < _chunks.Length; chunkIndex++)
            {
                Span<BenchmarkPosition> positions = _arena
                    .GetColumn<BenchmarkPosition>(_chunks[chunkIndex], _layout, _positionColumn)
                    [.._rowCounts[chunkIndex]];
                for (int row = 0; row < positions.Length; row++)
                    checksum += positions[row].X + positions[row].Y;
            }

            return new ScenarioMeasurement(_population, BitConverter.DoubleToInt64Bits(checksum));
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class NativeArenaClearFixture : IDisposable
    {
        private readonly NativeChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle _chunk;
        private readonly int _wholeChunkOperations;
        private readonly int _activeRowOperations;
        private readonly int _activeRows;

        internal NativeArenaClearFixture(int wholeChunkOperations, int activeRowOperations)
        {
            LayoutReportBuilder.CreateNormalLayout(ChunkLayoutOptions.KiB32, out _layout, out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            _arena = new NativeChunkArena(ChunkArenaCapacity.FromLayout(_layout, maximumChunks: 1));
            _chunk = _arena.Acquire();
            _wholeChunkOperations = wholeChunkOperations;
            _activeRowOperations = activeRowOperations;
            _activeRows = Math.Max(1, _layout.RowCapacity / 10);
        }

        internal ScenarioMeasurement ExecuteWholeChunk()
        {
            for (int i = 0; i < _wholeChunkOperations; i++)
                _arena.ClearChunk(_chunk);
            return new ScenarioMeasurement(_wholeChunkOperations, _layout.BytesUsed);
        }

        internal ScenarioMeasurement ExecuteActiveRows()
        {
            Span<BenchmarkPosition> active = _arena
                .GetColumn<BenchmarkPosition>(_chunk, _layout, _positionColumn)
                [.._activeRows];
            for (int i = 0; i < _activeRowOperations; i++)
                active.Clear();
            return new ScenarioMeasurement(_activeRowOperations, _activeRows);
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class PinnedArenaClearFixture : IDisposable
    {
        private readonly PinnedChunkArena _arena;
        private readonly ChunkLayoutPlan _layout;
        private readonly ComponentColumnLayout _positionColumn;
        private readonly ChunkHandle _chunk;
        private readonly int _wholeChunkOperations;
        private readonly int _activeRowOperations;
        private readonly int _activeRows;

        internal PinnedArenaClearFixture(int wholeChunkOperations, int activeRowOperations)
        {
            LayoutReportBuilder.CreateNormalLayout(ChunkLayoutOptions.KiB32, out _layout, out ComponentColumnLayout[] columns);
            _positionColumn = LayoutReportBuilder.FindColumn<BenchmarkPosition>(columns);
            _arena = new PinnedChunkArena(ChunkArenaCapacity.FromLayout(_layout, maximumChunks: 1));
            _chunk = _arena.Acquire();
            _wholeChunkOperations = wholeChunkOperations;
            _activeRowOperations = activeRowOperations;
            _activeRows = Math.Max(1, _layout.RowCapacity / 10);
        }

        internal ScenarioMeasurement ExecuteWholeChunk()
        {
            for (int i = 0; i < _wholeChunkOperations; i++)
                _arena.ClearChunk(_chunk);
            return new ScenarioMeasurement(_wholeChunkOperations, _layout.BytesUsed);
        }

        internal ScenarioMeasurement ExecuteActiveRows()
        {
            Span<BenchmarkPosition> active = _arena
                .GetColumn<BenchmarkPosition>(_chunk, _layout, _positionColumn)
                [.._activeRows];
            for (int i = 0; i < _activeRowOperations; i++)
                active.Clear();
            return new ScenarioMeasurement(_activeRowOperations, _activeRows);
        }

        public void Dispose() => _arena.Dispose();
    }

    private sealed class ArchetypeBulkAddFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeChunkStore<TArena> _store;
        private readonly uint[] _entityIndices;
        private readonly ChunkRow[] _locations;

        internal ArchetypeBulkAddFixture(int population)
        {
            _store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out _);
            _entityIndices = new uint[population];
            _locations = new ChunkRow[population];
            for (int i = 0; i < population; i++)
                _entityIndices[i] = (uint)i;
        }

        internal void Prepare()
        {
            for (int i = _store.Count - 1; i >= 0; i--)
            {
                if (!_store.TryRemove(_locations[i], _entityIndices[i], out ChunkRowRelocation relocation) || relocation.HasValue)
                    throw new InvalidOperationException("Descending bulk fixture removal violated dense insertion order.");
            }
        }

        internal ScenarioMeasurement Execute()
        {
            int added = _store.Add(_entityIndices, _locations);
            long checksum = _store.Count + _locations[0].Row + _locations[^1].Row;
            return new ScenarioMeasurement(added, checksum);
        }

        public void Dispose() => _store.Dispose();
    }

    private sealed class ArchetypeSwapChurnFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeChunkStore<TArena> _store;
        private readonly ChunkRow[] _locations;
        private readonly int _cycles;

        internal ArchetypeSwapChurnFixture(int population, int cycles)
        {
            _store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out _);
            _locations = new ChunkRow[population];
            var entityIndices = new uint[population];
            for (int i = 0; i < population; i++)
                entityIndices[i] = (uint)i;
            if (_store.Add(entityIndices, _locations) != population)
                throw new InvalidOperationException("Swap-churn fixture initialization exhausted its store.");
            _cycles = cycles;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int cycle = 0; cycle < _cycles; cycle++)
            {
                uint entityIndex = (uint)cycle % (uint)_locations.Length;
                if (!_store.TryRemove(_locations[entityIndex], entityIndex, out ChunkRowRelocation relocation))
                    throw new InvalidOperationException("Swap-churn fixture lost a live entity.");
                if (relocation.HasValue)
                    _locations[relocation.EntityIndex] = relocation.Destination;
                if (!_store.TryAdd(entityIndex, out _locations[entityIndex]))
                    throw new InvalidOperationException("Swap-churn fixture failed to restore capacity.");
                checksum += _locations[entityIndex].Row + (relocation.HasValue ? 1 : 0);
            }

            return new ScenarioMeasurement(_cycles, checksum);
        }

        public void Dispose() => _store.Dispose();
    }

    private sealed class ArchetypeEnableToggleFixture<TArena> : IDisposable
        where TArena : class, IChunkArenaBackend
    {
        private readonly ArchetypeChunkStore<TArena> _store;
        private readonly int _enableColumnIndex;
        private readonly ChunkRow[] _locations;
        private readonly int _operations;

        internal ArchetypeEnableToggleFixture(int population, int operations)
        {
            _store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out ComponentColumnLayout[] columns);
            ComponentColumnLayout enableColumn = LayoutReportBuilder.FindColumn<BenchmarkVelocity>(columns);
            _enableColumnIndex = _store.GetColumnIndex(enableColumn.ComponentId);
            _locations = new ChunkRow[population];
            var entityIndices = new uint[population];
            for (int i = 0; i < population; i++)
                entityIndices[i] = (uint)i;
            if (_store.Add(entityIndices, _locations) != population)
                throw new InvalidOperationException("Enable-toggle fixture initialization exhausted its store.");
            _operations = operations;
        }

        internal ScenarioMeasurement Execute()
        {
            long checksum = 0;
            for (int i = 0; i < _operations; i++)
            {
                ChunkRow location = _locations[i % _locations.Length];
                bool enabled = (i & 1) == 0;
                _store.SetEnabled(location, _enableColumnIndex, enabled);
                checksum += enabled ? 1 : 0;
            }

            return new ScenarioMeasurement(_operations, checksum);
        }

        public void Dispose() => _store.Dispose();
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
    Func<ScenarioMeasurement> Execute,
    Action? Prepare = null);

internal readonly record struct ScenarioMeasurement(long Operations, long Checksum);

internal sealed class BenchmarkReport
{
    public required string SuiteName { get; init; }
    public required string Generation { get; init; }
    public required string SemanticProfile { get; init; }
    public string? ScenarioFilter { get; init; }
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
    public required IReadOnlyList<ArenaFact> ArenaFacts { get; init; }
    public required IReadOnlyList<ArchetypeFact> ArchetypeFacts { get; init; }
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

    internal static void CreateNormalLayout(
        ChunkLayoutOptions options,
        out ChunkLayoutPlan layout,
        out ComponentColumnLayout[] columns)
    {
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        ComponentId[] signature = CreateNormalSignature();
        columns = new ComponentColumnLayout[signature.Length];
        layout = ChunkLayoutPlanner.Plan(schema, signature, options, columns);
    }

    internal static ComponentColumnLayout FindColumn<T>(ComponentColumnLayout[] columns)
        where T : unmanaged, IComponent<T>
    {
        ComponentId componentId = BenchmarkSchema.GetComponentId<T>();
        for (int i = 0; i < columns.Length; i++)
        {
            if (columns[i].ComponentId == componentId)
                return columns[i];
        }

        throw new InvalidOperationException($"Layout does not contain {typeof(T).Name}.");
    }

    private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
    {
        Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
        return componentIds;
    }
}

internal static class ArenaFactBuilder
{
    internal static IReadOnlyList<ArenaFact> Create()
    {
        ChunkLayoutOptions[] candidates =
        [
            ChunkLayoutOptions.KiB16,
            ChunkLayoutOptions.KiB32,
            ChunkLayoutOptions.KiB64,
        ];
        var facts = new List<ArenaFact>(candidates.Length * 2);
        foreach (ChunkLayoutOptions options in candidates)
        {
            var capacity = new ChunkArenaCapacity(
                options.ChunkSizeBytes,
                options.MinimumColumnAlignmentBytes,
                maximumChunks: 8);
            using (var native = new NativeChunkArena(capacity))
                facts.Add(CreateFact(native.GetSnapshot(), native.BaseAddress, capacity.TotalPayloadBytes));
            using (var pinned = new PinnedChunkArena(capacity))
            {
                facts.Add(CreateFact(
                    pinned.GetSnapshot(),
                    pinned.BaseAddress,
                    capacity.TotalPayloadBytes + capacity.ChunkAlignmentBytes - 1L));
            }
        }

        return facts;
    }

    private static ArenaFact CreateFact(ChunkArenaSnapshot snapshot, nuint baseAddress, long backingAllocationBytes) => new()
    {
        MemoryKind = snapshot.MemoryKind.ToString(),
        ChunkSizeBytes = snapshot.Capacity.ChunkSizeBytes,
        ChunkAlignmentBytes = snapshot.Capacity.ChunkAlignmentBytes,
        MaximumChunks = snapshot.Capacity.MaximumChunks,
        PayloadBytes = snapshot.Capacity.TotalPayloadBytes,
        BackingAllocationBytes = backingAllocationBytes,
        EstimatedSlotMetadataBytes = snapshot.Capacity.EstimatedSlotMetadataBytes,
        BaseAddressAligned = baseAddress % (nuint)snapshot.Capacity.ChunkAlignmentBytes == 0,
    };
}

internal static class ArchetypeBenchmarkFactory
{
    internal static ArchetypeChunkStore<TArena> CreateStore<TArena>(
        int population,
        out ChunkLayoutPlan layout,
        out ComponentColumnLayout[] columns)
        where TArena : class, IChunkArenaBackend
    {
        LayoutReportBuilder.CreateNormalLayout(ChunkLayoutOptions.KiB32, out layout, out columns);
        var capacityPlan = new ArchetypeCapacityPlan(layout, population);
        ChunkArenaCapacity capacity = ChunkArenaCapacity.FromLayout(layout, capacityPlan.RequiredChunkCount);
        object arena = typeof(TArena) == typeof(NativeChunkArena)
            ? new NativeChunkArena(capacity)
            : typeof(TArena) == typeof(PinnedChunkArena)
                ? new PinnedChunkArena(capacity)
                : throw new InvalidOperationException($"Unsupported benchmark arena {typeof(TArena).Name}.");
        return new ArchetypeChunkStore<TArena>((TArena)arena, layout, columns);
    }
}

internal static class ArchetypeFactBuilder
{
    internal static IReadOnlyList<ArchetypeFact> Create()
    {
        const int population = 100_000;
        return
        [
            Create<NativeChunkArena>(population),
            Create<PinnedChunkArena>(population),
        ];
    }

    private static ArchetypeFact Create<TArena>(int population)
        where TArena : class, IChunkArenaBackend
    {
        using ArchetypeChunkStore<TArena> store = ArchetypeBenchmarkFactory.CreateStore<TArena>(population, out _, out _);
        ArchetypeChunkSnapshot snapshot = store.GetSnapshot();
        return new ArchetypeFact
        {
            MemoryKind = store.Arena.GetSnapshot().MemoryKind.ToString(),
            ExpectedPopulation = population,
            RowCapacity = snapshot.RowCapacity,
            MaximumChunks = snapshot.MaximumChunks,
            MaximumRows = snapshot.MaximumRows,
            ChunkPayloadBytes = store.Arena.Capacity.TotalPayloadBytes,
            ChunkRecordSizeBytes = snapshot.ChunkRecordSizeBytes,
            ChunkMetadataBytes = snapshot.ChunkMetadataBytes,
            ChunkRowSizeBytes = Marshal.SizeOf<ChunkRow>(),
        };
    }
}

internal sealed class ArchetypeFact
{
    public required string MemoryKind { get; init; }
    public required int ExpectedPopulation { get; init; }
    public required int RowCapacity { get; init; }
    public required int MaximumChunks { get; init; }
    public required long MaximumRows { get; init; }
    public required long ChunkPayloadBytes { get; init; }
    public required int ChunkRecordSizeBytes { get; init; }
    public required long ChunkMetadataBytes { get; init; }
    public required int ChunkRowSizeBytes { get; init; }
}

internal sealed class ArenaFact
{
    public required string MemoryKind { get; init; }
    public required int ChunkSizeBytes { get; init; }
    public required int ChunkAlignmentBytes { get; init; }
    public required int MaximumChunks { get; init; }
    public required long PayloadBytes { get; init; }
    public required long BackingAllocationBytes { get; init; }
    public required long EstimatedSlotMetadataBytes { get; init; }
    public required bool BaseAddressAligned { get; init; }
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
