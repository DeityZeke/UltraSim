using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace UltraSim.ECS.Benchmarks;

internal static class PublicJobBenchmarkProgram
{
    internal static int Run(string[] args)
    {
        string outputPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "results",
            $"ecs-{EcsKernelVersion.Value}-public-generated-chunk-jobs.json"));
        for (int index = 0; index < args.Length; index++)
        {
            if ((args[index].Equals("--public-jobs-output", StringComparison.OrdinalIgnoreCase) ||
                 args[index].Equals("--output", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < args.Length)
            {
                outputPath = Path.GetFullPath(args[++index]);
            }
        }

        PublicJobBenchmarkReport report = PublicJobBenchmarkRunner.Run();
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        Console.WriteLine($"ECS {EcsKernelVersion.Value} public generated chunk-job experiment");
        foreach (PublicJobBenchmarkResult result in report.TraversalResults)
        {
            Console.WriteLine(
                $"{result.Family}/{result.Scale:N0}/{result.Order}/{result.Surface}: " +
                $"mean={result.MeanNanosecondsPerRow:F3} ns/row " +
                $"p95={result.P95NanosecondsPerRow:F3} ns/row " +
                $"logical={result.EffectiveLogicalGigabytesPerSecond:F2} GB/s " +
                $"alloc={result.ManagedAllocationBytes} B");
        }
        foreach (TrackedJobBenchmarkResult result in report.TrackedWriteResults)
        {
            Console.WriteLine(
                $"tracked/{result.Pattern}/{result.TargetWrites:N0}: " +
                $"visited={result.MeanNanosecondsPerVisitedRow:F3} ns/row " +
                $"write={result.MeanNanosecondsPerActualWrite:F3} ns/write " +
                $"delivered={result.ExactRowsDelivered:N0} alloc={result.ManagedAllocationBytes} B");
        }
        foreach (PublicJobDispatchResult result in report.DispatchResults)
        {
            Console.WriteLine(
                $"dispatch/{result.Shape}/x{result.InvocationCount}: " +
                $"mean={result.MeanNanosecondsPerInvocation:F2} ns/invocation " +
                $"p95={result.P95NanosecondsPerInvocation:F2} ns/invocation " +
                $"alloc={result.ManagedAllocationBytes} B");
        }
        Console.WriteLine($"Results: {outputPath}");
        return report.AllocationFree && report.CanonicalChecksPassed ? 0 : 1;
    }
}

internal static class PublicJobBenchmarkRunner
{
    private static readonly int[] Scales = [1_000, 10_000, 100_000];

    internal static PublicJobBenchmarkReport Run()
    {
        var traversalResults = new List<PublicJobBenchmarkResult>(60);
        foreach (int scale in Scales)
        {
            using var publicFixture = new PublicJobTraversalFixture(scale);
            using var internalFixture = new PublicJobTraversalFixture(scale);
            foreach (PublicJobFamily family in Enum.GetValues<PublicJobFamily>())
            {
                RunPair(
                    publicFixture,
                    internalFixture,
                    family,
                    PublicJobExecutionOrder.PublicFirst,
                    traversalResults);
                RunPair(
                    publicFixture,
                    internalFixture,
                    family,
                    PublicJobExecutionOrder.InternalFirst,
                    traversalResults);
            }
        }

        var trackedResults = new List<TrackedJobBenchmarkResult>();
        foreach (TrackedWritePattern pattern in Enum.GetValues<TrackedWritePattern>())
        {
            int[] targets = pattern == TrackedWritePattern.Dense
                ? [100_000]
                : [100, 1_000, 5_000, 10_000, 25_000, 50_000];
            foreach (int target in targets)
            {
                using var fixture = new PublicTrackedJobFixture(100_000);
                trackedResults.Add(RunTracked(fixture, pattern, target));
            }
        }

        List<PublicJobDispatchResult> dispatchResults = RunDispatchBaselines();

        bool allocationFree = traversalResults.All(static result => result.ManagedAllocationBytes == 0) &&
            trackedResults.All(static result => result.ManagedAllocationBytes == 0) &&
            dispatchResults.All(static result => result.ManagedAllocationBytes == 0);
        bool canonical = trackedResults.All(static result => result.ExactRowsDelivered == result.UniqueRowsWritten);
        return new PublicJobBenchmarkReport
        {
            SuiteName = "UltraSim ECS public generated serial chunk-job admission",
            ReportFormat = "ultrasim-ecs-public-jobs-v1",
            KernelVersion = EcsKernelVersion.Value,
            SemanticProfile = EcsKernelVersion.SemanticProfile,
            TimestampUtc = DateTimeOffset.UtcNow,
            RuntimeVersion = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            OperatingSystem = Environment.OSVersion.ToString(),
            PlanSizeBytes = System.Runtime.CompilerServices.Unsafe.SizeOf<
                ReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>>(),
            NewPersistentReservationBytes = 0,
            AllocationFree = allocationFree,
            CanonicalChecksPassed = canonical,
            TraversalResults = traversalResults,
            TrackedWriteResults = trackedResults,
            DispatchResults = dispatchResults,
        };
    }

