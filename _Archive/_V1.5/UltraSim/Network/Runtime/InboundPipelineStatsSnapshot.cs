namespace UltraSim.Network
{
    public readonly struct InboundPipelineStatsSnapshot
    {
        public readonly long PolledDatagrams;
        public readonly long DecodedDatagrams;
        public readonly long DroppedUnknownSessionDatagrams;
        public readonly long DroppedRateLimitedDatagrams;
        public readonly long RateLimitStrikeEvents;
        public readonly long RateLimitDisconnectSuggestions;
        public readonly long AppliedCommands;
        public readonly long FlushCalls;
        public readonly long PollCalls;
        public readonly long SessionCacheHits;
        public readonly long SessionCacheMisses;
        public readonly long PollElapsedTicksTotal;
        public readonly long ResolveElapsedTicksTotal;
        public readonly long RateLimitElapsedTicksTotal;
        public readonly long DecodeElapsedTicksTotal;
        public readonly long ConsumeElapsedTicksTotal;
        public readonly long MaxPollElapsedTicks;
        public readonly long MaxResolveElapsedTicks;
        public readonly long MaxRateLimitElapsedTicks;
        public readonly long MaxDecodeElapsedTicks;
        public readonly long MaxConsumeElapsedTicks;

        public InboundPipelineStatsSnapshot(
            long polledDatagrams,
            long decodedDatagrams,
            long droppedUnknownSessionDatagrams,
            long droppedRateLimitedDatagrams,
            long rateLimitStrikeEvents,
            long rateLimitDisconnectSuggestions,
            long appliedCommands,
            long flushCalls,
            long pollCalls,
            long sessionCacheHits,
            long sessionCacheMisses,
            long pollElapsedTicksTotal,
            long resolveElapsedTicksTotal,
            long rateLimitElapsedTicksTotal,
            long decodeElapsedTicksTotal,
            long consumeElapsedTicksTotal,
            long maxPollElapsedTicks,
            long maxResolveElapsedTicks,
            long maxRateLimitElapsedTicks,
            long maxDecodeElapsedTicks,
            long maxConsumeElapsedTicks)
        {
            PolledDatagrams = polledDatagrams;
            DecodedDatagrams = decodedDatagrams;
            DroppedUnknownSessionDatagrams = droppedUnknownSessionDatagrams;
            DroppedRateLimitedDatagrams = droppedRateLimitedDatagrams;
            RateLimitStrikeEvents = rateLimitStrikeEvents;
            RateLimitDisconnectSuggestions = rateLimitDisconnectSuggestions;
            AppliedCommands = appliedCommands;
            FlushCalls = flushCalls;
            PollCalls = pollCalls;
            SessionCacheHits = sessionCacheHits;
            SessionCacheMisses = sessionCacheMisses;
            PollElapsedTicksTotal = pollElapsedTicksTotal;
            ResolveElapsedTicksTotal = resolveElapsedTicksTotal;
            RateLimitElapsedTicksTotal = rateLimitElapsedTicksTotal;
            DecodeElapsedTicksTotal = decodeElapsedTicksTotal;
            ConsumeElapsedTicksTotal = consumeElapsedTicksTotal;
            MaxPollElapsedTicks = maxPollElapsedTicks;
            MaxResolveElapsedTicks = maxResolveElapsedTicks;
            MaxRateLimitElapsedTicks = maxRateLimitElapsedTicks;
            MaxDecodeElapsedTicks = maxDecodeElapsedTicks;
            MaxConsumeElapsedTicks = maxConsumeElapsedTicks;
        }
    }
}
