using System.Diagnostics;
using System.Text.Json;
using static UltraSim.ECS.Benchmarks.SchedulerBenchmarkWorlds;

namespace UltraSim.ECS.Benchmarks;

internal static class SchedulerBenchmarkProgram
{
    internal static int Run(string[] args)
    {
        string output = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "results",
            $"ecs-{EcsKernelVersion.Value}-scheduler.json"));
        bool quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
            if ((args[index] is "--output" or "--scheduler-output") && index + 1 < args.Length)
                output = Path.GetFullPath(args[++index]);

        SchedulerBenchmarkReport report = SchedulerBenchmarkRunner.Run(quick);
        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ECS {EcsKernelVersion.Value} bounded scheduler admission");
        foreach (SchedulerTiming result in report.Timings)
            Console.WriteLine($"{result.Category}/{result.Name}: mean={result.MeanMicroseconds:F3} us p50={result.P50Microseconds:F3} us p95={result.P95Microseconds:F3} us p99={result.P99Microseconds:F3} us alloc={result.AllocatedBytes} B hash={result.CanonicalHash:X16}");
        Console.WriteLine($"allocation-free={report.AllocationFree} canonical={report.CanonicalChecksPassed} results={output}");
        return report.AllocationFree && report.CanonicalChecksPassed ? 0 : 1;
    }
}

internal static class SchedulerBenchmarkRunner
{
    internal static SchedulerBenchmarkReport Run(bool quick)
    {
        var timings = new List<SchedulerTiming>(64);
        RunSchedulerOverhead(timings, quick);
        RunSerialFamilyMatrix(timings, quick);
        RunSerialBoundary(timings, quick);
        RunParallelCrossover(timings, quick);
        RunWorkerScaling(timings, quick);
        RunIndependentSystems(timings, quick);
        RunRange(timings, quick);
        RunEntityIdentityCost(timings, quick);
        RunStructuralMerge(timings, quick);
        RunPhaseStagger(timings, quick);
        SchedulerSoakResult soak = RunScheduledSoak(quick);
        List<SchedulerDeterminismResult> determinism = RunDeterminism(quick);
        bool canonical = timings.All(static value => value.CanonicalPassed) && soak.CanonicalPassed &&
            determinism.All(static value => value.CanonicalPassed);
        bool allocationFree = timings.All(static value => value.AllocatedBytes == 0) &&
            soak.AllocatedBytes == 0 && soak.WorkerManagedAllocationBytes == 0;
        return new SchedulerBenchmarkReport
        {
            SuiteName = "UltraSim ECS bounded system scheduler admission",
            ReportFormat = "ultrasim-ecs-system-scheduler-v1",
            KernelVersion = EcsKernelVersion.Value,
            SemanticProfile = EcsKernelVersion.SemanticProfile,
            TimestampUtc = DateTimeOffset.UtcNow,
            RuntimeVersion = Environment.Version.ToString(),
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown",
            ProcessorCount = Environment.ProcessorCount,
            Quick = quick,
            AllocationFree = allocationFree,
            CanonicalChecksPassed = canonical,
            Timings = timings,
            Soak = soak,
            Determinism = determinism,
        };
    }

    private static void RunSerialFamilyMatrix(List<SchedulerTiming> results, bool quick)
    {
        int[] scales = quick ? [1_000, 100_000] : [1_000, 10_000, 100_000];
        foreach (int rows in scales)
        foreach (SchedulerFamily family in Enum.GetValues<SchedulerFamily>())
        {
            using var direct = new SchedulerFamilyFixture(rows, family, scheduled: false);
            using var scheduled = new SchedulerFamilyFixture(rows, family, scheduled: true);
            Warm(direct.Execute, 32);
            Warm(scheduled.Execute, 32);
            int samples = quick ? 8 : 20;
            Measurement a = Measure(direct.Execute, samples, Repetitions(rows));
            Measurement b = Measure(scheduled.Execute, samples, Repetitions(rows));
            ulong directHash = direct.Hash();
            ulong scheduledHash = scheduled.Hash();
            bool canonical = directHash == scheduledHash;
            results.Add(a.ToTiming("SerialFamily", $"Direct_{family}_{rows}",
                rows, 1, 1, directHash, canonical));
            results.Add(b.ToTiming("SerialFamily", $"Scheduled_{family}_{rows}",
                rows, 1, 1, scheduledHash, canonical));
        }
    }

    private static void RunSchedulerOverhead(List<SchedulerTiming> results, bool quick)
    {
        int[] counts = quick ? [0, 16, 256] : [0, 1, 4, 16, 64, 256];
        foreach (int count in counts)
        {
            using var fixture = new SchedulerOverheadFixture(count, profiling: false);
            Warm(fixture.Tick, 128);
            Measurement measurement = Measure(fixture.Tick, quick ? 32 : 96, 64);
            results.Add(measurement.ToTiming(
                "SchedulerOverhead", $"NoWorkSystems{count}_ProfilingOff",
                rows: 1, operations: count, concurrency: 1,
                fixture.World.TickCount, canonical: true));
        }

        foreach (bool profiling in new[] { false, true })
        {
            using var fixture = new SchedulerOverheadFixture(16, profiling);
            Warm(fixture.Tick, 128);
            Measurement measurement = Measure(fixture.Tick, quick ? 32 : 96, 64);
            results.Add(measurement.ToTiming(
                "Profiling", profiling ? "NoWorkSystems16_On" : "NoWorkSystems16_Off",
                rows: 1, operations: 16, concurrency: 1,
                fixture.World.TickCount, canonical: true));
        }
    }

    private static void RunSerialBoundary(List<SchedulerTiming> results, bool quick)
    {
        foreach (int rows in quick ? new[] { 1_000, 100_000 } : new[] { 1, 1_000, 10_000, 100_000 })
        {
            using var direct = QueryFixture.CreateDirect(rows, operations: 1);
            using var scheduled = QueryFixture.CreateScheduled(rows, operations: 1, backgroundWorkers: 0);
            int samples = quick ? 12 : 32;
            Warm(direct.ExecuteDirect, 64);
            Warm(scheduled.Tick, 64);
            Measurement a = Measure(direct.ExecuteDirect, samples, Repetitions(rows));
            Measurement b = Measure(scheduled.Tick, samples, Repetitions(rows));
            ulong directHash = direct.Hash();
            ulong scheduledHash = scheduled.Hash();
            bool canonical = directHash == scheduledHash;
            results.Add(a.ToTiming("SerialBoundary", $"Direct_{rows}", rows, 1, 1, directHash, canonical));
            results.Add(b.ToTiming("SerialBoundary", $"Scheduled_{rows}", rows, 1, 1, scheduledHash, canonical));
        }
    }

    private static void RunParallelCrossover(List<SchedulerTiming> results, bool quick)
    {
        int[] scales = quick ? [1_000, 10_000, 100_000] : [1_000, 10_000, 50_000, 100_000, 250_000, 1_000_000];
        foreach (int operations in new[] { 1, 8 })
        foreach (int rows in scales)
        {
            using var serial = QueryFixture.CreateScheduled(rows, operations, backgroundWorkers: 7);
            using var parallel = QueryFixture.CreateScheduled(rows, operations, backgroundWorkers: 7);
            serial.World.SchedulerRunMode = SchedulerRunMode.Serial;
            parallel.World.SchedulerRunMode = SchedulerRunMode.Parallel;
            int samples = rows >= 1_000_000 ? 10 : quick ? 12 : 24;
            Warm(serial.Tick, rows >= 1_000_000 ? 8 : 32);
            Warm(parallel.Tick, rows >= 1_000_000 ? 8 : 32);
            Measurement a = Measure(serial.Tick, samples, Repetitions(rows));
            Measurement b = Measure(parallel.Tick, samples, Repetitions(rows));
            ulong serialHash = serial.Hash();
            ulong parallelHash = parallel.Hash();
            bool canonical = serialHash == parallelHash;
            results.Add(a.ToTiming("ParallelCrossover", $"Serial_Ops{operations}_{rows}", rows, operations, 1, serialHash, canonical));
            results.Add(b.ToTiming("ParallelCrossover", $"Parallel8_Ops{operations}_{rows}", rows, operations, 8, parallelHash, canonical));
        }
    }

    private static void RunWorkerScaling(List<SchedulerTiming> results, bool quick)
    {
        foreach (int concurrency in PracticalConcurrencies())
        {
            using var fixture = QueryFixture.CreateScheduled(100_000, operations: 8,
                backgroundWorkers: concurrency - 1);
            fixture.World.SchedulerRunMode = concurrency == 1
                ? SchedulerRunMode.Serial
                : SchedulerRunMode.Parallel;
            Warm(fixture.Tick, 32);
            Measurement measurement = Measure(fixture.Tick, quick ? 12 : 32, 1);
            results.Add(measurement.ToTiming("WorkerScaling", $"Query100K_C{concurrency}",
                100_000, 8, concurrency, fixture.Hash(), canonical: true));
        }
    }

