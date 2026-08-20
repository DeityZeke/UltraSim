namespace UltraSim.Client.Content;

public sealed class UiSchemaCacheStore
{
    private readonly ClientContentCachePathPolicy _pathPolicy;
    private readonly ContentCacheIndex _index;

    public UiSchemaCacheStore(ClientContentCachePathPolicy pathPolicy, ContentCacheIndex index)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public bool Has(UiSchemaCacheKey key)
    {
        return File.Exists(_pathPolicy.GetSchemaPath(key));
    }

    public bool TryWrite(
        UiSchemaCacheKey key,
        ReadOnlySpan<byte> schemaBytes,
        DateTimeOffset nowUtc,
        bool pinned,
        out string error)
    {
        if (!ContentPathValidation.ValidateSha256(schemaBytes, key.SchemaHashHex, out error))
        {
            return false;
        }

        string path = _pathPolicy.GetSchemaPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, schemaBytes.ToArray());
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);

        string relativePath = Path.GetRelativePath(_pathPolicy.RootPath, path).Replace('\\', '/');
        var entry = new ContentCacheEntry(
            CreateCacheKey(key),
            relativePath,
            ContentCacheEntryKind.Schema,
            schemaBytes.Length,
            nowUtc,
            pinned);
        return _index.TryUpsert(entry, out error);
    }

    public bool TryRead(UiSchemaCacheKey key, DateTimeOffset nowUtc, out byte[] schemaBytes, out string error)
    {
        schemaBytes = Array.Empty<byte>();
        string path = _pathPolicy.GetSchemaPath(key);
        if (!File.Exists(path))
        {
            error = "UI schema is not cached.";
            return false;
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (!ContentPathValidation.ValidateSha256(bytes, key.SchemaHashHex, out error))
        {
            return false;
        }

        schemaBytes = bytes;
        _index.TryMarkAccessed(CreateCacheKey(key), nowUtc);
        error = string.Empty;
        return true;
    }

    public bool Remove(UiSchemaCacheKey key)
    {
        string path = _pathPolicy.GetSchemaPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return _index.Remove(CreateCacheKey(key));
    }

    public static string CreateCacheKey(UiSchemaCacheKey key)
    {
        return string.Join(
            ':',
            "schema",
            key.Server.CacheNamespace,
            key.PanelType,
            key.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            key.SchemaHashHex);
    }
}
