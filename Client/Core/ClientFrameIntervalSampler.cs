using System.Diagnostics;

namespace UltraSim.Client.Core;

/// <summary>One pixel column's exact minimum/maximum eligible frame intervals.</summary>
public readonly record struct ClientFrameGraphColumn(uint MinimumMicroseconds, uint MaximumMicroseconds, int Samples)
{
    public bool HasSamples => Samples != 0;
}

/// <summary>
/// Cold summary of a rolling eligible-frame window. FPS is frames divided by summed frame time;
/// the lows are inverse mean duration of the slowest 1%/0.1%, not mean instantaneous FPS.
/// </summary>
public readonly record struct ClientFrameIntervalSummary(
    int Samples, double EligibleMilliseconds, double? CurrentMilliseconds, double? CurrentFps,
    double? AverageMilliseconds, double? AverageFps, double? P95Milliseconds,
    double? P99Milliseconds, double? P999Milliseconds, double? OnePercentLowFps,
    double? PointOnePercentLowFps, double? MaximumMilliseconds,
    int Over33Milliseconds, int Over50Milliseconds, int Over100Milliseconds,
    ulong TotalSamples, ulong SaturatedSamples)
{
    public bool OnePercentLowReady => Samples >= 100;
    public bool PointOnePercentLowReady => Samples >= 1000;
}

/// <summary>
/// Owner-local, finite wall-frame interval ring. The hot operation performs one clock-delta
/// conversion and one array write, with no sorting, formatting, logging or managed allocation.
/// An ineligible frame resets the timestamp; gameplay hitches remain eligible.
/// </summary>
public sealed class ClientFrameIntervalSampler
{
    public const int Capacity = 16_384;
    public const uint SummaryWindowSeconds = 60;
    public const uint DefaultGraphWindowSeconds = 30;
    private readonly uint[] _microseconds = new uint[Capacity];
    private readonly uint[] _sortScratch = new uint[Capacity];
    private readonly long _frequency;
    private long _previousTimestamp;
    private bool _hasPrevious;
    private int _next;
    private int _count;
    private ulong _totalSamples;
    private ulong _totalEligibleMicroseconds;
    private ulong _saturatedSamples;

    public ClientFrameIntervalSampler(long frequency = 0)
    {
        _frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
        if (_frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
    }

    public int Count => _count;
    public ulong TotalSamples => _totalSamples;
    public ulong TotalEligibleMicroseconds => _totalEligibleMicroseconds;
    public ulong SaturatedSamples => _saturatedSamples;

    public void Reset()
    {
        _hasPrevious = false;
        _next = 0;
        _count = 0;
        _totalSamples = 0;
        _totalEligibleMicroseconds = 0;
        _saturatedSamples = 0;
    }

    /// <returns>True only when an eligible interval was appended.</returns>
    public bool Observe(long monotonicTimestamp, bool eligible)
    {
        if (!eligible)
        {
            _hasPrevious = false;
            return false;
        }
        if (!_hasPrevious || monotonicTimestamp <= _previousTimestamp)
        {
            _previousTimestamp = monotonicTimestamp;
            _hasPrevious = true;
            return false;
        }
        long elapsedTicks = monotonicTimestamp - _previousTimestamp;
        _previousTimestamp = monotonicTimestamp;
        double duration = elapsedTicks * 1_000_000d / _frequency;
        uint microseconds;
        if (duration >= uint.MaxValue)
        {
            microseconds = uint.MaxValue;
            _saturatedSamples++;
        }
        else microseconds = Math.Max(1u, (uint)Math.Round(duration));
        _microseconds[_next] = microseconds;
        _next = (_next + 1) & (Capacity - 1);
        if (_count < Capacity) _count++;
        if (_totalSamples < ulong.MaxValue) _totalSamples++;
        _totalEligibleMicroseconds = ulong.MaxValue - _totalEligibleMicroseconds < microseconds
            ? ulong.MaxValue : _totalEligibleMicroseconds + microseconds;
        return true;
    }

    /// <summary>Cold exact rolling distribution; no allocation, including scratch storage.</summary>
    public ClientFrameIntervalSummary Summarize(uint windowSeconds = SummaryWindowSeconds)
    {
        if (windowSeconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        ulong limit = (ulong)windowSeconds * 1_000_000;
        ulong sum = 0;
        int count = 0, over33 = 0, over50 = 0, over100 = 0;
        uint maximum = 0;
        for (int age = 0; age < _count && sum < limit; age++)
        {
            uint value = _microseconds[(_next - 1 - age) & (Capacity - 1)];
            _sortScratch[count++] = value;
            sum += value;
            if (value > maximum) maximum = value;
            if (value > 33_000) over33++;
            if (value > 50_000) over50++;
            if (value > 100_000) over100++;
        }
        if (count == 0)
            return new(0, 0, null, null, null, null, null, null, null, null, null,
                null, 0, 0, 0, _totalSamples, _saturatedSamples);
        uint current = _microseconds[(_next - 1) & (Capacity - 1)];
        Array.Sort(_sortScratch, 0, count);
        return new(count, sum / 1000d, current / 1000d, 1_000_000d / current,
            sum / (1000d * count), count * 1_000_000d / sum,
            Percentile(95, 100), Percentile(99, 100), count >= 1000 ? Percentile(999, 1000) : null,
            count >= 100 ? SlowestFps((count + 99) / 100) : null,
            count >= 1000 ? SlowestFps((count + 999) / 1000) : null,
            maximum / 1000d, over33, over50, over100, _totalSamples, _saturatedSamples);

        double Percentile(int numerator, int denominator) =>
            _sortScratch[(count * numerator + denominator - 1) / denominator - 1] / 1000d;
        double SlowestFps(int slowest)
        {
            ulong tail = 0;
            for (int index = count - slowest; index < count; index++) tail += _sortScratch[index];
            return slowest * 1_000_000d / tail;
        }
    }

    /// <summary>Cold min/max decimation: a single hitch survives compression into one pixel.</summary>
    public void FillGraphColumns(Span<ClientFrameGraphColumn> columns,
        uint windowSeconds = DefaultGraphWindowSeconds)
    {
        if (columns.Length is < 1 or > 2048 || windowSeconds is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(columns));
        columns.Clear();
        ulong limit = (ulong)windowSeconds * 1_000_000;
        ulong ageMicroseconds = 0;
        for (int age = 0; age < _count && ageMicroseconds < limit; age++)
        {
            uint value = _microseconds[(_next - 1 - age) & (Capacity - 1)];
            int column = columns.Length - 1 - (int)(ageMicroseconds * (ulong)columns.Length / limit);
            ref ClientFrameGraphColumn current = ref columns[column];
            current = current.Samples == 0
                ? new(value, value, 1)
                : new(Math.Min(current.MinimumMicroseconds, value),
                    Math.Max(current.MaximumMicroseconds, value), current.Samples + 1);
            ageMicroseconds += value;
        }
    }

    /// <summary>Cold chronological export of the newest complete-frame window.</summary>
    public int CopyWindowSamples(Span<uint> destination, uint windowSeconds, out ulong coveredMicroseconds)
    {
        if (destination.Length < Capacity || windowSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(destination));
        ulong limit = (ulong)windowSeconds * 1_000_000;
        coveredMicroseconds = 0;
        int count = 0;
        for (int age = 0; age < _count && coveredMicroseconds < limit; age++)
        {
            uint value = _microseconds[(_next - 1 - age) & (Capacity - 1)];
            destination[count++] = value;
            coveredMicroseconds += value;
        }
        destination[..count].Reverse();
        return count;
    }
}
