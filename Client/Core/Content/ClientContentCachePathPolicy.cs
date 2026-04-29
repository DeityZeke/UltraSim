namespace UltraSim.Client.Content;

public sealed class ClientContentCachePathPolicy
{
    public ClientContentCachePathPolicy(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Cache root path is required.", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }
    public string GlobalCacheRoot => Path.Combine(RootPath, "Global");
    public string ModsRoot => Path.Combine(RootPath, "Mods");
    public string CacheIndexPath => Path.Combine(RootPath, "cache-index.json");

    public string GetServerRoot(ServerContentIdentity server)
    {
        return Path.Combine(RootPath, "Servers", server.CacheNamespace);
    }

    public bool TryGetServerAssetPath(ServerContentIdentity server, string? relativePath, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        if (!ContentPathValidation.TryNormalizeRelativePath(relativePath, out string normalizedPath, out error))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(GetServerRoot(server), "Assets", normalizedPath));
        string assetRoot = Path.GetFullPath(Path.Combine(GetServerRoot(server), "Assets"));
        if (!IsWithinRoot(candidate, assetRoot))
        {
            error = "Content path escapes the server asset cache root.";
            return false;
        }

        fullPath = candidate;
        error = string.Empty;
        return true;
    }

    public string GetSchemaPath(UiSchemaCacheKey key)
    {
        return Path.Combine(
            GetServerRoot(key.Server),
            "Schemas",
            key.PanelType,
            key.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            key.SchemaHashHex + ".uischema");
    }

    private static bool IsWithinRoot(string candidatePath, string rootPath)
    {
        string normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }
}
