using System.Diagnostics;
using UltraSim.Game.Protocol;

namespace UltraSim.Game.Replication;

/// <summary>
/// Owner-local finite token bucket plus phase-independent rolling burst window for one
/// authenticated application lifetime. It allocates only its bounded construction-time timestamp
/// ring, uses monotonic time, and owns no Game state.
/// </summary>
internal sealed class ServerInputRateLimiter
{
    private readonly long _frequency = Stopwatch.Frequency;
    private readonly long _maximumTokens;
    private readonly int _maximumInputHz;
    private readonly long[] _burstAdmissions;
    private readonly long _burstWindowTicks;
    private long _tokens;
    private long _lastRefill;
    private int _burstHead;
    private int _burstCount;

    internal ServerInputRateLimiter(RemoteServerPerformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _maximumInputHz = profile.MaximumInputHz;
        _maximumTokens = checked((long)profile.InputBurstCapacity * _frequency);
        _burstAdmissions = new long[profile.InputBurstCapacity];
        _burstWindowTicks = Math.Max(1, checked(
            (long)((Int128)profile.InputBurstWindowMilliseconds * _frequency / 1_000)));
        _tokens = _maximumTokens;
        _lastRefill = Stopwatch.GetTimestamp();
    }

    internal bool TryAdmit() => TryAdmitAt(Stopwatch.GetTimestamp());

    internal bool TryAdmitAt(long now)
    {
        if (now < _lastRefill) now = _lastRefill;
        long elapsed = now - _lastRefill;
        if (elapsed != 0)
        {
            Int128 proportional = (Int128)elapsed * _maximumInputHz;
            long added = proportional >= _maximumTokens
                ? _maximumTokens
                : checked((long)proportional);
            _tokens = Math.Min(_maximumTokens, checked(_tokens + added));
            _lastRefill = now;
        }
        while (_burstCount != 0 &&
               now - _burstAdmissions[_burstHead] >= _burstWindowTicks)
        {
            _burstAdmissions[_burstHead] = 0;
            _burstHead = (_burstHead + 1) % _burstAdmissions.Length;
            _burstCount--;
        }
        if (_tokens < _frequency || _burstCount == _burstAdmissions.Length) return false;
        _tokens -= _frequency;
        int tail = (_burstHead + _burstCount) % _burstAdmissions.Length;
        _burstAdmissions[tail] = now;
        _burstCount++;
        return true;
    }
}
