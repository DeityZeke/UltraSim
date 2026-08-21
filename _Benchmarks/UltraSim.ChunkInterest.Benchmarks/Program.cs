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
int queryOperations = ReadInt(args, "--query-operations=", 100_000);
int streamingOperations = ReadInt(args, "--streaming-operations=", 5_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/terrain-t0.7-chunk-interest-local.json"));
if (warmups < 1 || samples < 5 || queryOperations < 1 || streamingOperations < 1)
    throw new ArgumentOutOfRangeException(nameof(args));

var query = new InterestFixture();
using var noChange = new StreamingFixture(historyCapacity: 256);
using var delta = new StreamingFixture(historyCapacity: 256);
using var fallback = new StreamingFixture(historyCapacity: 2);

InterestTiming[] timings =
[
    Measure("exact-query-no-previous", warmups, samples, queryOperations, query.RunFresh),
    Measure("exact-query-retained-hysteresis", warmups, samples, queryOperations, query.RunRetained),
    Measure("one-millimeter-boundary-oscillation", warmups, samples, queryOperations,
        query.RunOneMillimeterOscillation),
    Measure("canonical-chunk-boundary-crossing", warmups, samples, queryOperations,
        query.RunChunkBoundaryCrossing),
    Measure("local-streaming-no-change", warmups, samples, streamingOperations, noChange.RunNoChange),
    Measure("local-streaming-one-public-update", warmups, samples, streamingOperations, delta.RunOneDelta),
    Measure("local-streaming-history-fallback", warmups, samples, Math.Max(1, streamingOperations / 8),
        fallback.RunHistoryFallback),
];
if (timings.Any(timing => timing.MaximumAllocatedBytes != 0))
    throw new InvalidOperationException("A warmed T0.7 path allocated: " +
        string.Join(", ", timings.Select(timing => $"{timing.Name}={timing.MaximumAllocatedBytes}")));

InterestShapeEvidence shapes = query.CaptureShapes();
LocalChunkStreamingSnapshot local = noChange.Oracle.GetSnapshot();
LocalChunkStreamingSnapshot deltaCounters = delta.Oracle.GetSnapshot();
LocalChunkStreamingSnapshot fallbackCounters = fallback.Oracle.GetSnapshot();
ChunkPublicationStoreSnapshot publication = noChange.State.Publications.GetSnapshot();
var report = new InterestReport(
    "terrain-t0.7-chunk-interest-local-oracle-v1",
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
    queryOperations,
    streamingOperations,
    new InterestProfileEvidence(
        "synthetic exact circular chunk-interest and OfflineDirect public-chunk consumer; not Network N1, a client resident renderer, or production population evidence",
        GameWorldSpatialProfile.WorldChunkEdgeUnits,
        GameChunkInterestProfile.ImmediateEnterRadiusMillimeters,
        GameChunkInterestProfile.ImmediateLeaveRadiusMillimeters,
        GameChunkInterestProfile.NearEnterRadiusMillimeters,
        GameChunkInterestProfile.NearLeaveRadiusMillimeters,
        GameChunkInterestProfile.FarEnterRadiusMillimeters,
        GameChunkInterestProfile.FarLeaveRadiusMillimeters,
        LocalChunkStreamingCadence.EvidenceProfile.ImmediateSteps,
        LocalChunkStreamingCadence.EvidenceProfile.NearSteps,
        LocalChunkStreamingCadence.EvidenceProfile.FarSteps,
        noChange.Population,
        noChange.Oracle.GetSnapshot().SubscriptionCount,
        local.ResidentRecordCount,
        local.ReservedBytes,
        publication.ReservedBytes,
        Marshal.SizeOf<ChunkInterestEntry>(),
        Marshal.SizeOf<ClientSemanticChunkSlot>(),
        Marshal.SizeOf<ChunkPublicationCursor>()),
    shapes,
    new StreamingBehaviorEvidence(
        CaptureCounters("no-change", local),
        CaptureCounters("one-public-update", deltaCounters),
        CaptureCounters("history-fallback", fallbackCounters)),
    timings);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
Console.WriteLine($"interest_counts: middle={shapes.MiddleTotal}/{shapes.MiddleImmediate}R0, " +
    $"boundary={shapes.BoundaryTotal}/{shapes.BoundaryImmediate}R0, " +
    $"corner={shapes.CornerTotal}/{shapes.CornerImmediate}R0");
Console.WriteLine($"interest_tiers_middle: immediate={shapes.MiddleImmediate}, near={shapes.MiddleNear}, " +
    $"far={shapes.MiddleFar}");
Console.WriteLine($"local: subscriptions={local.SubscriptionCount}, resident_records={local.ResidentRecordCount}, " +
    $"oracle_reserved={local.ReservedBytes}, publication_reserved={publication.ReservedBytes}");
foreach (InterestTiming timing in timings)
    Console.WriteLine($"{timing.Name}: mean={timing.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95={timing.P95NanosecondsPerOperation:F3}, max={timing.MaximumNanosecondsPerOperation:F3}, " +
        $"allocation={timing.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static StreamingFixtureCounters CaptureCounters(string name, in LocalChunkStreamingSnapshot snapshot) => new(
    name,
    snapshot.Synchronizations,
    snapshot.SnapshotApplications,
    snapshot.ChunkAbsentApplications,
    snapshot.DeltaApplications,
    snapshot.NoChangeQueries,
    snapshot.SnapshotFallbacks,
    snapshot.CadenceDeferrals);

static InterestTiming Measure(
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
    return new InterestTiming(name, operations, elapsed.Average(), elapsed[PercentileIndex(samples, .50)],
        elapsed[PercentileIndex(samples, .95)], elapsed[^1], allocated.Max(), checksum);
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);
static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class InterestFixture
{
    private readonly ChunkInterestEntry[] _initial = new ChunkInterestEntry[1_024];
    private ChunkInterestEntry[] _current = new ChunkInterestEntry[1_024];
    private ChunkInterestEntry[] _next = new ChunkInterestEntry[1_024];
    private int _initialCount;
    private int _currentCount;
    private bool _side;
    private readonly SpatialSpaceId _space = GameWorldState.DefaultSpace;

    internal InterestFixture()
    {
        WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
        RequireSuccess(anchor, [], _initial, out _initialCount);
        Array.Copy(_initial, _current, _initialCount);
        _currentCount = _initialCount;
    }

    internal ulong RunFresh(int operations)
    {
        ulong checksum = 0;
        WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
        for (int index = 0; index < operations; index++)
        {
            RequireSuccess(anchor, [], _next, out int count);
            checksum = unchecked(checksum + (uint)count);
        }
        return checksum;
    }

    internal ulong RunRetained(int operations)
    {
        ulong checksum = 0;
        WorldPosition anchor = new(0, 0, 8_000, 8_000, 0);
        for (int index = 0; index < operations; index++)
        {
            RequireSuccess(anchor, _initial.AsSpan(0, _initialCount), _next, out int count);
            checksum = unchecked(checksum + (uint)count);
        }
        return checksum;
    }

    internal ulong RunOneMillimeterOscillation(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            WorldPosition anchor = _side
                ? new WorldPosition(0, 0, 10_999, 8_000, 0)
                : new WorldPosition(0, 0, 11_000, 8_000, 0);
            RequireSuccess(anchor, _current.AsSpan(0, _currentCount), _next, out int count);
            (_current, _next) = (_next, _current);
            _currentCount = count;
            _side = !_side;
            checksum = unchecked(checksum + (uint)count);
        }
        return checksum;
    }

    internal ulong RunChunkBoundaryCrossing(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            WorldPosition anchor = _side
                ? new WorldPosition(1, 0, 0, 8_000, 0)
                : new WorldPosition(0, 0, 15_999, 8_000, 0);
            RequireSuccess(anchor, _current.AsSpan(0, _currentCount), _next, out int count);
            (_current, _next) = (_next, _current);
            _currentCount = count;
            _side = !_side;
            checksum = unchecked(checksum + (uint)count);
        }
        return checksum;
    }

    internal InterestShapeEvidence CaptureShapes()
    {
        int middle = QueryShape(new WorldPosition(0, 0, 8_000, 8_000, 0),
            out int middleImmediate, out int middleNear, out int middleFar);
        int boundary = QueryShape(new WorldPosition(0, 0, 12_000, 8_000, 0),
            out int boundaryImmediate, out int boundaryNear, out int boundaryFar);
        int corner = QueryShape(new WorldPosition(0, 0, 15_999, 15_999, 0),
            out int cornerImmediate, out int cornerNear, out int cornerFar);
        return new InterestShapeEvidence(
            middle, middleImmediate, middleNear, middleFar,
            boundary, boundaryImmediate, boundaryNear, boundaryFar,
            corner, cornerImmediate, cornerNear, cornerFar);
    }

    private int QueryShape(in WorldPosition anchor, out int immediate, out int near, out int far)
    {
        RequireSuccess(anchor, [], _next, out int count);
        immediate = 0;
        near = 0;
        far = 0;
        for (int index = 0; index < count; index++)
        {
            switch (_next[index].Tier)
            {
                case ChunkInterestTier.Immediate: immediate++; break;
                case ChunkInterestTier.Near: near++; break;
                case ChunkInterestTier.Far: far++; break;
                default: throw new InvalidOperationException();
            }
        }
        return count;
    }

    private void RequireSuccess(
        in WorldPosition anchor,
        ReadOnlySpan<ChunkInterestEntry> previous,
        Span<ChunkInterestEntry> destination,
        out int count)
    {
        if (ChunkInterestQuery.TryQuery(_space, anchor, GameChunkInterestProfile.Default, previous, destination,
                out count, out int required) != ChunkInterestQueryStatus.Success || count != required)
            throw new InvalidOperationException("Interest benchmark query failed.");
    }
}