    private static void RunIndependentSystems(List<SchedulerTiming> results, bool quick)
    {
        int rowsPerSystem = quick ? 10_000 : 50_000;
        foreach (int systems in new[] { 1, 2, 4, 8, 16 })
        {
            using var serial = new IndependentSystemsFixture(systems, rowsPerSystem, SchedulerRunMode.Serial);
            using var parallel = new IndependentSystemsFixture(systems, rowsPerSystem, SchedulerRunMode.Parallel);
            Warm(serial.Tick, 32);
            Warm(parallel.Tick, 32);
            Measurement a = Measure(serial.Tick, quick ? 12 : 32, 1);
            Measurement b = Measure(parallel.Tick, quick ? 12 : 32, 1);
            ulong serialHash = serial.Hash();
            ulong parallelHash = parallel.Hash();
            bool canonical = serialHash == parallelHash;
            int rows = checked(systems * rowsPerSystem);
            results.Add(a.ToTiming("IndependentSystems", $"Serial_S{systems}",
                rows, 8, 1, serialHash, canonical));
            results.Add(b.ToTiming("IndependentSystems", $"Parallel8_S{systems}",
                rows, 8, 8, parallelHash, canonical));
        }
    }

    private static void RunRange(List<SchedulerTiming> results, bool quick)
    {
        foreach (int operations in new[] { 1, 16 })
        foreach (int concurrency in PracticalConcurrencies())
        {
            using var fixture = new RangeFixture(100_000, operations, concurrency - 1);
            fixture.World.SchedulerRunMode = concurrency == 1 ? SchedulerRunMode.Serial : SchedulerRunMode.Parallel;
            Warm(fixture.Tick, 32);
            Measurement measurement = Measure(fixture.Tick, quick ? 12 : 32, 1);
            results.Add(measurement.ToTiming("Range", $"Range100K_Ops{operations}_C{concurrency}",
                100_000, operations, concurrency, fixture.Hash(), fixture.Validate()));
        }
    }

    private static void RunEntityIdentityCost(List<SchedulerTiming> results, bool quick)
    {
        using var indices = IdentityFixture.Create(100_000, reconstruct: false);
        using var entities = IdentityFixture.Create(100_000, reconstruct: true);
        Warm(indices.Tick, 32);
        Warm(entities.Tick, 32);
        Measurement a = Measure(indices.Tick, quick ? 12 : 32, 1);
        Measurement b = Measure(entities.Tick, quick ? 12 : 32, 1);
        results.Add(a.ToTiming("Identity", "LocalIndex100K", 100_000, 1, 1, indices.Hash, true));
        results.Add(b.ToTiming("Identity", "FullEntity100K", 100_000, 1, 1, entities.Hash, entities.Resolved == 100_000 * (32 + (quick ? 12 : 32))));
    }

    private static void RunStructuralMerge(List<SchedulerTiming> results, bool quick)
    {
        foreach (int concurrency in PracticalConcurrencies())
        {
            using var fixture = new StructuralChurnFixture(10_000, concurrency - 1);
            fixture.World.SchedulerRunMode = concurrency == 1 ? SchedulerRunMode.Serial : SchedulerRunMode.Parallel;
            Warm(fixture.Tick, 16);
            SchedulerRuntimeSnapshot before = fixture.World.GetSchedulerRuntimeSnapshot();
            StructuralMeasurement measurement = MeasureStructural(
                fixture.Tick, fixture.World.GetSchedulerRuntimeSnapshot, quick ? 10 : 24);
            SchedulerRuntimeSnapshot after = fixture.World.GetSchedulerRuntimeSnapshot();
            SchedulerTiming timing = measurement.Total.ToTiming("Structural", $"DestroyCreate10K_C{concurrency}",
                20_000, 1, concurrency, fixture.Hash(), fixture.World.AliveCount == 10_000);
            timing.MergedCommands = after.MergedCommandCount - before.MergedCommandCount;
            timing.MergedSegments = after.MergedSegmentCount - before.MergedSegmentCount;
            results.Add(timing);
            results.Add(measurement.Record.ToTiming("StructuralPhase", $"RecordAndGraph10K_C{concurrency}",
                20_000, 1, concurrency, timing.CanonicalHash, timing.CanonicalPassed));
            results.Add(measurement.Merge.ToTiming("StructuralPhase", $"Merge10K_C{concurrency}",
                20_000, 1, concurrency, timing.CanonicalHash, timing.CanonicalPassed));
            results.Add(measurement.Playback.ToTiming("StructuralPhase", $"Playback10K_C{concurrency}",
                20_000, 1, concurrency, timing.CanonicalHash, timing.CanonicalPassed));
        }
    }

