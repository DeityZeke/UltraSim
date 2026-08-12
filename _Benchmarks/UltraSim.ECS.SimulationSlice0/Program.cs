using System.Text.Json;
using UltraSim.ECS.SimulationSlice0;

SimulationOptions options = SimulationOptions.Parse(args);
SliceSelfTests.Run();
SimulationProfile profile = new(options);
double averageTierRowsPerTick = 0d;
for (int tier = 0; tier < profile.TierCounts.Length; tier++)
    averageTierRowsPerTick += profile.TierCounts[tier] / (double)profile.TierPeriods[tier];
var report = new SliceReport
{
    SliceVersion = "ultrasim-simulation-slice-0-v4-compiled-content",
    EcsVersion = "3.3.0",
    EcsSemanticProfile = "ecs-3.3.0-compiled-content-authoring",
    RuntimeVersion = Environment.Version.ToString(),
    ProcessorCount = Environment.ProcessorCount,
    Population = options.Population,
    ActorCount = profile.ActorCount,
    ItemCount = profile.ItemCount,
    EffectSlots = profile.EffectSlots,
    TierDistribution = profile.TierDistribution,
    WarmupTicks = options.WarmupTicks,
    MeasuredTicks = options.MeasuredTicks,
    WorkersRequested = options.Workers,
    ValidationEvery = options.ValidationEvery,
    ProbeRandomWrites = options.ProbeRandomWrites,
    RandomWriteProfile = options.ProbeRandomWrites
        ? "Verified unique target domain + PartitionDisjoint"
        : "Rolling target domain + SerialOnly damage",
    DeferredValidation = options.DeferredValidation,
    AllActive = options.AllActive,
    DeterministicSelfTestsPassed = true,
    LifecycleOverflowProbePassed = SliceSelfTests.LifecycleOverflowProbePassed,
    OriginalLunaOwnershipCollision = SliceSelfTests.OriginalRandomWriteCollision,
    TierFilterPressure = new TierFilterPressureMetrics(
        RemovedDataComponentTypes: 4,
        AddedStorageFreeTagTypes: 4,
        RemovedPhysicalActorArchetypeColumns: 8,
        OldResidentPayloadBytes: checked((long)profile.ActorCount * sizeof(int)),
        NewResidentPayloadBytes: 0,
        AverageTierRowsPerTick: averageTierRowsPerTick,
        OldMarkerReadBytesPerTick: averageTierRowsPerTick * 4d * sizeof(int),
        NewMarkerReadBytesPerTick: 0d,
        CompiledFilteredRouteCount: 16,
        FilteredDenseSystemRegistrations: 16,
        FilteredPreparedPlans: 16),
    Runs = [],
};

if (options.Mode is RunMode.Both or RunMode.Serial)
    report.Runs.Add(Run(options, parallel: false));
if (options.Mode is RunMode.Both or RunMode.Parallel)
    report.Runs.Add(Run(options, parallel: true));

if (report.Runs.Any(run => run.EcsCanonicalHash != run.OracleCanonicalHash))
    throw new InvalidOperationException("The Slice 0 oracle comparison failed.");
if (report.Runs.Select(run => run.EcsCanonicalHash).Distinct().Count() != 1)
    throw new InvalidOperationException("Serial and parallel canonical state diverged.");
if (report.Runs.Select(run => run.PresentationLifecycle.OrderedHash).Distinct().Count() != 1)
    throw new InvalidOperationException("Serial and parallel ordered lifecycle streams diverged.");

Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
}));

Console.WriteLine($"Simulation Slice 0: {profile.ActorCount:N0} actors, " +
    $"{profile.ItemCount:N0} items, {profile.EffectSlots:N0} effect slots");
foreach (RunMetrics run in report.Runs)
{
        Console.WriteLine($"{run.Mode}: mean={run.Timing.MeanMicroseconds / 1_000d:F3} ms " +
        $"p95={run.Timing.P95Microseconds / 1_000d:F3} ms " +
        $"ecs-alloc={run.Allocations.EcsTickCallerThreadBytes:N0} B " +
        $"hash=0x{run.EcsCanonicalHash:X16}");
}
Console.WriteLine($"Result: {options.OutputPath}");

static RunMetrics Run(SimulationOptions options, bool parallel)
{
    using var simulation = SimulationWorld.Create(options, parallel);
    var oracle = new SemanticOracle(simulation.Profile);
    RunMetrics metrics = simulation.Run(oracle);
    if (metrics.EcsCanonicalHash != metrics.OracleCanonicalHash)
        throw new InvalidOperationException($"{metrics.Mode} run did not match the semantic oracle.");
    return metrics;
}

internal sealed class SliceReport
{
    public string SliceVersion { get; set; } = string.Empty;
    public string EcsVersion { get; set; } = string.Empty;
    public string EcsSemanticProfile { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public int Population { get; set; }
    public int ActorCount { get; set; }
    public int ItemCount { get; set; }
    public int EffectSlots { get; set; }
    public string TierDistribution { get; set; } = string.Empty;
    public int WarmupTicks { get; set; }
    public int MeasuredTicks { get; set; }
    public int WorkersRequested { get; set; }
    public int ValidationEvery { get; set; }
    public bool ProbeRandomWrites { get; set; }
    public string RandomWriteProfile { get; set; } = string.Empty;
    public bool DeferredValidation { get; set; }
    public bool AllActive { get; set; }
    public bool DeterministicSelfTestsPassed { get; set; }
    public bool LifecycleOverflowProbePassed { get; set; }
    public DamageOwnershipCollision OriginalLunaOwnershipCollision { get; set; }
    public TierFilterPressureMetrics TierFilterPressure { get; set; } = null!;
    public List<RunMetrics> Runs { get; set; } = [];
}

internal sealed record TierFilterPressureMetrics(
    int RemovedDataComponentTypes,
    int AddedStorageFreeTagTypes,
    int RemovedPhysicalActorArchetypeColumns,
    long OldResidentPayloadBytes,
    long NewResidentPayloadBytes,
    double AverageTierRowsPerTick,
    double OldMarkerReadBytesPerTick,
    double NewMarkerReadBytesPerTick,
    int CompiledFilteredRouteCount,
    int FilteredDenseSystemRegistrations,
    int FilteredPreparedPlans);
