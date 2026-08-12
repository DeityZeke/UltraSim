using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Text.Json;

namespace UltraSim.ECS.Benchmarks;

internal static class ContentBenchmarkProgram
{
    private const string Profile = "ecs-3.3.0-compiled-content-authoring";

    internal static int Run(string[] args)
    {
        bool quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);
        string output = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "results", "ecs-content-candidate.json"));
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--output" && index + 1 < args.Length)
                output = Path.GetFullPath(args[++index]);
        }

        Warmup();
        var compile = new List<ContentCompileBenchmark>();
        int[] prefabCounts = quick ? [10, 100] : [10, 100, 1_000, 10_000];
        int[] signatureCounts = [1, 8, 32, 128];
        foreach (int prefabs in prefabCounts)
        {
            foreach (int signatures in signatureCounts)
            {
                if (signatures > prefabs)
                    continue;
                compile.Add(MeasureCompile(prefabs, signatures, quick ? 2 : prefabs == 10_000 ? 3 : 7));
            }
        }

        int normalScale = quick ? 10_000 : 100_000;
        int largeScale = quick ? 50_000 : 1_000_000;
        int samples = quick ? 2 : 7;
        var spawn = new List<ContentSpawnBenchmark>
        {
            MeasureOrdinaryCreate<ContentPayload16>(normalScale, samples),
            MeasureImmediate<ContentPayload16>("Immediate_OnePrefab_OneSignature", normalScale, 1, 1, samples),
            MeasureImmediate<ContentPayload16>("Immediate_128Prefabs_OneSignature", normalScale, 128, 1, samples),
            MeasureImmediate<ContentPayload16>("Immediate_128Prefabs_128Signatures", normalScale, 128, 128, samples),
            MeasureImmediate<ContentPayload16>("Immediate_OnePrefab_OneSignature", largeScale, 1, 1, quick ? 2 : 5),
            MeasureImmediate<ContentPayload16>("DefaultLadder_16B", normalScale, 1, 1, samples),
            MeasureImmediate<ContentPayload64>("DefaultLadder_64B", normalScale, 1, 1, samples),
            MeasureImmediate<ContentPayload256>("DefaultLadder_256B", normalScale, 1, 1, samples),
            MeasureImmediate<ContentPayload1024>("DefaultLadder_1024B_Wide", normalScale, 1, 1, samples),
        };
        spawn.AddRange(MeasureDeferred<ContentPayload16>(normalScale, prefabCount: 1, samples));
        spawn.AddRange(MeasureDeferred<ContentPayload1024>(normalScale, prefabCount: 1, samples));

        ContentLookupBenchmark lookup = MeasureLookup(quick ? 100_000 : 10_000_000, quick ? 3 : 11);
        var report = new ContentBenchmarkReport
        {
            Profile = Profile,
            TimestampUtc = DateTimeOffset.UtcNow,
            Configuration = GetConfiguration(),
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            TieredCompilationSetting = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime-default",
            ReadyToRunSetting = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "runtime-default",
            ServerGarbageCollection = GCSettings.IsServerGC,
            Quick = quick,
            Compile = compile,
            Spawn = spawn,
            Lookup = lookup,
            Notes =
            [
                "Compilation timings exclude definition construction and include signature resolution, archetype registration, and compact catalog construction.",
                "Spawn fixtures compile and seal outside the timed region. Runtime allocation is caller-thread allocation for the measured immediate/deferred work.",
                "Lifecycle publication uses a one-record ring; NextPublishedSequence proves exactly one Created publication per successful spawn without retaining N records.",
                "The 1 KiB profile is the wide/default-copy bandwidth characterization. No authoring object or reflection participates in its timed path.",
            ],
        };

        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Compiled-content benchmark ({(quick ? "quick" : "full")})");
        foreach (ContentCompileBenchmark result in compile)
        {
            Console.WriteLine(
                $"compile {result.Prefabs,6} prefabs/{result.UniqueSignatures,3} signatures: " +
                $"mean={result.MeanMilliseconds,8:F3} ms p95={result.P95Milliseconds,8:F3} ms " +
                $"alloc={result.MeanAllocatedBytes / 1024d,9:F1} KiB catalog={result.CatalogBytes / 1024d,8:F1} KiB");
        }
        foreach (ContentSpawnBenchmark result in spawn)
        {
            Console.WriteLine(
                $"spawn {result.Name,-43} n={result.Entities,8}: " +
                $"mean={result.MeanNanosecondsPerEntity,8:F2} ns/entity p95={result.P95NanosecondsPerEntity,8:F2} " +
                $"alloc={result.MeanAllocatedBytes,5} B commands={result.CommandsPerEntity:F2}");
        }
        Console.WriteLine(
            $"lookup direct-handle: {lookup.DirectHandleMeanNanoseconds:F2} ns/op; " +
            $"stable-key: {lookup.StableKeyMeanNanoseconds:F2} ns/op; alloc={lookup.MeanAllocatedBytes} B");
        Console.WriteLine($"Results: {output}");
        return 0;
    }

    private static ContentCompileBenchmark MeasureCompile(int prefabs, int signatures, int samples)
    {
        var times = new double[samples];
        var sealTimes = new double[samples];
        var allocations = new long[samples];
        CompiledContentSnapshot snapshot = default;
        int finalArchetypes = 0;
        int gen0 = 0;
        int gen1 = 0;
        int gen2 = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            ComponentSchema schema = ContentBenchmarkSchema.CreateSchema();
            using var world = new EcsWorld(schema, new EcsWorldOptions(
                new EntityCapacity(1), signatures, maximumPreparedPlans: 0));
            ContentCatalogBuilder builder = BuildDefinitions<ContentPayload16>(schema, prefabs, signatures);
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            CompiledPrefabCatalog catalog = builder.Compile(
                world, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 1));
            long stopped = Stopwatch.GetTimestamp();
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
            gen0 += GC.CollectionCount(0) - gen0Before;
            gen1 += GC.CollectionCount(1) - gen1Before;
            gen2 += GC.CollectionCount(2) - gen2Before;
            times[sample] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
            snapshot = catalog.GetSnapshot();
            finalArchetypes = world.ArchetypeCount;
            started = Stopwatch.GetTimestamp();
            world.Seal();
            stopped = Stopwatch.GetTimestamp();
            sealTimes[sample] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
        }

        Array.Sort(times);
        Array.Sort(sealTimes);
        return new ContentCompileBenchmark
        {
            Prefabs = prefabs,
            RequestedSignatures = signatures,
            UniqueSignatures = snapshot.UniqueSignatureCount,
            ArchetypesRegistered = snapshot.ArchetypesRegistered,
            FinalArchetypeCount = finalArchetypes,
            Samples = samples,
            MeanMilliseconds = times.Average(),
            P95Milliseconds = Percentile(times, 0.95),
            MeanSealMilliseconds = sealTimes.Average(),
            P95SealMilliseconds = Percentile(sealTimes, 0.95),
            MeanAllocatedBytes = (long)allocations.Average(),
            CatalogBytes = snapshot.TotalKnownPayloadBytes,
            EntryBytes = snapshot.EntryBytes,
            DescriptorBytes = snapshot.DescriptorBytes,
            DefaultBytes = snapshot.DefaultBytes,
            DiagnosticHash = snapshot.DiagnosticHash,
            DeduplicationRatio = (double)prefabs / snapshot.UniqueSignatureCount,
            Gen0Collections = gen0,
            Gen1Collections = gen1,
            Gen2Collections = gen2,
        };
    }

    private static ContentSpawnBenchmark MeasureOrdinaryCreate<T>(int count, int samples)
        where T : unmanaged, IComponent<T>
    {
        var elapsed = new double[samples];
        var allocated = new long[samples];
        long checksum = 0;
        int gen0 = 0;
        int gen1 = 0;
        int gen2 = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            ComponentSchema schema = ContentBenchmarkSchema.CreateSchema();
            int chunks = EstimateChunks<T>(count);
            using var world = new EcsWorld(schema, new EcsWorldOptions(
                new EntityCapacity(count), 1, maximumPreparedPlans: 0, lifecycleRecordCapacity: 1));
            ArchetypeHandle archetype = world.RegisterArchetype(
                [ContentBenchmarkSchema.GetComponentId<T>()], ChunkLayoutOptions.KiB16, chunks);
            world.Seal(); world.Initialize(); world.BeginRun();
            if (!world.TryCreate(archetype, out Entity warmed) || !world.TryDestroy(warmed))
                throw new InvalidOperationException("Ordinary create fixture warmup failed.");
            ulong sequence = world.GetLifecycleHistorySnapshot().NextPublishedSequence;
            long before = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            Entity last = Entity.Null;
            for (int index = 0; index < count; index++)
            {
                if (!world.TryCreate(archetype, out last))
                    throw new InvalidOperationException("Ordinary create benchmark exhausted capacity unexpectedly.");
            }
            long stopped = Stopwatch.GetTimestamp();
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            gen0 += GC.CollectionCount(0) - gen0Before;
            gen1 += GC.CollectionCount(1) - gen1Before;
            gen2 += GC.CollectionCount(2) - gen2Before;
            elapsed[sample] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
            ulong published = world.GetLifecycleHistorySnapshot().NextPublishedSequence - sequence;
            if (published != unchecked((ulong)count) || world.AliveCount != count)
                throw new InvalidOperationException("Ordinary create benchmark lifecycle/count validation failed.");
            checksum ^= last.Index ^ last.Generation;
        }
        return BuildSpawnResult(
            "Ordinary_CreateDefault_Control", "ImmediateControl", count, Unsafe.SizeOf<T>(),
            prefabCount: 0, uniqueSignatures: 1, samples, elapsed, allocated, checksum,
            commandsPerEntity: 0, lifecycleEventsPerEntity: 1, playbackMilliseconds: null,
            gen0, gen1, gen2);
    }

    private static ContentSpawnBenchmark MeasureImmediate<T>(
        string name,
        int count,
        int prefabCount,
        int uniqueSignatures,
        int samples)
        where T : unmanaged, IComponent<T>
    {
        var elapsed = new double[samples];
        var allocated = new long[samples];
        long checksum = 0;
        int gen0 = 0;
        int gen1 = 0;
        int gen2 = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            using SpawnFixture<T> fixture = SpawnFixture<T>.Create(
                count, prefabCount, uniqueSignatures, structural: false);
            ulong sequence = fixture.World.GetLifecycleHistorySnapshot().NextPublishedSequence;
            long before = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            Entity last = Entity.Null;
            for (int index = 0; index < count; index++)
                last = fixture.World.Instantiate(fixture.Handles[index % fixture.Handles.Length]);
            long stopped = Stopwatch.GetTimestamp();
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            gen0 += GC.CollectionCount(0) - gen0Before;
            gen1 += GC.CollectionCount(1) - gen1Before;
            gen2 += GC.CollectionCount(2) - gen2Before;
            elapsed[sample] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
            fixture.Validate(last, count, sequence);
            checksum ^= fixture.ReadValue(last) ^ last.Index ^ last.Generation;
        }
        return BuildSpawnResult(
            name, "Immediate", count, Unsafe.SizeOf<T>(), prefabCount, uniqueSignatures,
            samples, elapsed, allocated, checksum, commandsPerEntity: 0,
            lifecycleEventsPerEntity: 1, playbackMilliseconds: null, gen0, gen1, gen2);
    }

    private static IReadOnlyList<ContentSpawnBenchmark> MeasureDeferred<T>(
        int count,
        int prefabCount,
        int samples)
        where T : unmanaged, IComponent<T>
    {
        var record = new double[samples];
        var playback = new double[samples];
        var total = new double[samples];
        var allocated = new long[samples];
        long checksum = 0;
        int gen0 = 0;
        int gen1 = 0;
        int gen2 = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            using SpawnFixture<T> fixture = SpawnFixture<T>.Create(
                count, prefabCount, uniqueSignatures: 1, structural: true);
            StructuralCommandBuffer commands = fixture.World.StructuralCommands;
            ulong sequence = fixture.World.GetLifecycleHistorySnapshot().NextPublishedSequence;
            long before = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            for (int index = 0; index < count; index++)
                _ = commands.EnqueueInstantiate(fixture.Handles[index % fixture.Handles.Length]);
            long recorded = Stopwatch.GetTimestamp();
            StructuralPlaybackResult result = fixture.World.PlaybackStructuralCommands();
            long stopped = Stopwatch.GetTimestamp();
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            gen0 += GC.CollectionCount(0) - gen0Before;
            gen1 += GC.CollectionCount(1) - gen1Before;
            gen2 += GC.CollectionCount(2) - gen2Before;
            record[sample] = Stopwatch.GetElapsedTime(started, recorded).TotalMilliseconds;
            playback[sample] = Stopwatch.GetElapsedTime(recorded, stopped).TotalMilliseconds;
            total[sample] = Stopwatch.GetElapsedTime(started, stopped).TotalMilliseconds;
            if (result.CommandCount != count || result.CreatedCount != count || result.RejectedCount != 0)
                throw new InvalidOperationException("Deferred prefab benchmark structural counts diverged.");
            fixture.ValidatePopulation(count, sequence);
            checksum ^= result.CreatedCount ^ result.CommandCount;
        }

        string width = Unsafe.SizeOf<T>().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return
        [
            BuildSpawnResult($"Deferred_Record_{width}B", "DeferredRecord", count, Unsafe.SizeOf<T>(),
                prefabCount, 1, samples, record, allocated, checksum, 1, 1, playbackMilliseconds: null,
                gen0, gen1, gen2),
            BuildSpawnResult($"Deferred_Playback_{width}B", "DeferredPlayback", count, Unsafe.SizeOf<T>(),
                prefabCount, 1, samples, playback, allocated, checksum, 1, 1, playbackMilliseconds: playback.Average(),
                gen0, gen1, gen2),
            BuildSpawnResult($"Deferred_EndToEnd_{width}B", "DeferredEndToEnd", count, Unsafe.SizeOf<T>(),
                prefabCount, 1, samples, total, allocated, checksum, 1, 1, playbackMilliseconds: playback.Average(),
                gen0, gen1, gen2),
        ];
    }

    private static ContentLookupBenchmark MeasureLookup(int operations, int samples)
    {
        ComponentSchema schema = ContentBenchmarkSchema.CreateSchema();
        using var world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(1), 1, maximumPreparedPlans: 0));
        ContentCatalogBuilder builder = BuildDefinitions<ContentPayload16>(schema, 128, 1);
        CompiledPrefabCatalog catalog = builder.Compile(
            world, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, 1));
        PrefabKey key = new("bench/prefab-000127");
        PrefabHandle handle = catalog.GetHandle(key);
        _ = catalog.GetInfo(handle);
        _ = catalog.TryGetHandle(key, out _);
        var direct = new double[samples];
        var stable = new double[samples];
        var allocations = new long[samples];
        ulong checksum = 0;
        int gen0 = 0;
        int gen1 = 0;
        int gen2 = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            for (int index = 0; index < operations; index++)
                checksum ^= catalog.GetEntry(handle.PrefabId).SignatureHash;
            long middle = Stopwatch.GetTimestamp();
            for (int index = 0; index < operations; index++)
            {
                if (!catalog.TryGetHandle(key, out PrefabHandle found))
                    throw new InvalidOperationException("Stable-key lookup failed.");
                checksum ^= unchecked((ulong)found.GetHashCode());
            }
            long stopped = Stopwatch.GetTimestamp();
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            gen0 += GC.CollectionCount(0) - gen0Before;
            gen1 += GC.CollectionCount(1) - gen1Before;
            gen2 += GC.CollectionCount(2) - gen2Before;
            direct[sample] = Stopwatch.GetElapsedTime(started, middle).TotalMilliseconds;
            stable[sample] = Stopwatch.GetElapsedTime(middle, stopped).TotalMilliseconds;
        }
        return new ContentLookupBenchmark
        {
            Operations = operations,
            Samples = samples,
            DirectHandleMeanNanoseconds = direct.Average() * 1_000_000d / operations,
            DirectHandleP95Nanoseconds = PercentileSorted(direct, 0.95) * 1_000_000d / operations,
            StableKeyMeanNanoseconds = stable.Average() * 1_000_000d / operations,
            StableKeyP95Nanoseconds = PercentileSorted(stable, 0.95) * 1_000_000d / operations,
            MeanAllocatedBytes = (long)allocations.Average(),
            Gen0Collections = gen0,
            Gen1Collections = gen1,
            Gen2Collections = gen2,
            Checksum = checksum,
        };
    }

    private static ContentCatalogBuilder BuildDefinitions<T>(
        ComponentSchema schema,
        int prefabCount,
        int uniqueSignatures)
        where T : unmanaged, IComponent<T>
    {
        var builder = new ContentCatalogBuilder(schema);
        for (int index = 0; index < prefabCount; index++)
        {
            PrefabBuilder prefab = builder.Define(new PrefabKey($"bench/prefab-{index:D6}"));
            prefab.With(CreateValue<T>(index + 1));
            AddSignatureTags(prefab, index % uniqueSignatures);
        }
        return builder;
    }

    private static void AddSignatureTags(PrefabBuilder prefab, int signature)
    {
        if ((signature & 1) != 0) prefab.Tag<ContentTag0>();
        if ((signature & 2) != 0) prefab.Tag<ContentTag1>();
        if ((signature & 4) != 0) prefab.Tag<ContentTag2>();
        if ((signature & 8) != 0) prefab.Tag<ContentTag3>();
        if ((signature & 16) != 0) prefab.Tag<ContentTag4>();
        if ((signature & 32) != 0) prefab.Tag<ContentTag5>();
        if ((signature & 64) != 0) prefab.Tag<ContentTag6>();
    }

    private static T CreateValue<T>(long value) where T : unmanaged
    {
        T result = default;
        Span<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref result, 1));
        MemoryMarshal.Write(bytes, in value);
        return result;
    }

    private static int EstimateChunks<T>(int populationPerSignature)
        where T : unmanaged
    {
        int conservativeRows = Math.Max(1, 15_000 / checked(Unsafe.SizeOf<T>() + 24));
        return checked((populationPerSignature + conservativeRows - 1) / conservativeRows + 2);
    }

    private static ContentSpawnBenchmark BuildSpawnResult(
        string name,
        string mode,
        int count,
        int defaultBytes,
        int prefabCount,
        int uniqueSignatures,
        int samples,
        double[] elapsed,
        long[] allocated,
        long checksum,
        double commandsPerEntity,
        double lifecycleEventsPerEntity,
        double? playbackMilliseconds,
        int gen0 = 0,
        int gen1 = 0,
        int gen2 = 0)
    {
        double mean = elapsed.Average();
        double p95 = PercentileSorted(elapsed, 0.95);
        double bytes = (double)count * defaultBytes;
        return new ContentSpawnBenchmark
        {
            Name = name,
            Mode = mode,
            Entities = count,
            PrefabCount = prefabCount,
            UniqueSignatures = uniqueSignatures,
            DefaultBytesPerEntity = defaultBytes,
            Samples = samples,
            MeanMilliseconds = mean,
            P95Milliseconds = p95,
            MeanNanosecondsPerEntity = mean * 1_000_000d / count,
            P95NanosecondsPerEntity = p95 * 1_000_000d / count,
            EntitiesPerSecond = count / (mean / 1_000d),
            EffectiveDefaultGiBPerSecond = bytes / (mean / 1_000d) / (1024d * 1024d * 1024d),
            MeanAllocatedBytes = (long)allocated.Average(),
            Gen0Collections = gen0,
            Gen1Collections = gen1,
            Gen2Collections = gen2,
            CommandsPerEntity = commandsPerEntity,
            LifecycleEventsPerEntity = lifecycleEventsPerEntity,
            MeanPlaybackMilliseconds = playbackMilliseconds,
            Checksum = checksum,
        };
    }

    private static double PercentileSorted(double[] values, double percentile)
    {
        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        return Percentile(sorted, percentile);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void Warmup()
    {
        _ = Stopwatch.GetTimestamp();
        ContentCompileBenchmark compile = MeasureCompile(prefabs: 10, signatures: 1, samples: 1);
        ContentSpawnBenchmark spawn = MeasureImmediate<ContentPayload16>(
            "warmup", count: 128, prefabCount: 1, uniqueSignatures: 1, samples: 1);
        ContentSpawnBenchmark wideSpawn = MeasureImmediate<ContentPayload1024>(
            "warmup-wide", count: 128, prefabCount: 1, uniqueSignatures: 1, samples: 1);
        _ = MeasureDeferred<ContentPayload16>(count: 128, prefabCount: 1, samples: 1);
        _ = MeasureDeferred<ContentPayload1024>(count: 128, prefabCount: 1, samples: 1);
        GC.KeepAlive(compile);
        GC.KeepAlive(spawn);
        GC.KeepAlive(wideSpawn);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string GetConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private sealed class SpawnFixture<T> : IDisposable
        where T : unmanaged, IComponent<T>
    {
        private readonly ComponentLookup<T> _lookup;

        private SpawnFixture(EcsWorld world, PrefabHandle[] handles, ComponentLookup<T> lookup)
        {
            World = world;
            Handles = handles;
            _lookup = lookup;
        }

        internal EcsWorld World { get; }
        internal PrefabHandle[] Handles { get; }

        internal static SpawnFixture<T> Create(
            int count,
            int prefabCount,
            int uniqueSignatures,
            bool structural)
        {
            ComponentSchema schema = ContentBenchmarkSchema.CreateSchema();
            int perSignature = checked((count + uniqueSignatures - 1) / uniqueSignatures);
            int chunks = EstimateChunks<T>(perSignature);
            using var constructionGuard = new DisposeOnFailure();
            var world = new EcsWorld(schema, new EcsWorldOptions(
                new EntityCapacity(count),
                maximumArchetypes: uniqueSignatures,
                maximumPreparedPlans: 1,
                maximumStructuralCommands: structural ? count : 0,
                lifecycleRecordCapacity: 1));
            constructionGuard.World = world;
            ContentCatalogBuilder builder = BuildDefinitions<T>(schema, prefabCount, uniqueSignatures);
            CompiledPrefabCatalog catalog = builder.Compile(
                world, new ContentCompilationOptions(ChunkLayoutOptions.KiB16, chunks));
            var handles = new PrefabHandle[prefabCount];
            for (int index = 0; index < handles.Length; index++)
                handles[index] = catalog.GetHandle(new PrefabKey($"bench/prefab-{index:D6}"));
            ComponentLookup<T> lookup = world.PrepareLookup<T>();
            world.Seal(); world.Initialize(); world.BeginRun();
            if (structural)
            {
                StructuralCommandBuffer commands = world.StructuralCommands;
                DeferredEntity token = commands.EnqueueInstantiate(handles[0]);
                _ = world.PlaybackStructuralCommands();
                if (!commands.TryResolve(token, out Entity warmed) || !world.TryDestroy(warmed))
                    throw new InvalidOperationException("Deferred prefab fixture warmup failed.");
            }
            else
            {
                Entity warmed = world.Instantiate(handles[0]);
                if (!world.TryDestroy(warmed))
                    throw new InvalidOperationException("Immediate prefab fixture warmup failed.");
            }
            constructionGuard.World = null;
            return new SpawnFixture<T>(world, handles, lookup);
        }

        internal long ReadValue(Entity entity)
        {
            if (!World.TryRead(entity, _lookup, out T value))
                throw new InvalidOperationException("Spawned prefab value was unavailable.");
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            return MemoryMarshal.Read<long>(bytes);
        }

        internal void Validate(Entity last, int count, ulong startingSequence)
        {
            ValidatePopulation(count, startingSequence);
            long expected = ((count - 1) % Handles.Length) + 1L;
            if (ReadValue(last) != expected)
                throw new InvalidOperationException("Spawned prefab default validation failed.");
        }

        internal void ValidatePopulation(int count, ulong startingSequence)
        {
            ulong lifecycle = World.GetLifecycleHistorySnapshot().NextPublishedSequence - startingSequence;
            if (World.AliveCount != count || lifecycle != unchecked((ulong)count))
                throw new InvalidOperationException("Spawn population/lifecycle validation failed.");
        }

        public void Dispose() => World.Dispose();
    }

    private sealed class DisposeOnFailure : IDisposable
    {
        internal EcsWorld? World;
        public void Dispose() => World?.Dispose();
    }
}

internal sealed class ContentBenchmarkReport
{
    public required string Profile { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string Configuration { get; init; }
    public required string Runtime { get; init; }
    public required string OperatingSystem { get; init; }
    public required int ProcessorCount { get; init; }
    public required string TieredCompilationSetting { get; init; }
    public required string ReadyToRunSetting { get; init; }
    public required bool ServerGarbageCollection { get; init; }
    public required bool Quick { get; init; }
    public required IReadOnlyList<ContentCompileBenchmark> Compile { get; init; }
    public required IReadOnlyList<ContentSpawnBenchmark> Spawn { get; init; }
    public required ContentLookupBenchmark Lookup { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed class ContentCompileBenchmark
{
    public required int Prefabs { get; init; }
    public required int RequestedSignatures { get; init; }
    public required int UniqueSignatures { get; init; }
    public required int ArchetypesRegistered { get; init; }
    public required int FinalArchetypeCount { get; init; }
    public required int Samples { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double MeanSealMilliseconds { get; init; }
    public required double P95SealMilliseconds { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required long CatalogBytes { get; init; }
    public required long EntryBytes { get; init; }
    public required long DescriptorBytes { get; init; }
    public required long DefaultBytes { get; init; }
    public required ulong DiagnosticHash { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
}

internal sealed class ContentSpawnBenchmark
{
    public required string Name { get; init; }
    public required string Mode { get; init; }
    public required int Entities { get; init; }
    public required int PrefabCount { get; init; }
    public required int UniqueSignatures { get; init; }
    public required int DefaultBytesPerEntity { get; init; }
    public required int Samples { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double MeanNanosecondsPerEntity { get; init; }
    public required double P95NanosecondsPerEntity { get; init; }
    public required double EntitiesPerSecond { get; init; }
    public required double EffectiveDefaultGiBPerSecond { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required double CommandsPerEntity { get; init; }
    public required double LifecycleEventsPerEntity { get; init; }
    public required double? MeanPlaybackMilliseconds { get; init; }
    public required long Checksum { get; init; }
}

internal sealed class ContentLookupBenchmark
{
    public required int Operations { get; init; }
    public required int Samples { get; init; }
    public required double DirectHandleMeanNanoseconds { get; init; }
    public required double DirectHandleP95Nanoseconds { get; init; }
    public required double StableKeyMeanNanoseconds { get; init; }
    public required double StableKeyP95Nanoseconds { get; init; }
    public required long MeanAllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required ulong Checksum { get; init; }
}