    private static SchedulerSoakResult RunScheduledSoak(bool quick)
    {
        int measuredTicks = quick ? 300 : 60_000;
        const int warmup = 256;
        using var serial = new ScheduledSoakFixture(backgroundWorkers: 7, SchedulerRunMode.Serial);
        using var parallel = new ScheduledSoakFixture(backgroundWorkers: 7, SchedulerRunMode.Parallel);
        for (int tick = 0; tick < warmup; tick++) { serial.Tick(); parallel.Tick(); }
        var serialTicks = new long[measuredTicks];
        var parallelTicks = new long[measuredTicks];
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        SchedulerRuntimeSnapshot serialBefore = serial.World.GetSchedulerRuntimeSnapshot();
        SchedulerRuntimeSnapshot workerBefore = parallel.World.GetSchedulerRuntimeSnapshot();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        for (int tick = 0; tick < measuredTicks; tick++)
        {
            long start = Stopwatch.GetTimestamp(); serial.Tick(); serialTicks[tick] = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp(); parallel.Tick(); parallelTicks[tick] = Stopwatch.GetTimestamp() - start;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        SchedulerRuntimeSnapshot serialAfter = serial.World.GetSchedulerRuntimeSnapshot();
        SchedulerRuntimeSnapshot workerAfter = parallel.World.GetSchedulerRuntimeSnapshot();
        ulong serialHash = serial.Hash();
        ulong parallelHash = parallel.Hash();
        return new SchedulerSoakResult
        {
            Population = 100_000,
            WarmupTicks = warmup,
            MeasuredTicks = measuredTicks,
            QueryRowsPerTick = 400_000,
            TrackedWriteTargetsPerTick = 5_000,
            RandomLookupsPerTick = 500,
            StructuralCommandsPerTick = 400,
            SerialMeanMicroseconds = MeanMicroseconds(serialTicks),
            SerialP50Microseconds = PercentileMicroseconds(serialTicks, 0.50),
            SerialP95Microseconds = PercentileMicroseconds(serialTicks, 0.95),
            SerialP99Microseconds = PercentileMicroseconds(serialTicks, 0.99),
            SerialMaxMicroseconds = MaximumMicroseconds(serialTicks),
            ParallelMeanMicroseconds = MeanMicroseconds(parallelTicks),
            ParallelP50Microseconds = PercentileMicroseconds(parallelTicks, 0.50),
            ParallelP95Microseconds = PercentileMicroseconds(parallelTicks, 0.95),
            ParallelP99Microseconds = PercentileMicroseconds(parallelTicks, 0.99),
            ParallelMaxMicroseconds = MaximumMicroseconds(parallelTicks),
            SerialPreBarrierMeanMicroseconds = PhaseMean(serialBefore.PreBarrierTimestampTicks, serialAfter.PreBarrierTimestampTicks, measuredTicks),
            SerialGraphMeanMicroseconds = PhaseMean(serialBefore.GraphTimestampTicks, serialAfter.GraphTimestampTicks, measuredTicks),
            SerialMergeMeanMicroseconds = PhaseMean(serialBefore.StructuralMergeTimestampTicks, serialAfter.StructuralMergeTimestampTicks, measuredTicks),
            SerialPostBarrierMeanMicroseconds = PhaseMean(serialBefore.PostBarrierTimestampTicks, serialAfter.PostBarrierTimestampTicks, measuredTicks),
            ParallelPreBarrierMeanMicroseconds = PhaseMean(workerBefore.PreBarrierTimestampTicks, workerAfter.PreBarrierTimestampTicks, measuredTicks),
            ParallelGraphMeanMicroseconds = PhaseMean(workerBefore.GraphTimestampTicks, workerAfter.GraphTimestampTicks, measuredTicks),
            ParallelMergeMeanMicroseconds = PhaseMean(workerBefore.StructuralMergeTimestampTicks, workerAfter.StructuralMergeTimestampTicks, measuredTicks),
            ParallelPostBarrierMeanMicroseconds = PhaseMean(workerBefore.PostBarrierTimestampTicks, workerAfter.PostBarrierTimestampTicks, measuredTicks),
            SerialHash = serialHash,
            ParallelHash = parallelHash,
            CanonicalPassed = serialHash == parallelHash,
            AllocatedBytes = allocated,
            Gen0Collections = GC.CollectionCount(0) - gen0,
            Gen1Collections = GC.CollectionCount(1) - gen1,
            Gen2Collections = GC.CollectionCount(2) - gen2,
            SchedulerReservedPayloadBytes = parallel.World.SchedulerReservedBytes,
            WorkerManagedAllocationBytes = workerAfter.WorkerManagedAllocationBytes -
                workerBefore.WorkerManagedAllocationBytes,
            ChangedRowsDelivered = parallel.ChangedRowsDelivered,
            Systems = parallel.CopySnapshots(),
        };
    }

    private static void RunPhaseStagger(List<SchedulerTiming> results, bool quick)
    {
        using var aligned = new PhaseFixture(stagger: false);
        using var staggered = new PhaseFixture(stagger: true);
        int ticks = quick ? 100 : 500;
        for (int i = 0; i < 50; i++) { aligned.Tick(); staggered.Tick(); }
        Measurement a = Measure(aligned.Tick, ticks, 1);
        Measurement b = Measure(staggered.Tick, ticks, 1);
        ulong ah = aligned.Hash();
        ulong bh = staggered.Hash();
        bool canonical = ah == bh;
        results.Add(a.ToTiming("PhaseStagger", "Aligned10_Period5", 200_000, 4, 8, ah, canonical));
        results.Add(b.ToTiming("PhaseStagger", "Staggered10_Period5", 200_000, 4, 8, bh, canonical));
    }

    private static List<SchedulerDeterminismResult> RunDeterminism(bool quick)
    {
        int ticks = quick ? 200 : 10_000;
        int[] concurrencies = DeterminismConcurrencies(quick);
        var fixtures = new ScheduledSoakFixture[concurrencies.Length];
        for (int index = 0; index < fixtures.Length; index++)
            fixtures[index] = new ScheduledSoakFixture(
                concurrencies[index] - 1,
                concurrencies[index] == 1 ? SchedulerRunMode.Serial : SchedulerRunMode.Parallel);
        try
        {
            for (int tick = 0; tick < ticks; tick++)
                for (int index = 0; index < fixtures.Length; index++) fixtures[index].Tick();
            ulong reference = fixtures[0].Hash();
            var results = new List<SchedulerDeterminismResult>(fixtures.Length);
            for (int index = 0; index < fixtures.Length; index++)
            {
                ulong hash = fixtures[index].Hash();
                results.Add(new SchedulerDeterminismResult
                {
                    ExecutionConcurrency = concurrencies[index],
                    ReplayOrdinal = ReplayOrdinal(concurrencies, index),
                    Ticks = ticks,
                    CanonicalHash = hash,
                    TickCount = fixtures[index].World.TickCount,
                    ChangedRowsDelivered = fixtures[index].ChangedRowsDelivered,
                    CanonicalPassed = hash == reference,
                });
            }
            return results;
        }
        finally
        {
            foreach (ScheduledSoakFixture fixture in fixtures) fixture.Dispose();
        }
    }

    private static int Repetitions(int rows) => rows switch { <= 1 => 256, <= 1_000 => 32, <= 10_000 => 8, _ => 1 };
    private static int[] PracticalConcurrencies() =>
        Environment.ProcessorCount >= 16 ? [1, 2, 4, 8, 16] : [1, 2, 4, 8];
    private static int[] DeterminismConcurrencies(bool quick) => quick
        ? PracticalConcurrencies()
        : Environment.ProcessorCount >= 16 ? [1, 2, 4, 8, 16, 8, 8] : [1, 2, 4, 8, 8, 8];
    private static int ReplayOrdinal(int[] concurrencies, int index)
    {
        int ordinal = 0;
        for (int prior = 0; prior < index; prior++)
            if (concurrencies[prior] == concurrencies[index]) ordinal++;
        return ordinal;
    }
    private static void Warm(Action action, int count) { for (int i = 0; i < count; i++) action(); }

    private static Measurement Measure(Action action, int samples, int repetitions)
    {
        var ticks = new long[samples];
        long before = GC.GetAllocatedBytesForCurrentThread();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        for (int sample = 0; sample < samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int repeat = 0; repeat < repetitions; repeat++) action();
            ticks[sample] = Stopwatch.GetTimestamp() - start;
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        return CreateMeasurement(ticks, repetitions, allocation,
            GC.CollectionCount(0) - g0,
            GC.CollectionCount(1) - g1,
            GC.CollectionCount(2) - g2,
            checked(samples * repetitions));
    }

    private static StructuralMeasurement MeasureStructural(Action action,
        Func<SchedulerRuntimeSnapshot> snapshot, int samples)
    {
        var total = new long[samples];
        var record = new long[samples];
        var merge = new long[samples];
        var playback = new long[samples];
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        for (int sample = 0; sample < samples; sample++)
        {
            SchedulerRuntimeSnapshot before = snapshot();
            long start = Stopwatch.GetTimestamp();
            action();
            total[sample] = Stopwatch.GetTimestamp() - start;
            SchedulerRuntimeSnapshot after = snapshot();
            record[sample] = after.GraphTimestampTicks - before.GraphTimestampTicks;
            merge[sample] = after.StructuralMergeTimestampTicks - before.StructuralMergeTimestampTicks;
            playback[sample] = after.PostBarrierTimestampTicks - before.PostBarrierTimestampTicks;
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        return new StructuralMeasurement(
            CreateMeasurement(total, 1, allocation, GC.CollectionCount(0) - g0,
                GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2, samples),
            CreateMeasurement(record, 1, 0, 0, 0, 0, samples),
            CreateMeasurement(merge, 1, 0, 0, 0, 0, samples),
            CreateMeasurement(playback, 1, 0, 0, 0, 0, samples));
    }

    private static Measurement CreateMeasurement(long[] ticks, int repetitions, long allocation,
        int gen0, int gen1, int gen2, int invocations) => new(
        MeanMicroseconds(ticks) / repetitions,
        PercentileMicroseconds(ticks, 0.50) / repetitions,
        PercentileMicroseconds(ticks, 0.95) / repetitions,
        PercentileMicroseconds(ticks, 0.99) / repetitions,
        MaximumMicroseconds(ticks) / repetitions,
        allocation, gen0, gen1, gen2, invocations);

    private static double MeanMicroseconds(long[] ticks) => ticks.Average() * 1_000_000d / Stopwatch.Frequency;
    private static double MaximumMicroseconds(long[] ticks) => ticks.Max() * 1_000_000d / Stopwatch.Frequency;
    private static double PhaseMean(long before, long after, int count) =>
        (after - before) * 1_000_000d / Stopwatch.Frequency / count;
    private static double PercentileMicroseconds(long[] ticks, double percentile)
    {
        var copy = (long[])ticks.Clone();
        Array.Sort(copy);
        int index = Math.Clamp((int)Math.Ceiling(copy.Length * percentile) - 1, 0, copy.Length - 1);
        return copy[index] * 1_000_000d / Stopwatch.Frequency;
    }

    private readonly record struct Measurement(double MeanUs, double P50Us, double P95Us,
        double P99Us, double MaxUs, long Allocation,
        int Gen0, int Gen1, int Gen2, int Invocations)
    {
        internal SchedulerTiming ToTiming(string category, string name, int rows, int operations,
            int concurrency, ulong hash, bool canonical) => new()
        {
            Category = category, Name = name, Rows = rows, OperationsPerRow = operations,
            ExecutionConcurrency = concurrency, Samples = Invocations,
            MeanMicroseconds = MeanUs, P50Microseconds = P50Us, P95Microseconds = P95Us,
            P99Microseconds = P99Us, MaxMicroseconds = MaxUs,
            NanosecondsPerRow = rows == 0 ? 0 : MeanUs * 1_000d / rows,
            AllocatedBytes = Allocation, Gen0Collections = Gen0, Gen1Collections = Gen1,
            Gen2Collections = Gen2, CanonicalHash = hash, CanonicalPassed = canonical,
        };
    }

    private readonly record struct StructuralMeasurement(
        Measurement Total, Measurement Record, Measurement Merge, Measurement Playback);
}

internal sealed class QueryFixture : IDisposable
{
    private readonly ReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> _plan;
    private readonly ComponentLookup<BenchmarkVelocity> _velocity;
    private readonly int _operations;
    private readonly QueryRuntime? _runtime;

    private QueryFixture(EcsWorld world,
        ReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> plan,
        ComponentLookup<BenchmarkVelocity> velocity, int operations, QueryRuntime? runtime)
    { World = world; _plan = plan; _velocity = velocity; _operations = operations; _runtime = runtime; }
    internal EcsWorld World { get; }

    internal static QueryFixture CreateDirect(int count, int operations) => Create(count, operations, -1);
    internal static QueryFixture CreateScheduled(int count, int operations, int backgroundWorkers) =>
        Create(count, operations, backgroundWorkers);

    private static QueryFixture Create(int count, int operations, int backgroundWorkers)
    {
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        ComponentId[] signature =
        [
            BenchmarkSchema.GetComponentId<BenchmarkPosition>(),
            BenchmarkSchema.GetComponentId<BenchmarkWide>(),
            BenchmarkSchema.GetComponentId<BenchmarkVelocity>(),
        ];
        Array.Sort(signature, static (a, b) => a.CompareTo(b));
        EcsSchedulerOptions scheduler = backgroundWorkers < 0 ? default : SchedulerOptions(backgroundWorkers, 64);
        var world = new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(Math.Max(1, count)),
            1, 4, maximumStructuralCommands: 0, scheduler: scheduler));
        ArchetypeHandle archetype = world.RegisterArchetype(signature, ChunkLayoutOptions.KiB16,
            Math.Max(1, count / 64 + 4));
        var plan = world.PrepareReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>();
        ComponentLookup<BenchmarkVelocity> velocity = world.PrepareLookup<BenchmarkVelocity>();
        QueryRuntime? runtime = null;
        if (backgroundWorkers >= 0)
        {
            runtime = new QueryRuntime(operations);
            world.RegisterReadReadWriteSystem<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity, QueryRuntime, ScheduledQueryJob>(
                new SystemDefinition("benchmark.query", SystemStage.Movement,
                    executionPolicy: SystemExecutionPolicy.Auto,
                    parallelRowThreshold: 4_096, minimumRowsPerPartition: 2_048), plan, runtime);
        }
        world.Seal(); world.Initialize();
        for (int i = 0; i < count; i++) world.TryCreate(archetype, out _);
        world.BeginRun();
        return new QueryFixture(world, plan, velocity, operations, runtime);
    }

