namespace UltraSim.Client.Content;

public sealed class ServerAssetCacheStore
{
    private readonly ClientContentCachePathPolicy _pathPolicy;
    private readonly ContentCacheIndex _index;

    public ServerAssetCacheStore(ClientContentCachePathPolicy pathPolicy, ContentCacheIndex index)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public bool Has(ServerContentIdentity server, ServerPatchManifestEntry manifestEntry)
    {
        return TryGetPath(server, manifestEntry, out string path, out _) && File.Exists(path);
    }

    public bool TryWrite(
        ServerContentIdentity server,
        ServerPatchManifestEntry manifestEntry,
        ReadOnlySpan<byte> assetBytes,
        DateTimeOffset nowUtc,
        bool pinned,
        out string error)
    {
        if (!ValidateEntryAndBytes(manifestEntry, assetBytes, out error))
        {
            return false;
        }

        if (!TryGetPath(server, manifestEntry, out string path, out error))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, assetBytes.ToArray());
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);

        if (!ContentPathValidation.TryNormalizeRelativePath(manifestEntry.RelativePath, out string normalizedRelativePath, out error))
        {
            return false;
        }

        string indexedRelativePath = Path.GetRelativePath(_pathPolicy.RootPath, path).Replace('\\', '/');
        var entry = new ContentCacheEntry(
            CreateCacheKey(server, normalizedRelativePath, manifestEntry.Sha256Hex),
            indexedRelativePath,
            ContentCacheEntryKind.Asset,
            assetBytes.Length,
            nowUtc,
            pinned);
        return _index.TryUpsert(entry, out error);
    }

    public bool TryRead(
        ServerContentIdentity server,
        ServerPatchManifestEntry manifestEntry,
        DateTimeOffset nowUtc,
        out byte[] assetBytes,
        out string error)
    {
        assetBytes = Array.Empty<byte>();
        if (!TryGetPath(server, manifestEntry, out string path, out error))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            error = "Server asset is not cached.";
            return false;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (!ValidateEntryAndBytes(manifestEntry, bytes, out error))
        {
            return false;
        }

        assetBytes = bytes;
        if (ContentPathValidation.TryNormalizeRelativePath(manifestEntry.RelativePath, out string normalizedRelativePath, out _))
        {
            _index.TryMarkAccessed(CreateCacheKey(server, normalizedRelativePath, manifestEntry.Sha256Hex), nowUtc);
        }

        error = string.Empty;
        return true;
    }

    public bool Remove(ServerContentIdentity server, ServerPatchManifestEntry manifestEntry)
    {
        bool removedFile = false;
        if (TryGetPath(server, manifestEntry, out string path, out _) && File.Exists(path))
        {
            File.Delete(path);
            removedFile = true;
        }

        bool removedMetadata = false;
        if (ContentPathValidation.TryNormalizeRelativePath(manifestEntry.RelativePath, out string normalizedRelativePath, out _))
        {
            removedMetadata = _index.Remove(CreateCacheKey(server, normalizedRelativePath, manifestEntry.Sha256Hex));
        }

        return removedFile || removedMetadata;
    }

    public static string CreateCacheKey(ServerContentIdentity server, string normalizedRelativePath, string sha256Hex)
    {
        return string.Join(
            ':',
            "asset",
            server.CacheNamespace,
            normalizedRelativePath,
            sha256Hex.Trim().ToLowerInvariant());
    }

    private bool TryGetPath(
        ServerContentIdentity server,
        ServerPatchManifestEntry manifestEntry,
        out string path,
        out string error)
    {
        return _pathPolicy.TryGetServerAssetPath(server, manifestEntry.RelativePath, out path, out error);
    }

    private static bool ValidateEntryAndBytes(
        ServerPatchManifestEntry manifestEntry,
        ReadOnlySpan<byte> assetBytes,
        out string error)
    {
        if (!ContentPathValidation.TryNormalizeRelativePath(manifestEntry.RelativePath, out _, out error))
        {
            return false;
        }

        return ContentPathValidation.ValidateSizeAndSha256(
            assetBytes,
            manifestEntry.SizeBytes,
            manifestEntry.Sha256Hex,
            out error);
    }
}
