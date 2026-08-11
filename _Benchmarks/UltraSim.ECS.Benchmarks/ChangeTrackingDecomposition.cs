using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraSim.ECS.Benchmarks;

/// <summary>
/// Instrumented ECS 3.0.35 investigation. This is deliberately a benchmark-only
/// decomposition, not a production API or a kernel version checkpoint.
/// </summary>
internal static class ChangeTrackingDecompositionProgram
{
    internal static int Run(string[] args)
    {
        ChangeDecompositionConfiguration configuration =
            ChangeDecompositionConfiguration.Parse(args, out string outputPath);
        Console.WriteLine(
            $"ECS {EcsKernelVersion.Value} change-tracking decomposition: " +
            $"{configuration.EntityCount:N0} entities, {configuration.WarmupSamples} warmup, " +
            $"{configuration.MeasuredSamples} measured samples, both execution orders");

        ChangeDecompositionReport report = ChangeDecompositionRunner.Run(configuration);
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        }));

        foreach (ChangeDecompositionMeasurement result in report.Measurements.Where(
                     static result => result.ChangedRows == 5_000 &&
                         result.Locality == ChangeLocality.Random))
        {
            Console.WriteLine(
                $"{result.Order} {result.Surface}/{result.Case}: " +
                $"mean={result.MeanNanosecondsPerOperation:F3} ns/{result.OperationUnit}, " +
                $"p95={result.P95NanosecondsPerOperation:F3} ns/{result.OperationUnit}, " +
                $"elapsed={result.MeanMicroseconds:F3} us, alloc={result.AllocatedBytes} B");
        }
        Console.WriteLine(
            $"Correctness={report.CorrectnessPassed}; zero-allocation={report.ZeroAllocationPassed}; " +
            $"results={outputPath}");
        return report.CorrectnessPassed && report.ZeroAllocationPassed ? 0 : 1;
    }
}

internal sealed class ChangeDecompositionConfiguration
{
    public int EntityCount { get; init; } = 100_000;
    public int WarmupSamples { get; init; } = 8;
    public int MeasuredSamples { get; init; } = 31;
    public int MinimumTimedWrites { get; init; } = 10_000;
    public ulong Seed { get; init; } = 0xA0761D6478BD642FUL;
    public bool ReversePassOrder { get; init; }

    internal static ChangeDecompositionConfiguration Parse(string[] args, out string outputPath)
    {
        int entities = 100_000;
        int warmup = 8;
        int samples = 31;
        int minimumWrites = 10_000;
        ulong seed = 0xA0761D6478BD642FUL;
        bool reverseFirst = false;
        outputPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "results",
            "ecs-3.0.35-change-tracking-decomposition.json"));

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.Equals("--change-entities", StringComparison.OrdinalIgnoreCase))
                entities = ParseInt(args, ref index, argument);
            else if (argument.Equals("--change-warmup", StringComparison.OrdinalIgnoreCase))
                warmup = ParseInt(args, ref index, argument);
            else if (argument.Equals("--change-samples", StringComparison.OrdinalIgnoreCase))
                samples = ParseInt(args, ref index, argument);
            else if (argument.Equals("--change-minimum-writes", StringComparison.OrdinalIgnoreCase))
                minimumWrites = ParseInt(args, ref index, argument);
            else if (argument.Equals("--change-seed", StringComparison.OrdinalIgnoreCase))
                seed = ParseULong(args, ref index, argument);
            else if (argument.Equals("--change-reverse-first", StringComparison.OrdinalIgnoreCase))
                reverseFirst = true;
            else if (argument.Equals("--change-output", StringComparison.OrdinalIgnoreCase) ||
                     argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
                outputPath = Path.GetFullPath(ParseString(args, ref index, argument));
        }

        var configuration = new ChangeDecompositionConfiguration
        {
            EntityCount = entities,
            WarmupSamples = warmup,
            MeasuredSamples = samples,
            MinimumTimedWrites = minimumWrites,
            Seed = seed,
            ReversePassOrder = reverseFirst,
        };
        if (entities < 100_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entities),
                "The stable density/locality matrix requires at least 100,000 resident entities.");
        }
        if (warmup < 1 || samples < 5 || minimumWrites < 1)
            throw new ArgumentOutOfRangeException(nameof(args));
        return configuration;
    }

    private static int ParseInt(string[] args, ref int index, string argument) =>
        int.Parse(ParseString(args, ref index, argument), System.Globalization.CultureInfo.InvariantCulture);

    private static ulong ParseULong(string[] args, ref int index, string argument) =>
        ulong.Parse(ParseString(args, ref index, argument), System.Globalization.CultureInfo.InvariantCulture);

    private static string ParseString(string[] args, ref int index, string argument)
    {
        if (++index >= args.Length)
            throw new ArgumentException($"{argument} requires a value.");
        return args[index];
    }
}

internal static class ChangeDecompositionRunner
{
    private static readonly ChangeWriteCase[] PrimaryWriteCases =
    [
        ChangeWriteCase.PreboundWriteNoTracking,
        ChangeWriteCase.CheckedLocationWriteNoTracking,
        ChangeWriteCase.DenseStampOnlyPerRow,
        ChangeWriteCase.ExactStampOnlyPerRow,
        ChangeWriteCase.CheckedDenseTracked,
        ChangeWriteCase.CheckedExactTracked,
        ChangeWriteCase.DenseSharedVersionPerRow,
        ChangeWriteCase.ExactSharedVersionPerRow,
        ChangeWriteCase.DenseSharedVersionChunkGrouped,
        ChangeWriteCase.ExactSharedVersionChunkGrouped,
    ];

    private static readonly ChangeWriteCase[] SweepWriteCases =
    [
        ChangeWriteCase.CheckedDenseTracked,
        ChangeWriteCase.CheckedExactTracked,
        ChangeWriteCase.DenseSharedVersionPerRow,
        ChangeWriteCase.ExactSharedVersionPerRow,
        ChangeWriteCase.DenseSharedVersionChunkGrouped,
        ChangeWriteCase.ExactSharedVersionChunkGrouped,
    ];