    internal void ExecuteDirect()
    {
        var job = new DirectQueryJob(_operations);
        World.ExecuteJob(_plan, ref job);
    }
    internal void Tick() => World.Tick(1d / 60d);
    internal ulong Hash()
    {
        ulong hash = 1469598103934665603UL;
        for (uint index = 0; index < World.AliveCount; index++)
        {
            if (!World.TryGetEntity(index, out Entity entity) || !World.TryRead(entity, _velocity, out BenchmarkVelocity value))
                throw new InvalidOperationException("Query benchmark canonical scan failed.");
            hash = unchecked((hash ^ unchecked((uint)BitConverter.SingleToInt32Bits(value.X))) * 1099511628211UL);
            hash = unchecked((hash ^ unchecked((uint)BitConverter.SingleToInt32Bits(value.Y))) * 1099511628211UL);
        }
        return hash;
    }
    public void Dispose() => World.Dispose();
}

internal sealed class SchedulerOverheadFixture : IDisposable
{
    internal SchedulerOverheadFixture(int systemCount, bool profiling)
    {
        int capacity = Math.Max(1, systemCount);
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: capacity,
            maximumDependencies: 0,
            maximumWorkItems: 1,
            maximumStructuralSegments: 1,
            backgroundWorkerCount: 0,
            callerParticipates: true,
            structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 1,
            profilingEnabled: profiling);
        World = SchedulerBenchmarkWorlds.EmptySchedulerWorld(1, scheduler);
        World.RegisterArchetype([], ChunkLayoutOptions.KiB16, 1);
        for (int index = 0; index < systemCount; index++)
        {
            World.RegisterRangeSystem<NoWorkRuntime, NoWorkJob>(
                new SystemDefinition($"benchmark.empty.{index:D3}", SystemStage.WorldSim),
                new NoWorkRuntime());
        }
        World.Seal(); World.Initialize(); World.BeginRun();
    }

    internal EcsWorld World { get; }
    internal void Tick() => World.Tick(1d / 60d);
    public void Dispose() => World.Dispose();

    private sealed class NoWorkRuntime : IRangeSystemRuntime<NoWorkJob>
    {
        public int GetRangeCount(in SystemPrepareContext context) => 0;
        public NoWorkJob Prepare(in SystemPrepareContext context) => default;
    }

    private readonly struct NoWorkJob : IRangeJob
    {
        public void Execute(in SystemJobContext context, int start, int count) { }
    }
}

internal enum SchedulerFamily
{
    R1,
    W1,
    R1W1,
    R2,
    R3,
    R2W1,
    R1W2,
    R3W1,
    R2W2,
}

internal sealed class SchedulerFamilyFixture : IDisposable
{
    private readonly SchedulerFamily _family;
    private readonly bool _scheduled;
    private readonly AllFamilyRuntime? _runtime;
    private readonly ReadJobPlan<BenchmarkPosition> _r1;
    private readonly WriteJobPlan<BenchmarkVelocity> _w1;
    private readonly ReadWriteJobPlan<BenchmarkPosition, BenchmarkVelocity> _r1w1;
    private readonly ReadReadJobPlan<BenchmarkPosition, BenchmarkWide> _r2;
    private readonly ReadReadReadJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkScalar> _r3;
    private readonly ReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> _r2w1;
    private readonly ReadWriteWriteJobPlan<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity> _r1w2;
    private readonly ReadReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> _r3w1;
    private readonly ReadReadWriteWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> _r2w2;
    private readonly ComponentLookup<BenchmarkPosition> _positions;
    private readonly ComponentLookup<BenchmarkVelocity> _velocities;
    private readonly ComponentLookup<BenchmarkWide> _wide;
    private readonly ComponentLookup<BenchmarkScalar> _scalars;
    private long _directChecksum;

