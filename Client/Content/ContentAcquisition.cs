using System.Collections.ObjectModel;
using UltraSim.Game.Application;

namespace UltraSim.Client.Content;

public enum ContentSourceFetchError : byte
{
    None = 0,
    InvalidRequest,
    UnsupportedSource,
    Unavailable,
    CapacityExceeded,
    SourceFailure,
}

public enum ContentActivationAcquisitionStatus : byte
{
    None = 0,
    Applied,
    InvalidRequirement,
    CacheFailure,
    SourceUnavailable,
    SourceFailure,
    CandidateMismatch,
    InstallFailure,
    CatalogFailure,
}

public readonly record struct ContentPackageFetchRequest(
    ApplicationRealmId Realm,
    ContentPackageId PackageId,
    ContentHash ManifestHash,
    ContentSourceDescriptor Source)
{
    public bool IsValid => Realm.IsValid && PackageId.IsValid && ManifestHash.IsValid && Source.IsValid;
}

/// <summary>Bounded untrusted candidate returned by one approved physical byte source.</summary>
public sealed class FetchedContentPackage
{
    private readonly ReadOnlyCollection<ContentFilePayload> _payloads;

    public FetchedContentPackage(
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload> payloads,
        ContentLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(payloads);
        limits = ContentLimits.Resolve(limits);
        ContentFilePayload[] copy = payloads.Take(limits.MaximumFilesPerPackage + 1).ToArray();
        if (copy.Length > limits.MaximumFilesPerPackage)
            throw new ArgumentException("The fetched package exceeds the payload-count bound.", nameof(payloads));
        Manifest = manifest;
        _payloads = Array.AsReadOnly(copy);
    }

    public ContentPackageManifest Manifest { get; }
    public ReadOnlyCollection<ContentFilePayload> Payloads => _payloads;
}

/// <summary>
/// Physical byte-source seam. The authenticated requirement chooses the descriptor; an adapter
/// may only return candidate bytes for that exact request and confers no publication authority.
/// </summary>
public interface IContentPackageSource
{
    bool CanFetch(ContentSourceKind kind);

    bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error);
}

public readonly record struct ContentActivationAcquisitionResult(
    ContentActivationAcquisitionStatus Status,
    ContentCacheError CacheError,
    ContentSourceFetchError SourceError,
    int PackagesRequired,
    int PackagesReused,
    int PackagesFetched,
    int FilesFetched,
    long BytesFetched,
    ContentSourceDescriptor SelectedSource)
{
    public bool Applied => Status == ContentActivationAcquisitionStatus.Applied;
}

/// <summary>
/// Source-neutral A0.3 join transaction: reuse verified cache state when possible, otherwise fetch
/// exact authorized candidates, install through A0.2, and publish one immutable catalog last.
/// </summary>
public static class ContentActivationAcquirer
{
    public static bool TryAcquireAndPublish(
        ContentCache cache,
        ContentBaseCatalog baseCatalog,
        ContentActivationPlan plan,
        IEnumerable<ContentPackageAcquisition> acquisitions,
        IContentPackageSource source,
        bool persistentlyPinned,
        out ContentCatalogLease? lease,
        out ContentActivationAcquisitionResult result)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(baseCatalog);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(acquisitions);
        ArgumentNullException.ThrowIfNull(source);
        lease = null;

        ContentPackageAcquisition[] candidates = acquisitions
            .Take(plan.Packages.Count + 1).ToArray();
        if (candidates.Length != plan.Packages.Count ||
            candidates.Select(static value => value.Package).Distinct().Count() != candidates.Length)
            return Fail(ContentActivationAcquisitionStatus.InvalidRequirement, out result);

