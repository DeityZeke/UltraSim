namespace UltraSim.Client.Content;

public sealed class ContentCacheEvictionExecutor
{
    private readonly ClientContentCachePathPolicy _pathPolicy;
    private readonly ContentCacheIndex _index;

    public ContentCacheEvictionExecutor(ClientContentCachePathPolicy pathPolicy, ContentCacheIndex index)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public IReadOnlyList<ContentCacheEvictionResult> Execute(ContentCacheQuotaPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var results = new List<ContentCacheEvictionResult>();
        foreach (ContentCacheEntry candidate in plan.EvictionCandidates)
        {
            results.Add(Evict(candidate));
        }

        return results;
    }

    private ContentCacheEvictionResult Evict(ContentCacheEntry entry)
    {
        if (entry.Pinned)
        {
            return new ContentCacheEvictionResult(entry, DeletedFile: false, RemovedMetadata: false, "Pinned cache entry was not evicted.");
        }

        if (!TryResolveCachedPath(entry.RelativePath, out string fullPath, out string error))
        {
            return new ContentCacheEvictionResult(entry, DeletedFile: false, RemovedMetadata: false, error);
        }

        bool deletedFile = false;
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                deletedFile = true;
            }
        }
        catch (IOException ex)
        {
            return new ContentCacheEvictionResult(entry, DeletedFile: false, RemovedMetadata: false, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ContentCacheEvictionResult(entry, DeletedFile: false, RemovedMetadata: false, ex.Message);
        }

        bool removedMetadata = _index.Remove(entry.CacheKey);
        return new ContentCacheEvictionResult(entry, deletedFile, removedMetadata, null);
    }

    private bool TryResolveCachedPath(string relativePath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        if (!ContentPathValidation.TryNormalizeRelativePath(relativePath, out string normalizedPath, out error))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(_pathPolicy.RootPath, normalizedPath));
        string root = _pathPolicy.RootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _pathPolicy.RootPath
            : _pathPolicy.RootPath + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            error = "Cache entry path escapes the cache root.";
            return false;
        }

        fullPath = candidate;
        error = string.Empty;
        return true;
    }
}

public sealed record ContentCacheEvictionResult(
    ContentCacheEntry Entry,
    bool DeletedFile,
    bool RemovedMetadata,
    string? Error);
