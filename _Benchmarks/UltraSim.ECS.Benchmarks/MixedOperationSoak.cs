using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.Benchmarks;

internal static class MixedOperationSoakProgram
{
    internal static int Run(string[] args)
    {
        MixedOperationSoakConfiguration configuration =
            MixedOperationSoakConfiguration.Parse(args, out string outputPath);

        Console.WriteLine(
            $"ECS {EcsKernelVersion.Value} deterministic mixed-operation soak: " +
            $"{configuration.EntityCount:N0} entities, {configuration.WarmupTicks:N0} warmup ticks, " +
            $"{configuration.MeasuredTicks:N0} measured ticks x {configuration.MeasuredRuns} runs");
        Console.WriteLine(
            $"Per tick: {configuration.QueryRowsPerTick:N0} query rows, " +
            $"{configuration.TrackedWritesPerTick:N0} tracked writes, " +
            $"{configuration.RandomReadsPerTick:N0} random reads, " +
            $"{configuration.DestroyedPerTick:N0} destroys/creates, " +
            $"{configuration.TransitionsPerTick:N0} transitions");

        MixedOperationSoakReport report = MixedOperationSoakRunner.Run(configuration);
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        Console.WriteLine(
            $"Replay: interval={report.ReplayValidation.Matched}, " +
            $"measured={report.MeasuredReplayMatched}, hash={report.FinalCanonicalHash}");
        foreach (MixedOperationSoakRunReport run in report.Runs)
        {
            SoakPhaseReport total = run.Phases.Single(static phase => phase.Name == "TotalTick");
            SoakPhaseReport ecs = run.Phases.Single(static phase => phase.Name == "MeasuredEcsWork");
            Console.WriteLine(
                $"Run {run.RunIndex}: total mean/p95/p99/p99.9/max=" +
                $"{total.MeanMicroseconds:F3}/{total.P95Microseconds:F3}/" +
                $"{total.P99Microseconds:F3}/{total.P999Microseconds:F3}/" +
                $"{total.MaxMicroseconds:F3} us; ECS work mean={ecs.MeanMicroseconds:F3} us; " +
                $"alloc={run.TimedManagedAllocationBytes} B; GC=" +
                $"{run.Gen0Collections}/{run.Gen1Collections}/{run.Gen2Collections}");
        }
        Console.WriteLine($"Results: {outputPath}");
        return report.ReplayValidation.Matched && report.MeasuredReplayMatched ? 0 : 1;
    }
}

internal sealed class MixedOperationSoakConfiguration
{
    internal const ulong DefaultSeed = 0xD1B54A32D192ED03UL;

    public int EntityCount { get; init; } = 100_000;
    public int WarmupTicks { get; init; } = 2_000;
    public int MeasuredTicks { get; init; } = 30_000;
    public int MeasuredRuns { get; init; } = 2;
    public int ValidationTicks { get; init; } = 256;
    public int ValidationCheckpointInterval { get; init; } = 64;
    public ulong Seed { get; init; } = DefaultSeed;
    public bool UsePublicJobs { get; init; }
    public bool UseScheduler { get; init; }
    public SchedulerRunMode SchedulerRunMode { get; init; } = SchedulerRunMode.Parallel;
    public int SchedulerBackgroundWorkers { get; init; } = 7;

    [JsonIgnore]
    internal int TrackedWritesPerTick => Math.Max(1, EntityCount / 20);

    [JsonIgnore]
    internal int RandomReadsPerTick => Math.Max(1, EntityCount / 200);

    [JsonIgnore]
    internal int DestroyedPerTick => MakeEven(Math.Max(2, EntityCount / 1_000));

    [JsonIgnore]
    internal int CreatedPerTick => DestroyedPerTick;

    [JsonIgnore]
    internal int TransitionsPerTick => MakeEven(Math.Max(2, EntityCount / 500));

    [JsonIgnore]
    internal int QueryRowsPerTick => checked(EntityCount * 4);

    [JsonIgnore]
    internal int StructuralCommandsPerTick => checked(
        DestroyedPerTick + CreatedPerTick + TransitionsPerTick);

    internal static MixedOperationSoakConfiguration Parse(string[] args, out string outputPath)
    {
        int entities = 100_000;
        int warmup = 2_000;
        int ticks = 30_000;
        int runs = 2;
        int validationTicks = 256;
        int checkpointInterval = 64;
        ulong seed = DefaultSeed;
        bool usePublicJobs = false;
        bool useScheduler = false;
        SchedulerRunMode schedulerRunMode = SchedulerRunMode.Parallel;
        int schedulerBackgroundWorkers = 7;
        string? explicitOutputPath = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.Equals("--soak-entities", StringComparison.OrdinalIgnoreCase))
                entities = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-warmup", StringComparison.OrdinalIgnoreCase))
                warmup = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-ticks", StringComparison.OrdinalIgnoreCase))
                ticks = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-runs", StringComparison.OrdinalIgnoreCase))
                runs = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-validation-ticks", StringComparison.OrdinalIgnoreCase))
                validationTicks = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-checkpoint-interval", StringComparison.OrdinalIgnoreCase))
                checkpointInterval = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-seed", StringComparison.OrdinalIgnoreCase))
                seed = ParseULong(args, ref index, argument);
            else if (argument.Equals("--soak-public-jobs", StringComparison.OrdinalIgnoreCase))
                usePublicJobs = true;
            else if (argument.Equals("--soak-scheduler", StringComparison.OrdinalIgnoreCase))
            {
                usePublicJobs = true;
                useScheduler = true;
                schedulerRunMode = SchedulerRunMode.Parallel;
            }
            else if (argument.Equals("--soak-scheduler-serial", StringComparison.OrdinalIgnoreCase))
            {
                usePublicJobs = true;
                useScheduler = true;
                schedulerRunMode = SchedulerRunMode.Serial;
            }
            else if (argument.Equals("--soak-scheduler-workers", StringComparison.OrdinalIgnoreCase))
                schedulerBackgroundWorkers = ParseInt(args, ref index, argument);
            else if (argument.Equals("--soak-output", StringComparison.OrdinalIgnoreCase) ||
                     argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
                explicitOutputPath = Path.GetFullPath(ParseString(args, ref index, argument));
        }

        outputPath = explicitOutputPath ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "results",
            $"ecs-{EcsKernelVersion.Value}-100k-mixed-soak" +
            (usePublicJobs ? "-public-jobs.json" : ".json")));

        var configuration = new MixedOperationSoakConfiguration
        {
            EntityCount = entities,
            WarmupTicks = warmup,
            MeasuredTicks = ticks,
            MeasuredRuns = runs,
            ValidationTicks = validationTicks,
            ValidationCheckpointInterval = checkpointInterval,
            Seed = seed,
            UsePublicJobs = usePublicJobs,
            UseScheduler = useScheduler,
            SchedulerRunMode = schedulerRunMode,
            SchedulerBackgroundWorkers = schedulerBackgroundWorkers,
        };
        configuration.Validate();
        return configuration;
    }

    internal void Validate()
    {
        if (EntityCount < 1_024)
            throw new ArgumentOutOfRangeException(nameof(EntityCount), "The mixed soak requires at least 1,024 entities.");
        if (WarmupTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(WarmupTicks));
        if (MeasuredTicks < 1_000)
            throw new ArgumentOutOfRangeException(nameof(MeasuredTicks), "At least 1,000 measured ticks are required for useful tails.");
        if (MeasuredRuns < 2)
            throw new ArgumentOutOfRangeException(nameof(MeasuredRuns), "At least two measured replays are required.");
        if (ValidationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(ValidationTicks));
        if (ValidationCheckpointInterval <= 0 || ValidationTicks % ValidationCheckpointInterval != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ValidationCheckpointInterval),
                "The validation interval must divide validation ticks exactly.");
        }
        if (DestroyedPerTick + TransitionsPerTick >= EntityCount / 2)
            throw new ArgumentOutOfRangeException(nameof(EntityCount), "Structural selection has insufficient population headroom.");
        if (SchedulerBackgroundWorkers < 0)
            throw new ArgumentOutOfRangeException(nameof(SchedulerBackgroundWorkers));
        if (UseScheduler && !UsePublicJobs)
            throw new ArgumentException("Scheduled soak execution requires public generated job plans.");
    }

    internal MixedOperationSoakConfigurationReport ToReport() => new()
    {
        EntityCount = EntityCount,
        WarmupTicks = WarmupTicks,
        MeasuredTicks = MeasuredTicks,
        MeasuredRuns = MeasuredRuns,
        ValidationTicks = ValidationTicks,
        ValidationCheckpointInterval = ValidationCheckpointInterval,
        Seed = Seed,
        ExecutionSurface = UseScheduler
            ? $"ScheduledGeneratedSystems/{SchedulerRunMode}/C{SchedulerBackgroundWorkers + 1}"
            : UsePublicJobs ? "PublicGeneratedChunkJobs" : "InternalGeneratedControl",
        QueryPassesPerTick = 4,
        QueryRowsPerTick = QueryRowsPerTick,
        LogicalQueryBytesPerTick = checked((long)EntityCount * (16 + 12 + 12 + 12)),
        TrackedWritesPerTick = TrackedWritesPerTick,
        RandomReadsPerTick = RandomReadsPerTick,
        DestroyedPerTick = DestroyedPerTick,
        CreatedPerTick = CreatedPerTick,
        TransitionsPerTick = TransitionsPerTick,
        StructuralCommandsPerTick = StructuralCommandsPerTick,
    };

    private static int MakeEven(int value) => (value & 1) == 0 ? value : value + 1;

    private static int ParseInt(string[] args, ref int index, string argument) =>
        int.Parse(ParseString(args, ref index, argument), System.Globalization.CultureInfo.InvariantCulture);

    private static ulong ParseULong(string[] args, ref int index, string argument)
    {
        string value = ParseString(args, ref index, argument);
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt64(value[2..], 16)
            : ulong.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ParseString(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{argument} requires a value.");
        return args[++index];
    }
}