sealed class StreamingFixture : IDisposable
{
    private readonly Entity _entity;
    private ushort _orientation;
    private ulong _step;

    internal StreamingFixture(int historyCapacity)
    {
        const int capacity = 128;
        Population = 64;
        World = new EcsWorld(
            GameSchema.CreateSchema(),
            new EcsWorldOptions(new EntityCapacity(capacity), 2, 8, lifecycleRecordCapacity: capacity * 2));
        State = new GameWorldState(World, publicationLimits: new ChunkPublicationLimits(
            capacity, capacity * 2, 8, historyCapacity));
        Position = World.PrepareLookup<WorldPositionComponent>();
        Publication = World.PrepareLookup<WorldPublicComponent>();
        Span<ComponentId> signature = stackalloc ComponentId[2]
        {
            GameSchema.GetComponentId<WorldPositionComponent>(),
            GameSchema.GetComponentId<WorldPublicComponent>(),
        };
        signature.Sort();
        ArchetypeHandle archetype = World.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, 1);
        World.Seal();
        World.Initialize();
        World.BeginRun();
        var descriptor = new WorldPublicDescriptor(1, WorldYaw.Zero, WorldPublicStateFlags.Visible);
        Entity first = default;
        for (int index = 0; index < Population; index++)
        {
            if (!World.TryCreate(archetype, out Entity entity)) throw new InvalidOperationException();
            if (index == 0) first = entity;
            int chunk = index % 8;
            if (!World.TryWrite(entity, Position, new WorldPositionComponent(
                    new WorldPosition(chunk, 0, checked((ushort)(index * 97)), 8_000, index))) ||
                !World.TryWrite(entity, Publication, new WorldPublicComponent(descriptor with
                {
                    PrefabId = checked((uint)index + 1),
                })))
                throw new InvalidOperationException();
        }
        _entity = first;
        State.Initialize(World);
        Oracle = new LocalChunkStreamingOracle(
            new LocalChunkStreamingLimits(1_024, capacity),
            GameChunkInterestProfile.Default,
            LocalChunkStreamingCadence.EvidenceProfile);
        if (Oracle.Synchronize(State.Publications, GameWorldState.DefaultSpace,
                new WorldPosition(0, 0, 8_000, 8_000, 0), _step++, out _) !=
            LocalChunkStreamingStatus.Synchronized)
            throw new InvalidOperationException();
    }

    internal int Population { get; }
    internal EcsWorld World { get; }
    internal GameWorldState State { get; }
    internal LocalChunkStreamingOracle Oracle { get; }
    internal ComponentLookup<WorldPositionComponent> Position { get; }
    internal ComponentLookup<WorldPublicComponent> Publication { get; }

    internal ulong RunNoChange(int operations)
    {
        for (int index = 0; index < operations; index++) Synchronize();
        LocalChunkStreamingSnapshot snapshot = Oracle.GetSnapshot();
        return unchecked(snapshot.NoChangeQueries + snapshot.CadenceDeferrals);
    }

    internal ulong RunOneDelta(int operations)
    {
        for (int index = 0; index < operations; index++)
        {
            _orientation++;
            var descriptor = new WorldPublicDescriptor(1, new WorldYaw(_orientation),
                WorldPublicStateFlags.Visible);
            if (!World.TryWrite(_entity, Publication, new WorldPublicComponent(descriptor)))
                throw new InvalidOperationException();
            State.CommitAfterEcsStep(World);
            Synchronize();
        }
        return Oracle.GetSnapshot().DeltaApplications;
    }

    internal ulong RunHistoryFallback(int operations)
    {
        for (int operation = 0; operation < operations; operation++)
        {
            for (int dirty = 0; dirty < 3; dirty++)
            {
                _orientation++;
                var descriptor = new WorldPublicDescriptor(1, new WorldYaw(_orientation),
                    WorldPublicStateFlags.Visible);
                if (!World.TryWrite(_entity, Publication, new WorldPublicComponent(descriptor)))
                    throw new InvalidOperationException();
                State.CommitAfterEcsStep(World);
            }
            Synchronize();
        }
        return Oracle.GetSnapshot().SnapshotFallbacks;
    }

    private void Synchronize()
    {
        if (Oracle.Synchronize(State.Publications, GameWorldState.DefaultSpace,
                new WorldPosition(0, 0, 8_000, 8_000, 0), _step++, out _) !=
            LocalChunkStreamingStatus.Synchronized)
            throw new InvalidOperationException();
    }

    public void Dispose()
    {
        State.Dispose();
        World.Dispose();
    }
}

