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
int[] elevationDatumsMillimeters = [-1_200_000, 0, 1_200_000];
AuthoredTerrainFixture authored = AuthoredTerrainFixture.Create(chunkGridEdge, elevationDatumsMillimeters);
int requiredMinimum = authored.MinimumHeight;
int requiredMaximum = authored.MaximumHeight;
HeightCandidateEvidence[] candidates =
[
    EvaluateGlobalInt16Candidate("int16-global-q5mm", 5, authored, requiredMinimum, requiredMaximum),
    EvaluateGlobalInt16Candidate("int16-global-q10mm-control", 10, authored, requiredMinimum, requiredMaximum),
    EvaluateBasedUInt16Candidate("uint16-based-q5mm-selected", 5, authored, requiredMinimum, requiredMaximum),
    EvaluateBasedUInt16Candidate("uint16-based-q10mm", 10, authored, requiredMinimum, requiredMaximum),
    EvaluateGlobalInt16Candidate("int16-global-q25mm", 25, authored, requiredMinimum, requiredMaximum),
    EvaluateInt32Candidate(authored, requiredMinimum, requiredMaximum),
];
HeightCandidateEvidence selected = candidates.Single(
    static candidate => candidate.Name == "uint16-based-q5mm-selected");
if (!selected.SupportsRequiredAbsoluteEnvelope || selected.RejectedSourceSamples != 0 ||
    selected.RejectedChunks != 0 || selected.MaximumAbsoluteErrorMillimeters > 2)
    throw new InvalidOperationException("The selected terrain encoding did not satisfy its declared contract.");

SharedEdgeEvidence sharedEdges = authored.ValidateSharedEdges();
if (sharedEdges.MismatchedReconstructedEdges != 0 || sharedEdges.NeighborPairsWithDifferentBases == 0)
    throw new InvalidOperationException("Based terrain did not reconstruct exact cross-base shared edges.");

using var performance = new EncodingPerformanceFixture();
TerrainEncodingTiming[] timings =
[
    Measure("encode-uint16-offset-q5mm", warmups, iterations, planesPerIteration, performance.RunEncode),
    Measure("decode-uint16-offset-q5mm", warmups, iterations, planesPerIteration, performance.RunDecode),
    Measure("copy-int32-millimeters", warmups, iterations, planesPerIteration, performance.RunInt32Copy),
];

var report = new TerrainEncodingBenchmarkReport(
    "terrain-t0.2-height-encoding-v2-based",
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
        TerrainChunkFormat.HeightPlaneBaseBytes,
        TerrainChunkFormat.HeightOffsetPlaneBytes,
        TerrainChunkFormat.HeightPlaneBytes,
        TerrainChunkFormat.FoundationBytes,
        TerrainChunkFormat.DuplicatedBorderVertexCountPerChunk,
        TerrainChunkFormat.DuplicatedBorderBytesPerChunk,
        TerrainChunkFormat.HeightQuantumMillimeters,
        TerrainChunkFormat.MaximumHeightReliefMillimeters,
        TerrainChunkFormat.MinimumQuantizedHeightMillimeters,
        TerrainChunkFormat.MaximumQuantizedHeightMillimeters,
        requiredMinimum,
        requiredMaximum,
        16_000,
        15_999),
    new DirectoryAccountingEvidence(
        FrozenAtT02: false,
        SpatialChunkKeyBytes: Marshal.SizeOf<SpatialChunkKey>(),
        RequiredTerrainSectionReferences: 2,
        CandidateOffsetLengthBytesPerReference: 16,
        CandidateMinimumChunkDirectoryRowBytes: Marshal.SizeOf<SpatialChunkKey>() + 2 * 16,
        HeightBaseChargedToHeightPlaneBytes: TerrainChunkFormat.HeightPlaneBaseBytes,
        Note: "T0.2 charges the base to TerrainHeights/v1; T0.3 freezes the container and directory."),
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
    Console.WriteLine($"{candidate.Name}: absolute [{candidate.MinimumAbsoluteDecodedMillimeters}," +
        $"{candidate.MaximumAbsoluteDecodedMillimeters}]mm, local_relief={candidate.MaximumLocalReliefMillimeters}mm, " +
        $"envelope={candidate.SupportsRequiredAbsoluteEnvelope}, rejected_chunks={candidate.RejectedChunks}, " +
        $"rejected_samples={candidate.RejectedSourceSamples}, max_error={candidate.MaximumAbsoluteErrorMillimeters}mm, " +
        $"foundation={candidate.FoundationBytesPerChunk}B");
