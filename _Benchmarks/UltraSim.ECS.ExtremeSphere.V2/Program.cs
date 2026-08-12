using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.ECS.ExtremeSphere;
using UltraSim.ECS.ExtremeSphere.V2;

ExtremeSphereAdapterOptions options = ExtremeSphereAdapterOptions.Parse(
    args,
    Path.Combine(AppContext.BaseDirectory, "extreme-sphere-v2.json"));

if (options.Configuration.Pipeline != SpherePipeline.Split3)
    throw new NotSupportedException("The first comparable V2/V3 adapter supports the exact three-system pipeline only.");

ExtremeSphereRunReport report = Run(options.Configuration);
ExtremeSphereJson.Write(report, options.OutputPath);
Console.WriteLine($"V2 {report.Configuration.Mode} {report.Configuration.EntityCount:N0}: " +
    $"mean={report.TickTiming.MeanMicroseconds / 1000d:F3} ms " +
    $"p95={report.TickTiming.P95Microseconds / 1000d:F3} ms " +
    $"hash={report.CanonicalState.Hash} output={options.OutputPath}");

static ExtremeSphereRunReport Run(SphereRunConfiguration configuration)
{
    MemorySnapshot beforeWorld = MemorySnapshot.Capture();
    var schemaBuilder = new EcsSchemaBuilder();
    ComponentId positionId = schemaBuilder.RegisterComponent<SpherePosition>();
    ComponentId velocityId = schemaBuilder.RegisterComponent<SphereVelocity>();
    ComponentId pulseId = schemaBuilder.RegisterComponent<SpherePulse>();
    EcsSchema schema = schemaBuilder.Build();
    var signature = new ArchetypeSignature([positionId, velocityId, pulseId]);
    var world = new World(schema);
    var phaseSystem = new PhaseSystem(pulseId);
    var velocitySystem = new VelocitySystem(positionId, pulseId, velocityId);
    var movementSystem = new MovementSystem(positionId, velocityId);
    world.ConfigurePhaseStaggering(enabled: false);
    world.ConfigureExtremeSphereWorkerCount(configuration.BackgroundWorkers);
    world.SetSystemExecutionMode(configuration.Mode == SphereExecutionMode.Parallel
        ? SystemExecutionMode.ParallelBatches
        : SystemExecutionMode.Serial);
    world.RegisterSystems(phaseSystem, velocitySystem, movementSystem);
    world.Seal();
    world.Initialize();

    MeasurementBaseline spawnBaseline = MeasurementBaseline.Capture();
    long spawnStart = Stopwatch.GetTimestamp();
    for (int ordinal = 0; ordinal < configuration.EntityCount; ordinal++)
    {
        SphereWorkload.Initialize(
            ordinal, configuration.Seed, configuration.SpawnRadius,
            out float px, out float py, out float pz,
            out float speed, out float frequency, out float phase);
        Entity entity = world.CreateDefault(signature);
        if (!world.SetComponent(entity, new SpherePosition { X = px, Y = py, Z = pz }) ||
            !world.SetComponent(entity, default(SphereVelocity)) ||
            !world.SetComponent(entity, new SpherePulse { Speed = speed, Frequency = frequency, Phase = phase }))
            throw new InvalidOperationException($"V2 seed failed at ordinal {ordinal:N0}.");
    }
    long spawnTicks = Stopwatch.GetTimestamp() - spawnStart;
    AllocationMetrics spawnAllocations = spawnBaseline.Complete();
    var spawn = new SpawnMetrics(
        spawnTicks * 1_000d / Stopwatch.Frequency,
        spawnTicks * 1_000_000_000d / Stopwatch.Frequency / configuration.EntityCount,
        spawnAllocations.AllThreadsBytes,
        spawnAllocations.CallerThreadBytes,
        spawnAllocations.Gen0Collections,
        spawnAllocations.Gen1Collections,
        spawnAllocations.Gen2Collections);
    MemorySnapshot afterSeed = MemorySnapshot.Capture();

    for (int tick = 0; tick < configuration.WarmupTicks; tick++)
        world.Tick(configuration.DeltaSeconds);

    long[] totalTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    long[] phaseTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    long[] velocityTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    long[] movementTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    MeasurementBaseline measurementBaseline = MeasurementBaseline.Capture();
    for (int tick = 0; tick < configuration.MeasuredTicks; tick++)
    {
        long start = Stopwatch.GetTimestamp();
        world.Tick(configuration.DeltaSeconds);
        totalTicks[tick] = Stopwatch.GetTimestamp() - start;
        phaseTicks[tick] = phaseSystem.LastElapsedTicks;
        velocityTicks[tick] = velocitySystem.LastElapsedTicks;
        movementTicks[tick] = movementSystem.LastElapsedTicks;
        if (phaseSystem.LastRows != configuration.EntityCount ||
            velocitySystem.LastRows != configuration.EntityCount ||
            movementSystem.LastRows != configuration.EntityCount)
            throw new InvalidOperationException("A scheduled V2 system did not visit the full population.");
    }
    AllocationMetrics measurementAllocations = measurementBaseline.Complete();
    MemorySnapshot afterMeasurement = MemorySnapshot.Capture();

    var hasher = new CanonicalSphereHasher();
    int canonicalRows = 0;
    foreach (World.QueryChunk<SpherePosition, SphereVelocity, SpherePulse> chunk in
             world.QueryChunks<SpherePosition, SphereVelocity, SpherePulse>())
    {
        for (int row = 0; row < chunk.Entities.Length; row++)
        {
            SpherePosition position = chunk.Component1[row];
            SphereVelocity velocity = chunk.Component2[row];
            SpherePulse pulse = chunk.Component3[row];
            hasher.Add(
                chunk.Entities[row].Index,
                position.X, position.Y, position.Z,
                velocity.X, velocity.Y, velocity.Z,
                pulse.Speed, pulse.Frequency, pulse.Phase);
        }
        canonicalRows += chunk.Entities.Length;
    }
    if (canonicalRows != configuration.EntityCount)
        throw new InvalidOperationException("V2 canonical scan did not visit the expected population.");
    CanonicalSphereState canonical = hasher.Finish();
    world.Shutdown();

    TimingDistribution tickTiming = TimingDistribution.FromStopwatchTicks(totalTicks);
    double entitiesPerSecond = configuration.EntityCount * 1_000_000d / tickTiming.MeanMicroseconds;
    double nsPerEntityTick = tickTiming.MeanMicroseconds * 1_000d / configuration.EntityCount;
    double effectiveGiB = entitiesPerSecond * SphereWorkload.SplitPipelineBytesPerEntityTick /
        (1024d * 1024d * 1024d);
    return new ExtremeSphereRunReport
    {
        ProtocolVersion = SphereWorkload.ProtocolVersion,
        Generation = "V2",
        KernelProfile = "V2 current scheduler and packed archetypes",
        RuntimeVersion = Environment.Version.ToString(),
        FrameworkDescription = ExtremeSphereJson.FrameworkDescription,
        OSDescription = ExtremeSphereJson.OSDescription,
        ProcessArchitecture = ExtremeSphereJson.ProcessArchitecture,
        ProcessorCount = Environment.ProcessorCount,
        TimestampUtc = DateTimeOffset.UtcNow,
        Configuration = configuration,
        Spawn = spawn,
        TickTiming = tickTiming,
        MeanNanosecondsPerEntityTick = nsPerEntityTick,
        EntityTicksPerSecond = entitiesPerSecond,
        EffectiveGiBPerSecond = effectiveGiB,
        Systems =
        [
            SphereReportMath.BuildSystemTiming("phase", configuration.EntityCount, 24, phaseTicks),
            SphereReportMath.BuildSystemTiming("velocity", configuration.EntityCount, 36, velocityTicks),
            SphereReportMath.BuildSystemTiming("movement", configuration.EntityCount, 36, movementTicks),
        ],
        MeasurementAllocations = measurementAllocations,
        BeforeWorldMemory = beforeWorld,
        AfterSeedMemory = afterSeed,
        AfterMeasurementMemory = afterMeasurement,
        CanonicalState = canonical,
        SchedulerDispatches = 0,
        SchedulerWorkerWakes = 0,
        SchedulerWorkerParks = 0,
        TotalRowsVisited = checked((long)configuration.EntityCount * configuration.MeasuredTicks * 3L),
        AllocationScope = "GC.GetTotalAllocatedBytes covers caller and V2 worker threads; V2 has no independent worker allocation counter.",
        Notes = "Seeded once; warmed once; sustained fixed-population three-system tick. V2 PatchChunks also publishes its native dirty state. ECS-only: no renderer, culling, or transform upload.",
    };
}
