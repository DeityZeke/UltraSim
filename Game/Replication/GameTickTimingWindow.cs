using System.Diagnostics;
using System.Numerics;

namespace UltraSim.Game.Replication;

public enum GameTelemetryLevel : byte
{
    Off = 0,
    Basic = 1,
    Detailed = 2,
}

/// <summary>
/// Cold snapshot of one bounded authoritative fixed-step timing window. Unprefixed percentiles
/// are exact rolling observations from the most recent bounded sample set. WholeWindow fields are
/// conservative power-of-two histogram bucket upper bounds, while each Maximum field is exact.
/// </summary>
public readonly record struct GameTickTimingSnapshot(
    GameTelemetryLevel Level,
    ulong SampleCount,
    long RecentMicroseconds,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long MaximumMicroseconds,
    ulong IntervalSampleCount,
    long IntervalP50Microseconds,
    long IntervalP95Microseconds,
    long IntervalP99Microseconds,
    long IntervalMaximumMicroseconds,
    ulong LatenessSampleCount,
    long LatenessP50Microseconds,
    long LatenessP95Microseconds,
    long LatenessP99Microseconds,
    long LatenessMaximumMicroseconds,
    ulong DeadlineMisses,
    ulong CatchUpTicks,
    ulong SkippedOpportunities,
    long WholeWindowP50Microseconds,
    long WholeWindowP95Microseconds,
    long WholeWindowP99Microseconds,
    long WholeWindowIntervalP50Microseconds,
    long WholeWindowIntervalP95Microseconds,
    long WholeWindowIntervalP99Microseconds,
    long WholeWindowLatenessP50Microseconds,
    long WholeWindowLatenessP95Microseconds,
    long WholeWindowLatenessP99Microseconds);

/// <summary>
/// Owner-thread bounded timing window for authoritative fixed steps. Recording is allocation-free;
/// percentile sorting is performed only for cold diagnostics capture.
/// </summary>
public sealed class GameTickTimingWindow
{
    public const int MaximumSamples = 256;
    private readonly long[] _microseconds = new long[MaximumSamples];
    private readonly long[] _intervalMicroseconds = new long[MaximumSamples];
    private readonly long[] _latenessMicroseconds = new long[MaximumSamples];
    private readonly ulong[] _durationWholeBuckets = new ulong[64];
    private readonly ulong[] _intervalWholeBuckets = new ulong[64];
    private readonly ulong[] _latenessWholeBuckets = new ulong[64];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private GameTelemetryLevel _level = GameTelemetryLevel.Basic;
    private int _next;
    private int _count;
    private ulong _observations;
    private long _durationMaximum;
    private int _intervalNext;
    private int _intervalCount;
    private ulong _intervalObservations;
    private long _intervalMaximum;
    private int _latenessNext;
    private int _latenessCount;
    private ulong _latenessObservations;
    private long _latenessMaximum;
    private long _lastTickStarted;
    private ulong _deadlineMisses;
    private ulong _catchUpTicks;
    private ulong _skippedOpportunities;

    public GameTelemetryLevel Level => _level;

    public void Configure(GameTelemetryLevel level)
    {
        EnsureOwner();
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        _level = level;
        Array.Clear(_microseconds);
        Array.Clear(_intervalMicroseconds);
        Array.Clear(_latenessMicroseconds);
        Array.Clear(_durationWholeBuckets);
        Array.Clear(_intervalWholeBuckets);
        Array.Clear(_latenessWholeBuckets);
        _next = _count = _intervalNext = _intervalCount = _latenessNext = _latenessCount = 0;
        _observations = _intervalObservations = _latenessObservations = 0;
        _durationMaximum = _intervalMaximum = _latenessMaximum = 0;
        _lastTickStarted = 0;
        _deadlineMisses = _catchUpTicks = _skippedOpportunities = 0;
    }

    public long Start() => _level == GameTelemetryLevel.Off ? 0 : Stopwatch.GetTimestamp();

    public void RecordScheduledStart(
        long scheduledTimestamp,
        long actualTimestamp,
        long targetIntervalTicks,
        bool catchUp)
    {
        EnsureOwner();
        if (_level == GameTelemetryLevel.Off) return;
        if (targetIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(targetIntervalTicks));
        if (_lastTickStarted != 0)
            Record(_intervalMicroseconds, ref _intervalNext, ref _intervalCount,
                ref _intervalObservations, ref _intervalMaximum,
                ToMicroseconds(Math.Max(0, actualTimestamp - _lastTickStarted)),
                _intervalWholeBuckets);
        _lastTickStarted = actualTimestamp;
        long lateness = Math.Max(0, actualTimestamp - scheduledTimestamp);
        Record(_latenessMicroseconds, ref _latenessNext, ref _latenessCount,
            ref _latenessObservations, ref _latenessMaximum,
            ToMicroseconds(lateness), _latenessWholeBuckets);
        if (lateness >= targetIntervalTicks) SaturatingIncrement(ref _deadlineMisses);
        if (catchUp) SaturatingIncrement(ref _catchUpTicks);
    }

