using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int samples = ReadInt(args, "--samples=", 21);
int queryOperations = ReadInt(args, "--queries=", 1_000_000);
int meshBuilds = ReadInt(args, "--mesh-builds=", 32);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.4-query-mesh-local.json"));
if (warmups < 1 || samples < 5 || queryOperations < 1 || meshBuilds < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

var fixture = new TerrainQueryMeshFixture(gridEdge: 16);
TerrainQueryMeshTiming[] timings =
[
    Measure("sample-base-terrain-height", warmups, samples, queryOperations, fixture.RunHeightQueries),
    Measure("resolve-base-terrain-material", warmups, samples, queryOperations, fixture.RunMaterialQueries),
    Measure("build-fixed-grid-base-mesh", warmups, samples, meshBuilds, fixture.RunMeshBuilds),
];
if (timings.Any(timing => timing.MaximumAllocatedBytes != 0))
    throw new InvalidOperationException("A warmed T0.4 query/mesh path allocated.");

var report = new TerrainQueryMeshReport(
    "terrain-t0.4-base-query-mesh-v1",
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
    new TerrainQueryMeshFormatEvidence(
        TerrainChunkFormat.MaterialCellEdge,
        TerrainChunkFormat.HeightVertexEdge,
        TerrainChunkFormat.HeightVertexCount,
        TerrainChunkFormat.TriangleCount,
        TerrainChunkFormat.TriangleIndexCount,
        Marshal.SizeOf<TerrainBaseMeshVertex>(),
        checked(TerrainChunkFormat.HeightVertexCount * Marshal.SizeOf<TerrainBaseMeshVertex>()),
        checked(TerrainChunkFormat.TriangleIndexCount * sizeof(ushort)),
        checked(TerrainChunkFormat.TriangleCount * sizeof(ushort)),
        fixture.MeshPayloadBytes,
        fixture.MeshHash,
        fixture.ExactSeamFrameComparisons,
        "simple fixed grid retained; greedy merging deferred until real Godot/content evidence"),
    new TerrainQueryMeshFixtureEvidence(
        "deterministic synthetic nonlinear q5 base terrain; no statics or additional structural surfaces",
        fixture.GridEdge,
        fixture.ChunkCount,
        fixture.MinimumChunkCoordinate,
        fixture.MaximumChunkCoordinate,
        fixture.QueryOperations,
        fixture.MeshBuildOperations),
    timings);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
Console.WriteLine($"terrain chunks={fixture.ChunkCount}, mesh_bytes={fixture.MeshPayloadBytes}, " +
    $"mesh_hash={fixture.MeshHash}, seam_frames={fixture.ExactSeamFrameComparisons}");
foreach (TerrainQueryMeshTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F3}, max={timing.MaximumNanosecondsPerOperation:F3}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static TerrainQueryMeshTiming Measure(
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
    return new TerrainQueryMeshTiming(
        name,
        operations,
        elapsed.Average(),
        elapsed[PercentileIndex(samples, .50)],
        elapsed[PercentileIndex(samples, .95)],
        elapsed[^1],
        allocated.Max(),
        checksum);
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class TerrainQueryMeshFixture
{
    private static readonly SpatialSpaceId Space = new(0xA004);
    private static readonly WorldContentFingerprint Content =
        new(0x1122_3344_5566_7788UL, 0x8877_6655_4433_2211UL);
    private readonly BoundWorldPack _world;
    private readonly TerrainBaseMeshVertex[] _vertices =
        new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
    private readonly ushort[] _indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
    private readonly ushort[] _materials = new ushort[TerrainChunkFormat.TriangleCount];

    internal TerrainQueryMeshFixture(int gridEdge)
    {
        GridEdge = gridEdge;
        ChunkCount = checked(gridEdge * gridEdge);
        MinimumChunkCoordinate = -(gridEdge / 2);
        MaximumChunkCoordinate = checked(MinimumChunkCoordinate + gridEdge - 1);
        var limits = new WorldPackLimits(8 * 1024 * 1024, ChunkCount, 8);
        var builder = new WorldPackBuilder(Space, Content, limits);
        for (int chunkZ = MinimumChunkCoordinate; chunkZ <= MaximumChunkCoordinate; chunkZ++)
        {
            for (int chunkX = MinimumChunkCoordinate; chunkX <= MaximumChunkCoordinate; chunkX++)
            {
                CreatePlanes(chunkX, chunkZ, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
                    out TerrainHeightOffset[] heights);
                if (!builder.TryAddBaseTerrain(new SpatialChunkKey(Space, chunkX, chunkZ), materials,
                        heightBase, heights, out WorldPackBuildError error))
                    throw new InvalidOperationException($"Terrain fixture add failed: {error}.");
            }
        }
        if (!builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError) || artifact is null)
            throw new InvalidOperationException($"Terrain fixture build failed: {buildError}.");
        var binding = new WorldContentBinding(Content, [1, 2, 3, 4, 5]);
        if (!artifact.Open().TryBind(binding, out BoundWorldPack? world, out WorldPackBindingError bindingError) ||
            world is null)
            throw new InvalidOperationException($"Terrain fixture binding failed: {bindingError}.");
        _world = world;

        var target = new SpatialChunkKey(Space, 0, 0);
        if (!_world.TryBuildBaseTerrainMesh(target, _vertices, _indices, _materials, out _, out _))
            throw new InvalidOperationException("Terrain fixture mesh did not build.");
        MeshPayloadBytes = checked(_vertices.Length * Marshal.SizeOf<TerrainBaseMeshVertex>() +
            _indices.Length * sizeof(ushort) + _materials.Length * sizeof(ushort));
        MeshHash = ComputeMeshHash(_vertices, _indices, _materials);
        ExactSeamFrameComparisons = CountExactSeamFrames();
        if (ExactSeamFrameComparisons != TerrainChunkFormat.HeightVertexEdge)
            throw new InvalidOperationException("Adjacent T0.4 mesh frames diverged.");
    }

    internal int GridEdge { get; }
    internal int ChunkCount { get; }
    internal int MinimumChunkCoordinate { get; }
    internal int MaximumChunkCoordinate { get; }
    internal int MeshPayloadBytes { get; }
    internal string MeshHash { get; }
    internal int ExactSeamFrameComparisons { get; }
    internal int QueryOperations { get; private set; }
    internal int MeshBuildOperations { get; private set; }

    internal ulong RunHeightQueries(int operations)
    {
        QueryOperations = Math.Max(QueryOperations, operations);
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = MinimumChunkCoordinate + operation % GridEdge;
            int chunkZ = MinimumChunkCoordinate + operation / GridEdge % GridEdge;
            ushort localX = checked((ushort)(operation * 73 % GameWorldSpatialProfile.WorldChunkEdgeUnits));
            ushort localZ = checked((ushort)(operation * 151 % GameWorldSpatialProfile.WorldChunkEdgeUnits));
            var location = new WorldPosition(chunkX, chunkZ, localX, localZ, 0);
            if (!_world.TrySampleBaseTerrainHeight(location, out int height))
                throw new InvalidOperationException("Admitted height query missed.");
            checksum = unchecked(checksum + (uint)height);
        }
        return checksum;
    }

    internal ulong RunMaterialQueries(int operations)
    {
        QueryOperations = Math.Max(QueryOperations, operations);
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = MinimumChunkCoordinate + operation % GridEdge;
            int chunkZ = MinimumChunkCoordinate + operation / GridEdge % GridEdge;
            ushort localX = checked((ushort)(operation * 73 % GameWorldSpatialProfile.WorldChunkEdgeUnits));
            ushort localZ = checked((ushort)(operation * 151 % GameWorldSpatialProfile.WorldChunkEdgeUnits));
            var location = new WorldPosition(chunkX, chunkZ, localX, localZ, int.MaxValue);
            if (!_world.TryResolveBaseTerrainMaterial(location, out ushort material))
                throw new InvalidOperationException("Admitted material query missed.");
            checksum = unchecked(checksum + material);
        }
        return checksum;
    }

    internal ulong RunMeshBuilds(int operations)
    {
        MeshBuildOperations = Math.Max(MeshBuildOperations, operations);
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int chunkX = MinimumChunkCoordinate + operation % GridEdge;
            int chunkZ = MinimumChunkCoordinate + operation / GridEdge % GridEdge;
            if (!_world.TryBuildBaseTerrainMesh(new SpatialChunkKey(Space, chunkX, chunkZ),
                    _vertices, _indices, _materials, out TerrainBaseMeshSummary summary,
                    out TerrainBaseMeshBuildError error))
                throw new InvalidOperationException($"Admitted mesh build failed: {error}.");
            checksum = unchecked(checksum + (uint)_vertices[operation % _vertices.Length].NormalY +
                _indices[operation % _indices.Length] + _materials[operation % _materials.Length] +
                (uint)summary.MaximumHeightMillimeters);
        }
        return checksum;
    }

    private int CountExactSeamFrames()
    {
        var leftVertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
        var rightVertices = new TerrainBaseMeshVertex[TerrainChunkFormat.HeightVertexCount];
        var indices = new ushort[TerrainChunkFormat.TriangleIndexCount];
        var materials = new ushort[TerrainChunkFormat.TriangleCount];
        if (!_world.TryBuildBaseTerrainMesh(new SpatialChunkKey(Space, -1, 0), leftVertices, indices, materials,
                out _, out _) ||
            !_world.TryBuildBaseTerrainMesh(new SpatialChunkKey(Space, 0, 0), rightVertices, indices, materials,
                out _, out _))
            throw new InvalidOperationException("Seam fixture mesh did not build.");
        int exact = 0;
        for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        {
            TerrainBaseMeshVertex left = leftVertices[TerrainChunkFormat.GetHeightIndex(
                new TerrainVertexCoord(32, z))];
            TerrainBaseMeshVertex right = rightVertices[TerrainChunkFormat.GetHeightIndex(
                new TerrainVertexCoord(0, z))];
            if (left.AbsoluteYMillimeters == right.AbsoluteYMillimeters &&
                left.NormalX == right.NormalX && left.NormalY == right.NormalY && left.NormalZ == right.NormalZ &&
                left.TangentX == right.TangentX && left.TangentY == right.TangentY &&
                left.TangentZ == right.TangentZ)
                exact++;
        }
        return exact;
    }

    private static void CreatePlanes(
        int chunkX,
        int chunkZ,
        out ushort[] materials,
        out TerrainHeightPlaneBase heightBase,
        out TerrainHeightOffset[] heights)
    {
        materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        for (byte z = 0; z < TerrainChunkFormat.MaterialCellEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.MaterialCellEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                int material = (globalX + globalZ * 2) % 5;
                if (material < 0) material += 5;
                materials[TerrainChunkFormat.GetMaterialIndex(new TerrainCellCoord(x, z))] =
                    checked((ushort)(material + 1));
            }
        }
        var source = new int[TerrainChunkFormat.HeightVertexCount];
        for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                long shape = (long)globalX * globalX + 3L * globalZ * globalZ + (long)globalX * globalZ;
                long pattern = shape % 17;
                if (pattern < 0) pattern += 17;
                source[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z))] =
                    checked((int)(750_000L + globalX * 25L + globalZ * 15L + pattern * 5L));
            }
        }
        heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        if (!TerrainHeightEncoding.TryEncodePlane(source, heights, out heightBase, out _, out _))
            throw new InvalidOperationException("Terrain fixture height plane did not encode.");
    }

    private static string ComputeMeshHash(
        ReadOnlySpan<TerrainBaseMeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        ReadOnlySpan<ushort> materials)
    {
        int length = checked(vertices.Length * 9 * sizeof(int) +
            indices.Length * sizeof(ushort) + materials.Length * sizeof(ushort));
        var bytes = new byte[length];
        int offset = 0;
        foreach (TerrainBaseMeshVertex vertex in vertices)
        {
            Write(vertex.LocalXMillimeters);
            Write(vertex.AbsoluteYMillimeters);
            Write(vertex.LocalZMillimeters);
            Write(vertex.NormalX);
            Write(vertex.NormalY);
            Write(vertex.NormalZ);
            Write(vertex.TangentX);
            Write(vertex.TangentY);
            Write(vertex.TangentZ);
        }
        foreach (ushort index in indices)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), index);
            offset += sizeof(ushort);
        }
        foreach (ushort material in materials)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), material);
            offset += sizeof(ushort);
        }
        if (offset != bytes.Length) throw new InvalidOperationException("Mesh hash payload length drifted.");
        return Convert.ToHexString(SHA256.HashData(bytes));

        void Write(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(int);
        }
    }
}

sealed record TerrainQueryMeshReport(
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
    TerrainQueryMeshFormatEvidence Format,
    TerrainQueryMeshFixtureEvidence Fixture,
    IReadOnlyList<TerrainQueryMeshTiming> Timings);

sealed record TerrainQueryMeshFormatEvidence(
    int MaterialCellEdge,
    int HeightVertexEdge,
    int VertexCount,
    int TriangleCount,
    int IndexCount,
    int VertexBytes,
    int VertexPayloadBytes,
    int IndexPayloadBytes,
    int TriangleMaterialPayloadBytes,
    int CompletePayloadBytes,
    string CanonicalMeshHash,
    int ExactSeamFrameComparisons,
    string MergePolicy);

sealed record TerrainQueryMeshFixtureEvidence(
    string Classification,
    int GridEdge,
    int Chunks,
    int MinimumChunkCoordinate,
    int MaximumChunkCoordinate,
    int QueryOperations,
    int MeshBuildOperations);

sealed record TerrainQueryMeshTiming(
    string Name,
    int OperationsPerSample,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);
