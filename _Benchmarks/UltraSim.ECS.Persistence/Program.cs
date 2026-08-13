using System.Diagnostics;
using System.Buffers.Binary;
using System.Text.Json;
using UltraSim.ECS;

int[] populations = args.Length == 0 ? [100_000, 1_000_000] : args.Select(int.Parse).ToArray();
var results = new List<PersistenceScaleResult>();
foreach (int population in populations)
{
    // Twenty-one samples make p95 an actual tail statistic rather than the
    // maximum of a three/five-sample smoke run.
    int iterations = 21;
    results.Add(Run(population, iterations));
}
var report = new PersistenceBenchmarkReport("ecs-3.4.1-persistence-correctness-hardening", DateTimeOffset.UtcNow, results.ToArray());
string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../results/ecs-v3-3.4.1-persistence.json"));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

static PersistenceScaleResult Run(int population, int iterations)
{
    ComponentSchema schema = PersistenceBenchmarkSchema.CreateSchema();
    PersistenceCatalog catalog = PersistenceBenchmarkSchema.CreatePersistenceCatalog();
    ComponentId[] signature =
    [
        PersistenceBenchmarkSchema.GetComponentId<PersistBenchIdentity>(),
        PersistenceBenchmarkSchema.GetComponentId<PersistBenchPosition>(),
        PersistenceBenchmarkSchema.GetComponentId<PersistBenchVelocity>(),
        PersistenceBenchmarkSchema.GetComponentId<PersistBenchWide>(),
        PersistenceBenchmarkSchema.GetComponentId<PersistBenchMembership>(),
    ];
    Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
    Span<ComponentColumnLayout> columns = stackalloc ComponentColumnLayout[signature.Length];
    ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB64, columns);
    int maximumChunks = (population + layout.RowCapacity - 1) / layout.RowCapacity;
    var identityOptions = new PersistenceIdentityOptions(new PersistentWorldId(0x3400));
    using var world = CreateWorld(schema, identityOptions, population, maximumChunks);
    ArchetypeHandle shape = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB64, maximumChunks);
    ComponentLookup<PersistBenchIdentity> identity = world.PrepareLookup<PersistBenchIdentity>();
    ComponentLookup<PersistBenchPosition> position = world.PrepareLookup<PersistBenchPosition>();
    ComponentLookup<PersistBenchVelocity> velocity = world.PrepareLookup<PersistBenchVelocity>();
    ComponentLookup<PersistBenchWide> wide = world.PrepareLookup<PersistBenchWide>();
    world.Seal(); world.Initialize(); world.BeginRun();
    for (int i = 0; i < population; i++)
    {
        if (!world.TryCreate(shape, out Entity entity)) throw new InvalidOperationException("Benchmark setup capacity failure.");
        world.TryWrite(entity, identity, new PersistBenchIdentity { Value = i });
        world.TryWrite(entity, position, new PersistBenchPosition { X = i * 0.25f, Y = -i * 0.5f, Z = i % 1024 });
        world.TryWrite(entity, velocity, new PersistBenchVelocity { X = i % 13, Y = i % 17, Z = i % 19 });
        world.TrySetEnabled(entity, velocity, (i & 7) != 0);
        world.TryWrite(entity, wide, new PersistBenchWide { A = i, B = i + 1, C = i + 2, D = i + 3,
            E = i + 4, F = i + 5, G = i + 6, H = i + 7 });
    }
    var restoreOptions = new PersistenceRestoreOptions(
        new PersistenceLimits(2_000_000_000, population + 1, 32, 64, 256, 1_500_000_000, 1_000_000_000),
        ChunkLayoutOptions.KiB64);
    PersistenceSnapshotArtifact warmArtifact = EcsPersistence.Capture(world, catalog,
        new PersistenceCaptureOptions(1, 0), out _);
    PersistenceRestorePlan warmPlan = EcsPersistenceRestore.Preflight(warmArtifact.Bytes, catalog, restoreOptions);
    using (EcsWorld warmRestored = warmPlan.Restore(
        requirements => CreateWorld(requirements.Schema, requirements.IdentityOptions,
            requirements.EntityCount, requirements.Shapes.Max(static shape => shape.MaximumChunks)), out _)) { }
    var captureMs = new double[iterations]; var preflightMs = new double[iterations]; var restoreMs = new double[iterations];
    var pauseMs = new double[iterations];
    var encodeMs = new double[iterations];
    var captureAlloc = new long[iterations]; var restoreAlloc = new long[iterations];
    int artifactBytes = 0; string digest = string.Empty; bool canonical = false;
    SectionBytes sections = default;
    long restorePlanTemporaryBytes = 0;
    long stagingWorldKnownPayloadBytes = 0;
    WorldReservedPayloadSnapshot sourcePayload = world.GetReservedPayloadSnapshot();
    int gen0Before = GC.CollectionCount(0), gen1Before = GC.CollectionCount(1), gen2Before = GC.CollectionCount(2);
    for (int iteration = 0; iteration < iterations; iteration++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(world, catalog,
            new PersistenceCaptureOptions(1, 0), out PersistenceCaptureReport captureReport);
        captureMs[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        pauseMs[iteration] = captureReport.CollectTime.TotalMilliseconds;
        encodeMs[iteration] = captureReport.EncodeTime.TotalMilliseconds;
        captureAlloc[iteration] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        artifactBytes = artifact.ByteCount; digest = artifact.Digest;
        sections = ReadSectionBytes(artifact.Bytes.Span);
        start = Stopwatch.GetTimestamp();
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact.Bytes, catalog, restoreOptions);
        restorePlanTemporaryBytes = plan.Requirements.EstimatedTemporaryBytes;
        preflightMs[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        using EcsWorld restored = plan.Restore(
            requirements => CreateWorld(requirements.Schema, requirements.IdentityOptions,
                requirements.EntityCount, requirements.Shapes.Max(static shape => shape.MaximumChunks)), out _);
        stagingWorldKnownPayloadBytes = restored.GetReservedPayloadSnapshot().TotalKnownPayloadBytes;
        restoreMs[iteration] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        restoreAlloc[iteration] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (iteration == 0)
        {
            PersistenceSnapshotArtifact recaptured = EcsPersistence.Capture(restored, catalog,
                new PersistenceCaptureOptions(1, 0), out _);
            canonical = recaptured.Bytes.Span.SequenceEqual(artifact.Bytes.Span);
            if (!canonical) throw new InvalidOperationException("Persistence benchmark canonical recapture failed.");
        }
    }
    double payloadGiB = artifactBytes / (1024d * 1024d * 1024d);
    double logicalComponentGiB = (population * 92d) / (1024d * 1024d * 1024d);
    return new PersistenceScaleResult(population, iterations, artifactBytes, artifactBytes / (double)population,
        Mean(captureMs), Percentile(captureMs, .95), Mean(preflightMs), Percentile(preflightMs, .95),
        Mean(restoreMs), Percentile(restoreMs, .95), Mean(pauseMs), Percentile(pauseMs, .95),
        Mean(encodeMs), Percentile(encodeMs, .95), payloadGiB / (Mean(captureMs) / 1000d),
        logicalComponentGiB / (Mean(encodeMs) / 1000d),
        captureAlloc.Average(), restoreAlloc.Average(), canonical, digest,
        GC.CollectionCount(0) - gen0Before, GC.CollectionCount(1) - gen1Before,
        GC.CollectionCount(2) - gen2Before,
        Process.GetCurrentProcess().WorkingSet64, GC.GetTotalMemory(false),
        sections.Schema, sections.ShapesAndRows, sections.Entities, sections.Columns,
        sections.EnablePlanes, sections.SystemState, sections.Prefabs, sections.ContainerOverhead,
        checked((long)population * sizeof(int)), checked((long)population * sizeof(uint)),
        checked((long)population * sizeof(uint)), sourcePayload.PersistenceIdentityBytes,
        restorePlanTemporaryBytes, stagingWorldKnownPayloadBytes);
}

