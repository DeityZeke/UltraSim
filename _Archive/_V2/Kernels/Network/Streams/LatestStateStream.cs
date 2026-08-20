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
        public uint DeliveryCadenceTicks;
        public uint PublishTick;
        public ImmutablePayloadBuffer Payload = default!;
    }

    private readonly object _sync = new();
    private readonly Dictionary<EntryKey, Entry> _entriesByKey = new();
    private Entry[] _orderedSnapshot = Array.Empty<Entry>();
    private int _orderedSnapshotCount;
    private bool _orderedSnapshotDirty = true;
    private ulong _nextSequence = 1;

    public ulong Publish(ulong itemKey, ImmutablePayloadBuffer payload, uint deliveryCadenceTicks = 1)
    {
        return Publish(itemKey, default, PacketClass.Realtime, payload, deliveryCadenceTicks);
    }

    public ulong Publish(ulong itemKey, SubscriptionKey scopeKey, ImmutablePayloadBuffer payload, uint deliveryCadenceTicks = 1)
    {
        return Publish(itemKey, scopeKey, PacketClass.Realtime, payload, deliveryCadenceTicks);
    }

    public ulong Publish(ulong itemKey, SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload, uint deliveryCadenceTicks = 1)
    {
        if (itemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(itemKey));
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Latest-state stream only supports Realtime or Bulk packet classes.");
        if (deliveryCadenceTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryCadenceTicks));

        lock (_sync)
        {
            ulong sequence = _nextSequence++;
            PublishCore(itemKey, scopeKey, packetClass, payload, sequence, deliveryCadenceTicks, 0);
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

                PublishCore(itemKey, scopeKey, packetClass, payload, sequence, 1, 0);
            }

            return sequence;
        }
    }

    public ulong PublishScopedCopies(ulong itemKey, ReadOnlySpan<ScopedLatestStatePublishTarget> targets, PacketClass packetClass, ImmutablePayloadBuffer payload)
    {
        if (itemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(itemKey));
        if (packetClass is not PacketClass.Realtime and not PacketClass.Bulk)
            throw new ArgumentOutOfRangeException(nameof(packetClass), "Latest-state stream only supports Realtime or Bulk packet classes.");
        if (targets.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(targets));

        lock (_sync)
        {
            ulong sequence = _nextSequence++;
            for (int i = 0; i < targets.Length; i++)
            {
                ScopedLatestStatePublishTarget target = targets[i];
                if (!target.ScopeKey.IsValid)
                    throw new ArgumentOutOfRangeException(nameof(targets));
                if (target.DeliveryCadenceTicks == 0)
                    throw new ArgumentOutOfRangeException(nameof(targets));

                PublishCore(itemKey, target.ScopeKey, packetClass, payload, sequence, target.DeliveryCadenceTicks, target.PublishTick);
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

    public bool Remove(ulong itemKey, SubscriptionKey scopeKey = default)
    {
        if (itemKey == 0)
            throw new ArgumentOutOfRangeException(nameof(itemKey));

        lock (_sync)
        {
            bool removed = _entriesByKey.Remove(new EntryKey(itemKey, scopeKey));
            if (removed)
                _orderedSnapshotDirty = true;

            return removed;
        }
    }

    public LatestStateDrainResult DrainSince(LatestStateCursor cursor, Span<LatestStateUpdate> destination)
    {
        lock (_sync)
        {
            if (destination.Length == 0 || _entriesByKey.Count == 0)
                return new LatestStateDrainResult(0, cursor, _nextSequence - 1);

            EnsureOrderedSnapshot_NoLock();
            int start = FindFirstSequenceGreaterThan_NoLock(cursor.LastSeenSequence);
            if (start >= _orderedSnapshotCount)
                return new LatestStateDrainResult(0, cursor, _nextSequence - 1);

            int count = Math.Min(destination.Length, _orderedSnapshotCount - start);
            ulong lastSequence = cursor.LastSeenSequence;
            for (int i = 0; i < count; i++)
            {
                Entry entry = _orderedSnapshot[start + i];
                destination[i] = new LatestStateUpdate(entry.ItemKey, entry.Sequence, entry.ScopeKey, entry.PacketClass, entry.Payload, entry.DeliveryCadenceTicks, entry.PublishTick);
                lastSequence = entry.Sequence;
            }

            return new LatestStateDrainResult(
                Count: count,
                NextCursor: new LatestStateCursor(lastSequence),
                HighestPublishedSequence: _nextSequence - 1);
        }
    }

    public int CopySnapshot(Span<LatestStateUpdate> destination)
    {
        lock (_sync)
        {
            if (destination.Length == 0 || _entriesByKey.Count == 0)
                return 0;

            EnsureOrderedSnapshot_NoLock();
            int count = Math.Min(destination.Length, _orderedSnapshotCount);
            for (int i = 0; i < count; i++)
            {
                Entry entry = _orderedSnapshot[i];
                destination[i] = new LatestStateUpdate(entry.ItemKey, entry.Sequence, entry.ScopeKey, entry.PacketClass, entry.Payload, entry.DeliveryCadenceTicks, entry.PublishTick);
            }

            return count;
        }
    }

    public int CopySnapshotForScopes(ReadOnlySpan<SubscriptionKey> scopeKeys, Span<LatestStateUpdate> destination)
    {
        if (scopeKeys.Length == 0 || destination.Length == 0)
            return 0;

        lock (_sync)
        {
            if (_entriesByKey.Count == 0)
                return 0;

            EnsureOrderedSnapshot_NoLock();
            int count = 0;
            for (int i = 0; i < _orderedSnapshotCount && count < destination.Length; i++)
            {
                Entry entry = _orderedSnapshot[i];
                if (!Contains(scopeKeys, entry.ScopeKey))
                    continue;

                destination[count++] = new LatestStateUpdate(
                    entry.ItemKey,
                    entry.Sequence,
                    entry.ScopeKey,
                    entry.PacketClass,
                    entry.Payload,
                    entry.DeliveryCadenceTicks,
                    entry.PublishTick);
            }

            return count;
        }
    }

    private void PublishCore(ulong itemKey, SubscriptionKey scopeKey, PacketClass packetClass, ImmutablePayloadBuffer payload, ulong sequence, uint deliveryCadenceTicks, uint publishTick)
    {
        EntryKey key = new(itemKey, scopeKey);
        if (_entriesByKey.TryGetValue(key, out Entry? entry))
        {
            entry.Sequence = sequence;
            entry.ScopeKey = scopeKey;
            entry.PacketClass = packetClass;
            entry.DeliveryCadenceTicks = deliveryCadenceTicks;
            entry.PublishTick = publishTick;
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
                DeliveryCadenceTicks = deliveryCadenceTicks,
                PublishTick = publishTick,
                Payload = payload,
            });
        }

        _orderedSnapshotDirty = true;
    }

    private void EnsureOrderedSnapshot_NoLock()
    {
        if (!_orderedSnapshotDirty)
            return;

        int required = _entriesByKey.Count;
        if (_orderedSnapshot.Length < required)
            _orderedSnapshot = new Entry[required];

        int index = 0;
        foreach (Entry entry in _entriesByKey.Values)
            _orderedSnapshot[index++] = entry;

        Array.Sort(_orderedSnapshot, 0, index, EntrySequenceComparer.Instance);
        _orderedSnapshotCount = index;
        _orderedSnapshotDirty = false;
    }

    private int FindFirstSequenceGreaterThan_NoLock(ulong sequence)
    {
        int low = 0;
        int high = _orderedSnapshotCount;
        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (_orderedSnapshot[mid].Sequence <= sequence)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private static bool Contains(ReadOnlySpan<SubscriptionKey> keys, SubscriptionKey key)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] == key)
                return true;
        }

        return false;
    }

    private sealed class EntrySequenceComparer : IComparer<Entry>
    {
        public static readonly EntrySequenceComparer Instance = new();

        public int Compare(Entry? x, Entry? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            return x.Sequence.CompareTo(y.Sequence);
        }
    }
}