foreach (TerrainEncodingTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean {timing.MeanNanosecondsPerVertex:F3} ns/vertex, " +
        $"p95 {timing.P95NanosecondsPerVertex:F3}, max {timing.MaximumNanosecondsPerVertex:F3}, " +
        $"allocation {timing.MaximumAllocatedBytes} B");
Console.WriteLine($"fixture heights [{authored.MinimumHeight},{authored.MaximumHeight}]mm, " +
    $"edge comparisons={sharedEdges.ReconstructedEdgeComparisons}, " +
    $"cross-base pairs={sharedEdges.NeighborPairsWithDifferentBases}, " +
    $"mismatches={sharedEdges.MismatchedReconstructedEdges}");
Console.WriteLine(Path.GetFullPath(output));

static HeightCandidateEvidence EvaluateGlobalInt16Candidate(
    string name,
    int quantum,
    AuthoredTerrainFixture fixture,
    int requiredMinimum,
    int requiredMaximum)
{
    int minimum = checked(short.MinValue * quantum);
    int maximum = checked(short.MaxValue * quantum);
    var errors = new int[fixture.StoredHeightMillimeters.Length];
    int accepted = 0;
    int rejected = 0;
    int rejectedChunks = 0;
    long totalError = 0;
    int maximumError = 0;
    for (int chunk = 0; chunk < fixture.ChunkCount; chunk++)
    {
        bool chunkRejected = false;
        int start = chunk * TerrainChunkFormat.HeightVertexCount;
        for (int local = 0; local < TerrainChunkFormat.HeightVertexCount; local++)
        {
            int value = fixture.StoredHeightMillimeters[start + local];
            if (!TryQuantize(value, quantum, out int decoded) || decoded < minimum || decoded > maximum)
            {
                rejected++;
                chunkRejected = true;
                continue;
            }
            int error = Math.Abs(value - decoded);
            errors[accepted++] = error;
            totalError = checked(totalError + error);
            maximumError = Math.Max(maximumError, error);
        }
        if (chunkRejected) rejectedChunks++;
    }
    Array.Sort(errors, 0, accepted);
    return new HeightCandidateEvidence(
        name,
        "signed-int16-global",
        sizeof(short),
        0,
        quantum,
        minimum,
        maximum,
        checked(maximum - minimum),
        minimum <= requiredMinimum && maximum >= requiredMaximum,
        fixture.StoredHeightMillimeters.Length,
        rejected,
        rejectedChunks,
        maximumError,
        accepted == 0 ? 0 : totalError / (double)accepted,
        accepted == 0 ? 0 : errors[PercentileIndex(accepted, .95)],
        TerrainChunkFormat.MaterialPlaneBytes + TerrainChunkFormat.HeightVertexCount * sizeof(short));
}

static HeightCandidateEvidence EvaluateBasedUInt16Candidate(
    string name,
    int quantum,
    AuthoredTerrainFixture fixture,
    int requiredMinimum,
    int requiredMaximum)
{
    int minimumAbsolute = int.MinValue / quantum * quantum;
    int maximumAbsolute = int.MaxValue / quantum * quantum;
    int maximumRelief = checked(ushort.MaxValue * quantum);
    var errors = new int[fixture.StoredHeightMillimeters.Length];
    int accepted = 0;
    int rejected = 0;
    int rejectedChunks = 0;
    long totalError = 0;
    int maximumError = 0;
    int[] plane = new int[TerrainChunkFormat.HeightVertexCount];
    for (int chunk = 0; chunk < fixture.ChunkCount; chunk++)
    {
        int start = chunk * TerrainChunkFormat.HeightVertexCount;
        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        bool invalid = false;
        for (int local = 0; local < plane.Length; local++)
        {
            int value = fixture.StoredHeightMillimeters[start + local];
            if (!TryQuantize(value, quantum, out int decoded))
            {
                invalid = true;
                break;
            }
            plane[local] = decoded;
            minimum = Math.Min(minimum, decoded);
            maximum = Math.Max(maximum, decoded);
        }
        if (invalid || (long)maximum - minimum > maximumRelief)
        {
            rejectedChunks++;
            rejected += plane.Length;
            continue;
        }
        for (int local = 0; local < plane.Length; local++)
        {
            int value = fixture.StoredHeightMillimeters[start + local];
            int error = Math.Abs(value - plane[local]);
            errors[accepted++] = error;
            totalError = checked(totalError + error);
            maximumError = Math.Max(maximumError, error);
        }
    }
    Array.Sort(errors, 0, accepted);
    return new HeightCandidateEvidence(
        name,
        "uint16-offset-plus-int32-base",
        sizeof(ushort),
        sizeof(int),
        quantum,
        minimumAbsolute,
        maximumAbsolute,
        maximumRelief,
        minimumAbsolute <= requiredMinimum && maximumAbsolute >= requiredMaximum,
        fixture.StoredHeightMillimeters.Length,
        rejected,
        rejectedChunks,
        maximumError,
        accepted == 0 ? 0 : totalError / (double)accepted,
        accepted == 0 ? 0 : errors[PercentileIndex(accepted, .95)],
        TerrainChunkFormat.MaterialPlaneBytes + sizeof(int) +
            TerrainChunkFormat.HeightVertexCount * sizeof(ushort));
}

