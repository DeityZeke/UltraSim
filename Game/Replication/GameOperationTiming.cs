using System.Diagnostics;
using System.Numerics;

namespace UltraSim.Game.Replication;

/// <summary>Cold bounded duration distribution owned by a Game operation.</summary>
public readonly record struct GameTimingDistribution(
    ulong Count,
    ulong TotalStopwatchTicks,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long MaximumMicroseconds,
    int RollingSamples,
    long WholeWindowP50Microseconds,
    long WholeWindowP95Microseconds,
    long WholeWindowP99Microseconds);

/// <summary>
/// Owner-thread fixed timing window. Detailed recording is allocation-free; Off
/// and Basic perform no timestamp read through <see cref="StartDetailed"/>.
/// </summary>
public sealed class GameOperationTimingWindow
{
    public const int MaximumSamples = 256;
    private readonly long[] _samples = new long[MaximumSamples];
    private readonly ulong[] _wholeBuckets = new ulong[64];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _next;
    private int _count;
    private ulong _observations;
    private ulong _totalTicks;
    private long _maximumMicroseconds;
    private GameTelemetryLevel _level;

    public GameOperationTimingWindow(GameTelemetryLevel level = GameTelemetryLevel.Off) =>
        Configure(level);

    public GameTelemetryLevel Level => _level;

    public void Configure(GameTelemetryLevel level)
    {
        EnsureOwner();
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        _level = level;
        Array.Clear(_samples);
        Array.Clear(_wholeBuckets);
        _next = _count = 0;
        _observations = 0;
        _totalTicks = 0;
        _maximumMicroseconds = 0;
    }

    public long StartDetailed() =>
        _level == GameTelemetryLevel.Detailed ? Stopwatch.GetTimestamp() : 0;

    public void RecordDetailed(long started)
    {
        EnsureOwner();
        if (_level != GameTelemetryLevel.Detailed) return;
        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - started);
        long microseconds = checked((long)((Int128)elapsed * 1_000_000 / Stopwatch.Frequency));
        _samples[_next] = microseconds;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        if (_observations != ulong.MaxValue) _observations++;
        ulong ticks = checked((ulong)elapsed);
        _totalTicks = ulong.MaxValue - _totalTicks < ticks ? ulong.MaxValue : _totalTicks + ticks;
        _maximumMicroseconds = Math.Max(_maximumMicroseconds, microseconds);
        int bucket = microseconds == 0 ? 0 : Math.Min(63,
            BitOperations.Log2(checked((ulong)microseconds)) + 1);
        if (_wholeBuckets[bucket] != ulong.MaxValue) _wholeBuckets[bucket]++;
    }

    public GameTimingDistribution Capture()
    {
        EnsureOwner();
        if (_count == 0) return default;
        Span<long> ordered = stackalloc long[MaximumSamples];
        _samples.AsSpan(0, _count).CopyTo(ordered);
        ordered[.._count].Sort();
        return new GameTimingDistribution(
            _observations, _totalTicks,
            ordered[Index(0.50)], ordered[Index(0.95)], ordered[Index(0.99)],
            _maximumMicroseconds, _count,
            WholePercentile(50), WholePercentile(95), WholePercentile(99));
    }

    private int Index(double percentile) => Math.Clamp(
        (int)Math.Ceiling(_count * percentile) - 1, 0, _count - 1);

    private long WholePercentile(int percentage)
    {
        if (_observations == 0) return 0;
        ulong target = (ulong)(((UInt128)_observations * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 0; index < _wholeBuckets.Length; index++)
        {
            cumulative = ulong.MaxValue - cumulative < _wholeBuckets[index]
                ? ulong.MaxValue
                : cumulative + _wholeBuckets[index];
            if (cumulative < target) continue;
            if (index == 0) return 0;
            ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
            return checked((long)Math.Min(upper, checked((ulong)_maximumMicroseconds)));
        }
        return _maximumMicroseconds;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Game timing window belongs to another thread.");
    }
}