    private static List<PublicJobDispatchResult> RunDispatchBaselines()
    {
        var results = new List<PublicJobDispatchResult>(8);
        using (var noMatch = new PublicDispatchFixture(population: 1, matching: false))
            results.Add(RunDispatch(noMatch, "NoMatch", invocationCount: 1));
        using (var tiny = new PublicDispatchFixture(population: 1, matching: true))
        {
            results.Add(RunDispatch(tiny, "TinyMatch", invocationCount: 1));
            foreach (int count in new[] { 1, 4, 16, 64 })
                results.Add(RunDispatch(tiny, "SequentialTiny", count));
        }
        using (var ordinary = new PublicDispatchFixture(population: 100_000, matching: true))
            results.Add(RunDispatch(ordinary, "Ordinary100K", invocationCount: 1));
        return results;
    }

    private static PublicJobDispatchResult RunDispatch(
        PublicDispatchFixture fixture,
        string shape,
        int invocationCount)
    {
        const int iterations = 80;
        for (int warmup = 0; warmup < 256; warmup++)
            fixture.Execute(invocationCount);

        var ticks = new long[iterations];
        long allocation = 0;
        long checksum = 0;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            JobInvocation invocation = fixture.Execute(invocationCount);
            ticks[iteration] = Stopwatch.GetTimestamp() - started;
            allocation += GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
            checksum = unchecked(checksum + invocation.Checksum + invocation.Rows);
        }

