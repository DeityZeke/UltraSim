using UltraSim.UI;

namespace UltraSim.Client.Content;

public enum ContentUiDefinitionResolutionError : byte
{
    None = 0,
    InvalidRequest = 1,
    CatalogDisposed = 2,
    DefinitionNotFound = 3,
    DuplicateDefinition = 4,
    CacheFailure = 5,
    InvalidArtifact = 6,
}

/// <summary>
/// Canonical package-local address for an exact UI artifact. The decoded typed identity remains
/// semantic authority; this address only avoids scanning or decoding unrelated package files.
/// </summary>
public static class ContentUiDefinitionPath
{
    public static bool TryCreate(UiDefinitionIdentity identity, out ContentFilePath path)
    {
        if (!identity.IsValid)
        {
            path = default;
            return false;
        }
        return ContentFilePath.TryCreate($"ui/{identity.Hash}.usui", out path);
    }
}

/// <summary>
/// Resolves an exact typed UI definition only from packages selected by one applied scope catalog.
/// File paths and generic logical content IDs never become UI definition identity.
/// </summary>
public static class ContentUiDefinitionResolver
{
    public static bool TryResolve(
        ContentCatalogLease catalog,
        UiDefinitionIdentity identity,
        out CompiledUiDefinition? definition,
        out ContentUiDefinitionResolutionError error)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        definition = null;
        error = ContentUiDefinitionResolutionError.None;
        if (!identity.IsValid)
            return Fail(ContentUiDefinitionResolutionError.InvalidRequest, out error);
        if (!ContentUiDefinitionPath.TryCreate(identity, out ContentFilePath artifactPath))
            return Fail(ContentUiDefinitionResolutionError.InvalidRequest, out error);

        ContentCache? cache = catalog.Owner;
        if (cache is null)
            return Fail(ContentUiDefinitionResolutionError.CatalogDisposed, out error);

        foreach (PackageManifestIdentity selected in catalog.SelectedPackages)
        {
            if (!cache.TryOpenPackage(
                    catalog.Catalog.Scope,
                    selected.PackageId,
                    selected.ManifestHash,
                    out VerifiedContentPackage? package,
                    out _))
                return Fail(ContentUiDefinitionResolutionError.CacheFailure, out error);

            if (!package!.Manifest.TryGetFile(artifactPath, out ContentFileDescriptor file)) continue;
            if (file.Kind != ContentFileKind.UiDefinition ||
                file.SizeBytes > UiDefinitionArtifactLimits.Default.MaximumArtifactBytes ||
                file.SizeBytes > int.MaxValue)
                return Fail(ContentUiDefinitionResolutionError.InvalidArtifact, out error);
            if (!cache.TryOpenBlob(file, out ContentBlobLease? blob, out _))
                return Fail(ContentUiDefinitionResolutionError.CacheFailure, out error);
            using (blob)
            {
                byte[] bytes = new byte[checked((int)file.SizeBytes)];
                if (!TryReadExact(blob!.Stream, bytes) ||
                    !UiDefinitionArtifactCodec.TryReadCanonical(bytes, out CompiledUiDefinition? candidate) ||
                    UiDefinitionIdentity.From(candidate!) != identity)
                    return Fail(ContentUiDefinitionResolutionError.InvalidArtifact, out error);
                if (definition is not null)
                {
                    definition = null;
                    return Fail(ContentUiDefinitionResolutionError.DuplicateDefinition, out error);
                }
                definition = candidate;
            }
        }

        if (definition is null)
            return Fail(ContentUiDefinitionResolutionError.DefinitionNotFound, out error);
        return true;
    }

    private static bool TryReadExact(Stream stream, Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination[offset..]);
            if (read == 0) return false;
            offset += read;
        }
        return stream.ReadByte() == -1;
    }

    private static bool Fail(
        ContentUiDefinitionResolutionError value,
        out ContentUiDefinitionResolutionError error)
    {
        error = value;
        return false;
    }
}