    public void RecordSkippedOpportunity()
    {
        EnsureOwner();
        if (_level != GameTelemetryLevel.Off) SaturatingIncrement(ref _skippedOpportunities);
    }

    public void RecordElapsed(long startTimestamp)
    {
        EnsureOwner();
        if (_level == GameTelemetryLevel.Off) return;
        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - startTimestamp);
        Record(_microseconds, ref _next, ref _count, ref _observations, ref _durationMaximum,
            ToMicroseconds(elapsed), _durationWholeBuckets);
    }

    public GameTickTimingSnapshot Capture()
    {
        EnsureOwner();
        TimingValues duration = Values(_microseconds, _next, _count);
        TimingValues interval = Values(_intervalMicroseconds, _intervalNext, _intervalCount);
        TimingValues lateness = Values(_latenessMicroseconds, _latenessNext, _latenessCount);
        return new GameTickTimingSnapshot(
            _level, _observations, duration.Recent, duration.P50, duration.P95, duration.P99,
            _durationMaximum, _intervalObservations, interval.P50, interval.P95, interval.P99,
            _intervalMaximum, _latenessObservations, lateness.P50, lateness.P95, lateness.P99,
            _latenessMaximum, _deadlineMisses, _catchUpTicks, _skippedOpportunities,
            WholePercentile(_durationWholeBuckets, _observations, _durationMaximum, 50),
            WholePercentile(_durationWholeBuckets, _observations, _durationMaximum, 95),
            WholePercentile(_durationWholeBuckets, _observations, _durationMaximum, 99),
            WholePercentile(_intervalWholeBuckets, _intervalObservations, _intervalMaximum, 50),
            WholePercentile(_intervalWholeBuckets, _intervalObservations, _intervalMaximum, 95),
            WholePercentile(_intervalWholeBuckets, _intervalObservations, _intervalMaximum, 99),
            WholePercentile(_latenessWholeBuckets, _latenessObservations, _latenessMaximum, 50),
            WholePercentile(_latenessWholeBuckets, _latenessObservations, _latenessMaximum, 95),
            WholePercentile(_latenessWholeBuckets, _latenessObservations, _latenessMaximum, 99));
    }

    private static TimingValues Values(long[] source, int next, int count)
    {
        if (count == 0) return default;
        Span<long> ordered = stackalloc long[MaximumSamples];
        source.AsSpan(0, count).CopyTo(ordered);
        ordered[..count].Sort();
        int recent = (next - 1 + source.Length) % source.Length;
        return new TimingValues(source[recent],
            ordered[Index(count, 0.50)], ordered[Index(count, 0.95)],
            ordered[Index(count, 0.99)], ordered[count - 1]);
    }

    private static int Index(int count, double percentile) => Math.Clamp(
        (int)Math.Ceiling(count * percentile) - 1, 0, count - 1);

    private static long ToMicroseconds(long ticks) => checked((long)(
        (Int128)ticks * 1_000_000 / Stopwatch.Frequency));

    private static void Record(
        long[] values,
        ref int next,
        ref int count,
        ref ulong observations,
        ref long maximum,
        long value,
        ulong[] wholeBuckets)
    {
        values[next] = value;
        next = (next + 1) % values.Length;
        if (count < values.Length) count++;
        if (observations != ulong.MaxValue) observations++;
        maximum = Math.Max(maximum, value);
        int bucket = value == 0 ? 0 : Math.Min(63,
            BitOperations.Log2(checked((ulong)value)) + 1);
        if (wholeBuckets[bucket] != ulong.MaxValue) wholeBuckets[bucket]++;
    }

    private static long WholePercentile(
        ulong[] buckets,
        ulong observations,
        long maximum,
        int percentage)
    {
        if (observations == 0) return 0;
        ulong target = (ulong)(((UInt128)observations * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 0; index < buckets.Length; index++)
        {
            cumulative = ulong.MaxValue - cumulative < buckets[index]
                ? ulong.MaxValue
                : cumulative + buckets[index];
            if (cumulative < target) continue;
            if (index == 0) return 0;
            ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
            return checked((long)Math.Min(upper, checked((ulong)maximum)));
        }
        return maximum;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The tick timing window belongs to another thread.");
    }

    private readonly record struct TimingValues(
        long Recent, long P50, long P95, long P99, long Maximum);
}