static SectionBytes ReadSectionBytes(ReadOnlySpan<byte> artifact)
{
    int count = BinaryPrimitives.ReadInt32LittleEndian(artifact[64..]);
    int directory = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(artifact[72..]));
    long schema = 0, shapes = 0, entities = 0, columns = 0, enables = 0, systems = 0, prefabs = 0;
    for (int index = 0; index < count; index++)
    {
        ReadOnlySpan<byte> entry = artifact.Slice(directory + index * 32, 32);
        uint kind = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        long length = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]));
        switch (kind)
        {
            case 1: schema += length; break;
            case 2 or 4: shapes += length; break;
            case 3: entities += length; break;
            case 5: columns += length; break;
            case 6: enables += length; break;
            case 7: systems += length; break;
            case 8: prefabs += length; break;
        }
    }
    long classified = schema + shapes + entities + columns + enables + systems + prefabs;
    return new SectionBytes(schema, shapes, entities, columns, enables, systems, prefabs,
        artifact.Length - classified);
}

static EcsWorld CreateWorld(ComponentSchema schema, PersistenceIdentityOptions identity, int population, int maximumChunks) =>
    new(schema, new EcsWorldOptions(new EntityCapacity(Math.Max(1, population)), maximumArchetypes: 8,
        maximumPreparedPlans: 8, maximumPreparedTransitions: 8, maximumStructuralCommands: 0,
        persistence: identity));

