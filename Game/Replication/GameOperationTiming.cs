using System.Diagnostics;

namespace UltraSim.Game.Replication;

/// <summary>Cold bounded duration distribution owned by a Game operation.</summary>
public readonly record struct GameTimingDistribution(
    ulong Count,
    ulong TotalStopwatchTicks,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long MaximumMicroseconds);

/// <summary>
/// Owner-thread fixed timing window. Detailed recording is allocation-free; Off
/// and Basic perform no timestamp read through <see cref="StartDetailed"/>.
/// </summary>
public sealed class GameOperationTimingWindow
{
    public const int MaximumSamples = 256;
    private readonly long[] _samples = new long[MaximumSamples];
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
            _maximumMicroseconds);
    }

    private int Index(double percentile) => Math.Clamp(
        (int)Math.Ceiling(_count * percentile) - 1, 0, _count - 1);

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Game timing window belongs to another thread.");
    }
}
