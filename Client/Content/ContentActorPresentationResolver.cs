namespace UltraSim.Client.Content;

public enum ContentActorPresentationResolutionError : byte
{
    None = 0,
    InvalidRequest,
    CatalogDisposed,
    DefinitionNotFound,
    DefinitionKindMismatch,
    ManifestUnavailable,
    FileMismatch,
    CacheFailure,
    InvalidArtifact,
    SkeletonProfileMismatch,
}

/// <summary>Stable logical identities and skeleton contract for the Concept Demo humanoids.</summary>
public static class ConceptDemoActorPresentationProfile
{
    public const string HumanXDefinitionId = "actors:human/x";
    public const string HumanYDefinitionId = "actors:human/y";
    public const string OrderedSkeletonFingerprint =
        "b9830ad4b78df28556c6be2336a46ac9703063b5e07725d4cea98943d18626ee";

    public static ActorPresentationResolutionRequirements Requirements { get; } = Create();

    private static ActorPresentationResolutionRequirements Create()
    {
        if (!ContentHash.TryParse(OrderedSkeletonFingerprint, out ContentHash skeleton))
            throw new InvalidOperationException("The Concept Demo skeleton profile is invalid.");
        return new ActorPresentationResolutionRequirements(
            65, skeleton, ActorPresentationSemanticClips.RequiredV1);
    }
}

/// <summary>Applied-catalog source boundary consumed by engine-specific native resource caches.</summary>
public interface IActorPresentationContentSource
{
    bool TryResolveActorPresentation(
        LogicalContentId definitionId,
        ActorPresentationResolutionRequirements requirements,
        out ActorPresentationContentLease? lease,
        out ContentActorPresentationResolutionError error,
        ActorPresentationArtifactLimits limits = default);
}

/// <summary>Finite semantic compatibility required by one actor-presentation consumer.</summary>
public readonly record struct ActorPresentationResolutionRequirements(
    int ExactBoneCount,
    ContentHash OrderedSkeletonFingerprint,
    ActorPresentationSemanticClips RequiredClips)
{
    public bool IsValid => ExactBoneCount is >= 1 and <= 1_024 &&
        OrderedSkeletonFingerprint.IsValid && RequiredClips != ActorPresentationSemanticClips.None &&
        (RequiredClips & ~ActorPresentationSemanticClips.RequiredV1) == 0;
}

/// <summary>
/// Process-local native-realization input. The exact CAS blob stays pinned until the engine-owned
/// resource cache releases this lease; possessing equivalent bytes outside the applied catalog is
/// insufficient to produce it.
/// </summary>
public sealed class ActorPresentationContentLease : IDisposable
{
    private IDisposable? _lifetime;
    private readonly byte[] _bytes;

    internal ActorPresentationContentLease(
        IDisposable lifetime,
        byte[] bytes,
        ActorPresentationArtifactInfo info,
        ContentCatalogGenerationId catalogGeneration,
        LogicalContentId definitionId,
        ContentPackageId ownerPackage,
        ContentHash ownerManifest,
        ContentHash fileHash)
    {
        _lifetime = lifetime;
        _bytes = bytes;
        Bytes = bytes;
        Info = info;
        CatalogGeneration = catalogGeneration;
        DefinitionId = definitionId;
        OwnerPackage = ownerPackage;
        OwnerManifest = ownerManifest;
        FileHash = fileHash;
    }

    public ReadOnlyMemory<byte> Bytes { get; }
    public ActorPresentationArtifactInfo Info { get; }
    public ContentCatalogGenerationId CatalogGeneration { get; }
    public LogicalContentId DefinitionId { get; }
    public ContentPackageId OwnerPackage { get; }
    public ContentHash OwnerManifest { get; }
    public ContentHash FileHash { get; }
    public bool IsDisposed => Volatile.Read(ref _lifetime) is null;

    internal ActorPresentationContentLease WithLifetime(IDisposable lifetime) => new(
        lifetime, _bytes, Info, CatalogGeneration, DefinitionId,
        OwnerPackage, OwnerManifest, FileHash);

