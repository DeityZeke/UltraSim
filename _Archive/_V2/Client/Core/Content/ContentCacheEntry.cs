namespace UltraSim.Client.Content;

public sealed record ContentCacheEntry(
    string CacheKey,
    string RelativePath,
    ContentCacheEntryKind Kind,
    long SizeBytes,
    DateTimeOffset LastAccessUtc,
    bool Pinned = false);

public enum ContentCacheEntryKind
{
    Asset = 0,
    Schema = 1,
    PatchManifest = 2,
    StaticChunk = 3
}
