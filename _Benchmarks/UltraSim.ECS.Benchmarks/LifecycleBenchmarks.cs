using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace UltraSim.ECS.Benchmarks;

internal static class LifecycleBenchmarkProgram
{
    private const int Population = 100_000;
    private const int Samples = 20;
    private const int Warmups = 5;

    internal static int Run(string[] args)
    {
        string output = GetOutput(args) ?? Path.Combine(
            "_Benchmarks", "results", "ecs-3.2.0-lifecycle-candidate.json");
        var results = new List<LifecycleBenchmarkCase>();
        MeasureMutationPair(results, "Create_Default", Population * 2, MutationKind.Create);
        MeasureMutationPair(results, "Destroy", Population * 2, MutationKind.Destroy);
        MeasureMutationPair(results, "Transition", Population * 4, MutationKind.Transition);
        MeasureMutationPair(results, "Deferred_Create_Playback", Population * 2, MutationKind.DeferredCreate);
        MeasureMutationPair(results, "Deferred_Whole_Destroy_Playback", Population * 2, MutationKind.DeferredDestroy);
        MeasureMutationPair(results, "Deferred_Whole_Transition_Playback", Population * 4, MutationKind.DeferredTransition);
        MeasureRawPublication(results, 100);
        MeasureRawPublication(results, 1_000);
        MeasureRawPublication(results, 10_000);
        MeasureRawPublication(results, 100_000);
        MeasureConsumption(results, 1, 1, 1);
        MeasureConsumption(results, 1_000, 1_000, 1);
        MeasureConsumption(results, 10_000, 10_000, 1);
        MeasureConsumption(results, 100_000, 100_000, 1);
        MeasureConsumption(results, 100_000, 64, 1);
        MeasureConsumption(results, 100_000, 100_000, 3);
        MeasureConsumption(results, 100_000, 64, 3);
        LifecycleOverflowResult overflow = MeasureOverflow();
        FilterBenchmarkResult filter = MeasureFilter();

        var report = new LifecycleBenchmarkReport(
            KernelVersion: EcsKernelVersion.Value,
            SemanticProfile: EcsKernelVersion.SemanticProfile,
            RuntimeVersion: Environment.Version.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            LifecycleRecordSizeBytes: Unsafe.SizeOf<LifecycleRecord>(),
            LifecycleCursorSizeBytes: Unsafe.SizeOf<LifecycleCursor>(),
            TimestampUtc: DateTimeOffset.UtcNow,
            Cases: results,
            Overflow: overflow,
            Filter: filter);
        string full = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Lifecycle candidate benchmark ({results.Count} cases)");
        foreach (LifecycleBenchmarkCase result in results)
            Console.WriteLine($"{result.Name}: mean={result.MeanNanosecondsPerOperation:F2} ns/op " +
                $"p50={result.P50NanosecondsPerOperation:F2} ns/op " +
                $"p95={result.P95NanosecondsPerOperation:F2} ns/op " +
                $"p99={result.P99NanosecondsPerOperation:F2} ns/op alloc={result.AllocatedBytes} B" +
                (result.DeltaPercent.HasValue ? $" delta={result.DeltaPercent:F2}%" : string.Empty));
        Console.WriteLine($"Filter: filtered={filter.FilteredNanosecondsPerRow:F3} ns/row control={filter.ControlNanosecondsPerRow:F3} ns/row delta={filter.DeltaPercent:F2}%");
        Console.WriteLine($"Result: {full}");
        return 0;
    }