    internal SchedulerFamilyFixture(int rows, SchedulerFamily family, bool scheduled)
    {
        _family = family;
        _scheduled = scheduled;
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        EcsSchedulerOptions scheduler = scheduled
            ? SchedulerBenchmarkWorlds.SchedulerOptions(0, 1)
            : default;
        World = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(rows), 1, 13, maximumStructuralCommands: 0, scheduler: scheduler));
        ComponentId[] signature =
        [
            BenchmarkSchema.GetComponentId<BenchmarkPosition>(),
            BenchmarkSchema.GetComponentId<BenchmarkVelocity>(),
            BenchmarkSchema.GetComponentId<BenchmarkWide>(),
            BenchmarkSchema.GetComponentId<BenchmarkScalar>(),
        ];
        Array.Sort(signature, static (a, b) => a.CompareTo(b));
        ArchetypeHandle archetype = World.RegisterArchetype(
            signature, ChunkLayoutOptions.KiB32, Math.Max(1, rows / 256 + 8));
        _r1 = World.PrepareReadJob<BenchmarkPosition>();
        _w1 = World.PrepareWriteJob<BenchmarkVelocity>();
        _r1w1 = World.PrepareReadWriteJob<BenchmarkPosition, BenchmarkVelocity>();
        _r2 = World.PrepareReadReadJob<BenchmarkPosition, BenchmarkWide>();
        _r3 = World.PrepareReadReadReadJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar>();
        _r2w1 = World.PrepareReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>();
        _r1w2 = World.PrepareReadWriteWriteJob<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity>();
        _r3w1 = World.PrepareReadReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>();
        _r2w2 = World.PrepareReadReadWriteWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>();
        _positions = World.PrepareLookup<BenchmarkPosition>();
        _velocities = World.PrepareLookup<BenchmarkVelocity>();
        _wide = World.PrepareLookup<BenchmarkWide>();
        _scalars = World.PrepareLookup<BenchmarkScalar>();
        if (scheduled)
        {
            _runtime = new AllFamilyRuntime();
            RegisterScheduled(family, _runtime);
        }
        World.Seal(); World.Initialize();
        for (int index = 0; index < rows; index++)
            if (!World.TryCreate(archetype, out _)) throw new InvalidOperationException();
        World.BeginRun();
    }

    internal EcsWorld World { get; }
    internal void Execute()
    {
        if (_scheduled)
        {
            World.Tick(1d / 60d);
            return;
        }
        var job = new AllFamilyJob();
        _ = _family switch
        {
            SchedulerFamily.R1 => World.ExecuteJob(_r1, ref job),
            SchedulerFamily.W1 => World.ExecuteJob(_w1, ref job),
            SchedulerFamily.R1W1 => World.ExecuteJob(_r1w1, ref job),
            SchedulerFamily.R2 => World.ExecuteJob(_r2, ref job),
            SchedulerFamily.R3 => World.ExecuteJob(_r3, ref job),
            SchedulerFamily.R2W1 => World.ExecuteJob(_r2w1, ref job),
            SchedulerFamily.R1W2 => World.ExecuteJob(_r1w2, ref job),
            SchedulerFamily.R3W1 => World.ExecuteJob(_r3w1, ref job),
            SchedulerFamily.R2W2 => World.ExecuteJob(_r2w2, ref job),
            _ => throw new ArgumentOutOfRangeException(),
        };
        _directChecksum = unchecked(_directChecksum + job.Checksum);
    }

    private void RegisterScheduled(SchedulerFamily family, AllFamilyRuntime runtime)
    {
        var definition = new SystemDefinition(
            $"benchmark.serial-family.{family}", SystemStage.WorldSim,
            executionPolicy: SystemExecutionPolicy.Serial);
        _ = family switch
        {
            SchedulerFamily.R1 => World.RegisterReadSystem<BenchmarkPosition, AllFamilyRuntime, AllFamilyJob>(definition, _r1, runtime),
            SchedulerFamily.W1 => World.RegisterWriteSystem<BenchmarkVelocity, AllFamilyRuntime, AllFamilyJob>(definition, _w1, runtime),
            SchedulerFamily.R1W1 => World.RegisterReadWriteSystem<BenchmarkPosition, BenchmarkVelocity, AllFamilyRuntime, AllFamilyJob>(definition, _r1w1, runtime),
            SchedulerFamily.R2 => World.RegisterReadReadSystem<BenchmarkPosition, BenchmarkWide, AllFamilyRuntime, AllFamilyJob>(definition, _r2, runtime),
            SchedulerFamily.R3 => World.RegisterReadReadReadSystem<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, AllFamilyRuntime, AllFamilyJob>(definition, _r3, runtime),
            SchedulerFamily.R2W1 => World.RegisterReadReadWriteSystem<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity, AllFamilyRuntime, AllFamilyJob>(definition, _r2w1, runtime),
            SchedulerFamily.R1W2 => World.RegisterReadWriteWriteSystem<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity, AllFamilyRuntime, AllFamilyJob>(definition, _r1w2, runtime),
            SchedulerFamily.R3W1 => World.RegisterReadReadReadWriteSystem<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity, AllFamilyRuntime, AllFamilyJob>(definition, _r3w1, runtime),
            SchedulerFamily.R2W2 => World.RegisterReadReadWriteWriteSystem<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity, AllFamilyRuntime, AllFamilyJob>(definition, _r2w2, runtime),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    internal ulong Hash()
    {
        ulong hash = 1469598103934665603UL;
        for (uint index = 0; index < World.AliveCount; index++)
        {
            if (!World.TryGetEntity(index, out Entity entity) ||
                !World.TryRead(entity, _positions, out BenchmarkPosition position) ||
                !World.TryRead(entity, _velocities, out BenchmarkVelocity velocity) ||
                !World.TryRead(entity, _wide, out BenchmarkWide wide) ||
                !World.TryRead(entity, _scalars, out BenchmarkScalar scalar))
                throw new InvalidOperationException("Family benchmark canonical scan failed.");
            hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(position.X)) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(velocity.X)) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)wide.A) * 1099511628211UL);
            hash = unchecked((hash ^ (uint)scalar.Value) * 1099511628211UL);
        }
        hash = unchecked((hash ^ unchecked((ulong)(_scheduled ? _runtime!.Checksum : _directChecksum))) *
            1099511628211UL);
        return hash;
    }

    public void Dispose() => World.Dispose();
}

internal sealed class AllFamilyRuntime : ISystemRuntime<AllFamilyJob>
{
    internal long Checksum;
    public AllFamilyJob Prepare(in SystemPrepareContext context) => default;
    public void Complete(in SystemCompleteContext context, ReadOnlySpan<AllFamilyJob> results)
    {
        for (int index = 0; index < results.Length; index++)
            Checksum = unchecked(Checksum + results[index].Checksum);
    }
}

internal struct AllFamilyJob :
    IReadChunkJob<BenchmarkPosition>, IScheduledReadChunkJob<BenchmarkPosition>,
    IWriteChunkJob<BenchmarkVelocity>, IScheduledWriteChunkJob<BenchmarkVelocity>,
    IReadWriteChunkJob<BenchmarkPosition, BenchmarkVelocity>, IScheduledReadWriteChunkJob<BenchmarkPosition, BenchmarkVelocity>,
    IReadReadChunkJob<BenchmarkPosition, BenchmarkWide>, IScheduledReadReadChunkJob<BenchmarkPosition, BenchmarkWide>,
    IReadReadReadChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar>, IScheduledReadReadReadChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar>,
    IReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>, IScheduledReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>,
    IReadWriteWriteChunkJob<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity>, IScheduledReadWriteWriteChunkJob<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity>,
    IReadReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>, IScheduledReadReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>,
    IReadReadWriteWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>, IScheduledReadReadWriteWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>
{
    internal long Checksum;

    public void Execute(in ReadComponentChunk<BenchmarkPosition> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
            Checksum = unchecked(Checksum + BitConverter.SingleToInt32Bits(chunk.ReadComponents[row].X) + row + 1);
    }
    public void Execute(in SystemJobContext context, in ReadComponentChunk<BenchmarkPosition> chunk) => Execute(chunk);

    public void Execute(in WriteComponentChunk<BenchmarkVelocity> chunk)
    { for (int row = 0; row < chunk.Count; row++) chunk.WriteComponents[row].X += 1; }
    public void Execute(in SystemJobContext context, in WriteComponentChunk<BenchmarkVelocity> chunk) => Execute(chunk);

    public void Execute(in ReadWriteComponentChunk<BenchmarkPosition, BenchmarkVelocity> chunk)
    { for (int row = 0; row < chunk.Count; row++) chunk.WriteComponents[row].X += chunk.ReadComponents[row].X + 1; }
    public void Execute(in SystemJobContext context, in ReadWriteComponentChunk<BenchmarkPosition, BenchmarkVelocity> chunk) => Execute(chunk);

    public void Execute(in ReadReadComponentChunk<BenchmarkPosition, BenchmarkWide> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
            Checksum = unchecked(Checksum + BitConverter.SingleToInt32Bits(chunk.ReadComponents1[row].X) +
                chunk.ReadComponents2[row].A + row + 1);
    }
    public void Execute(in SystemJobContext context, in ReadReadComponentChunk<BenchmarkPosition, BenchmarkWide> chunk) => Execute(chunk);

    public void Execute(in ReadReadReadComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkScalar> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
            Checksum = unchecked(Checksum + BitConverter.SingleToInt32Bits(chunk.ReadComponents1[row].X) +
                chunk.ReadComponents2[row].A + chunk.ReadComponents3[row].Value + row + 1);
    }
    public void Execute(in SystemJobContext context, in ReadReadReadComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkScalar> chunk) => Execute(chunk);

    public void Execute(in ReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> chunk)
    { for (int row = 0; row < chunk.Count; row++) chunk.WriteComponents[row].X += chunk.ReadComponents1[row].X + chunk.ReadComponents2[row].A + 1; }
    public void Execute(in SystemJobContext context, in ReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> chunk) => Execute(chunk);

    public void Execute(in ReadWriteWriteComponentChunk<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity> chunk)
    { for (int row = 0; row < chunk.Count; row++) { chunk.WriteComponents1[row].Value++; chunk.WriteComponents2[row].Y += chunk.ReadComponents[row].Y + 1; } }
    public void Execute(in SystemJobContext context, in ReadWriteWriteComponentChunk<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity> chunk) => Execute(chunk);

    public void Execute(in ReadReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> chunk)
    { for (int row = 0; row < chunk.Count; row++) chunk.WriteComponents[row].X += chunk.ReadComponents1[row].X + chunk.ReadComponents2[row].A + chunk.ReadComponents3[row].Value + 1; }
    public void Execute(in SystemJobContext context, in ReadReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> chunk) => Execute(chunk);

    public void Execute(in ReadReadWriteWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> chunk)
    { for (int row = 0; row < chunk.Count; row++) { chunk.WriteComponents1[row].Value++; chunk.WriteComponents2[row].X += chunk.ReadComponents1[row].X + chunk.ReadComponents2[row].A + 1; } }
    public void Execute(in SystemJobContext context, in ReadReadWriteWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> chunk) => Execute(chunk);
}

