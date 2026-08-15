using System.Diagnostics;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Network;
using UltraSim.Server.Runtime;

if (args.Contains("--n07", StringComparer.Ordinal))
{
    RunN07(args);
    return;
}

int population = ReadInt(args, "--population", 100_000);
int warmup = ReadInt(args, "--warmup", 4);
int samples = ReadInt(args, "--samples", 12);
string? output = ReadText(args, "--output");
if (population <= 0 || warmup < 1 || samples < 3)
    throw new ArgumentOutOfRangeException(nameof(args));

const string workload = "ultrasim-network-n0.1-canonical-projection-v1";
var composition = new UltraSimGameComposition(
    maximumChunksPerContentArchetype: Math.Max(16, (population + 255) / 256),
    runtimeProfile: GameRuntimeProfile.Networked);
using var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(
    UltraSimGameComposition.CreateWorldOptions(
        population + 64,
        new PersistenceIdentityOptions(new PersistentWorldId(0xA001)),
        GameRuntimeProfile.Networked),
    WorldClockOptions.Default));
runtime.Initialize();
runtime.Start();
using var admission = new AdmissionService(runtime.Game, composition);
Check(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
    (world, generationId, game) =>
    {
        for (int index = 0; index < population; index++)
            if (!world.TryInstantiate(game.Content.Character, out _)) return false;
        return true;
    }, out bool populated) && populated, "Population construction failed.");

var limits = new GameReplicationLimits(
    population + 64,
    8,
    UltraSimGameComposition.NetworkLifecycleRecordCapacity,
    population + UltraSimGameComposition.NetworkLifecycleRecordCapacity,
    UltraSimGameComposition.NetworkLifecycleRecordCapacity,
    64,
    64);
using var projection = new GameProjectionService(runtime.Game, admission, limits);
long baselineStarted = Stopwatch.GetTimestamp();
GameProjectionCaptureResult baseline = projection.RebuildBaseline();
double baselineMicroseconds = Microseconds(Stopwatch.GetTimestamp() - baselineStarted);
Check(baseline.Status == GameProjectionCaptureStatus.BaselineRebuilt, "Baseline rebuild failed.");
var offline = new OfflineDirectGameProjection(
    population + 64,
    maximumParticipants: 8,
    semanticTraceCapacity: checked(population * (warmup + samples + 2) + 16));
Check(offline.ApplyBaseline(projection, baseline.Frame) == OfflineProjectionApplyStatus.Success,
    "Initial OfflineDirect baseline failed.");
var deltas = new GameProjectionDelta[population + UltraSimGameComposition.NetworkLifecycleRecordCapacity];

for (int index = 0; index < warmup; index++)
    RunOne(runtime, projection, offline, deltas, population, null, null, null);
baseline = projection.RebuildBaseline();
Check(baseline.Status == GameProjectionCaptureStatus.BaselineRebuilt, "Post-warmup baseline failed.");
Check(offline.ApplyBaseline(projection, baseline.Frame) == OfflineProjectionApplyStatus.Success,
    "Post-warmup OfflineDirect baseline failed.");

var tickUs = new double[samples];
var captureUs = new double[samples];
var applyUs = new double[samples];
long beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
for (int index = 0; index < samples; index++)
    RunOne(runtime, projection, offline, deltas, population, tickUs, captureUs, applyUs, index);
long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;

const int idleSamples = 64;
var idleCaptureUs = new double[idleSamples];
for (int index = 0; index < idleSamples; index++)
{
    long started = Stopwatch.GetTimestamp();
    GameProjectionCaptureResult idle = projection.CaptureAfterTick();
    idleCaptureUs[index] = Microseconds(Stopwatch.GetTimestamp() - started);
    Check(idle.Succeeded && idle.Frame.DeltaCount == 0, "Idle capture produced unexpected work.");
    Check(offline.ApplyFrame(projection, idle.Frame, ReadOnlySpan<GameProjectionDelta>.Empty) ==
        OfflineProjectionApplyStatus.Success, "Idle OfflineDirect frame failed.");
}

var projected = new GameProjectedEntity[population];
Check(offline.CopyEntities(projected) == population, "OfflineDirect entity count diverged.");
int expectedMarker = warmup + samples;
for (int index = 0; index < projected.Length; index++)
{
    Check(projected[index].HasSimulationState, "Projected entity lost simulation membership.");
    Check(projected[index].SimulationState.TickMarker == expectedMarker,
        "Projected simulation state diverged.");
}
OfflineProjectionSnapshot offlineSnapshot = offline.GetSnapshot();
GameProjectionSnapshot projectionSnapshot = projection.GetSnapshot();