    private static void MeasureMutationPair(
        List<LifecycleBenchmarkCase> results,
        string name,
        int lifecycleCapacity,
        MutationKind kind)
    {
        using var disabled = new MutationFixture(0);
        using var enabled = new MutationFixture(lifecycleCapacity);
        for (int warm = 0; warm < Warmups; warm++)
        {
            ExecuteMutationIteration(disabled, kind);
            ExecuteMutationIteration(enabled, kind);
        }
        var disabledElapsed = new long[Samples];
        var enabledElapsed = new long[Samples];
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        ulong disabledChecksum = 0;
        ulong enabledChecksum = 0;
        for (int sample = 0; sample < Samples; sample++)
        {
            if ((sample & 1) == 0)
            {
                disabledChecksum = unchecked(disabledChecksum +
                    MeasureMutationIteration(disabled, kind, out disabledElapsed[sample]));
                enabledChecksum = unchecked(enabledChecksum +
                    MeasureMutationIteration(enabled, kind, out enabledElapsed[sample]));
            }
            else
            {
                enabledChecksum = unchecked(enabledChecksum +
                    MeasureMutationIteration(enabled, kind, out enabledElapsed[sample]));
                disabledChecksum = unchecked(disabledChecksum +
                    MeasureMutationIteration(disabled, kind, out disabledElapsed[sample]));
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        if (disabledChecksum != enabledChecksum)
            throw new InvalidOperationException($"{name} paired controls ended with different checksums.");
        LifecycleBenchmarkCase control = CreateResult(
            $"{name}_Disabled", Population, disabledElapsed, allocated, disabledChecksum);
        LifecycleBenchmarkCase candidate = CreateResult(
            $"{name}_Enabled", Population, enabledElapsed, allocated, enabledChecksum,
            control.Name, (enabledElapsed.Average(static value => (double)value) /
                disabledElapsed.Average(static value => (double)value) - 1d) * 100d);
        results.Add(control);
        results.Add(candidate);
    }

    private static void ExecuteMutationIteration(MutationFixture fixture, MutationKind kind)
    {
        fixture.Prepare(kind);
        _ = fixture.Execute(kind);
        fixture.Cleanup(kind);
    }

    private static ulong MeasureMutationIteration(
        MutationFixture fixture,
        MutationKind kind,
        out long elapsed)
    {
        fixture.Prepare(kind);
        long start = Stopwatch.GetTimestamp();
        ulong checksum = fixture.Execute(kind);
        elapsed = Stopwatch.GetTimestamp() - start;
        fixture.Cleanup(kind);
        return checksum;
    }

    private static void MeasureRawPublication(
        List<LifecycleBenchmarkCase> results,
        int eventCount)
    {
        var history = new LifecycleHistory(eventCount + 1);
        Entity entity = new(WorldId.Create(), 0, 1);
        ComponentId component = new(1);
        // Small loops otherwise cross tiered-compilation thresholds inside the
        // measured sample set and report JIT pauses as publication cost.
        for (int warm = 0; warm < 64; warm++)
            for (int index = 0; index < eventCount; index++)
                history.Publish(LifecycleKind.ComponentAdded, entity, component);

        var elapsed = new long[Samples];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < Samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int index = 0; index < eventCount; index++)
                history.Publish(LifecycleKind.ComponentAdded, entity, component);
            elapsed[sample] = Stopwatch.GetTimestamp() - start;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        results.Add(CreateResult(
            $"Publish_{eventCount}", eventCount, elapsed, allocated,
            history.GetSnapshot().NextPublishedSequence));
    }

    private static void MeasureConsumption(
        List<LifecycleBenchmarkCase> results,
        int eventCount,
        int spanSize,
        int consumerCount)
    {
        using var fixture = new MutationFixture(eventCount + 8);
        fixture.Publish(eventCount);
        var cursors = new LifecycleCursor[consumerCount];
        for (int index = 0; index < cursors.Length; index++)
            cursors[index] = fixture.World.CaptureLifecycleCursorFromBeginning();
        var records = new LifecycleRecord[spanSize];
        for (int warm = 0; warm < Warmups; warm++)
        {
            for (int consumer = 0; consumer < cursors.Length; consumer++)
            {
                LifecycleCursor warmCursor = cursors[consumer];
                while (fixture.World.ReadLifecycle(ref warmCursor, records).Status == LifecycleReadStatus.Success &&
                       warmCursor.NextSequence != unchecked((ulong)eventCount)) { }
            }
        }
        var elapsed = new long[Samples];
        ulong checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < Samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int consumer = 0; consumer < cursors.Length; consumer++)
            {
                LifecycleCursor measured = cursors[consumer];
                int delivered = 0;
                while (delivered != eventCount)
                {
                    LifecycleReadResult read = fixture.World.ReadLifecycle(ref measured, records);
                    if (read.Status != LifecycleReadStatus.Success)
                        throw new InvalidOperationException("Lifecycle consume benchmark lost retained history.");
                    delivered += read.Count;
                }
                checksum ^= measured.NextSequence + unchecked((ulong)consumer);
            }
            elapsed[sample] = Stopwatch.GetTimestamp() - start;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        results.Add(CreateResult(
            $"Consume_{eventCount}_Span{spanSize}_Consumers{consumerCount}",
            checked(eventCount * consumerCount),
            elapsed,
            allocated,
            checksum));
    }

