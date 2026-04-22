namespace UltraSim.Network;

public sealed class OrderedEventStream
{
    private readonly object _sync = new();
    private readonly OrderedEventEntry[] _ring;
    private int _count;
    private int _writeIndex;
    private ulong _nextSequence = 1;

    public OrderedEventStream(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _ring = new OrderedEventEntry[capacity];
    }

    public ulong Publish(ImmutablePayloadBuffer payload)
    {
        return Publish(default, PacketClass.ReliableEvent, payload);
    }

    public ulong Publish(SubscriptionKey scopeKey, ImmutablePayloadBuffer payload)
    {
        return Publish(scopeKey, PacketClass.ReliableEvent, payload);
    }

    public ulong Publish(SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload)
    {
        if (packetClass is not PacketClass.ReliableEvent and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Ordered-event stream only supports ReliableEvent or Bulk packet classes.");

        lock (_sync)
        {
            ulong sequence = _nextSequence++;
            _ring[_writeIndex] = new OrderedEventEntry(sequence, scopeKey, packetClass, payload);
            _writeIndex = (_writeIndex + 1) % _ring.Length;
            if (_count < _ring.Length)
                _count++;

            return sequence;
        }
    }

    public OrderedEventSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            GetBounds(out ulong oldest, out ulong newest);
            return new OrderedEventSnapshot(
                Count: _count,
                Capacity: _ring.Length,
                OldestAvailableSequence: oldest,
                NewestAvailableSequence: newest);
        }
    }

    public OrderedEventDrainResult DrainSince(OrderedEventCursor cursor, Span<OrderedEventEntry> destination)
    {
        lock (_sync)
        {
            GetBounds(out ulong oldest, out ulong newest);
            if (_count == 0 || destination.Length == 0)
            {
                return new OrderedEventDrainResult(
                    OrderedEventDrainStatus.Success,
                    0,
                    cursor.NextSequence == 0 ? new OrderedEventCursor(_nextSequence) : cursor,
                    oldest,
                    newest);
            }

            ulong requested = cursor.NextSequence == 0 ? oldest : cursor.NextSequence;
            if (requested < oldest)
            {
                return new OrderedEventDrainResult(
                    OrderedEventDrainStatus.RecoveryRequired,
                    0,
                    new OrderedEventCursor(oldest),
                    oldest,
                    newest);
            }

            if (requested > newest)
            {
                return new OrderedEventDrainResult(
                    OrderedEventDrainStatus.Success,
                    0,
                    new OrderedEventCursor(requested),
                    oldest,
                    newest);
            }

            int count = 0;
            ulong sequence = requested;
            int oldestIndex = (_writeIndex - _count + _ring.Length) % _ring.Length;
            while (count < destination.Length && sequence <= newest)
            {
                int offset = checked((int)(sequence - oldest));
                int slot = (oldestIndex + offset) % _ring.Length;
                destination[count++] = _ring[slot];
                sequence++;
            }

            return new OrderedEventDrainResult(
                OrderedEventDrainStatus.Success,
                count,
                new OrderedEventCursor(sequence),
                oldest,
                newest);
        }
    }

    private void GetBounds(out ulong oldest, out ulong newest)
    {
        newest = _nextSequence - 1;
        oldest = _count == 0 ? _nextSequence : _nextSequence - (ulong)_count;
    }
}
