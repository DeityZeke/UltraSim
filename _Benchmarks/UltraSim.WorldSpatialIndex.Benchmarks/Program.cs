using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int iterations = ReadInt(args, "--iterations=", 21);
int operations = ReadInt(args, "--operations=", 100_000);
int rebuilds = ReadInt(args, "--rebuilds=", 64);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.1-world-spatial-index-local.json"));
if (warmups < 1 || iterations < 5 || operations < 1 || rebuilds < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

using var idle = BenchFixture.OneEntity();
using var same = BenchFixture.OneEntity();
using var crossing = BenchFixture.OneEntity();
using var lifecycle = BenchFixture.Empty(64);
using var rebuild = BenchFixture.Populated(4_096, distinctChunks: true);
using var iteration = BenchFixture.Populated(1_024, distinctChunks: false);

var scenarios = new List<WorldSpatialScenarioResult>
{
    Measure("post-step-observation-no-change", warmups, iterations, operations,
        count => idle.RunIdle(count)),
    Measure("changed-position-same-home", warmups, iterations, operations,
        count => same.RunSameHome(count)),
    Measure("changed-position-cross-chunk", warmups, iterations, operations,
        count => crossing.RunCrossChunk(count)),
    Measure("create-insert-destroy-remove-pair", warmups, iterations, operations / 4,
        count => lifecycle.RunLifecyclePairs(count)),
    Measure("full-rebuild-4096", warmups, iterations, rebuilds,
        count => rebuild.RunRebuilds(count)),
    Measure("chunk-member-copy-1024", warmups, iterations, operations / 16,
        count => iteration.RunMemberCopies(count)),
};

WorldSpatialIndexSnapshot index = rebuild.State.Index.GetSnapshot();
var report = new WorldSpatialBenchmarkReport(
    "terrain-t0.1-world-spatial-index-v1",
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
    rebuilds,
    new(index.EntityCapacity, index.ChunkCapacity, index.ReservedBytes,
        Marshal.SizeOf<WorldPositionComponent>(), UltraSimGameComposition.SpatialLifecycleRecordCapacity),
    scenarios);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
foreach (WorldSpatialScenarioResult result in scenarios)
    Console.WriteLine($"{result.Name}: mean {result.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p50 {result.P50NanosecondsPerOperation:F3}, p95 {result.P95NanosecondsPerOperation:F3}, " +
        $"max {result.MaximumNanosecondsPerOperation:F3}, allocation {result.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static WorldSpatialScenarioResult Measure(
    string name,
    int warmups,
    int iterations,
    int operations,
    Func<int, ulong> run)
{
    ulong checksum = 0;
    for (int index = 0; index < warmups; index++) checksum = unchecked(checksum + run(operations));
    var elapsed = new double[iterations];
    var allocated = new long[iterations];
    for (int index = 0; index < iterations; index++)
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
    return new WorldSpatialScenarioResult(
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
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..],
        out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class BenchFixture : IDisposable
{
    private readonly Entity[] _entities;
    private readonly Entity[] _members;
    private readonly SpatialChunkKey _sharedKey = new(GameWorldState.DefaultSpace, 0, 0);
    private Entity _single;

    private BenchFixture(int capacity, int population, bool distinctChunks)
    {
        int lifecycleCapacity = Math.Max(64, checked(capacity * 2));
        World = new EcsWorld(
            GameSchema.CreateSchema(),
            new EcsWorldOptions(
                new EntityCapacity(capacity),
                maximumArchetypes: 2,
                maximumPreparedPlans: 8,
                lifecycleRecordCapacity: lifecycleCapacity));
        State = new GameWorldState(World);
        Span<ComponentId> signature = stackalloc ComponentId[1]
        {
            GameSchema.GetComponentId<WorldPositionComponent>(),
        };
        SpatialArchetype = World.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            maximumChunks: Math.Max(1, (capacity + 511) / 512));
        World.Seal();
        World.Initialize();
        World.BeginRun();
        State.Initialize(World);
        _entities = new Entity[population];
        _members = new Entity[Math.Max(1, population)];
        for (int index = 0; index < population; index++)
        {
            if (!World.TryCreate(SpatialArchetype, out Entity entity))
                throw new InvalidOperationException("Benchmark population exceeded archetype storage.");
            int chunk = distinctChunks ? index : 0;
            if (!State.TrySetPosition(World, entity, new WorldPosition(chunk, 0, 0, 0, index)))
                throw new InvalidOperationException("Benchmark position initialization failed.");
            _entities[index] = entity;
        }
        if (population > 0)
        {
            _single = _entities[0];
            State.CommitAfterEcsStep(World);
        }
    }

    internal EcsWorld World { get; }
    internal GameWorldState State { get; }
    internal ArchetypeHandle SpatialArchetype { get; }

    internal static BenchFixture Empty(int capacity) => new(capacity, 0, false);
    internal static BenchFixture OneEntity() => new(16, 1, false);
    internal static BenchFixture Populated(int population, bool distinctChunks) =>
        new(population + 8, population, distinctChunks);

    internal ulong RunIdle(int operations)
    {
        for (int index = 0; index < operations; index++) State.CommitAfterEcsStep(World);
        return unchecked((ulong)State.GetSnapshot().CommitCount);
    }

    internal ulong RunSameHome(int operations)
    {
        for (int index = 0; index < operations; index++)
        {
            if (!State.TrySetPosition(World, _single,
                    new WorldPosition(0, 0, checked((ushort)(index & 15_999)), 1, index)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
        }
        return unchecked((ulong)State.Index.GetSnapshot().SameChunkObservations);
    }

    internal ulong RunCrossChunk(int operations)
    {
        for (int index = 0; index < operations; index++)
        {
            int chunk = index & 1;
            if (!State.TrySetPosition(World, _single, new WorldPosition(chunk, -chunk, 0, 0, index)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
        }
        return unchecked((ulong)State.Index.GetSnapshot().CrossChunkMigrations);
    }

    internal ulong RunLifecyclePairs(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!World.TryCreate(SpatialArchetype, out Entity entity) ||
                !State.TrySetPosition(World, entity, new WorldPosition(index, 0, 0, 0, 0)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
            checksum = unchecked(checksum + entity.Generation);
            if (!World.TryDestroy(entity)) throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
        }
        return checksum;
    }

    internal ulong RunRebuilds(int operations)
    {
        for (int index = 0; index < operations; index++) State.Rebuild(World);
        return unchecked((ulong)State.GetSnapshot().FullRebuildCount);
    }

    internal ulong RunMemberCopies(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
            checksum = unchecked(checksum + (uint)State.Index.CopyMembers(_sharedKey, _members));
        return checksum;
    }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}

sealed record WorldSpatialBenchmarkReport(
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
    int HotOperationsPerIteration,
    int RebuildsPerIteration,
    WorldSpatialProfileEvidence Profile,
    IReadOnlyList<WorldSpatialScenarioResult> Scenarios);

sealed record WorldSpatialProfileEvidence(
    int EntityCapacity,
    int ChunkCapacity,
    long IndexReservedBytes,
    int WorldPositionComponentBytes,
    int DefaultLifecycleCapacity);

sealed record WorldSpatialScenarioResult(
    string Name,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);
