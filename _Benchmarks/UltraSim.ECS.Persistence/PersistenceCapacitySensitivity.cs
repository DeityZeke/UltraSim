using System.Diagnostics;
using System.Text.Json;
using UltraSim.ECS;

internal static class PersistenceCapacitySensitivityProgram
{
    internal static void Run(string[] args)
    {
        int live = ReadInt(args, "--live=", 100_000);
        int capacity = ReadInt(args, "--capacity=", 8_000_000);
        int iterations = ReadInt(args, "--iterations=", 21);
        int warmups = ReadInt(args, "--warmups=", 3);
        bool reverse = args.Contains("--reverse", StringComparer.Ordinal);
        if (live <= 0 || capacity < live || iterations < 3 || warmups < 1)
            throw new ArgumentException("Sparse capacity requires capacity >= live > 0, at least one warmup, and at least three iterations.");

        var cases = new (string Name, int Numerator, int Denominator, int Capacity)[]
        {
            ("capacity-control-normal-10pct", 1, 10, live),
            ("sparse-low-1pct", 1, 100, capacity),
            ("sparse-normal-10pct", 1, 10, capacity),
            ("sparse-dense-100pct", 1, 1, capacity),
        };
        if (reverse) Array.Reverse(cases);
        var results = new List<PersistenceCapacitySensitivityResult>();
        foreach ((string name, int numerator, int denominator, int caseCapacity) in cases)
            results.Add(RunCase(name, numerator, denominator, live, caseCapacity, warmups, iterations));

        var report = new PersistenceCapacitySensitivityReport(
            "ecs-3.4.1-persistence-sparse-capacity-reference-density",
            DateTimeOffset.UtcNow,
            reverse ? "reverse" : "forward",
            "Capacity-sized ordinal/generation arrays are the current direct O(1) reference-encoding control; this is not an accepted baseline or a proposed replacement.",
            results.ToArray());
        string output = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            $"../../../../results/ecs-v3-3.4.1-persistence-sparse-capacity-{(reverse ? "reverse" : "forward")}.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, json);
        Console.WriteLine(json);
        Console.WriteLine($"Results: {output}");
    }

