using UltraSim.Game.World;

namespace UltraSim.Client.Content;

public enum ContentWorldStructuralManifestResolutionStatus : byte
{
    None = 0,
    InvalidRequest,
    CatalogDisposed,
    DefinitionNotFound,
    DefinitionNotSelected,
    CacheFailure,
    InvalidArtifact,
    FingerprintMismatch,
}

/// <summary>
/// Opens exact scope-authorized passive bytes, then independently verifies the World-owned
/// structural format and advertised fingerprint. Cached blob possession is never authorization.
/// </summary>
public static class ContentWorldStructuralManifestResolver
{
    public static bool TryResolve(
        ContentCatalogLease catalog,
        LogicalContentId definitionId,
        WorldStructuralManifestFingerprint expectedFingerprint,
        out WorldStructuralManifest? manifest,
        out ContentWorldStructuralManifestResolutionStatus status,
        WorldStructuralManifestLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        manifest = null;
        status = ContentWorldStructuralManifestResolutionStatus.InvalidRequest;
        if (!definitionId.IsValid || !expectedFingerprint.IsValid) return false;
        ContentCache? cache = catalog.Owner;
        if (cache is null)
        {
            status = ContentWorldStructuralManifestResolutionStatus.CatalogDisposed;
            return false;
        }
        if (!catalog.Catalog.TryGet(definitionId, out ResolvedContentEntry? entry) ||
            entry!.Kind != ContentDefinitionKind.WorldStructuralManifest ||
            !entry.Definition.PrimaryFile.IsValid || entry.ReferencedFiles.Count != 1)
        {
            status = ContentWorldStructuralManifestResolutionStatus.DefinitionNotFound;
            return false;
        }
        PackageManifestIdentity owner = new(entry.OwnerPackageId, entry.OwnerManifestHash);
        bool selected = false;
        foreach (PackageManifestIdentity candidate in catalog.SelectedPackages)
            if (candidate == owner) { selected = true; break; }
        if (!selected)
        {
            status = ContentWorldStructuralManifestResolutionStatus.DefinitionNotSelected;
            return false;
        }
        if (!cache.TryOpenPackage(catalog.Catalog.Scope, owner.PackageId, owner.ManifestHash,
                out VerifiedContentPackage? package, out _))
        {
            status = ContentWorldStructuralManifestResolutionStatus.CacheFailure;
            return false;
        }
        if (!package!.Manifest.TryGetFile(entry.Definition.PrimaryFile,
                out ContentFileDescriptor file) ||
            file.Kind != ContentFileKind.WorldStructuralManifest ||
            file.Sha256 != entry.ReferencedFiles[0] ||
            file.SizeBytes > WorldStructuralManifestFormat.MaximumManifestBytes)
        {
            status = ContentWorldStructuralManifestResolutionStatus.InvalidArtifact;
            return false;
        }
        if (!cache.TryOpenBlob(file, out ContentBlobLease? blob, out _))
        {
            status = ContentWorldStructuralManifestResolutionStatus.CacheFailure;
            return false;
        }
        byte[] bytes = new byte[checked((int)file.SizeBytes)];
        using (blob)
        {
            try { blob!.Stream.ReadExactly(bytes); }
            catch (EndOfStreamException)
            {
                status = ContentWorldStructuralManifestResolutionStatus.InvalidArtifact;
                return false;
            }
        }
        if (!WorldStructuralManifest.TryOpen(bytes, out WorldStructuralManifest? opened,
                out _, limits))
        {
            status = ContentWorldStructuralManifestResolutionStatus.InvalidArtifact;
            return false;
        }
        if (opened!.Fingerprint != expectedFingerprint)
        {
            status = ContentWorldStructuralManifestResolutionStatus.FingerprintMismatch;
            return false;
        }
        manifest = opened;
        status = ContentWorldStructuralManifestResolutionStatus.None;
        return true;
    }
}