internal struct DirectQueryJob : IReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>
{
    private readonly int _operations;
    internal DirectQueryJob(int operations) => _operations = operations;
    public void Execute(in ReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> chunk) =>
        QueryKernel.Execute(chunk.ReadComponents1, chunk.ReadComponents2, chunk.WriteComponents, _operations);
}
internal struct ScheduledQueryJob : IScheduledReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>
{
    private readonly int _operations;
    internal int Rows;
    internal ScheduledQueryJob(int operations) => (_operations, Rows) = (operations, 0);
    public void Execute(in SystemJobContext context,
        in ReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> chunk)
    { QueryKernel.Execute(chunk.ReadComponents1, chunk.ReadComponents2, chunk.WriteComponents, _operations); Rows += chunk.Count; }
}
internal sealed class QueryRuntime : ISystemRuntime<ScheduledQueryJob>
{
    private readonly int _operations;
    internal QueryRuntime(int operations) => _operations = operations;
    public ScheduledQueryJob Prepare(in SystemPrepareContext context) => new(_operations);
}
internal static class QueryKernel
{
    internal static void Execute(ReadOnlySpan<BenchmarkPosition> positions, ReadOnlySpan<BenchmarkWide> wide,
        Span<BenchmarkVelocity> velocities, int operations)
    {
        for (int row = 0; row < velocities.Length; row++)
        {
            float x = velocities[row].X;
            float y = velocities[row].Y;
            for (int op = 0; op < operations; op++)
            {
                x = x * 1.000001f + positions[row].X + unchecked((float)(wide[row].A & 7));
                y = y * 0.999999f + positions[row].Y + unchecked((float)(wide[row].B & 7));
            }
            velocities[row].X = x; velocities[row].Y = y;
        }
    }
}

internal sealed class RangeFixture : IDisposable
{
    private readonly int[] _values;
    internal RangeFixture(int count, int operations, int backgroundWorkers)
    {
        _values = new int[count];
        EcsSchedulerOptions scheduler = SchedulerOptions(backgroundWorkers, 32);
        World = EmptySchedulerWorld(count: 1, scheduler);
        World.RegisterRangeSystem<RangeRuntime, IntegerRangeJob>(
            new SystemDefinition("benchmark.range", SystemStage.AI,
                executionPolicy: SystemExecutionPolicy.Auto,
                parallelRowThreshold: 4_096, minimumRowsPerPartition: 2_048),
            new RangeRuntime(_values, operations));
        World.Seal(); World.Initialize(); World.BeginRun();
    }
    internal EcsWorld World { get; }
    internal void Tick() => World.Tick(1d / 60d);
    internal bool Validate() => _values.All(static value => value != 0);
    internal ulong Hash() { ulong h = 1469598103934665603UL; foreach (int v in _values) h = unchecked((h ^ (uint)v) * 1099511628211UL); return h; }
    public void Dispose() => World.Dispose();
}

internal sealed class IndependentSystemsFixture : IDisposable
{
    private readonly int[][] _values;

    internal IndependentSystemsFixture(int systemCount, int rowsPerSystem, SchedulerRunMode mode)
    {
        _values = new int[systemCount][];
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: systemCount, maximumDependencies: 0,
            maximumWorkItems: systemCount, maximumStructuralSegments: systemCount,
            backgroundWorkerCount: 7, callerParticipates: true,
            structuralCommandsPerProducer: 0, structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: 1);
        World = SchedulerBenchmarkWorlds.EmptySchedulerWorld(1, scheduler);
        World.RegisterArchetype([], ChunkLayoutOptions.KiB16, 1);
        for (int system = 0; system < systemCount; system++)
        {
            int[] values = new int[rowsPerSystem];
            for (int row = 0; row < values.Length; row++) values[row] = row + system;
            _values[system] = values;
            World.RegisterRangeSystem<IndependentRuntime, IndependentJob>(
                new SystemDefinition($"benchmark.independent.{system:D2}", SystemStage.WorldSim,
                    executionPolicy: SystemExecutionPolicy.Serial),
                new IndependentRuntime(values));
        }
        World.Seal(); World.Initialize(); World.BeginRun();
        World.SchedulerRunMode = mode;
    }

    internal EcsWorld World { get; }
    internal void Tick() => World.Tick(1d / 60d);
    internal ulong Hash()
    {
        ulong hash = 1469598103934665603UL;
        for (int system = 0; system < _values.Length; system++)
            for (int row = 0; row < _values[system].Length; row++)
                hash = unchecked((hash ^ unchecked((uint)_values[system][row])) * 1099511628211UL);
        return hash;
    }
    public void Dispose() => World.Dispose();

    private sealed class IndependentRuntime : IRangeSystemRuntime<IndependentJob>
    {
        private readonly int[] _values;
        internal IndependentRuntime(int[] values) => _values = values;
        public int GetRangeCount(in SystemPrepareContext context) => _values.Length;
        public IndependentJob Prepare(in SystemPrepareContext context) => new(_values);
    }

    private readonly struct IndependentJob : IRangeJob
    {
        private readonly int[] _values;
        internal IndependentJob(int[] values) => _values = values;
        public void Execute(in SystemJobContext context, int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                int value = _values[index];
                for (int operation = 0; operation < 8; operation++)
                    value = unchecked(value * 1_664_525 + 1_013_904_223);
                _values[index] = value;
            }
        }
    }
}
internal sealed class RangeRuntime : IRangeSystemRuntime<IntegerRangeJob>
{
    private readonly int[] _values; private readonly int _operations;
    internal RangeRuntime(int[] values, int operations) => (_values, _operations) = (values, operations);
    public int GetRangeCount(in SystemPrepareContext context) => _values.Length;
    public IntegerRangeJob Prepare(in SystemPrepareContext context) => new(_values, _operations);
}
internal struct IntegerRangeJob : IRangeJob
{
    private readonly int[] _values; private readonly int _operations;
    internal IntegerRangeJob(int[] values, int operations) => (_values, _operations) = (values, operations);
    public void Execute(in SystemJobContext context, int start, int count)
    {
        for (int i = start; i < start + count; i++)
        { int value = _values[i]; for (int op = 0; op < _operations; op++) value = unchecked(value * 1664525 + 1013904223); _values[i] = value; }
    }
}

internal sealed class IdentityFixture : IDisposable
{
    private readonly IdentityRuntime _runtime;
    private IdentityFixture(EcsWorld world, IdentityRuntime runtime) { World = world; _runtime = runtime; }
    internal EcsWorld World { get; }
    internal long Resolved => _runtime.Resolved;
    internal ulong Hash => _runtime.Hash;
    internal void Tick() => World.Tick(1d / 60d);
    internal static IdentityFixture Create(int count, bool reconstruct)
    {
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
        var world = new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(count), 1, 2,
            scheduler: SchedulerOptions(0, 16, profiling: false)));
        ArchetypeHandle archetype = world.RegisterArchetype([position], ChunkLayoutOptions.KiB16, count / 128 + 4);
        ReadJobPlan<BenchmarkPosition> plan = world.PrepareReadJob<BenchmarkPosition>();
        var runtime = new IdentityRuntime(reconstruct);
        world.RegisterReadSystem<BenchmarkPosition, IdentityRuntime, IdentityJob>(
            new SystemDefinition("benchmark.identity", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Serial), plan, runtime);
        world.Seal(); world.Initialize(); for (int i = 0; i < count; i++) world.TryCreate(archetype, out _); world.BeginRun();
        return new IdentityFixture(world, runtime);
    }
    public void Dispose() => World.Dispose();
}
internal sealed class IdentityRuntime : ISystemRuntime<IdentityJob>
{
    private readonly bool _reconstruct;
    internal long Resolved; internal ulong Hash;
    internal IdentityRuntime(bool reconstruct) => _reconstruct = reconstruct;
    public IdentityJob Prepare(in SystemPrepareContext context) => new(_reconstruct);
    public void Complete(in SystemCompleteContext context, ReadOnlySpan<IdentityJob> jobs)
    { for (int i = 0; i < jobs.Length; i++) { Resolved += jobs[i].Resolved; Hash ^= jobs[i].Hash; } }
}
internal struct IdentityJob : IScheduledReadChunkJob<BenchmarkPosition>
{
    private readonly bool _reconstruct; internal int Resolved; internal ulong Hash;
    internal IdentityJob(bool reconstruct) => (_reconstruct, Resolved, Hash) = (reconstruct, 0, 0);
    public void Execute(in SystemJobContext context, in ReadComponentChunk<BenchmarkPosition> chunk)
    {
        ulong h = Hash;
        foreach (uint index in chunk.EntityIndices)
        {
            h = unchecked(h * 1099511628211UL + index + 1);
            if (_reconstruct) { if (!context.TryGetEntity(index, out Entity entity)) throw new InvalidOperationException(); h ^= entity.Generation; Resolved++; }
        }
        Hash = h;
    }
}

