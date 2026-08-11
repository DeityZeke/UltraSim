using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Owner-controlled cadence values; graph topology remains sealed.</summary>
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

    public bool Enabled { get; }

    public int PeriodTicks { get; }

    public int StartOffset { get; }

    public bool Equals(SystemSchedule other) =>
        Enabled == other.Enabled &&
        PeriodTicks == other.PeriodTicks &&
        StartOffset == other.StartOffset;

    public override bool Equals(object? obj) => obj is SystemSchedule other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Enabled, PeriodTicks, StartOffset);
}
