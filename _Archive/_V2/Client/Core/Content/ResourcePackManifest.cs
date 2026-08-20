namespace UltraSim.Client.Content;

public sealed record ResourcePackManifest(
    string PackId,
    int Priority,
    IReadOnlyDictionary<string, string> Remaps);

public static class ResourcePackManifestValidator
{
    public static bool TryCreateResolverLayer(
        ResourcePackManifest manifest,
        out ContentResolverLayer layer,
        out string error)
    {
        layer = default!;
        if (string.IsNullOrWhiteSpace(manifest.PackId))
        {
            error = "Resource pack id is required.";
            return false;
        }

        if (manifest.PackId.Any(char.IsControl))
        {
            error = "Resource pack id cannot contain control characters.";
            return false;
        }

        int priority = ContentResolverPriority.UserResourcePack + manifest.Priority;
        return ContentResolverLayer.TryCreate(
            "resource-pack." + manifest.PackId.Trim(),
            ContentTrustClass.UserResourcePack,
            priority,
            manifest.Remaps,
            out layer,
            out error);
    }
}