    internal static ChangeDecompositionReport Run(ChangeDecompositionConfiguration configuration)
    {
        var measurements = new List<ChangeDecompositionMeasurement>(512);
        var metadata = new List<ChangeDecompositionWorldFacts>(4);
        PrimeJit(configuration);
        ChangeExecutionOrder[] passOrder = Enum.GetValues<ChangeExecutionOrder>();
        if (configuration.ReversePassOrder)
            Array.Reverse(passOrder);
        foreach (ChangeExecutionOrder order in passOrder)
        {
            using (var fixture = new ChangeWriteDecompositionFixture(configuration))
            {
                metadata.Add(fixture.CaptureFacts($"Write-{order}"));
                ChangeSelection primary = fixture.GetSelection(ChangeLocality.Random, 5_000);
                ChangeWriteCase[] primaryCases = Ordered(PrimaryWriteCases, order);
                foreach (ChangeWriteCase @case in primaryCases)
                {
                    measurements.Add(MeasureWrite(
                        fixture,
                        configuration,
                        order,
                        "PrimaryDecomposition",
                        @case,
                        primary));
                }

                ChangeWriteCase[] sweepCases = Ordered(SweepWriteCases, order);
                ChangeSelection[] selections = Ordered(fixture.Selections, order);
                foreach (ChangeSelection selection in selections)
                {
                    foreach (ChangeWriteCase @case in sweepCases)
                    {
                        if (selection.Locality == ChangeLocality.Random &&
                            selection.Count == 5_000)
                        {
                            continue;
                        }
                        measurements.Add(MeasureWrite(
                            fixture,
                            configuration,
                            order,
                            "DensityLocalitySweep",
                            @case,
                            selection));
                    }
                }

                measurements.Add(MeasureVersionAdvance(fixture, configuration, order));
            }

            using (var fixture = new ChangeConsumerDecompositionFixture(configuration))
            {
                metadata.Add(fixture.CaptureFacts($"Consumer-{order}"));
                ChangeSelection[] selections = Ordered(fixture.Selections, order);
                ChangeConsumerCase[] consumers = Ordered(
                    Enum.GetValues<ChangeConsumerCase>(),
                    order);
                foreach (ChangeSelection selection in selections)
                {
                    foreach (ChangeConsumerCase consumer in consumers)
                    {
                        measurements.Add(MeasureConsumer(
                            fixture,
                            configuration,
                            order,
                            consumer,
                            selection));
                    }
                }
            }
        }

        bool zeroAllocation = measurements.All(static result => result.AllocatedBytes == 0);
        return new ChangeDecompositionReport
        {
            SuiteName = "UltraSim ECS change-tracking cost decomposition",
            KernelVersion = EcsKernelVersion.Value,
            SemanticProfile = "ultrasim-ecs-change-decomposition-v1",
            Status = "Instrumented investigation; no production kernel strategy or API change",
            TimestampUtc = DateTimeOffset.UtcNow,
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            RuntimeControls = new Dictionary<string, string?>
            {
                ["DOTNET_TieredCompilation"] = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                ["DOTNET_TieredPGO"] = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                ["DOTNET_GCServer"] = Environment.GetEnvironmentVariable("DOTNET_GCServer"),
            },
            Configuration = configuration,
            CorrectnessPassed = true,
            ZeroAllocationPassed = zeroAllocation,
            WorldFacts = metadata,
            Measurements = measurements,
        };
    }

    private static void PrimeJit(ChangeDecompositionConfiguration configuration)
    {
        const int primingCalls = 64;
        using (var fixture = new ChangeWriteDecompositionFixture(configuration))
        {
            ChangeSelection selection = fixture.GetSelection(ChangeLocality.Random, 5_000);
            foreach (ChangeWriteCase @case in PrimaryWriteCases)
            {
                for (int call = 0; call < primingCalls; call++)
                    _ = fixture.Execute(@case, selection, repetitions: 1);
            }
            for (int call = 0; call < primingCalls; call++)
                _ = fixture.ExecuteVersionAdvance(100_000);
        }

        using var consumer = new ChangeConsumerDecompositionFixture(configuration);
        ChangeSelection consumerSelection = consumer.GetSelection(ChangeLocality.Random, 5_000);
        foreach (ChangeConsumerCase @case in Enum.GetValues<ChangeConsumerCase>())
        {
            for (int call = 0; call < primingCalls; call++)
            {
                consumer.Prepare(@case, consumerSelection);
                _ = consumer.Consume(@case, consumerSelection);
            }
        }
    }

    private static ChangeDecompositionMeasurement MeasureWrite(
        ChangeWriteDecompositionFixture fixture,
        ChangeDecompositionConfiguration configuration,
        ChangeExecutionOrder order,
        string scope,
        ChangeWriteCase @case,
        ChangeSelection selection)
    {
        int repetitions = Math.Max(
            1,
            (configuration.MinimumTimedWrites + selection.Count - 1) / selection.Count);
        long operations = checked((long)selection.Count * repetitions);
        TimedSamples samples = Measure(
            configuration,
            operations,
            () => fixture.Execute(@case, selection, repetitions));
        fixture.ValidateTrackedPublication(@case, selection);
        return CreateMeasurement(
            order,
            "Write",
            scope,
            @case.ToString(),
            selection,
            samples,
            operations,
            "write",
            deliveredRows: 0);
    }

    private static ChangeDecompositionMeasurement MeasureVersionAdvance(
        ChangeWriteDecompositionFixture fixture,
        ChangeDecompositionConfiguration configuration,
        ChangeExecutionOrder order)
    {
        const int operations = 100_000;
        TimedSamples samples = Measure(
            configuration,
            operations,
            () => fixture.ExecuteVersionAdvance(operations));
        ChangeSelection selection = fixture.GetSelection(ChangeLocality.Dense, configuration.EntityCount);
        return CreateMeasurement(
            order,
            "Primitive",
            "PrimaryDecomposition",
            "ChangeVersionNextOnly",
            selection,
            samples,
            operations,
            "advance",
            deliveredRows: 0);
    }

