using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int samples = ReadInt(args, "--samples=", 21);
int randomReads = ReadInt(args, "--reads=", 1_000_000);
int chunkCopies = ReadInt(args, "--copies=", 10_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.3-worldpack-local.json"));
if (warmups < 1 || samples < 5 || randomReads < 1 || chunkCopies < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

using var fixture = new WorldPackBenchmarkFixture(gridEdge: 16);
WorldPackBenchmarkTiming[] timings =
[
    Measure("compile-canonical-pack", warmups, samples, 1, fixture.RunCompile),
    Measure("open-owned-memory", warmups, samples, 1, fixture.RunOpenMemory),
    Measure("reopen-buffered-file", warmups, samples, 1, fixture.RunOpenFile),
    Measure("random-base-terrain-read", warmups, samples, randomReads, fixture.RunRandomReads),
    Measure("copy-complete-base-terrain", warmups, samples, chunkCopies, fixture.RunChunkCopies),
];
if (timings[3].MaximumAllocatedBytes != 0 || timings[4].MaximumAllocatedBytes != 0)
    throw new InvalidOperationException("Admitted WorldPack read paths allocated after warmup.");

var report = new WorldPackBenchmarkReport(
    "terrain-t0.3-worldpack-v1",
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
    new WorldPackFormatEvidence(
        WorldPackFormat.ContainerVersion,
        WorldPackFormat.HeaderBytes,
        WorldPackFormat.ChunkDirectoryRowBytes,
        WorldPackFormat.SectionDirectoryRowBytes,
        fixture.ChunkCount,
        fixture.Artifact.Length,
        checked(fixture.ChunkCount * TerrainChunkFormat.FoundationBytes),
        checked(WorldPackFormat.HeaderBytes +
            fixture.ChunkCount * WorldPackFormat.ChunkDirectoryRowBytes +
            2 * WorldPackFormat.SectionDirectoryRowBytes),
        fixture.Artifact.Fingerprint.ToString(),
        fixture.DeterministicBytesEqual,
        fixture.DeterministicFingerprintEqual,
        TerrainChunkFormat.FoundationBytes,
        TerrainChunkFormat.MaterialPlaneBytes,
        TerrainChunkFormat.HeightPlaneBytes),
    new WorldPackFixtureEvidence(
        "deterministic synthetic base terrain only; no future structural-surface section is invented",
        fixture.GridEdge,
        fixture.ChunkCount,
        fixture.MinimumChunkCoordinate,
        fixture.MaximumChunkCoordinate,
        fixture.RandomReadOperations,
        fixture.ChunkCopyOperations),
    timings);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
Console.WriteLine($"pack chunks={fixture.ChunkCount}, bytes={fixture.Artifact.Length}, " +
    $"fingerprint={fixture.Artifact.Fingerprint}, deterministic={fixture.DeterministicBytesEqual}");
foreach (WorldPackBenchmarkTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F3}, max={timing.MaximumNanosecondsPerOperation:F3}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static WorldPackBenchmarkTiming Measure(
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
    return new WorldPackBenchmarkTiming(
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

sealed class WorldPackBenchmarkFixture : IDisposable
{
    private static readonly SpatialSpaceId Space = new(0xA003);
    private static readonly WorldContentFingerprint Content =
        new(0x1020_3040_5060_7080UL, 0x8070_6050_4030_2010UL);
    private readonly WorldPackLimits _limits;
    private readonly WorldPackBuilder _builder;
    private readonly byte[] _bytes;
    private readonly WorldPack _pack;
    private readonly string _path;
    private readonly ushort[] _materialScratch = new ushort[TerrainChunkFormat.MaterialCellCount];
    private readonly int[] _heightScratch = new int[TerrainChunkFormat.HeightVertexCount];

    internal WorldPackBenchmarkFixture(int gridEdge)
    {
        GridEdge = gridEdge;
        ChunkCount = checked(gridEdge * gridEdge);
        MinimumChunkCoordinate = -(gridEdge / 2);
        MaximumChunkCoordinate = checked(MinimumChunkCoordinate + gridEdge - 1);
        _limits = new WorldPackLimits(8 * 1024 * 1024, ChunkCount, 8);
        _builder = Build(reverse: false);
        if (!_builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error) || artifact is null)
            throw new InvalidOperationException($"WorldPack fixture failed: {error}.");
        Artifact = artifact;
        _bytes = new byte[artifact.Length];
        artifact.CopyTo(_bytes);
        _pack = artifact.Open();
        WorldPackBuilder reverse = Build(reverse: true);
        if (!reverse.TryBuild(out WorldPackArtifact? reverseArtifact, out error) || reverseArtifact is null)
            throw new InvalidOperationException($"Reverse WorldPack fixture failed: {error}.");
        var reverseBytes = new byte[reverseArtifact.Length];
        reverseArtifact.CopyTo(reverseBytes);
        DeterministicBytesEqual = _bytes.AsSpan().SequenceEqual(reverseBytes);
        DeterministicFingerprintEqual = artifact.Fingerprint == reverseArtifact.Fingerprint;
        if (!DeterministicBytesEqual || !DeterministicFingerprintEqual)
            throw new InvalidOperationException("WorldPack compilation was not canonical across insertion order.");
        _path = Path.Combine(Path.GetTempPath(), $"ultrasim-worldpack-benchmark-{Guid.NewGuid():N}.uwp");
        using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        artifact.WriteTo(stream);
    }

    internal int GridEdge { get; }
    internal int ChunkCount { get; }
    internal int MinimumChunkCoordinate { get; }
    internal int MaximumChunkCoordinate { get; }
    internal WorldPackArtifact Artifact { get; }
    internal bool DeterministicBytesEqual { get; }
    internal bool DeterministicFingerprintEqual { get; }
    internal int RandomReadOperations { get; private set; }
    internal int ChunkCopyOperations { get; private set; }

    internal ulong RunCompile(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!_builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError error) || artifact is null)
                throw new InvalidOperationException($"Compile failed: {error}.");
            checksum = unchecked(checksum + (uint)artifact.Length + artifact.Fingerprint.Part0);
        }
        return checksum;
    }

    internal ulong RunOpenMemory(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!WorldPack.TryOpen(_bytes, _limits, Content, out WorldPack? pack, out WorldPackOpenError error) ||
                pack is null)
                throw new InvalidOperationException($"Memory open failed: {error}.");
            checksum = unchecked(checksum + (uint)pack.Length + pack.Fingerprint.Part1);
        }
        return checksum;
    }

    internal ulong RunOpenFile(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!WorldPack.TryOpenFile(_path, _limits, Content, out WorldPack? pack,
                    out WorldPackOpenError error) || pack is null)
                throw new InvalidOperationException($"File open failed: {error}.");
            checksum = unchecked(checksum + (uint)pack.Length + pack.Fingerprint.Part2);
        }
        return checksum;
    }

    internal ulong RunRandomReads(int operations)
    {
        RandomReadOperations = operations;
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int x = MinimumChunkCoordinate + operation % GridEdge;
            int z = MinimumChunkCoordinate + operation / GridEdge % GridEdge;
            if (!_pack.TryGetBaseTerrain(new SpatialChunkKey(Space, x, z), out WorldTerrainChunkView terrain))
                throw new InvalidOperationException("Random read missed an admitted chunk.");
            int material = operation % TerrainChunkFormat.MaterialCellCount;
            int vertex = operation % TerrainChunkFormat.HeightVertexCount;
            checksum = unchecked(checksum + terrain.GetMaterial(material) +
                (uint)terrain.GetAbsoluteHeightMillimeters(vertex));
        }
        return checksum;
    }

    internal ulong RunChunkCopies(int operations)
    {
        ChunkCopyOperations = operations;
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            int x = MinimumChunkCoordinate + operation % GridEdge;
            int z = MinimumChunkCoordinate + operation / GridEdge % GridEdge;
            if (!_pack.TryGetBaseTerrain(new SpatialChunkKey(Space, x, z), out WorldTerrainChunkView terrain))
                throw new InvalidOperationException("Chunk copy missed an admitted chunk.");
            terrain.CopyMaterials(_materialScratch);
            terrain.CopyAbsoluteHeights(_heightScratch);
            checksum = unchecked(checksum + _materialScratch[operation % _materialScratch.Length] +
                (uint)_heightScratch[operation % _heightScratch.Length]);
        }
        return checksum;
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private WorldPackBuilder Build(bool reverse)
    {
        var builder = new WorldPackBuilder(Space, Content, _limits);
        var coordinates = new List<(int X, int Z)>(ChunkCount);
        for (int z = MinimumChunkCoordinate; z <= MaximumChunkCoordinate; z++)
            for (int x = MinimumChunkCoordinate; x <= MaximumChunkCoordinate; x++)
                coordinates.Add((x, z));
        if (reverse) coordinates.Reverse();
        foreach ((int x, int z) in coordinates)
        {
            CreatePlanes(x, z, out ushort[] materials, out TerrainHeightPlaneBase heightBase,
                out TerrainHeightOffset[] heights);
            if (!builder.TryAddBaseTerrain(new SpatialChunkKey(Space, x, z), materials, heightBase, heights,
                    out WorldPackBuildError error))
                throw new InvalidOperationException($"Fixture add failed: {error}.");
        }
        return builder;
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
                uint hash = unchecked((uint)(globalX * 31) ^ (uint)(globalZ * 17));
                materials[TerrainChunkFormat.GetMaterialIndex(new TerrainCellCoord(x, z))] =
                    checked((ushort)(hash % 5 + 1));
            }
        }
        var source = new int[TerrainChunkFormat.HeightVertexCount];
        for (byte z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        {
            for (byte x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                source[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(x, z))] =
                    checked(750_000 + globalX * 35 + globalZ * 20);
            }
        }
        heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        if (!TerrainHeightEncoding.TryEncodePlane(source, heights, out heightBase, out _, out _))
            throw new InvalidOperationException("Fixture height plane did not encode.");
    }
}

sealed record WorldPackBenchmarkReport(
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
    WorldPackFormatEvidence Format,
    WorldPackFixtureEvidence Fixture,
    IReadOnlyList<WorldPackBenchmarkTiming> Timings);

sealed record WorldPackFormatEvidence(
    uint ContainerVersion,
    int HeaderBytes,
    int ChunkDirectoryRowBytes,
    int SectionDirectoryRowBytes,
    int ChunkCount,
    int ArtifactBytes,
    int TerrainPayloadBytes,
    int HeaderAndDirectoryBytes,
    string PackFingerprint,
    bool DeterministicBytesAcrossInsertionOrder,
    bool DeterministicFingerprintAcrossInsertionOrder,
    int FoundationBytesPerChunk,
    int MaterialBytesPerChunk,
    int HeightBytesPerChunk);

sealed record WorldPackFixtureEvidence(
    string Classification,
    int GridEdge,
    int Chunks,
    int MinimumChunkCoordinate,
    int MaximumChunkCoordinate,
    int RandomReadOperations,
    int ChunkCopyOperations);

sealed record WorldPackBenchmarkTiming(
    string Name,
    int OperationsPerSample,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);