        double meanNanoseconds = ticks.Average() * 1_000_000_000d / Stopwatch.Frequency;
        double p95Nanoseconds = Percentile(ticks, 0.95) * 1_000_000_000d / Stopwatch.Frequency;
        long expectedRows = checked((long)fixture.Population * invocationCount);
        return new PublicJobDispatchResult
        {
            Shape = shape,
            Population = fixture.Population,
            InvocationCount = invocationCount,
            Iterations = iterations,
            RowsPerSample = expectedRows,
            MeanNanosecondsPerInvocation = meanNanoseconds / invocationCount,
            P95NanosecondsPerInvocation = p95Nanoseconds / invocationCount,
            MeanNanosecondsPerRow = expectedRows == 0 ? 0 : meanNanoseconds / expectedRows,
            ManagedAllocationBytes = allocation,
            Gen0Collections = GC.CollectionCount(0) - gen0Before,
            Gen1Collections = GC.CollectionCount(1) - gen1Before,
            Gen2Collections = GC.CollectionCount(2) - gen2Before,
            Checksum = checksum,
        };
    }

    private static void RunPair(
        PublicJobTraversalFixture publicFixture,
        PublicJobTraversalFixture internalFixture,
        PublicJobFamily family,
        PublicJobExecutionOrder order,
        List<PublicJobBenchmarkResult> results)
    {
        int repetitions = publicFixture.Population switch
        {
            <= 1_000 => 100,
            <= 10_000 => 20,
            _ => 5,
        };
        Func<JobInvocation> publicAction = publicFixture.GetPublicAction(family);
        Func<JobInvocation> internalAction = internalFixture.GetInternalAction(family);
        for (int warmup = 0; warmup < 64; warmup++)
        {
            if (order == PublicJobExecutionOrder.PublicFirst)
            {
                Repeat(publicAction, repetitions);
                Repeat(internalAction, repetitions);
            }
            else
            {
                Repeat(internalAction, repetitions);
                Repeat(publicAction, repetitions);
            }
        }

        const int iterations = 40;
        var publicTicks = new long[iterations];
        var internalTicks = new long[iterations];
        long publicAllocation = 0;
        long internalAllocation = 0;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long checksum = 0;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (order == PublicJobExecutionOrder.PublicFirst)
            {
                Measure(publicAction, repetitions, out publicTicks[iteration], out long allocation, ref checksum);
                publicAllocation += allocation;
                Measure(internalAction, repetitions, out internalTicks[iteration], out allocation, ref checksum);
                internalAllocation += allocation;
            }
            else
            {
                Measure(internalAction, repetitions, out internalTicks[iteration], out long allocation, ref checksum);
                internalAllocation += allocation;
                Measure(publicAction, repetitions, out publicTicks[iteration], out allocation, ref checksum);
                publicAllocation += allocation;
            }
        }
        int gen0 = GC.CollectionCount(0) - gen0Before;
        int gen1 = GC.CollectionCount(1) - gen1Before;
        int gen2 = GC.CollectionCount(2) - gen2Before;
        long publicState = publicFixture.ComputeCanonicalChecksum();
        long internalState = internalFixture.ComputeCanonicalChecksum();
        if (publicState != internalState)
        {
            throw new InvalidOperationException(
                $"Public/internal canonical component state diverged for {family}/{publicFixture.Population}/{order}.");
        }
        long operations = checked((long)publicFixture.Population * repetitions);
        int logicalBytes = GetLogicalBytes(family);
        results.Add(CreateResult(
            family,
            publicFixture.Population,
            order,
            "PublicFacade",
            publicTicks,
            operations,
            logicalBytes,
            publicAllocation,
            gen0,
            gen1,
            gen2,
            checksum,
            repetitions));
        results.Add(CreateResult(
            family,
            publicFixture.Population,
            order,
            "InternalGenerated",
            internalTicks,
            operations,
            logicalBytes,
            internalAllocation,
            gen0,
            gen1,
            gen2,
            checksum,
            repetitions));
    }

    private static TrackedJobBenchmarkResult RunTracked(
        PublicTrackedJobFixture fixture,
        TrackedWritePattern pattern,
        int targetWrites)
    {
        var warmupJob = new ConditionalVelocityJob(pattern, targetWrites, fixture.Population);
        for (int warmup = 0; warmup < 256; warmup++)
            fixture.World.ExecuteTrackedJob(fixture.Plan, ref warmupJob);

        ChangeCursor cursor = fixture.World.CreateChangeCursorFromNow();
        const int iterations = 40;
        var ticks = new long[iterations];
        long allocation = 0;
        long checksum = 0;
        long writes = 0;
        long chunks = 0;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var job = new ConditionalVelocityJob(pattern, targetWrites, fixture.Population);
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            int visited = fixture.World.ExecuteTrackedJob(fixture.Plan, ref job);
            ticks[iteration] = Stopwatch.GetTimestamp() - started;
            allocation += GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
            if (visited != fixture.Population)
                throw new InvalidOperationException("Tracked public-job benchmark lost rows.");
            writes += job.Writes;
            chunks += job.ChunksTouched;
            checksum = unchecked(checksum + job.Checksum);
        }

        var changed = new ChangedScalarCountJob();
        int delivered = fixture.World.ExecuteChangedRowsJob(
            fixture.ChangedPlan,
            ref cursor,
            ref changed);
        if (delivered != changed.Rows)
            throw new InvalidOperationException("Tracked public-job exact consumer disagreed with its job.");
        long uniqueWrites = pattern == TrackedWritePattern.Dense
            ? fixture.Population
            : CountSelectedRows(pattern, targetWrites, fixture.Population);
        double meanTicks = ticks.Average();
        double p95Ticks = Percentile(ticks, 0.95);
        double meanNs = meanTicks * 1_000_000_000d / Stopwatch.Frequency;
        double p95Ns = p95Ticks * 1_000_000_000d / Stopwatch.Frequency;
        double meanWrites = writes / (double)iterations;
        return new TrackedJobBenchmarkResult
        {
            Pattern = pattern.ToString(),
            Population = fixture.Population,
            TargetWrites = targetWrites,
            RowsVisited = fixture.Population,
            UniqueRowsWritten = uniqueWrites,
            MeanActualWrites = meanWrites,
            MeanChunksTouched = chunks / (double)iterations,
            DenseChunkStamps = pattern == TrackedWritePattern.Dense ? chunks / iterations : 0,
            ExactRowStamps = pattern == TrackedWritePattern.Dense ? 0 : writes / iterations,
            ExactRowsDelivered = delivered,
            MeanNanosecondsPerVisitedRow = meanNs / fixture.Population,
            P95NanosecondsPerVisitedRow = p95Ns / fixture.Population,
            MeanNanosecondsPerActualWrite = meanWrites == 0 ? 0 : meanNs / meanWrites,
            P95NanosecondsPerActualWrite = meanWrites == 0 ? 0 : p95Ns / meanWrites,
            ManagedAllocationBytes = allocation,
            Checksum = checksum,
        };
    }

    private static PublicJobBenchmarkResult CreateResult(
        PublicJobFamily family,
        int scale,
        PublicJobExecutionOrder order,
        string surface,
        long[] samples,
        long operations,
        int logicalBytesPerRow,
        long allocation,
        int gen0,
        int gen1,
        int gen2,
        long checksum,
        int invocationsPerSample)
    {
        double meanTicks = samples.Average();
        double p95Ticks = Percentile(samples, 0.95);
        double meanNsPerRow = meanTicks * 1_000_000_000d /
            (Stopwatch.Frequency * operations);
        double p95NsPerRow = p95Ticks * 1_000_000_000d /
            (Stopwatch.Frequency * operations);
        return new PublicJobBenchmarkResult
        {
            Family = family.ToString(),
            Scale = scale,
            Order = order.ToString(),
            Surface = surface,
            Iterations = samples.Length,
            InvocationsPerSample = invocationsPerSample,
            OperationsPerSample = operations,
            LogicalBytesPerRow = logicalBytesPerRow,
            MeanNanosecondsPerRow = meanNsPerRow,
            P95NanosecondsPerRow = p95NsPerRow,
            EffectiveLogicalGigabytesPerSecond = logicalBytesPerRow / meanNsPerRow,
            ManagedAllocationBytes = allocation,
            Gen0Collections = gen0,
            Gen1Collections = gen1,
            Gen2Collections = gen2,
            Checksum = checksum,
        };
    }

    private static int GetLogicalBytes(PublicJobFamily family) => family switch
    {
        PublicJobFamily.ReadRead => 72,
        PublicJobFamily.ReadReadWrite => 80,
        PublicJobFamily.ReadWriteWrite => 20,
        PublicJobFamily.ReadReadReadWrite => 84,
        PublicJobFamily.ReadReadWriteWrite => 84,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static int CountSelectedRows(TrackedWritePattern pattern, int target, int population)
    {
        int count = 0;
        for (uint entityIndex = 0; entityIndex < unchecked((uint)population); entityIndex++)
        {
            if (ConditionalVelocityJob.IsSelected(pattern, target, population, entityIndex))
                count++;
        }
        return count;
    }

    private static JobInvocation Repeat(Func<JobInvocation> action, int repetitions)
    {
        long checksum = 0;
        long rows = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            JobInvocation result = action();
            rows += result.Rows;
            checksum = unchecked(checksum + result.Checksum);
        }
        return new JobInvocation(rows, checksum);
    }

    private static void Measure(
        Func<JobInvocation> action,
        int repetitions,
        out long elapsedTicks,
        out long allocation,
        ref long checksum)
    {
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        JobInvocation result = Repeat(action, repetitions);
        elapsedTicks = Stopwatch.GetTimestamp() - started;
        allocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        checksum = unchecked(checksum + result.Checksum + result.Rows);
    }

    private static double Percentile(long[] samples, double percentile)
    {
        long[] sorted = (long[])samples.Clone();
        Array.Sort(sorted);
        int index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}