internal sealed class StructuralChurnFixture : IDisposable
{
    internal StructuralChurnFixture(int count, int backgroundWorkers)
    {
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
        EcsSchedulerOptions scheduler = SchedulerOptions(backgroundWorkers, 64,
            commandsPerProducer: count * 2, payloadBytes: 0, profiling: true);
        World = new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(count), 1, 1,
            maximumStructuralCommands: count * 2, scheduler: scheduler));
        Archetype = World.RegisterArchetype([position], ChunkLayoutOptions.KiB16, count / 128 + 4);
        World.RegisterRangeSystem<StructuralChurnRuntime, StructuralChurnJob>(
            new SystemDefinition("benchmark.structural", SystemStage.Cleanup,
                executionPolicy: SystemExecutionPolicy.Auto,
                parallelRowThreshold: 1_024, minimumRowsPerPartition: 512),
            new StructuralChurnRuntime(Archetype, count));
        World.Seal(); World.Initialize(); for (int i = 0; i < count; i++) World.TryCreate(Archetype, out _); World.BeginRun();
    }
    internal EcsWorld World { get; } internal ArchetypeHandle Archetype { get; }
    internal void Tick() => World.Tick(1d / 60d);
    internal ulong Hash() { ulong h = 0; for (uint i = 0; i < World.AliveCount; i++) { if (!World.TryGetEntity(i, out Entity e)) throw new InvalidOperationException(); h = unchecked(h * 31 + e.Generation); } return h; }
    public void Dispose() => World.Dispose();
}
internal sealed class StructuralChurnRuntime : IRangeSystemRuntime<StructuralChurnJob>
{
    private readonly ArchetypeHandle _archetype; private readonly int _count;
    internal StructuralChurnRuntime(ArchetypeHandle archetype, int count) => (_archetype, _count) = (archetype, count);
    public int GetRangeCount(in SystemPrepareContext context) => _count;
    public StructuralChurnJob Prepare(in SystemPrepareContext context) => new(_archetype);
}
internal struct StructuralChurnJob : IRangeJob
{
    private readonly ArchetypeHandle _archetype;
    internal StructuralChurnJob(ArchetypeHandle archetype) => _archetype = archetype;
    public void Execute(in SystemJobContext context, int start, int count)
    {
        for (int i = start; i < start + count; i++)
        { if (!context.TryGetEntity(unchecked((uint)i), out Entity entity)) throw new InvalidOperationException(); if (!context.Commands.TryDestroy(entity)) throw new InvalidOperationException(); context.Commands.CreateDefault(_archetype); }
    }
}

internal sealed class PhaseFixture : IDisposable
{
    private readonly int[][] _values = new int[10][];
    internal PhaseFixture(bool stagger)
    {
        EcsSchedulerOptions scheduler = new(
            maximumSystems: 10, maximumDependencies: 0, maximumWorkItems: 256,
            maximumStructuralSegments: 512, backgroundWorkerCount: 7,
            maximumPartitionsPerSystem: 32, phaseStaggeringEnabled: stagger,
            maximumStaggerPhases: 5);
        World = EmptySchedulerWorld(1, scheduler);
        for (int index = 0; index < _values.Length; index++)
        {
            _values[index] = new int[20_000];
            World.RegisterRangeSystem<RangeRuntime, IntegerRangeJob>(
                new SystemDefinition($"phase.benchmark.{index}", SystemStage.WorldSim,
                    new SystemSchedule(true, 5, 0), SystemExecutionPolicy.Auto,
                    parallelRowThreshold: 2_048, minimumRowsPerPartition: 1_024,
                    automaticallyStagger: stagger),
                new RangeRuntime(_values[index], operations: 4));
        }
        World.Seal(); World.Initialize(); World.BeginRun(); World.SchedulerRunMode = SchedulerRunMode.Parallel;
    }
    internal EcsWorld World { get; }
    internal void Tick() => World.Tick(1d / 60d);
    internal ulong Hash() { ulong h=1469598103934665603UL; foreach(int[] values in _values) foreach(int value in values) h=unchecked((h^(uint)value)*1099511628211UL); return h; }
    public void Dispose() => World.Dispose();
}

internal sealed class ScheduledSoakFixture : IDisposable
{
    private readonly ComponentLookup<BenchmarkVelocity> _velocity;
    private readonly ComponentLookup<BenchmarkScalar> _scalar;
    private readonly SoakChangedRuntime _changedRuntime;
    internal ScheduledSoakFixture(int backgroundWorkers, SchedulerRunMode mode)
    {
        const int count = 100_000;
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        ComponentId scalarId = BenchmarkSchema.GetComponentId<BenchmarkScalar>();
        ComponentId[] signature =
        [BenchmarkSchema.GetComponentId<BenchmarkPosition>(), BenchmarkSchema.GetComponentId<BenchmarkWide>(), BenchmarkSchema.GetComponentId<BenchmarkVelocity>(), scalarId];
        Array.Sort(signature, static (a,b) => a.CompareTo(b));
        EcsSchedulerOptions scheduler = SchedulerOptions(backgroundWorkers, 256,
            commandsPerProducer: 1_024, payloadBytes: 4096, profiling: true);
        World = new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(count), 1, 16,
            maximumStructuralCommands: 2_048, scheduler: scheduler));
        ArchetypeHandle archetype = World.RegisterArchetype(signature, ChunkLayoutOptions.KiB16, count / 64 + 4);
        var plan = World.PrepareReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>();
        _velocity = World.PrepareLookup<BenchmarkVelocity>();
        _scalar = World.PrepareLookup<BenchmarkScalar>();
        WriteJobPlan<BenchmarkScalar> tracked = World.PrepareWriteJob<BenchmarkScalar>();
        ChangedRowsJobPlan<BenchmarkScalar, BenchmarkScalar> changed =
            World.PrepareChangedRowsJob<BenchmarkScalar, BenchmarkScalar>();
        for (int system = 0; system < 4; system++)
            World.RegisterReadReadWriteSystem<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity, QueryRuntime, ScheduledQueryJob>(
                new SystemDefinition($"soak.query.{system}", (SystemStage)((int)SystemStage.Movement + system),
                    executionPolicy: SystemExecutionPolicy.Auto, parallelRowThreshold: 8_192,
                    minimumRowsPerPartition: 2_048), plan, new QueryRuntime(1));
        World.RegisterTrackedWriteSystem<BenchmarkScalar, SoakTrackedRuntime, SoakTrackedJob>(
            new SystemDefinition("soak.tracked", SystemStage.WorldSim,
                executionPolicy: SystemExecutionPolicy.Auto, parallelRowThreshold: 8_192,
                minimumRowsPerPartition: 2_048), tracked, new SoakTrackedRuntime());
        World.RegisterRangeSystem<SoakLookupRuntime, SoakLookupJob>(
            new SystemDefinition("soak.lookup", SystemStage.AI, executionPolicy: SystemExecutionPolicy.Parallel,
                additionalReads: [BenchmarkSchema.GetComponentId<BenchmarkVelocity>()],
                parallelRowThreshold: 1, minimumRowsPerPartition: 64),
            new SoakLookupRuntime(_velocity));
        _changedRuntime = new SoakChangedRuntime();
        World.RegisterChangedRowsSystem<BenchmarkScalar, BenchmarkScalar, SoakChangedRuntime, SoakChangedJob>(
            new SystemDefinition("soak.changed", SystemStage.Networking,
                executionPolicy: SystemExecutionPolicy.Serial), changed, _changedRuntime);
        World.RegisterRangeSystem<SoakStructuralRuntime, SoakStructuralJob>(
            new SystemDefinition("soak.structural", SystemStage.Cleanup,
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1, minimumRowsPerPartition: 32),
            new SoakStructuralRuntime(archetype));
        World.Seal(); World.Initialize(); for (int i = 0; i < count; i++) World.TryCreate(archetype, out _);
        World.BeginRun();
        _changedRuntime.Arm(World.CreateChangeCursorFromNow());
        World.SchedulerRunMode = mode;
    }
    internal EcsWorld World { get; }
    internal long ChangedRowsDelivered => _changedRuntime.Rows;
    internal void Tick() => World.Tick(1d / 60d);
    internal ulong Hash() { ulong h = 1469598103934665603UL; for (uint i=0;i<World.AliveCount;i++) { if(!World.TryGetEntity(i,out Entity e)||!World.TryRead(e,_velocity,out BenchmarkVelocity v)||!World.TryRead(e,_scalar,out BenchmarkScalar s)) throw new InvalidOperationException(); h=unchecked((h^(uint)BitConverter.SingleToInt32Bits(v.X))*1099511628211UL); h=unchecked((h^(uint)s.Value)*1099511628211UL); h=unchecked((h^e.Generation)*1099511628211UL); } return h; }
    internal List<SystemRuntimeSnapshot> CopySnapshots()
    {
        var values = new SystemRuntimeSnapshot[World.SystemCount];
        World.CopySystemRuntimeSnapshots(values);
        return [.. values];
    }
    public void Dispose() => World.Dispose();
}
internal sealed class SoakLookupRuntime : IRangeSystemRuntime<SoakLookupJob>
{
    private readonly ComponentLookup<BenchmarkVelocity> _lookup;
    internal SoakLookupRuntime(ComponentLookup<BenchmarkVelocity> lookup) => _lookup=lookup;
    public int GetRangeCount(in SystemPrepareContext context)=>500;
    public SoakLookupJob Prepare(in SystemPrepareContext context)=>new(_lookup);
}
internal struct SoakLookupJob : IRangeJob
{
    private readonly ComponentLookup<BenchmarkVelocity> _lookup;
    internal SoakLookupJob(ComponentLookup<BenchmarkVelocity> lookup)=>_lookup=lookup;
    public void Execute(in SystemJobContext context,int start,int count){for(int i=start;i<start+count;i++){uint index=unchecked((uint)((i*199)%100000));if(!context.TryGetEntity(index,out Entity e)||!context.TryRead(e,_lookup,out _))throw new InvalidOperationException();}}
}
internal sealed class SoakTrackedRuntime : ISystemRuntime<SoakTrackedJob>
{
    public SoakTrackedJob Prepare(in SystemPrepareContext context) => default;
}
internal struct SoakTrackedJob : IScheduledTrackedWriteChunkJob<BenchmarkScalar>
{
    public void Execute(in SystemJobContext context, in TrackedWriteComponentChunk<BenchmarkScalar> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            if (chunk.EntityIndices[row] % 20 != 0) continue;
            ref BenchmarkScalar scalar = ref chunk.Write.GetTracked(row);
            scalar.Value++;
        }
    }
}
internal sealed class SoakChangedRuntime : IChangedRowsSystemRuntime<SoakChangedJob>
{
    private bool _armed;
    internal long Rows;
    public ChangeCursor Cursor { get; set; }
    internal void Arm(ChangeCursor cursor) { Cursor = cursor; _armed = true; }
    public SoakChangedJob Prepare(in SystemPrepareContext context)
    {
        if (!_armed) throw new InvalidOperationException("The soak change cursor was not armed.");
        return default;
    }
    public void Complete(in SystemCompleteContext context, ReadOnlySpan<SoakChangedJob> jobs)
    { for (int i = 0; i < jobs.Length; i++) Rows += jobs[i].Rows; }
}
internal struct SoakChangedJob : IScheduledChangedRowsChunkJob<BenchmarkScalar>
{
    internal int Rows;
    public void Execute(in SystemJobContext context, in ReadOnlyComponentChunk<BenchmarkScalar> chunk) => Rows += chunk.Count;
}
internal sealed class SoakStructuralRuntime : IRangeSystemRuntime<SoakStructuralJob>
{
    private readonly ArchetypeHandle _archetype; internal SoakStructuralRuntime(ArchetypeHandle archetype)=>_archetype=archetype;
    public int GetRangeCount(in SystemPrepareContext context)=>200;
    public SoakStructuralJob Prepare(in SystemPrepareContext context)=>new(_archetype);
}
internal struct SoakStructuralJob : IRangeJob
{
    private readonly ArchetypeHandle _archetype; internal SoakStructuralJob(ArchetypeHandle archetype)=>_archetype=archetype;
    public void Execute(in SystemJobContext context,int start,int count){for(int i=start;i<start+count;i++){if(!context.TryGetEntity(unchecked((uint)i),out Entity e))throw new InvalidOperationException();context.Commands.TryDestroy(e);context.Commands.CreateDefault(_archetype);}}
}