var result = new BenchmarkReport(
    workload,
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    Environment.ProcessorCount,
    population,
    warmup,
    samples,
    baselineMicroseconds,
    Stats.From(tickUs),
    Stats.From(captureUs),
    Stats.From(applyUs),
    Stats.From(idleCaptureUs),
    allocated,
    projectionSnapshot.ReservedBytes,
    offlineSnapshot.ReservedBytes,
    projectionSnapshot.EntityCount,
    offlineSnapshot.EntityCount,
    offlineSnapshot.CanonicalHash,
    offlineSnapshot.SemanticTraceDigest.ToString());
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (!string.IsNullOrWhiteSpace(output))
{
    string path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine);
}
static void RunOne(
    ServerRuntime runtime,
    GameProjectionService projection,
    OfflineDirectGameProjection offline,
    GameProjectionDelta[] deltas,
    int expectedRows,
    double[]? tickUs,
    double[]? captureUs,
    double[]? applyUs,
    int sample = 0)
{
    long started = Stopwatch.GetTimestamp();
    runtime.Tick();
    if (tickUs is not null) tickUs[sample] = Microseconds(Stopwatch.GetTimestamp() - started);
    started = Stopwatch.GetTimestamp();
    GameProjectionCaptureResult capture = projection.CaptureAfterTick();
    if (captureUs is not null) captureUs[sample] = Microseconds(Stopwatch.GetTimestamp() - started);
    Check(capture.Succeeded, "Projection capture failed.");
    int count = projection.CopyLastFrameDeltas(deltas);
    Check(count == expectedRows, "Changed-row capture did not include every scheduled write.");
    started = Stopwatch.GetTimestamp();
    OfflineProjectionApplyStatus applied = offline.ApplyFrame(projection, capture.Frame, deltas.AsSpan(0, count));
    if (applyUs is not null) applyUs[sample] = Microseconds(Stopwatch.GetTimestamp() - started);
    Check(applied == OfflineProjectionApplyStatus.Success, "OfflineDirect frame failed.");
}

