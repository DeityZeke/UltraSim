using UltraSim.Game.World;

namespace UltraSim.Client.Content;

public enum ContentWorldPackResolutionStatus : byte
{
    None = 0,
    InvalidRequest = 1,
    CatalogDisposed = 2,
    DefinitionNotFound = 3,
    DefinitionNotSelected = 4,
    CacheFailure = 5,
    InvalidArtifact = 6,
    PackFingerprintMismatch = 7,
    ContentBindingFailure = 8,
}

public readonly record struct ContentWorldPackResolutionResult(
    ContentWorldPackResolutionStatus Status,
    WorldPackOpenError OpenError,
    WorldPackBindingError BindingError)
{
    public bool IsResolved => Status == ContentWorldPackResolutionStatus.None &&
        OpenError == WorldPackOpenError.None && BindingError == WorldPackBindingError.None;
}

/// <summary>
/// Opens and binds one exact World-owned artifact selected by an applied scope catalog. Content
/// authorizes and supplies bytes; World retains container, fingerprint, and structural binding
/// authority.
/// </summary>
public static class ContentWorldPackResolver
{
    public static bool TryResolve(
        ContentCatalogLease catalog,
        LogicalContentId definitionId,
        WorldPackFingerprint expectedPackFingerprint,
        WorldContentBinding binding,
        out BoundWorldPack? world,
        out ContentWorldPackResolutionResult result,
        WorldPackLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(binding);
        world = null;
        result = default;
        if (!definitionId.IsValid || !expectedPackFingerprint.IsValid)
            return Fail(ContentWorldPackResolutionStatus.InvalidRequest, out result);
        if (limits == default) limits = WorldPackLimits.Default;

        ContentCache? cache = catalog.Owner;
        if (cache is null)
            return Fail(ContentWorldPackResolutionStatus.CatalogDisposed, out result);
        if (!catalog.Catalog.TryGet(definitionId, out ResolvedContentEntry? entry) ||
            entry!.Kind != ContentDefinitionKind.WorldPack ||
            !entry.Definition.PrimaryFile.IsValid || entry.ReferencedFiles.Count != 1)
            return Fail(ContentWorldPackResolutionStatus.DefinitionNotFound, out result);

        PackageManifestIdentity owner = new(entry.OwnerPackageId, entry.OwnerManifestHash);
        if (!Contains(catalog.SelectedPackages, owner))
            return Fail(ContentWorldPackResolutionStatus.DefinitionNotSelected, out result);
        if (!cache.TryOpenPackage(
                catalog.Catalog.Scope, owner.PackageId, owner.ManifestHash,
                out VerifiedContentPackage? package, out _))
            return Fail(ContentWorldPackResolutionStatus.CacheFailure, out result);
        if (!package!.Manifest.TryGetFile(
                entry.Definition.PrimaryFile, out ContentFileDescriptor file) ||
            file.Kind != ContentFileKind.WorldPack || file.Sha256 != entry.ReferencedFiles[0] ||
            file.SizeBytes > limits.MaximumPackBytes)
            return Fail(ContentWorldPackResolutionStatus.InvalidArtifact, out result);
        if (!cache.TryOpenBlob(file, out ContentBlobLease? blob, out _))
            return Fail(ContentWorldPackResolutionStatus.CacheFailure, out result);

        WorldPack? pack;
        WorldPackOpenError openError;
        using (blob)
        {
            if (!WorldPack.TryOpen(
                    blob!.Stream, limits, binding.Fingerprint, out pack, out openError))
                return Fail(ContentWorldPackResolutionStatus.InvalidArtifact, openError,
                    WorldPackBindingError.None, out result);
        }
        if (pack!.Fingerprint != expectedPackFingerprint)
            return Fail(ContentWorldPackResolutionStatus.PackFingerprintMismatch,
                WorldPackOpenError.None, WorldPackBindingError.None, out result);
        if (!pack.TryBind(binding, out world, out WorldPackBindingError bindingError))
            return Fail(ContentWorldPackResolutionStatus.ContentBindingFailure,
                WorldPackOpenError.None, bindingError, out result);

        result = new ContentWorldPackResolutionResult(
            ContentWorldPackResolutionStatus.None,
            WorldPackOpenError.None,
            WorldPackBindingError.None);
        return true;
    }

    private static bool Contains(
        ReadOnlySpan<PackageManifestIdentity> selected,
        PackageManifestIdentity owner)
    {
        foreach (PackageManifestIdentity candidate in selected)
            if (candidate == owner) return true;
        return false;
    }

    private static bool Fail(
        ContentWorldPackResolutionStatus status,
        out ContentWorldPackResolutionResult result) =>
        Fail(status, WorldPackOpenError.None, WorldPackBindingError.None, out result);

    private static bool Fail(
        ContentWorldPackResolutionStatus status,
        WorldPackOpenError openError,
        WorldPackBindingError bindingError,
        out ContentWorldPackResolutionResult result)
    {
        result = new ContentWorldPackResolutionResult(status, openError, bindingError);
        return false;
    }
}
