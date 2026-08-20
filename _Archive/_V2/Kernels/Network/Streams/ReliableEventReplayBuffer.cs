namespace UltraSim.Network;

/// <summary>
/// Bounded per-connection replay window for ordered-event payloads that have been sent
/// but not yet acknowledged. When a requested sequence falls behind the retained window,
/// the caller should fall back to full-baseline recovery.
/// </summary>
public sealed class ReliableEventReplayBuffer
{
    private sealed class ConnectionWindow
    {
        public readonly ReliableEventReplayEntry[] Entries;
        public int Start;
        public int Count;

        public ConnectionWindow(int capacity)
        {
            Entries = new ReliableEventReplayEntry[capacity];
        }
    }

    private readonly int _capacityPerConnection;
    private readonly Dictionary<ConnectionId, ConnectionWindow> _windowByConnection = new();

    public ReliableEventReplayBuffer(int capacityPerConnection)
    {
        if (capacityPerConnection <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityPerConnection));

        _capacityPerConnection = capacityPerConnection;
    }

    public int CapacityPerConnection => _capacityPerConnection;

    public void Track(ConnectionId connectionId, in ScheduledWorkItem item)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (item.Kind != ScheduledWorkKind.OrderedEvent)
            throw new ArgumentOutOfRangeException(nameof(item), "Replay buffer only tracks ordered-event work items.");

        Track(connectionId, sentTick: 0, item.Sequence, item.ScopeKey, item.PacketClass, item.Payload);
    }

    public void Track(ConnectionId connectionId, ulong sequence, SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload)
    {
        Track(connectionId, sentTick: 0, sequence, scopeKey, packetClass, payload);
    }

    public void Track(ConnectionId connectionId, long sentTick, ulong sequence, SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload)
    {
        if (!connectionId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        if (packetClass is not PacketClass.ReliableEvent and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass));

        ConnectionWindow window = GetOrCreateWindow(connectionId);
        if (window.Count == _capacityPerConnection)
        {
            window.Start = (window.Start + 1) % _capacityPerConnection;
        }
        else
        {
            window.Count++;
        }

        int slot = PhysicalIndex(window, window.Count - 1);
        window.Entries[slot] = new ReliableEventReplayEntry(sequence, sentTick, scopeKey, packetClass, payload);
    }

    public int AcknowledgeUpTo(ConnectionId connectionId, ulong sequence)
    {
        if (!_windowByConnection.TryGetValue(connectionId, out ConnectionWindow? window))
            return 0;

        int removed = 0;
        while (window.Count > 0)
        {
            ReliableEventReplayEntry head = window.Entries[window.Start];
            if (head.Sequence > sequence)
                break;

            window.Entries[window.Start] = default;
            window.Start = (window.Start + 1) % _capacityPerConnection;
            window.Count--;
            removed++;
        }

        if (window.Count == 0)
            window.Start = 0;

        return removed;
    }

    public int CopyPendingSince(ConnectionId connectionId, ulong nextSequence, Span<ReliableEventReplayEntry> destination, out bool recoveryRequired)
    {
        recoveryRequired = false;
        if (!_windowByConnection.TryGetValue(connectionId, out ConnectionWindow? window) || window.Count == 0 || destination.Length == 0)
            return 0;

        ReliableEventReplayEntry oldest = window.Entries[window.Start];
        if (nextSequence < oldest.Sequence)
        {
            recoveryRequired = true;
            return 0;
        }

        int written = 0;
        for (int i = 0; i < window.Count && written < destination.Length; i++)
        {
            ReliableEventReplayEntry entry = window.Entries[PhysicalIndex(window, i)];
            if (entry.Sequence < nextSequence)
                continue;

            destination[written++] = entry;
        }

        return written;
    }

    public bool TryGetOldestSequence(ConnectionId connectionId, out ulong sequence)
    {
        if (_windowByConnection.TryGetValue(connectionId, out ConnectionWindow? window) && window.Count > 0)
        {
            sequence = window.Entries[window.Start].Sequence;
            return true;
        }

        sequence = 0;
        return false;
    }

    public bool TryGetOldestEntry(ConnectionId connectionId, out ReliableEventReplayEntry entry)
    {
        if (_windowByConnection.TryGetValue(connectionId, out ConnectionWindow? window) && window.Count > 0)
        {
            entry = window.Entries[window.Start];
            return true;
        }

        entry = default;
        return false;
    }

    public bool TryGetNewestSequence(ConnectionId connectionId, out ulong sequence)
    {
        if (_windowByConnection.TryGetValue(connectionId, out ConnectionWindow? window) && window.Count > 0)
        {
            sequence = window.Entries[PhysicalIndex(window, window.Count - 1)].Sequence;
            return true;
        }

        sequence = 0;
        return false;
    }

    private ConnectionWindow GetOrCreateWindow(ConnectionId connectionId)
    {
        if (_windowByConnection.TryGetValue(connectionId, out ConnectionWindow? window))
            return window;

        window = new ConnectionWindow(_capacityPerConnection);
        _windowByConnection.Add(connectionId, window);
        return window;
    }

    private int PhysicalIndex(ConnectionWindow window, int logicalIndex)
    {
        return (window.Start + logicalIndex) % _capacityPerConnection;
    }
}