        int required = 0, reused = 0, fetched = 0, files = 0;
        long bytes = 0;
        ContentSourceDescriptor selected = default;
        foreach (ContentActivationPackage requirement in plan.Packages)
        {
            ContentPackageAcquisition? acquisition = candidates.FirstOrDefault(value =>
                value.Package == requirement.PackageId && value.ManifestHash == requirement.ManifestHash);
            if (acquisition is null)
                return Fail(ContentActivationAcquisitionStatus.InvalidRequirement, out result,
                    required, reused, fetched, files, bytes, selected);
            if (requirement.Requirement == ContentRequirementPolicy.Required) required++;

            if (cache.TryOpenPackage(
                    plan.Realm, requirement.PackageId, requirement.ManifestHash,
                    out _, out ContentCacheError openError))
            {
                reused++;
                continue;
            }
            if (openError != ContentCacheError.MissingManifest)
                return Fail(ContentActivationAcquisitionStatus.CacheFailure, out result,
                    required, reused, fetched, files, bytes, selected, openError);

            bool foundSource = false;
            foreach (ContentSourceDescriptor candidate in acquisition.Sources)
            {
                if (!source.CanFetch(candidate.Kind)) continue;
                selected = candidate;
                foundSource = true;
                break;
            }
            if (!foundSource)
                return Fail(ContentActivationAcquisitionStatus.SourceUnavailable, out result,
                    required, reused, fetched, files, bytes, selected,
                    sourceError: ContentSourceFetchError.UnsupportedSource);

            var request = new ContentPackageFetchRequest(
                plan.Realm, requirement.PackageId, requirement.ManifestHash, selected);
            FetchedContentPackage? downloaded;
            ContentSourceFetchError fetchError;
            try
            {
                if (!source.TryFetch(in request, out downloaded, out fetchError))
                    return Fail(ContentActivationAcquisitionStatus.SourceFailure, out result,
                        required, reused, fetched, files, bytes, selected, sourceError: fetchError);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Fail(ContentActivationAcquisitionStatus.SourceFailure, out result,
                    required, reused, fetched, files, bytes, selected,
                    sourceError: ContentSourceFetchError.SourceFailure);
            }
            if (downloaded is null || downloaded.Manifest.PackageId != requirement.PackageId ||
                downloaded.Manifest.ManifestHash != requirement.ManifestHash)
                return Fail(ContentActivationAcquisitionStatus.CandidateMismatch, out result,
                    required, reused, fetched, files, bytes, selected);

            long fetchedBytes = 0;
            foreach (ContentFilePayload payload in downloaded.Payloads)
                fetchedBytes = checked(fetchedBytes + payload.Bytes.Length);
            if (!cache.TryInstallPackage(
                    plan.Realm, downloaded.Manifest, downloaded.Payloads,
                    out _, out ContentCacheError installError))
                return Fail(ContentActivationAcquisitionStatus.InstallFailure, out result,
                    required, reused, fetched, files, bytes, selected, installError);
            fetched++;
            files = checked(files + downloaded.Payloads.Count);
            bytes = checked(bytes + fetchedBytes);
        }

        if (!cache.TryPublishCatalog(
                baseCatalog, plan, persistentlyPinned, out lease, out ContentCacheError catalogError))
            return Fail(ContentActivationAcquisitionStatus.CatalogFailure, out result,
                required, reused, fetched, files, bytes, selected, catalogError);
        result = new ContentActivationAcquisitionResult(
            ContentActivationAcquisitionStatus.Applied, ContentCacheError.None,
            ContentSourceFetchError.None, required, reused, fetched, files, bytes, selected);
        return true;
    }

    private static bool Fail(
        ContentActivationAcquisitionStatus status,
        out ContentActivationAcquisitionResult result,
        int required = 0,
        int reused = 0,
        int fetched = 0,
        int files = 0,
        long bytes = 0,
        ContentSourceDescriptor selected = default,
        ContentCacheError cacheError = ContentCacheError.None,
        ContentSourceFetchError sourceError = ContentSourceFetchError.None)
    {
        result = new ContentActivationAcquisitionResult(
            status, cacheError, sourceError, required, reused, fetched, files, bytes, selected);
        return false;
    }
}