internal static class MixedOperationSoakRunner
{
    internal static MixedOperationSoakReport Run(MixedOperationSoakConfiguration configuration)
    {
        configuration.Validate();
        ReplayValidationReport validation = ValidateReplay(configuration);
        if (!validation.Matched)
            throw new InvalidOperationException("The interval canonical replay diverged before timing began.");

        var executions = new MixedOperationSoakExecution[configuration.MeasuredRuns];
        for (int runIndex = 0; runIndex < executions.Length; runIndex++)
        {
            Console.WriteLine($"Starting measured replay {runIndex + 1}/{executions.Length}...");
            executions[runIndex] = ExecuteMeasuredRun(configuration, runIndex + 1);
        }

        string finalHash = executions[0].Report.FinalCanonicalHash;
        long checksum = executions[0].Report.Checksum;
        bool replayMatched = true;
        for (int index = 1; index < executions.Length; index++)
        {
            replayMatched &= executions[index].Report.FinalCanonicalHash == finalHash;
            replayMatched &= executions[index].Report.Checksum == checksum;
        }
        if (!replayMatched)
            throw new InvalidOperationException("The measured canonical replay diverged between isolated worlds.");

        return new MixedOperationSoakReport
        {
            SuiteName = "UltraSim ECS deterministic 100K mixed-operation soak",
            ReportFormat = "ultrasim-ecs-mixed-soak-report-v1",
            WorkloadProfile = configuration.UseScheduler
                ? "ecs-v3-100k-mixed-operations-scheduled-parity-v1"
                : configuration.UsePublicJobs
                    ? "ecs-v3-100k-mixed-operations-public-jobs-v1"
                    : "ecs-v3-100k-mixed-operations-v1",
            CanonicalStateProfile = "ultrasim-ecs-mixed-soak-canonical-v1",
            KernelVersion = EcsKernelVersion.Value,
            SemanticProfile = EcsKernelVersion.SemanticProfile,
            TimestampUtc = DateTimeOffset.UtcNow,
            Configuration = configuration.ToReport(),
            Environment = SoakEnvironmentReport.Capture(),
            ReplayValidation = validation,
            MeasuredReplayMatched = replayMatched,
            FinalCanonicalHash = finalHash,
            Runs = executions.Select(static execution => execution.Report).ToArray(),
            CombinedPhases = PhaseSamples.Combine(
                executions.Select(static execution => execution.Samples).ToArray(),
                configuration,
                executions.Sum(static execution => execution.Report.ChangedRowsDelivered) /
                    (double)(configuration.MeasuredTicks * executions.Length)),
            Interpretation = new SoakInterpretation
            {
                TickBudgetMilliseconds = 16.0,
                EcsInfrastructureTargetMilliseconds = 0.5,
                QueryTimingIncludesUsefulComponentArithmetic = true,
                SelectionTimingExcludedFromMeasuredEcsWork = true,
                CanonicalHashTimingExcludedFromTicks = true,
                NaturalGc = true,
                Notes = configuration.UseScheduler
                    ? "This scheduler parity profile preserves the public-job workload selections and canonical " +
                      "state, but executes query, 5K prepared random writes, 500 prepared random reads, and exact " +
                      "change consumption inside the scheduler graph. The phase named Query is the complete graph " +
                      "tick and is included exactly once. The 100 destroy, 100 create, and 200 transition commands " +
                      "are intentionally recorded and played outside the graph in this longitudinal parity profile; " +
                      "worker structural-lane merge is measured separately by scheduler structural fixtures."
                    : configuration.UsePublicJobs
                    ? "This single-owner profile executes all four ordinary system-like passes, exact changed-row " +
                      "consumption, canonical ownership traversal, cursors, and store inspection through public ECS APIs. " +
                      "The 5K arbitrary entity-list activity writes intentionally remain checked random writes. " +
                      "MeasuredEcsWork excludes deterministic target selection but includes useful job arithmetic, " +
                      "checked lookups, tracked writes, change consumption, command recording/playback, and create initialization."
                    : "This is the immutable longitudinal 3.0.35 control profile. Generated chunk queries use the " +
                      "internal seal-compiled surface. MeasuredEcsWork excludes deterministic target selection but " +
                      "includes useful visitor arithmetic, checked lookups, tracked writes, change consumption, " +
                      "command recording/playback, and create initialization.",
            },
        };
    }

    private static ReplayValidationReport ValidateReplay(MixedOperationSoakConfiguration configuration)
    {
        Console.WriteLine(
            $"Validating canonical replay at {configuration.ValidationCheckpointInterval}-tick checkpoints...");
        CanonicalTrace first = ExecuteCanonicalTrace(configuration);
        CanonicalTrace second = ExecuteCanonicalTrace(configuration);
        bool matched = first.Checksums.SequenceEqual(second.Checksums) &&
            first.Checkpoints.Select(static checkpoint => checkpoint.Hash)
                .SequenceEqual(second.Checkpoints.Select(static checkpoint => checkpoint.Hash));
        return new ReplayValidationReport
        {
            Ticks = configuration.ValidationTicks,
            CheckpointInterval = configuration.ValidationCheckpointInterval,
            CheckpointCount = first.Checkpoints.Length,
            Matched = matched,
            Checkpoints = first.Checkpoints,
        };
    }

    private static CanonicalTrace ExecuteCanonicalTrace(MixedOperationSoakConfiguration configuration)
    {
        using var world = new MixedOperationSoakWorld(configuration);
        int checkpointCount = checked(
            configuration.ValidationTicks / configuration.ValidationCheckpointInterval + 1);
        var checkpoints = new ReplayCheckpoint[checkpointCount];
        var checksums = new long[checkpointCount];
        int checkpointIndex = 0;
        checkpoints[checkpointIndex] = new ReplayCheckpoint
        {
            Tick = 0,
            Hash = world.ComputeCanonicalHash(),
        };
        checksums[checkpointIndex++] = world.Checksum;

        for (int tick = 1; tick <= configuration.ValidationTicks; tick++)
        {
            world.ExecuteTick(samples: null, sampleIndex: -1);
            if (tick % configuration.ValidationCheckpointInterval != 0)
                continue;
            checkpoints[checkpointIndex] = new ReplayCheckpoint
            {
                Tick = tick,
                Hash = world.ComputeCanonicalHash(),
            };
            checksums[checkpointIndex++] = world.Checksum;
        }

        return new CanonicalTrace(checkpoints, checksums);
    }

    private static MixedOperationSoakExecution ExecuteMeasuredRun(
        MixedOperationSoakConfiguration configuration,
        int runIndex)
    {
        using var world = new MixedOperationSoakWorld(configuration);
        for (int tick = 0; tick < configuration.WarmupTicks; tick++)
            world.ExecuteTick(samples: null, sampleIndex: -1);

        var samples = new PhaseSamples(configuration.MeasuredTicks);
        SoakCounters countersBefore = world.CaptureCounters();
        using Process process = Process.GetCurrentProcess();
        SoakStoreReport[] storesBefore = world.CaptureStores();
        long managedHeapBefore = GC.GetGCMemoryInfo().HeapSizeBytes;
        long retainedManagedBefore = GC.GetTotalMemory(forceFullCollection: false);
        long workingSetBefore = process.WorkingSet64;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        long runStarted = Stopwatch.GetTimestamp();

        for (int tick = 0; tick < configuration.MeasuredTicks; tick++)
            world.ExecuteTick(samples, tick);

        long runStopped = Stopwatch.GetTimestamp();
        long allocationAfter = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0) - gen0Before;
        int gen1 = GC.CollectionCount(1) - gen1Before;
        int gen2 = GC.CollectionCount(2) - gen2Before;
        long workingSetAfter = process.WorkingSet64;
        long peakWorkingSet = process.PeakWorkingSet64;
        long retainedManagedAfter = GC.GetTotalMemory(forceFullCollection: false);
        long managedHeapAfter = GC.GetGCMemoryInfo().HeapSizeBytes;
        SoakStoreReport[] storesAfter = world.CaptureStores();
        SoakCounters countersAfter = world.CaptureCounters();
        SoakCounters measuredCounters = countersAfter - countersBefore;
        string canonicalHash = world.ComputeCanonicalHash();

        long allocation = allocationAfter - allocationBefore;
        if (allocation != 0)
        {
            throw new InvalidOperationException(
                $"Measured replay {runIndex} allocated {allocation} managed bytes after warmup.");
        }
        if (world.AliveCount != configuration.EntityCount)
            throw new InvalidOperationException("The measured soak did not preserve the configured live population.");

        long elapsedTicks = runStopped - runStarted;
        double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
        return new MixedOperationSoakExecution(
            new MixedOperationSoakRunReport
            {
                RunIndex = runIndex,
                ElapsedSeconds = elapsedSeconds,
                TicksPerSecond = configuration.MeasuredTicks / elapsedSeconds,
                Checksum = world.Checksum,
                FinalCanonicalHash = canonicalHash,
                AliveCount = world.AliveCount,
                ChangeVersion = world.CurrentChangeVersion,
                QueryRowsVisited = measuredCounters.QueryRowsVisited,
                RandomReads = measuredCounters.RandomReads,
                TrackedWrites = measuredCounters.TrackedWrites,
                ChangedRowsDelivered = measuredCounters.ChangedRowsDelivered,
                CommandsRecorded = measuredCounters.CommandsRecorded,
                CommandsApplied = measuredCounters.CommandsApplied,
                CommandsRejected = measuredCounters.CommandsRejected,
                TemporaryHoles = measuredCounters.TemporaryHoles,
                ReusedTemporaryHoles = measuredCounters.ReusedTemporaryHoles,
                CompactedTemporaryHoles = measuredCounters.CompactedTemporaryHoles,
                TemporaryHoleDonorRelocations = measuredCounters.TemporaryHoleDonorRelocations,
                ReusedTemporaryHoleFixedRowBytes = measuredCounters.ReusedTemporaryHoleFixedRowBytes,
                StructuralCommandCapacity = world.StructuralCommandCapacity,
                StructuralCommandHighWater = world.StructuralCommandHighWater,
                StructuralCommandReservedBytes = world.StructuralCommandReservedBytes,
                TimedManagedAllocationBytes = allocation,
                Gen0Collections = gen0,
                Gen1Collections = gen1,
                Gen2Collections = gen2,
                ManagedHeapBeforeBytes = managedHeapBefore,
                ManagedHeapAfterBytes = managedHeapAfter,
                RetainedManagedBeforeBytes = retainedManagedBefore,
                RetainedManagedAfterBytes = retainedManagedAfter,
                WorkingSetBeforeBytes = workingSetBefore,
                WorkingSetAfterBytes = workingSetAfter,
                PeakWorkingSetBytes = peakWorkingSet,
                StoresBefore = storesBefore,
                StoresAfter = storesAfter,
                Phases = samples.CreateReports(
                    configuration,
                    measuredCounters.ChangedRowsDelivered / (double)configuration.MeasuredTicks),
            },
            samples);
    }

    private readonly record struct CanonicalTrace(
        ReplayCheckpoint[] Checkpoints,
        long[] Checksums);
}

internal sealed class MixedOperationSoakWorld : IDisposable
{
    private readonly MixedOperationSoakConfiguration _configuration;
    private readonly EcsWorld _world;
    private readonly EcsWorld<NativeChunkArena>? _core;
    private readonly ArchetypeHandle _baseArchetype;
    private readonly ArchetypeHandle _wideArchetype;
    private readonly ArchetypeTransition _addWide;
    private readonly ArchetypeTransition _removeWide;
    private readonly ComponentLookup<SoakIdentity> _identityLookup;
    private readonly ComponentLookup<SoakPosition> _positionLookup;
    private readonly ComponentLookup<SoakVelocity> _velocityLookup;
    private readonly ComponentLookup<SoakHealth> _healthLookup;
    private readonly ComponentLookup<SoakActivity> _activityLookup;
    private readonly ComponentLookup<SoakWide> _wideLookup;
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, SoakVelocity, SoakPosition>? _movementPlan;
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, SoakPosition, SoakHealth>? _healthPlan;
    private readonly ArchetypeReadWriteQueryPlan<NativeChunkArena, SoakHealth, SoakVelocity>? _responsePlan;
    private readonly ArchetypeReadReadQueryPlan<NativeChunkArena, SoakPosition, SoakHealth>? _telemetryPlan;
    private readonly ArchetypeChangedReadQueryPlan<NativeChunkArena, SoakIdentity>? _changedPlan;
    private readonly ReadWriteJobPlan<SoakVelocity, SoakPosition> _publicMovementPlan;
    private readonly ReadWriteJobPlan<SoakPosition, SoakHealth> _publicHealthPlan;
    private readonly ReadWriteJobPlan<SoakHealth, SoakVelocity> _publicResponsePlan;
    private readonly ReadReadJobPlan<SoakPosition, SoakHealth> _publicTelemetryPlan;
    private readonly ChangedRowsJobPlan<SoakIdentity, SoakActivity> _publicChangedPlan;
    private readonly ReadReadJobPlan<SoakIdentity, SoakPosition> _publicOwnershipPlan;
    private readonly ArchetypeComponentBinding<SoakIdentity> _baseIdentityBinding;
    private readonly ArchetypeComponentBinding<SoakIdentity> _wideIdentityBinding;
    private readonly ArchetypeStorage<NativeChunkArena>? _baseStorage;
    private readonly ArchetypeStorage<NativeChunkArena>? _wideStorage;
    private readonly StructuralCommandBuffer _commands;
    private readonly Entity[] _entities;
    private readonly bool[] _isWide;
    private readonly int[] _lifecycleRevisions;
    private readonly int[] _selectionMarks;
    private readonly int[] _writeMarks;
    private readonly int[] _randomReadSlots;
    private readonly int[] _trackedWriteSlots;
    private readonly int[] _canonicalSeen;
    private readonly Entity[] _destroyEntities;
    private readonly int[] _destroySlots;
    private readonly ArchetypeHandle[] _createArchetypes;
    private readonly DeferredEntity[] _createTokens;
    private readonly Entity[] _addWideEntities;
    private readonly int[] _addWideSlots;
    private readonly Entity[] _removeWideEntities;
    private readonly int[] _removeWideSlots;
    private DeterministicRandom _random;
    private ChangeCursor _changeCursor;
    private int _selectionEpoch;
    private int _writeEpoch;
    private int _tick;
    private int _structuralCommandHighWater;
    private long _checksum;
    private long _queryRowsVisited;
    private long _randomReads;
    private long _trackedWrites;
    private long _changedRowsDelivered;
    private long _commandsRecorded;
    private long _commandsApplied;
    private long _commandsRejected;
    private long _temporaryHoles;
    private long _reusedTemporaryHoles;
    private long _compactedTemporaryHoles;
    private long _temporaryHoleDonorRelocations;
    private long _reusedTemporaryHoleFixedRowBytes;