static HeightCandidateEvidence EvaluateInt32Candidate(
    AuthoredTerrainFixture fixture,
    int requiredMinimum,
    int requiredMaximum) => new(
        "int32-q1mm-exact",
        "signed-int32-global",
        sizeof(int),
        0,
        1,
        int.MinValue,
        int.MaxValue,
        (long)int.MaxValue - int.MinValue,
        int.MinValue <= requiredMinimum && int.MaxValue >= requiredMaximum,
        fixture.StoredHeightMillimeters.Length,
        0,
        0,
        0,
        0,
        0,
        TerrainChunkFormat.MaterialPlaneBytes + TerrainChunkFormat.HeightVertexCount * sizeof(int));

static bool TryQuantize(int millimeters, int quantum, out int quantized)
{
    long magnitude = millimeters < 0 ? -(long)millimeters : millimeters;
    long units = (magnitude + quantum / 2) / quantum;
    if (millimeters < 0) units = -units;
    long decoded = units * quantum;
    if (decoded is < int.MinValue or > int.MaxValue)
    {
        quantized = 0;
        return false;
    }
    quantized = (int)decoded;
    return true;
}

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
    private readonly TerrainHeightOffset[] _encoded = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    private readonly int[] _decoded = new int[TerrainChunkFormat.HeightVertexCount];
    private readonly int[] _int32Copy = new int[TerrainChunkFormat.HeightVertexCount];
    private TerrainHeightPlaneBase _planeBase;

    internal EncodingPerformanceFixture()
    {
        for (int index = 0; index < _source.Length; index++)
            _source[index] = checked(1_400_000 + (index * 7_919 % 300_001) - 150_000);
        if (!TerrainHeightEncoding.TryEncodePlane(_source, _encoded, out _planeBase, out _, out _))
            throw new InvalidOperationException();
    }

    internal ulong RunEncode(int planes)
    {
        ulong checksum = 0;
        for (int index = 0; index < planes; index++)
        {
            if (!TerrainHeightEncoding.TryEncodePlane(_source, _encoded, out _planeBase, out _,
                    out TerrainHeightEncodingSummary summary))
                throw new InvalidOperationException();
            checksum = unchecked(checksum + (uint)_planeBase.Millimeters +
                (uint)summary.MaximumAbsoluteErrorMillimeters +
                _encoded[index % _encoded.Length].Units);
        }
        return checksum;
    }

    internal ulong RunDecode(int planes)
    {
        ulong checksum = 0;
        for (int index = 0; index < planes; index++)
        {
            if (!TerrainHeightEncoding.TryDecodePlane(_planeBase, _encoded, _decoded, out _))
                throw new InvalidOperationException();
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
        int worldCount,
        int[] storedHeightMillimeters,
        TerrainHeightPlaneBase[] planeBases,
        TerrainHeightOffset[] encodedOffsets,
        ushort[] materials,
        int minimumHeight,
        int maximumHeight)
    {
        ChunkGridEdge = chunkGridEdge;
        WorldCount = worldCount;
        StoredHeightMillimeters = storedHeightMillimeters;
        PlaneBases = planeBases;
        EncodedOffsets = encodedOffsets;
        Materials = materials;
        MinimumHeight = minimumHeight;
        MaximumHeight = maximumHeight;
    }

    internal int ChunkGridEdge { get; }
    internal int WorldCount { get; }
    internal int ChunkCount => checked(WorldCount * ChunkGridEdge * ChunkGridEdge);
    internal int[] StoredHeightMillimeters { get; }
    internal TerrainHeightPlaneBase[] PlaneBases { get; }
    internal TerrainHeightOffset[] EncodedOffsets { get; }
    internal ushort[] Materials { get; }
    internal int MinimumHeight { get; }
    internal int MaximumHeight { get; }

    internal static AuthoredTerrainFixture Create(int chunkGridEdge, ReadOnlySpan<int> elevationDatums)
    {
        int chunksPerWorld = checked(chunkGridEdge * chunkGridEdge);
        int chunkCount = checked(elevationDatums.Length * chunksPerWorld);
        var heights = new int[checked(chunkCount * TerrainChunkFormat.HeightVertexCount)];
        var planeBases = new TerrainHeightPlaneBase[chunkCount];
        var encoded = new TerrainHeightOffset[heights.Length];
        var materials = new ushort[checked(chunkCount * TerrainChunkFormat.MaterialCellCount)];
        int minimum = int.MaxValue;
        int maximum = int.MinValue;
        for (int world = 0; world < elevationDatums.Length; world++)
        {
            int elevationDatum = elevationDatums[world];
            for (int chunkZ = 0; chunkZ < chunkGridEdge; chunkZ++)
            {
                for (int chunkX = 0; chunkX < chunkGridEdge; chunkX++)
                {
                    int chunk = world * chunksPerWorld + chunkZ * chunkGridEdge + chunkX;
                    int heightStart = chunk * TerrainChunkFormat.HeightVertexCount;
                    Span<int> sourcePlane = heights.AsSpan(heightStart, TerrainChunkFormat.HeightVertexCount);
                    for (byte localZ = 0; localZ < TerrainChunkFormat.HeightVertexEdge; localZ++)
                    {
                        for (byte localX = 0; localX < TerrainChunkFormat.HeightVertexEdge; localX++)
                        {
                            int globalX = chunkX * TerrainChunkFormat.MaterialCellEdge + localX;
                            int globalZ = chunkZ * TerrainChunkFormat.MaterialCellEdge + localZ;
                            int relative = GenerateHeight(globalX, globalZ,
                                chunkGridEdge * TerrainChunkFormat.MaterialCellEdge);
                            int value = checked(elevationDatum + relative);
                            sourcePlane[TerrainChunkFormat.GetHeightIndex(
                                new TerrainVertexCoord(localX, localZ))] = value;
                            minimum = Math.Min(minimum, value);
                            maximum = Math.Max(maximum, value);
                        }
                    }
                    if (!TerrainHeightEncoding.TryEncodePlane(sourcePlane,
                            encoded.AsSpan(heightStart, TerrainChunkFormat.HeightVertexCount),
                            out planeBases[chunk], out _, out _))
                        throw new InvalidOperationException("Representative terrain exceeded the selected contract.");

                    int materialStart = chunk * TerrainChunkFormat.MaterialCellCount;
                    for (byte localZ = 0; localZ < TerrainChunkFormat.MaterialCellEdge; localZ++)
                    {
                        for (byte localX = 0; localX < TerrainChunkFormat.MaterialCellEdge; localX++)
                        {
                            int globalX = chunkX * TerrainChunkFormat.MaterialCellEdge + localX;
                            int globalZ = chunkZ * TerrainChunkFormat.MaterialCellEdge + localZ;
                            int relative = GenerateHeight(globalX, globalZ,
                                chunkGridEdge * TerrainChunkFormat.MaterialCellEdge);
                            materials[materialStart + TerrainChunkFormat.GetMaterialIndex(
                                new TerrainCellCoord(localX, localZ))] = SelectMaterial(relative, globalX, globalZ);
                        }
                    }
                }
            }
        }
        return new AuthoredTerrainFixture(chunkGridEdge, elevationDatums.Length, heights, planeBases,
            encoded, materials, minimum, maximum);
    }

    internal SharedEdgeEvidence ValidateSharedEdges()
    {
        int comparisons = 0;
        int mismatches = 0;
        int differentBasePairs = 0;
        int chunksPerWorld = checked(ChunkGridEdge * ChunkGridEdge);
        for (int world = 0; world < WorldCount; world++)
        {
            for (int chunkZ = 0; chunkZ < ChunkGridEdge; chunkZ++)
            {
                for (int chunkX = 0; chunkX < ChunkGridEdge; chunkX++)
                {
                    int chunk = world * chunksPerWorld + chunkZ * ChunkGridEdge + chunkX;
                    if (chunkX + 1 < ChunkGridEdge)
                        CompareNeighbor(chunk, chunk + 1, verticalEdge: true,
                            ref comparisons, ref mismatches, ref differentBasePairs);
                    if (chunkZ + 1 < ChunkGridEdge)
                        CompareNeighbor(chunk, chunk + ChunkGridEdge, verticalEdge: false,
                            ref comparisons, ref mismatches, ref differentBasePairs);
                }
            }
        }
        int stored = checked(ChunkCount * TerrainChunkFormat.HeightVertexCount);
        int uniqueEdge = checked(ChunkGridEdge * TerrainChunkFormat.MaterialCellEdge + 1);
        int unique = checked(WorldCount * uniqueEdge * uniqueEdge);
        return new SharedEdgeEvidence(
            comparisons,
            mismatches,
            differentBasePairs,
            stored,
            unique,
            stored - unique,
            checked((stored - unique) * sizeof(ushort)));
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
            "synthetic woods/coast/ridge fixture repeated in three independent elevation datums; production content is not authored",
            WorldCount,
            ChunkCount,
            StoredHeightMillimeters.Length,
            Materials.Length,
            ordered[0],
            ordered[GetPercentileIndex(ordered.Length, .05)],
            ordered[GetPercentileIndex(ordered.Length, .50)],
            ordered[GetPercentileIndex(ordered.Length, .95)],
            ordered[^1],
            materialCounts);
    }

    private void CompareNeighbor(
        int firstChunk,
        int secondChunk,
        bool verticalEdge,
        ref int comparisons,
        ref int mismatches,
        ref int differentBasePairs)
    {
        if (PlaneBases[firstChunk] != PlaneBases[secondChunk]) differentBasePairs++;
        int firstStart = firstChunk * TerrainChunkFormat.HeightVertexCount;
        int secondStart = secondChunk * TerrainChunkFormat.HeightVertexCount;
        for (byte vertex = 0; vertex < TerrainChunkFormat.HeightVertexEdge; vertex++)
        {
            TerrainVertexCoord firstCoord = verticalEdge
                ? new TerrainVertexCoord(32, vertex)
                : new TerrainVertexCoord(vertex, 32);
            TerrainVertexCoord secondCoord = verticalEdge
                ? new TerrainVertexCoord(0, vertex)
                : new TerrainVertexCoord(vertex, 0);
            int first = checked(PlaneBases[firstChunk].Millimeters +
                EncodedOffsets[firstStart + TerrainChunkFormat.GetHeightIndex(firstCoord)].OffsetMillimeters);
            int second = checked(PlaneBases[secondChunk].Millimeters +
                EncodedOffsets[secondStart + TerrainChunkFormat.GetHeightIndex(secondCoord)].OffsetMillimeters);
            comparisons++;
            if (first != second) mismatches++;
        }
    }

    private static int GenerateHeight(int globalX, int globalZ, int worldCellEdge)
    {
        if (globalX == 0 && globalZ == 0) return -300_000;
        if (globalX == worldCellEdge && globalZ == worldCellEdge) return 300_000;
        uint hash = unchecked((uint)(globalX * 73_856_093) ^ (uint)(globalZ * 19_349_663) ^ 0x9E37_79B9u);
        int noise = (int)(hash % 10_001) - 5_000;
        int height;
        if (globalX < worldCellEdge / 3)
            height = -105_000 + globalX * 1_050 + globalZ * 90 + noise;
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
    int HeightPlaneBaseBytes,
    int HeightOffsetPlaneBytes,
    int HeightPlaneBytes,
    int FoundationBytes,
    int DuplicatedBorderVerticesPerChunk,
    int DuplicatedBorderBytesPerChunk,
    int SelectedHeightQuantumMillimeters,
    long MaximumLocalReliefMillimeters,
    int MinimumAbsoluteQuantizedMillimeters,
    int MaximumAbsoluteQuantizedMillimeters,
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
    int HeightBaseChargedToHeightPlaneBytes,
    string Note);

sealed record CompilerScratchEvidence(
    int AuthoredHeightPlaneBytes,
    int MaterialPlaneBytes,
    int EncodedHeightPlaneBytes,
    int OneChunkScratchHighWaterBytes,
    long TimedEncodingAllocatedBytes);

sealed record AuthoredTerrainDistribution(
    string Classification,
    int IndependentElevationDatums,
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
    int ReconstructedEdgeComparisons,
    int MismatchedReconstructedEdges,
    int NeighborPairsWithDifferentBases,
    int StoredHeightVertices,
    int UniqueGlobalHeightVertices,
    int DuplicatedHeightVertices,
    int DuplicatedHeightBytes);

sealed record HeightCandidateEvidence(
    string Name,
    string Representation,
    int StorageBytesPerVertex,
    int BaseBytesPerChunk,
    int QuantumMillimeters,
    int MinimumAbsoluteDecodedMillimeters,
    int MaximumAbsoluteDecodedMillimeters,
    long MaximumLocalReliefMillimeters,
    bool SupportsRequiredAbsoluteEnvelope,
    int SourceSamples,
    int RejectedSourceSamples,
    int RejectedChunks,
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
