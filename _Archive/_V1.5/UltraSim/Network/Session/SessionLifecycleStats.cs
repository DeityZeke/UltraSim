using System;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class SessionLifecycleStats
    {
        private readonly long[] _disconnectReasonCounts = new long[Enum.GetValues<SessionDisconnectReason>().Length];
        private long _created;
        private long _toHandshaking;
        private long _toAuthenticated;
        private long _toReady;
        private long _toDisconnecting;
        private long _toDisconnected;
        private long _removed;
        private long _rejectedCreates;
        private long _activityTouches;

        public void RecordCreated() => Interlocked.Increment(ref _created);

        public void RecordTransition(SessionState state)
        {
            switch (state)
            {
                case SessionState.Handshaking:
                    Interlocked.Increment(ref _toHandshaking);
                    break;
                case SessionState.Authenticated:
                    Interlocked.Increment(ref _toAuthenticated);
                    break;
                case SessionState.Ready:
                    Interlocked.Increment(ref _toReady);
                    break;
                case SessionState.Disconnecting:
                    Interlocked.Increment(ref _toDisconnecting);
                    break;
                case SessionState.Disconnected:
                    Interlocked.Increment(ref _toDisconnected);
                    break;
            }
        }

        public void RecordDisconnectReason(SessionDisconnectReason reason)
        {
            int index = (int)reason;
            if ((uint)index < (uint)_disconnectReasonCounts.Length)
                Interlocked.Increment(ref _disconnectReasonCounts[index]);
        }

        public void RecordRemoved() => Interlocked.Increment(ref _removed);

        public void RecordRejectedCreate() => Interlocked.Increment(ref _rejectedCreates);

        public void RecordActivityTouch() => Interlocked.Increment(ref _activityTouches);

        public SessionLifecycleStatsSnapshot Snapshot()
        {
            var reasons = new long[_disconnectReasonCounts.Length];
            for (int i = 0; i < reasons.Length; i++)
                reasons[i] = Interlocked.Read(ref _disconnectReasonCounts[i]);

            return new SessionLifecycleStatsSnapshot(
                created: Interlocked.Read(ref _created),
                transitionedToHandshaking: Interlocked.Read(ref _toHandshaking),
                transitionedToAuthenticated: Interlocked.Read(ref _toAuthenticated),
                transitionedToReady: Interlocked.Read(ref _toReady),
                transitionedToDisconnecting: Interlocked.Read(ref _toDisconnecting),
                transitionedToDisconnected: Interlocked.Read(ref _toDisconnected),
                removed: Interlocked.Read(ref _removed),
                rejectedCreates: Interlocked.Read(ref _rejectedCreates),
                activityTouches: Interlocked.Read(ref _activityTouches),
                disconnectReasonCounts: reasons);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _created, 0);
            Interlocked.Exchange(ref _toHandshaking, 0);
            Interlocked.Exchange(ref _toAuthenticated, 0);
            Interlocked.Exchange(ref _toReady, 0);
            Interlocked.Exchange(ref _toDisconnecting, 0);
            Interlocked.Exchange(ref _toDisconnected, 0);
            Interlocked.Exchange(ref _removed, 0);
            Interlocked.Exchange(ref _rejectedCreates, 0);
            Interlocked.Exchange(ref _activityTouches, 0);

            for (int i = 0; i < _disconnectReasonCounts.Length; i++)
                Interlocked.Exchange(ref _disconnectReasonCounts[i], 0);
        }
    }
}
