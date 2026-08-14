using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Owner-controlled deterministic cadence; graph topology remains sealed.</summary>
/// <remarks>
/// A due tick satisfies the scheduler's period/offset rule. Offset zero includes
/// tick zero. Dependencies order due invocations but do not force a dependency to
/// run on a tick where its own cadence is not due. This is step cadence:
/// <c>PeriodTicks = 10</c> means every ten authoritative simulation steps, not
/// every ten milliseconds. Time-relative behavior belongs in
/// <c>DeltaSeconds</c>, authoritative world time, or a game-owned accumulator.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
public readonly struct SystemSchedule : IEquatable<SystemSchedule>
{
    public SystemSchedule(bool enabled = true, int periodTicks = 1, int startOffset = 0)
    {
        if (periodTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodTicks));
        if (startOffset < 0 || startOffset >= periodTicks)
            throw new ArgumentOutOfRangeException(nameof(startOffset));

        Enabled = enabled;
        PeriodTicks = periodTicks;
        StartOffset = startOffset;
    }

    /// <summary>Gets whether automatic tick execution is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the positive execution period in authoritative simulation steps.</summary>
    public int PeriodTicks { get; }

    /// <summary>Gets the due-step phase in the range zero through period minus one.</summary>
    public int StartOffset { get; }

    public bool Equals(SystemSchedule other) =>
        Enabled == other.Enabled &&
        PeriodTicks == other.PeriodTicks &&
        StartOffset == other.StartOffset;

    public override bool Equals(object? obj) => obj is SystemSchedule other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Enabled, PeriodTicks, StartOffset);
}