    private static ChangeDecompositionMeasurement MeasureConsumer(
        ChangeConsumerDecompositionFixture fixture,
        ChangeDecompositionConfiguration configuration,
        ChangeExecutionOrder order,
        ChangeConsumerCase @case,
        ChangeSelection selection)
    {
        TimedSamples samples = Measure(
            configuration,
            operations: 1,
            prepare: () => fixture.Prepare(@case, selection),
            execute: () => fixture.Consume(@case, selection));
        long deliveredRows = @case == ChangeConsumerCase.ExactChangedRows
            ? selection.Count
            : selection.ResidentRowsInTouchedChunks;
        ChangeDecompositionMeasurement result = CreateMeasurement(
            order,
            "Consume",
            "DensityLocalitySweep",
            @case.ToString(),
            selection,
            samples,
            Math.Max(1, deliveredRows),
            "delivered-row",
            deliveredRows);
        result.MeanNanosecondsPerChangedRow =
            samples.MeanNanoseconds / selection.Count;
        result.P95NanosecondsPerChangedRow =
            samples.P95Nanoseconds / selection.Count;
        result.MeanNanosecondsPerResidentRowInspected =
            samples.MeanNanoseconds / selection.ResidentRowsInTouchedChunks;
        result.P95NanosecondsPerResidentRowInspected =
            samples.P95Nanoseconds / selection.ResidentRowsInTouchedChunks;
        return result;
    }

    private static ChangeDecompositionMeasurement CreateMeasurement(
        ChangeExecutionOrder order,
        string surface,
        string scope,
        string @case,
        ChangeSelection selection,
        TimedSamples samples,
        long operations,
        string operationUnit,
        long deliveredRows) => new()
    {
        Order = order,
        Surface = surface,
        Scope = scope,
        Case = @case,
        Locality = selection.Locality,
        ChangedRows = selection.Count,
        TouchedChunks = selection.TouchedChunks,
        TotalOccupiedChunks = selection.TotalOccupiedChunks,
        ResidentRowsInTouchedChunks = selection.ResidentRowsInTouchedChunks,
        ExactChangedRuns = selection.ExactChangedRuns,
        DeliveredRows = deliveredRows,
        FalsePositiveRows = deliveredRows == 0 ? 0 : deliveredRows - selection.Count,
        TimedOperations = operations,
        OperationUnit = operationUnit,
        Samples = samples.Count,
        MeanMicroseconds = samples.MeanNanoseconds / 1_000d,
        P50Microseconds = samples.P50Nanoseconds / 1_000d,
        P95Microseconds = samples.P95Nanoseconds / 1_000d,
        P99Microseconds = samples.P99Nanoseconds / 1_000d,
        MeanNanosecondsPerOperation = samples.MeanNanoseconds / operations,
        P95NanosecondsPerOperation = samples.P95Nanoseconds / operations,
        AllocatedBytes = samples.AllocatedBytes,
        Gen0Collections = samples.Gen0Collections,
        Gen1Collections = samples.Gen1Collections,
        Gen2Collections = samples.Gen2Collections,
        Checksum = samples.Checksum,
    };

    private static TimedSamples Measure(
        ChangeDecompositionConfiguration configuration,
        long operations,
        Func<long> execute) =>
        Measure(configuration, operations, prepare: null, execute);

    private static TimedSamples Measure(
        ChangeDecompositionConfiguration configuration,
        long operations,
        Action? prepare,
        Func<long> execute)
    {
        for (int warmup = 0; warmup < configuration.WarmupSamples; warmup++)
        {
            prepare?.Invoke();
            s_checksum = unchecked(s_checksum + execute());
        }

        var ticks = new long[configuration.MeasuredSamples];
        long allocated = 0;
        long checksum = 0;
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        for (int sample = 0; sample < ticks.Length; sample++)
        {
            prepare?.Invoke();
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            long result = execute();
            long stopped = Stopwatch.GetTimestamp();
            long allocationAfter = GC.GetAllocatedBytesForCurrentThread();
            ticks[sample] = stopped - started;
            allocated += allocationAfter - allocationBefore;
            checksum = unchecked(checksum + result);
        }
        s_checksum = unchecked(s_checksum + checksum + operations);
        if (allocated != 0)
        {
            throw new InvalidOperationException(
                $"A warmed change-decomposition path allocated {allocated} managed bytes.");
        }

        Array.Sort(ticks);
        double tickToNanoseconds = 1_000_000_000d / Stopwatch.Frequency;
        double meanTicks = ticks.Average(static value => (double)value);
        return new TimedSamples(
            ticks.Length,
            meanTicks * tickToNanoseconds,
            Percentile(ticks, 0.50) * tickToNanoseconds,
            Percentile(ticks, 0.95) * tickToNanoseconds,
            Percentile(ticks, 0.99) * tickToNanoseconds,
            allocated,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            checksum);
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static T[] Ordered<T>(T[] values, ChangeExecutionOrder order)
    {
        T[] copy = values.ToArray();
        if (order == ChangeExecutionOrder.Reverse)
            Array.Reverse(copy);
        return copy;
    }

    private static long s_checksum;
}

internal abstract class ChangeDecompositionFixture : IDisposable
{
    protected readonly EcsWorld<NativeChunkArena> World;
    protected readonly ArchetypeId Archetype;
    protected readonly ArchetypeStorage<NativeChunkArena> Storage;
    protected readonly ArchetypeChunkStore<NativeChunkArena> Store;
    protected readonly Entity[] Entities;
    protected readonly EntityLocation[] Locations;
    protected readonly ArchetypeComponentBinding<ChangeDecompositionIdentity> IdentityBinding;
    protected readonly ArchetypeComponentBinding<ChangeDecompositionRaw> RawBinding;
    protected readonly ArchetypeComponentBinding<ChangeDecompositionDense> DenseBinding;
    protected readonly ArchetypeComponentBinding<ChangeDecompositionExact> ExactBinding;
    protected readonly ComponentLookupPlan<NativeChunkArena, ChangeDecompositionDense> DenseLookup;
    protected readonly ComponentLookupPlan<NativeChunkArena, ChangeDecompositionExact> ExactLookup;
    protected readonly SparseRowChangeVersions ExactSparse;
    protected readonly ArchetypeChangedReadQueryPlan<NativeChunkArena, ChangeDecompositionIdentity> DenseChangedPlan;
    protected readonly ArchetypeChangedReadQueryPlan<NativeChunkArena, ChangeDecompositionIdentity> ExactChangedPlan;

