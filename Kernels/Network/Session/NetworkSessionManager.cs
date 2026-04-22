namespace UltraSim.Network;

public sealed class NetworkSessionManager
{
    private sealed class SessionEntry
    {
        public SessionId SessionId;
        public ConnectionId ConnectionId;
        public ConnectionSessionState State;
        public SessionDisconnectReason DisconnectReason;
        public long CreatedTicks;
        public long LastActivityTicks;
    }

    private readonly object _sync = new();
    private readonly Dictionary<SessionId, SessionEntry> _sessionsById = new();
    private readonly Dictionary<ConnectionId, SessionId> _sessionIdByConnection = new();
    private readonly Queue<SessionLifecycleEvent> _lifecycleEvents = new();
    private readonly SessionAdmissionPolicy _policy;
    private uint _nextSessionId = 1;

    public NetworkSessionManager(SessionAdmissionPolicy policy = default)
    {
        if (policy.MaxActiveSessions < 0)
            throw new ArgumentOutOfRangeException(nameof(policy));

        _policy = policy.Equals(default(SessionAdmissionPolicy))
            ? SessionAdmissionPolicy.Default
            : policy;
    }

    public int Count
    {
        get
        {
            lock (_sync)
                return _sessionsById.Count;
        }
    }

    public SessionId CreateSession(ConnectionId connectionId, long nowTicks)
    {
        if (TryCreateSession(connectionId, nowTicks, out SessionId sessionId, out SessionDisconnectReason rejectReason))
            return sessionId;

        throw new InvalidOperationException($"Session creation rejected for connection {connectionId}: {rejectReason}");
    }

    public bool TryCreateSession(
        ConnectionId connectionId,
        long nowTicks,
        out SessionId sessionId,
        out SessionDisconnectReason rejectReason)
    {
        lock (_sync)
        {
            if (!connectionId.IsValid)
                throw new ArgumentOutOfRangeException(nameof(connectionId));

            sessionId = SessionId.Invalid;
            rejectReason = SessionDisconnectReason.None;

            if (_sessionIdByConnection.TryGetValue(connectionId, out SessionId existing))
            {
                if (_sessionsById.TryGetValue(existing, out SessionEntry? existingSession) &&
                    existingSession.State != ConnectionSessionState.Disconnected)
                {
                    if (_policy.DuplicateSessionPolicy == DuplicateSessionPolicy.RejectNew)
                    {
                        rejectReason = SessionDisconnectReason.DuplicateSession;
                        _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                            SessionLifecycleEventKind.Rejected,
                            SessionId.Invalid,
                            connectionId,
                            ConnectionSessionState.Disconnected,
                            ConnectionSessionState.Disconnected,
                            rejectReason,
                            nowTicks));
                        return false;
                    }

                    ForceDisconnect_NoLock(existingSession, SessionDisconnectReason.DuplicateSession, nowTicks, removeDisconnected: true);
                }
            }

