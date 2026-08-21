using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 5);
int samples = ReadInt(args, "--samples=", 21);
int operations = ReadInt(args, "--operations=", 10_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.8-client-semantic-cache.json"));
if (warmups < 1 || samples < 5 || operations < 1) throw new ArgumentOutOfRangeException(nameof(args));

BoundWorldPack world = Fixture.CreateWorld();
var resident = new ResidentFixture(world);
var lifecycle = new LifecycleFixture(world);
var delta = new DeltaFixture(world);
DeltaScaleFixture[] scale =
[
    new(world, 1, 1),
    new(world, 32, 1),
    new(world, 128, 1),
    new(world, 512, 1),
    new(world, 512, 2),
];
Timing[] timings =
[
    Measure("resident-world-hash-32", warmups, samples, operations, resident.Hash),
    Measure("borrow-resident-pages-32", warmups, samples, operations, resident.Borrow),
    Measure("request-stage-publish-retire", warmups, samples, Math.Max(1, operations / 10), lifecycle.Run),
    Measure("one-public-delta", warmups, samples, operations, delta.Run),
    Measure("one-public-delta-resident-1", warmups, samples, operations, scale[0].Run),
    Measure("one-public-delta-resident-32", warmups, samples, operations, scale[1].Run),
    Measure("one-public-delta-resident-128", warmups, samples, operations, scale[2].Run),
    Measure("one-public-delta-resident-512", warmups, samples, operations, scale[3].Run),
    Measure("one-public-delta-resident-512-near-full-knowledge", warmups, samples, operations, scale[4].Run),
];
if (timings.Any(value => value.MaximumAllocatedBytes != 0))
    throw new InvalidOperationException("A warmed T0.8 semantic-cache path allocated.");

var report = new Report(
    "terrain-t0.8-client-semantic-cache-hardening-v2",
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
    operations,
    new Shape(
        "synthetic production-client semantic residency; no Network N1 and no Godot resource cost",
        world.ChunkCount,
        resident.Cache.GetSnapshot().ResidentChunks,
        resident.Cache.GetSnapshot().ReservedBytes,
        new ClientWorldSemanticCache(world).GetSnapshot().ReservedBytes,
        Marshal.SizeOf<ClientSemanticChunkHandle>(),
        Marshal.SizeOf<ClientSemanticChunkInfo>()),
    timings);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
foreach (Timing timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F3}, max={timing.MaximumNanosecondsPerOperation:F3}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine($"resident_reserved={resident.Cache.GetSnapshot().ReservedBytes} " +
    $"default_reserved={new ClientWorldSemanticCache(world).GetSnapshot().ReservedBytes}");
Console.WriteLine(Path.GetFullPath(output));

static Timing Measure(string name, int warmups, int samples, int operations, Func<int, ulong> body)
{
    ulong checksum = 0;
    for (int index = 0; index < warmups; index++) checksum = unchecked(checksum + body(operations));
    var elapsed = new double[samples];
    var allocated = new long[samples];
    for (int index = 0; index < samples; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        checksum = unchecked(checksum + body(operations));
        long stop = Stopwatch.GetTimestamp();
        allocated[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        elapsed[index] = (stop - start) * 1_000_000_000d / Stopwatch.Frequency / operations;
    }
    Array.Sort(elapsed);
    return new Timing(name, operations, elapsed.Average(), elapsed[Percentile(samples, .50)],
        elapsed[Percentile(samples, .95)], elapsed[^1], allocated.Max(), checksum);
}

static int Percentile(int count, double fraction) =>
    Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1);
static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class ResidentFixture
{
    private readonly ClientSemanticChunkHandle[] _handles = new ClientSemanticChunkHandle[32];
    internal ResidentFixture(BoundWorldPack world)
    {
        Cache = new ClientWorldSemanticCache(world, new(32, 4, 4, 4, 128));
        for (int index = 0; index < _handles.Length; index++)
        {
            SpatialChunkKey key = Fixture.Key(index);
            Fixture.Require(Cache.TryRequest(key, out _handles[index], out _));
            Fixture.Require(Cache.TryStageFromBaseWorld(_handles[index], [], default, out _));
            Fixture.Require(Cache.TryPublish(_handles[index], out _));
        }
    }

    internal ClientWorldSemanticCache Cache { get; }

    internal ulong Hash(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            ClientSemanticHash hash = Cache.GetSnapshot().WorldSemanticHash;
            checksum = unchecked(checksum + hash.Low + hash.High);
        }
        return checksum;
    }

    internal ulong Borrow(int operations)
    {
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            for (int index = 0; index < _handles.Length; index++)
            {
                Fixture.Require(Cache.TryGetResidentView(_handles[index], out ClientSemanticChunkView view));
                checksum = unchecked(checksum + view.TerrainMaterials[(operation + index) & 1023] +
                    (uint)view.TerrainAbsoluteHeightsMillimeters[(operation + index) %
                        TerrainChunkFormat.HeightVertexCount]);
            }
        }
        return checksum;
    }
}