    private static LifecycleOverflowResult MeasureOverflow()
    {
        using var fixture = new MutationFixture(64);
        LifecycleCursor lagging = fixture.World.CaptureLifecycleCursorFromBeginning();
        fixture.Publish(1_000);
        Span<LifecycleRecord> records = stackalloc LifecycleRecord[16];
        LifecycleReadResult read = fixture.World.ReadLifecycle(ref lagging, records);
        return new LifecycleOverflowResult(
            fixture.World.GetLifecycleHistorySnapshot(),
            read.Status.ToString(),
            lagging.NextSequence,
            read.OldestRetainedSequence,
            read.NextPublishedSequence);
    }

    private static FilterBenchmarkResult MeasureFilter()
    {
        using var filtered = new FilterFixture(filtered: true);
        using var control = new FilterFixture(filtered: false);
        for (int warm = 0; warm < Warmups; warm++)
        {
            filtered.Execute();
            control.Execute();
        }
        double filteredNs = MeasureRows(filtered, Samples);
        double controlNs = MeasureRows(control, Samples);
        return new FilterBenchmarkResult(
            Population / 2,
            filtered.PlanSealMicroseconds,
            control.PlanSealMicroseconds,
            filteredNs,
            controlNs,
            (filteredNs / controlNs - 1d) * 100d,
            0,
            filtered.ExpectedMatchingArchetypes,
            filtered.TotalArchetypes);
    }

