using System.Diagnostics;
using UltraSim.Network;

namespace UltraSim.Client;

public static class ClientNetworkSummaryFormatter
{
    public static string FormatOffline()
    {
        return "Network: offline";
    }

    public static string Format(
        bool isReady,
        SessionId sessionId,
        double lastPingRoundTripMilliseconds,
        in ClientPumpProfileSnapshot profile)
    {
        string state = isReady ? "ready" : "connecting";
        string ping = lastPingRoundTripMilliseconds >= 0.0
            ? $"{lastPingRoundTripMilliseconds:0.0}ms"
            : "--";

        return
            $"Network: {state} session:{sessionId.Value} " +
            $"ping:{ping} " +
            $"in:{profile.InboundDatagramCount} poll:{TicksToMilliseconds(profile.PollElapsedTicks):0.00}ms " +
            $"move:{TicksToMilliseconds(profile.MovementElapsedTicks):0.00}ms " +
            $"obs:{TicksToMilliseconds(profile.ObserverStateElapsedTicks):0.00}ms " +
            $"evt:{TicksToMilliseconds(profile.ObserverEventElapsedTicks):0.00}ms";
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks <= 0
            ? 0.0
            : ticks * 1000.0 / Stopwatch.Frequency;
    }
}
