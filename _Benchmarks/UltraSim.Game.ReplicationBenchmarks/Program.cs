using System.Diagnostics;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Server.Runtime;

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