    protected ChangeDecompositionFixture(ChangeDecompositionConfiguration configuration)
    {
        Configuration = configuration;
        ComponentSchema schema = ChangeTrackingDecompositionSchema.CreateSchema();
        World = new EcsWorld<NativeChunkArena>(
            schema,
            new EntityCapacity(configuration.EntityCount),
            maximumArchetypes: 1,
            static capacity => new NativeChunkArena(capacity),
            maximumQueryPlans: 4);
        ComponentId identity = ChangeTrackingDecompositionSchema.GetComponentId<ChangeDecompositionIdentity>();
        ComponentId raw = ChangeTrackingDecompositionSchema.GetComponentId<ChangeDecompositionRaw>();
        ComponentId dense = ChangeTrackingDecompositionSchema.GetComponentId<ChangeDecompositionDense>();
        ComponentId exact = ChangeTrackingDecompositionSchema.GetComponentId<ChangeDecompositionExact>();
        ComponentId[] signature = [identity, raw, dense, exact];
        Array.Sort(signature, static (left, right) => left.Value.CompareTo(right.Value));
        Archetype = World.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB32,
            maximumChunks: checked(((configuration.EntityCount + 511) / 512) + 8));
        IdentityBinding = World.BindComponent<ChangeDecompositionIdentity>(Archetype, identity);
        RawBinding = World.BindComponent<ChangeDecompositionRaw>(Archetype, raw);
        DenseBinding = World.BindComponent<ChangeDecompositionDense>(Archetype, dense);
        ExactBinding = World.BindComponent<ChangeDecompositionExact>(Archetype, exact);
        DenseLookup = World.PrepareComponentLookup<ChangeDecompositionDense>();
        ExactLookup = World.PrepareComponentLookup<ChangeDecompositionExact>();
        DenseChangedPlan = World.PrepareChangedReadQuery<ChangeDecompositionIdentity>(identity, dense);
        ExactChangedPlan = World.PrepareChangedRowsQuery<ChangeDecompositionIdentity>(identity, exact);
        World.Seal();
        World.Initialize();
        World.BeginRun();

        Storage = World.GetArchetypeStorage(Archetype);
        Store = Storage.Store;
        ExactSparse = Store.EnableSparseRowChangeTracking(ExactBinding.ColumnIndex);
        Entities = new Entity[configuration.EntityCount];
        Locations = new EntityLocation[configuration.EntityCount];
        for (int logical = 0; logical < Entities.Length; logical++)
        {
            if (!World.TryCreateDefault(Archetype, out Entity entity) ||
                !World.TryGetLocation(entity, out EntityLocation location))
            {
                throw new InvalidOperationException("Change-decomposition bootstrap exhausted bounded capacity.");
            }
            Entities[logical] = entity;
            Locations[logical] = location;
            Store.GetBoundComponentRef<ChangeDecompositionIdentity>(
                location.ChunkRow,
                IdentityBinding.ColumnIndex) = new ChangeDecompositionIdentity { Value = logical };
        }

        Selections = BuildSelections(configuration, Locations, Store.GetSnapshot());
        ValidatePhysicalOwnership();
    }

    protected ChangeDecompositionConfiguration Configuration { get; }
    internal ChangeSelection[] Selections { get; }

    internal ChangeSelection GetSelection(ChangeLocality locality, int count) =>
        Selections.Single(selection => selection.Locality == locality && selection.Count == count);

    internal ChangeDecompositionWorldFacts CaptureFacts(string name)
    {
        ArchetypeChunkSnapshot snapshot = Store.GetSnapshot();
        return new ChangeDecompositionWorldFacts
        {
            Name = name,
            EntityCount = snapshot.EntityCount,
            RowCapacity = snapshot.RowCapacity,
            OccupiedChunks = snapshot.OccupiedChunkCount,
            AcquiredChunks = snapshot.AcquiredChunkCount,
            DenseChangeMetadataBytes = snapshot.ChangeVersionMetadataBytes,
            ExactChangeMetadataBytes = snapshot.SparseRowChangeVersionMetadataBytes,
        };
    }

    public void Dispose() => World.Dispose();

    protected bool TryResolve(int logical, out EntityLocation location)
    {
        if (!World.TryGetLocation(Entities[logical], out location) ||
            location.ArchetypeId != Archetype)
        {
            return false;
        }
        return true;
    }

    private void ValidatePhysicalOwnership()
    {
        for (int logical = 0; logical < Entities.Length; logical++)
        {
            if (!TryResolve(logical, out EntityLocation location) || location != Locations[logical])
                throw new InvalidOperationException("Change-decomposition location bootstrap is inconsistent.");
            ChangeDecompositionIdentity identity = Store.GetBoundComponentRef<ChangeDecompositionIdentity>(
                location.ChunkRow,
                IdentityBinding.ColumnIndex);
            if (identity.Value != logical)
                throw new InvalidOperationException("Change-decomposition identity ownership is inconsistent.");
        }
    }

