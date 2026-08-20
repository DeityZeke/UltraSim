using UltraSim.Network;

namespace UltraSim.Client.Engines.Networking
{
    public readonly struct ClientNetworkBridgeStatsSnapshot
    {
        public readonly ClientNetworkTickResult LastTickResult;
        public readonly ClientCorrectionStatsSnapshot Correction;
        public readonly InboundPipelineStatsSnapshot Inbound;
        public readonly long PollElapsedTicksTotal;
        public readonly long QueueElapsedTicksTotal;
        public readonly long ApplyElapsedTicksTotal;
        public readonly long ApplyHandlerElapsedTicksTotal;
        public readonly long ApplyStatsElapsedTicksTotal;
        public readonly long WorldElapsedTicksTotal;
        public readonly long FlushElapsedTicksTotal;
        public readonly long MaxPollElapsedTicks;
        public readonly long MaxQueueElapsedTicks;
        public readonly long MaxApplyElapsedTicks;
        public readonly long MaxApplyHandlerElapsedTicks;
        public readonly long MaxApplyStatsElapsedTicks;
        public readonly long MaxWorldElapsedTicks;
        public readonly long MaxFlushElapsedTicks;
        public readonly int TickCount;

        public ClientNetworkBridgeStatsSnapshot(
            in ClientNetworkTickResult lastTickResult,
            in ClientCorrectionStatsSnapshot correction,
            in InboundPipelineStatsSnapshot inbound,
            long pollElapsedTicksTotal,
            long queueElapsedTicksTotal,
            long applyElapsedTicksTotal,
            long applyHandlerElapsedTicksTotal,
            long applyStatsElapsedTicksTotal,
            long worldElapsedTicksTotal,
            long flushElapsedTicksTotal,
            long maxPollElapsedTicks,
            long maxQueueElapsedTicks,
            long maxApplyElapsedTicks,
            long maxApplyHandlerElapsedTicks,
            long maxApplyStatsElapsedTicks,
            long maxWorldElapsedTicks,
            long maxFlushElapsedTicks,
            int tickCount)
        {
            LastTickResult = lastTickResult;
            Correction = correction;
            Inbound = inbound;
            PollElapsedTicksTotal = pollElapsedTicksTotal;
            QueueElapsedTicksTotal = queueElapsedTicksTotal;
            ApplyElapsedTicksTotal = applyElapsedTicksTotal;
            ApplyHandlerElapsedTicksTotal = applyHandlerElapsedTicksTotal;
            ApplyStatsElapsedTicksTotal = applyStatsElapsedTicksTotal;
            WorldElapsedTicksTotal = worldElapsedTicksTotal;
            FlushElapsedTicksTotal = flushElapsedTicksTotal;
            MaxPollElapsedTicks = maxPollElapsedTicks;
            MaxQueueElapsedTicks = maxQueueElapsedTicks;
            MaxApplyElapsedTicks = maxApplyElapsedTicks;
            MaxApplyHandlerElapsedTicks = maxApplyHandlerElapsedTicks;
            MaxApplyStatsElapsedTicks = maxApplyStatsElapsedTicks;
            MaxWorldElapsedTicks = maxWorldElapsedTicks;
            MaxFlushElapsedTicks = maxFlushElapsedTicks;
            TickCount = tickCount;
        }
    }
}