    private static double MeasureRows(FilterFixture fixture, int samples)
    {
        long total = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < samples; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            int rows = fixture.Execute();
            total += Stopwatch.GetTimestamp() - start;
            if (rows != Population / 2)
                throw new InvalidOperationException("Filter benchmark row count changed.");
        }
        if (GC.GetAllocatedBytesForCurrentThread() != before)
            throw new InvalidOperationException("Filtered generated traversal allocated after warmup.");
        return total * 1_000_000_000d / Stopwatch.Frequency / (samples * (Population / 2d));
    }

    private static LifecycleBenchmarkCase CreateResult(
        string name,
        int operations,
        long[] elapsed,
        long allocated,
        ulong checksum,
        string? pairedControl = null,
        double? deltaPercent = null)
    {
        Array.Sort(elapsed);
        double meanTicks = elapsed.Average(static value => (double)value);
        long p50 = elapsed[(int)Math.Ceiling(elapsed.Length * 0.50d) - 1];
        long p95 = elapsed[(int)Math.Ceiling(elapsed.Length * 0.95d) - 1];
        long p99 = elapsed[(int)Math.Ceiling(elapsed.Length * 0.99d) - 1];
        return new LifecycleBenchmarkCase(
            name,
            operations,
            meanTicks * 1_000_000_000d / Stopwatch.Frequency / operations,
            p50 * 1_000_000_000d / Stopwatch.Frequency / operations,
            p95 * 1_000_000_000d / Stopwatch.Frequency / operations,
            p99 * 1_000_000_000d / Stopwatch.Frequency / operations,
            p95 * 1_000_000d / Stopwatch.Frequency,
            allocated,
            checksum,
            pairedControl,
            deltaPercent);
    }

    private static string? GetOutput(string[] args)
    {
        for (int index = 0; index + 1 < args.Length; index++)
            if (args[index] == "--output")
                return args[index + 1];
        return null;
    }

    private enum MutationKind
    {
        Create,
        Destroy,
        Transition,
        DeferredCreate,
        DeferredDestroy,
        DeferredTransition,
    }

    private sealed class MutationFixture : IDisposable
    {
        private readonly Entity[] _entities = new Entity[Population];
        private readonly ArchetypeHandle[] _createArchetypes = new ArchetypeHandle[Population];
        private readonly DeferredEntity[] _createTokens = new DeferredEntity[Population];
        private bool _inSource = true;

        internal MutationFixture(int lifecycleCapacity)
        {
            World = new EcsWorld(
                BenchmarkSchema.CreateSchema(),
                new EcsWorldOptions(
                    new EntityCapacity(Population),
                    2,
                    1,
                    maximumPreparedTransitions: 2,
                    maximumStructuralCommands: Population,
                    lifecycleRecordCapacity: lifecycleCapacity));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            Source = World.RegisterArchetype([position], ChunkLayoutOptions.KiB16, 256);
            Destination = World.RegisterArchetype(Sorted(position, velocity), ChunkLayoutOptions.KiB16, 256);
            Forward = World.PrepareTransition(Source, Destination);
            Backward = World.PrepareTransition(Destination, Source);
            Array.Fill(_createArchetypes, Source);
            World.Seal();
            World.Initialize();
            World.BeginRun();
        }

        internal EcsWorld World { get; }
        private ArchetypeHandle Source { get; }
        private ArchetypeHandle Destination { get; }
        private ArchetypeTransition Forward { get; }
        private ArchetypeTransition Backward { get; }

        internal ulong Execute(MutationKind kind)
        {
            switch (kind)
            {
                case MutationKind.Create:
                    CreateAll();
                    return _entities[^1].Generation;
                case MutationKind.Destroy:
                    DestroyAll();
                    return unchecked((ulong)World.AliveCount);
                case MutationKind.Transition:
                    if (_entities[0] == Entity.Null)
                        CreateAll();
                    ArchetypeTransition transition = _inSource ? Forward : Backward;
                    for (int index = 0; index < _entities.Length; index++)
                        if (!World.TryTransition(_entities[index], transition))
                            throw new InvalidOperationException("Transition benchmark rejected a valid entity.");
                    _inSource = !_inSource;
                    return unchecked((ulong)World.AliveCount);
                case MutationKind.DeferredCreate:
                case MutationKind.DeferredDestroy:
                case MutationKind.DeferredTransition:
                    World.PlaybackStructuralCommands();
                    if (kind == MutationKind.DeferredTransition)
                        _inSource = !_inSource;
                    return unchecked((ulong)World.AliveCount);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        internal void Prepare(MutationKind kind)
        {
            if (kind == MutationKind.Destroy)
                CreateAll();
            else if (kind == MutationKind.Transition && _entities[0] == Entity.Null)
                CreateAll();
            else if (kind == MutationKind.DeferredCreate)
                World.StructuralCommands.EnqueueCreateDefault(_createArchetypes, _createTokens);
            else if (kind == MutationKind.DeferredDestroy)
            {
                CreateAll();
                World.StructuralCommands.EnqueueDestroy(_entities);
            }
            else if (kind == MutationKind.DeferredTransition)
            {
                if (_entities[0] == Entity.Null)
                    CreateAll();
                World.StructuralCommands.EnqueueTransition(
                    _entities,
                    _inSource ? Forward : Backward);
            }
        }

        internal void Cleanup(MutationKind kind)
        {
            if (kind == MutationKind.Create)
                DestroyAll();
            else if (kind == MutationKind.DeferredCreate)
            {
                for (int index = 0; index < _createTokens.Length; index++)
                {
                    if (!World.StructuralCommands.TryResolve(_createTokens[index], out _entities[index]))
                        throw new InvalidOperationException("Deferred create benchmark lost a result.");
                }
                DestroyAll();
            }
            else if (kind == MutationKind.DeferredDestroy)
                Array.Clear(_entities);
        }

        internal void Publish(int records)
        {
            Entity live = Entity.Null;
            for (int index = 0; index < records; index++)
            {
                if ((index & 1) == 0)
                {
                    if (!World.TryCreate(Source, out live))
                        throw new InvalidOperationException("Lifecycle publish setup exhausted entity capacity.");
                }
                else if (!World.TryDestroy(live))
                {
                    throw new InvalidOperationException("Lifecycle publish setup destroy failed.");
                }
            }
        }

        private void CreateAll()
        {
            for (int index = 0; index < _entities.Length; index++)
                if (!World.TryCreate(Source, out _entities[index]))
                    throw new InvalidOperationException("Create benchmark exhausted capacity.");
            _inSource = true;
        }

        private void DestroyAll()
        {
            for (int index = 0; index < _entities.Length; index++)
                if (!World.TryDestroy(_entities[index]))
                    throw new InvalidOperationException("Destroy benchmark rejected a live entity.");
            Array.Clear(_entities);
        }

        public void Dispose() => World.Dispose();
    }

    private sealed class FilterFixture : IDisposable
    {
        private readonly EcsWorld _world;
        private readonly ReadWriteJobPlan<BenchmarkPosition, BenchmarkVelocity> _plan;

        internal FilterFixture(bool filtered)
        {
            _world = new EcsWorld(
                BenchmarkSchema.CreateSchema(),
                new EcsWorldOptions(new EntityCapacity(Population), 2, 2));
            ComponentId position = BenchmarkSchema.GetComponentId<BenchmarkPosition>();
            ComponentId velocity = BenchmarkSchema.GetComponentId<BenchmarkVelocity>();
            ComponentId tag = BenchmarkSchema.GetComponentId<BenchmarkDisabled>();
            ComponentId[] matchSignature = Sorted(position, velocity, tag);
            ArchetypeHandle match = _world.RegisterArchetype(
                matchSignature,
                ChunkLayoutOptions.KiB16,
                128);
            ArchetypeHandle? other = null;
            if (filtered)
            {
                other = _world.RegisterArchetype(
                    Sorted(position, velocity),
                    ChunkLayoutOptions.KiB16,
                    128);
            }
            long start = Stopwatch.GetTimestamp();
            _plan = filtered
                ? _world.PrepareReadWriteJob<BenchmarkPosition, BenchmarkVelocity>(
                    _world.PrepareQueryFilter([tag], []))
                : _world.PrepareReadWriteJob<BenchmarkPosition, BenchmarkVelocity>();
            _world.Seal();
            PlanSealMicroseconds = (Stopwatch.GetTimestamp() - start) * 1_000_000d / Stopwatch.Frequency;
            _world.Initialize();
            _world.BeginRun();
            for (int index = 0; index < Population / 2; index++)
            {
                if (!_world.TryCreate(match, out _))
                    throw new InvalidOperationException("Filter matching seed failed.");
                if (other.HasValue && !_world.TryCreate(other.Value, out _))
                    throw new InvalidOperationException("Filter nonmatching seed failed.");
            }
            ExpectedMatchingArchetypes = 1;
            TotalArchetypes = filtered ? 2 : 1;
        }

        internal double PlanSealMicroseconds { get; }
        internal int ExpectedMatchingArchetypes { get; }
        internal int TotalArchetypes { get; }

        internal int Execute()
        {
            var job = new FilterJob();
            return _world.ExecuteJob(_plan, ref job);
        }

        public void Dispose() => _world.Dispose();
    }

    private struct FilterJob : IReadWriteChunkJob<BenchmarkPosition, BenchmarkVelocity>
    {
        public void Execute(in ReadWriteComponentChunk<BenchmarkPosition, BenchmarkVelocity> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
                chunk.WriteComponents[row].X += chunk.ReadComponents[row].X + 1f;
        }
    }

    private static ComponentId[] Sorted(params ComponentId[] values)
    {
        Array.Sort(values);
        return values;
    }
}

internal sealed record LifecycleBenchmarkReport(
    string KernelVersion,
    string SemanticProfile,
    string RuntimeVersion,
    int ProcessorCount,
    int LifecycleRecordSizeBytes,
    int LifecycleCursorSizeBytes,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<LifecycleBenchmarkCase> Cases,
    LifecycleOverflowResult Overflow,
    FilterBenchmarkResult Filter);

internal sealed record LifecycleBenchmarkCase(
    string Name,
    int Operations,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double P99NanosecondsPerOperation,
    double P95Microseconds,
    long AllocatedBytes,
    ulong Checksum,
    string? PairedControl,
    double? DeltaPercent);

internal sealed record LifecycleOverflowResult(
    LifecycleHistorySnapshot Snapshot,
    string Status,
    ulong CursorSequenceAfterRead,
    ulong OldestRetainedSequence,
    ulong NextPublishedSequence);

internal sealed record FilterBenchmarkResult(
    int Rows,
    double FilteredPlanSealMicroseconds,
    double ControlPlanSealMicroseconds,
    double FilteredNanosecondsPerRow,
    double ControlNanosecondsPerRow,
    double DeltaPercent,
    long AllocatedBytes,
    int MatchingArchetypes,
    int TotalArchetypes);