    private static ChangeSelection[] BuildSelections(
        ChangeDecompositionConfiguration configuration,
        EntityLocation[] locations,
        ArchetypeChunkSnapshot snapshot)
    {
        int[] randomOrder = Enumerable.Range(0, configuration.EntityCount).ToArray();
        var random = new SplitMix64(configuration.Seed);
        for (int index = randomOrder.Length - 1; index > 0; index--)
        {
            int swap = random.NextInt(index + 1);
            (randomOrder[index], randomOrder[swap]) = (randomOrder[swap], randomOrder[index]);
        }

        var byChunk = new List<int>[snapshot.MaximumChunks];
        var occupancy = new int[snapshot.MaximumChunks];
        for (int logical = 0; logical < locations.Length; logical++)
        {
            int chunk = checked((int)locations[logical].ChunkRow.ChunkIndex);
            (byChunk[chunk] ??= []).Add(logical);
            occupancy[chunk]++;
        }
        List<int>[] occupied = byChunk.Where(static rows => rows is not null).Cast<List<int>>().ToArray();
        var clusteredOrder = new int[configuration.EntityCount];
        int clusteredCount = 0;
        const int clusterRows = 256;
        for (int offset = 0; clusteredCount < clusteredOrder.Length; offset += clusterRows)
        {
            foreach (List<int> rows in occupied)
            {
                int count = Math.Min(clusterRows, rows.Count - offset);
                if (count <= 0)
                    continue;
                rows.CopyTo(offset, clusteredOrder, clusteredCount, count);
                clusteredCount += count;
            }
        }

        var selections = new List<ChangeSelection>(19);
        foreach (int count in ChangeDecompositionRunnerChangedRows.Counts)
        {
            selections.Add(CreateSelection(ChangeLocality.Contiguous, count,
                Enumerable.Range(0, count).ToArray(), locations, occupancy, snapshot.OccupiedChunkCount));
            selections.Add(CreateSelection(ChangeLocality.Clustered256, count,
                clusteredOrder.AsSpan(0, count).ToArray(), locations, occupancy, snapshot.OccupiedChunkCount));
            selections.Add(CreateSelection(ChangeLocality.Random, count,
                randomOrder.AsSpan(0, count).ToArray(), locations, occupancy, snapshot.OccupiedChunkCount));
        }
        selections.Add(CreateSelection(ChangeLocality.Dense, configuration.EntityCount,
            Enumerable.Range(0, configuration.EntityCount).ToArray(), locations, occupancy, snapshot.OccupiedChunkCount));
        return selections.ToArray();
    }

    private static ChangeSelection CreateSelection(
        ChangeLocality locality,
        int count,
        int[] logicalIndices,
        EntityLocation[] locations,
        int[] occupancy,
        int occupiedChunkCount)
    {
        int[] grouped = logicalIndices.ToArray();
        Array.Sort(grouped, (left, right) =>
        {
            ChunkRow leftRow = locations[left].ChunkRow;
            ChunkRow rightRow = locations[right].ChunkRow;
            int chunk = leftRow.ChunkIndex.CompareTo(rightRow.ChunkIndex);
            return chunk != 0 ? chunk : leftRow.Row.CompareTo(rightRow.Row);
        });
        var touched = new bool[occupancy.Length];
        int touchedChunks = 0;
        int residentRows = 0;
        foreach (int logical in logicalIndices)
        {
            int chunk = checked((int)locations[logical].ChunkRow.ChunkIndex);
            if (touched[chunk])
                continue;
            touched[chunk] = true;
            touchedChunks++;
            residentRows += occupancy[chunk];
        }
        int exactRuns = 0;
        ChunkRow previous = ChunkRow.Invalid;
        foreach (int logical in grouped)
        {
            ChunkRow row = locations[logical].ChunkRow;
            if (!previous.IsValid ||
                row.ChunkIndex != previous.ChunkIndex ||
                row.Row != previous.Row + 1)
            {
                exactRuns++;
            }
            previous = row;
        }
        return new ChangeSelection(
            locality,
            count,
            logicalIndices,
            grouped,
            touchedChunks,
            occupiedChunkCount,
            residentRows,
            exactRuns);
    }
}

// Keeps the density list private to the runner while allowing the fixture's cold
// selection builder to share the exact immutable contract.
internal static class ChangeDecompositionRunnerChangedRows
{
    internal static ReadOnlySpan<int> Counts => [100, 1_000, 5_000, 10_000, 25_000, 50_000];
}

internal sealed class ChangeWriteDecompositionFixture : ChangeDecompositionFixture
{
    internal ChangeWriteDecompositionFixture(ChangeDecompositionConfiguration configuration)
        : base(configuration)
    {
    }

    internal long Execute(ChangeWriteCase @case, ChangeSelection selection, int repetitions) => @case switch
    {
        ChangeWriteCase.PreboundWriteNoTracking => ExecutePreboundWrite(selection, repetitions),
        ChangeWriteCase.CheckedLocationWriteNoTracking => ExecuteCheckedLocationWrite(selection, repetitions),
        ChangeWriteCase.DenseStampOnlyPerRow => ExecuteStampOnly(selection, repetitions, exact: false),
        ChangeWriteCase.ExactStampOnlyPerRow => ExecuteStampOnly(selection, repetitions, exact: true),
        ChangeWriteCase.CheckedDenseTracked => ExecuteCheckedTracked(selection, repetitions, exact: false),
        ChangeWriteCase.CheckedExactTracked => ExecuteCheckedTracked(selection, repetitions, exact: true),
        ChangeWriteCase.DenseSharedVersionPerRow => ExecuteSharedPerRow(selection, repetitions, exact: false),
        ChangeWriteCase.ExactSharedVersionPerRow => ExecuteSharedPerRow(selection, repetitions, exact: true),
        ChangeWriteCase.DenseSharedVersionChunkGrouped => ExecuteSharedGrouped(selection, repetitions, exact: false),
        ChangeWriteCase.ExactSharedVersionChunkGrouped => ExecuteSharedGrouped(selection, repetitions, exact: true),
        _ => throw new ArgumentOutOfRangeException(nameof(@case)),
    };

