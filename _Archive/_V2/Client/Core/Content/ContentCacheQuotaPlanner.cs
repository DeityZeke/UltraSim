namespace UltraSim.Client.Content;

public static class ContentCacheQuotaPlanner
{
    public static ContentCacheQuotaPlan PlanEviction(
        IEnumerable<ContentCacheEntry> entries,
        long quotaBytes,
        double targetFillRatio = 0.90)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (quotaBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quotaBytes), "Quota cannot be negative.");
        }

        targetFillRatio = Math.Clamp(targetFillRatio, 0.0, 1.0);
        var entryList = entries.ToArray();
        long currentSize = 0;
        for (int i = 0; i < entryList.Length; i++)
        {
            if (entryList[i].SizeBytes > 0)
            {
                currentSize += entryList[i].SizeBytes;
            }
        }

        long targetSize = (long)Math.Floor(quotaBytes * targetFillRatio);
        if (currentSize <= quotaBytes)
        {
            return new ContentCacheQuotaPlan(currentSize, quotaBytes, targetSize, Array.Empty<ContentCacheEntry>());
        }

        long bytesToFree = Math.Max(0, currentSize - targetSize);
        var candidates = new List<ContentCacheEntry>();
        long plannedBytes = 0;
        foreach (ContentCacheEntry entry in entryList
            .Where(static entry => !entry.Pinned)
            .OrderBy(static entry => entry.LastAccessUtc)
            .ThenByDescending(static entry => entry.SizeBytes)
            .ThenBy(static entry => entry.CacheKey, StringComparer.Ordinal))
        {
            candidates.Add(entry);
            if (entry.SizeBytes > 0)
            {
                plannedBytes += entry.SizeBytes;
            }

            if (plannedBytes >= bytesToFree)
            {
                break;
            }
        }

        return new ContentCacheQuotaPlan(currentSize, quotaBytes, targetSize, candidates);
    }
}