    private static PersistenceCapacitySensitivityResult RunCase(
        string density,
        int numerator,
        int denominator,
        int live,
        int capacity,
        int warmups,
        int iterations)
    {
        ComponentSchema schema = PersistenceSparseBenchmarkSchema.CreateSchema();
        PersistenceCatalog catalog = PersistenceSparseBenchmarkSchema.CreatePersistenceCatalog();
        ComponentId[] signature =
        [
            PersistenceSparseBenchmarkSchema.GetComponentId<PersistSparseIdentity>(),
            PersistenceSparseBenchmarkSchema.GetComponentId<PersistSparseReference>(),
        ];
        Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
        Span<ComponentColumnLayout> columns = stackalloc ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB64, columns);
        int maximumChunks = checked((live + layout.RowCapacity - 1) / layout.RowCapacity);
        var identityOptions = new PersistenceIdentityOptions(new PersistentWorldId(0x3410));
        using var world = CreateWorld(schema, identityOptions, capacity, maximumChunks);
        ArchetypeHandle shape = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB64, maximumChunks);
        ComponentLookup<PersistSparseIdentity> identities = world.PrepareLookup<PersistSparseIdentity>();
        ComponentLookup<PersistSparseReference> references = world.PrepareLookup<PersistSparseReference>();
        world.Seal();
        world.Initialize();
        world.BeginRun();

        var entities = new Entity[live];
        for (int index = 0; index < live; index++)
        {
            if (!world.TryCreate(shape, out Entity entity))
                throw new InvalidOperationException("Sparse-capacity setup exhausted entity storage.");
            entities[index] = entity;
            if (!world.TryWrite(entity, identities, new PersistSparseIdentity { Value = index }))
                throw new InvalidOperationException("Sparse-capacity identity initialization failed.");
        }

        int populatedReferences = 0;
        for (int index = 0; index < live; index++)
        {
            // Multiplication by an odd constant distributes the selected rows
            // without adding a random generator to this cold measurement.
            ulong bucket = ((ulong)(uint)index * 2_654_435_761UL) % unchecked((uint)denominator);
            Entity target = bucket < unchecked((uint)numerator) ? entities[(index * 17 + 11) % live] : Entity.Null;
            if (target != Entity.Null) populatedReferences++;
            if (!world.TryWrite(entities[index], references, new PersistSparseReference { Target = target }))
                throw new InvalidOperationException("Sparse-capacity reference initialization failed.");
        }

        PersistenceSnapshotArtifact? warm = null;
        for (int warmup = 0; warmup < warmups; warmup++)
        {
            warm = EcsPersistence.Capture(world, catalog, new PersistenceCaptureOptions(1), out _);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        }
        var totalMs = new double[iterations];
        var collectMs = new double[iterations];
        var encodeMs = new double[iterations];
        var allocatedBytes = new long[iterations];
        PersistenceSnapshotArtifact? canonicalSource = null;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(
                world, catalog, new PersistenceCaptureOptions(1), out PersistenceCaptureReport report);
            totalMs[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            collectMs[iteration] = report.CollectTime.TotalMilliseconds;
            encodeMs[iteration] = report.EncodeTime.TotalMilliseconds;
            allocatedBytes[iteration] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            canonicalSource ??= artifact;
        }

        PersistenceSnapshotArtifact source = canonicalSource ?? warm ??
            throw new InvalidOperationException("Sparse-capacity capture did not produce an artifact.");
        var restoreOptions = new PersistenceRestoreOptions(
            new PersistenceLimits(
                Math.Max(source.ByteCount + 1024, 16 * 1024 * 1024),
                live + 1,
                8,
                8,
                256,
                Math.Max(source.ByteCount + 1024, 16 * 1024 * 1024),
                Math.Max(source.ByteCount * 3L, 64L * 1024 * 1024)),
            ChunkLayoutOptions.KiB64);
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(source.Bytes, catalog, restoreOptions);
        using EcsWorld restored = plan.Restore(
            requirements => CreateWorld(
                requirements.Schema,
                requirements.IdentityOptions,
                Math.Max(1, requirements.EntityCount),
                requirements.Shapes.Max(static value => value.MaximumChunks)),
            out _);
        PersistenceSnapshotArtifact recaptured = EcsPersistence.Capture(
            restored, catalog, new PersistenceCaptureOptions(1), out _);
        bool canonical = source.Bytes.Span.SequenceEqual(recaptured.Bytes.Span);
        if (!canonical) throw new InvalidOperationException("Sparse-capacity canonical recapture failed.");

        WorldReservedPayloadSnapshot payload = world.GetReservedPayloadSnapshot();
        return new PersistenceCapacitySensitivityResult(
            density,
            live,
            capacity,
            live / (double)capacity,
            populatedReferences,
            populatedReferences / (double)live,
            warmups,
            iterations,
            source.ByteCount,
            Mean(totalMs),
            Percentile(totalMs, .95),
            Mean(collectMs),
            Percentile(collectMs, .95),
            Mean(encodeMs),
            Percentile(encodeMs, .95),
            allocatedBytes.Average(),
            checked((long)capacity * sizeof(int)),
            checked((long)capacity * sizeof(uint)),
            payload.PersistenceIdentityBytes,
            GC.GetTotalMemory(false),
            Process.GetCurrentProcess().WorkingSet64,
            canonical);
    }

    private static EcsWorld CreateWorld(
        ComponentSchema schema,
        PersistenceIdentityOptions identity,
        int entityCapacity,
        int maximumChunks) =>
        new(schema, new EcsWorldOptions(
            new EntityCapacity(entityCapacity),
            maximumArchetypes: 4,
            maximumPreparedPlans: 4,
            maximumPreparedTransitions: 4,
            maximumStructuralCommands: 0,
            persistence: identity));

    private static int ReadInt(string[] arguments, string prefix, int fallback)
    {
        string? value = arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? fallback : int.Parse(value[prefix.Length..]);
    }

    private static double Mean(double[] values) => values.Average();
    private static double Percentile(double[] values, double percentile)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
    }
}

internal sealed record PersistenceCapacitySensitivityReport(
    string Profile,
    DateTimeOffset CapturedAt,
    string ExecutionOrder,
    string Contract,
    PersistenceCapacitySensitivityResult[] Results);

internal sealed record PersistenceCapacitySensitivityResult(
    string ReferenceDensity,
    int LiveEntities,
    int EntityCapacity,
    double LiveFraction,
    int PopulatedReferences,
    double PopulatedReferenceFraction,
    int Warmups,
    int Iterations,
    int ArtifactBytes,
    double CaptureMeanMs,
    double CaptureP95Ms,
    double CollectMeanMs,
    double CollectP95Ms,
    double EncodeMeanMs,
    double EncodeP95Ms,
    double CaptureAllocatedBytes,
    long CapacityOrdinalMapBytes,
    long CapacityGenerationMapBytes,
    long PersistentIdentityMappingBytes,
    long ManagedHeapBytes,
    long ProcessWorkingSetBytes,
    bool CanonicalRecapture);

[EcsComponent("pbench.sparse.identity", PersistenceId = 34101)]
internal struct PersistSparseIdentity : IComponent<PersistSparseIdentity>
{
    public int Value { get; set; }
}

[EcsComponent("pbench.sparse.reference", PersistenceId = 34102)]
internal struct PersistSparseReference : IComponent<PersistSparseReference>
{
    [PersistenceEntityReference]
    public Entity Target { get; set; }
}

[EcsSchema(typeof(PersistSparseIdentity), typeof(PersistSparseReference))]
internal static partial class PersistenceSparseBenchmarkSchema { }
