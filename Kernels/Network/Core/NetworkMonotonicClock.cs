using System.Diagnostics;

namespace UltraSim.Network;

/// <summary>Internal monotonic time seam for transport policy and deterministic gates.</summary>
internal interface INetworkMonotonicClock
{
    ulong Milliseconds { get; }
}

internal sealed class StopwatchNetworkMonotonicClock : INetworkMonotonicClock
{
    internal static readonly StopwatchNetworkMonotonicClock Instance = new();

    private StopwatchNetworkMonotonicClock()
    {
    }

    public ulong Milliseconds
    {
        get
        {
            long timestamp = Stopwatch.GetTimestamp();
            long seconds = timestamp / Stopwatch.Frequency;
            long remainder = timestamp % Stopwatch.Frequency;
            return checked((ulong)(seconds * 1_000L + remainder * 1_000L / Stopwatch.Frequency));
        }
    }
}