internal sealed class PublicJobTraversalFixture : IDisposable
{
    private static readonly FieldInfo CoreField = typeof(EcsWorld).GetField(
        "_core",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The public ECS facade core field was not found.");

    private readonly EcsWorld _world;
    private readonly EcsWorld<NativeChunkArena> _core;
    private readonly ReadReadJobPlan<BenchmarkPosition, BenchmarkWide> _readRead;
    private readonly ReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> _readReadWrite;
    private readonly ReadWriteWriteJobPlan<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity> _readWriteWrite;
    private readonly ReadReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> _readReadReadWrite;
    private readonly ReadReadWriteWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity> _readReadWriteWrite;

    internal PublicJobTraversalFixture(int population)
    {
        Population = population;
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        _world = new EcsWorld(
            schema,
            new EcsWorldOptions(new EntityCapacity(population), 1, maximumPreparedPlans: 5));
        ComponentId[] signature =
        [
            BenchmarkSchema.GetComponentId<BenchmarkPosition>(),
            BenchmarkSchema.GetComponentId<BenchmarkVelocity>(),
            BenchmarkSchema.GetComponentId<BenchmarkWide>(),
            BenchmarkSchema.GetComponentId<BenchmarkScalar>(),
        ];
        Array.Sort(signature, static (left, right) => left.CompareTo(right));
        ArchetypeHandle archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB32,
            maximumChunks: Math.Max(1, population / 256 + 8));
        _readRead = _world.PrepareReadReadJob<BenchmarkPosition, BenchmarkWide>();
        _readReadWrite = _world.PrepareReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>();
        _readWriteWrite = _world.PrepareReadWriteWriteJob<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity>();
        _readReadReadWrite = _world.PrepareReadReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>();
        _readReadWriteWrite = _world.PrepareReadReadWriteWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>();
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        for (int index = 0; index < population; index++)
        {
            if (!_world.TryCreate(archetype, out _))
                throw new InvalidOperationException("Public job traversal fixture exhausted capacity.");
        }
        _core = (EcsWorld<NativeChunkArena>)(CoreField.GetValue(_world)
            ?? throw new InvalidOperationException("The public ECS facade core is null."));
    }

