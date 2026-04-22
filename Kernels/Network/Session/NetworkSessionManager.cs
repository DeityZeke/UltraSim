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
    private uint _nextSessionId = 1;

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
        lock (_sync)
        {
            if (!connectionId.IsValid)
                throw new ArgumentOutOfRangeException(nameof(connectionId));

            if (_sessionIdByConnection.TryGetValue(connectionId, out SessionId existing))
            {
                if (_sessionsById.TryGetValue(existing, out SessionEntry? existingSession) &&
                    existingSession.State != ConnectionSessionState.Disconnected)
                {
                    throw new InvalidOperationException($"Connection {connectionId} already has an active session.");
                }

                _sessionIdByConnection.Remove(connectionId);
                _sessionsById.Remove(existing);
            }

            if (_nextSessionId == 0)
                throw new InvalidOperationException("Session id space exhausted.");

            SessionId sessionId = new(_nextSessionId++);
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
            return sessionId;
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
}