static double Mean(double[] values) => values.Average();
static double Percentile(double[] values, double percentile)
{
    double[] sorted = (double[])values.Clone(); Array.Sort(sorted);
    return sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
}

internal sealed record PersistenceBenchmarkReport(string Profile, DateTimeOffset CapturedAt, PersistenceScaleResult[] Results);
internal sealed record PersistenceScaleResult(int Population, int Iterations, int ArtifactBytes, double BytesPerEntity,
    double CaptureMeanMs, double CaptureP95Ms, double PreflightMeanMs, double PreflightP95Ms,
    double RestoreMeanMs, double RestoreP95Ms, double AuthoritativePauseMeanMs, double AuthoritativePauseP95Ms,
    double EncodeMeanMs, double EncodeP95Ms, double ArtifactGiBPerSecond, double LogicalComponentGiBPerSecond,
    double CaptureAllocatedBytes,
    double RestoreAllocatedBytes, bool CanonicalRecapture, string Digest, int Gen0Collections, int Gen1Collections,
    int Gen2Collections, long ProcessWorkingSetBytes, long ManagedHeapBytes,
    long SavedSchemaMetadataBytes, long SavedShapeAndRowMetadataBytes, long SavedEntityIdentityBytes,
    long SavedComponentColumnBytes, long SavedEnablePlaneBytes, long SavedSystemStateBytes,
    long SavedPrefabMetadataBytes, long ContainerDirectoryChecksumPaddingBytes,
    long CaptureEntityOrdinalMapBytes, long CaptureGenerationValidationBytes, long CaptureShapeOrdinalBytes,
    long PersistentIdentityMappingBytes, long RestorePlanEstimatedTemporaryBytes,
    long StagingWorldKnownPayloadBytes);

internal readonly record struct SectionBytes(long Schema, long ShapesAndRows, long Entities, long Columns,
    long EnablePlanes, long SystemState, long Prefabs, long ContainerOverhead);

[EcsComponent("pbench.identity", PersistenceId = 34001)]
internal struct PersistBenchIdentity : IComponent<PersistBenchIdentity> { public int Value { get; set; } }
[EcsComponent("pbench.position", PersistenceId = 34002)]
internal struct PersistBenchPosition : IComponent<PersistBenchPosition> { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
[EcsComponent("pbench.velocity", IsEnableable = true, PersistenceId = 34003, PersistEnableState = true)]
internal struct PersistBenchVelocity : IComponent<PersistBenchVelocity> { public float X { get; set; } public float Y { get; set; } public float Z { get; set; } }
[EcsComponent("pbench.wide", PersistenceId = 34004)]
internal struct PersistBenchWide : IComponent<PersistBenchWide>
{ public long A { get; set; } public long B { get; set; } public long C { get; set; } public long D { get; set; }
  public long E { get; set; } public long F { get; set; } public long G { get; set; } public long H { get; set; } }
[EcsComponent("pbench.membership", PersistenceId = 34005, PersistenceMode = ComponentPersistenceMode.MembershipOnly)]
internal struct PersistBenchMembership : IComponent<PersistBenchMembership> { public int RuntimeOnly { get; set; } }
[EcsSchema(typeof(PersistBenchIdentity), typeof(PersistBenchPosition), typeof(PersistBenchVelocity), typeof(PersistBenchWide), typeof(PersistBenchMembership))]
internal static partial class PersistenceBenchmarkSchema { }
