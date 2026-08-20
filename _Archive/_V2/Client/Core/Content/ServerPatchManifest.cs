namespace UltraSim.Client.Content;

public sealed record ServerPatchManifest(
    string PatchId,
    int Revision,
    IReadOnlyList<ServerPatchManifestEntry> Entries);

public sealed record ServerPatchManifestEntry(
    string RelativePath,
    long SizeBytes,
    string Sha256Hex);

public static class ServerPatchManifestValidator
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat",
        ".cmd",
        ".com",
        ".dll",
        ".dylib",
        ".exe",
        ".gd",
        ".pdb",
        ".ps1",
        ".py",
        ".sh",
        ".so"
    };

    public static bool TryValidate(ServerPatchManifest manifest, out string error)
    {
        if (string.IsNullOrWhiteSpace(manifest.PatchId))
        {
            error = "Patch id is required.";
            return false;
        }

        if (manifest.PatchId.Any(char.IsControl))
        {
            error = "Patch id cannot contain control characters.";
            return false;
        }

        if (manifest.Revision < 0)
        {
            error = "Patch revision cannot be negative.";
            return false;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ServerPatchManifestEntry entry in manifest.Entries)
        {
            if (!ContentPathValidation.TryNormalizeRelativePath(entry.RelativePath, out string normalizedPath, out error))
            {
                return false;
            }

            string extension = Path.GetExtension(normalizedPath);
            if (BlockedExtensions.Contains(extension))
            {
                error = $"Server patch content cannot include executable/script path '{normalizedPath}'.";
                return false;
            }

            if (!seenPaths.Add(normalizedPath))
            {
                error = $"Server patch manifest contains duplicate path '{normalizedPath}'.";
                return false;
            }

            if (entry.SizeBytes < 0)
            {
                error = $"Server patch entry '{normalizedPath}' has a negative size.";
                return false;
            }

            if (!ContentPathValidation.TryNormalizeSha256Hex(entry.Sha256Hex, out _, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
