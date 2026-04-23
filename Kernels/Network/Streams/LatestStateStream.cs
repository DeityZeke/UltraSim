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
    private Entry[] _orderedSnapshot = Array.Empty<Entry>();
    private int _orderedSnapshotCount;
    private bool _orderedSnapshotDirty = true;
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

            EnsureOrderedSnapshot_NoLock();
            int start = FindFirstSequenceGreaterThan_NoLock(cursor.LastSeenSequence);
            if (start >= _orderedSnapshotCount)
                return new LatestStateDrainResult(0, cursor, _nextSequence - 1);

            int count = Math.Min(destination.Length, _orderedSnapshotCount - start);
            ulong lastSequence = cursor.LastSeenSequence;
            for (int i = 0; i < count; i++)
            {
                Entry entry = _orderedSnapshot[start + i];
                destination[i] = new LatestStateUpdate(entry.ItemKey, entry.Sequence, entry.ScopeKey, entry.PacketClass, entry.Payload);
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
                destination[i] = new LatestStateUpdate(entry.ItemKey, entry.Sequence, entry.ScopeKey, entry.PacketClass, entry.Payload);
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
                    entry.Payload);
            }

            return count;
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
