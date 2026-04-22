namespace UltraSim.Network;

public sealed class LatestStateStream
{
    private sealed class Entry
    {
        public ulong ItemKey;
        public ulong Sequence;
        public SubscriptionKey ScopeKey;
        public ImmutablePayloadBuffer Payload = default!;
    }

    private readonly object _sync = new();
    private readonly Dictionary<ulong, Entry> _entriesByKey = new();
    private ulong _nextSequence = 1;

    public ulong Publish(ulong itemKey, ImmutablePayloadBuffer payload)
    {
        return Publish(itemKey, default, payload);
    }

    public ulong Publish(ulong itemKey, SubscriptionKey scopeKey, ImmutablePayloadBuffer payload)
    {
        if (itemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(itemKey));

        lock (_sync)
        {
            ulong sequence = _nextSequence++;
            if (_entriesByKey.TryGetValue(itemKey, out Entry? entry))
            {
                entry.Sequence = sequence;
                entry.ScopeKey = scopeKey;
                entry.Payload = payload;
            }
            else
            {
                _entriesByKey.Add(itemKey, new Entry
                {
                    ItemKey = itemKey,
                    Sequence = sequence,
                    ScopeKey = scopeKey,
                    Payload = payload,
                });
            }

            return sequence;
        }
    }

    public LatestStateSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new LatestStateSnapshot(
                ItemCount: _entriesByKey.Count,
                HighestPublishedSequence: _nextSequence - 1);
        }
    }

    public LatestStateDrainResult DrainSince(LatestStateCursor cursor, Span<LatestStateUpdate> destination)
    {
        lock (_sync)
        {
            if (destination.Length == 0 || _entriesByKey.Count == 0)
                return new LatestStateDrainResult(0, cursor, _nextSequence - 1);

            var updates = new List<Entry>(_entriesByKey.Count);
            foreach (Entry entry in _entriesByKey.Values)
            {
                if (entry.Sequence > cursor.LastSeenSequence)
                    updates.Add(entry);
            }

            if (updates.Count == 0)
                return new LatestStateDrainResult(0, cursor, _nextSequence - 1);

            updates.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));

            int count = Math.Min(destination.Length, updates.Count);
            ulong lastSequence = cursor.LastSeenSequence;
            for (int i = 0; i < count; i++)
            {
                Entry entry = updates[i];
                destination[i] = new LatestStateUpdate(entry.ItemKey, entry.Sequence, entry.ScopeKey, entry.Payload);
                lastSequence = entry.Sequence;
            }

            return new LatestStateDrainResult(
                Count: count,
                NextCursor: new LatestStateCursor(lastSequence),
                HighestPublishedSequence: _nextSequence - 1);
        }
    }
}