    internal long ExecuteVersionAdvance(int operations)
    {
        ChangeVersion version = new(1);
        for (int operation = 0; operation < operations; operation++)
            version = version.Next();
        return unchecked((long)version.Value);
    }

    internal void ValidateTrackedPublication(ChangeWriteCase @case, ChangeSelection selection)
    {
        bool exact;
        switch (@case)
        {
            case ChangeWriteCase.CheckedDenseTracked:
            case ChangeWriteCase.DenseSharedVersionPerRow:
            case ChangeWriteCase.DenseSharedVersionChunkGrouped:
                exact = false;
                break;
            case ChangeWriteCase.CheckedExactTracked:
            case ChangeWriteCase.ExactSharedVersionPerRow:
            case ChangeWriteCase.ExactSharedVersionChunkGrouped:
                exact = true;
                break;
            default:
                return;
        }

        ChangeCursor cursor = World.CreateChangeCursorFromNow();
        _ = Execute(@case, selection, repetitions: 1);
        var selected = new bool[Configuration.EntityCount];
        foreach (int logical in selection.LogicalIndices)
            selected[logical] = true;
        var visitor = new PublicationValidationVisitor(selected);
        int visited = exact
            ? World.VisitChangedReadRows(ExactChangedPlan, ref cursor, ref visitor)
            : World.VisitChangedReadChunks(DenseChangedPlan, ref cursor, ref visitor);
        int expected = exact ? selection.Count : selection.ResidentRowsInTouchedChunks;
        if (visited != expected || visitor.Rows != expected || visitor.SelectedRows != selection.Count)
        {
            throw new InvalidOperationException(
                $"{@case} publication validation delivered {visited}/{visitor.Rows} rows and " +
                $"{visitor.SelectedRows} selected; expected {expected} and {selection.Count}.");
        }
    }

    private struct PublicationValidationVisitor : IComponentChunkReadVisitor<ChangeDecompositionIdentity>
    {
        private readonly bool[] _selected;

        internal PublicationValidationVisitor(bool[] selected)
        {
            _selected = selected;
            Rows = 0;
            SelectedRows = 0;
        }

        internal int Rows;
        internal int SelectedRows;

        public void Execute(in ReadOnlyComponentChunk<ChangeDecompositionIdentity> chunk)
        {
            int selectedRows = SelectedRows;
            for (int row = 0; row < chunk.Count; row++)
            {
                if (_selected[chunk.Components[row].Value])
                    selectedRows++;
            }
            Rows += chunk.Count;
            SelectedRows = selectedRows;
        }
    }

