using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int samples = ReadInt(args, "--samples=", 21);
int reads = ReadInt(args, "--reads=", 1_000_000);
int groups = ReadInt(args, "--groups=", 250_000);
int queries = ReadInt(args, "--queries=", 25_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.5-frozen-structures-local.json"));
if (warmups < 1 || samples < 5 || reads < 1 || groups < 1 || queries < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

var fixture = new FrozenStructureFixture(gridEdge: 16, staticsPerChunk: 32, multiCount: 64,
    piecesPerMulti: 8);
FrozenStructureTiming[] timings =
[
    Measure("random-static-or-fragment-read", warmups, samples, reads, fixture.RunReads),
    Measure("copy-compatible-render-groups", warmups, samples, groups, fixture.RunRenderGroups),
    Measure("bounded-static-bounds-query", warmups, samples, queries, fixture.RunBoundsQueries),
];
if (timings.Any(timing => timing.MaximumAllocatedBytes != 0))
    throw new InvalidOperationException("A warmed T0.5 frozen-structure path allocated.");

var report = new FrozenStructureReport(
    "terrain-t0.5-frozen-statics-static-multis-v1",
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
    new FrozenStructureFixtureEvidence(
        "deterministic synthetic 16x16 base world with independent trees/rocks and static placed cabin/shelf fragments; not production content",
        fixture.GridEdge,
        fixture.ChunkCount,
        fixture.StaticCount,
        fixture.MultiCount,
        fixture.FragmentCount,
        fixture.PackBytes,
        fixture.SectionCount,
        fixture.MaximumStaticsInOneChunk,
        fixture.MaximumFragmentsInOneChunk,
        fixture.MaximumRenderGroupsInOneChunk,
        fixture.MaximumQueryHits,
        "logical Multi identity survives chunk fragmentation; no moving aggregate or vehicle contract"),
    timings);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
Console.WriteLine($"chunks={fixture.ChunkCount}, statics={fixture.StaticCount}, multis={fixture.MultiCount}, " +
    $"fragments={fixture.FragmentCount}, pack_bytes={fixture.PackBytes}, max_query_hits={fixture.MaximumQueryHits}");
foreach (FrozenStructureTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F3}, max={timing.MaximumNanosecondsPerOperation:F3}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static FrozenStructureTiming Measure(
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
    return new FrozenStructureTiming(name, operations, elapsed.Average(), elapsed[PercentileIndex(samples, .50)],
        elapsed[PercentileIndex(samples, .95)], elapsed[^1], allocated.Max(), checksum);
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class FrozenStructureFixture
{
    private static readonly SpatialSpaceId Space = new(0xA005);
    private static readonly WorldContentFingerprint Content =
        new(0x1122_3344_5566_7788UL, 0x8877_6655_4433_2211UL);
    private readonly BoundWorldPack _world;
    private readonly WorldStaticRenderGroup[] _groups = new WorldStaticRenderGroup[64];
    private readonly WorldStaticQueryHit[] _hits = new WorldStaticQueryHit[128];

    internal FrozenStructureFixture(int gridEdge, int staticsPerChunk, int multiCount, int piecesPerMulti)
    {
        GridEdge = gridEdge;
        ChunkCount = checked(gridEdge * gridEdge);
        int minimum = -(gridEdge / 2);
        int maximum = checked(minimum + gridEdge - 1);
        var limits = new WorldPackLimits(16 * 1024 * 1024, ChunkCount, 8,
            maximumFrozenStatics: checked(ChunkCount * staticsPerChunk),
            maximumMultiInstances: multiCount,
            maximumMultiFragments: checked(multiCount * piecesPerMulti));
        var builder = new WorldPackBuilder(Space, Content, limits);
        for (int z = minimum; z <= maximum; z++)
        {
            for (int x = minimum; x <= maximum; x++)
            {
                var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
                Array.Fill(materials, checked((ushort)((x * 17 + z * 31 & 3) + 1)));
                var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
                if (!TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase))
                    throw new InvalidOperationException("Zero terrain base was rejected.");
                if (!builder.TryAddBaseTerrain(new SpatialChunkKey(Space, x, z), materials,
                        heightBase, heights, out WorldPackBuildError error))
                    throw new InvalidOperationException($"Terrain fixture add failed: {error}.");
            }
        }

        ulong staticId = 1;
        for (int z = minimum; z <= maximum; z++)
        {
            for (int x = minimum; x <= maximum; x++)
            {
                for (int index = 0; index < staticsPerChunk; index++)
                {
                    ushort localX = checked((ushort)(250 + index % 8 * 1_900));
                    ushort localZ = checked((ushort)(250 + index / 8 * 3_700));
                    var position = new WorldPosition(x, z, localX, localZ, index % 3 * 200);
                    var source = new WorldFrozenStaticSource(new WorldStaticId(staticId++),
                        new WorldStaticDefinitionId(checked((uint)(index % 4 + 1))), position,
                        checked((ushort)(index & 3)), checked((ushort)(index & 1)),
                        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable, Bounds(position, 180));
                    if (!builder.TryAddFrozenStatic(source, out WorldPackBuildError error))
                        throw new InvalidOperationException($"Static fixture add failed: {error}.");
                }
            }
        }

        for (int multi = 0; multi < multiCount; multi++)
        {
            int interiorEdge = gridEdge - 2;
            int rootX = minimum + 1 + multi % interiorEdge;
            int rootZ = minimum + 1 + multi / interiorEdge % interiorEdge;
            var root = new WorldPosition(rootX, rootZ, 15_500, 15_500, 0);
            var pieces = new WorldMultiPieceSource[piecesPerMulti];
            for (int piece = 0; piece < pieces.Length; piece++)
            {
                long globalX = root.GlobalXMillimeters + (piece % 4 - 1) * 700L;
                long globalZ = root.GlobalZMillimeters + (piece / 4) * 700L;
                if (!WorldPosition.TryFromGlobalMillimeters(globalX, piece * 250, globalZ,
                        out WorldPosition position))
                    throw new InvalidOperationException("Multi piece position exceeded the fixture profile.");
                pieces[piece] = new WorldMultiPieceSource(new MultiPieceId(checked((uint)piece + 1)),
                    new WorldStaticDefinitionId(checked((uint)(piece % 4 + 5))), position,
                    0, checked((ushort)(piece & 1)),
                    WorldStaticFlags.Renderable | WorldStaticFlags.Collidable, Bounds(position, 220));
            }
            if (!builder.TryAddStaticMulti(new MultiInstanceId(checked((ulong)multi + 1)), root, 0, pieces,
                    out WorldPackBuildError error))
                throw new InvalidOperationException($"Multi fixture add failed: {error}.");
        }

        if (!builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError) || artifact is null)
            throw new InvalidOperationException($"Structure fixture build failed: {buildError}.");
        var binding = new WorldContentBinding(Content, [1, 2, 3, 4], [1U, 2U, 3U, 4U, 5U, 6U, 7U, 8U]);
        if (!artifact.Open().TryBind(binding, out BoundWorldPack? world, out WorldPackBindingError bindError) ||
            world is null)
            throw new InvalidOperationException($"Structure fixture binding failed: {bindError}.");
        _world = world;
        StaticCount = world.FrozenStaticCount;
        MultiCount = world.MultiInstanceCount;
        FragmentCount = world.MultiFragmentCount;
        PackBytes = world.Pack.Length;
        SectionCount = world.Pack.SectionCount;
        for (int z = minimum; z <= maximum; z++)
        for (int x = minimum; x <= maximum; x++)
        {
            var key = new SpatialChunkKey(Space, x, z);
            if (world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
            {
                MaximumStaticsInOneChunk = Math.Max(MaximumStaticsInOneChunk, statics.Count);
                MaximumRenderGroupsInOneChunk = Math.Max(MaximumRenderGroupsInOneChunk,
                    statics.GetRenderGroupCount());
            }
            if (world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments))
            {
                MaximumFragmentsInOneChunk = Math.Max(MaximumFragmentsInOneChunk, fragments.Count);
                MaximumRenderGroupsInOneChunk = Math.Max(MaximumRenderGroupsInOneChunk,
                    fragments.GetRenderGroupCount());
            }
        }
    }

    internal int GridEdge { get; }
    internal int ChunkCount { get; }
    internal int StaticCount { get; }
    internal int MultiCount { get; }
    internal int FragmentCount { get; }
    internal int PackBytes { get; }
    internal int SectionCount { get; }
    internal int MaximumStaticsInOneChunk { get; private set; }
    internal int MaximumFragmentsInOneChunk { get; private set; }
    internal int MaximumRenderGroupsInOneChunk { get; private set; }
    internal int MaximumQueryHits { get; private set; }

    internal ulong RunReads(int operations)
    {
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = operation % GridEdge - GridEdge / 2;
            int chunkZ = operation / GridEdge % GridEdge - GridEdge / 2;
            var key = new SpatialChunkKey(Space, chunkX, chunkZ);
            if (!_world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics))
                throw new InvalidOperationException("Static chunk was absent.");
            WorldFrozenStaticRecord record = statics.GetRecord(operation % statics.Count);
            checksum = unchecked(checksum + record.Id.Value + record.DefinitionId.Value);
            if (_world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments) && fragments.Count != 0)
            {
                WorldMultiFragmentRecord fragment = fragments.GetRecord(operation % fragments.Count);
                checksum = unchecked(checksum + fragment.MultiId.Value + fragment.PieceId.Value);
            }
        }
        return checksum;
    }

    internal ulong RunRenderGroups(int operations)
    {
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = operation % GridEdge - GridEdge / 2;
            int chunkZ = operation / GridEdge % GridEdge - GridEdge / 2;
            var key = new SpatialChunkKey(Space, chunkX, chunkZ);
            if (!_world.TryGetFrozenStatics(key, out WorldFrozenStaticChunkView statics) ||
                !statics.TryCopyRenderGroups(_groups, out int required))
                throw new InvalidOperationException("Static render groups were not representable.");
            checksum = unchecked(checksum + (uint)required + (uint)_groups[0].Count);
            if (_world.TryGetMultiFragments(key, out WorldMultiFragmentChunkView fragments) &&
                fragments.Count != 0)
            {
                if (!fragments.TryCopyRenderGroups(_groups, out required))
                    throw new InvalidOperationException("Multi render groups were not representable.");
                checksum = unchecked(checksum + (uint)required);
            }
        }
        return checksum;
    }

    internal ulong RunBoundsQueries(int operations)
    {
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = operation % GridEdge - GridEdge / 2;
            int chunkZ = operation / GridEdge % GridEdge - GridEdge / 2;
            long centerX = (long)chunkX * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                250 + operation % 8 * 1_900;
            long centerZ = (long)chunkZ * GameWorldSpatialProfile.WorldChunkEdgeUnits +
                250 + operation / 8 % 4 * 3_700;
            var query = new WorldStaticBounds(centerX - 500, -1_000, centerZ - 500,
                centerX + 500, 4_000, centerZ + 500);
            if (!_world.TryQueryStaticBounds(query, 16, _hits, out int count, out int required,
                    out WorldStaticQueryError error))
                throw new InvalidOperationException($"Static bounds query failed: {error}/{required}.");
            MaximumQueryHits = Math.Max(MaximumQueryHits, count);
            checksum = unchecked(checksum + (uint)count + (count == 0 ? 0UL : _hits[0].DefinitionId.Value));
        }
        return checksum;
    }

    private static WorldStaticBounds Bounds(in WorldPosition position, int half) => new(
        position.GlobalXMillimeters - half,
        (long)position.YMillimeters - half,
        position.GlobalZMillimeters - half,
        position.GlobalXMillimeters + half,
        (long)position.YMillimeters + half,
        position.GlobalZMillimeters + half);
}

readonly record struct FrozenStructureTiming(
    string Name,
    int OperationsPerSample,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);

readonly record struct FrozenStructureFixtureEvidence(
    string Description,
    int GridEdge,
    int ChunkCount,
    int FrozenStaticCount,
    int MultiInstanceCount,
    int MultiFragmentCount,
    int PackBytes,
    int SectionCount,
    int MaximumStaticsInOneChunk,
    int MaximumFragmentsInOneChunk,
    int MaximumRenderGroupsInOneChunk,
    int MaximumQueryHits,
    string ScopeBoundary);

readonly record struct FrozenStructureReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string RuntimeVersion,
    string Framework,
    string OperatingSystem,
    string Architecture,
    int LogicalProcessors,
    bool ServerGc,
    long StopwatchFrequency,
    string TieredCompilation,
    string ReadyToRun,
    int Warmups,
    int Samples,
    FrozenStructureFixtureEvidence Fixture,
    FrozenStructureTiming[] Timings);
