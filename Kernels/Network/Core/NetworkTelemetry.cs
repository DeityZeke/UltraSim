using System.Diagnostics;
using System.Numerics;

namespace UltraSim.Network;

/// <summary>
/// Finite diagnostic cost policy. Off performs no timestamp reads; Basic measures
/// service cadence/duration; Detailed additionally attributes protocol stages.
/// Telemetry never changes wire or scheduling semantics.
/// </summary>
public enum NetworkTelemetryLevel : byte
{
    Off = 0,
    Basic = 1,
    Detailed = 2,
}

/// <summary>
/// Cold bounded timing distribution. Percentiles are upper bounds from 64
/// power-of-two Stopwatch-tick buckets; totals and maxima are exact.
/// </summary>
public readonly record struct NetworkTimingDistribution(
    ulong Count,
    ulong TotalStopwatchTicks,
    ulong P50StopwatchTicks,
    ulong P95StopwatchTicks,
    ulong P99StopwatchTicks,
    ulong MaximumStopwatchTicks)
{
    public double AchievedHertz(double measuredSeconds) =>
        measuredSeconds > 0 ? Count / measuredSeconds : 0;

    public static double ToMicroseconds(ulong ticks) =>
        ticks * 1_000_000d / Stopwatch.Frequency;

}

/// <summary>Cold immutable service metrics for one Hosted transport lifetime.</summary>
public readonly record struct NetworkServiceTimingSnapshot(
    NetworkTelemetryLevel Level,
    NetworkTimingDistribution PumpIntervals,
    NetworkTimingDistribution PumpDurations,
    NetworkProtocolStageTiming ProtocolStages);

/// <summary>
/// Owner-local fixed histogram. Recording is allocation-free and snapshotting is
/// a cold scan of 64 primitive counters.
/// </summary>
internal sealed class NetworkTimingHistogram
{
    private readonly ulong[] _buckets = new ulong[64];
    private ulong _count;
    private ulong _total;
    private ulong _maximum;

    internal void Record(long elapsedTicks)
    {
        ulong elapsed = checked((ulong)Math.Max(0, elapsedTicks));
        int bucket = elapsed == 0 ? 0 : BitOperations.Log2(elapsed) + 1;
        if (bucket >= _buckets.Length) bucket = _buckets.Length - 1;
        if (_buckets[bucket] != ulong.MaxValue) _buckets[bucket]++;
        if (_count != ulong.MaxValue) _count++;
        _total = ulong.MaxValue - _total < elapsed ? ulong.MaxValue : _total + elapsed;
        _maximum = Math.Max(_maximum, elapsed);
    }

    internal NetworkTimingDistribution Snapshot() => new(
        _count,
        _total,
        Percentile(50),
        Percentile(95),
        Percentile(99),
        _maximum);

    internal void Clear()
    {
        Array.Clear(_buckets);
        _count = 0;
        _total = 0;
        _maximum = 0;
    }

    private ulong Percentile(int percentage)
    {
        if (_count == 0) return 0;
        ulong target = (ulong)(((UInt128)_count * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 0; index < _buckets.Length; index++)
        {
            cumulative = ulong.MaxValue - cumulative < _buckets[index]
                ? ulong.MaxValue
                : cumulative + _buckets[index];
            if (cumulative < target) continue;
            if (index == 0) return 0;
            // Bucket N contains [2^(N-1), 2^N-1]. Clamp its approximate
            // upper bound to the exact observed maximum so a reported
            // percentile can never exceed the distribution maximum.
            ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
            return Math.Min(upper, _maximum);
        }
        return _maximum;
    }
}