    private long ExecutePreboundWrite(ChangeSelection selection, int repetitions)
    {
        long checksum = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            int salt = unchecked((repetition + 1) * 1_000_003);
            foreach (int logical in selection.LogicalIndices)
            {
                int value = unchecked(salt ^ logical * 65_537);
                Store.GetBoundComponentRef<ChangeDecompositionRaw>(
                    Locations[logical].ChunkRow,
                    RawBinding.ColumnIndex) = new ChangeDecompositionRaw { Value = value };
                checksum = unchecked(checksum + value);
            }
        }
        return checksum;
    }

    private long ExecuteCheckedLocationWrite(ChangeSelection selection, int repetitions)
    {
        long checksum = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            int salt = unchecked((repetition + 1) * 1_000_003);
            foreach (int logical in selection.LogicalIndices)
            {
                if (!TryResolve(logical, out EntityLocation location))
                    throw new InvalidOperationException("A checked untracked write lost its entity.");
                int value = unchecked(salt ^ logical * 65_537);
                Store.GetBoundComponentRef<ChangeDecompositionRaw>(
                    location.ChunkRow,
                    RawBinding.ColumnIndex) = new ChangeDecompositionRaw { Value = value };
                checksum = unchecked(checksum + value);
            }
        }
        return checksum;
    }

    private long ExecuteStampOnly(ChangeSelection selection, int repetitions, bool exact)
    {
        int columnIndex = exact ? ExactBinding.ColumnIndex : DenseBinding.ColumnIndex;
        long checksum = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            var version = new ChangeVersion(checked((ulong)repetition + 1UL));
            foreach (int logical in selection.LogicalIndices)
            {
                ChunkRow row = Locations[logical].ChunkRow;
                Store.StampSparseChangeVersion(row.ChunkIndex, columnIndex, row.Row, 1, version);
                checksum = unchecked(checksum + row.Row + (long)version.Value);
            }
        }
        return checksum;
    }

    private long ExecuteCheckedTracked(ChangeSelection selection, int repetitions, bool exact)
    {
        long checksum = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            int salt = unchecked((repetition + 1) * 1_000_003);
            foreach (int logical in selection.LogicalIndices)
            {
                int value = unchecked(salt ^ logical * 65_537);
                bool written = exact
                    ? World.TrySetComponentPrepared(
                        Entities[logical],
                        ExactLookup,
                        new ChangeDecompositionExact { Value = value })
                    : World.TrySetComponentPrepared(
                        Entities[logical],
                        DenseLookup,
                        new ChangeDecompositionDense { Value = value });
                if (!written)
                    throw new InvalidOperationException("A checked tracked write lost its entity.");
                checksum = unchecked(checksum + value);
            }
        }
        return checksum;
    }

    private long ExecuteSharedPerRow(ChangeSelection selection, int repetitions, bool exact)
    {
        long checksum = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            int salt = unchecked((repetition + 1) * 1_000_003);
            int first = selection.LogicalIndices[0];
            int firstValue = unchecked(salt ^ first * 65_537);
            bool firstWritten = exact
                ? World.TrySetComponentPrepared(
                    Entities[first],
                    ExactLookup,
                    new ChangeDecompositionExact { Value = firstValue })
                : World.TrySetComponentPrepared(
                    Entities[first],
                    DenseLookup,
                    new ChangeDecompositionDense { Value = firstValue });
            if (!firstWritten)
                throw new InvalidOperationException("A shared-version write lost its first entity.");
            ChangeVersion version = World.CurrentChangeVersion;
            checksum = unchecked(checksum + firstValue);
            for (int index = 1; index < selection.LogicalIndices.Length; index++)
            {
                int logical = selection.LogicalIndices[index];
                if (!TryResolve(logical, out EntityLocation location))
                    throw new InvalidOperationException("A shared-version write lost its entity.");
                int value = unchecked(salt ^ logical * 65_537);
                if (exact)
                {
                    Store.GetBoundComponentRef<ChangeDecompositionExact>(
                        location.ChunkRow,
                        ExactBinding.ColumnIndex) = new ChangeDecompositionExact { Value = value };
                    Store.StampSparseChangeVersion(
                        location.ChunkRow.ChunkIndex,
                        ExactBinding.ColumnIndex,
                        location.ChunkRow.Row,
                        1,
                        version);
                }
                else
                {
                    Store.GetBoundComponentRef<ChangeDecompositionDense>(
                        location.ChunkRow,
                        DenseBinding.ColumnIndex) = new ChangeDecompositionDense { Value = value };
                    Store.StampSparseChangeVersion(
                        location.ChunkRow.ChunkIndex,
                        DenseBinding.ColumnIndex,
                        location.ChunkRow.Row,
                        1,
                        version);
                }
                checksum = unchecked(checksum + value);
            }
        }
        return checksum;
    }

    private long ExecuteSharedGrouped(ChangeSelection selection, int repetitions, bool exact)
    {
        int[] grouped = selection.ChunkGroupedLogicalIndices;
        long checksum = 0;
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            int salt = unchecked((repetition + 1) * 1_000_003);
            int first = grouped[0];
            int firstValue = unchecked(salt ^ first * 65_537);
            bool firstWritten = exact
                ? World.TrySetComponentPrepared(
                    Entities[first],
                    ExactLookup,
                    new ChangeDecompositionExact { Value = firstValue })
                : World.TrySetComponentPrepared(
                    Entities[first],
                    DenseLookup,
                    new ChangeDecompositionDense { Value = firstValue });
            if (!firstWritten)
                throw new InvalidOperationException("A grouped shared-version write lost its first entity.");
            ChangeVersion version = World.CurrentChangeVersion;
            uint previousChunk = Locations[first].ChunkRow.ChunkIndex;
            checksum = unchecked(checksum + firstValue);
            for (int index = 1; index < grouped.Length; index++)
            {
                int logical = grouped[index];
                if (!TryResolve(logical, out EntityLocation location))
                    throw new InvalidOperationException("A grouped shared-version write lost its entity.");
                ChunkRow row = location.ChunkRow;
                int value = unchecked(salt ^ logical * 65_537);
                if (exact)
                {
                    Store.GetBoundComponentRef<ChangeDecompositionExact>(
                        row,
                        ExactBinding.ColumnIndex) = new ChangeDecompositionExact { Value = value };
                    if (row.ChunkIndex != previousChunk)
                    {
                        Store.StampSparseChangeVersion(
                            row.ChunkIndex,
                            ExactBinding.ColumnIndex,
                            row.Row,
                            1,
                            version);
                    }
                    else
                    {
                        ExactSparse.StampRange(row.ChunkIndex, row.Row, 1, version);
                    }
                }
                else
                {
                    Store.GetBoundComponentRef<ChangeDecompositionDense>(
                        row,
                        DenseBinding.ColumnIndex) = new ChangeDecompositionDense { Value = value };
                    if (row.ChunkIndex != previousChunk)
                    {
                        Store.StampSparseChangeVersion(
                            row.ChunkIndex,
                            DenseBinding.ColumnIndex,
                            row.Row,
                            1,
                            version);
                    }
                }
                previousChunk = row.ChunkIndex;
                checksum = unchecked(checksum + value);
            }
        }
        return checksum;
    }
}

internal sealed class ChangeConsumerDecompositionFixture : ChangeDecompositionFixture
{
    private ChangeCursor _cursor;
    private readonly int[] _selectedEpochs;
    private int _selectionEpoch;

    internal ChangeConsumerDecompositionFixture(ChangeDecompositionConfiguration configuration)
        : base(configuration)
    {
        _selectedEpochs = new int[configuration.EntityCount];
    }

    internal void Prepare(ChangeConsumerCase @case, ChangeSelection selection)
    {
        _cursor = World.CreateChangeCursorFromNow();
        _selectionEpoch++;
        if (_selectionEpoch == int.MaxValue)
        {
            Array.Clear(_selectedEpochs);
            _selectionEpoch = 1;
        }
        foreach (int logical in selection.LogicalIndices)
        {
            _selectedEpochs[logical] = _selectionEpoch;
            int value = unchecked(_selectionEpoch * 1_000_003 ^ logical * 65_537);
            bool written = @case == ChangeConsumerCase.ExactChangedRows
                ? World.TrySetComponentPrepared(
                    Entities[logical],
                    ExactLookup,
                    new ChangeDecompositionExact { Value = value })
                : World.TrySetComponentPrepared(
                    Entities[logical],
                    DenseLookup,
                    new ChangeDecompositionDense { Value = value });
            if (!written)
                throw new InvalidOperationException("Changed-consumer preparation lost an entity.");
        }
    }

