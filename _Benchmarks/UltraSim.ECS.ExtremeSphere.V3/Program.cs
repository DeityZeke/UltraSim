using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.ECS.ExtremeSphere;
using UltraSim.ECS.ExtremeSphere.V3;

ExtremeSphereAdapterOptions options = ExtremeSphereAdapterOptions.Parse(
    args,
    Path.Combine(AppContext.BaseDirectory, "extreme-sphere-v3.json"));

if (options.Configuration.Pipeline != SpherePipeline.Split3)
    throw new NotSupportedException("The first comparable V2/V3 adapter supports the exact three-system pipeline only.");

ExtremeSphereRunReport report = Run(options.Configuration);
ExtremeSphereJson.Write(report, options.OutputPath);
Console.WriteLine($"V3 {report.Configuration.Mode} {report.Configuration.EntityCount:N0}: " +
    $"mean={report.TickTiming.MeanMicroseconds / 1000d:F3} ms " +
    $"p95={report.TickTiming.P95Microseconds / 1000d:F3} ms " +
    $"hash={report.CanonicalState.Hash} output={options.OutputPath}");

static ExtremeSphereRunReport Run(SphereRunConfiguration configuration)
{
    MemorySnapshot beforeWorld = MemorySnapshot.Capture();
    ComponentSchema schema = ExtremeSphereSchema.CreateSchema();
    ComponentId[] signature =
    [
        ExtremeSphereSchema.GetComponentId<SpherePosition>(),
        ExtremeSphereSchema.GetComponentId<SphereVelocity>(),
        ExtremeSphereSchema.GetComponentId<SpherePulse>(),
    ];
    Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));

    ChunkLayoutOptions layout = new(configuration.ChunkSizeKiB * 1024);
    Span<ComponentColumnLayout> columns = stackalloc ComponentColumnLayout[signature.Length];
    ChunkLayoutPlan layoutPlan = ChunkLayoutPlanner.Plan(schema, signature, layout, columns);
    int maximumChunks = checked((configuration.EntityCount + layoutPlan.RowCapacity - 1) / layoutPlan.RowCapacity + 2);
    bool parallel = configuration.Mode == SphereExecutionMode.Parallel;
    int participants = parallel ? configuration.BackgroundWorkers + 1 : 1;
    int maximumPartitions = Math.Max(1, participants * 4);
    var scheduler = new EcsSchedulerOptions(
        maximumSystems: 3,
        maximumDependencies: 2,
        maximumWorkItems: maximumPartitions * 3,
        maximumStructuralSegments: maximumPartitions * 3,
        backgroundWorkerCount: parallel ? configuration.BackgroundWorkers : 0,
        callerParticipates: true,
        structuralCommandsPerProducer: 0,
        structuralPayloadBytesPerProducer: 0,
        maximumPartitionsPerSystem: maximumPartitions,
        profilingEnabled: false,
        phaseStaggeringEnabled: false);

    using var world = new EcsWorld(
        schema,
        new EcsWorldOptions(
            new EntityCapacity(configuration.EntityCount),
            maximumArchetypes: 1,
            maximumPreparedPlans: 7,
            maximumStructuralCommands: 0,
            scheduler: scheduler));
    ArchetypeHandle archetype = world.RegisterArchetype(signature, layout, maximumChunks);
    ComponentLookup<SpherePosition> positions = world.PrepareLookup<SpherePosition>();
    ComponentLookup<SphereVelocity> velocities = world.PrepareLookup<SphereVelocity>();
    ComponentLookup<SpherePulse> pulses = world.PrepareLookup<SpherePulse>();
    WriteJobPlan<SpherePulse> phasePlan = world.PrepareWriteJob<SpherePulse>();
    ReadReadWriteJobPlan<SpherePosition, SpherePulse, SphereVelocity> velocityPlan =
        world.PrepareReadReadWriteJob<SpherePosition, SpherePulse, SphereVelocity>();
    ReadWriteJobPlan<SphereVelocity, SpherePosition> movementPlan =
        world.PrepareReadWriteJob<SphereVelocity, SpherePosition>();
    ReadReadReadJobPlan<SpherePosition, SphereVelocity, SpherePulse> canonicalPlan =
        world.PrepareReadReadReadJob<SpherePosition, SphereVelocity, SpherePulse>();

    var phaseRuntime = new PhaseRuntime();
    var velocityRuntime = new VelocityRuntime();
    var movementRuntime = new MovementRuntime();
    SystemExecutionPolicy policy = parallel ? SystemExecutionPolicy.Parallel : SystemExecutionPolicy.Serial;
    SystemId phaseId = world.RegisterWriteSystem<SpherePulse, PhaseRuntime, PhaseJob>(
        Definition("extreme-sphere.phase", SystemStage.Movement, policy), phasePlan, phaseRuntime);
    SystemId velocityId = world.RegisterReadReadWriteSystem<
        SpherePosition, SpherePulse, SphereVelocity, VelocityRuntime, VelocityJob>(
        Definition("extreme-sphere.velocity", SystemStage.SpatialIndex, policy, [phaseId]), velocityPlan, velocityRuntime);
    world.RegisterReadWriteSystem<SphereVelocity, SpherePosition, MovementRuntime, MovementJob>(
        Definition("extreme-sphere.movement", SystemStage.Perception, policy, [velocityId]), movementPlan, movementRuntime);

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
        if (!world.TryCreate(archetype, out Entity entity) ||
            !world.TryWrite(entity, positions, new SpherePosition { X = px, Y = py, Z = pz }) ||
            !world.TryWrite(entity, velocities, default) ||
            !world.TryWrite(entity, pulses, new SpherePulse { Speed = speed, Frequency = frequency, Phase = phase }))
            throw new InvalidOperationException($"V3 seed failed at ordinal {ordinal:N0}.");
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
    WorldReservedPayloadSnapshot seedReserved = world.GetReservedPayloadSnapshot();
    MemorySnapshot afterSeed = MemorySnapshot.Capture(
        seedReserved.TotalKnownPayloadBytes, seedReserved.NativeChunkPayloadBytes);

    world.BeginRun();
    for (int tick = 0; tick < configuration.WarmupTicks; tick++)
        world.Tick(configuration.DeltaSeconds);

    long[] totalTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    long[] phaseTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    long[] velocityTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    long[] movementTicks = GC.AllocateUninitializedArray<long>(configuration.MeasuredTicks);
    SchedulerRuntimeSnapshot schedulerBefore = world.GetSchedulerRuntimeSnapshot();
    MeasurementBaseline measurementBaseline = MeasurementBaseline.Capture();
    for (int tick = 0; tick < configuration.MeasuredTicks; tick++)
    {
        long start = Stopwatch.GetTimestamp();
        world.Tick(configuration.DeltaSeconds);
        totalTicks[tick] = Stopwatch.GetTimestamp() - start;
        phaseTicks[tick] = phaseRuntime.LastElapsedTicks;
        velocityTicks[tick] = velocityRuntime.LastElapsedTicks;
        movementTicks[tick] = movementRuntime.LastElapsedTicks;
        if (phaseRuntime.LastRows != configuration.EntityCount ||
            velocityRuntime.LastRows != configuration.EntityCount ||
            movementRuntime.LastRows != configuration.EntityCount)
            throw new InvalidOperationException("A scheduled V3 system did not visit the full population.");
    }
    SchedulerRuntimeSnapshot schedulerAfter = world.GetSchedulerRuntimeSnapshot();
    long workerAllocationDelta = schedulerAfter.WorkerManagedAllocationBytes - schedulerBefore.WorkerManagedAllocationBytes;
    AllocationMetrics measurementAllocations = measurementBaseline.Complete(workerAllocationDelta);
    WorldReservedPayloadSnapshot reserved = world.GetReservedPayloadSnapshot();
    MemorySnapshot afterMeasurement = MemorySnapshot.Capture(
        reserved.TotalKnownPayloadBytes, reserved.NativeChunkPayloadBytes);

    var canonicalJob = new CanonicalJob { Hasher = new CanonicalSphereHasher() };
    int canonicalRows = world.ExecuteJob(canonicalPlan, ref canonicalJob);
    if (canonicalRows != configuration.EntityCount || canonicalJob.Rows != configuration.EntityCount)
        throw new InvalidOperationException("V3 canonical scan did not visit the expected population.");
    CanonicalSphereState canonical = canonicalJob.Hasher.Finish();

    TimingDistribution tickTiming = TimingDistribution.FromStopwatchTicks(totalTicks);
    double entitiesPerSecond = configuration.EntityCount * 1_000_000d / tickTiming.MeanMicroseconds;
    double nsPerEntityTick = tickTiming.MeanMicroseconds * 1_000d / configuration.EntityCount;
    double effectiveGiB = entitiesPerSecond * SphereWorkload.SplitPipelineBytesPerEntityTick /
        (1024d * 1024d * 1024d);
    return new ExtremeSphereRunReport
    {
        ProtocolVersion = SphereWorkload.ProtocolVersion,
        Generation = "V3",
        KernelProfile = "ECS 3.1.2 scheduler proof hardening",
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
        SchedulerDispatches = schedulerAfter.DispatchCount - schedulerBefore.DispatchCount,
        SchedulerWorkerWakes = schedulerAfter.WorkerWakeCount - schedulerBefore.WorkerWakeCount,
        SchedulerWorkerParks = schedulerAfter.WorkerParkCount - schedulerBefore.WorkerParkCount,
        TotalRowsVisited = checked((long)configuration.EntityCount * configuration.MeasuredTicks * 3L),
        AllocationScope = "GC.GetTotalAllocatedBytes covers the process; V3 worker counters independently cover scheduler workers.",
        Notes = "Seeded once; warmed once; sustained fixed-population three-system tick. ECS-only: no renderer, culling, or transform upload.",
    };
}

static SystemDefinition Definition(
    string key,
    SystemStage stage,
    SystemExecutionPolicy policy,
    IReadOnlyList<SystemId>? requires = null) =>
    new(
        key,
        stage,
        executionPolicy: policy,
        requires: requires,
        parallelRowThreshold: 1,
        minimumRowsPerPartition: 4_096,
        automaticallyStagger: false);