static void RunN07(string[] arguments)
{
    int entities = ReadInt(arguments, "--entities", 5_000);
    int recipients = ReadInt(arguments, "--recipients", 16);
    int dirtyPercent = ReadInt(arguments, "--dirty-percent", 10);
    int warmup = ReadInt(arguments, "--warmup", 4);
    int samples = ReadInt(arguments, "--samples", 12);
    int regionEdge = ReadInt(arguments, "--region-edge-mm", 16_000);
    int immediateRadius = ReadInt(arguments, "--immediate-radius-mm", 5_000);
    int nearRadius = ReadInt(arguments, "--near-radius-mm", 96_000);
    int farRadius = ReadInt(arguments, "--far-radius-mm", 192_000);
    int spacing = ReadInt(arguments, "--spacing-mm", 4_000);
    string? output = ReadText(arguments, "--output");
    int immediateLeave = checked(immediateRadius + Math.Max(1, immediateRadius / 10));
    if (entities <= 0 || recipients <= 0 || dirtyPercent is < 0 or > 100 ||
        warmup < 1 || samples < 3 || regionEdge <= 0 || spacing <= 0 || immediateRadius <= 0 ||
        nearRadius < immediateLeave || farRadius < nearRadius)
        throw new ArgumentOutOfRangeException(nameof(arguments));

    var profile = new GameInterestProfile(
        regionEdge,
        immediateRadius,
        immediateLeave,
        nearRadius,
        farRadius,
        NearIntervalMilliseconds: 33,
        FarIntervalMilliseconds: 50,
        ApplicationAckRetryMilliseconds: 100,
        ApplicationAckTimeoutMilliseconds: 2_000);
    int farCells = checked((int)(((long)farRadius + regionEdge - 1L) / regionEdge));
    int edgeWidth = checked(farCells * 2 + 1);
    // One crossing temporarily owns the old envelope plus its incoming strip
    // before the outgoing strip is retired.
    int maximumEdges = checked(edgeWidth * edgeWidth * recipients + edgeWidth);
    var limits = new GameInterestLimits(
        entities,
        recipients,
        MaximumRegions: checked(maximumEdges + entities),
        MaximumRegionInterestEdges: maximumEdges,
        MaximumRecipientEntityRelations: checked(entities * recipients),
        MaximumOutstandingBundles: Math.Max(64, checked(recipients * 4)),
        MaximumRecordsPerBundle: 32);
    var service = new GameInterestService(new StateEpoch(0x707), limits, profile);
    var points = new ReplicationSpatialPoint[entities];
    var versions = new ulong[entities];
    int width = checked((int)Math.Ceiling(Math.Sqrt(entities)));
    int anchorColumns = Math.Max(1, width / 8);

    long setupStarted = Stopwatch.GetTimestamp();
    for (int recipient = 0; recipient < recipients; recipient++)
    {
        Check(service.SetRecipientAnchor(
            new ParticipantId(checked((ulong)recipient + 1)),
            RecipientAnchor(recipient, anchorColumns),
            0) == GameInterestUpdateStatus.Success,
            "Recipient setup exceeded the declared interest bounds.");
    }
    for (int entity = 0; entity < entities; entity++)
    {
        points[entity] = new ReplicationSpatialPoint(
            checked((entity % width) * spacing),
            checked((entity / width) * spacing));
        versions[entity] = 1;
        Check(service.UpsertEntity(
            BenchmarkProjected(entity, marker: 1),
            versions[entity],
            points[entity],
            GameReplicationHorizon.Far,
            0) == GameInterestUpdateStatus.Success,
            "Entity setup exceeded the declared interest bounds.");
    }
    var recordScratch = new GameWorldStateRecord[32];
    DrainInterest(service, nowMilliseconds: 100, recordScratch);
    double setupMicroseconds = Microseconds(Stopwatch.GetTimestamp() - setupStarted);
    GameInterestSnapshot initial = service.GetSnapshot();
    int dirty = dirtyPercent == 0 ? 0 : Math.Max(1, checked(entities * dirtyPercent / 100));

    for (int round = 0; round < warmup; round++)
        _ = RunInterestRound(service, points, versions, dirty, recipients, anchorColumns,
            regionEdge, 20_000 + round * 100, recordScratch);

    var updateUs = new double[samples];
    var deliveryUs = new double[samples];
    var recipientMoveUs = new double[samples];
    var movementDeliveryUs = new double[samples];
    ulong deliveredBundles = 0;
    ulong deliveredBytes = 0;
    long updateBytes = 0;
    long deliveryBytes = 0;
    long recipientMoveBytes = 0;
    long movementDeliveryBytes = 0;
    long beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
    for (int sample = 0; sample < samples; sample++)
    {
        InterestRoundTiming timing = RunInterestRound(
            service, points, versions, dirty, recipients, anchorColumns,
            regionEdge, 30_000 + sample * 100, recordScratch);
        updateUs[sample] = timing.UpdateMicroseconds;
        deliveryUs[sample] = timing.DeliveryMicroseconds;
        recipientMoveUs[sample] = timing.RecipientMoveMicroseconds;
        movementDeliveryUs[sample] = timing.MovementDeliveryMicroseconds;
        deliveredBundles = checked(deliveredBundles + timing.Bundles);
        deliveredBytes = checked(deliveredBytes + timing.BundleBytes);
        updateBytes = checked(updateBytes + timing.UpdateAllocatedBytes);
        deliveryBytes = checked(deliveryBytes + timing.DeliveryAllocatedBytes);
        recipientMoveBytes = checked(recipientMoveBytes + timing.RecipientMoveAllocatedBytes);
        movementDeliveryBytes = checked(movementDeliveryBytes + timing.MovementDeliveryAllocatedBytes);
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;

    const int idleSamples = 64;
    var idleUs = new double[idleSamples];
    for (int sample = 0; sample < idleSamples; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        GameWorldStateBuildStatus status = service.TryBuildNextBundle(
            100_000 + sample, 1_400, recordScratch, out _);
        idleUs[sample] = Microseconds(Stopwatch.GetTimestamp() - started);
        Check(status == GameWorldStateBuildStatus.NoData,
            "Idle interest scheduler found unexpected debt.");
    }

    GameInterestSnapshot final = service.GetSnapshot();
    var report = new N07BenchmarkReport(
        "ultrasim-network-n0.7-maintained-interest-v1",
        DateTimeOffset.UtcNow,
        Environment.Version.ToString(),
        Environment.ProcessorCount,
        entities,
        recipients,
        dirtyPercent,
        warmup,
        samples,
        spacing,
        regionEdge,
        immediateRadius,
        nearRadius,
        farRadius,
        setupMicroseconds,
        Stats.From(updateUs),
        Stats.From(deliveryUs),
        Stats.From(recipientMoveUs),
        Stats.From(movementDeliveryUs),
        Stats.From(idleUs),
        allocated,
        updateBytes,
        deliveryBytes,
        recipientMoveBytes,
        movementDeliveryBytes,
        initial.RelationCount,
        final.RelationCount,
        final.PendingRelationCount,
        final.UniqueSemanticConstructions,
        final.SemanticRecordCopies,
        final.SemanticBytesCopied,
        deliveredBundles,
        deliveredBytes,
        final.RegionInterestEdgeCount,
        final.ReservedBytes,
        "Single-owner maintained graph; complete WorldState records; immediate application ACK; " +
        "one recipient crosses and returns across a region boundary per sample; presentation excluded.");
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (!string.IsNullOrWhiteSpace(output))
    {
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}

static InterestRoundTiming RunInterestRound(
    GameInterestService service,
    ReplicationSpatialPoint[] points,
    ulong[] versions,
    int dirty,
    int recipients,
    int anchorColumns,
    int regionEdge,
    long now,
    GameWorldStateRecord[] scratch)
{
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < dirty; index++)
    {
        versions[index]++;
        ReplicationSpatialPoint point = points[index];
        points[index] = new ReplicationSpatialPoint(
            checked(point.XMillimeters + ((versions[index] & 1) == 0 ? 1 : -1)),
            point.ZMillimeters);
        Check(service.UpsertEntity(
            BenchmarkProjected(index, checked((int)Math.Min(versions[index], int.MaxValue))),
            versions[index], points[index], GameReplicationHorizon.Far, now) ==
            GameInterestUpdateStatus.Success,
            "Canonical update failed during maintained-interest benchmark.");
    }
    double updateMicroseconds = Microseconds(Stopwatch.GetTimestamp() - started);
    long afterUpdate = GC.GetAllocatedBytesForCurrentThread();
    started = Stopwatch.GetTimestamp();
    (ulong bundles, ulong bytes) = DrainInterest(service, now + 50, scratch);
    double deliveryMicroseconds = Microseconds(Stopwatch.GetTimestamp() - started);
    long afterDelivery = GC.GetAllocatedBytesForCurrentThread();

    int recipientIndex = checked((int)((now / 100) % recipients));
    ParticipantId participant = new(checked((ulong)recipientIndex + 1));
    ReplicationSpatialPoint origin = RecipientAnchor(recipientIndex, anchorColumns);
    started = Stopwatch.GetTimestamp();
    Check(service.SetRecipientAnchor(participant,
        new ReplicationSpatialPoint(checked(origin.XMillimeters + regionEdge), origin.ZMillimeters),
        now + 51) == GameInterestUpdateStatus.Success,
        "Recipient traversal failed during maintained-interest benchmark.");
    Check(service.SetRecipientAnchor(participant, origin, now + 52) == GameInterestUpdateStatus.Success,
        "Recipient return traversal failed during maintained-interest benchmark.");
    double recipientMoveMicroseconds = Microseconds(Stopwatch.GetTimestamp() - started);
    long afterMove = GC.GetAllocatedBytesForCurrentThread();
    started = Stopwatch.GetTimestamp();
    (ulong movementBundles, ulong movementBytes) = DrainInterest(service, now + 99, scratch);
    double movementDeliveryMicroseconds = Microseconds(Stopwatch.GetTimestamp() - started);
    return new(
        updateMicroseconds,
        deliveryMicroseconds,
        recipientMoveMicroseconds,
        movementDeliveryMicroseconds,
        checked(bundles + movementBundles),
        checked(bytes + movementBytes),
        afterUpdate - allocated,
        afterDelivery - afterUpdate,
        afterMove - afterDelivery,
        GC.GetAllocatedBytesForCurrentThread() - afterMove);
}

static (ulong Bundles, ulong Bytes) DrainInterest(
    GameInterestService service,
    long nowMilliseconds,
    GameWorldStateRecord[] scratch)
{
    ulong bundles = 0;
    ulong bytes = 0;
    while (true)
    {
        GameWorldStateBuildStatus status = service.TryBuildNextBundle(
            nowMilliseconds, 1_400, scratch, out GameWorldStateBundle bundle);
        if (status == GameWorldStateBuildStatus.NoData) break;
        if (status != GameWorldStateBuildStatus.Success)
            throw new InvalidOperationException(
                $"WorldState debt did not become schedulable: {status}.");
        Check(service.TryAcknowledgeBundle(bundle.Participant, bundle.BundleId, applied: true),
            "Application ACK failed during maintained-interest benchmark.");
        bundles++;
        bytes = checked(bytes + (ulong)bundle.EstimatedEncodedBytes);
    }
    return (bundles, bytes);
}

static ReplicationSpatialPoint RecipientAnchor(int recipient, int columns) => new(
    checked((recipient % columns) * 24_000),
    checked((recipient / columns) * 24_000));

static GameProjectedEntity BenchmarkProjected(int index, int marker) => new(
    new ReplicationEntityId(checked((uint)index), 1),
    HasSimulationState: true,
    new GameSimulationState { TickMarker = marker, InputVelocity = marker });

static int ReadInt(string[] arguments, string name, int fallback)
{
    string? value = ReadText(arguments, name);
    return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

static string? ReadText(string[] arguments, string name)
{
    string prefix = name + "=";
    foreach (string argument in arguments)
        if (argument.StartsWith(prefix, StringComparison.Ordinal)) return argument[prefix.Length..];
    return null;
}

static double Microseconds(long ticks) => ticks * (1_000_000d / Stopwatch.Frequency);

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct Stats(double MeanMicroseconds, double P50Microseconds, double P95Microseconds,
    double P99Microseconds, double MaximumMicroseconds)
{
    internal static Stats From(double[] source)
    {
        double[] sorted = (double[])source.Clone();
        Array.Sort(sorted);
        return new Stats(
            source.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1]);
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
}

readonly record struct BenchmarkReport(
    string Workload,
    DateTimeOffset CapturedUtc,
    string Runtime,
    int ProcessorCount,
    int Population,
    int Warmup,
    int Samples,
    double BaselineMicroseconds,
    Stats ScheduledTick,
    Stats CanonicalCapture,
    Stats OfflineDirectApply,
    Stats IdleCapture,
    long TimedManagedBytes,
    long ProjectionReservedBytes,
    long OfflineReservedBytes,
    int ProjectionEntityCount,
    int OfflineEntityCount,
    ulong OfflineCanonicalHash,
    string SemanticTraceDigest);

readonly record struct InterestRoundTiming(
    double UpdateMicroseconds,
    double DeliveryMicroseconds,
    double RecipientMoveMicroseconds,
    double MovementDeliveryMicroseconds,
    ulong Bundles,
    ulong BundleBytes,
    long UpdateAllocatedBytes,
    long DeliveryAllocatedBytes,
    long RecipientMoveAllocatedBytes,
    long MovementDeliveryAllocatedBytes);

readonly record struct N07BenchmarkReport(
    string Workload,
    DateTimeOffset CapturedUtc,
    string Runtime,
    int ProcessorCount,
    int Entities,
    int Recipients,
    int DirtyPercent,
    int Warmup,
    int Samples,
    int EntitySpacingMillimeters,
    int RegionEdgeMillimeters,
    int ImmediateRadiusMillimeters,
    int NearRadiusMillimeters,
    int FarRadiusMillimeters,
    double SetupMicroseconds,
    Stats CanonicalUpdate,
    Stats PendingDeliveryAndApplicationAck,
    Stats OneRecipientRegionCrossing,
    Stats MovementDeliveryAndApplicationAck,
    Stats IdleScheduler,
    long TimedManagedBytes,
    long CanonicalUpdateAllocatedBytes,
    long DeliveryAllocatedBytes,
    long RecipientMoveAllocatedBytes,
    long MovementDeliveryAllocatedBytes,
    int InitialRelations,
    int FinalRelations,
    int FinalPendingRelations,
    ulong UniqueSemanticConstructions,
    ulong SemanticRecordCopies,
    ulong SemanticBytesCopied,
    ulong DeliveredBundles,
    ulong DeliveredBundleBytes,
    int RegionInterestEdges,
    long ReservedBytes,
    string Notes);
