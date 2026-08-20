namespace UltraSim.Network
{
    public readonly struct ReplicationStatsSnapshot
    {
        private readonly long[] _sentByLane;
        private readonly long[] _deferredByLane;
        private readonly long[] _droppedByLane;
        private readonly long[] _coalescedByLane;

        public readonly long DirtyDrained;
        public readonly long DeltasEncoded;
        public readonly long EncodedBytes;
        public readonly long InterestedSelected;
        public readonly long PacketsSent;
        public readonly long BytesSent;
        public readonly long ReplayTicksPruned;
        public readonly long InterestFilterCalls;
        public readonly long InterestCacheLookups;
        public readonly long InterestCacheHits;
        public readonly long InterestCacheMisses;
        public readonly long FanoutReuseLookups;
        public readonly long FanoutReuseHits;
        public readonly long FanoutReuseMisses;
        public readonly long FanoutReuseBytesSaved;
        public readonly long SendAttempts;

        public ReplicationStatsSnapshot(
            long dirtyDrained,
            long deltasEncoded,
            long encodedBytes,
            long interestedSelected,
            long packetsSent,
            long bytesSent,
            long replayTicksPruned,
            long interestFilterCalls,
            long interestCacheLookups,
            long interestCacheHits,
            long interestCacheMisses,
            long fanoutReuseLookups,
            long fanoutReuseHits,
            long fanoutReuseMisses,
            long fanoutReuseBytesSaved,
            long sendAttempts,
            long[] sentByLane,
            long[] deferredByLane,
            long[] droppedByLane,
            long[] coalescedByLane)
        {
            DirtyDrained = dirtyDrained;
            DeltasEncoded = deltasEncoded;
            EncodedBytes = encodedBytes;
            InterestedSelected = interestedSelected;
            PacketsSent = packetsSent;
            BytesSent = bytesSent;
            ReplayTicksPruned = replayTicksPruned;
            InterestFilterCalls = interestFilterCalls;
            InterestCacheLookups = interestCacheLookups;
            InterestCacheHits = interestCacheHits;
            InterestCacheMisses = interestCacheMisses;
            FanoutReuseLookups = fanoutReuseLookups;
            FanoutReuseHits = fanoutReuseHits;
            FanoutReuseMisses = fanoutReuseMisses;
            FanoutReuseBytesSaved = fanoutReuseBytesSaved;
            SendAttempts = sendAttempts;
            _sentByLane = sentByLane;
            _deferredByLane = deferredByLane;
            _droppedByLane = droppedByLane;
            _coalescedByLane = coalescedByLane;
        }

        public long GetSentByLane(ReplicationLane lane) => GetLaneValue(_sentByLane, lane);

        public long GetDeferredByLane(ReplicationLane lane) => GetLaneValue(_deferredByLane, lane);

        public long GetDroppedByLane(ReplicationLane lane) => GetLaneValue(_droppedByLane, lane);

        public long GetCoalescedByLane(ReplicationLane lane) => GetLaneValue(_coalescedByLane, lane);

        private static long GetLaneValue(long[]? values, ReplicationLane lane)
        {
            if (values == null || values.Length == 0)
                return 0;

            int index = (int)lane;
            if ((uint)index >= (uint)values.Length)
                return 0;

            return values[index];
        }
    }
}