readonly record struct InterestTiming(
    string Name,
    int Operations,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);

readonly record struct InterestShapeEvidence(
    int MiddleTotal,
    int MiddleImmediate,
    int MiddleNear,
    int MiddleFar,
    int BoundaryTotal,
    int BoundaryImmediate,
    int BoundaryNear,
    int BoundaryFar,
    int CornerTotal,
    int CornerImmediate,
    int CornerNear,
    int CornerFar);

readonly record struct StreamingFixtureCounters(
    string Name,
    ulong Synchronizations,
    ulong SnapshotApplications,
    ulong ChunkAbsentApplications,
    ulong DeltaApplications,
    ulong NoChangeQueries,
    ulong SnapshotFallbacks,
    ulong CadenceDeferrals);

readonly record struct StreamingBehaviorEvidence(
    StreamingFixtureCounters NoChange,
    StreamingFixtureCounters OnePublicUpdate,
    StreamingFixtureCounters HistoryFallback);

readonly record struct InterestProfileEvidence(
    string Workload,
    int ChunkEdgeMillimeters,
    int ImmediateEnterRadiusMillimeters,
    int ImmediateLeaveRadiusMillimeters,
    int NearEnterRadiusMillimeters,
    int NearLeaveRadiusMillimeters,
    int FarEnterRadiusMillimeters,
    int FarLeaveRadiusMillimeters,
    uint ImmediateCadenceSteps,
    uint NearCadenceSteps,
    uint FarCadenceSteps,
    int PublicPopulation,
    int SubscriptionCount,
    int ResidentRecordCount,
    long OracleReservedBytes,
    long PublicationReservedBytes,
    int InterestEntryBytes,
    int ClientSemanticChunkSlotBytes,
    int PublicationCursorBytes);

readonly record struct InterestReport(
    string WorkloadVersion,
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
    int QueryOperations,
    int StreamingOperations,
    InterestProfileEvidence Profile,
    InterestShapeEvidence Shapes,
    StreamingBehaviorEvidence Streaming,
    IReadOnlyList<InterestTiming> Timings);
