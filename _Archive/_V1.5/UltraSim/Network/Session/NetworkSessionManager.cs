using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class NetworkSessionManager
    {
        private static readonly Dictionary<int, long> EmptyConnectionSnapshot = new Dictionary<int, long>();

        private sealed class SessionEntry
        {
            public long SessionId;
            public int ConnectionId;
            public SessionState State;
            public SessionDisconnectReason DisconnectReason;
            public long OpaqueGameIdentityKey;
            public long CreatedTicks;
            public long LastActivityTicks;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<long, SessionEntry> _sessionsById = new Dictionary<long, SessionEntry>();
        private readonly Dictionary<int, long> _sessionIdByConnection = new Dictionary<int, long>();
        private Dictionary<int, long> _sessionIdByConnectionSnapshot = new Dictionary<int, long>();
        private readonly Queue<SessionLifecycleEvent> _lifecycleEvents = new Queue<SessionLifecycleEvent>();
        private readonly List<SessionEntry> _timeoutSweepScratch = new List<SessionEntry>(64);
        private readonly SessionLifecycleStats _stats;
        private readonly DuplicateSessionPolicy _duplicateSessionPolicy;
        private readonly int _maxActiveSessions;
        private long _nextSessionId = 1;

        public NetworkSessionManager(
            SessionLifecycleStats? stats = null,
            DuplicateSessionPolicy duplicateSessionPolicy = DuplicateSessionPolicy.RejectNew,
            int maxActiveSessions = 0)
        {
            if (maxActiveSessions < 0)
                throw new ArgumentOutOfRangeException(nameof(maxActiveSessions));

            _stats = stats ?? new SessionLifecycleStats();
            _duplicateSessionPolicy = duplicateSessionPolicy;
            _maxActiveSessions = maxActiveSessions;
        }

        public SessionLifecycleStats Stats
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _stats;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_sync)
                    return _sessionsById.Count;
            }
        }

        public long CreateSession(int connectionId, long nowTicks)
        {
            if (TryCreateSession(connectionId, nowTicks, out long sessionId, out SessionDisconnectReason rejectReason))
                return sessionId;

            if (rejectReason == SessionDisconnectReason.DuplicateSession)
                ThrowDuplicateConnection(connectionId);

            if (rejectReason == SessionDisconnectReason.ServerBusy)
                ThrowSessionCapacityExceeded(connectionId, _maxActiveSessions);

            throw new InvalidOperationException($"Session creation rejected for connection {connectionId}: {rejectReason}.");
        }

        public bool TryCreateSession(
            int connectionId,
            long nowTicks,
            out long sessionId,
            out SessionDisconnectReason rejectReason)
        {
            lock (_sync)
            {
                sessionId = 0;
                rejectReason = SessionDisconnectReason.None;

                if (_sessionIdByConnection.TryGetValue(connectionId, out long existingSessionId))
                {
                    if (!_sessionsById.TryGetValue(existingSessionId, out SessionEntry? existingSession))
                    {
                        _sessionIdByConnection.Remove(connectionId);
                        PublishConnectionSnapshot_NoLock();
                    }
                    else if (existingSession.State == SessionState.Disconnected)
                    {
                        RemoveSession_NoLock(existingSession, nowTicks);
                    }
                    else
                    {
                        if (_duplicateSessionPolicy == DuplicateSessionPolicy.RejectNew)
                        {
                            rejectReason = SessionDisconnectReason.DuplicateSession;
                            RecordRejectedAdmission_NoLock(connectionId, rejectReason, nowTicks);
                            return false;
                        }

                        ForceDisconnectAndRemove_NoLock(existingSession, SessionDisconnectReason.DuplicateSession, nowTicks);
                    }
                }

                if (_maxActiveSessions > 0 && _sessionsById.Count >= _maxActiveSessions)
                {
                    rejectReason = SessionDisconnectReason.ServerBusy;
                    RecordRejectedAdmission_NoLock(connectionId, rejectReason, nowTicks);
                    return false;
                }

                if (_nextSessionId <= 0 || _nextSessionId == long.MaxValue)
                    ThrowSessionIdExhausted();

                sessionId = _nextSessionId++;
                var entry = new SessionEntry
                {
                    SessionId = sessionId,
                    ConnectionId = connectionId,
                    State = SessionState.Connecting,
                    DisconnectReason = SessionDisconnectReason.None,
                    OpaqueGameIdentityKey = 0,
                    CreatedTicks = nowTicks,
                    LastActivityTicks = nowTicks
                };

                _sessionsById.Add(sessionId, entry);
                _sessionIdByConnection.Add(connectionId, sessionId);
                PublishConnectionSnapshot_NoLock();

                _stats.RecordCreated();
                EnqueueLifecycleEvent_NoLock(
                    SessionLifecycleEventKind.Created,
                    entry,
                    previousState: SessionState.Disconnected,
                    nowTicks);

                return true;
            }
        }

        public bool TryGetSessionIdByConnection(int connectionId, out long sessionId)
        {
            Dictionary<int, long> snapshot = Volatile.Read(ref _sessionIdByConnectionSnapshot);
            return snapshot.TryGetValue(connectionId, out sessionId);
        }

        public Dictionary<int, long> CopyConnectionSnapshot()
        {
            Dictionary<int, long> snapshot = Volatile.Read(ref _sessionIdByConnectionSnapshot);
            return snapshot.Count == 0
                ? new Dictionary<int, long>()
                : new Dictionary<int, long>(snapshot);
        }

        public bool TryGetSnapshot(long sessionId, out SessionSnapshot snapshot)
        {
            lock (_sync)
            {
                if (_sessionsById.TryGetValue(sessionId, out var session))
                {
                    snapshot = CreateSnapshot(session);
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        public int DrainLifecycleEvents(Span<SessionLifecycleEvent> destination)
        {
            lock (_sync)
            {
                int count = 0;
                while (count < destination.Length && _lifecycleEvents.Count > 0)
                    destination[count++] = _lifecycleEvents.Dequeue();

                return count;
            }
        }

        public bool TryMarkHandshaking(long sessionId, long nowTicks) =>
            TryTransition(sessionId, SessionState.Connecting, SessionState.Handshaking, nowTicks);

        public bool TryMarkAuthenticated(long sessionId, long nowTicks) =>
            TryTransition(sessionId, SessionState.Handshaking, SessionState.Authenticated, nowTicks);

        public bool TryMarkReady(long sessionId, long nowTicks) =>
            TryTransition(sessionId, SessionState.Authenticated, SessionState.Ready, nowTicks);

        public bool TryReturnToAuthenticated(long sessionId, long nowTicks) =>
            TryTransition(sessionId, SessionState.Ready, SessionState.Authenticated, nowTicks);

        public bool TryBeginDisconnect(long sessionId, SessionDisconnectReason reason, long nowTicks)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                    return false;

                if (session.State == SessionState.Disconnected || session.State == SessionState.Disconnecting)
                    return false;

                var previous = session.State;
                session.State = SessionState.Disconnecting;
                session.DisconnectReason = reason;
                session.LastActivityTicks = nowTicks;

                _stats.RecordTransition(SessionState.Disconnecting);
                _stats.RecordDisconnectReason(reason);
                EnqueueLifecycleEvent_NoLock(SessionLifecycleEventKind.StateChanged, session, previous, nowTicks);
                return true;
            }
        }

        public bool TryCompleteDisconnect(long sessionId, long nowTicks)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                    return false;

                if (session.State != SessionState.Disconnecting)
                    return false;

                var previous = session.State;
                session.State = SessionState.Disconnected;
                session.LastActivityTicks = nowTicks;

                _stats.RecordTransition(SessionState.Disconnected);
                EnqueueLifecycleEvent_NoLock(SessionLifecycleEventKind.StateChanged, session, previous, nowTicks);
                return true;
            }
        }

        public bool TryRemoveDisconnected(long sessionId)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                    return false;

                if (session.State != SessionState.Disconnected)
                    return false;

                RemoveSession_NoLock(session, nowTicks: session.LastActivityTicks);
                return true;
            }
        }

        public bool TryDisconnectSession(
            long sessionId,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? session))
                    return false;

                ForceDisconnect_NoLock(session, reason, nowTicks, removeDisconnected);
                return true;
            }
        }

        public bool TryDisconnectConnection(
            int connectionId,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true)
        {
            lock (_sync)
            {
                if (!_sessionIdByConnection.TryGetValue(connectionId, out long sessionId))
                    return false;

                if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? session))
                    return false;

                ForceDisconnect_NoLock(session, reason, nowTicks, removeDisconnected);
                return true;
            }
        }

        public int DisconnectAll(
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected = true)
        {
            lock (_sync)
            {
                if (_sessionsById.Count == 0)
                    return 0;

                List<SessionEntry> snapshot = _timeoutSweepScratch;
                snapshot.Clear();
                foreach (SessionEntry session in _sessionsById.Values)
                    snapshot.Add(session);

                int disconnected = 0;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    SessionEntry session = snapshot[i];
                    if (!_sessionsById.ContainsKey(session.SessionId))
                        continue;

                    ForceDisconnect_NoLock(session, reason, nowTicks, removeDisconnected);
                    disconnected++;
                }

                snapshot.Clear();
                return disconnected;
            }
        }

        public bool TryTouchActivity(long sessionId, long nowTicks)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                    return false;

                session.LastActivityTicks = nowTicks;
                _stats.RecordActivityTouch();
                return true;
            }
        }

        public bool TrySetOpaqueGameIdentityKey(long sessionId, long opaqueGameIdentityKey)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                    return false;

                session.OpaqueGameIdentityKey = opaqueGameIdentityKey;
                return true;
            }
        }

        public bool TryGetOpaqueGameIdentityKey(long sessionId, out long opaqueGameIdentityKey)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                {
                    opaqueGameIdentityKey = 0;
                    return false;
                }

                opaqueGameIdentityKey = session.OpaqueGameIdentityKey;
                return true;
            }
        }

        public int SweepTimeouts(
            long nowTicks,
            long authTimeoutTicks,
            long keepAliveTimeoutTicks,
            bool removeDisconnected = true)
        {
            lock (_sync)
            {
                List<SessionEntry> timedOut = _timeoutSweepScratch;
                timedOut.Clear();
                foreach (SessionEntry session in _sessionsById.Values)
                {
                    if (ShouldTimeout(session, nowTicks, authTimeoutTicks, keepAliveTimeoutTicks))
                        timedOut.Add(session);
                }

                if (timedOut.Count == 0)
                    return 0;

                int count = 0;
                for (int i = 0; i < timedOut.Count; i++)
                {
                    SessionEntry session = timedOut[i];
                    if (!ShouldTimeout(session, nowTicks, authTimeoutTicks, keepAliveTimeoutTicks))
                        continue;

                    if (session.State == SessionState.Disconnected)
                    {
                        if (removeDisconnected)
                        {
                            RemoveSession_NoLock(session, nowTicks);
                            count++;
                        }

                        continue;
                    }

                    if (session.State != SessionState.Disconnecting)
                        TransitionToDisconnecting_NoLock(session, SessionDisconnectReason.Timeout, nowTicks);

                    TransitionToDisconnected_NoLock(session, nowTicks);
                    if (removeDisconnected)
                        RemoveSession_NoLock(session, nowTicks);

                    count++;
                }

                timedOut.Clear();
                return count;
            }
        }

        private bool TryTransition(long sessionId, SessionState expected, SessionState next, long nowTicks)
        {
            lock (_sync)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var session))
                    return false;

                if (session.State != expected)
                    return false;

                var previous = session.State;
                session.State = next;
                session.LastActivityTicks = nowTicks;
                _stats.RecordTransition(next);
                EnqueueLifecycleEvent_NoLock(SessionLifecycleEventKind.StateChanged, session, previous, nowTicks);
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SessionSnapshot CreateSnapshot(SessionEntry session) =>
            new SessionSnapshot(
                sessionId: session.SessionId,
                connectionId: session.ConnectionId,
                state: session.State,
                disconnectReason: session.DisconnectReason,
                opaqueGameIdentityKey: session.OpaqueGameIdentityKey,
                createdTicks: session.CreatedTicks,
                lastActivityTicks: session.LastActivityTicks);

        private static bool ShouldTimeout(
            SessionEntry session,
            long nowTicks,
            long authTimeoutTicks,
            long keepAliveTimeoutTicks)
        {
            long elapsed = nowTicks - session.LastActivityTicks;
            if (elapsed < 0)
                return false;

            if (session.State == SessionState.Ready ||
                session.State == SessionState.Disconnecting ||
                session.State == SessionState.Disconnected)
                return keepAliveTimeoutTicks > 0 && elapsed >= keepAliveTimeoutTicks;

            return IsPreReadyState(session.State) && authTimeoutTicks > 0 && elapsed >= authTimeoutTicks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPreReadyState(SessionState state) =>
            state == SessionState.Connecting ||
            state == SessionState.Handshaking ||
            state == SessionState.Authenticated;

        private void ForceDisconnectAndRemove_NoLock(
            SessionEntry session,
            SessionDisconnectReason reason,
            long nowTicks)
        {
            ForceDisconnect_NoLock(session, reason, nowTicks, removeDisconnected: true);
        }

        private void ForceDisconnect_NoLock(
            SessionEntry session,
            SessionDisconnectReason reason,
            long nowTicks,
            bool removeDisconnected)
        {
            if (session.State != SessionState.Disconnecting && session.State != SessionState.Disconnected)
                TransitionToDisconnecting_NoLock(session, reason, nowTicks);

            if (session.State != SessionState.Disconnected)
                TransitionToDisconnected_NoLock(session, nowTicks);

            if (removeDisconnected)
                RemoveSession_NoLock(session, nowTicks);
        }

        private void TransitionToDisconnecting_NoLock(
            SessionEntry session,
            SessionDisconnectReason reason,
            long nowTicks)
        {
            SessionState previous = session.State;
            session.State = SessionState.Disconnecting;
            session.DisconnectReason = reason;
            session.LastActivityTicks = nowTicks;

            _stats.RecordTransition(SessionState.Disconnecting);
            _stats.RecordDisconnectReason(reason);
            EnqueueLifecycleEvent_NoLock(SessionLifecycleEventKind.StateChanged, session, previous, nowTicks);
        }

        private void TransitionToDisconnected_NoLock(SessionEntry session, long nowTicks)
        {
            SessionState previous = session.State;
            session.State = SessionState.Disconnected;
            session.LastActivityTicks = nowTicks;

            _stats.RecordTransition(SessionState.Disconnected);
            EnqueueLifecycleEvent_NoLock(SessionLifecycleEventKind.StateChanged, session, previous, nowTicks);
        }

        private void RemoveSession_NoLock(SessionEntry session, long nowTicks)
        {
            _sessionsById.Remove(session.SessionId);
            _sessionIdByConnection.Remove(session.ConnectionId);
            PublishConnectionSnapshot_NoLock();
            _stats.RecordRemoved();
            EnqueueLifecycleEvent_NoLock(SessionLifecycleEventKind.Removed, session, session.State, nowTicks);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PublishConnectionSnapshot_NoLock()
        {
            Dictionary<int, long> snapshot = _sessionIdByConnection.Count == 0
                ? EmptyConnectionSnapshot
                : new Dictionary<int, long>(_sessionIdByConnection);
            Volatile.Write(ref _sessionIdByConnectionSnapshot, snapshot);
        }

        private void EnqueueLifecycleEvent_NoLock(
            SessionLifecycleEventKind kind,
            SessionEntry session,
            SessionState previousState,
            long nowTicks)
        {
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                kind: kind,
                sessionId: session.SessionId,
                connectionId: session.ConnectionId,
                previousState: previousState,
                currentState: session.State,
                disconnectReason: session.DisconnectReason,
                opaqueGameIdentityKey: session.OpaqueGameIdentityKey,
                timestampTicks: nowTicks));
        }

        private void RecordRejectedAdmission_NoLock(
            int connectionId,
            SessionDisconnectReason reason,
            long nowTicks)
        {
            _stats.RecordRejectedCreate();
            _stats.RecordDisconnectReason(reason);
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                kind: SessionLifecycleEventKind.Rejected,
                sessionId: 0,
                connectionId: connectionId,
                previousState: SessionState.Disconnected,
                currentState: SessionState.Disconnected,
                disconnectReason: reason,
                opaqueGameIdentityKey: 0,
                timestampTicks: nowTicks));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDuplicateConnection(int connectionId) =>
            throw new InvalidOperationException($"Connection {connectionId} already has an active session.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowSessionCapacityExceeded(int connectionId, int maxActiveSessions) =>
            throw new InvalidOperationException(
                $"Connection {connectionId} rejected: session cap reached (maxActiveSessions={maxActiveSessions}).");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowSessionIdExhausted() =>
            throw new InvalidOperationException("Session id space exhausted.");
    }
}
