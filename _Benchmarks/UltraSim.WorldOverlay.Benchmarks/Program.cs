using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Framework.Persistence;
using UltraSim.Spatial;

int warmups = ReadInt(args, "--warmups=", 3);
int samples = ReadInt(args, "--samples=", 15);
int reads = ReadInt(args, "--reads=", 1_000_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.9-world-overlay-hardening-local.json"));
if (warmups < 1 || samples < 5 || reads < 1) throw new ArgumentOutOfRangeException(nameof(args));

using var fixture = new OverlayFixture(gridEdge: 16, changedEdge: 8);
using var smallBaseScaling = new OverlayFixture(gridEdge: 16, changedEdge: 1);
using var largeBaseScaling = new OverlayFixture(gridEdge: 64, changedEdge: 1);
Timing[] timings =
[
    Measure("build-64-chunk-overlay", warmups, samples, 1, fixture.BuildOverlay),
    Measure("build-1-chunk-overlay-256-base", warmups, samples, 1, smallBaseScaling.BuildOverlay),
    Measure("build-1-chunk-overlay-4096-base", warmups, samples, 1, largeBaseScaling.BuildOverlay),
    Measure("encode-worldview-artifact", warmups, samples, 1, fixture.EncodeArtifact),
    Measure("prepare-resolve-validate-worldview", warmups, samples, 1, fixture.PrepareArtifact),
    Measure("overlay-first-terrain-read", warmups, samples, reads, fixture.ReadPreparedWorld),
];
if (timings[^1].MaximumAllocatedBytes != 0)
    throw new InvalidOperationException("The warmed overlay-first terrain read allocated.");

var report = new
{
    schema = "terrain-t0.9-world-overlay-hardening-v2",
    captured_at_local = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
    runtime = Environment.Version.ToString(),
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount,
    server_gc = GCSettings.IsServerGC,
    stopwatch_frequency = Stopwatch.Frequency,
    scope = "synthetic cold prepared-world overlay/persistence mechanics; not terrain editing, N1 transfer, authored content, or tick-path evidence",
    fixture = new
    {
        base_chunks = fixture.BaseChunkCount,
        changed_chunks = fixture.ChangedChunkCount,
        statics_per_changed_chunk = 1,
        overlay_revision = fixture.Overlay.Revision.Value,
        overlay_reserved_bytes = fixture.Overlay.ReservedBytes,
        worldview_artifact_bytes = fixture.Artifact.Length,
        exact_pack_fingerprint = fixture.Bound.Fingerprint.ToString(),
        replacement_hashes_valid = fixture.HashCount == fixture.ChangedChunkCount,
        immutable_structural_identity_index_bytes = fixture.Bound.StructuralIdentityIndexReservedBytes,
        scaling_base_chunks = new[] { smallBaseScaling.BaseChunkCount, largeBaseScaling.BaseChunkCount },
        scaling_changed_chunks = 1,
        scaling_identity_index_bytes = new[]
        {
            smallBaseScaling.Bound.StructuralIdentityIndexReservedBytes,
            largeBaseScaling.Bound.StructuralIdentityIndexReservedBytes,
        },
    },
    acceptance = new
    {
        immutable_pack_bytes_saved = false,
        exact_pack_resolution_required = true,
        terrain_and_static_replacements_restored = true,
        warmed_overlay_first_reads_allocate_zero = true,
        ordinary_identity_validation_scales_with_retained_replacements_not_base_world = true,
        complete_multi_root_replacement_remains_an_explicit_global_transaction = true,
    },
    timings,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
foreach (Timing timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F2} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F2}, max={timing.MaximumNanosecondsPerOperation:F2}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine($"overlay={fixture.Overlay.ReservedBytes} B, artifact={fixture.Artifact.Length} B, " +
    $"pack={fixture.Bound.Fingerprint}");
Console.WriteLine(Path.GetFullPath(output));

static Timing Measure(string name, int warmups, int samples, int operations, Func<int, ulong> run)
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
    return new Timing(name, operations, elapsed.Average(), elapsed[PercentileIndex(samples, .50)],
        elapsed[PercentileIndex(samples, .95)], elapsed[^1], allocated.Max(), checksum);
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class OverlayFixture : IDisposable
{
    private static readonly WorldContentFingerprint Content =
        new(0x1900_0000_0000_0001, 0x1900_0000_0000_0002);
    private static readonly WorldContentBinding Binding = new(Content, [1, 2], [1U]);
    private static readonly PersistentWorldId WorldId = new(0x90_0099);
    private readonly EcsWorld _world;
    private readonly GameWorldState _state;
    private readonly WorldSaveSetParticipant _restoreParticipant;
    private readonly WorldPosition[] _reads;

    internal OverlayFixture(int gridEdge, int changedEdge)
    {
        GridEdge = gridEdge;
        BaseChunkCount = checked(gridEdge * gridEdge);
        ChangedChunkCount = checked(changedEdge * changedEdge);
        int maximumPackBytes = Math.Max(8 * 1024 * 1024,
            checked(BaseChunkCount * 5_000 + 1024 * 1024));
        var packBuilder = new WorldPackBuilder(
            GameWorldState.DefaultSpace,
            Content,
            new WorldPackLimits(maximumPackBytes, BaseChunkCount, 8,
                maximumFrozenStatics: BaseChunkCount));
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        Array.Fill(materials, (ushort)1);
        var absolute = new int[TerrainChunkFormat.HeightVertexCount];
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        if (!TerrainHeightEncoding.TryEncodePlane(absolute, offsets,
                out TerrainHeightPlaneBase heightBase, out _, out _))
            throw new InvalidOperationException("Flat terrain did not encode.");
        for (int z = 0; z < gridEdge; z++)
            for (int x = 0; x < gridEdge; x++)
            {
                if (!packBuilder.TryAddBaseTerrain(
                        new SpatialChunkKey(GameWorldState.DefaultSpace, x, z),
                        materials, heightBase, offsets, out _))
                    throw new InvalidOperationException("Base terrain admission failed.");
                var position = new WorldPosition(x, z, 2_000, 2_000, 0);
                if (!packBuilder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                        new WorldStaticId(checked((ulong)(z * gridEdge + x + 1))),
                        new WorldStaticDefinitionId(1), position, WorldYaw.Zero, 0,
                        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                        new WorldStaticBounds(
                            position.GlobalXMillimeters - 100, -100,
                            position.GlobalZMillimeters - 100,
                            position.GlobalXMillimeters + 100, 100,
                            position.GlobalZMillimeters + 100)), out _))
                    throw new InvalidOperationException("Base static admission failed.");
            }
        if (!packBuilder.TryBuild(out WorldPackArtifact? artifact, out _) || artifact is null ||
            !artifact.Open().TryBind(Binding, out BoundWorldPack? bound, out _) || bound is null)
            throw new InvalidOperationException("Benchmark base world did not bind.");
        Bound = bound;
        _world = new EcsWorld(GameSchema.CreateSchema(), UltraSimGameComposition.CreateWorldOptions(
            64, new PersistenceIdentityOptions(WorldId)));
        _state = new GameWorldState(_world, bound);
        Overlay = CreateOverlay();
        if (!_state.TryPublishOverlay(Overlay)) throw new InvalidOperationException("Overlay did not publish.");
        HashCount = 0;
        for (int z = 0; z < changedEdge; z++)
            for (int x = 0; x < changedEdge; x++)
                if (Overlay.TryGetChunkRevision(
                        new SpatialChunkKey(GameWorldState.DefaultSpace, x, z), out _, out _))
                    HashCount++;
        Artifact = Encode();
        _restoreParticipant = new WorldSaveSetParticipant(
            fingerprint => fingerprint == Bound.Fingerprint ? Bound : null);
        _reads = new WorldPosition[BaseChunkCount];
        int read = 0;
        for (int z = 0; z < gridEdge; z++)
            for (int x = 0; x < gridEdge; x++)
                _reads[read++] = new WorldPosition(x, z, 100, 100, 0);
    }

    internal int BaseChunkCount { get; }
    internal int GridEdge { get; }
    internal int ChangedChunkCount { get; }
    internal int HashCount { get; }
    internal BoundWorldPack Bound { get; }
    internal WorldMutableOverlay Overlay { get; }
    internal byte[] Artifact { get; }

    internal ulong BuildOverlay(int operations)
    {
        ulong checksum = 0;
        for (int iteration = 0; iteration < operations; iteration++)
        {
            WorldMutableOverlay overlay = CreateOverlay();
            checksum = unchecked(checksum + (ulong)overlay.ReservedBytes + overlay.Revision.Value);
        }
        return checksum;
    }

    internal ulong EncodeArtifact(int operations)
    {
        ulong checksum = 0;
        for (int iteration = 0; iteration < operations; iteration++)
        {
            byte[] artifact = Encode();
            checksum = unchecked(checksum + (uint)artifact.Length + artifact[0]);
        }
        return checksum;
    }

    internal ulong PrepareArtifact(int operations)
    {
        ulong checksum = 0;
        for (int iteration = 0; iteration < operations; iteration++)
        {
            using IPreparedLoadParticipant prepared = _restoreParticipant.Prepare(
                Artifact, new SaveSetContext(1, 0));
            prepared.Resolve(EmptyResolver.Instance);
            prepared.Validate();
            var world = (PreparedWorldParticipant)prepared;
            checksum = unchecked(checksum + world.Overlay!.Revision.Value +
                (ulong)world.Overlay.ChangedChunkCount);
        }
        return checksum;
    }

    internal ulong ReadPreparedWorld(int operations)
    {
        WorldPreparedView prepared = _state.PreparedWorld!;
        ulong checksum = 0;
        for (int iteration = 0; iteration < operations; iteration++)
        {
            WorldPosition position = _reads[iteration % _reads.Length];
            if (!prepared.TryResolveBaseTerrainMaterial(position, out ushort material))
                throw new InvalidOperationException("Prepared-world read missed a base chunk.");
            checksum += material;
        }
        return checksum;
    }

    public void Dispose()
    {
        _state.Dispose();
        _world.Dispose();
    }

    private WorldMutableOverlay CreateOverlay()
    {
        int changedEdge = checked((int)Math.Sqrt(ChangedChunkCount));
        var builder = new WorldMutableOverlayBuilder(Bound);
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        for (int z = 0; z < changedEdge; z++)
        {
            for (int x = 0; x < changedEdge; x++)
            {
                var key = new SpatialChunkKey(GameWorldState.DefaultSpace, x, z);
                if (!Bound.TryGetBaseTerrain(key, out WorldTerrainChunkView terrain))
                    throw new InvalidOperationException("Overlay fixture missed terrain.");
                terrain.CopyMaterials(materials);
                for (int index = 0; index < offsets.Length; index++)
                    offsets[index] = terrain.GetHeightOffset(index);
                materials[0] = 2;
                var position = new WorldPosition(x, z, 1_000, 1_000, 0);
                WorldFrozenStaticRecord[] statics =
                [
                    new WorldFrozenStaticRecord(
                        new WorldStaticId(checked((ulong)(z * GridEdge + x + 1))),
                        new WorldStaticDefinitionId(1), position, WorldYaw.Zero, 0,
                        WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                        new WorldStaticBounds(
                            position.GlobalXMillimeters - 100, -100,
                            position.GlobalZMillimeters - 100,
                            position.GlobalXMillimeters + 100, 100,
                            position.GlobalZMillimeters + 100)),
                ];
                if (!builder.TryReplaceTerrain(key, materials, terrain.HeightBase, offsets) ||
                    !builder.TryReplaceFrozenStatics(key, statics))
                    throw new InvalidOperationException("Overlay fixture replacement failed.");
            }
        }
        if (!builder.TryBuild(out WorldMutableOverlay? overlay) || overlay is null)
            throw new InvalidOperationException("Overlay fixture did not build.");
        return overlay;
    }

    private byte[] Encode()
    {
        var participant = new WorldSaveSetParticipant(_state, WorldId);
        using ICapturedSaveSetParticipant captured = participant.Capture(new SaveSetContext(1, 0));
        using var stream = new MemoryStream();
        captured.WriteArtifact(stream);
        return stream.ToArray();
    }

    private sealed class EmptyResolver : IExternalPersistentEntityResolver
    {
        internal static readonly EmptyResolver Instance = new();
        public bool TryResolve(PersistentEntityKey key, out object? value) { value = null; return false; }
    }
}

readonly record struct Timing(
    string Name,
    int OperationsPerSample,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);
