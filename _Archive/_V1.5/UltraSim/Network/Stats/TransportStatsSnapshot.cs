using System.Diagnostics;

namespace UltraSim.Network
{
    public readonly struct TransportStatsSnapshot
    {
        public readonly long OutboundPackets;
        public readonly long OutboundBytes;
        public readonly long InboundPackets;
        public readonly long InboundBytes;
        public readonly long DroppedPackets;
        public readonly long DroppedBytes;
        public readonly long PollCalls;
        public readonly long PollTicks;
        public readonly long FlushCalls;
        public readonly long FlushTicks;

        public TransportStatsSnapshot(
            long outboundPackets,
            long outboundBytes,
            long inboundPackets,
            long inboundBytes,
            long droppedPackets,
            long droppedBytes,
            long pollCalls,
            long pollTicks,
            long flushCalls,
            long flushTicks)
        {
            OutboundPackets = outboundPackets;
            OutboundBytes = outboundBytes;
            InboundPackets = inboundPackets;
            InboundBytes = inboundBytes;
            DroppedPackets = droppedPackets;
            DroppedBytes = droppedBytes;
            PollCalls = pollCalls;
            PollTicks = pollTicks;
            FlushCalls = flushCalls;
            FlushTicks = flushTicks;
        }

        public double PollMilliseconds => TicksToMilliseconds(PollTicks);
        public double FlushMilliseconds => TicksToMilliseconds(FlushTicks);

        private static double TicksToMilliseconds(long ticks) =>
            ticks <= 0 ? 0d : (ticks * 1000d) / Stopwatch.Frequency;
    }
}
