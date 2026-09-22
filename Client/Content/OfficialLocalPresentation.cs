namespace UltraSim.Client.Content;

/// <summary>
/// Finite product-owned identity of the F1.4 Official world.local actor presentation package.
/// The source location is not a trust boundary: acquisition accepts only this exact package and
/// manifest, and ordinary passive validation verifies every declared blob before publication.
/// </summary>
public static class OfficialLocalPresentation
{
    public const string ArchiveFileName = "ultrasim-official-actors-v1.uspkg";
    public const string HumanXPath = "actors/human-x.glb";
    public const string HumanYPath = "actors/human-y.glb";
    public const long HumanXBytes = 2_213_380;
    public const long HumanYBytes = 2_509_988;
    public const string HumanXSha256 =
        "2704d59bee5f8046f335d7e4da05fb8f4f3c7c8f30cdf1e636c4de75ff3b5833";
    public const string HumanYSha256 =
        "553fb71ebe61caede890e582b93a37335ff554f1bb907c034658c5f434e6a567";

    public static ContentPackageManifest Manifest { get; } = CreateManifest();
    public static ContentActivationPlan Plan { get; } = CreatePlan();
    public static ContentScopeActivationKey ActivationKey { get; } =
        new(Plan.ScopeActivation, Plan.PlanHash);

    public static ContentPackageAcquisition CreateAcquisition() => new(
        Manifest.PackageId,
        Manifest.ManifestHash,
        [new ContentSourceDescriptor(ContentSourceKind.Local, ArchiveFileName)]);

    /// <summary>
    /// Reopens the exact persisted Local activation, or installs it from one explicitly selected
    /// read-only source root. Absence is an ordinary optional-presentation outcome.
    /// </summary>
    public static bool TryOpenOrAcquire(
        ContentCache cache,
        string? officialSourceRoot,
        out ContentCatalogLease? lease,
        out OfficialLocalPresentationResult result)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (cache.TryOpenCatalog(
                ContentBaseCatalog.Empty, ActivationKey, out lease, out ContentCacheError openError))
        {
            result = new OfficialLocalPresentationResult(
                OfficialLocalPresentationStatus.Reopened, openError, default, default);
            return true;
        }
        if (openError is not ContentCacheError.ActivationNotFound ||
            string.IsNullOrWhiteSpace(officialSourceRoot) ||
            !Directory.Exists(officialSourceRoot))
        {
            lease = null;
            result = new OfficialLocalPresentationResult(
                openError == ContentCacheError.ActivationNotFound
                    ? OfficialLocalPresentationStatus.Unavailable
                    : OfficialLocalPresentationStatus.CacheFailure,
                openError, default, default);
            return false;
        }

        try
        {
            var source = new LocalContentPackageSource(officialSourceRoot);
            bool applied = ContentActivationAcquirer.TryAcquireAndPublish(
                cache,
                ContentBaseCatalog.Empty,
                Plan,
                [CreateAcquisition()],
                source,
                persistentlyPinned: true,
                out lease,
                out ContentActivationAcquisitionResult acquisition);
            result = new OfficialLocalPresentationResult(
                applied
                    ? OfficialLocalPresentationStatus.Installed
                    : OfficialLocalPresentationStatus.AcquisitionFailure,
                acquisition.CacheError,
                acquisition.SourceError,
                acquisition);
            return applied;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            lease = null;
            result = new OfficialLocalPresentationResult(
                OfficialLocalPresentationStatus.SourceFailure,
                ContentCacheError.None,
                ContentSourceFetchError.Unavailable,
                default);
            return false;
        }
    }

    public static bool TryDecodeArchive(
        ReadOnlyMemory<byte> archive,
        out FetchedContentPackage? package) =>
        ContentPackageArchive.TryDecodeCanonical(
            archive, Manifest.PackageId, Manifest.ManifestHash, out package);

    private static ContentPackageManifest CreateManifest()
    {
        string? error = null;
        if (!ContentPackageId.TryCreate(
                "official.world-local.actor-presentations", out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate("actors", out ContentNamespaceId contentNamespace) ||
            !ContentFilePath.TryCreate(HumanXPath, out ContentFilePath xPath) ||
            !ContentFilePath.TryCreate(HumanYPath, out ContentFilePath yPath) ||
            !ContentHash.TryParse(HumanXSha256, out ContentHash xHash) ||
            !ContentHash.TryParse(HumanYSha256, out ContentHash yHash) ||
            !ContentFileDescriptor.TryCreate(
                xPath, ContentFileKind.ActorGltfBinary, HumanXBytes, xHash, default,
                out ContentFileDescriptor xFile) ||
            !ContentFileDescriptor.TryCreate(
                yPath, ContentFileKind.ActorGltfBinary, HumanYBytes, yHash, default,
                out ContentFileDescriptor yFile) ||
            !LogicalContentId.TryCreate(
                ConceptDemoActorPresentationProfile.HumanXDefinitionId, out LogicalContentId xId) ||
            !LogicalContentId.TryCreate(
                ConceptDemoActorPresentationProfile.HumanYDefinitionId, out LogicalContentId yId) ||
            !ContentDefinitionDescriptor.TryCreateFileDefinition(
                xId, ContentDefinitionKind.ActorPresentation, ContentDefinitionOperation.Add,
                xPath, out ContentDefinitionDescriptor? xDefinition) ||
            !ContentDefinitionDescriptor.TryCreateFileDefinition(
                yId, ContentDefinitionKind.ActorPresentation, ContentDefinitionOperation.Add,
                yPath, out ContentDefinitionDescriptor? yDefinition) ||
            !ContentPackageManifest.TryCreate(
                1, 1, packageId, contentNamespace,
                "UltraSim Official humanoid presentation", "1.0.0",
                ContentTrustClass.ScopeAuthorizedPassive,
                [xFile, yFile], [xDefinition!, yDefinition!],
                out ContentPackageManifest? manifest, out error))
            throw new InvalidOperationException(
                $"The compiled Official Local presentation identity is invalid: {error}");
        return manifest!;
    }

    private static ContentActivationPlan CreatePlan()
    {
        if (!ContentActivationPlan.TryCreate(
                ContentScopeKey.Local,
                new ContentActivationId(0x4631_2E34_2D4F_4646UL, 0x4943_4941_4C00_0001UL),
                ContentBaseCatalog.Empty.Fingerprint,
                [new ContentActivationPackage(
                    Manifest.PackageId, Manifest.ManifestHash, ContentRequirementPolicy.Required)],
                out ContentActivationPlan? plan,
                out string? error))
            throw new InvalidOperationException(
                $"The compiled Official Local presentation activation is invalid: {error}");
        return plan!;
    }
}

public enum OfficialLocalPresentationStatus : byte
{
    Unavailable = 0,
    Reopened = 1,
    Installed = 2,
    CacheFailure = 3,
    AcquisitionFailure = 4,
    SourceFailure = 5,
}

public readonly record struct OfficialLocalPresentationResult(
    OfficialLocalPresentationStatus Status,
    ContentCacheError CacheError,
    ContentSourceFetchError SourceError,
    ContentActivationAcquisitionResult Acquisition)
{
    public bool Available => Status is
        OfficialLocalPresentationStatus.Reopened or OfficialLocalPresentationStatus.Installed;
}
