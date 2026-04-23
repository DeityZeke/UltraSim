namespace UltraSim.Network;

public sealed class LatestStateStream
{
    private readonly record struct EntryKey(ulong ItemKey, SubscriptionKey ScopeKey);

    private sealed class Entry
    {
        public ulong ItemKey;
        public ulong Sequence;
        public SubscriptionKey ScopeKey;
        public PacketClass PacketClass;
        public ImmutablePayloadBuffer Payload = default!;
    }

    private readonly object _sync = new();
    private readonly Dictionary<EntryKey, Entry> _entriesByKey = new();
    private ulong _nextSequence = 1;

    public ulong Publish(ulong itemKey, ImmutablePayloadBuffer payload)
    {
        return Publish(itemKey, default, PacketClass.Realtime, payload);
    }

    public ulong Publish(ulong itemKey, SubscriptionKey scopeKey, ImmutablePayloadBuffer payload)
    {
        return Publish(itemKey, scopeKey, PacketClass.Realtime, payload);
    }

    public ulong Publish(ulong itemKey, SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload)
    {
        if (itemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(itemKey));
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Latest-state stream only supports Realtime or Bulk packet classes.");

        lock (_sync)
        {
            ulong sequence = _nextSequence++;
            PublishCore(itemKey, scopeKey, packetClass, payload, sequence);
            return sequence;
        }
    }

    public ulong PublishScopedCopies(ulong itemKey, ReadOnlySpan<SubscriptionKey> scopeKeys, PacketClass packetClass, ImmutablePayloadBuffer payload)
    {
        if (itemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(itemKey));
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Latest-state stream only supports Realtime or Bulk packet classes.");
        if (scopeKeys.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(scopeKeys));

        lock (_sync)
        {
            ulong sequence = _nextSequence++;
            for (int i = 0; i < scopeKeys.Length; i++)
            {
                SubscriptionKey scopeKey = scopeKeys[i];
                if (!scopeKey.IsValid)
                    throw new ArgumentOutOfRangeException(nameof(scopeKeys));

                PublishCore(itemKey, scopeKey, packetClass, payload, sequence);
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
                destination[i] = new LatestStateUpdate(entry.ItemKey, entry.Sequence, entry.ScopeKey, entry.PacketClass, entry.Payload);
                lastSequence = entry.Sequence;
            }

            return new LatestStateDrainResult(
                Count: count,
                NextCursor: new LatestStateCursor(lastSequence),
                HighestPublishedSequence: _nextSequence - 1);
        }
    }

    private void PublishCore(ulong itemKey, SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload, ulong sequence)
    {
        EntryKey key = new(itemKey, scopeKey);
        if (_entriesByKey.TryGetValue(key, out Entry? entry))
        {
            entry.Sequence = sequence;
            entry.ScopeKey = scopeKey;
            entry.PacketClass = packetClass;
            entry.Payload = payload;
        }
        else
        {
            _entriesByKey.Add(key, new Entry
            {
                ItemKey = itemKey,
                Sequence = sequence,
                ScopeKey = scopeKey,
                PacketClass = packetClass,
                Payload = payload,
            });
        }
    }
}