    internal int Population { get; }

    internal Func<JobInvocation> GetPublicAction(PublicJobFamily family) => family switch
    {
        PublicJobFamily.ReadRead => ExecutePublicReadRead,
        PublicJobFamily.ReadReadWrite => ExecutePublicReadReadWrite,
        PublicJobFamily.ReadWriteWrite => ExecutePublicReadWriteWrite,
        PublicJobFamily.ReadReadReadWrite => ExecutePublicReadReadReadWrite,
        PublicJobFamily.ReadReadWriteWrite => ExecutePublicReadReadWriteWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    internal Func<JobInvocation> GetInternalAction(PublicJobFamily family) => family switch
    {
        PublicJobFamily.ReadRead => ExecuteInternalReadRead,
        PublicJobFamily.ReadReadWrite => ExecuteInternalReadReadWrite,
        PublicJobFamily.ReadWriteWrite => ExecuteInternalReadWriteWrite,
        PublicJobFamily.ReadReadReadWrite => ExecuteInternalReadReadReadWrite,
        PublicJobFamily.ReadReadWriteWrite => ExecuteInternalReadReadWriteWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private JobInvocation ExecutePublicReadRead()
    {
        var job = new ReadReadArithmeticJob();
        int rows = _world.ExecuteJob(_readRead, ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecuteInternalReadRead()
    {
        var job = new ReadReadArithmeticJob();
        int rows = _core.VisitReadReadChunks(_readRead.GetPlan(), ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecutePublicReadReadWrite()
    {
        var job = new ReadReadWriteArithmeticJob();
        int rows = _world.ExecuteJob(_readReadWrite, ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecuteInternalReadReadWrite()
    {
        var job = new ReadReadWriteArithmeticJob();
        int rows = _core.VisitReadReadWriteChunks(_readReadWrite.GetPlan(), ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecutePublicReadWriteWrite()
    {
        var job = new ReadWriteWriteArithmeticJob();
        int rows = _world.ExecuteJob(_readWriteWrite, ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecuteInternalReadWriteWrite()
    {
        var job = new ReadWriteWriteArithmeticJob();
        int rows = _core.VisitReadWriteWriteChunks(_readWriteWrite.GetPlan(), ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecutePublicReadReadReadWrite()
    {
        var job = new ReadReadReadWriteArithmeticJob();
        int rows = _world.ExecuteJob(_readReadReadWrite, ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecuteInternalReadReadReadWrite()
    {
        var job = new ReadReadReadWriteArithmeticJob();
        int rows = _core.VisitReadReadReadWriteChunks(_readReadReadWrite.GetPlan(), ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecutePublicReadReadWriteWrite()
    {
        var job = new ReadReadWriteWriteArithmeticJob();
        int rows = _world.ExecuteJob(_readReadWriteWrite, ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    private JobInvocation ExecuteInternalReadReadWriteWrite()
    {
        var job = new ReadReadWriteWriteArithmeticJob();
        int rows = _core.VisitReadReadWriteWriteChunks(_readReadWriteWrite.GetPlan(), ref job);
        return new JobInvocation(rows, job.Checksum);
    }

    internal long ComputeCanonicalChecksum()
    {
        var job = new CanonicalFourColumnJob();
        int rows = _core.VisitReadReadReadWriteChunks(_readReadReadWrite.GetPlan(), ref job);
        if (rows != Population)
            throw new InvalidOperationException("Public job canonical traversal lost rows.");
        return job.Checksum;
    }

    public void Dispose() => _world.Dispose();
}

internal sealed class PublicTrackedJobFixture : IDisposable
{
    internal PublicTrackedJobFixture(int population)
    {
        Population = population;
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        World = new EcsWorld(
            schema,
            new EcsWorldOptions(new EntityCapacity(population), 1, maximumPreparedPlans: 2));
        ComponentId[] signature =
        [
            BenchmarkSchema.GetComponentId<BenchmarkPosition>(),
            BenchmarkSchema.GetComponentId<BenchmarkVelocity>(),
            BenchmarkSchema.GetComponentId<BenchmarkWide>(),
        ];
        Array.Sort(signature, static (left, right) => left.CompareTo(right));
        ArchetypeHandle archetype = World.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB32,
            maximumChunks: Math.Max(1, population / 256 + 8));
        Plan = World.PrepareReadReadWriteJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>();
        ChangedPlan = World.PrepareChangedRowsJob<BenchmarkPosition, BenchmarkVelocity>();
        World.Seal();
        World.Initialize();
        World.BeginRun();
        for (int index = 0; index < population; index++)
        {
            if (!World.TryCreate(archetype, out _))
                throw new InvalidOperationException("Tracked public job fixture exhausted capacity.");
        }
    }

    internal EcsWorld World { get; }
    internal int Population { get; }
    internal ReadReadWriteJobPlan<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> Plan { get; }
    internal ChangedRowsJobPlan<BenchmarkPosition, BenchmarkVelocity> ChangedPlan { get; }
    public void Dispose() => World.Dispose();
}

internal sealed class PublicDispatchFixture : IDisposable
{
    private readonly EcsWorld _world;
    private readonly ReadReadJobPlan<BenchmarkPosition, BenchmarkWide> _plan;

    internal PublicDispatchFixture(int population, bool matching)
    {
        Population = matching ? population : 0;
        ComponentSchema schema = BenchmarkSchema.CreateSchema();
        _world = new EcsWorld(
            schema,
            new EcsWorldOptions(new EntityCapacity(Math.Max(1, population)), 1, maximumPreparedPlans: 1));
        ComponentId[] signature = matching
            ?
            [
                BenchmarkSchema.GetComponentId<BenchmarkPosition>(),
                BenchmarkSchema.GetComponentId<BenchmarkWide>(),
            ]
            : [BenchmarkSchema.GetComponentId<BenchmarkPosition>()];
        Array.Sort(signature, static (left, right) => left.CompareTo(right));
        ArchetypeHandle archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB32,
            maximumChunks: Math.Max(1, population / 256 + 8));
        _plan = _world.PrepareReadReadJob<BenchmarkPosition, BenchmarkWide>();
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        for (int index = 0; index < population; index++)
        {
            if (!_world.TryCreate(archetype, out _))
                throw new InvalidOperationException("Public dispatch fixture exhausted capacity.");
        }
    }

    internal int Population { get; }

    internal JobInvocation Execute(int invocationCount)
    {
        long rows = 0;
        long checksum = 0;
        for (int invocation = 0; invocation < invocationCount; invocation++)
        {
            var job = new ReadReadArithmeticJob();
            rows += _world.ExecuteJob(_plan, ref job);
            checksum = unchecked(checksum + job.Checksum);
        }
        return new JobInvocation(rows, checksum);
    }

    public void Dispose() => _world.Dispose();
}

internal struct ReadReadArithmeticJob : IReadReadChunkJob<BenchmarkPosition, BenchmarkWide>
{
    internal long Checksum;
    public void Execute(in ReadReadComponentChunk<BenchmarkPosition, BenchmarkWide> chunk)
    {
        long checksum = Checksum;
        for (int row = 0; row < chunk.Count; row++)
        {
            ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
            ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
            checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(position.X) +
                wide.A + wide.D + wide.H + chunk.EntityIndices[row]);
        }
        Checksum = checksum;
    }
}

internal struct ReadReadWriteArithmeticJob : IReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>
{
    internal long Checksum;
    public void Execute(in ReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> chunk)
    {
        long checksum = Checksum;
        for (int row = 0; row < chunk.Count; row++)
        {
            float value = chunk.ReadComponents1[row].X + chunk.ReadComponents2[row].A;
            chunk.WriteComponents[row].X += value + 1;
            checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(chunk.WriteComponents[row].X));
        }
        Checksum = checksum;
    }
}

internal struct ReadWriteWriteArithmeticJob : IReadWriteWriteChunkJob<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity>
{
    internal long Checksum;
    public void Execute(in ReadWriteWriteComponentChunk<BenchmarkPosition, BenchmarkScalar, BenchmarkVelocity> chunk)
    {
        long checksum = Checksum;
        for (int row = 0; row < chunk.Count; row++)
        {
            chunk.WriteComponents1[row].Value += row + 1;
            chunk.WriteComponents2[row].Y += chunk.ReadComponents[row].Y + 1;
            checksum = unchecked(checksum + chunk.WriteComponents1[row].Value +
                BitConverter.SingleToInt32Bits(chunk.WriteComponents2[row].Y));
        }
        Checksum = checksum;
    }
}

internal struct ReadReadReadWriteArithmeticJob :
    IReadReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>
{
    internal long Checksum;
    public void Execute(in ReadReadReadWriteComponentChunk<
        BenchmarkPosition,
        BenchmarkWide,
        BenchmarkScalar,
        BenchmarkVelocity> chunk)
    {
        long checksum = Checksum;
        for (int row = 0; row < chunk.Count; row++)
        {
            chunk.WriteComponents[row].X += chunk.ReadComponents1[row].X +
                chunk.ReadComponents2[row].A + chunk.ReadComponents3[row].Value + 1;
            checksum = unchecked(checksum + BitConverter.SingleToInt32Bits(chunk.WriteComponents[row].X));
        }
        Checksum = checksum;
    }
}

internal struct ReadReadWriteWriteArithmeticJob :
    IReadReadWriteWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>
{
    internal long Checksum;
    public void Execute(in ReadReadWriteWriteComponentChunk<
        BenchmarkPosition,
        BenchmarkWide,
        BenchmarkScalar,
        BenchmarkVelocity> chunk)
    {
        long checksum = Checksum;
        for (int row = 0; row < chunk.Count; row++)
        {
            chunk.WriteComponents1[row].Value += row + 1;
            chunk.WriteComponents2[row].X += chunk.ReadComponents1[row].X + chunk.ReadComponents2[row].H + 1;
            checksum = unchecked(checksum + chunk.WriteComponents1[row].Value +
                BitConverter.SingleToInt32Bits(chunk.WriteComponents2[row].X));
        }
        Checksum = checksum;
    }
}

internal struct CanonicalFourColumnJob :
    IReadReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkScalar, BenchmarkVelocity>
{
    internal long Checksum;
    public void Execute(in ReadReadReadWriteComponentChunk<
        BenchmarkPosition,
        BenchmarkWide,
        BenchmarkScalar,
        BenchmarkVelocity> chunk)
    {
        long checksum = Checksum;
        for (int row = 0; row < chunk.Count; row++)
        {
            ref readonly BenchmarkPosition position = ref chunk.ReadComponents1[row];
            ref readonly BenchmarkWide wide = ref chunk.ReadComponents2[row];
            ref readonly BenchmarkScalar scalar = ref chunk.ReadComponents3[row];
            ref readonly BenchmarkVelocity velocity = ref chunk.WriteComponents[row];
            checksum = unchecked(
                (checksum * 1099511628211L) ^
                BitConverter.SingleToInt32Bits(position.X) ^
                wide.A ^ wide.D ^ wide.H ^ scalar.Value ^
                BitConverter.SingleToInt32Bits(velocity.X) ^
                BitConverter.SingleToInt32Bits(velocity.Y) ^
                chunk.EntityIndices[row]);
        }
        Checksum = checksum;
    }
}

internal struct ConditionalVelocityJob :
    ITrackedReadReadWriteChunkJob<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity>
{
    private readonly TrackedWritePattern _pattern;
    private readonly int _target;
    private readonly int _population;

    internal ConditionalVelocityJob(TrackedWritePattern pattern, int target, int population)
    {
        _pattern = pattern;
        _target = target;
        _population = population;
        Writes = 0;
        ChunksTouched = 0;
        Checksum = 0;
    }

    internal long Writes;
    internal long ChunksTouched;
    internal long Checksum;

    public void Execute(in TrackedReadReadWriteComponentChunk<BenchmarkPosition, BenchmarkWide, BenchmarkVelocity> chunk)
    {
        long before = Writes;
        if (_pattern == TrackedWritePattern.Dense)
        {
            Span<BenchmarkVelocity> write = chunk.Write.DenseSpan;
            for (int row = 0; row < write.Length; row++)
            {
                write[row].X += 1;
                Checksum = unchecked(Checksum + BitConverter.SingleToInt32Bits(write[row].X));
            }
            Writes += write.Length;
        }
        else
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                uint entityIndex = chunk.EntityIndices[row];
                if (!IsSelected(_pattern, _target, _population, entityIndex))
                    continue;
                ref BenchmarkVelocity write = ref chunk.Write.GetTracked(row);
                write.X += 1;
                Writes++;
                Checksum = unchecked(Checksum + BitConverter.SingleToInt32Bits(write.X));
            }
        }
        if (Writes != before)
            ChunksTouched++;
    }

    internal static bool IsSelected(
        TrackedWritePattern pattern,
        int target,
        int population,
        uint entityIndex)
    {
        if (pattern == TrackedWritePattern.Contiguous)
            return entityIndex < unchecked((uint)target);
        if (pattern == TrackedWritePattern.Clustered)
        {
            int clusterRows = Math.Max(1, target / 100);
            return entityIndex % 1_000U < unchecked((uint)clusterRows);
        }
        uint mixed = unchecked(entityIndex * 0x9E37_79B9U);
        mixed ^= mixed >> 16;
        return mixed % unchecked((uint)population) < unchecked((uint)target);
    }
}

internal struct ChangedScalarCountJob : IReadOnlyChunkJob<BenchmarkPosition>
{
    internal int Rows;
    public void Execute(in ReadOnlyComponentChunk<BenchmarkPosition> chunk) => Rows += chunk.Count;
}

internal enum PublicJobFamily
{
    ReadRead,
    ReadReadWrite,
    ReadWriteWrite,
    ReadReadReadWrite,
    ReadReadWriteWrite,
}

internal enum PublicJobExecutionOrder
{
    PublicFirst,
    InternalFirst,
}

internal enum TrackedWritePattern
{
    Contiguous,
    Clustered,
    RandomInChunk,
    Dense,
}

internal readonly record struct JobInvocation(long Rows, long Checksum);

internal sealed class PublicJobBenchmarkReport
{
    public required string SuiteName { get; init; }
    public required string ReportFormat { get; init; }
    public required string KernelVersion { get; init; }
    public required string SemanticProfile { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string RuntimeVersion { get; init; }
    public required int ProcessorCount { get; init; }
    public required string OperatingSystem { get; init; }
    public required int PlanSizeBytes { get; init; }
    public required long NewPersistentReservationBytes { get; init; }
    public required bool AllocationFree { get; init; }
    public required bool CanonicalChecksPassed { get; init; }
    public required IReadOnlyList<PublicJobBenchmarkResult> TraversalResults { get; init; }
    public required IReadOnlyList<TrackedJobBenchmarkResult> TrackedWriteResults { get; init; }
    public required IReadOnlyList<PublicJobDispatchResult> DispatchResults { get; init; }
}

internal sealed class PublicJobBenchmarkResult
{
    public required string Family { get; init; }
    public required int Scale { get; init; }
    public required string Order { get; init; }
    public required string Surface { get; init; }
    public required int Iterations { get; init; }
    public required int InvocationsPerSample { get; init; }
    public required long OperationsPerSample { get; init; }
    public required int LogicalBytesPerRow { get; init; }
    public required double MeanNanosecondsPerRow { get; init; }
    public required double P95NanosecondsPerRow { get; init; }
    public required double EffectiveLogicalGigabytesPerSecond { get; init; }
    public required long ManagedAllocationBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
}

internal sealed class TrackedJobBenchmarkResult
{
    public required string Pattern { get; init; }
    public required int Population { get; init; }
    public required int TargetWrites { get; init; }
    public required int RowsVisited { get; init; }
    public required long UniqueRowsWritten { get; init; }
    public required double MeanActualWrites { get; init; }
    public required double MeanChunksTouched { get; init; }
    public required long DenseChunkStamps { get; init; }
    public required long ExactRowStamps { get; init; }
    public required int ExactRowsDelivered { get; init; }
    public required double MeanNanosecondsPerVisitedRow { get; init; }
    public required double P95NanosecondsPerVisitedRow { get; init; }
    public required double MeanNanosecondsPerActualWrite { get; init; }
    public required double P95NanosecondsPerActualWrite { get; init; }
    public required long ManagedAllocationBytes { get; init; }
    public required long Checksum { get; init; }
}

internal sealed class PublicJobDispatchResult
{
    public required string Shape { get; init; }
    public required int Population { get; init; }
    public required int InvocationCount { get; init; }
    public required int Iterations { get; init; }
    public required long RowsPerSample { get; init; }
    public required double MeanNanosecondsPerInvocation { get; init; }
    public required double P95NanosecondsPerInvocation { get; init; }
    public required double MeanNanosecondsPerRow { get; init; }
    public required long ManagedAllocationBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
}
