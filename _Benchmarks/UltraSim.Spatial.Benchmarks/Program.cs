using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int iterations = ReadInt(args, "--iterations=", 31);
int operations = ReadInt(args, "--operations=", 500_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.0-spatial-contracts-local.json"));
if (warmups < 1 || iterations < 5 || operations < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

var scenarios = new List<SpatialScenarioResult>
{
    Measure("normalize-global-to-chunk-local", warmups, iterations, operations, new NormalizeScenario()),
    Measure("reconstruct-global-position", warmups, iterations, operations, new ReconstructScenario()),
    Measure("chunk-key-hash-order", warmups, iterations, operations, new ChunkKeyScenario()),
    Measure("chunk-bounds-distance-overlap", warmups, iterations, operations, new BoundsScenario()),
    Measure("terrain-cell-from-local", warmups, iterations, operations, new TerrainCellScenario()),
};

var report = new SpatialBenchmarkReport(
    "terrain-t0.0-spatial-contracts-v1",
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
    operations,
    new SpatialProfileEvidence(
        GameWorldSpatialProfile.UnitsPerMeter,
        GameWorldSpatialProfile.WorldChunkEdgeUnits,
        GameWorldSpatialProfile.TerrainCellEdgeUnits,
        GameWorldSpatialProfile.PlacementSnapUnits,
        GameWorldSpatialProfile.AuthoritativePositionQuantumUnits,
        GameWorldSpatialProfile.MinimumGlobalAxisUnits,
        GameWorldSpatialProfile.MaximumGlobalAxisUnits,
        Marshal.SizeOf<SpatialChunkKey>(),
        Marshal.SizeOf<WorldPosition>()),
    scenarios);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
foreach (SpatialScenarioResult result in scenarios)
    Console.WriteLine($"{result.Name}: mean {result.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p50 {result.P50NanosecondsPerOperation:F3}, p95 {result.P95NanosecondsPerOperation:F3}, " +
        $"max {result.MaximumNanosecondsPerOperation:F3}, allocation {result.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static SpatialScenarioResult Measure(
    string name,
    int warmups,
    int iterations,
    int operations,
    ISpatialScenario scenario)
{
    ulong warmChecksum = 0;
    for (int index = 0; index < warmups; index++)
        warmChecksum = unchecked(warmChecksum + scenario.Run(operations));
    var elapsed = new double[iterations];
    var allocated = new long[iterations];
    ulong checksum = warmChecksum;
    for (int index = 0; index < iterations; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        checksum = unchecked(checksum + scenario.Run(operations));
        long finished = Stopwatch.GetTimestamp();
        allocated[index] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        elapsed[index] = (finished - started) * 1_000_000_000.0 / Stopwatch.Frequency / operations;
    }

    Array.Sort(elapsed);
    return new SpatialScenarioResult(
        name,
        elapsed.Average(),
        elapsed[PercentileIndex(elapsed.Length, 0.50)],
        elapsed[PercentileIndex(elapsed.Length, 0.95)],
        elapsed[^1],
        allocated.Max(),
        checksum);
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);

static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..],
        out int parsed) ? parsed : fallback;

static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

internal interface ISpatialScenario
{
    ulong Run(int operations);
}

internal sealed class NormalizeScenario : ISpatialScenario
{
    private readonly SpatialChunkGeometry _geometry = GameWorldSpatialProfile.ChunkGeometry;

    public ulong Run(int operations)
    {
        ulong checksum = 0;
        long x = -8_000_000_001;
        long z = 5_000_000_003;
        for (int index = 0; index < operations; index++)
        {
            x = unchecked(x + 104_729);
            z = unchecked(z - 130_363);
            if (!_geometry.TryNormalize(x, z, out SpatialChunkCoord chunk, out int localX, out int localZ))
                throw new InvalidOperationException("Normalization benchmark left the admitted profile.");
            checksum = unchecked(checksum + (uint)chunk.X + (uint)chunk.Z + (uint)localX + (uint)localZ);
        }
        return checksum;
    }
}

internal sealed class ReconstructScenario : ISpatialScenario
{
    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            var position = new WorldPosition(
                unchecked(index * 17 - 2_000_000),
                unchecked(index * -19 + 1_000_000),
                unchecked((ushort)(index % GameWorldSpatialProfile.WorldChunkEdgeUnits)),
                unchecked((ushort)((index * 31) % GameWorldSpatialProfile.WorldChunkEdgeUnits)),
                index);
            checksum = unchecked(checksum + (ulong)position.GlobalXMillimeters +
                (ulong)position.GlobalZMillimeters + (uint)position.YMillimeters);
        }
        return checksum;
    }
}

internal sealed class ChunkKeyScenario : ISpatialScenario
{
    private readonly SpatialSpaceId _space = new(0x5445525241494E);

    public ulong Run(int operations)
    {
        ulong checksum = 0;
        SpatialChunkKey previous = new(_space, int.MinValue, int.MinValue);
        for (int index = 0; index < operations; index++)
        {
            var current = new SpatialChunkKey(_space, unchecked(index * 104_729), unchecked(index * -130_363));
            checksum = unchecked(checksum + (uint)current.GetHashCode() + (uint)current.CompareTo(previous));
            previous = current;
        }
        return checksum;
    }
}

internal sealed class BoundsScenario : ISpatialScenario
{
    private readonly SpatialChunkGeometry _geometry = GameWorldSpatialProfile.ChunkGeometry;

    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            var coord = new SpatialChunkCoord(index - operations / 2, operations / 3 - index);
            SpatialBounds bounds = _geometry.GetBounds(coord);
            long pointX = checked(bounds.MaxXExclusive + index % 257);
            long pointZ = checked(bounds.MinZ - index % 131);
            Int128 distance = bounds.SquaredDistanceTo(pointX, pointZ);
            bool overlap = bounds.IntersectsCircle(pointX, pointZ, 512);
            checksum = unchecked(checksum + (ulong)distance + (overlap ? 1UL : 0UL));
        }
        return checksum;
    }
}

internal sealed class TerrainCellScenario : ISpatialScenario
{
    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            var local = new WorldLocalPosition(
                unchecked((ushort)(index % GameWorldSpatialProfile.WorldChunkEdgeUnits)),
                unchecked((ushort)((index * 31) % GameWorldSpatialProfile.WorldChunkEdgeUnits)),
                index);
            TerrainCellCoord cell = TerrainCellCoord.FromLocal(local);
            checksum = unchecked(checksum + cell.X + (uint)(cell.Z << 8));
        }
        return checksum;
    }
}

internal sealed record SpatialBenchmarkReport(
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
    int OperationsPerIteration,
    SpatialProfileEvidence Profile,
    IReadOnlyList<SpatialScenarioResult> Scenarios);

internal sealed record SpatialProfileEvidence(
    int UnitsPerMeter,
    int WorldChunkEdgeUnits,
    int TerrainCellEdgeUnits,
    int PlacementSnapUnits,
    int PositionQuantumUnits,
    long MinimumGlobalAxisUnits,
    long MaximumGlobalAxisUnits,
    int SpatialChunkKeyBytes,
    int WorldPositionBytes);

internal sealed record SpatialScenarioResult(
    string Name,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);