internal static class SchedulerBenchmarkWorlds
{
    internal static EcsSchedulerOptions SchedulerOptions(int backgroundWorkers, int workItems,
        int commandsPerProducer = 0, int payloadBytes = 0, bool profiling = false) => new(
            maximumSystems: 32, maximumDependencies: 128, maximumWorkItems: workItems,
            maximumStructuralSegments: workItems * 4, backgroundWorkerCount: backgroundWorkers,
            callerParticipates: true, structuralCommandsPerProducer: commandsPerProducer,
            structuralPayloadBytesPerProducer: payloadBytes, maximumPartitionsPerSystem: 64,
            profilingEnabled: profiling);

    internal static EcsWorld EmptySchedulerWorld(int count, EcsSchedulerOptions scheduler)
    {
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        return new EcsWorld(schema, new EcsWorldOptions(new EntityCapacity(count), 1, 1,
            maximumStructuralCommands: 0, scheduler: scheduler));
    }
}

internal sealed class SchedulerBenchmarkReport
{
    public string SuiteName { get; set; } = string.Empty; public string ReportFormat { get; set; } = string.Empty;
    public string KernelVersion { get; set; } = string.Empty; public string SemanticProfile { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; } public string RuntimeVersion { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty; public int ProcessorCount { get; set; } public bool Quick { get; set; }
    public bool AllocationFree { get; set; } public bool CanonicalChecksPassed { get; set; }
    public List<SchedulerTiming> Timings { get; set; } = []; public SchedulerSoakResult Soak { get; set; } = new();
    public List<SchedulerDeterminismResult> Determinism { get; set; } = [];
}
internal sealed class SchedulerTiming
{
    public string Category { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public int Rows { get; set; } public int OperationsPerRow { get; set; } public int ExecutionConcurrency { get; set; }
    public int Samples { get; set; } public double MeanMicroseconds { get; set; } public double P50Microseconds { get; set; }
    public double P95Microseconds { get; set; } public double P99Microseconds { get; set; } public double MaxMicroseconds { get; set; }
    public double NanosecondsPerRow { get; set; } public long AllocatedBytes { get; set; }
    public int Gen0Collections { get; set; } public int Gen1Collections { get; set; } public int Gen2Collections { get; set; }
    public ulong CanonicalHash { get; set; } public bool CanonicalPassed { get; set; }
    public long MergedCommands { get; set; } public long MergedSegments { get; set; }
}
internal sealed class SchedulerSoakResult
{
    public int Population { get; set; } public int WarmupTicks { get; set; } public int MeasuredTicks { get; set; }
    public int QueryRowsPerTick { get; set; } public int TrackedWriteTargetsPerTick { get; set; }
    public int RandomLookupsPerTick { get; set; } public int StructuralCommandsPerTick { get; set; }
    public double SerialMeanMicroseconds { get; set; } public double SerialP50Microseconds { get; set; }
    public double SerialP95Microseconds { get; set; } public double SerialP99Microseconds { get; set; }
    public double SerialMaxMicroseconds { get; set; } public double ParallelMeanMicroseconds { get; set; }
    public double ParallelP50Microseconds { get; set; } public double ParallelP95Microseconds { get; set; }
    public double ParallelP99Microseconds { get; set; } public double ParallelMaxMicroseconds { get; set; }
    public double SerialPreBarrierMeanMicroseconds { get; set; } public double SerialGraphMeanMicroseconds { get; set; }
    public double SerialMergeMeanMicroseconds { get; set; } public double SerialPostBarrierMeanMicroseconds { get; set; }
    public double ParallelPreBarrierMeanMicroseconds { get; set; } public double ParallelGraphMeanMicroseconds { get; set; }
    public double ParallelMergeMeanMicroseconds { get; set; } public double ParallelPostBarrierMeanMicroseconds { get; set; }
    public ulong SerialHash { get; set; } public ulong ParallelHash { get; set; } public bool CanonicalPassed { get; set; }
    public long AllocatedBytes { get; set; } public int Gen0Collections { get; set; } public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; } public long SchedulerReservedPayloadBytes { get; set; }
    public long ChangedRowsDelivered { get; set; }
    public long WorkerManagedAllocationBytes { get; set; }
    public List<SystemRuntimeSnapshot> Systems { get; set; } = [];
}
internal sealed class SchedulerDeterminismResult
{
    public int ExecutionConcurrency { get; set; }
    public int ReplayOrdinal { get; set; }
    public int Ticks { get; set; }
    public ulong TickCount { get; set; }
    public ulong CanonicalHash { get; set; }
    public long ChangedRowsDelivered { get; set; }
    public bool CanonicalPassed { get; set; }
}
