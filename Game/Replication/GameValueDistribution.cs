using System.Numerics;

namespace UltraSim.Game.Replication;

/// <summary>
/// Bounded unsigned-value distribution. Recent percentiles are exact across the most recent 256
/// observations; whole-window percentiles are conservative power-of-two bucket upper bounds.
/// </summary>
public readonly record struct GameValueDistribution(
    ulong Count,
    ulong Total,
    double Average,
    ulong P50,
    ulong P95,
    ulong P99,
    ulong Maximum,
    int RollingSamples,
    ulong WholeWindowP50UpperBound,
    ulong WholeWindowP95UpperBound,
    ulong WholeWindowP99UpperBound);

/// <summary>Owner-local, fixed-storage value census used only by cold detailed diagnostics.</summary>
public sealed class GameValueDistributionWindow
{
    private const int MaximumSamples = 256;
    public const int ReservedPayloadBytes = (MaximumSamples + 65) * sizeof(ulong);
    private readonly ulong[] _samples = new ulong[MaximumSamples];
    private readonly ulong[] _wholeBuckets = new ulong[65];
    private int _next;
    private int _count;
    private ulong _observations;
    private ulong _total;
    private ulong _maximum;

    public void Record(ulong value)
    {
        _samples[_next] = value;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        if (_observations != ulong.MaxValue) _observations++;
        _total = ulong.MaxValue - _total < value ? ulong.MaxValue : _total + value;
        _maximum = Math.Max(_maximum, value);
        int bucket = value == 0 ? 0 : Math.Min(64, BitOperations.Log2(value) + 1);
        if (_wholeBuckets[bucket] != ulong.MaxValue) _wholeBuckets[bucket]++;
    }

    public GameValueDistribution Capture()
    {
        if (_count == 0) return default;
        Span<ulong> ordered = stackalloc ulong[MaximumSamples];
        _samples.AsSpan(0, _count).CopyTo(ordered);
        ordered[.._count].Sort();
        return new(
            _observations,
            _total,
            _observations == 0 ? 0d : (double)_total / _observations,
            ordered[Index(50)], ordered[Index(95)], ordered[Index(99)], _maximum, _count,
            WholePercentile(50), WholePercentile(95), WholePercentile(99));
    }

    public void Clear()
    {
        Array.Clear(_samples);
        Array.Clear(_wholeBuckets);
        _next = _count = 0;
        _observations = _total = _maximum = 0;
    }

    private int Index(int percentage) => Math.Clamp(
        (int)(((long)_count * percentage + 99) / 100) - 1, 0, _count - 1);

    private ulong WholePercentile(int percentage)
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
            ulong upper = index == 64 ? ulong.MaxValue : (1UL << index) - 1;
            return Math.Min(upper, _maximum);
        }
        return _maximum;
    }
}