            if (_policy.MaxActiveSessions > 0 && _sessionsById.Count >= _policy.MaxActiveSessions)
            {
                rejectReason = SessionDisconnectReason.ServerBusy;
                _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                    SessionLifecycleEventKind.Rejected,
                    SessionId.Invalid,
                    connectionId,
                    ConnectionSessionState.Disconnected,
                    ConnectionSessionState.Disconnected,
                    rejectReason,
                    nowTicks));
                return false;
            }

            if (_nextSessionId == 0)
                throw new InvalidOperationException("Session id space exhausted.");

            sessionId = new SessionId(_nextSessionId++);
            var entry = new SessionEntry
            {
                SessionId = sessionId,
                ConnectionId = connectionId,
                State = ConnectionSessionState.Connecting,
                DisconnectReason = SessionDisconnectReason.None,
                CreatedTicks = nowTicks,
                LastActivityTicks = nowTicks,
            };

            _sessionsById.Add(sessionId, entry);
            _sessionIdByConnection.Add(connectionId, sessionId);
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.Created,
                sessionId,
                connectionId,
                ConnectionSessionState.Disconnected,
                ConnectionSessionState.Connecting,
                SessionDisconnectReason.None,
                nowTicks));
            return true;
        }
    }

    public bool TryGetSessionIdByConnection(ConnectionId connectionId, out SessionId sessionId)
    {
        lock (_sync)
            return _sessionIdByConnection.TryGetValue(connectionId, out sessionId);
    }

    public bool TryGetSnapshot(SessionId sessionId, out SessionSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
            {
                snapshot = new SessionSnapshot(
                    entry.SessionId,
                    entry.ConnectionId,
                    entry.State,
                    entry.DisconnectReason,
                    entry.CreatedTicks,
                    entry.LastActivityTicks);
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

    public bool TryMarkHandshaking(SessionId sessionId, long nowTicks) =>
        TryTransition(sessionId, ConnectionSessionState.Connecting, ConnectionSessionState.Handshaking, nowTicks);

    public bool TryMarkAuthenticated(SessionId sessionId, long nowTicks) =>
        TryTransition(sessionId, ConnectionSessionState.Handshaking, ConnectionSessionState.Authenticated, nowTicks);

    public bool TryMarkReady(SessionId sessionId, long nowTicks) =>
        TryTransition(sessionId, ConnectionSessionState.Authenticated, ConnectionSessionState.Ready, nowTicks);

    public bool TryBeginDisconnect(SessionId sessionId, SessionDisconnectReason reason, long nowTicks)
    {
        lock (_sync)
        {
            if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
                return false;

            if (entry.State is ConnectionSessionState.Disconnecting or ConnectionSessionState.Disconnected)
                return false;

            ConnectionSessionState previous = entry.State;
            entry.State = ConnectionSessionState.Disconnecting;
            entry.DisconnectReason = reason;
            entry.LastActivityTicks = nowTicks;
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.StateChanged,
                sessionId,
                entry.ConnectionId,
                previous,
                entry.State,
                reason,
                nowTicks));
            return true;
        }
    }

    public bool TryCompleteDisconnect(SessionId sessionId, long nowTicks)
    {
        lock (_sync)
        {
            if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
                return false;

            if (entry.State != ConnectionSessionState.Disconnecting)
                return false;

            ConnectionSessionState previous = entry.State;
            entry.State = ConnectionSessionState.Disconnected;
            entry.LastActivityTicks = nowTicks;
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.StateChanged,
                sessionId,
                entry.ConnectionId,
                previous,
                entry.State,
                entry.DisconnectReason,
                nowTicks));
            return true;
        }
    }

    public bool TryRemoveDisconnected(SessionId sessionId, long nowTicks)
    {
        lock (_sync)
        {
            if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
                return false;

            if (entry.State != ConnectionSessionState.Disconnected)
                return false;

            _sessionsById.Remove(sessionId);
            _sessionIdByConnection.Remove(entry.ConnectionId);
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.Removed,
                sessionId,
                entry.ConnectionId,
                ConnectionSessionState.Disconnected,
                ConnectionSessionState.Disconnected,
                entry.DisconnectReason,
                nowTicks));
            return true;
        }
    }

    public bool TryTouchActivity(SessionId sessionId, long nowTicks)
    {
        lock (_sync)
        {
            if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
                return false;

            entry.LastActivityTicks = nowTicks;
            return true;
        }
    }

    public bool TryDisconnectConnection(
        ConnectionId connectionId,
        SessionDisconnectReason reason,
        long nowTicks,
        bool removeDisconnected = true)
    {
        lock (_sync)
        {
            if (!_sessionIdByConnection.TryGetValue(connectionId, out SessionId sessionId))
                return false;
            if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
                return false;

            ForceDisconnect_NoLock(entry, reason, nowTicks, removeDisconnected);
            return true;
        }
    }

    public int SweepTimeouts(long nowTicks)
    {
        if (_policy.AuthTimeoutTicks <= 0 && _policy.KeepAliveTimeoutTicks <= 0)
            return 0;

        lock (_sync)
        {
            if (_sessionsById.Count == 0)
                return 0;

            SessionEntry[] snapshot = new SessionEntry[_sessionsById.Count];
            _sessionsById.Values.CopyTo(snapshot, 0);
            int count = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                SessionEntry entry = snapshot[i];
                if (!_sessionsById.ContainsKey(entry.SessionId))
                    continue;

                if (!ShouldTimeout(entry, nowTicks))
                    continue;

                ForceDisconnect_NoLock(entry, SessionDisconnectReason.Timeout, nowTicks, removeDisconnected: false);
                count++;
            }

            return count;
        }
    }

    private bool TryTransition(
        SessionId sessionId,
        ConnectionSessionState expected,
        ConnectionSessionState next,
        long nowTicks)
    {
        lock (_sync)
        {
            if (!_sessionsById.TryGetValue(sessionId, out SessionEntry? entry))
                return false;

            if (entry.State != expected)
                return false;

            ConnectionSessionState previous = entry.State;
            entry.State = next;
            entry.LastActivityTicks = nowTicks;
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.StateChanged,
                sessionId,
                entry.ConnectionId,
                previous,
                next,
                entry.DisconnectReason,
                nowTicks));
            return true;
        }
    }

    private bool ShouldTimeout(SessionEntry entry, long nowTicks)
    {
        long elapsed = nowTicks - entry.LastActivityTicks;
        if (elapsed < 0)
            return false;

        return entry.State switch
        {
            ConnectionSessionState.Connecting or
            ConnectionSessionState.Handshaking or
            ConnectionSessionState.Authenticated =>
                _policy.AuthTimeoutTicks > 0 && elapsed >= _policy.AuthTimeoutTicks,
            ConnectionSessionState.Ready or
            ConnectionSessionState.Disconnecting =>
                _policy.KeepAliveTimeoutTicks > 0 && elapsed >= _policy.KeepAliveTimeoutTicks,
            ConnectionSessionState.Disconnected => false,
            _ => false,
        };
    }

    private void ForceDisconnect_NoLock(
        SessionEntry entry,
        SessionDisconnectReason reason,
        long nowTicks,
        bool removeDisconnected)
    {
        if (entry.State != ConnectionSessionState.Disconnecting &&
            entry.State != ConnectionSessionState.Disconnected)
        {
            ConnectionSessionState previous = entry.State;
            entry.State = ConnectionSessionState.Disconnecting;
            entry.DisconnectReason = reason;
            entry.LastActivityTicks = nowTicks;
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.StateChanged,
                entry.SessionId,
                entry.ConnectionId,
                previous,
                entry.State,
                reason,
                nowTicks));
        }

        if (entry.State != ConnectionSessionState.Disconnected)
        {
            ConnectionSessionState previous = entry.State;
            entry.State = ConnectionSessionState.Disconnected;
            entry.LastActivityTicks = nowTicks;
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.StateChanged,
                entry.SessionId,
                entry.ConnectionId,
                previous,
                entry.State,
                entry.DisconnectReason,
                nowTicks));
        }

        if (removeDisconnected)
        {
            _sessionsById.Remove(entry.SessionId);
            _sessionIdByConnection.Remove(entry.ConnectionId);
            _lifecycleEvents.Enqueue(new SessionLifecycleEvent(
                SessionLifecycleEventKind.Removed,
                entry.SessionId,
                entry.ConnectionId,
                ConnectionSessionState.Disconnected,
                ConnectionSessionState.Disconnected,
                entry.DisconnectReason,
                nowTicks));
        }
    }
}
