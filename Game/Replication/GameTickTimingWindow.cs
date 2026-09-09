using System.Diagnostics;

namespace UltraSim.Game.Replication;

/// <summary>Cold snapshot of one bounded authoritative fixed-step timing window.</summary>
public readonly record struct GameTickTimingSnapshot(
    int SampleCount,
    long RecentMicroseconds,
    long P95Microseconds);

/// <summary>
/// Owner-thread bounded timing window for authoritative fixed steps. Recording is allocation-free;
/// percentile sorting is performed only for cold diagnostics capture.
/// </summary>
public sealed class GameTickTimingWindow
{
    public const int MaximumSamples = 128;
    private readonly long[] _microseconds = new long[MaximumSamples];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _next;
    private int _count;

    public void RecordElapsed(long startTimestamp)
    {
        EnsureOwner();
        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - startTimestamp);
        long seconds = elapsed / Stopwatch.Frequency;
        long remainder = elapsed % Stopwatch.Frequency;
        long microseconds = checked(
            seconds * 1_000_000L + remainder * 1_000_000L / Stopwatch.Frequency);
        _microseconds[_next] = microseconds;
        _next = (_next + 1) % _microseconds.Length;
        if (_count < _microseconds.Length) _count++;
    }

    public GameTickTimingSnapshot Capture()
    {
        EnsureOwner();
        if (_count == 0) return default;
        Span<long> ordered = stackalloc long[MaximumSamples];
        _microseconds.AsSpan(0, _count).CopyTo(ordered);
        ordered[.._count].Sort();
        int p95 = Math.Clamp((int)Math.Ceiling(_count * 0.95d) - 1, 0, _count - 1);
        int recent = (_next - 1 + _microseconds.Length) % _microseconds.Length;
        return new GameTickTimingSnapshot(
            _count,
            _microseconds[recent],
            ordered[p95]);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The tick timing window belongs to another thread.");
    }
}