    internal long Consume(ChangeConsumerCase @case, ChangeSelection selection)
    {
        var visitor = new SelectedIdentityVisitor(_selectedEpochs, _selectionEpoch);
        int visited = @case == ChangeConsumerCase.ExactChangedRows
            ? World.VisitChangedReadRows(ExactChangedPlan, ref _cursor, ref visitor)
            : World.VisitChangedReadChunks(DenseChangedPlan, ref _cursor, ref visitor);
        int expected = @case == ChangeConsumerCase.ExactChangedRows
            ? selection.Count
            : selection.ResidentRowsInTouchedChunks;
        if (visited != expected || visitor.Rows != expected || visitor.SelectedRows != selection.Count)
        {
            throw new InvalidOperationException(
                $"{@case} delivered {visited}/{visitor.Rows} rows and {visitor.SelectedRows} selected; " +
                $"expected {expected} rows and {selection.Count} selected.");
        }
        return unchecked(visitor.Checksum + visited + (long)_cursor.Version.Value);
    }

    private struct SelectedIdentityVisitor : IComponentChunkReadVisitor<ChangeDecompositionIdentity>
    {
        private readonly int[] _selectedEpochs;
        private readonly int _epoch;

        internal SelectedIdentityVisitor(int[] selectedEpochs, int epoch)
        {
            _selectedEpochs = selectedEpochs;
            _epoch = epoch;
            Rows = 0;
            SelectedRows = 0;
            Checksum = 0;
        }

        internal int Rows;
        internal int SelectedRows;
        internal long Checksum;

        public void Execute(in ReadOnlyComponentChunk<ChangeDecompositionIdentity> chunk)
        {
            int rows = Rows;
            int selected = SelectedRows;
            long checksum = Checksum;
            for (int row = 0; row < chunk.Count; row++)
            {
                int logical = chunk.Components[row].Value;
                if (_selectedEpochs[logical] == _epoch)
                    selected++;
                checksum = unchecked(checksum + logical + chunk.EntityIndices[row]);
            }
            Rows = rows + chunk.Count;
            SelectedRows = selected;
            Checksum = checksum;
        }
    }
}

internal readonly record struct ChangeSelection(
    ChangeLocality Locality,
    int Count,
    int[] LogicalIndices,
    int[] ChunkGroupedLogicalIndices,
    int TouchedChunks,
    int TotalOccupiedChunks,
    int ResidentRowsInTouchedChunks,
    int ExactChangedRuns);

internal readonly record struct TimedSamples(
    int Count,
    double MeanNanoseconds,
    double P50Nanoseconds,
    double P95Nanoseconds,
    double P99Nanoseconds,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long Checksum);

internal enum ChangeExecutionOrder
{
    Forward,
    Reverse,
}

internal enum ChangeLocality
{
    Contiguous,
    Clustered256,
    Random,
    Dense,
}

internal enum ChangeWriteCase
{
    PreboundWriteNoTracking,
    CheckedLocationWriteNoTracking,
    DenseStampOnlyPerRow,
    ExactStampOnlyPerRow,
    CheckedDenseTracked,
    CheckedExactTracked,
    DenseSharedVersionPerRow,
    ExactSharedVersionPerRow,
    DenseSharedVersionChunkGrouped,
    ExactSharedVersionChunkGrouped,
}

internal enum ChangeConsumerCase
{
    DenseChangedChunks,
    ExactChangedRows,
}

internal sealed class ChangeDecompositionReport
{
    public required string SuiteName { get; init; }
    public required string KernelVersion { get; init; }
    public required string SemanticProfile { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string RuntimeVersion { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required string OperatingSystem { get; init; }
    public required int ProcessorCount { get; init; }
    public required IReadOnlyDictionary<string, string?> RuntimeControls { get; init; }
    public required ChangeDecompositionConfiguration Configuration { get; init; }
    public required bool CorrectnessPassed { get; init; }
    public required bool ZeroAllocationPassed { get; init; }
    public required IReadOnlyList<ChangeDecompositionWorldFacts> WorldFacts { get; init; }
    public required IReadOnlyList<ChangeDecompositionMeasurement> Measurements { get; init; }
}

internal sealed class ChangeDecompositionWorldFacts
{
    public required string Name { get; init; }
    public required int EntityCount { get; init; }
    public required int RowCapacity { get; init; }
    public required int OccupiedChunks { get; init; }
    public required int AcquiredChunks { get; init; }
    public required long DenseChangeMetadataBytes { get; init; }
    public required long ExactChangeMetadataBytes { get; init; }
}

internal sealed class ChangeDecompositionMeasurement
{
    public required ChangeExecutionOrder Order { get; init; }
    public required string Surface { get; init; }
    public required string Scope { get; init; }
    public required string Case { get; init; }
    public required ChangeLocality Locality { get; init; }
    public required int ChangedRows { get; init; }
    public required int TouchedChunks { get; init; }
    public required int TotalOccupiedChunks { get; init; }
    public required int ResidentRowsInTouchedChunks { get; init; }
    public required int ExactChangedRuns { get; init; }
    public required long DeliveredRows { get; init; }
    public required long FalsePositiveRows { get; init; }
    public required long TimedOperations { get; init; }
    public required string OperationUnit { get; init; }
    public required int Samples { get; init; }
    public required double MeanMicroseconds { get; init; }
    public required double P50Microseconds { get; init; }
    public required double P95Microseconds { get; init; }
    public required double P99Microseconds { get; init; }
    public required double MeanNanosecondsPerOperation { get; init; }
    public required double P95NanosecondsPerOperation { get; init; }
    public double MeanNanosecondsPerChangedRow { get; set; }
    public double P95NanosecondsPerChangedRow { get; set; }
    public double MeanNanosecondsPerResidentRowInspected { get; set; }
    public double P95NanosecondsPerResidentRowInspected { get; set; }
    public required long AllocatedBytes { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long Checksum { get; init; }
}

internal struct SplitMix64
{
    private ulong _state;

    internal SplitMix64(ulong seed) => _state = seed;

    internal int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        ulong value = NextUInt64();
        return (int)(((UInt128)value * (uint)exclusiveMaximum) >> 64);
    }

    private ulong NextUInt64()
    {
        unchecked
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
