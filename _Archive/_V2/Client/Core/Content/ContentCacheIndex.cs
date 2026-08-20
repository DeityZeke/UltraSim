namespace UltraSim.Client.Content;

public sealed class ContentCacheIndex
{
    private readonly Dictionary<string, ContentCacheEntry> _entries = new(StringComparer.Ordinal);

    public ContentCacheIndex()
    {
    }

    public ContentCacheIndex(IEnumerable<ContentCacheEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (ContentCacheEntry entry in entries)
        {
            if (!TryUpsert(entry, out string error))
            {
                throw new ArgumentException(error, nameof(entries));
            }
        }
    }

    public IReadOnlyCollection<ContentCacheEntry> Entries => _entries.Values;
    public int Count => _entries.Count;
    public long TotalSizeBytes => _entries.Values.Sum(static entry => Math.Max(0, entry.SizeBytes));

    public bool TryUpsert(ContentCacheEntry entry, out string error)
    {
        if (!ValidateEntry(entry, out error))
        {
            return false;
        }

        _entries[entry.CacheKey] = entry;
        return true;
    }

    public bool TryGet(string cacheKey, out ContentCacheEntry entry)
    {
        return _entries.TryGetValue(cacheKey, out entry!);
    }

    public bool TryMarkAccessed(string cacheKey, DateTimeOffset accessedUtc)
    {
        if (!_entries.TryGetValue(cacheKey, out ContentCacheEntry? entry))
        {
            return false;
        }

        _entries[cacheKey] = entry with { LastAccessUtc = accessedUtc };
        return true;
    }

    public bool TrySetPinned(string cacheKey, bool pinned)
    {
        if (!_entries.TryGetValue(cacheKey, out ContentCacheEntry? entry))
        {
            return false;
        }

        _entries[cacheKey] = entry with { Pinned = pinned };
        return true;
    }

    public bool Remove(string cacheKey)
    {
        return _entries.Remove(cacheKey);
    }

    public ContentCacheQuotaPlan PlanEviction(long quotaBytes, double targetFillRatio = 0.90)
    {
        return ContentCacheQuotaPlanner.PlanEviction(_entries.Values, quotaBytes, targetFillRatio);
    }

    public IReadOnlyList<ContentCacheEntry> ApplyEvictionPlan(ContentCacheQuotaPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var removed = new List<ContentCacheEntry>();
        foreach (ContentCacheEntry candidate in plan.EvictionCandidates)
        {
            if (_entries.Remove(candidate.CacheKey, out ContentCacheEntry? entry))
            {
                removed.Add(entry);
            }
        }

        return removed;
    }

    public IReadOnlyList<ContentCacheEntry> SnapshotOrdered()
    {
        return _entries.Values
            .OrderBy(static entry => entry.CacheKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ValidateEntry(ContentCacheEntry entry, out string error)
    {
        if (string.IsNullOrWhiteSpace(entry.CacheKey))
        {
            error = "Cache key is required.";
            return false;
        }

        if (entry.CacheKey.Any(char.IsControl))
        {
            error = "Cache key cannot contain control characters.";
            return false;
        }

        if (!ContentPathValidation.TryNormalizeRelativePath(entry.RelativePath, out _, out error))
        {
            return false;
        }

        if (entry.SizeBytes < 0)
        {
            error = "Cache entry size cannot be negative.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