sealed class LifecycleFixture
{
    private readonly ClientWorldSemanticCache _cache;
    private int _next;

    internal LifecycleFixture(BoundWorldPack world) =>
        _cache = new ClientWorldSemanticCache(world, new(1, 4, 4, 4, 8));

    internal ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            SpatialChunkKey key = Fixture.Key(_next++ & 63);
            Fixture.Require(_cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            Fixture.Require(_cache.TryStageFromBaseWorld(handle, [], default, out _));
            Fixture.Require(_cache.TryPublish(handle, out _));
            checksum = unchecked(checksum + _cache.GetSnapshot().WorldSemanticHash.Low);
            Fixture.Require(_cache.BeginRetire(handle));
            Fixture.Require(_cache.CompleteRetire(handle));
        }
        return checksum;
    }
}

sealed class DeltaFixture
{
    private readonly ClientWorldSemanticCache _cache;
    private readonly ClientSemanticChunkHandle _handle;
    private readonly PublicationEpoch _epoch;
    private readonly WorldPublicDeltaRecord[] _delta = new WorldPublicDeltaRecord[1];
    private ulong _revision = 1;

    internal DeltaFixture(BoundWorldPack world)
    {
        _cache = new ClientWorldSemanticCache(world, new(1, 4, 4, 4, 8));
        _epoch = PublicationEpoch.Create();
        SpatialChunkKey key = Fixture.Key(0);
        WorldPublicRecord record = CreateRecord(key, 1);
        Fixture.Require(_cache.TryRequest(key, out _handle, out _));
        Fixture.Require(_cache.TryStageFromBaseWorld(_handle, [record],
            new ChunkPublicationCursor(_epoch, key, new ChunkPublicationRevision(1)), out _));
        Fixture.Require(_cache.TryPublish(_handle, out _));
    }

    internal ulong Run(int operations)
    {
        ulong checksum = 0;
        SpatialChunkKey key = Fixture.Key(0);
        for (int index = 0; index < operations; index++)
        {
            ulong revision = ++_revision;
            WorldPublicRecord record = CreateRecord(key, revision);
            _delta[0] = new WorldPublicDeltaRecord(
                WorldPublicOperation.Present, WorldPublicFieldMask.Transform,
                record.Id, revision, record);
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Delta, key,
                new ChunkPublicationCursor(_epoch, key, new ChunkPublicationRevision(revision)),
                new ChunkPublicationRevision(1), 1, 1);
            Fixture.Require(_cache.TryApplyDelta(_handle, batch, _delta, out _));
            checksum = unchecked(checksum + _cache.GetSnapshot().WorldSemanticHash.Low);
        }
        return checksum;
    }

    private static WorldPublicRecord CreateRecord(SpatialChunkKey key, ulong version) => new(
        new WorldPublicEntityId(1, 1), version, 1,
        new WorldPosition(key.Coord.X, key.Coord.Z,
            checked((ushort)(version % GameWorldSpatialProfile.WorldChunkEdgeUnits)), 500, 750),
        WorldYaw.Zero, WorldPublicStateFlags.Visible,
        new ChunkPublicationRevision(version));
}

sealed class DeltaScaleFixture
{
    private readonly ClientWorldSemanticCache _cache;
    private readonly ClientSemanticChunkHandle _target;
    private readonly SpatialChunkKey _key;
    private readonly PublicationEpoch _epoch;
    private readonly WorldPublicEntityId _identity;
    private readonly uint _prefab;
    private readonly WorldPublicDeltaRecord[] _delta = new WorldPublicDeltaRecord[1];
    private ulong _revision = 1;

