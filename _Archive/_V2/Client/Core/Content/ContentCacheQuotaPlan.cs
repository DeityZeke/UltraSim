namespace UltraSim.Client.Content;

public sealed record ContentCacheQuotaPlan(
    long CurrentSizeBytes,
    long QuotaBytes,
    long TargetSizeBytes,
    IReadOnlyList<ContentCacheEntry> EvictionCandidates)
{
    public long BytesToFree => Math.Max(0, CurrentSizeBytes - TargetSizeBytes);
}
