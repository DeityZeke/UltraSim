using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

/// <summary>Explicit bounded ownership for the persistent passive-content store.</summary>
public readonly record struct ContentCacheLimits
{
    public ContentCacheLimits(
        long quotaBytes,
        long highWaterBytes,
        int maximumBlobs,
        int maximumManifests,
        int maximumActivations,
        int maximumMetadataFileBytes)
    {
        if (quotaBytes < 1 || highWaterBytes < 1 || highWaterBytes > quotaBytes ||
            maximumBlobs is < 1 or > 1_000_000 ||
            maximumManifests is < 1 or > 65_536 ||
            maximumActivations is < 1 or > 16_384 ||
            maximumMetadataFileBytes is < 1_024 or > 256 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(quotaBytes));
        QuotaBytes = quotaBytes;
        HighWaterBytes = highWaterBytes;
        MaximumBlobs = maximumBlobs;
        MaximumManifests = maximumManifests;
        MaximumActivations = maximumActivations;
        MaximumMetadataFileBytes = maximumMetadataFileBytes;
    }

    public static ContentCacheLimits Default { get; } = new(
        quotaBytes: 8L * 1024 * 1024 * 1024,
        highWaterBytes: 7L * 1024 * 1024 * 1024,
        maximumBlobs: 65_536,
        maximumManifests: 4_096,
        maximumActivations: 1_024,
        maximumMetadataFileBytes: 64 * 1024 * 1024);

    public long QuotaBytes { get; }
    public long HighWaterBytes { get; }
    public int MaximumBlobs { get; }
    public int MaximumManifests { get; }
    public int MaximumActivations { get; }
    public int MaximumMetadataFileBytes { get; }

    internal static ContentCacheLimits Resolve(ContentCacheLimits limits) =>
        limits.MaximumBlobs == 0 ? Default : limits;
}

public enum ContentCacheError : byte
{
    None = 0,
    InvalidRequest,
    CacheBusy,
    UnsafePath,
    CapacityExceeded,
    QuotaExceeded,
    UnsupportedTrustClass,
    PayloadValidationFailed,
    MissingManifest,
    CorruptManifest,
    MissingBlob,
    CorruptBlob,
    ActivationNotFound,
    ActivationConflict,
    CorruptActivation,
    CatalogResolutionFailed,
    IoFailure,
}

public readonly record struct InstalledContentPackage(
    ContentScopeKey Scope,
    ContentPackageId PackageId,
    ContentHash ManifestHash,
    int BlobCount,
    long BlobBytes)
{
    public ApplicationRealmId Realm => Scope.Realm;
    public bool IsValid => Scope.IsValid && PackageId.IsValid && ManifestHash.IsValid &&
        BlobCount >= 0 && BlobBytes >= 0;
}

public readonly record struct ContentCacheSnapshot(
    int BlobCount,
    long BlobBytes,
    int ScopeCount,
    int LocalManifestCount,
    int RealmManifestCount,
    int LocalActivationCount,
    int RealmActivationCount,
    int LiveBlobPins,
    int LiveCatalogPins,
    long QuotaBytes,
    long HighWaterBytes,
    ulong Installs,
    ulong ReusedBlobs,
    ulong CorruptionsDetected,
    ulong BlobsRepaired,
    ulong ManifestsRepaired,
    ulong BlobsEvicted,
    long BytesEvicted)
{
    public int ManifestCount => LocalManifestCount + RealmManifestCount;
    public int ActivationCount => LocalActivationCount + RealmActivationCount;
}

/// <summary>
/// Immutable preflight for one package install. Required payloads are the unique missing or
/// corrupt content-addressed blobs; already-valid blobs remain reusable regardless of realm.
/// </summary>
public sealed class ContentPackageInstallPlan
{
    internal ContentPackageInstallPlan(
        ContentFileDescriptor[] requiredPayloads,
        int reusedBlobCount,
        int corruptBlobCount,
        bool manifestNeedsRepair)
    {
        RequiredPayloads = Array.AsReadOnly(requiredPayloads);
        ReusedBlobCount = reusedBlobCount;
        CorruptBlobCount = corruptBlobCount;
        ManifestNeedsRepair = manifestNeedsRepair;
    }

    public IReadOnlyList<ContentFileDescriptor> RequiredPayloads { get; }
    public int ReusedBlobCount { get; }
    public int CorruptBlobCount { get; }
    public bool ManifestNeedsRepair { get; }
}

public readonly record struct ContentCacheEvictionResult(
    int BlobsRemoved,
    long BytesRemoved,
    int ManifestsRemoved,
    int ActivationsRemoved,
    long RemainingBlobBytes);

internal readonly record struct ContentCacheManifestKey(
    ContentScopeKey Scope,
    ContentHash ManifestHash);

public readonly record struct ContentScopeRetirementResult(
    int ManifestsRemoved,
    int ActivationsRemoved);