    internal DeltaScaleFixture(BoundWorldPack world, int residents, int identitiesPerChunk)
    {
        _cache = new ClientWorldSemanticCache(world,
            new(residents, 4, 0, identitiesPerChunk,
                checked(residents * identitiesPerChunk)));
        _epoch = PublicationEpoch.Create();
        ClientSemanticChunkHandle target = default;
        for (int chunk = 0; chunk < residents; chunk++)
        {
            SpatialChunkKey key = Fixture.Key(chunk);
            var records = new WorldPublicRecord[identitiesPerChunk];
            for (int row = 0; row < records.Length; row++)
            {
                uint index = checked((uint)(chunk * identitiesPerChunk + row + 1));
                records[row] = Record(key, new WorldPublicEntityId(index, 1), 1,
                    checked((uint)(row + 1)));
            }
            Fixture.Require(_cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            Fixture.Require(_cache.TryStageFromBaseWorld(handle, records,
                new ChunkPublicationCursor(_epoch, key, new ChunkPublicationRevision(1)), out _));
            Fixture.Require(_cache.TryPublish(handle, out _));
            if (chunk == residents - 1) target = handle;
        }
        _target = target;
        _key = Fixture.Key(residents - 1);
        _identity = new WorldPublicEntityId(
            checked((uint)(residents * identitiesPerChunk)), 1);
        _prefab = checked((uint)identitiesPerChunk);
    }

    internal ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            ulong revision = ++_revision;
            WorldPublicRecord record = Record(_key, _identity, revision, _prefab);
            _delta[0] = new WorldPublicDeltaRecord(
                WorldPublicOperation.Present, WorldPublicFieldMask.Transform,
                record.Id, record.EntityPublicVersion, record);
            var batch = new ChunkPublicationBatch(
                ChunkPublicationReadStatus.Delta, _key,
                new ChunkPublicationCursor(_epoch, _key, new ChunkPublicationRevision(revision)),
                new ChunkPublicationRevision(1), 1, 1);
            Fixture.Require(_cache.TryApplyDelta(_target, batch, _delta, out _));
            Fixture.Require(_cache.TryGetInfo(_target, out ClientSemanticChunkInfo info));
            checksum = unchecked(checksum + info.SemanticHash.Low);
        }
        return checksum;
    }

    private static WorldPublicRecord Record(SpatialChunkKey key, WorldPublicEntityId id,
        ulong version, uint prefab) => new(
            id, version, prefab,
            new WorldPosition(key.Coord.X, key.Coord.Z,
                checked((ushort)(version % GameWorldSpatialProfile.WorldChunkEdgeUnits)),
                checked((ushort)(id.Index % GameWorldSpatialProfile.WorldChunkEdgeUnits)), 750),
            WorldYaw.Zero, WorldPublicStateFlags.Visible,
            new ChunkPublicationRevision(version));
}

static class Fixture
{
    private static readonly SpatialSpaceId Space = new(808);
    private static readonly WorldContentFingerprint Content = new(0x8080, 0xCACE);

    internal static BoundWorldPack CreateWorld()
    {
        var builder = new WorldPackBuilder(
            Space, Content, new WorldPackLimits(16 * 1024 * 1024, 512, 8, 512, 0, 0));
        for (int index = 0; index < 512; index++)
        {
            ushort[] materials = new ushort[TerrainChunkFormat.MaterialCellCount];
            Array.Fill(materials, checked((ushort)(index % 2 + 1)));
            var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
            Require(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
            Require(builder.TryAddBaseTerrain(Key(index), materials, heightBase, offsets, out _));
            var position = new WorldPosition(index, 0, 1_000, 1_000, 0);
            Require(builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(checked((ulong)index + 1)), new WorldStaticDefinitionId(1),
                position, WorldYaw.Zero, 0, WorldStaticFlags.Renderable,
                new WorldStaticBounds(position.GlobalXMillimeters - 100, -100,
                    position.GlobalZMillimeters - 100, position.GlobalXMillimeters + 100, 100,
                    position.GlobalZMillimeters + 100)), out _));
        }
        Require(builder.TryBuild(out WorldPackArtifact? artifact, out _));
        WorldPack pack = artifact!.Open();
        Require(pack.TryBind(new WorldContentBinding(Content, [1, 2], [1]),
            out BoundWorldPack? bound, out _));
        return bound!;
    }

    internal static SpatialChunkKey Key(int index) => new(Space, index, 0);
    internal static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Benchmark fixture invariant failed.");
    }
}

readonly record struct Timing(
    string Name,
    int Operations,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);

readonly record struct Shape(
    string Scope,
    int WorldChunks,
    int ResidentChunks,
    long ResidentFixtureReservedBytes,
    long DefaultLimitsReservedBytes,
    int HandleBytes,
    int InfoBytes);

readonly record struct Report(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string RuntimeVersion,
    string Framework,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    bool ServerGc,
    long StopwatchFrequency,
    string TieredCompilation,
    string ReadyToRun,
    int Warmups,
    int Samples,
    int Operations,
    Shape Shape,
    Timing[] Timings);