    internal ActorPresentationContentLease WithLifetime(
        IDisposable lifetime, PresentationGenerationId generation, PresentationSourceKind source)
    {
        var result = WithLifetime(lifetime);
        result.PresentationGeneration = generation;
        result.PresentationSource = source;
        return result;
    }

    public PresentationGenerationId PresentationGeneration { get; private set; }
    public PresentationSourceKind PresentationSource { get; private set; }

    public void Dispose() => Interlocked.Exchange(ref _lifetime, null)?.Dispose();
}

/// <summary>
/// Resolves one passive actor artifact only through a live applied scope catalog, then leaves
/// native import and scene ownership to the engine adapter.
/// </summary>
public static class ContentActorPresentationResolver
{
    public static bool TryResolve(
        ContentCatalogLease catalog,
        LogicalContentId definitionId,
        ActorPresentationResolutionRequirements requirements,
        out ActorPresentationContentLease? lease,
        out ContentActorPresentationResolutionError error,
        ActorPresentationArtifactLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lease = null;
        error = ContentActorPresentationResolutionError.None;
        if (!definitionId.IsValid || !requirements.IsValid)
            return Fail(ContentActorPresentationResolutionError.InvalidRequest, out error);

        ContentCache? cache = catalog.Owner;
        if (cache is null)
            return Fail(ContentActorPresentationResolutionError.CatalogDisposed, out error);
        if (!catalog.Catalog.TryGet(definitionId, out ResolvedContentEntry? entry))
            return Fail(ContentActorPresentationResolutionError.DefinitionNotFound, out error);
        if (entry!.Kind != ContentDefinitionKind.ActorPresentation ||
            entry.ReferencedFiles.Count != 1)
            return Fail(ContentActorPresentationResolutionError.DefinitionKindMismatch, out error);
        if (!cache.TryOpenPackage(
                catalog.Catalog.Scope,
                entry.OwnerPackageId,
                entry.OwnerManifestHash,
                out VerifiedContentPackage? package,
                out _))
            return Fail(ContentActorPresentationResolutionError.ManifestUnavailable, out error);
        if (!package!.Manifest.TryGetFile(
                entry.Definition.PrimaryFile, out ContentFileDescriptor file) ||
            file.Kind != ContentFileKind.ActorGltfBinary ||
            file.Sha256 != entry.ReferencedFiles[0] || file.SizeBytes > int.MaxValue)
            return Fail(ContentActorPresentationResolutionError.FileMismatch, out error);
        if (!cache.TryOpenBlob(file, out ContentBlobLease? blob, out _))
            return Fail(ContentActorPresentationResolutionError.CacheFailure, out error);

        try
        {
            byte[] bytes = new byte[checked((int)file.SizeBytes)];
            if (!TryReadExact(blob!.Stream, bytes) || blob.Stream.ReadByte() != -1 ||
                !ActorPresentationArtifact.TryInspect(bytes, out ActorPresentationArtifactInfo info,
                    out _, limits))
                return Fail(ContentActorPresentationResolutionError.InvalidArtifact, out error);
            if (info.Bones != requirements.ExactBoneCount ||
                info.OrderedSkeletonFingerprint != requirements.OrderedSkeletonFingerprint ||
                (info.SemanticClips & requirements.RequiredClips) != requirements.RequiredClips)
                return Fail(ContentActorPresentationResolutionError.SkeletonProfileMismatch, out error);

            lease = new ActorPresentationContentLease(
                blob, bytes, info, catalog.Catalog.Generation, definitionId,
                entry.OwnerPackageId, entry.OwnerManifestHash, file.Sha256);
            blob = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or
            OverflowException)
        {
            return Fail(ContentActorPresentationResolutionError.CacheFailure, out error);
        }
        finally
        {
            blob?.Dispose();
        }
    }

    private static bool TryReadExact(Stream stream, Span<byte> destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination[offset..]);
            if (read <= 0) return false;
            offset += read;
        }
        return true;
    }

    private static bool Fail(
        ContentActorPresentationResolutionError value,
        out ContentActorPresentationResolutionError error)
    {
        error = value;
        return false;
    }
}