    internal MixedOperationSoakWorld(MixedOperationSoakConfiguration configuration)
    {
        _configuration = configuration;
        _random = new DeterministicRandom(configuration.Seed);
        ComponentSchema schema = MixedOperationSoakSchema.CreateSchema();
        _world = new EcsWorld(
            schema,
            new EcsWorldOptions(
                new EntityCapacity(configuration.EntityCount),
                maximumArchetypes: 2,
                maximumPreparedPlans: 16,
                maximumPreparedTransitions: 2,
                maximumStructuralCommands: configuration.StructuralCommandsPerTick,
                scheduler: CreateSchedulerOptions(configuration)));

        ComponentId identity = MixedOperationSoakSchema.GetComponentId<SoakIdentity>();
        ComponentId position = MixedOperationSoakSchema.GetComponentId<SoakPosition>();
        ComponentId velocity = MixedOperationSoakSchema.GetComponentId<SoakVelocity>();
        ComponentId health = MixedOperationSoakSchema.GetComponentId<SoakHealth>();
        ComponentId activity = MixedOperationSoakSchema.GetComponentId<SoakActivity>();
        ComponentId wide = MixedOperationSoakSchema.GetComponentId<SoakWide>();
        int maximumChunks = checked(configuration.EntityCount / 192 + 32);
        _baseArchetype = _world.RegisterArchetype(
            CreateSortedSignature(identity, position, velocity, health, activity),
            ChunkLayoutOptions.KiB32,
            maximumChunks);
        _wideArchetype = _world.RegisterArchetype(
            CreateSortedSignature(identity, position, velocity, health, activity, wide),
            ChunkLayoutOptions.KiB32,
            maximumChunks);
        _addWide = _world.PrepareTransition(_baseArchetype, _wideArchetype);
        _removeWide = _world.PrepareTransition(_wideArchetype, _baseArchetype);
        _identityLookup = _world.PrepareLookup<SoakIdentity>();
        _positionLookup = _world.PrepareLookup<SoakPosition>();
        _velocityLookup = _world.PrepareLookup<SoakVelocity>();
        _healthLookup = _world.PrepareLookup<SoakHealth>();
        _activityLookup = _world.PrepareLookup<SoakActivity>();
        _wideLookup = _world.PrepareLookup<SoakWide>();

        int entityCount = configuration.EntityCount;
        _entities = new Entity[entityCount];
        _isWide = new bool[entityCount];
        _lifecycleRevisions = new int[entityCount];
        _selectionMarks = new int[entityCount];
        _writeMarks = new int[entityCount];
        _randomReadSlots = new int[configuration.RandomReadsPerTick];
        _trackedWriteSlots = new int[configuration.TrackedWritesPerTick];
        _canonicalSeen = new int[entityCount];
        _destroyEntities = new Entity[configuration.DestroyedPerTick];
        _destroySlots = new int[configuration.DestroyedPerTick];
        _createArchetypes = new ArchetypeHandle[configuration.CreatedPerTick];
        _createTokens = new DeferredEntity[configuration.CreatedPerTick];
        int halfTransitions = configuration.TransitionsPerTick / 2;
        _addWideEntities = new Entity[halfTransitions];
        _addWideSlots = new int[halfTransitions];
        _removeWideEntities = new Entity[halfTransitions];
        _removeWideSlots = new int[halfTransitions];

        if (configuration.UsePublicJobs)
        {
            _publicMovementPlan = _world.PrepareReadWriteJob<SoakVelocity, SoakPosition>();
            _publicHealthPlan = _world.PrepareReadWriteJob<SoakPosition, SoakHealth>();
            _publicResponsePlan = _world.PrepareReadWriteJob<SoakHealth, SoakVelocity>();
            _publicTelemetryPlan = _world.PrepareReadReadJob<SoakPosition, SoakHealth>();
            _publicChangedPlan = _world.PrepareChangedRowsJob<SoakIdentity, SoakActivity>();
            _publicOwnershipPlan = _world.PrepareReadReadJob<SoakIdentity, SoakPosition>();
            if (configuration.UseScheduler)
                RegisterScheduledSystems(health, activity);
        }
        else
        {
            FieldInfo coreField = typeof(EcsWorld).GetField(
                "_core",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The ECS facade core field was not found.");
            _core = (EcsWorld<NativeChunkArena>)(coreField.GetValue(_world)
                ?? throw new InvalidOperationException("The ECS facade core is null."));
            _movementPlan = _core.PrepareReadWriteQuery<SoakVelocity, SoakPosition>(velocity, position);
            _healthPlan = _core.PrepareReadWriteQuery<SoakPosition, SoakHealth>(position, health);
            _responsePlan = _core.PrepareReadWriteQuery<SoakHealth, SoakVelocity>(health, velocity);
            _telemetryPlan = _core.PrepareReadReadQuery<SoakPosition, SoakHealth>(position, health);
            _changedPlan = _core.PrepareChangedRowsQuery<SoakIdentity>(identity, activity);
            _baseIdentityBinding = _core.BindComponent<SoakIdentity>(_baseArchetype.Id, identity);
            _wideIdentityBinding = _core.BindComponent<SoakIdentity>(_wideArchetype.Id, identity);
            _baseStorage = _core.GetArchetypeStorage(_baseArchetype.Id);
            _wideStorage = _core.GetArchetypeStorage(_wideArchetype.Id);
        }

        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _commands = _world.StructuralCommands;

        for (int slot = 0; slot < entityCount; slot++)
        {
            bool useWide = (slot & 1) != 0;
            ArchetypeHandle archetype = useWide ? _wideArchetype : _baseArchetype;
            if (!_world.TryCreate(
                    archetype,
                    _identityLookup,
                    new SoakIdentity { Value = slot },
                    out Entity entity))
            {
                throw new InvalidOperationException("The soak fixture exhausted entity or archetype capacity during bootstrap.");
            }
            _entities[slot] = entity;
            _isWide[slot] = useWide;
            _lifecycleRevisions[slot] = 1;
            InitializeEntity(slot, entity, tick: 0, lifecycleRevision: 1, useWide);
        }

        bool plansMatched = configuration.UsePublicJobs
            ? _publicMovementPlan.MatchedArchetypeCount == 2 &&
              _publicHealthPlan.MatchedArchetypeCount == 2 &&
              _publicResponsePlan.MatchedArchetypeCount == 2 &&
              _publicTelemetryPlan.MatchedArchetypeCount == 2 &&
              _publicChangedPlan.MatchedArchetypeCount == 2 &&
              _publicOwnershipPlan.MatchedArchetypeCount == 2
            : _movementPlan!.MatchedArchetypeCount == 2 &&
              _healthPlan!.MatchedArchetypeCount == 2 &&
              _responsePlan!.MatchedArchetypeCount == 2 &&
              _telemetryPlan!.MatchedArchetypeCount == 2 &&
              _changedPlan!.MatchedArchetypeCount == 2;
        if (!plansMatched)
        {
            throw new InvalidOperationException("The soak query catalog did not bind both archetypes.");
        }
        _changeCursor = configuration.UsePublicJobs
            ? _world.CreateChangeCursorFromNow()
            : _core!.CreateChangeCursorFromNow();
        if (configuration.UseScheduler)
            _world.SchedulerRunMode = configuration.SchedulerRunMode;
    }

    internal int AliveCount => _world.AliveCount;
    internal ulong CurrentChangeVersion => _world.CurrentChangeVersion.Value;
    internal long Checksum => _checksum;
    internal long QueryRowsVisited => _queryRowsVisited;
    internal long RandomReads => _randomReads;
    internal long TrackedWrites => _trackedWrites;
    internal long ChangedRowsDelivered => _changedRowsDelivered;
    internal long CommandsRecorded => _commandsRecorded;
    internal long CommandsApplied => _commandsApplied;
    internal long CommandsRejected => _commandsRejected;
    internal long TemporaryHoles => _temporaryHoles;
    internal long ReusedTemporaryHoles => _reusedTemporaryHoles;
    internal long CompactedTemporaryHoles => _compactedTemporaryHoles;
    internal long TemporaryHoleDonorRelocations => _temporaryHoleDonorRelocations;
    internal long ReusedTemporaryHoleFixedRowBytes => _reusedTemporaryHoleFixedRowBytes;
    internal int StructuralCommandCapacity => _commands.Capacity;
    internal int StructuralCommandHighWater => _structuralCommandHighWater;
    internal long StructuralCommandReservedBytes => _commands.ReservedBytes;

    internal SoakCounters CaptureCounters() => new(
        _queryRowsVisited,
        _randomReads,
        _trackedWrites,
        _changedRowsDelivered,
        _commandsRecorded,
        _commandsApplied,
        _commandsRejected,
        _temporaryHoles,
        _reusedTemporaryHoles,
        _compactedTemporaryHoles,
        _temporaryHoleDonorRelocations,
        _reusedTemporaryHoleFixedRowBytes);

    internal void ExecuteTick(PhaseSamples? samples, int sampleIndex)
    {
        if (_configuration.UseScheduler)
        {
            ExecuteTickScheduled(samples, sampleIndex);
            return;
        }
        if (_configuration.UsePublicJobs)
        {
            ExecuteTickPublic(samples, sampleIndex);
            return;
        }
        ExecuteTickInternal(samples, sampleIndex);
    }

    private void ExecuteTickScheduled(PhaseSamples? samples, int sampleIndex)
    {
        long totalStarted = Stopwatch.GetTimestamp();
        long phaseStarted = Stopwatch.GetTimestamp();
        _world.Tick(1d / 64d);
        Record(samples?.Query, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        SelectStructuralTargets();
        Record(samples?.WorkloadSelection, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        RecordStructuralCommands();
        Record(samples?.CommandRecord, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        StructuralPlaybackResult playback = _world.PlaybackStructuralCommands();
        ValidateAndAccumulate(playback);
        Record(samples?.StructuralPlayback, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        PublishStructuralResults();
        Record(samples?.CreateInitialization, sampleIndex, phaseStarted);

        _tick++;
        if (_world.AliveCount != _configuration.EntityCount)
            throw new InvalidOperationException("A scheduled parity soak tick did not preserve its live population.");
        if (samples is not null)
        {
            long total = Stopwatch.GetTimestamp() - totalStarted;
            samples.TotalTick[sampleIndex] = total;
            samples.MeasuredEcsWork[sampleIndex] =
                samples.Query[sampleIndex] +
                samples.CommandRecord[sampleIndex] +
                samples.StructuralPlayback[sampleIndex] +
                samples.CreateInitialization[sampleIndex];
        }
    }

    private void ExecuteTickInternal(PhaseSamples? samples, int sampleIndex)
    {
        long totalStarted = Stopwatch.GetTimestamp();

        long phaseStarted = Stopwatch.GetTimestamp();
        var movement = new MovementVisitor();
        int moved = _core!.VisitReadWriteChunks(_movementPlan!, ref movement);
        var health = new HealthVisitor();
        int healthRows = _core.VisitReadWriteChunks(_healthPlan!, ref health);
        var response = new ResponseVisitor();
        int responseRows = _core.VisitReadWriteChunks(_responsePlan!, ref response);
        var telemetry = new TelemetryVisitor();
        int telemetryRows = _core.VisitReadReadChunks(_telemetryPlan!, ref telemetry);
        if (moved != _configuration.EntityCount ||
            healthRows != _configuration.EntityCount ||
            responseRows != _configuration.EntityCount ||
            telemetryRows != _configuration.EntityCount)
        {
            throw new InvalidOperationException("A full-world soak query lost rows.");
        }
        _queryRowsVisited += moved + healthRows + responseRows + telemetryRows;
        _checksum = unchecked(_checksum + movement.Checksum + health.Checksum +
            response.Checksum + telemetry.Checksum);
        Record(samples?.Query, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        ExecuteRandomReads();
        Record(samples?.RandomLookup, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        ExecuteTrackedWrites();
        Record(samples?.TrackedWrite, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        var changed = new ChangedIdentityVisitor();
        int changedRows = _core.VisitChangedReadRows(_changedPlan!, ref _changeCursor, ref changed);
        if (changedRows != changed.Rows)
            throw new InvalidOperationException("The exact changed-row consumer count is inconsistent.");
        _changedRowsDelivered += changedRows;
        _checksum = unchecked(_checksum + changed.Checksum + changedRows);
        Record(samples?.ChangeConsume, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        SelectStructuralTargets();
        Record(samples?.WorkloadSelection, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        RecordStructuralCommands();
        Record(samples?.CommandRecord, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        StructuralPlaybackResult playback = _world.PlaybackStructuralCommands();
        ValidateAndAccumulate(playback);
        Record(samples?.StructuralPlayback, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        PublishStructuralResults();
        Record(samples?.CreateInitialization, sampleIndex, phaseStarted);

        _tick++;
        if (_world.AliveCount != _configuration.EntityCount)
            throw new InvalidOperationException("A soak tick did not preserve its live population.");
        if (samples is not null)
        {
            long total = Stopwatch.GetTimestamp() - totalStarted;
            samples.TotalTick[sampleIndex] = total;
            samples.MeasuredEcsWork[sampleIndex] =
                samples.Query[sampleIndex] +
                samples.RandomLookup[sampleIndex] +
                samples.TrackedWrite[sampleIndex] +
                samples.ChangeConsume[sampleIndex] +
                samples.CommandRecord[sampleIndex] +
                samples.StructuralPlayback[sampleIndex] +
                samples.CreateInitialization[sampleIndex];
        }
    }

    private void ExecuteTickPublic(PhaseSamples? samples, int sampleIndex)
    {
        long totalStarted = Stopwatch.GetTimestamp();

        long phaseStarted = Stopwatch.GetTimestamp();
        var movement = new MovementVisitor();
        int moved = _world.ExecuteJob(_publicMovementPlan, ref movement);
        var health = new HealthVisitor();
        int healthRows = _world.ExecuteJob(_publicHealthPlan, ref health);
        var response = new ResponseVisitor();
        int responseRows = _world.ExecuteJob(_publicResponsePlan, ref response);
        var telemetry = new TelemetryVisitor();
        int telemetryRows = _world.ExecuteJob(_publicTelemetryPlan, ref telemetry);
        if (moved != _configuration.EntityCount ||
            healthRows != _configuration.EntityCount ||
            responseRows != _configuration.EntityCount ||
            telemetryRows != _configuration.EntityCount)
        {
            throw new InvalidOperationException("A public full-world soak job lost rows.");
        }
        _queryRowsVisited += moved + healthRows + responseRows + telemetryRows;
        _checksum = unchecked(_checksum + movement.Checksum + health.Checksum +
            response.Checksum + telemetry.Checksum);
        Record(samples?.Query, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        ExecuteRandomReads();
        Record(samples?.RandomLookup, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        ExecuteTrackedWrites();
        Record(samples?.TrackedWrite, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        var changed = new ChangedIdentityVisitor();
        int changedRows = _world.ExecuteChangedRowsJob(
            _publicChangedPlan,
            ref _changeCursor,
            ref changed);
        if (changedRows != changed.Rows)
            throw new InvalidOperationException("The public exact changed-row consumer count is inconsistent.");
        _changedRowsDelivered += changedRows;
        _checksum = unchecked(_checksum + changed.Checksum + changedRows);
        Record(samples?.ChangeConsume, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        SelectStructuralTargets();
        Record(samples?.WorkloadSelection, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        RecordStructuralCommands();
        Record(samples?.CommandRecord, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        StructuralPlaybackResult playback = _world.PlaybackStructuralCommands();
        ValidateAndAccumulate(playback);
        Record(samples?.StructuralPlayback, sampleIndex, phaseStarted);

        phaseStarted = Stopwatch.GetTimestamp();
        PublishStructuralResults();
        Record(samples?.CreateInitialization, sampleIndex, phaseStarted);

        _tick++;
        if (_world.AliveCount != _configuration.EntityCount)
            throw new InvalidOperationException("A public soak tick did not preserve its live population.");
        if (samples is not null)
        {
            long total = Stopwatch.GetTimestamp() - totalStarted;
            samples.TotalTick[sampleIndex] = total;
            samples.MeasuredEcsWork[sampleIndex] =
                samples.Query[sampleIndex] +
                samples.RandomLookup[sampleIndex] +
                samples.TrackedWrite[sampleIndex] +
                samples.ChangeConsume[sampleIndex] +
                samples.CommandRecord[sampleIndex] +
                samples.StructuralPlayback[sampleIndex] +
                samples.CreateInitialization[sampleIndex];
        }
    }

    internal string ComputeCanonicalHash() => ComputeCanonicalHash(includeObservationVersions: true);

    internal string ComputeCanonicalLogicalHash() => ComputeCanonicalHash(includeObservationVersions: false);

    private string ComputeCanonicalHash(bool includeObservationVersions)
    {
        Array.Clear(_canonicalSeen);
        int ownershipRows;
        if (_configuration.UsePublicJobs)
        {
            var ownership = new PublicIdentityOwnershipJob(_entities, _canonicalSeen);
            ownershipRows = _world.ExecuteJob(_publicOwnershipPlan, ref ownership);
            if (ownership.Rows != _configuration.EntityCount)
                throw new InvalidOperationException("Canonical public ownership traversal lost rows.");
        }
        else
        {
            var ownership = new IdentityOwnershipVisitor(_entities, _canonicalSeen);
            int baseRows = _core!.VisitReadChunks(_baseIdentityBinding, ref ownership);
            int wideRows = _core.VisitReadChunks(_wideIdentityBinding, ref ownership);
            ownershipRows = baseRows + wideRows;
            if (ownership.Rows != _configuration.EntityCount)
                throw new InvalidOperationException("Canonical internal ownership traversal lost rows.");
        }
        if (ownershipRows != _configuration.EntityCount)
            throw new InvalidOperationException("Canonical ownership traversal lost rows.");
        for (int slot = 0; slot < _canonicalSeen.Length; slot++)
        {
            if (_canonicalSeen[slot] != 1)
                throw new InvalidOperationException("Canonical ownership traversal found a duplicate or missing logical entity.");
        }

        var hash = new CanonicalStateHasher();
        hash.AddString(includeObservationVersions
            ? "UltraSim.ECS.MixedOperationSoak.v1"
            : "UltraSim.ECS.MixedOperationSoak.Logical.v1");
        hash.AddUInt64(_configuration.Seed);
        hash.AddInt32(_configuration.EntityCount);
        hash.AddInt32(_tick);
        hash.AddInt32(_world.AliveCount);
        if (includeObservationVersions)
        {
            hash.AddUInt64(_world.CurrentChangeVersion.Value);
            hash.AddUInt64(_changeCursor.Version.Value);
        }
        hash.AddInt64(_checksum);
        hash.AddInt64(_queryRowsVisited);
        hash.AddInt64(_randomReads);
        hash.AddInt64(_trackedWrites);
        hash.AddInt64(_changedRowsDelivered);
        hash.AddInt64(_commandsRecorded);
        hash.AddInt64(_commandsApplied);
        hash.AddInt64(_commandsRejected);

        ChangeCursor pendingCursor = _changeCursor;
        var pendingChanged = new ChangedIdentityVisitor();
        int pendingChangedRows = _configuration.UsePublicJobs
            ? _world.ExecuteChangedRowsJob(
                _publicChangedPlan,
                ref pendingCursor,
                ref pendingChanged)
            : _core!.VisitChangedReadRows(
                _changedPlan!,
                ref pendingCursor,
                ref pendingChanged);
        if (pendingChangedRows != pendingChanged.Rows)
            throw new InvalidOperationException("Canonical pending-change observation is inconsistent.");
        hash.AddInt32(pendingChangedRows);
        hash.AddInt64(pendingChanged.Checksum);
        if (includeObservationVersions)
            hash.AddUInt64(pendingCursor.Version.Value);

        for (int slot = 0; slot < _configuration.EntityCount; slot++)
        {
            Entity entity = _entities[slot];
            if (!_world.IsAlive(entity) ||
                !_world.TryGetArchetype(entity, out ArchetypeHandle archetype) ||
                archetype != (_isWide[slot] ? _wideArchetype : _baseArchetype) ||
                !_world.TryRead(entity, _identityLookup, out SoakIdentity identity) ||
                identity.Value != slot ||
                !_world.TryRead(entity, _positionLookup, out SoakPosition position) ||
                !_world.TryRead(entity, _velocityLookup, out SoakVelocity velocity) ||
                !_world.TryRead(entity, _healthLookup, out SoakHealth health) ||
                !_world.TryRead(entity, _activityLookup, out SoakActivity activity) ||
                !_world.TryGetEnabled(entity, _velocityLookup, out bool velocityEnabled))
            {
                throw new InvalidOperationException("Canonical state validation found an invalid logical entity.");
            }

            hash.AddByte(0xE1);
            hash.AddInt32(slot);
            hash.AddInt32(_lifecycleRevisions[slot]);
            hash.AddUInt32(entity.Generation);
            hash.AddBoolean(_isWide[slot]);
            hash.AddInt32(identity.Value);
            hash.AddInt32(BitConverter.SingleToInt32Bits(position.X));
            hash.AddInt32(BitConverter.SingleToInt32Bits(position.Y));
            hash.AddInt32(BitConverter.SingleToInt32Bits(velocity.X));
            hash.AddInt32(BitConverter.SingleToInt32Bits(velocity.Y));
            hash.AddInt32(health.Value);
            hash.AddInt32(activity.Value);
            hash.AddBoolean(velocityEnabled);
            if (_isWide[slot])
            {
                if (!_world.TryRead(entity, _wideLookup, out SoakWide wide))
                    throw new InvalidOperationException("Canonical state lost a wide component.");
                hash.AddInt64(wide.A);
                hash.AddInt64(wide.B);
                hash.AddInt64(wide.C);
                hash.AddInt64(wide.D);
                hash.AddInt64(wide.E);
                hash.AddInt64(wide.F);
                hash.AddInt64(wide.G);
                hash.AddInt64(wide.H);
            }
            else if (_world.Has(entity, _wideLookup))
            {
                throw new InvalidOperationException("Canonical state found a wide component on the base archetype.");
            }
        }

        ArchetypeChunkSnapshot baseSnapshot = _configuration.UsePublicJobs
            ? _world.GetArchetypeSnapshot(_baseArchetype)
            : _baseStorage!.Store.GetSnapshot();
        ArchetypeChunkSnapshot wideSnapshot = _configuration.UsePublicJobs
            ? _world.GetArchetypeSnapshot(_wideArchetype)
            : _wideStorage!.Store.GetSnapshot();
        hash.AddByte(0xA7);
        hash.AddInt32(baseSnapshot.EntityCount);
        hash.AddInt32(wideSnapshot.EntityCount);
        return hash.Finish();
    }

    internal SoakStoreReport[] CaptureStores() => _configuration.UsePublicJobs
        ?
        [
            CaptureStore(
                "Base",
                _world.GetArchetypeSnapshot(_baseArchetype),
                _world.GetArchetypeArenaSnapshot(_baseArchetype)),
            CaptureStore(
                "Wide",
                _world.GetArchetypeSnapshot(_wideArchetype),
                _world.GetArchetypeArenaSnapshot(_wideArchetype)),
        ]
        :
        [
            CaptureStore("Base", _baseStorage!),
            CaptureStore("Wide", _wideStorage!),
        ];

    public void Dispose() => _world.Dispose();

    internal SchedulerRuntimeSnapshot GetSchedulerSnapshot() =>
        _configuration.UseScheduler
            ? _world.GetSchedulerRuntimeSnapshot()
            : throw new InvalidOperationException("This soak world does not use the scheduler.");

    internal long SchedulerReservedBytes => _configuration.UseScheduler
        ? _world.SchedulerReservedBytes
        : 0;

    private static EcsSchedulerOptions CreateSchedulerOptions(
        MixedOperationSoakConfiguration configuration) => configuration.UseScheduler
        ? new EcsSchedulerOptions(
            maximumSystems: 7,
            maximumDependencies: 0,
            maximumWorkItems: 256,
            maximumStructuralSegments: 256,
            backgroundWorkerCount: configuration.SchedulerBackgroundWorkers,
            callerParticipates: true,
            structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 64,
            profilingEnabled: true)
        : EcsSchedulerOptions.Disabled;

    private void RegisterScheduledSystems(ComponentId health, ComponentId activity)
    {
        _world.RegisterReadWriteSystem<SoakVelocity, SoakPosition,
            ScheduledSoakQueryRuntime, ScheduledSoakQueryJob>(
            ScheduledQueryDefinition("soak.parity.movement", SystemStage.Movement),
            _publicMovementPlan,
            new ScheduledSoakQueryRuntime(this, ScheduledSoakQueryKind.Movement));
        _world.RegisterReadWriteSystem<SoakPosition, SoakHealth,
            ScheduledSoakQueryRuntime, ScheduledSoakQueryJob>(
            ScheduledQueryDefinition("soak.parity.health", SystemStage.SpatialIndex),
            _publicHealthPlan,
            new ScheduledSoakQueryRuntime(this, ScheduledSoakQueryKind.Health));
        _world.RegisterReadWriteSystem<SoakHealth, SoakVelocity,
            ScheduledSoakQueryRuntime, ScheduledSoakQueryJob>(
            ScheduledQueryDefinition("soak.parity.response", SystemStage.Perception),
            _publicResponsePlan,
            new ScheduledSoakQueryRuntime(this, ScheduledSoakQueryKind.Response));
        _world.RegisterReadReadSystem<SoakPosition, SoakHealth,
            ScheduledSoakQueryRuntime, ScheduledSoakQueryJob>(
            ScheduledQueryDefinition("soak.parity.telemetry", SystemStage.Combat),
            _publicTelemetryPlan,
            new ScheduledSoakQueryRuntime(this, ScheduledSoakQueryKind.Telemetry));
        _world.RegisterRangeSystem<ScheduledSoakRandomReadRuntime, ScheduledSoakRandomReadJob>(
            new SystemDefinition("soak.parity.random-read", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalReads: [health], parallelRowThreshold: 1,
                minimumRowsPerPartition: 64),
            new ScheduledSoakRandomReadRuntime(this));
        _world.RegisterRangeSystem<ScheduledSoakTrackedWriteRuntime, ScheduledSoakTrackedWriteJob>(
            new SystemDefinition("soak.parity.tracked-write", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalWrites: [activity], parallelRowThreshold: 1,
                minimumRowsPerPartition: 256,
                parallelRandomAccessPolicy: ParallelRandomAccessPolicy.PartitionDisjoint),
            new ScheduledSoakTrackedWriteRuntime(this));
        _world.RegisterChangedRowsSystem<SoakIdentity, SoakActivity,
            ScheduledSoakChangedRuntime, ScheduledSoakChangedJob>(
            new SystemDefinition("soak.parity.changed", SystemStage.Networking,
                executionPolicy: SystemExecutionPolicy.Serial),
            _publicChangedPlan,
            new ScheduledSoakChangedRuntime(this));
    }

    private static SystemDefinition ScheduledQueryDefinition(string key, SystemStage stage) =>
        new(key, stage, executionPolicy: SystemExecutionPolicy.Auto,
            parallelRowThreshold: 8_192, minimumRowsPerPartition: 2_048);

    private void CompleteScheduledQuery(
        ScheduledSoakQueryKind kind,
        ReadOnlySpan<ScheduledSoakQueryJob> jobs)
    {
        int rows = 0;
        long checksum = 0;
        for (int index = 0; index < jobs.Length; index++)
        {
            rows += jobs[index].Rows;
            checksum = unchecked(checksum + jobs[index].Checksum);
        }
        if (rows != _configuration.EntityCount)
            throw new InvalidOperationException($"Scheduled parity query {kind} lost rows.");
        _queryRowsVisited += rows;
        _checksum = unchecked(_checksum + checksum);
    }

    private enum ScheduledSoakQueryKind : byte
    {
        Movement,
        Health,
        Response,
        Telemetry,
    }

    private sealed class ScheduledSoakQueryRuntime : ISystemRuntime<ScheduledSoakQueryJob>
    {
        private readonly MixedOperationSoakWorld _owner;
        private readonly ScheduledSoakQueryKind _kind;
        internal ScheduledSoakQueryRuntime(
            MixedOperationSoakWorld owner,
            ScheduledSoakQueryKind kind) => (_owner, _kind) = (owner, kind);
        public ScheduledSoakQueryJob Prepare(in SystemPrepareContext context) => new(_kind);
        public void Complete(in SystemCompleteContext context,
            ReadOnlySpan<ScheduledSoakQueryJob> jobs) => _owner.CompleteScheduledQuery(_kind, jobs);
    }

    private struct ScheduledSoakQueryJob :
        IScheduledReadWriteChunkJob<SoakVelocity, SoakPosition>,
        IScheduledReadWriteChunkJob<SoakPosition, SoakHealth>,
        IScheduledReadWriteChunkJob<SoakHealth, SoakVelocity>,
        IScheduledReadReadChunkJob<SoakPosition, SoakHealth>
    {
        private readonly ScheduledSoakQueryKind _kind;
        internal int Rows;
        internal long Checksum;
        internal ScheduledSoakQueryJob(ScheduledSoakQueryKind kind) =>
            (_kind, Rows, Checksum) = (kind, 0, 0);

        public void Execute(in SystemJobContext context,
            in ReadWriteComponentChunk<SoakVelocity, SoakPosition> chunk)
        {
            System.Diagnostics.Debug.Assert(_kind == ScheduledSoakQueryKind.Movement);
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                ref readonly SoakVelocity velocity = ref chunk.ReadComponents[row];
                ref SoakPosition position = ref chunk.WriteComponents[row];
                if (chunk.IsReadEnabled(row))
                {
                    position.X += velocity.X * (1f / 64f);
                    position.Y += velocity.Y * (1f / 64f);
                }
                checksum = unchecked(checksum +
                    BitConverter.SingleToInt32Bits(position.X) +
                    BitConverter.SingleToInt32Bits(position.Y));
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }

        public void Execute(in SystemJobContext context,
            in ReadWriteComponentChunk<SoakPosition, SoakHealth> chunk)
        {
            System.Diagnostics.Debug.Assert(_kind == ScheduledSoakQueryKind.Health);
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                ref readonly SoakPosition position = ref chunk.ReadComponents[row];
                uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(position.X));
                int value = 512 + unchecked((int)((bits ^ (bits >> 11)) & 511));
                chunk.WriteComponents[row] = new SoakHealth { Value = value };
                checksum = unchecked(checksum + value);
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }

        public void Execute(in SystemJobContext context,
            in ReadWriteComponentChunk<SoakHealth, SoakVelocity> chunk)
        {
            System.Diagnostics.Debug.Assert(_kind == ScheduledSoakQueryKind.Response);
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                int health = chunk.ReadComponents[row].Value;
                chunk.WriteComponents[row] = new SoakVelocity
                {
                    X = (health & 255) * (1f / 1024f),
                    Y = ((health >> 2) & 255) * (-1f / 2048f),
                };
                checksum = unchecked(checksum + health + (chunk.IsWriteEnabled(row) ? 1 : 0));
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }

        public void Execute(in SystemJobContext context,
            in ReadReadComponentChunk<SoakPosition, SoakHealth> chunk)
        {
            System.Diagnostics.Debug.Assert(_kind == ScheduledSoakQueryKind.Telemetry);
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                checksum = unchecked(checksum +
                    BitConverter.SingleToInt32Bits(chunk.ReadComponents1[row].X) +
                    chunk.ReadComponents2[row].Value);
            }
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private sealed class ScheduledSoakRandomReadRuntime :
        IRangeSystemRuntime<ScheduledSoakRandomReadJob>
    {
        private readonly MixedOperationSoakWorld _owner;
        internal ScheduledSoakRandomReadRuntime(MixedOperationSoakWorld owner) => _owner = owner;
        public int GetRangeCount(in SystemPrepareContext context) => _owner._randomReadSlots.Length;
        public ScheduledSoakRandomReadJob Prepare(in SystemPrepareContext context)
        {
            _owner.SelectRandomReadTargets();
            return new ScheduledSoakRandomReadJob(_owner);
        }
        public void Complete(in SystemCompleteContext context,
            ReadOnlySpan<ScheduledSoakRandomReadJob> jobs)
        {
            long checksum = 0;
            int rows = 0;
            for (int index = 0; index < jobs.Length; index++)
            {
                checksum = unchecked(checksum + jobs[index].Checksum);
                rows += jobs[index].Rows;
            }
            if (rows != _owner._randomReadSlots.Length)
                throw new InvalidOperationException("Scheduled random reads lost targets.");
            _owner._randomReads += rows;
            _owner._checksum = unchecked(_owner._checksum + checksum);
        }
    }

    private struct ScheduledSoakRandomReadJob : IRangeJob
    {
        private readonly MixedOperationSoakWorld _owner;
        internal int Rows;
        internal long Checksum;
        internal ScheduledSoakRandomReadJob(MixedOperationSoakWorld owner) =>
            (_owner, Rows, Checksum) = (owner, 0, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            long checksum = Checksum;
            for (int operation = start; operation < start + count; operation++)
            {
                int slot = _owner._randomReadSlots[operation];
                if (!context.TryRead(_owner._entities[slot], _owner._healthLookup, out SoakHealth health))
                    throw new InvalidOperationException("A scheduled random read lost a live entity.");
                checksum = unchecked(checksum + health.Value + slot);
            }
            Rows += count;
            Checksum = checksum;
        }
    }

    private sealed class ScheduledSoakTrackedWriteRuntime :
        IRangeSystemRuntime<ScheduledSoakTrackedWriteJob>
    {
        private readonly MixedOperationSoakWorld _owner;
        internal ScheduledSoakTrackedWriteRuntime(MixedOperationSoakWorld owner) => _owner = owner;
        public int GetRangeCount(in SystemPrepareContext context) => _owner._trackedWriteSlots.Length;
        public ScheduledSoakTrackedWriteJob Prepare(in SystemPrepareContext context)
        {
            _owner.SelectTrackedWriteTargets();
            return new ScheduledSoakTrackedWriteJob(_owner);
        }
        public void Complete(in SystemCompleteContext context,
            ReadOnlySpan<ScheduledSoakTrackedWriteJob> jobs)
        {
            int rows = 0;
            for (int index = 0; index < jobs.Length; index++) rows += jobs[index].Rows;
            if (rows != _owner._trackedWriteSlots.Length)
                throw new InvalidOperationException("Scheduled tracked writes lost targets.");
            _owner._trackedWrites += rows;
        }
    }

    private struct ScheduledSoakTrackedWriteJob : IRangeJob
    {
        private readonly MixedOperationSoakWorld _owner;
        internal int Rows;
        internal ScheduledSoakTrackedWriteJob(MixedOperationSoakWorld owner) =>
            (_owner, Rows) = (owner, 0);
        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int operation = start; operation < start + count; operation++)
            {
                int slot = _owner._trackedWriteSlots[operation];
                var value = new SoakActivity { Value = _owner.GetTrackedWriteValue(slot) };
                if (!context.TryWrite(_owner._entities[slot], _owner._activityLookup, value))
                    throw new InvalidOperationException("A scheduled tracked write lost a live entity.");
            }
            Rows += count;
        }
    }

    private sealed class ScheduledSoakChangedRuntime :
        IChangedRowsSystemRuntime<ScheduledSoakChangedJob>
    {
        private readonly MixedOperationSoakWorld _owner;
        internal ScheduledSoakChangedRuntime(MixedOperationSoakWorld owner) => _owner = owner;
        public ChangeCursor Cursor
        {
            get => _owner._changeCursor;
            set => _owner._changeCursor = value;
        }
        public ScheduledSoakChangedJob Prepare(in SystemPrepareContext context) => default;
        public void Complete(in SystemCompleteContext context,
            ReadOnlySpan<ScheduledSoakChangedJob> jobs)
        {
            int rows = 0;
            long checksum = 0;
            for (int index = 0; index < jobs.Length; index++)
            {
                rows += jobs[index].Rows;
                checksum = unchecked(checksum + jobs[index].Checksum);
            }
            _owner._changedRowsDelivered += rows;
            _owner._checksum = unchecked(_owner._checksum + checksum + rows);
        }
    }

    private struct ScheduledSoakChangedJob : IScheduledChangedRowsChunkJob<SoakIdentity>
    {
        internal int Rows;
        internal long Checksum;
        public void Execute(in SystemJobContext context,
            in ReadOnlyComponentChunk<SoakIdentity> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
                checksum = unchecked(checksum + chunk.Components[row].Value);
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private void ExecuteRandomReads()
    {
        SelectRandomReadTargets();
        long checksum = 0;
        for (int operation = 0; operation < _configuration.RandomReadsPerTick; operation++)
        {
            int slot = _randomReadSlots[operation];
            if (!_world.TryRead(_entities[slot], _healthLookup, out SoakHealth health))
                throw new InvalidOperationException("A checked random read lost a live entity.");
            checksum = unchecked(checksum + health.Value + slot);
        }
        _randomReads += _configuration.RandomReadsPerTick;
        _checksum = unchecked(_checksum + checksum);
    }

    private void ExecuteTrackedWrites()
    {
        SelectTrackedWriteTargets();
        for (int written = 0; written < _trackedWriteSlots.Length; written++)
        {
            int slot = _trackedWriteSlots[written];
            int value = GetTrackedWriteValue(slot);
            if (!_world.TryWrite(_entities[slot], _activityLookup, new SoakActivity { Value = value }))
                throw new InvalidOperationException("A tracked scalar write lost a live entity.");
        }
        _trackedWrites += _trackedWriteSlots.Length;
    }

    private void SelectRandomReadTargets()
    {
        for (int operation = 0; operation < _randomReadSlots.Length; operation++)
            _randomReadSlots[operation] = _random.NextInt(_configuration.EntityCount);
    }

    private void SelectTrackedWriteTargets()
    {
        int epoch = NextEpoch(_writeMarks, ref _writeEpoch);
        int written = 0;
        while (written < _configuration.TrackedWritesPerTick)
        {
            int slot = _random.NextInt(_configuration.EntityCount);
            if (_writeMarks[slot] == epoch)
                continue;
            _writeMarks[slot] = epoch;
            _trackedWriteSlots[written++] = slot;
        }
    }

    private int GetTrackedWriteValue(int slot) => unchecked(
        (_tick + 1) * 1_000_003 ^ slot * 65_537 ^ _lifecycleRevisions[slot] * 257);

    private void SelectStructuralTargets()
    {
        int epoch = NextEpoch(_selectionMarks, ref _selectionEpoch);
        int destroyBase = _configuration.DestroyedPerTick / 2;
        int destroyWide = _configuration.DestroyedPerTick - destroyBase;
        int destroyIndex = 0;
        destroyIndex += SelectSlotsByArchetype(
            useWide: false,
            destroyBase,
            _destroySlots.AsSpan(destroyIndex),
            _destroyEntities.AsSpan(destroyIndex),
            epoch);
        destroyIndex += SelectSlotsByArchetype(
            useWide: true,
            destroyWide,
            _destroySlots.AsSpan(destroyIndex),
            _destroyEntities.AsSpan(destroyIndex),
            epoch);
        if (destroyIndex != _configuration.DestroyedPerTick)
            throw new InvalidOperationException("Destroy target selection produced the wrong count.");

        int halfTransitions = _configuration.TransitionsPerTick / 2;
        int addCount = SelectSlotsByArchetype(
            useWide: false,
            halfTransitions,
            _addWideSlots,
            _addWideEntities,
            epoch);
        int removeCount = SelectSlotsByArchetype(
            useWide: true,
            halfTransitions,
            _removeWideSlots,
            _removeWideEntities,
            epoch);
        if (addCount != halfTransitions || removeCount != halfTransitions)
            throw new InvalidOperationException("Transition target selection produced the wrong count.");
    }

    private int SelectSlotsByArchetype(
        bool useWide,
        int count,
        Span<int> slots,
        Span<Entity> entities,
        int epoch)
    {
        int selected = 0;
        while (selected < count)
        {
            int slot = _random.NextInt(_configuration.EntityCount);
            if (_selectionMarks[slot] == epoch || _isWide[slot] != useWide)
                continue;
            _selectionMarks[slot] = epoch;
            slots[selected] = slot;
            entities[selected] = _entities[slot];
            selected++;
        }
        return selected;
    }

    private void RecordStructuralCommands()
    {
        _commands.EnqueueDestroy(_destroyEntities);
        for (int index = 0; index < _destroySlots.Length; index++)
        {
            _createArchetypes[index] = _isWide[_destroySlots[index]]
                ? _wideArchetype
                : _baseArchetype;
        }
        _commands.EnqueueCreateDefault(_createArchetypes, _createTokens);
        _commands.EnqueueTransition(_removeWideEntities, _removeWide);
        _commands.EnqueueTransition(_addWideEntities, _addWide);
        int count = _commands.Count;
        if (count != _configuration.StructuralCommandsPerTick)
            throw new InvalidOperationException("The structural command count differs from the workload contract.");
        _structuralCommandHighWater = Math.Max(_structuralCommandHighWater, count);
        _commandsRecorded += count;
    }

    private void ValidateAndAccumulate(in StructuralPlaybackResult playback)
    {
        int halfTransitions = _configuration.TransitionsPerTick / 2;
        if (playback.CommandCount != _configuration.StructuralCommandsPerTick ||
            playback.DestroyedCount != _configuration.DestroyedPerTick ||
            playback.CreatedCount != _configuration.CreatedPerTick ||
            playback.RemovedCount != halfTransitions ||
            playback.AddedCount != halfTransitions ||
            playback.RejectedCount != 0)
        {
            throw new InvalidOperationException("Structural playback diverged from the mixed-workload contract.");
        }
        _commandsApplied += playback.AppliedCount;
        _commandsRejected += playback.RejectedCount;
        _temporaryHoles += playback.TemporaryHoleCount;
        _reusedTemporaryHoles += playback.ReusedTemporaryHoleCount;
        _compactedTemporaryHoles += playback.CompactedTemporaryHoleCount;
        _temporaryHoleDonorRelocations += playback.TemporaryHoleDonorRelocationCount;
        _reusedTemporaryHoleFixedRowBytes += playback.ReusedTemporaryHoleFixedRowBytes;
        _checksum = unchecked(_checksum + playback.AppliedCount + playback.ReusedTemporaryHoleCount);
    }

    private void PublishStructuralResults()
    {
        for (int index = 0; index < _destroySlots.Length; index++)
        {
            if (!_commands.TryResolve(_createTokens[index], out Entity entity))
                throw new InvalidOperationException("A successful deferred create did not resolve.");
            int slot = _destroySlots[index];
            int lifecycleRevision = checked(_lifecycleRevisions[slot] + 1);
            _lifecycleRevisions[slot] = lifecycleRevision;
            _entities[slot] = entity;
            InitializeEntity(slot, entity, _tick + 1, lifecycleRevision, _isWide[slot]);
        }
        for (int index = 0; index < _addWideSlots.Length; index++)
            _isWide[_addWideSlots[index]] = true;
        for (int index = 0; index < _removeWideSlots.Length; index++)
            _isWide[_removeWideSlots[index]] = false;
    }

    private void InitializeEntity(
        int slot,
        Entity entity,
        int tick,
        int lifecycleRevision,
        bool useWide)
    {
        int mixed = unchecked(slot * 397 ^ lifecycleRevision * 65_537 ^ tick * 17);
        var position = new SoakPosition
        {
            X = (mixed & 0xFFFF) * (1f / 256f),
            Y = ((mixed >> 8) & 0xFFFF) * (-1f / 512f),
        };
        var velocity = new SoakVelocity
        {
            X = ((mixed >> 3) & 1023) * (1f / 4096f),
            Y = ((mixed >> 13) & 1023) * (-1f / 4096f),
        };
        var health = new SoakHealth { Value = 500 + (mixed & 511) };
        var activity = new SoakActivity { Value = mixed };
        if (!_world.TryWrite(entity, _identityLookup, new SoakIdentity { Value = slot }) ||
            !_world.TryWrite(entity, _positionLookup, position) ||
            !_world.TryWrite(entity, _velocityLookup, velocity) ||
            !_world.TryWrite(entity, _healthLookup, health) ||
            !_world.TryWrite(entity, _activityLookup, activity) ||
            !_world.TrySetEnabled(entity, _velocityLookup, (mixed & 3) != 0))
        {
            throw new InvalidOperationException("Entity initialization lost a required component.");
        }
        if (useWide)
        {
            long value = unchecked(((long)mixed << 32) | (uint)slot);
            if (!_world.TryWrite(entity, _wideLookup, new SoakWide
                {
                    A = value + 1,
                    B = value + 2,
                    C = value + 3,
                    D = value + 4,
                    E = value + 5,
                    F = value + 6,
                    G = value + 7,
                    H = value + 8,
                }))
            {
                throw new InvalidOperationException("Wide entity initialization lost its wide component.");
            }
        }
    }

    private static int NextEpoch(int[] marks, ref int epoch)
    {
        if (epoch == int.MaxValue)
        {
            Array.Clear(marks);
            epoch = 1;
        }
        else
        {
            epoch++;
        }
        return epoch;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Record(long[]? samples, int index, long started)
    {
        if (samples is not null)
            samples[index] = Stopwatch.GetTimestamp() - started;
    }

    private static ComponentId[] CreateSortedSignature(params ComponentId[] componentIds)
    {
        Array.Sort(componentIds, static (left, right) => left.Value.CompareTo(right.Value));
        return componentIds;
    }

    private static SoakStoreReport CaptureStore(
        string name,
        ArchetypeStorage<NativeChunkArena> storage)
    {
        ArchetypeChunkSnapshot store = storage.Store.GetSnapshot();
        ChunkArenaSnapshot arena = storage.Store.Arena.GetSnapshot();
        return CaptureStore(name, store, arena);
    }

    private static SoakStoreReport CaptureStore(
        string name,
        ArchetypeChunkSnapshot store,
        ChunkArenaSnapshot arena)
    {
        return new SoakStoreReport
        {
            Name = name,
            EntityCount = store.EntityCount,
            RowCapacity = store.RowCapacity,
            MaximumChunks = store.MaximumChunks,
            AcquiredChunks = store.AcquiredChunkCount,
            OccupiedChunks = store.OccupiedChunkCount,
            FullChunks = store.FullChunkCount,
            PartialChunks = store.PartialChunkCount,
            EmptyChunks = store.EmptyChunkCount,
            UnusedAcquiredRows = store.UnusedAcquiredRows,
            ChunkMetadataBytes = store.ChunkMetadataBytes,
            DenseChangeMetadataBytes = store.ChangeVersionMetadataBytes,
            ExactChangeMetadataBytes = store.SparseRowChangeVersionMetadataBytes,
            NativePayloadReservedBytes = arena.PayloadBytesInUse,
            NativeChunkHighWater = arena.HighWatermark,
        };
    }

    private struct MovementVisitor : IComponentChunkReadWriteVisitor<SoakVelocity, SoakPosition>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadWriteComponentChunk<SoakVelocity, SoakPosition> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                ref readonly SoakVelocity velocity = ref chunk.ReadComponents[row];
                ref SoakPosition position = ref chunk.WriteComponents[row];
                if (chunk.IsReadEnabled(row))
                {
                    position.X += velocity.X * (1f / 64f);
                    position.Y += velocity.Y * (1f / 64f);
                }
                checksum = unchecked(checksum +
                    BitConverter.SingleToInt32Bits(position.X) +
                    BitConverter.SingleToInt32Bits(position.Y));
            }
            Checksum = checksum;
        }
    }

    private struct HealthVisitor : IComponentChunkReadWriteVisitor<SoakPosition, SoakHealth>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadWriteComponentChunk<SoakPosition, SoakHealth> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                ref readonly SoakPosition position = ref chunk.ReadComponents[row];
                uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(position.X));
                int value = 512 + unchecked((int)((bits ^ (bits >> 11)) & 511));
                chunk.WriteComponents[row] = new SoakHealth { Value = value };
                checksum = unchecked(checksum + value);
            }
            Checksum = checksum;
        }
    }

    private struct ResponseVisitor : IComponentChunkReadWriteVisitor<SoakHealth, SoakVelocity>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadWriteComponentChunk<SoakHealth, SoakVelocity> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                int health = chunk.ReadComponents[row].Value;
                chunk.WriteComponents[row] = new SoakVelocity
                {
                    X = (health & 255) * (1f / 1024f),
                    Y = ((health >> 2) & 255) * (-1f / 2048f),
                };
                checksum = unchecked(checksum + health + (chunk.IsWriteEnabled(row) ? 1 : 0));
            }
            Checksum = checksum;
        }
    }

    private struct TelemetryVisitor : IComponentChunkReadReadVisitor<SoakPosition, SoakHealth>
    {
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadReadComponentChunk<SoakPosition, SoakHealth> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                checksum = unchecked(checksum +
                    BitConverter.SingleToInt32Bits(chunk.ReadComponents1[row].X) +
                    chunk.ReadComponents2[row].Value);
            }
            Checksum = checksum;
        }
    }

    private struct ChangedIdentityVisitor : IComponentChunkReadVisitor<SoakIdentity>
    {
        internal int Rows;
        internal long Checksum;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Execute(in ReadOnlyComponentChunk<SoakIdentity> chunk)
        {
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
                checksum = unchecked(checksum + chunk.Components[row].Value);
            Rows += chunk.Count;
            Checksum = checksum;
        }
    }

    private struct IdentityOwnershipVisitor : IComponentChunkReadVisitor<SoakIdentity>
    {
        private readonly Entity[] _entities;
        private readonly int[] _seen;

        internal IdentityOwnershipVisitor(Entity[] entities, int[] seen)
        {
            _entities = entities;
            _seen = seen;
            Rows = 0;
        }

        internal int Rows;

        public void Execute(in ReadOnlyComponentChunk<SoakIdentity> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                int logical = chunk.Components[row].Value;
                if (unchecked((uint)logical) >= unchecked((uint)_entities.Length) ||
                    _entities[logical].Index != chunk.EntityIndices[row])
                {
                    throw new InvalidOperationException("Canonical ownership does not match the entity location table.");
                }
                _seen[logical]++;
            }
            Rows += chunk.Count;
        }
    }

    private struct PublicIdentityOwnershipJob : IReadReadChunkJob<SoakIdentity, SoakPosition>
    {
        private readonly Entity[] _entities;
        private readonly int[] _seen;

        internal PublicIdentityOwnershipJob(Entity[] entities, int[] seen)
        {
            _entities = entities;
            _seen = seen;
            Rows = 0;
        }

        internal int Rows;

        public void Execute(in ReadReadComponentChunk<SoakIdentity, SoakPosition> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                int logical = chunk.ReadComponents1[row].Value;
                if (unchecked((uint)logical) >= unchecked((uint)_entities.Length) ||
                    _entities[logical].Index != chunk.EntityIndices[row])
                {
                    throw new InvalidOperationException(
                        "Canonical public ownership does not match the entity location table.");
                }
                _seen[logical]++;
            }
            Rows += chunk.Count;
        }
    }
}

internal sealed class PhaseSamples
{
    internal PhaseSamples(int count)
    {
        WorkloadSelection = new long[count];
        Query = new long[count];
        RandomLookup = new long[count];
        TrackedWrite = new long[count];
        ChangeConsume = new long[count];
        CommandRecord = new long[count];
        StructuralPlayback = new long[count];
        CreateInitialization = new long[count];
        MeasuredEcsWork = new long[count];
        TotalTick = new long[count];
    }

    internal long[] WorkloadSelection { get; }
    internal long[] Query { get; }
    internal long[] RandomLookup { get; }
    internal long[] TrackedWrite { get; }
    internal long[] ChangeConsume { get; }
    internal long[] CommandRecord { get; }
    internal long[] StructuralPlayback { get; }
    internal long[] CreateInitialization { get; }
    internal long[] MeasuredEcsWork { get; }
    internal long[] TotalTick { get; }

    internal SoakPhaseReport[] CreateReports(
        MixedOperationSoakConfiguration configuration,
        double changedRowsPerTick) =>
    [
        SoakPhaseReport.Create("WorkloadSelection", WorkloadSelection),
        SoakPhaseReport.Create("Queries", Query,
            operationsPerTick: configuration.QueryRowsPerTick,
            logicalBytesPerTick: checked((long)configuration.EntityCount * 52)),
        SoakPhaseReport.Create("RandomLookup", RandomLookup,
            operationsPerTick: configuration.RandomReadsPerTick),
        SoakPhaseReport.Create("TrackedWrite", TrackedWrite,
            operationsPerTick: configuration.TrackedWritesPerTick,
            logicalBytesPerTick: checked((long)configuration.TrackedWritesPerTick * Unsafe.SizeOf<SoakActivity>())),
        SoakPhaseReport.Create("ExactChangeConsume", ChangeConsume,
            operationsPerTick: changedRowsPerTick,
            logicalBytesPerTick: changedRowsPerTick * Unsafe.SizeOf<SoakIdentity>()),
        SoakPhaseReport.Create("CommandRecord", CommandRecord,
            operationsPerTick: configuration.StructuralCommandsPerTick),
        SoakPhaseReport.Create("StructuralPlayback", StructuralPlayback,
            operationsPerTick: configuration.StructuralCommandsPerTick),
        SoakPhaseReport.Create("CreateInitialization", CreateInitialization,
            operationsPerTick: configuration.CreatedPerTick),
        SoakPhaseReport.Create("MeasuredEcsWork", MeasuredEcsWork),
        SoakPhaseReport.Create("TotalTick", TotalTick),
    ];

    internal static SoakPhaseReport[] Combine(
        PhaseSamples[] runs,
        MixedOperationSoakConfiguration configuration,
        double changedRowsPerTick)
    {
        if (runs.Length == 0)
            return [];
        return
        [
            CombinePhase("WorkloadSelection", runs, static run => run.WorkloadSelection),
            CombinePhase("Queries", runs, static run => run.Query,
                configuration.QueryRowsPerTick,
                checked((long)configuration.EntityCount * 52)),
            CombinePhase("RandomLookup", runs, static run => run.RandomLookup,
                configuration.RandomReadsPerTick),
            CombinePhase("TrackedWrite", runs, static run => run.TrackedWrite,
                configuration.TrackedWritesPerTick,
                checked((long)configuration.TrackedWritesPerTick * Unsafe.SizeOf<SoakActivity>())),
            CombinePhase("ExactChangeConsume", runs, static run => run.ChangeConsume,
                changedRowsPerTick,
                changedRowsPerTick * Unsafe.SizeOf<SoakIdentity>()),
            CombinePhase("CommandRecord", runs, static run => run.CommandRecord,
                configuration.StructuralCommandsPerTick),
            CombinePhase("StructuralPlayback", runs, static run => run.StructuralPlayback,
                configuration.StructuralCommandsPerTick),
            CombinePhase("CreateInitialization", runs, static run => run.CreateInitialization,
                configuration.CreatedPerTick),
            CombinePhase("MeasuredEcsWork", runs, static run => run.MeasuredEcsWork),
            CombinePhase("TotalTick", runs, static run => run.TotalTick),
        ];
    }

    private static SoakPhaseReport CombinePhase(
        string name,
        PhaseSamples[] runs,
        Func<PhaseSamples, long[]> selector,
        double operationsPerTick = 0,
        double logicalBytesPerTick = 0)
    {
        int length = runs.Sum(run => selector(run).Length);
        var combined = new long[length];
        int offset = 0;
        foreach (PhaseSamples run in runs)
        {
            long[] source = selector(run);
            source.CopyTo(combined, offset);
            offset += source.Length;
        }
        return SoakPhaseReport.Create(name, combined, operationsPerTick, logicalBytesPerTick);
    }
}

internal readonly record struct SoakCounters(
    long QueryRowsVisited,
    long RandomReads,
    long TrackedWrites,
    long ChangedRowsDelivered,
    long CommandsRecorded,
    long CommandsApplied,
    long CommandsRejected,
    long TemporaryHoles,
    long ReusedTemporaryHoles,
    long CompactedTemporaryHoles,
    long TemporaryHoleDonorRelocations,
    long ReusedTemporaryHoleFixedRowBytes)
{
    public static SoakCounters operator -(SoakCounters left, SoakCounters right) => new(
        left.QueryRowsVisited - right.QueryRowsVisited,
        left.RandomReads - right.RandomReads,
        left.TrackedWrites - right.TrackedWrites,
        left.ChangedRowsDelivered - right.ChangedRowsDelivered,
        left.CommandsRecorded - right.CommandsRecorded,
        left.CommandsApplied - right.CommandsApplied,
        left.CommandsRejected - right.CommandsRejected,
        left.TemporaryHoles - right.TemporaryHoles,
        left.ReusedTemporaryHoles - right.ReusedTemporaryHoles,
        left.CompactedTemporaryHoles - right.CompactedTemporaryHoles,
        left.TemporaryHoleDonorRelocations - right.TemporaryHoleDonorRelocations,
        left.ReusedTemporaryHoleFixedRowBytes - right.ReusedTemporaryHoleFixedRowBytes);
}

internal struct DeterministicRandom
{
    private ulong _state;

    internal DeterministicRandom(ulong seed)
    {
        _state = seed == 0 ? MixedOperationSoakConfiguration.DefaultSeed : seed;
    }

    internal int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        ulong state = _state;
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        _state = state;
        ulong value = unchecked(state * 2685821657736338717UL);
        return (int)(value % unchecked((uint)exclusiveMaximum));
    }
}

internal sealed class MixedOperationSoakReport
{
    public required string SuiteName { get; init; }
    public required string ReportFormat { get; init; }
    public required string WorkloadProfile { get; init; }
    public required string CanonicalStateProfile { get; init; }
    public required string KernelVersion { get; init; }
    public required string SemanticProfile { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required MixedOperationSoakConfigurationReport Configuration { get; init; }
    public required SoakEnvironmentReport Environment { get; init; }
    public required ReplayValidationReport ReplayValidation { get; init; }
    public required bool MeasuredReplayMatched { get; init; }
    public required string FinalCanonicalHash { get; init; }
    public required IReadOnlyList<MixedOperationSoakRunReport> Runs { get; init; }
    public required IReadOnlyList<SoakPhaseReport> CombinedPhases { get; init; }
    public required SoakInterpretation Interpretation { get; init; }
}

internal sealed class MixedOperationSoakConfigurationReport
{
    public required string ExecutionSurface { get; init; }
    public required int EntityCount { get; init; }
    public required int WarmupTicks { get; init; }
    public required int MeasuredTicks { get; init; }
    public required int MeasuredRuns { get; init; }
    public required int ValidationTicks { get; init; }
    public required int ValidationCheckpointInterval { get; init; }
    public required ulong Seed { get; init; }
    public required int QueryPassesPerTick { get; init; }
    public required int QueryRowsPerTick { get; init; }
    public required long LogicalQueryBytesPerTick { get; init; }
    public required int TrackedWritesPerTick { get; init; }
    public required int RandomReadsPerTick { get; init; }
    public required int DestroyedPerTick { get; init; }
    public required int CreatedPerTick { get; init; }
    public required int TransitionsPerTick { get; init; }
    public required int StructuralCommandsPerTick { get; init; }
}

internal sealed class ReplayValidationReport
{
    public required int Ticks { get; init; }
    public required int CheckpointInterval { get; init; }
    public required int CheckpointCount { get; init; }
    public required bool Matched { get; init; }
    public required IReadOnlyList<ReplayCheckpoint> Checkpoints { get; init; }
}

internal sealed class ReplayCheckpoint
{
    public required int Tick { get; init; }
    public required string Hash { get; init; }
}

internal sealed class MixedOperationSoakRunReport
{
    public required int RunIndex { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required double TicksPerSecond { get; init; }
    public required long Checksum { get; init; }
    public required string FinalCanonicalHash { get; init; }
    public required int AliveCount { get; init; }
    public required ulong ChangeVersion { get; init; }
    public required long QueryRowsVisited { get; init; }
    public required long RandomReads { get; init; }
    public required long TrackedWrites { get; init; }
    public required long ChangedRowsDelivered { get; init; }
    public required long CommandsRecorded { get; init; }
    public required long CommandsApplied { get; init; }
    public required long CommandsRejected { get; init; }
    public required long TemporaryHoles { get; init; }
    public required long ReusedTemporaryHoles { get; init; }
    public required long CompactedTemporaryHoles { get; init; }
    public required long TemporaryHoleDonorRelocations { get; init; }
    public required long ReusedTemporaryHoleFixedRowBytes { get; init; }
    public required int StructuralCommandCapacity { get; init; }
    public required int StructuralCommandHighWater { get; init; }
    public required long StructuralCommandReservedBytes { get; init; }
    public required long TimedManagedAllocationBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long ManagedHeapBeforeBytes { get; init; }
    public required long ManagedHeapAfterBytes { get; init; }
    public required long RetainedManagedBeforeBytes { get; init; }
    public required long RetainedManagedAfterBytes { get; init; }
    public required long WorkingSetBeforeBytes { get; init; }
    public required long WorkingSetAfterBytes { get; init; }
    public required long PeakWorkingSetBytes { get; init; }
    public required IReadOnlyList<SoakStoreReport> StoresBefore { get; init; }
    public required IReadOnlyList<SoakStoreReport> StoresAfter { get; init; }
    public required IReadOnlyList<SoakPhaseReport> Phases { get; init; }
}

internal sealed class SoakPhaseReport
{
    public required string Name { get; init; }
    public required int Samples { get; init; }
    public required double MeanMicroseconds { get; init; }
    public required double StandardDeviationMicroseconds { get; init; }
    public required double MinMicroseconds { get; init; }
    public required double P50Microseconds { get; init; }
    public required double P95Microseconds { get; init; }
    public required double P99Microseconds { get; init; }
    public required double P999Microseconds { get; init; }
    public required double MaxMicroseconds { get; init; }
    public double MeanOperationsPerTick { get; init; }
    public double MeanNanosecondsPerOperation { get; init; }
    public double LogicalBytesPerTick { get; init; }
    public double LogicalGigabytesPerSecond { get; init; }

    internal static SoakPhaseReport Create(
        string name,
        long[] stopwatchTicks,
        double operationsPerTick = 0,
        double logicalBytesPerTick = 0)
    {
        if (stopwatchTicks.Length == 0)
            throw new ArgumentException("At least one timing sample is required.", nameof(stopwatchTicks));
        var sorted = stopwatchTicks.ToArray();
        Array.Sort(sorted);
        double tickToMicroseconds = 1_000_000d / Stopwatch.Frequency;
        double meanTicks = stopwatchTicks.Average(static value => (double)value);
        double variance = 0;
        for (int index = 0; index < stopwatchTicks.Length; index++)
        {
            double difference = stopwatchTicks[index] - meanTicks;
            variance += difference * difference;
        }
        variance /= stopwatchTicks.Length;
        double meanMicroseconds = meanTicks * tickToMicroseconds;
        return new SoakPhaseReport
        {
            Name = name,
            Samples = stopwatchTicks.Length,
            MeanMicroseconds = meanMicroseconds,
            StandardDeviationMicroseconds = Math.Sqrt(variance) * tickToMicroseconds,
            MinMicroseconds = sorted[0] * tickToMicroseconds,
            P50Microseconds = Percentile(sorted, 0.50) * tickToMicroseconds,
            P95Microseconds = Percentile(sorted, 0.95) * tickToMicroseconds,
            P99Microseconds = Percentile(sorted, 0.99) * tickToMicroseconds,
            P999Microseconds = Percentile(sorted, 0.999) * tickToMicroseconds,
            MaxMicroseconds = sorted[^1] * tickToMicroseconds,
            MeanOperationsPerTick = operationsPerTick,
            MeanNanosecondsPerOperation = operationsPerTick == 0
                ? 0
                : meanMicroseconds * 1_000d / operationsPerTick,
            LogicalBytesPerTick = logicalBytesPerTick,
            LogicalGigabytesPerSecond = logicalBytesPerTick == 0 || meanMicroseconds == 0
                ? 0
                : logicalBytesPerTick / (meanMicroseconds * 1_000d),
        };
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

internal sealed class SoakStoreReport
{
    public required string Name { get; init; }
    public required int EntityCount { get; init; }
    public required int RowCapacity { get; init; }
    public required int MaximumChunks { get; init; }
    public required int AcquiredChunks { get; init; }
    public required int OccupiedChunks { get; init; }
    public required int FullChunks { get; init; }
    public required int PartialChunks { get; init; }
    public required int EmptyChunks { get; init; }
    public required long UnusedAcquiredRows { get; init; }
    public required long ChunkMetadataBytes { get; init; }
    public required long DenseChangeMetadataBytes { get; init; }
    public required long ExactChangeMetadataBytes { get; init; }
    public required long NativePayloadReservedBytes { get; init; }
    public required int NativeChunkHighWater { get; init; }
}

internal sealed class SoakEnvironmentReport
{
    public required string RuntimeVersion { get; init; }
    public required string FrameworkDescription { get; init; }
    public required string Configuration { get; init; }
    public required string OperatingSystem { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required int ProcessorCount { get; init; }
    public required string CpuModel { get; init; }
    public required string GitCommit { get; init; }
    public required bool GitDirty { get; init; }
    public required string? DotnetTieredCompilation { get; init; }
    public required string? DotnetReadyToRun { get; init; }
    public required long StopwatchFrequency { get; init; }

    internal static SoakEnvironmentReport Capture()
    {
        (string commit, bool dirty) = CaptureGit();
        return new SoakEnvironmentReport
        {
            RuntimeVersion = Environment.Version.ToString(),
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
#if DEBUG
            Configuration = "Debug",
#else
            Configuration = "Release",
#endif
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            CpuModel = CaptureCpuModel(),
            GitCommit = commit,
            GitDirty = dirty,
            DotnetTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            DotnetReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
            StopwatchFrequency = Stopwatch.Frequency,
        };
    }

    private static string CaptureCpuModel()
    {
        const string cpuInfo = "/proc/cpuinfo";
        if (!File.Exists(cpuInfo))
            return "unknown";
        foreach (string line in File.ReadLines(cpuInfo))
        {
            if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
            {
                int separator = line.IndexOf(':');
                return separator >= 0 ? line[(separator + 1)..].Trim() : line.Trim();
            }
        }
        return "unknown";
    }

    private static (string Commit, bool Dirty) CaptureGit()
    {
        try
        {
            string commit = RunGit("rev-parse HEAD");
            string status = RunGit("status --porcelain");
            return (commit, status.Length != 0);
        }
        catch
        {
            return ("unknown", true);
        }
    }

    private static string RunGit(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Unable to start git.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        return output.Trim();
    }
}

internal sealed class SoakInterpretation
{
    public required double TickBudgetMilliseconds { get; init; }
    public required double EcsInfrastructureTargetMilliseconds { get; init; }
    public required bool QueryTimingIncludesUsefulComponentArithmetic { get; init; }
    public required bool SelectionTimingExcludedFromMeasuredEcsWork { get; init; }
    public required bool CanonicalHashTimingExcludedFromTicks { get; init; }
    public required bool NaturalGc { get; init; }
    public required string Notes { get; init; }
}

internal readonly record struct MixedOperationSoakExecution(
    MixedOperationSoakRunReport Report,
    PhaseSamples Samples);
