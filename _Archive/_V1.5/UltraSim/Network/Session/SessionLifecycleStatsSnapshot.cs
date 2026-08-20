using System;

namespace UltraSim.Network
{
    public readonly struct SessionLifecycleStatsSnapshot
    {
        private readonly long[] _disconnectReasonCounts;

        public readonly long Created;
        public readonly long TransitionedToHandshaking;
        public readonly long TransitionedToAuthenticated;
        public readonly long TransitionedToReady;
        public readonly long TransitionedToDisconnecting;
        public readonly long TransitionedToDisconnected;
        public readonly long Removed;
        public readonly long RejectedCreates;
        public readonly long ActivityTouches;

        public SessionLifecycleStatsSnapshot(
            long created,
            long transitionedToHandshaking,
            long transitionedToAuthenticated,
            long transitionedToReady,
            long transitionedToDisconnecting,
            long transitionedToDisconnected,
            long removed,
            long rejectedCreates,
            long activityTouches,
            long[] disconnectReasonCounts)
        {
            Created = created;
            TransitionedToHandshaking = transitionedToHandshaking;
            TransitionedToAuthenticated = transitionedToAuthenticated;
            TransitionedToReady = transitionedToReady;
            TransitionedToDisconnecting = transitionedToDisconnecting;
            TransitionedToDisconnected = transitionedToDisconnected;
            Removed = removed;
            RejectedCreates = rejectedCreates;
            ActivityTouches = activityTouches;
            _disconnectReasonCounts = disconnectReasonCounts;
        }

        public long GetDisconnectReasonCount(SessionDisconnectReason reason)
        {
            int index = (int)reason;
            if ((uint)index >= (uint)_disconnectReasonCounts.Length)
                return 0;

            return _disconnectReasonCounts[index];
        }

        public int CopyTopDisconnectReasons(int maxCount, Span<SessionDisconnectReasonCount> destination)
        {
            if (maxCount <= 0 || destination.Length == 0)
                return 0;

            int limit = maxCount < destination.Length ? maxCount : destination.Length;
            int written = 0;
            var used = new bool[_disconnectReasonCounts.Length];

            for (int i = 0; i < limit; i++)
            {
                int bestIndex = -1;
                long bestCount = 0;
                for (int j = 0; j < _disconnectReasonCounts.Length; j++)
                {
                    if (used[j])
                        continue;

                    long count = _disconnectReasonCounts[j];
                    if (count <= 0)
                        continue;

                    if (count > bestCount)
                    {
                        bestCount = count;
                        bestIndex = j;
                    }
                }

                if (bestIndex < 0)
                    break;

                used[bestIndex] = true;
                destination[written++] = new SessionDisconnectReasonCount(
                    (SessionDisconnectReason)bestIndex,
                    bestCount);
            }

            return written;
        }
    }
}
