namespace UltraSim.Client.Content;

/// <summary>Immutable selected passive clip bytes. A renderer may copy PCM into its own native stream.</summary>
public readonly record struct ResolvedContentAudioClip(
    LogicalContentId Id, ContentHash BlobHash, ContentHash CatalogFingerprint,
    ContentCatalogGenerationId CatalogGeneration, ContentAudioArtifactInfo Format, byte[] WavBytes);

/// <summary>
/// Audio uses the same scope-authorized catalog and CAS path as other passive content. A global
/// blob's presence alone never authorizes a cue in another scope.
/// </summary>
public static class ContentAudioClipResolver
{
    public static bool TryRead(ContentCache cache, ContentCatalogLease catalogLease,
        LogicalContentId id, out ResolvedContentAudioClip clip)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(catalogLease);
        clip = default;
        ResolvedContentCatalog catalog = catalogLease.Catalog;
        if (!ReferenceEquals(catalogLease.Owner, cache) ||
            !catalog.TryGet(id, out ResolvedContentEntry? entry) ||
            entry is null || entry.Kind != ContentDefinitionKind.AudioClip ||
            !cache.TryOpenPackage(catalog.Scope, entry.OwnerPackageId,
                entry.OwnerManifestHash, out VerifiedContentPackage? package, out _) ||
            package is null) return false;
        ContentFileDescriptor descriptor = default;
        foreach (ContentFileDescriptor candidate in package.Manifest.Files)
        {
            if (candidate.Path != entry.Definition.PrimaryFile) continue;
            descriptor = candidate;
            break;
        }
        if (descriptor.Kind != ContentFileKind.PcmWav || !descriptor.IsValid ||
            !cache.TryOpenBlob(descriptor, out ContentBlobLease? blob, out _) || blob is null)
            return false;
        using (blob)
        {
            if (descriptor.SizeBytes > ContentAudioArtifact.MaximumBytes) return false;
            byte[] bytes = new byte[checked((int)descriptor.SizeBytes)];
            blob.Stream.ReadExactly(bytes);
            if (!ContentAudioArtifact.TryInspect(bytes, out ContentAudioArtifactInfo info))
                return false;
            clip = new(id, descriptor.Sha256, catalog.Fingerprint, catalog.Generation,
                info, bytes);
            return true;
        }
    }
}
