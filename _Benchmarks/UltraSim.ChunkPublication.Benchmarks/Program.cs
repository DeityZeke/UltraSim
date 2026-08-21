using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int samples = ReadInt(args, "--samples=", 21);
int commits = ReadInt(args, "--commits=", 100_000);
int reads = ReadInt(args, "--reads=", 50_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.6-chunk-publication-local.json"));
if (warmups < 1 || samples < 5 || commits < 1 || reads < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

using var idle = PublicationFixture.Create(population: 1, chunkCount: 1);
using var same = PublicationFixture.Create(population: 1, chunkCount: 1);
using var crossing = PublicationFixture.Create(population: 1, chunkCount: 1);
using var delta = PublicationFixture.Create(population: 1, chunkCount: 1);
using var packed = PublicationFixture.Create(population: 1_024, chunkCount: 8);
using var policy = PublicationFixture.Create(population: 4_096, chunkCount: 256);

PublicationTiming[] timings =
[
    Measure("post-step-observation-no-change", warmups, samples, commits, idle.RunIdle),
    Measure("public-update-same-chunk", warmups, samples, commits, same.RunSameChunk),
    Measure("public-migration-cross-chunk", warmups, samples, commits, crossing.RunCrossChunk),
    Measure("copy-one-record-delta", warmups, samples, commits, delta.RunDelta),
    Measure("iterate-prepared-pages-128", warmups, samples, reads, packed.RunPreparedIteration),
    Measure("copy-canonical-snapshot-128", warmups, samples, reads, packed.RunSnapshotCopy),
    Measure("encode-canonical-snapshot-128", warmups, samples, reads, packed.RunSnapshotEncode),
];
if (timings.Any(timing => timing.MaximumAllocatedBytes != 0))
    throw new InvalidOperationException("A warmed T0.6 publication path allocated: " +
        string.Join(", ", timings.Select(timing => $"{timing.Name}={timing.MaximumAllocatedBytes}")));

PublicationPolicyEvidence policyEvidence = policy.EvaluatePolicy();
ChunkPublicationStoreSnapshot packedSnapshot = packed.State.Publications.GetSnapshot();
var report = new PublicationReport(
    "terrain-t0.6-chunk-publication-v1",
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString(),
    Environment.ProcessorCount,
    GCSettings.IsServerGC,
    Stopwatch.Frequency,
    Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "unset",
    Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "unset",
    warmups,
    samples,
    commits,
    reads,
    new PublicationProfileEvidence(
        "synthetic dynamic public entities; 128-row packed read chunk and 256-chunk policy fixture; not production content or an observer/network workload",
        packed.Population,
        packed.State.Publications.Limits.MaximumPublicEntities,
        packed.ChunkCount,
        packed.MaximumRowsPerChunk,
        packedSnapshot.ReservedBytes,
        packedSnapshot.HistoryEntryCapacity,
        Marshal.SizeOf<WorldPublicComponent>(),
        Marshal.SizeOf<WorldPublicEntityId>(),
        Marshal.SizeOf<PublicationEpoch>(),
        Marshal.SizeOf<ChunkPublicationRevision>(),
        ChunkPublicationSemanticCodec.HeaderBytes,
        ChunkPublicationSemanticCodec.RecordBytes),
    policyEvidence,
    timings);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
Console.WriteLine($"packed_entities={packed.Population}, packed_chunks={packed.ChunkCount}, " +
    $"max_rows_per_chunk={packed.MaximumRowsPerChunk}, reserved_bytes={packedSnapshot.ReservedBytes}");
Console.WriteLine($"policy_sparse: eager={policyEvidence.SparseEagerConstructions}, " +
    $"eager_wasted={policyEvidence.SparseEagerWastedConstructions}, " +
    $"on_demand={policyEvidence.SparseOnDemandConstructions}, " +
    $"on_demand_hits={policyEvidence.SparseOnDemandCacheHits}; " +
    $"policy_full: eager={policyEvidence.FullEagerConstructions}, " +
    $"on_demand={policyEvidence.FullOnDemandConstructions}, " +
    $"on_demand_hits={policyEvidence.FullOnDemandCacheHits}");
foreach (PublicationTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F3}, max={timing.MaximumNanosecondsPerOperation:F3}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static PublicationTiming Measure(
    string name,
    int warmups,
    int samples,
    int operations,
    Func<int, ulong> run)
{
    ulong checksum = 0;
    for (int index = 0; index < warmups; index++) checksum = unchecked(checksum + run(operations));
    var elapsed = new double[samples];
    var allocated = new long[samples];
    for (int index = 0; index < samples; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        checksum = unchecked(checksum + run(operations));
        long finished = Stopwatch.GetTimestamp();
        allocated[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        elapsed[index] = (finished - started) * 1_000_000_000.0 / Stopwatch.Frequency / operations;
    }
    Array.Sort(elapsed);
    return new PublicationTiming(name, operations, elapsed.Average(),
        elapsed[PercentileIndex(samples, .50)], elapsed[PercentileIndex(samples, .95)],
        elapsed[^1], allocated.Max(), checksum);
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class PublicationFixture : IDisposable
{
    private readonly Entity[] _entities;
    private readonly WorldPublicRecord[] _records;
    private readonly WorldPublicDeltaRecord[] _deltas;
    private readonly byte[] _semanticBytes;
    private readonly SpatialChunkKey[] _keys;
    private readonly bool[] _cache;
    private ChunkPublicationCursor _deltaCursor;

    private PublicationFixture(int population, int chunkCount)
    {
        if (population < 1 || chunkCount < 1 || population % chunkCount != 0)
            throw new ArgumentOutOfRangeException(nameof(population));
        Population = population;
        ChunkCount = chunkCount;
        MaximumRowsPerChunk = population / chunkCount;
        int capacity = checked(population + 8);
        World = new EcsWorld(
            GameSchema.CreateSchema(),
            new EcsWorldOptions(
                new EntityCapacity(capacity),
                maximumArchetypes: 2,
                maximumPreparedPlans: 8,
                lifecycleRecordCapacity: checked(capacity * 2)));
        State = new GameWorldState(World);
        ComponentLookup<WorldPositionComponent> position = World.PrepareLookup<WorldPositionComponent>();
        ComponentLookup<WorldPublicComponent> publication = World.PrepareLookup<WorldPublicComponent>();
        Span<ComponentId> signature = stackalloc ComponentId[2]
        {
            GameSchema.GetComponentId<WorldPositionComponent>(),
            GameSchema.GetComponentId<WorldPublicComponent>(),
        };
        signature.Sort();
        ArchetypeHandle archetype = World.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            maximumChunks: Math.Max(1, (population + 255) / 256));
        World.Seal();
        World.Initialize();
        World.BeginRun();
        _entities = new Entity[population];
        _keys = new SpatialChunkKey[chunkCount];
        _cache = new bool[chunkCount];
        var descriptor = new WorldPublicDescriptor(1, WorldYaw.Zero, WorldPublicStateFlags.Visible);
        for (int chunk = 0; chunk < chunkCount; chunk++)
            _keys[chunk] = new SpatialChunkKey(GameWorldState.DefaultSpace, chunk, 0);
        for (int index = 0; index < population; index++)
        {
            if (!World.TryCreate(archetype, out Entity entity)) throw new InvalidOperationException();
            int chunk = index % chunkCount;
            ushort localX = checked((ushort)((index / chunkCount * 113) % 16_000));
            ushort localZ = checked((ushort)((index / chunkCount * 197) % 16_000));
            if (!World.TryWrite(entity, position,
                    new WorldPositionComponent(new WorldPosition(chunk, 0, localX, localZ, index))) ||
                !World.TryWrite(entity, publication, new WorldPublicComponent(descriptor)))
                throw new InvalidOperationException();
            _entities[index] = entity;
        }
        State.Initialize(World);
        _records = new WorldPublicRecord[MaximumRowsPerChunk];
        _deltas = new WorldPublicDeltaRecord[2];
        _semanticBytes = new byte[ChunkPublicationSemanticCodec.MeasureSnapshot(MaximumRowsPerChunk)];
        if (State.Publications.TryCopySnapshot(_keys[0], _records, out ChunkPublicationBatch initial) !=
            ChunkPublicationReadStatus.Snapshot)
            throw new InvalidOperationException();
        _deltaCursor = initial.Cursor;
    }

    internal static PublicationFixture Create(int population, int chunkCount) => new(population, chunkCount);
    internal EcsWorld World { get; }
    internal GameWorldState State { get; }
    internal int Population { get; }
    internal int ChunkCount { get; }
    internal int MaximumRowsPerChunk { get; }

    internal ulong RunIdle(int operations)
    {
        for (int index = 0; index < operations; index++) State.CommitAfterEcsStep(World);
        return unchecked((ulong)State.GetSnapshot().CommitCount);
    }

    internal ulong RunSameChunk(int operations)
    {
        Entity entity = _entities[0];
        for (int index = 0; index < operations; index++)
        {
            if (!State.TrySetPosition(World, entity,
                    new WorldPosition(0, 0, checked((ushort)(index & 15_999)), 1, index)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
        }
        return State.Publications.GetSnapshot().PublishedChunkRevisions;
    }

    internal ulong RunCrossChunk(int operations)
    {
        Entity entity = _entities[0];
        for (int index = 0; index < operations; index++)
        {
            int chunk = index & 1;
            if (!State.TrySetPosition(World, entity, new WorldPosition(chunk, 0, 0, 0, index)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
        }
        return State.Publications.GetSnapshot().PublishedChunkRevisions;
    }

    internal ulong RunDelta(int operations)
    {
        Entity entity = _entities[0];
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!State.TrySetPosition(World, entity,
                    new WorldPosition(0, 0, checked((ushort)(index & 15_999)), 2, index)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
            if (State.Publications.TryCopyDelta(_keys[0], _deltaCursor, _deltas, out ChunkPublicationBatch batch) !=
                ChunkPublicationReadStatus.Delta || batch.Count != 1)
                throw new InvalidOperationException();
            _deltaCursor = batch.Cursor;
            checksum = unchecked(checksum + _deltas[0].EntityPublicVersion);
        }
        return checksum;
    }

    internal ulong RunPreparedIteration(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            SpatialChunkKey key = _keys[index % _keys.Length];
            if (!State.Publications.TryGetCommittedView(key, out ChunkPublicationView view))
                throw new InvalidOperationException();
            foreach (ChunkPublicationPageView page in view)
                for (int row = 0; row < page.Count; row++)
                    checksum = unchecked(checksum + page.EntityPublicVersions[row] + page.PrefabIds[row]);
        }
        return checksum;
    }

    internal ulong RunSnapshotCopy(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            SpatialChunkKey key = _keys[index % _keys.Length];
            if (State.Publications.TryCopySnapshot(key, _records, out ChunkPublicationBatch batch) !=
                ChunkPublicationReadStatus.Snapshot)
                throw new InvalidOperationException();
            checksum = unchecked(checksum + (uint)batch.Count + _records[0].Id.Generation);
        }
        return checksum;
    }

    internal ulong RunSnapshotEncode(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            SpatialChunkKey key = _keys[index % _keys.Length];
            if (State.Publications.TryCopySnapshot(key, _records, out ChunkPublicationBatch batch) !=
                    ChunkPublicationReadStatus.Snapshot ||
                !ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, _records, _semanticBytes, out int written))
                throw new InvalidOperationException();
            checksum = unchecked(checksum + (uint)written + _semanticBytes[0]);
        }
        return checksum;
    }

    internal PublicationPolicyEvidence EvaluatePolicy()
    {
        const int sparseUnique = 64;
        const int repeats = 4;
        if (_keys.Length != 256) throw new InvalidOperationException();
        int eagerConstructions = _keys.Length;
        ulong eagerBytes = 0;
        for (int index = 0; index < _keys.Length; index++) eagerBytes += checked((ulong)Construct(index));
        int eagerWasted = _keys.Length - sparseUnique;

        Array.Clear(_cache);
        int onDemandConstructions = 0;
        int onDemandHits = 0;
        ulong onDemandBytes = 0;
        for (int repeat = 0; repeat < repeats; repeat++)
        for (int index = 0; index < sparseUnique; index++)
        {
            int key = index * 4;
            if (_cache[key]) onDemandHits++;
            else
            {
                _cache[key] = true;
                onDemandConstructions++;
                onDemandBytes += checked((ulong)Construct(key));
            }
        }

        Array.Clear(_cache);
        int fullOnDemandConstructions = 0;
        int fullOnDemandHits = 0;
        for (int repeat = 0; repeat < 2; repeat++)
        for (int index = 0; index < _keys.Length; index++)
        {
            if (_cache[index]) fullOnDemandHits++;
            else
            {
                _cache[index] = true;
                fullOnDemandConstructions++;
                _ = Construct(index);
            }
        }
        return new PublicationPolicyEvidence(
            _keys.Length,
            sparseUnique * repeats,
            eagerConstructions,
            eagerWasted,
            onDemandConstructions,
            onDemandHits,
            eagerBytes,
            onDemandBytes,
            _keys.Length * 2,
            _keys.Length,
            fullOnDemandConstructions,
            fullOnDemandHits,
            "on-demand semantic construction over always-current prepared pages; no wait and no production byte cache until a consumer earns its retention policy");
    }

    private int Construct(int keyIndex)
    {
        if (State.Publications.TryCopySnapshot(_keys[keyIndex], _records, out ChunkPublicationBatch batch) !=
                ChunkPublicationReadStatus.Snapshot ||
            !ChunkPublicationSemanticCodec.TryWriteSnapshot(batch, _records, _semanticBytes, out int written))
            throw new InvalidOperationException();
        return written;
    }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}

sealed record PublicationReport(
    string Workload,
    DateTimeOffset TimestampUtc,
    string RuntimeVersion,
    string FrameworkDescription,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    bool ServerGc,
    long StopwatchFrequency,
    string DotnetTieredCompilation,
    string DotnetReadyToRun,
    int Warmups,
    int Samples,
    int CommitOperationsPerSample,
    int ReadOperationsPerSample,
    PublicationProfileEvidence Profile,
    PublicationPolicyEvidence Policy,
    IReadOnlyList<PublicationTiming> Timings);

sealed record PublicationProfileEvidence(
    string Fixture,
    int Population,
    int AdmittedEntityCapacity,
    int ChunkCount,
    int MaximumRowsPerChunk,
    long StoreReservedBytes,
    int HistoryEntryCapacity,
    int WorldPublicComponentBytes,
    int WorldPublicEntityIdBytes,
    int PublicationEpochBytes,
    int ChunkPublicationRevisionBytes,
    int SemanticHeaderBytes,
    int SemanticRecordBytes);

sealed record PublicationPolicyEvidence(
    int DirtyChunkCount,
    int SparseRequests,
    int SparseEagerConstructions,
    int SparseEagerWastedConstructions,
    int SparseOnDemandConstructions,
    int SparseOnDemandCacheHits,
    ulong SparseEagerConstructedBytes,
    ulong SparseOnDemandConstructedBytes,
    int FullRequests,
    int FullEagerConstructions,
    int FullOnDemandConstructions,
    int FullOnDemandCacheHits,
    string SelectedPolicy);

sealed record PublicationTiming(
    string Name,
    int Operations,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);
