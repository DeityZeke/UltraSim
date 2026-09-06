namespace UltraSim.Client.Content;

/// <summary>
/// Canonical transport envelope for an immutable package manifest. It is a source artifact only;
/// the manifest hash and package identity remain the semantic authority.
/// </summary>
public static class ContentPackageManifestEnvelope
{
    public static bool TryEncodeCanonical(
        ContentPackageManifest manifest,
        out byte[] bytes,
        ContentLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        limits = ContentLimits.Resolve(limits);
        bytes = ContentCacheMetadata.EncodeManifest(manifest);
        return bytes.Length <= limits.MaximumPackageBytes;
    }

    public static bool TryDecodeCanonical(
        ReadOnlySpan<byte> bytes,
        ContentPackageId expectedPackage,
        ContentHash expectedManifestHash,
        out ContentPackageManifest? manifest,
        ContentLimits limits = default)
    {
        manifest = null;
        limits = ContentLimits.Resolve(limits);
        return expectedPackage.IsValid && expectedManifestHash.IsValid &&
            bytes.Length > 0 && bytes.Length <= limits.MaximumPackageBytes &&
            ContentCacheMetadata.TryDecodeManifest(bytes, expectedManifestHash, limits, out manifest) &&
            manifest!.PackageId == expectedPackage;
    }
}
