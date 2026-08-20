using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UltraSim.Client.Content;

public sealed class ServerPatchManifestCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ClientContentCachePathPolicy _pathPolicy;
    private readonly ContentCacheIndex _index;

    public ServerPatchManifestCacheStore(ClientContentCachePathPolicy pathPolicy, ContentCacheIndex index)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public bool Has(ServerContentIdentity server, string patchId, int revision)
    {
        return TryGetPath(server, patchId, revision, out string path, out _) && File.Exists(path);
    }

    public bool TryWrite(
        ServerContentIdentity server,
        ServerPatchManifest manifest,
        DateTimeOffset nowUtc,
        bool pinned,
        out string error)
    {
        if (!ServerPatchManifestValidator.TryValidate(manifest, out error))
        {
            return false;
        }

        if (!TryGetPath(server, manifest.PatchId, manifest.Revision, out string path, out error))
        {
            return false;
        }

        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);

        string relativePath = Path.GetRelativePath(_pathPolicy.RootPath, path).Replace('\\', '/');
        var entry = new ContentCacheEntry(
            CreateCacheKey(server, manifest.PatchId, manifest.Revision),
            relativePath,
            ContentCacheEntryKind.PatchManifest,
            bytes.Length,
            nowUtc,
            pinned);
        return _index.TryUpsert(entry, out error);
    }

    public bool TryRead(
        ServerContentIdentity server,
        string patchId,
        int revision,
        DateTimeOffset nowUtc,
        out ServerPatchManifest manifest,
        out string error)
    {
        manifest = new ServerPatchManifest(string.Empty, 0, Array.Empty<ServerPatchManifestEntry>());
        if (!TryGetPath(server, patchId, revision, out string path, out error))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            error = "Server patch manifest is not cached.";
            return false;
        }

        string json = File.ReadAllText(path);
        ServerPatchManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ServerPatchManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Server patch manifest JSON is invalid: {ex.Message}";
            return false;
        }

        if (parsed is null)
        {
            error = "Server patch manifest JSON is empty.";
            return false;
        }

        if (!string.Equals(parsed.PatchId, patchId, StringComparison.Ordinal) || parsed.Revision != revision)
        {
            error = "Cached patch manifest identity does not match requested key.";
            return false;
        }

        if (!ServerPatchManifestValidator.TryValidate(parsed, out error))
        {
            return false;
        }

        manifest = parsed;
        _index.TryMarkAccessed(CreateCacheKey(server, patchId, revision), nowUtc);
        error = string.Empty;
        return true;
    }

    public bool Remove(ServerContentIdentity server, string patchId, int revision)
    {
        bool removedFile = false;
        if (TryGetPath(server, patchId, revision, out string path, out _) && File.Exists(path))
        {
            File.Delete(path);
            removedFile = true;
        }

        bool removedMetadata = _index.Remove(CreateCacheKey(server, patchId, revision));
        return removedFile || removedMetadata;
    }

    public bool TryGetPath(
        ServerContentIdentity server,
        string patchId,
        int revision,
        out string path,
        out string error)
    {
        path = string.Empty;
        if (!TryValidatePatchIdentity(patchId, revision, out error))
        {
            return false;
        }

        string patchIdHashHex = CreatePatchIdHashHex(patchId);
        path = _pathPolicy.GetServerPatchManifestPath(server, patchIdHashHex, revision);
        error = string.Empty;
        return true;
    }

    public static string CreateCacheKey(ServerContentIdentity server, string patchId, int revision)
    {
        return string.Join(
            ':',
            "patch-manifest",
            server.CacheNamespace,
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CreatePatchIdHashHex(patchId));
    }

    public static string CreatePatchIdHashHex(string patchId)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(patchId))).ToLowerInvariant();
    }

    private static bool TryValidatePatchIdentity(string patchId, int revision, out string error)
    {
        if (string.IsNullOrWhiteSpace(patchId))
        {
            error = "Patch id is required.";
            return false;
        }

        if (patchId.Any(char.IsControl))
        {
            error = "Patch id cannot contain control characters.";
            return false;
        }

        if (revision < 0)
        {
            error = "Patch revision cannot be negative.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
