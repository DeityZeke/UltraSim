using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int iterations = ReadInt(args, "--iterations=", 21);
int planesPerIteration = ReadInt(args, "--planes=", 1_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.2-height-encoding-local.json"));
if (warmups < 1 || iterations < 5 || planesPerIteration < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

const int chunkGridEdge = 8;
const int requiredMinimumHeightMillimeters = -300_000;
const int requiredMaximumHeightMillimeters = 300_000;
AuthoredTerrainFixture authored = AuthoredTerrainFixture.Create(chunkGridEdge);
HeightCandidateEvidence[] candidates =
[
    EvaluateInt16Candidate("int16-q1mm", 1, authored.StoredHeightMillimeters,
        requiredMinimumHeightMillimeters, requiredMaximumHeightMillimeters),
    EvaluateInt16Candidate("int16-q5mm", 5, authored.StoredHeightMillimeters,
        requiredMinimumHeightMillimeters, requiredMaximumHeightMillimeters),
    EvaluateInt16Candidate("int16-q10mm-selected", 10, authored.StoredHeightMillimeters,
        requiredMinimumHeightMillimeters, requiredMaximumHeightMillimeters),
    EvaluateInt16Candidate("int16-q25mm", 25, authored.StoredHeightMillimeters,
        requiredMinimumHeightMillimeters, requiredMaximumHeightMillimeters),
    EvaluateInt16Candidate("int16-q50mm", 50, authored.StoredHeightMillimeters,
        requiredMinimumHeightMillimeters, requiredMaximumHeightMillimeters),
    EvaluateInt32Candidate(authored.StoredHeightMillimeters,
        requiredMinimumHeightMillimeters, requiredMaximumHeightMillimeters),
];
HeightCandidateEvidence selected = candidates.Single(static candidate => candidate.Name == "int16-q10mm-selected");
if (!selected.SupportsRequiredEnvelope || selected.RejectedSourceSamples != 0 ||
    selected.MaximumAbsoluteErrorMillimeters > 5)
    throw new InvalidOperationException("The selected terrain encoding did not satisfy its declared contract.");

SharedEdgeEvidence sharedEdges = authored.ValidateSharedEdges();
if (sharedEdges.MismatchedEncodedEdges != 0)
    throw new InvalidOperationException("Deterministic authored terrain did not compile to exact shared edges.");

using var performance = new EncodingPerformanceFixture();
TerrainEncodingTiming[] timings =
[
    Measure("encode-int16-q10mm", warmups, iterations, planesPerIteration,
        performance.RunEncode),
    Measure("decode-int16-q10mm", warmups, iterations, planesPerIteration,
        performance.RunDecode),
    Measure("copy-int32-millimeters", warmups, iterations, planesPerIteration,
        performance.RunInt32Copy),
];

var report = new TerrainEncodingBenchmarkReport(
    "terrain-t0.2-height-encoding-v1",
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
    iterations,
    planesPerIteration,
    new TerrainGeometryEvidence(
        GameWorldSpatialProfile.WorldChunkEdgeUnits,
        GameWorldSpatialProfile.TerrainCellEdgeUnits,
        GameWorldSpatialProfile.TerrainCellsPerChunkEdge,
        GameWorldSpatialProfile.AuthoritativePositionQuantumUnits,
        TerrainChunkFormat.MaterialCellCount,
        TerrainChunkFormat.HeightVertexCount,
        TerrainChunkFormat.TriangleCount,
        TerrainChunkFormat.TriangleIndexCount,
        TerrainChunkFormat.MaterialPlaneBytes,
        TerrainChunkFormat.HeightPlaneBytes,
        TerrainChunkFormat.FoundationBytes,
        TerrainChunkFormat.DuplicatedBorderVertexCountPerChunk,
        TerrainChunkFormat.DuplicatedBorderBytesPerChunk,
        TerrainChunkFormat.HeightQuantumMillimeters,
        TerrainChunkFormat.MinimumHeightMillimeters,
        TerrainChunkFormat.MaximumHeightMillimeters,
        requiredMinimumHeightMillimeters,
        requiredMaximumHeightMillimeters,
        16_000,
        15_999),
    new DirectoryAccountingEvidence(
        FrozenAtT02: false,
        SpatialChunkKeyBytes: Marshal.SizeOf<SpatialChunkKey>(),
        RequiredTerrainSectionReferences: 2,
        CandidateOffsetLengthBytesPerReference: 16,
        CandidateMinimumChunkDirectoryRowBytes: Marshal.SizeOf<SpatialChunkKey>() + 2 * 16,
        Note: "T0.2 accounting candidate only; T0.3 freezes the sectioned container and exact directory."),
    new CompilerScratchEvidence(
        AuthoredHeightPlaneBytes: TerrainChunkFormat.HeightVertexCount * sizeof(int),
        MaterialPlaneBytes: TerrainChunkFormat.MaterialPlaneBytes,
        EncodedHeightPlaneBytes: TerrainChunkFormat.HeightPlaneBytes,
        OneChunkScratchHighWaterBytes: TerrainChunkFormat.HeightVertexCount * sizeof(int) +
            TerrainChunkFormat.MaterialPlaneBytes + TerrainChunkFormat.HeightPlaneBytes,
        TimedEncodingAllocatedBytes: timings[0].MaximumAllocatedBytes),
    authored.GetDistributionEvidence(),
    sharedEdges,
    candidates,
    timings);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
foreach (HeightCandidateEvidence candidate in candidates)
    Console.WriteLine($"{candidate.Name}: range [{candidate.MinimumDecodedMillimeters}," +
        $"{candidate.MaximumDecodedMillimeters}]mm, envelope={candidate.SupportsRequiredEnvelope}, " +
        $"rejected={candidate.RejectedSourceSamples}, max_error={candidate.MaximumAbsoluteErrorMillimeters}mm, " +
        $"foundation={candidate.FoundationBytesPerChunk}B");
foreach (TerrainEncodingTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean {timing.MeanNanosecondsPerVertex:F3} ns/vertex, " +
        $"p95 {timing.P95NanosecondsPerVertex:F3}, max {timing.MaximumNanosecondsPerVertex:F3}, " +
        $"allocation {timing.MaximumAllocatedBytes} B");
Console.WriteLine($"fixture heights [{authored.MinimumHeight},{authored.MaximumHeight}]mm, " +
    $"edge comparisons={sharedEdges.EncodedEdgeComparisons}, mismatches={sharedEdges.MismatchedEncodedEdges}");
Console.WriteLine(Path.GetFullPath(output));

static HeightCandidateEvidence EvaluateInt16Candidate(
    string name,
    int quantum,
    ReadOnlySpan<int> source,
    int requiredMinimum,
    int requiredMaximum)
{
    int minimum = checked(short.MinValue * quantum);
    int maximum = checked(short.MaxValue * quantum);
    var errors = new int[source.Length];
    int accepted = 0;
    int rejected = 0;
    long totalError = 0;
    int maximumError = 0;
    for (int index = 0; index < source.Length; index++)
    {
        int value = source[index];
        if (value < minimum || value > maximum)
        {
            rejected++;
            continue;
        }
        long magnitude = value < 0 ? -(long)value : value;
        long units = (magnitude + quantum / 2) / quantum;
        if (value < 0) units = -units;
        int decoded = checked((int)units * quantum);
        int error = Math.Abs(value - decoded);
        errors[accepted++] = error;
        totalError = checked(totalError + error);
        maximumError = Math.Max(maximumError, error);
    }
    Array.Sort(errors, 0, accepted);
    return new HeightCandidateEvidence(
        name,
        sizeof(short),
        quantum,
        minimum,
        maximum,
        minimum <= requiredMinimum && maximum >= requiredMaximum,
        source.Length,
        rejected,
        maximumError,
        accepted == 0 ? 0 : totalError / (double)accepted,
        accepted == 0 ? 0 : errors[PercentileIndex(accepted, .95)],
        TerrainChunkFormat.MaterialPlaneBytes + TerrainChunkFormat.HeightVertexCount * sizeof(short));
}

static HeightCandidateEvidence EvaluateInt32Candidate(
    ReadOnlySpan<int> source,
    int requiredMinimum,
    int requiredMaximum) => new(
        "int32-q1mm-exact",
        sizeof(int),
        1,
        int.MinValue,
        int.MaxValue,
        int.MinValue <= requiredMinimum && int.MaxValue >= requiredMaximum,
        source.Length,
        0,
        0,
        0,
        0,
        TerrainChunkFormat.MaterialPlaneBytes + TerrainChunkFormat.HeightVertexCount * sizeof(int));

static TerrainEncodingTiming Measure(
    string name,
    int warmups,
    int iterations,
    int planes,
    Func<int, ulong> run)
{
    ulong checksum = 0;
    for (int index = 0; index < warmups; index++) checksum = unchecked(checksum + run(planes));
    var elapsed = new double[iterations];
    var allocated = new long[iterations];
    long vertices = checked((long)planes * TerrainChunkFormat.HeightVertexCount);
    for (int index = 0; index < iterations; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        checksum = unchecked(checksum + run(planes));
        long finished = Stopwatch.GetTimestamp();
        allocated[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        elapsed[index] = (finished - started) * 1_000_000_000.0 / Stopwatch.Frequency / vertices;
    }
    Array.Sort(elapsed);
    return new TerrainEncodingTiming(
        name,
        elapsed.Average(),
        elapsed[PercentileIndex(iterations, .50)],
        elapsed[PercentileIndex(iterations, .95)],
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

sealed class EncodingPerformanceFixture : IDisposable
{
    private readonly int[] _source = new int[TerrainChunkFormat.HeightVertexCount];
    private readonly TerrainHeight[] _encoded = new TerrainHeight[TerrainChunkFormat.HeightVertexCount];
    private readonly int[] _decoded = new int[TerrainChunkFormat.HeightVertexCount];
    private readonly int[] _int32Copy = new int[TerrainChunkFormat.HeightVertexCount];

    internal EncodingPerformanceFixture()
    {
        for (int index = 0; index < _source.Length; index++)
            _source[index] = checked((index * 7_919 % 590_001) - 295_000);
        if (!TerrainHeightEncoding.TryEncodePlane(_source, _encoded, out _, out _))
            throw new InvalidOperationException();
    }

    internal ulong RunEncode(int planes)
    {
        ulong checksum = 0;
        for (int index = 0; index < planes; index++)
        {
            if (!TerrainHeightEncoding.TryEncodePlane(_source, _encoded, out _,
                    out TerrainHeightEncodingSummary summary))
                throw new InvalidOperationException();
            checksum = unchecked(checksum + (uint)summary.MaximumAbsoluteErrorMillimeters +
                (uint)_encoded[index % _encoded.Length].Units);
        }
        return checksum;
    }

    internal ulong RunDecode(int planes)
    {
        ulong checksum = 0;
        for (int index = 0; index < planes; index++)
        {
            TerrainHeightEncoding.DecodePlane(_encoded, _decoded);
            checksum = unchecked(checksum + (uint)_decoded[index % _decoded.Length]);
        }
        return checksum;
    }

    internal ulong RunInt32Copy(int planes)
    {
        ulong checksum = 0;
        for (int index = 0; index < planes; index++)
        {
            _source.AsSpan().CopyTo(_int32Copy);
            checksum = unchecked(checksum + (uint)_int32Copy[index % _int32Copy.Length]);
        }
        return checksum;
    }

    public void Dispose() { }
}

sealed class AuthoredTerrainFixture
{
    private AuthoredTerrainFixture(
        int chunkGridEdge,
        int[] storedHeightMillimeters,
        TerrainHeight[] encodedHeights,
        ushort[] materials,
        int minimumHeight,
        int maximumHeight)
    {
        ChunkGridEdge = chunkGridEdge;
        StoredHeightMillimeters = storedHeightMillimeters;
        EncodedHeights = encodedHeights;
        Materials = materials;
        MinimumHeight = minimumHeight;
        MaximumHeight = maximumHeight;
    }

    internal int ChunkGridEdge { get; }
    internal int[] StoredHeightMillimeters { get; }
    internal TerrainHeight[] EncodedHeights { get; }
    internal ushort[] Materials { get; }
    internal int MinimumHeight { get; }
    internal int MaximumHeight { get; }

    internal static AuthoredTerrainFixture Create(int chunkGridEdge)
    {
        int chunkCount = checked(chunkGridEdge * chunkGridEdge);
        var heights = new int[checked(chunkCount * TerrainChunkFormat.HeightVertexCount)];
        var encoded = new TerrainHeight[heights.Length];
        var materials = new ushort[checked(chunkCount * TerrainChunkFormat.MaterialCellCount)];
        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        for (int chunkZ = 0; chunkZ < chunkGridEdge; chunkZ++)
        {
            for (int chunkX = 0; chunkX < chunkGridEdge; chunkX++)
            {
                int chunk = chunkZ * chunkGridEdge + chunkX;
                int heightBase = chunk * TerrainChunkFormat.HeightVertexCount;
                var sourcePlane = heights.AsSpan(heightBase, TerrainChunkFormat.HeightVertexCount);
                for (byte localZ = 0; localZ < TerrainChunkFormat.HeightVertexEdge; localZ++)
                {
                    for (byte localX = 0; localX < TerrainChunkFormat.HeightVertexEdge; localX++)
                    {
                        int globalX = chunkX * TerrainChunkFormat.MaterialCellEdge + localX;
                        int globalZ = chunkZ * TerrainChunkFormat.MaterialCellEdge + localZ;
                        int value = GenerateHeight(globalX, globalZ, chunkGridEdge * TerrainChunkFormat.MaterialCellEdge);
                        sourcePlane[TerrainChunkFormat.GetHeightIndex(new TerrainVertexCoord(localX, localZ))] = value;
                        minimum = Math.Min(minimum, value);
                        maximum = Math.Max(maximum, value);
                    }
                }
                if (!TerrainHeightEncoding.TryEncodePlane(sourcePlane,
                        encoded.AsSpan(heightBase, TerrainChunkFormat.HeightVertexCount), out _, out _))
                    throw new InvalidOperationException("Representative terrain exceeded the selected height contract.");

                int materialBase = chunk * TerrainChunkFormat.MaterialCellCount;
                for (byte localZ = 0; localZ < TerrainChunkFormat.MaterialCellEdge; localZ++)
                {
                    for (byte localX = 0; localX < TerrainChunkFormat.MaterialCellEdge; localX++)
                    {
                        int globalX = chunkX * TerrainChunkFormat.MaterialCellEdge + localX;
                        int globalZ = chunkZ * TerrainChunkFormat.MaterialCellEdge + localZ;
                        int value = GenerateHeight(globalX, globalZ, chunkGridEdge * TerrainChunkFormat.MaterialCellEdge);
                        materials[materialBase + TerrainChunkFormat.GetMaterialIndex(
                            new TerrainCellCoord(localX, localZ))] = SelectMaterial(value, globalX, globalZ);
                    }
                }
            }
        }
        return new AuthoredTerrainFixture(chunkGridEdge, heights, encoded, materials, minimum, maximum);
    }

    internal SharedEdgeEvidence ValidateSharedEdges()
    {
        int comparisons = 0;
        int mismatches = 0;
        for (int chunkZ = 0; chunkZ < ChunkGridEdge; chunkZ++)
        {
            for (int chunkX = 0; chunkX < ChunkGridEdge; chunkX++)
            {
                int current = (chunkZ * ChunkGridEdge + chunkX) * TerrainChunkFormat.HeightVertexCount;
                if (chunkX + 1 < ChunkGridEdge)
                {
                    int right = (chunkZ * ChunkGridEdge + chunkX + 1) * TerrainChunkFormat.HeightVertexCount;
                    for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
                    {
                        comparisons++;
                        if (EncodedHeights[current + TerrainChunkFormat.GetHeightIndex(
                                new TerrainVertexCoord(32, vertex))] !=
                            EncodedHeights[right + TerrainChunkFormat.GetHeightIndex(
                                new TerrainVertexCoord(0, vertex))])
                            mismatches++;
                    }
                }
                if (chunkZ + 1 < ChunkGridEdge)
                {
                    int bottom = ((chunkZ + 1) * ChunkGridEdge + chunkX) * TerrainChunkFormat.HeightVertexCount;
                    for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
                    {
                        comparisons++;
                        if (EncodedHeights[current + TerrainChunkFormat.GetHeightIndex(
                                new TerrainVertexCoord(vertex, 32))] !=
                            EncodedHeights[bottom + TerrainChunkFormat.GetHeightIndex(
                                new TerrainVertexCoord(vertex, 0))])
                            mismatches++;
                    }
                }
            }
        }
        int stored = checked(ChunkGridEdge * ChunkGridEdge * TerrainChunkFormat.HeightVertexCount);
        int uniqueEdge = checked(ChunkGridEdge * TerrainChunkFormat.MaterialCellEdge + 1);
        int unique = checked(uniqueEdge * uniqueEdge);
        return new SharedEdgeEvidence(
            comparisons,
            mismatches,
            stored,
            unique,
            stored - unique,
            checked((stored - unique) * sizeof(short)));
    }

    internal AuthoredTerrainDistribution GetDistributionEvidence()
    {
        int[] ordered = (int[])StoredHeightMillimeters.Clone();
        Array.Sort(ordered);
        MaterialCount[] materialCounts = Materials
            .GroupBy(static material => material)
            .OrderBy(static group => group.Key)
            .Select(static group => new MaterialCount(group.Key, group.Count()))
            .ToArray();
        return new AuthoredTerrainDistribution(
            "deterministic-authored-style woods/coast/ridge fixture; production world content is not yet authored",
            ChunkGridEdge * ChunkGridEdge,
            StoredHeightMillimeters.Length,
            Materials.Length,
            ordered[0],
            ordered[GetPercentileIndex(ordered.Length, .05)],
            ordered[GetPercentileIndex(ordered.Length, .50)],
            ordered[GetPercentileIndex(ordered.Length, .95)],
            ordered[^1],
            materialCounts);
    }

    private static int GenerateHeight(int globalX, int globalZ, int worldCellEdge)
    {
        if (globalX == 0 && globalZ == 0) return -300_000;
        if (globalX == worldCellEdge && globalZ == worldCellEdge) return 300_000;
        uint hash = unchecked((uint)(globalX * 73_856_093) ^ (uint)(globalZ * 19_349_663) ^ 0x9E37_79B9u);
        int noise = (int)(hash % 10_001) - 5_000;
        int height;
        if (globalX < worldCellEdge / 3)
        {
            height = -105_000 + globalX * 1_050 + globalZ * 90 + noise;
        }
        else if (globalX < worldCellEdge * 2 / 3)
        {
            int dx = globalX - worldCellEdge / 2;
            int dz = globalZ - worldCellEdge / 2;
            height = 12_000 + checked((dx * dx + dz * dz) * 7) + noise;
        }
        else
        {
            int ridge = globalX - worldCellEdge * 2 / 3;
            int shoulder = worldCellEdge / 2 - Math.Abs(globalZ - worldCellEdge / 2);
            height = 55_000 + ridge * 2_550 + shoulder * 280 + noise;
        }
        return Math.Clamp(height, -299_999, 299_999);
    }

    private static ushort SelectMaterial(int height, int globalX, int globalZ)
    {
        if (height < -10_000) return 1;
        if (height < 8_000) return 2;
        if (height > 180_000) return 5;
        if (height > 80_000) return 4;
        return (globalX * 31 + globalZ * 17) % 19 == 0 ? (ushort)3 : (ushort)2;
    }

    private static int GetPercentileIndex(int count, double percentile) =>
        Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
}

sealed record TerrainEncodingBenchmarkReport(
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
    int Iterations,
    int PlanesPerIteration,
    TerrainGeometryEvidence Geometry,
    DirectoryAccountingEvidence DirectoryAccounting,
    CompilerScratchEvidence CompilerScratch,
    AuthoredTerrainDistribution AuthoredSource,
    SharedEdgeEvidence SharedEdges,
    IReadOnlyList<HeightCandidateEvidence> HeightCandidates,
    IReadOnlyList<TerrainEncodingTiming> Timings);

sealed record TerrainGeometryEvidence(
    int WorldChunkEdgeMillimeters,
    int TerrainCellEdgeMillimeters,
    int TerrainCellsPerChunkEdge,
    int DynamicPositionQuantumMillimeters,
    int MaterialCellsPerChunk,
    int HeightVerticesPerChunk,
    int TrianglesPerChunk,
    int TriangleIndicesPerChunk,
    int MaterialPlaneBytes,
    int HeightPlaneBytes,
    int FoundationBytes,
    int DuplicatedBorderVerticesPerChunk,
    int DuplicatedBorderBytesPerChunk,
    int SelectedHeightQuantumMillimeters,
    int MinimumTerrainHeightMillimeters,
    int MaximumTerrainHeightMillimeters,
    int RequiredAuthoredMinimumMillimeters,
    int RequiredAuthoredMaximumMillimeters,
    int MaximumTerrainVertexLocalAxisMillimetersInclusive,
    int MaximumDynamicLocalAxisMillimetersInclusive);

sealed record DirectoryAccountingEvidence(
    bool FrozenAtT02,
    int SpatialChunkKeyBytes,
    int RequiredTerrainSectionReferences,
    int CandidateOffsetLengthBytesPerReference,
    int CandidateMinimumChunkDirectoryRowBytes,
    string Note);

sealed record CompilerScratchEvidence(
    int AuthoredHeightPlaneBytes,
    int MaterialPlaneBytes,
    int EncodedHeightPlaneBytes,
    int OneChunkScratchHighWaterBytes,
    long TimedEncodingAllocatedBytes);

sealed record AuthoredTerrainDistribution(
    string Classification,
    int Chunks,
    int StoredHeightSamples,
    int MaterialSamples,
    int MinimumHeightMillimeters,
    int P05HeightMillimeters,
    int P50HeightMillimeters,
    int P95HeightMillimeters,
    int MaximumHeightMillimeters,
    IReadOnlyList<MaterialCount> MaterialCounts);

sealed record MaterialCount(ushort MaterialId, int Count);

sealed record SharedEdgeEvidence(
    int EncodedEdgeComparisons,
    int MismatchedEncodedEdges,
    int StoredHeightVertices,
    int UniqueGlobalHeightVertices,
    int DuplicatedHeightVertices,
    int DuplicatedHeightBytes);

sealed record HeightCandidateEvidence(
    string Name,
    int StorageBytesPerVertex,
    int QuantumMillimeters,
    int MinimumDecodedMillimeters,
    int MaximumDecodedMillimeters,
    bool SupportsRequiredEnvelope,
    int SourceSamples,
    int RejectedSourceSamples,
    int MaximumAbsoluteErrorMillimeters,
    double MeanAbsoluteErrorMillimeters,
    int P95AbsoluteErrorMillimeters,
    int FoundationBytesPerChunk);

sealed record TerrainEncodingTiming(
    string Name,
    double MeanNanosecondsPerVertex,
    double P50NanosecondsPerVertex,
    double P95NanosecondsPerVertex,
    double MaximumNanosecondsPerVertex,
    long MaximumAllocatedBytes,
    ulong Checksum);
