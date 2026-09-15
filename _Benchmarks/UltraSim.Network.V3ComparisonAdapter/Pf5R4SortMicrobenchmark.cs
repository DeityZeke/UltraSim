using System.Diagnostics;
using System.Text.Json;
using UltraSim.Game.World;

internal static class Pf5R4SortMicrobenchmark
{
    internal static void Run(string[] args)
    {
        int iterations = ReadInt(args, "--iterations", 20_000);
        int samples = ReadInt(args, "--samples", 31);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r4-index-sort-micro.json";
        if (iterations < 1_000 || samples < 5) throw new ArgumentOutOfRangeException(nameof(args));
        var rows = new List<Pf5R4SortMicroRow>();
        foreach (int count in new[] { 1, 4, 8, 16, 30 })
        {
            var fixture = new Fixture(count);
            for (int warm = 0; warm < 4_096; warm++)
            {
                fixture.OldRecordSort();
                fixture.NewIndexSortThenConstruct();
            }
            P03Metric oldMetric = Measure(fixture.OldRecordSort, iterations, samples);
            P03Metric newMetric = Measure(fixture.NewIndexSortThenConstruct, iterations, samples);
            long oldAllocation = Allocation(fixture.OldRecordSort, iterations);
            long newAllocation = Allocation(fixture.NewIndexSortThenConstruct, iterations);
            if (!fixture.Equivalent || oldAllocation != 0 || newAllocation != 0)
                throw new InvalidOperationException("PF5-R4 sort microbenchmark diverged.");
            rows.Add(new(count, oldMetric, newMetric, oldAllocation, newAllocation,
                (oldMetric.Mean - newMetric.Mean) / oldMetric.Mean * 100d));
        }
        var report = new Pf5R4SortMicroReport(
            "ultrasim-pf5-r4-index-sort-v1", DateTimeOffset.UtcNow, Stopwatch.Frequency,
            iterations, samples, rows.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static P03Metric Measure(Action action, int iterations, int samples)
    {
        var values = new long[samples];
        for (int sample = 0; sample < samples; sample++)
        {
            long started = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++) action();
            values[sample] = Stopwatch.GetTimestamp() - started;
        }
        Array.Sort(values);
        double scale = 1_000_000_000d / Stopwatch.Frequency / iterations;
        double mean = values.Average(value => (double)value) * scale;
        return new P03Metric(samples, mean,
            values[(int)Math.Floor((samples - 1) * .50)] * scale,
            values[(int)Math.Floor((samples - 1) * .95)] * scale,
            values[(int)Math.Floor((samples - 1) * .99)] * scale,
            values[^1] * scale);
    }

    private static long Allocation(Action action, int iterations)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? value = ReadText(args, name);
        return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadText(string[] args, string name)
    {
        string prefix = name + "=";
        string? value = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal));
        return value?[prefix.Length..];
    }

    private sealed class Fixture
    {
        private readonly WorldPublicDeltaRecord[] _source;
        private readonly WorldPublicDeltaRecord[] _old;
        private readonly WorldPublicDeltaRecord[] _new;
        private readonly int[] _indices;

        internal Fixture(int count)
        {
            _source = new WorldPublicDeltaRecord[count];
            _old = new WorldPublicDeltaRecord[count];
            _new = new WorldPublicDeltaRecord[count];
            _indices = new int[count];
            for (int index = 0; index < count; index++)
            {
                uint identity = checked((uint)(count - index));
                _source[index] = new WorldPublicDeltaRecord(
                    WorldPublicOperation.Present, WorldPublicFieldMask.Complete,
                    new WorldPublicEntityId(identity, 1), identity, default);
            }
        }

        internal bool Equivalent => _old.AsSpan().SequenceEqual(_new);

        internal void OldRecordSort()
        {
            for (int index = 0; index < _indices.Length; index++)
            {
                _indices[index] = index;
                _old[index] = _source[_indices[index]];
            }
            SortRecords(_old);
        }

        internal void NewIndexSortThenConstruct()
        {
            for (int index = 0; index < _indices.Length; index++) _indices[index] = index;
            SortIndices(_indices);
            for (int index = 0; index < _indices.Length; index++)
                _new[index] = _source[_indices[index]];
        }

        private void SortIndices(Span<int> values)
        {
            if (values.Length <= 8)
            {
                for (int index = 1; index < values.Length; index++)
                {
                    int value = values[index];
                    int position = index;
                    while (position > 0 && Compare(values[position - 1], value) > 0)
                    {
                        values[position] = values[position - 1];
                        position--;
                    }
                    values[position] = value;
                }
                return;
            }
            for (int root = values.Length / 2 - 1; root >= 0; root--)
                SiftIndices(values, root, values.Length);
            for (int end = values.Length - 1; end > 0; end--)
            {
                (values[0], values[end]) = (values[end], values[0]);
                SiftIndices(values, 0, end);
            }
        }

        private void SiftIndices(Span<int> values, int root, int count)
        {
            while (true)
            {
                int child = root * 2 + 1;
                if (child >= count) return;
                if (child + 1 < count && Compare(values[child], values[child + 1]) < 0) child++;
                if (Compare(values[root], values[child]) >= 0) return;
                (values[root], values[child]) = (values[child], values[root]);
                root = child;
            }
        }

        private int Compare(int left, int right) => _source[left].Id.CompareTo(_source[right].Id);

        private static void SortRecords(Span<WorldPublicDeltaRecord> values)
        {
            for (int root = values.Length / 2 - 1; root >= 0; root--)
                SiftRecords(values, root, values.Length);
            for (int end = values.Length - 1; end > 0; end--)
            {
                (values[0], values[end]) = (values[end], values[0]);
                SiftRecords(values, 0, end);
            }
        }

        private static void SiftRecords(Span<WorldPublicDeltaRecord> values, int root, int count)
        {
            while (true)
            {
                int child = root * 2 + 1;
                if (child >= count) return;
                if (child + 1 < count && values[child].Id.CompareTo(values[child + 1].Id) < 0)
                    child++;
                if (values[root].Id.CompareTo(values[child].Id) >= 0) return;
                (values[root], values[child]) = (values[child], values[root]);
                root = child;
            }
        }
    }
}

internal sealed record Pf5R4SortMicroReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    long StopwatchFrequency,
    int Iterations,
    int Samples,
    Pf5R4SortMicroRow[] Rows);

internal sealed record Pf5R4SortMicroRow(
    int Records,
    P03Metric OldRecordSort,
    P03Metric NewIndexSortThenConstruct,
    long OldAllocationBytes,
    long NewAllocationBytes,
    double MeanReductionPercent);
