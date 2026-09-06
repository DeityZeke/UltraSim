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
    CandidateMismatch,
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

public readonly record struct ContentActivationAcquirerLimits
{
    public ContentActivationAcquirerLimits(int maximumSourceAttemptsPerPackage, int maximumConcurrentFileFetches)
    {
        if (maximumSourceAttemptsPerPackage is < 1 or > 32 || maximumConcurrentFileFetches is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(maximumSourceAttemptsPerPackage));
        MaximumSourceAttemptsPerPackage = maximumSourceAttemptsPerPackage;
        MaximumConcurrentFileFetches = maximumConcurrentFileFetches;
    }

    public static ContentActivationAcquirerLimits Default { get; } = new(8, 4);
    public int MaximumSourceAttemptsPerPackage { get; }
    public int MaximumConcurrentFileFetches { get; }

    internal static ContentActivationAcquirerLimits Resolve(ContentActivationAcquirerLimits limits) =>
        limits.MaximumSourceAttemptsPerPackage == 0 ? Default : limits;
}

public readonly record struct ContentPackageFetchRequest(
    ApplicationRealmId Realm,
    ContentPackageId PackageId,
    ContentHash ManifestHash,
    ContentSourceDescriptor Source)
{
    public bool IsValid => Realm.IsValid && PackageId.IsValid && ManifestHash.IsValid && Source.IsValid;
}

public readonly record struct ContentPackageManifestFetchRequest(
    ApplicationRealmId Realm,
    ContentPackageId PackageId,
    ContentHash ManifestHash,
    ContentSourceDescriptor Source)
{
    public bool IsValid => Realm.IsValid && PackageId.IsValid && ManifestHash.IsValid && Source.IsValid;
}

public readonly record struct ContentFileFetchRequest(
    ApplicationRealmId Realm,
    ContentPackageId PackageId,
    ContentHash ManifestHash,
    ContentFileDescriptor File,
    ContentSourceDescriptor Source)
{
    public bool IsValid => Realm.IsValid && PackageId.IsValid && ManifestHash.IsValid &&
        File.IsValid && Source.IsValid;
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

public interface IContentPackageSource
{
    bool CanFetch(ContentSourceKind kind);

    bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error);
}

/// <summary>
/// Optional hash-granular source capability. File calls may occur concurrently, but one mirror
/// candidate is completely validated before any of its bytes enter the cache.
/// </summary>
public interface IContentPackageFileSource : IContentPackageSource
{
    bool CanFetchFiles(in ContentSourceDescriptor source);

    bool TryFetchManifest(
        in ContentPackageManifestFetchRequest request,
        out ContentPackageManifest? manifest,
        out ContentSourceFetchError error);

    bool TryFetchFile(
        in ContentFileFetchRequest request,
        out ContentFilePayload payload,
        out ContentSourceFetchError error);
}

public readonly record struct ContentActivationAcquisitionResult(
    ContentActivationAcquisitionStatus Status,
    ContentCacheError CacheError,
    ContentSourceFetchError SourceError,
    int PackagesRequired,
    int PackagesReused,
    int PackagesFetched,
    int FilesReused,
    int FilesFetched,
    long BytesFetched,
    int SourceAttempts,
    int SourcesRejected,
    int OptionalPackagesDeferred,
    int PeakConcurrentFileFetches,
    ContentSourceDescriptor SelectedSource)
{
    public bool Applied => Status == ContentActivationAcquisitionStatus.Applied;
}

/// <summary>
/// Source-neutral join transaction: reuse exact cache state, retry bounded approved mirrors,
/// acquire only invalid/missing hashes when supported, and publish one immutable catalog last.
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
        out ContentActivationAcquisitionResult result,
        ContentActivationAcquirerLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(baseCatalog);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(acquisitions);
        ArgumentNullException.ThrowIfNull(source);
        limits = ContentActivationAcquirerLimits.Resolve(limits);
        lease = null;

        ContentPackageAcquisition[] candidates = acquisitions.Take(plan.Packages.Count + 1).ToArray();
        if (candidates.Length != plan.Packages.Count ||
            candidates.Select(static value => value.Package).Distinct().Count() != candidates.Length)
            return Fail(ContentActivationAcquisitionStatus.InvalidRequirement, out result);

        var progress = new AcquisitionProgress();
        foreach (ContentActivationPackage requirement in plan.Packages)
        {
            ContentPackageAcquisition? acquisition = candidates.FirstOrDefault(value =>
                value.Package == requirement.PackageId && value.ManifestHash == requirement.ManifestHash);
            if (acquisition is null)
                return Fail(ContentActivationAcquisitionStatus.InvalidRequirement, progress, out result);
            if (requirement.Requirement == ContentRequirementPolicy.Required) progress.Required++;

            if (cache.TryOpenPackage(plan.Realm, requirement.PackageId, requirement.ManifestHash,
                    out VerifiedContentPackage? installed, out ContentCacheError openError))
            {
                progress.PackagesReused++;
                progress.FilesReused += installed!.Manifest.Files.Select(static value => value.Sha256).Distinct().Count();
                continue;
            }
            if (openError is not (ContentCacheError.MissingManifest or ContentCacheError.MissingBlob or
                    ContentCacheError.CorruptManifest or ContentCacheError.CorruptBlob))
                return Fail(ContentActivationAcquisitionStatus.CacheFailure, progress, out result,
                    cacheError: openError);

            ContentPackageManifest? cachedManifest = null;
            _ = cache.TryOpenPackageManifest(plan.Realm, requirement.PackageId, requirement.ManifestHash,
                out cachedManifest, out ContentCacheError manifestOpenError);
            if (manifestOpenError is not (ContentCacheError.None or ContentCacheError.MissingManifest or
                    ContentCacheError.CorruptManifest))
                return Fail(ContentActivationAcquisitionStatus.CacheFailure, progress, out result,
                    cacheError: manifestOpenError);

            bool installedFromSource = false;
            bool anySupported = false;
            int packageSourceAttempts = 0;
            ContentSourceFetchError lastSourceError = ContentSourceFetchError.UnsupportedSource;
            foreach (ContentSourceDescriptor candidate in acquisition.Sources)
            {
                if (packageSourceAttempts == limits.MaximumSourceAttemptsPerPackage) break;
                bool granular = source is IContentPackageFileSource files && files.CanFetchFiles(in candidate);
                if (!granular && !source.CanFetch(candidate.Kind)) continue;
                anySupported = true;
                packageSourceAttempts++;
                progress.SourceAttempts++;
                progress.Selected = candidate;

                bool success;
                ContentCacheError installError;
                if (granular)
                {
                    success = TryAcquireGranular(cache, plan.Realm, requirement, candidate,
                        (IContentPackageFileSource)source, cachedManifest, limits,
                        progress, out installError, out lastSourceError);
                }
                else
                {
                    success = TryAcquireArchive(cache, plan.Realm, requirement, candidate, source,
                        progress, out installError, out lastSourceError);
                }
                if (success)
                {
                    installedFromSource = true;
                    progress.PackagesFetched++;
                    break;
                }
                if (installError != ContentCacheError.None)
                    return Fail(ContentActivationAcquisitionStatus.InstallFailure, progress, out result,
                        cacheError: installError, sourceError: lastSourceError);
                progress.SourcesRejected++;
            }

            if (!installedFromSource)
            {
                if (requirement.Requirement == ContentRequirementPolicy.Optional)
                {
                    progress.OptionalDeferred++;
                    continue;
                }
                ContentActivationAcquisitionStatus failureStatus = !anySupported
                    ? ContentActivationAcquisitionStatus.SourceUnavailable
                    : lastSourceError == ContentSourceFetchError.CandidateMismatch
                        ? ContentActivationAcquisitionStatus.CandidateMismatch
                        : ContentActivationAcquisitionStatus.SourceFailure;
                return Fail(failureStatus,
                    progress, out result, sourceError: lastSourceError);
            }
        }

        if (!cache.TryPublishCatalog(baseCatalog, plan, persistentlyPinned,
                out lease, out ContentCacheError catalogError))
            return Fail(ContentActivationAcquisitionStatus.CatalogFailure, progress, out result,
                cacheError: catalogError);
        result = progress.ToResult(ContentActivationAcquisitionStatus.Applied,
            ContentCacheError.None, ContentSourceFetchError.None);
        return true;
    }

    private static bool TryAcquireArchive(
        ContentCache cache,
        ApplicationRealmId realm,
        ContentActivationPackage requirement,
        ContentSourceDescriptor candidate,
        IContentPackageSource source,
        AcquisitionProgress progress,
        out ContentCacheError installError,
        out ContentSourceFetchError sourceError)
    {
        installError = ContentCacheError.None;
        var request = new ContentPackageFetchRequest(realm, requirement.PackageId,
            requirement.ManifestHash, candidate);
        try
        {
            if (!source.TryFetch(in request, out FetchedContentPackage? downloaded, out sourceError))
                return false;
            if (downloaded is null || downloaded.Manifest.PackageId != requirement.PackageId ||
                downloaded.Manifest.ManifestHash != requirement.ManifestHash)
            {
                sourceError = ContentSourceFetchError.CandidateMismatch;
                return false;
            }
            long bytes = 0;
            foreach (ContentFilePayload payload in downloaded.Payloads)
                bytes = checked(bytes + payload.Bytes.Length);
            if (!cache.TryInstallPackage(realm, downloaded.Manifest, downloaded.Payloads,
                    out _, out installError))
            {
                if (installError == ContentCacheError.PayloadValidationFailed)
                {
                    installError = ContentCacheError.None;
                    sourceError = ContentSourceFetchError.CandidateMismatch;
                }
                return false;
            }
            progress.FilesFetched += downloaded.Payloads.Count;
            progress.BytesFetched = checked(progress.BytesFetched + bytes);
            sourceError = ContentSourceFetchError.None;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            sourceError = ContentSourceFetchError.SourceFailure;
            return false;
        }
    }

    private static bool TryAcquireGranular(
        ContentCache cache,
        ApplicationRealmId realm,
        ContentActivationPackage requirement,
        ContentSourceDescriptor candidate,
        IContentPackageFileSource source,
        ContentPackageManifest? cachedManifest,
        ContentActivationAcquirerLimits limits,
        AcquisitionProgress progress,
        out ContentCacheError installError,
        out ContentSourceFetchError sourceError)
    {
        installError = ContentCacheError.None;
        ContentPackageManifest? manifest = cachedManifest;
        try
        {
            if (manifest is null)
            {
                var manifestRequest = new ContentPackageManifestFetchRequest(
                    realm, requirement.PackageId, requirement.ManifestHash, candidate);
                if (!source.TryFetchManifest(in manifestRequest, out manifest, out sourceError)) return false;
                if (manifest is null || manifest.PackageId != requirement.PackageId ||
                    manifest.ManifestHash != requirement.ManifestHash)
                {
                    sourceError = ContentSourceFetchError.CandidateMismatch;
                    return false;
                }
            }
            if (!cache.TryPlanPackageInstall(realm, manifest,
                    out ContentPackageInstallPlan? installPlan, out installError))
            {
                sourceError = ContentSourceFetchError.None;
                return false;
            }
            ContentFileDescriptor[] needed = installPlan!.RequiredPayloads.ToArray();
            var payloads = new ContentFilePayload[needed.Length];
            var errors = new ContentSourceFetchError[needed.Length];
            int active = 0, peak = 0;
            Parallel.For(0, needed.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = limits.MaximumConcurrentFileFetches,
            }, index =>
            {
                int now = Interlocked.Increment(ref active);
                UpdateMaximum(ref peak, now);
                try
                {
                    var fileRequest = new ContentFileFetchRequest(
                        realm, requirement.PackageId, requirement.ManifestHash, needed[index], candidate);
                    if (!source.TryFetchFile(in fileRequest, out ContentFilePayload payload,
                            out ContentSourceFetchError fetchError) ||
                        payload.Path != needed[index].Path ||
                        !ContentPackageValidator.TryValidatePayload(needed[index], payload.Bytes.Span))
                    {
                        errors[index] = fetchError == ContentSourceFetchError.None
                            ? ContentSourceFetchError.CandidateMismatch
                            : fetchError;
                        return;
                    }
                    payloads[index] = payload;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    errors[index] = ContentSourceFetchError.SourceFailure;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
            progress.PeakConcurrent = Math.Max(progress.PeakConcurrent, peak);
            ContentSourceFetchError firstError = errors.FirstOrDefault(static value =>
                value != ContentSourceFetchError.None);
            if (firstError != ContentSourceFetchError.None)
            {
                sourceError = firstError;
                return false;
            }
            if (!cache.TryInstallPackageDelta(realm, manifest, payloads, out _, out installError))
            {
                sourceError = ContentSourceFetchError.None;
                return false;
            }
            progress.FilesReused += installPlan.ReusedBlobCount;
            progress.FilesFetched += payloads.Length;
            foreach (ContentFilePayload payload in payloads)
                progress.BytesFetched = checked(progress.BytesFetched + payload.Bytes.Length);
            sourceError = ContentSourceFetchError.None;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            sourceError = ContentSourceFetchError.SourceFailure;
            return false;
        }
    }

    private static bool Fail(
        ContentActivationAcquisitionStatus status,
        AcquisitionProgress progress,
        out ContentActivationAcquisitionResult result,
        ContentCacheError cacheError = ContentCacheError.None,
        ContentSourceFetchError sourceError = ContentSourceFetchError.None)
    {
        result = progress.ToResult(status, cacheError, sourceError);
        return false;
    }

    private static bool Fail(
        ContentActivationAcquisitionStatus status,
        out ContentActivationAcquisitionResult result)
    {
        result = new ContentActivationAcquisitionResult(
            status, ContentCacheError.None, ContentSourceFetchError.None,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, default);
        return false;
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            if (current >= value || Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }

    private sealed class AcquisitionProgress
    {
        internal int Required;
        internal int PackagesReused;
        internal int PackagesFetched;
        internal int FilesReused;
        internal int FilesFetched;
        internal long BytesFetched;
        internal int SourceAttempts;
        internal int SourcesRejected;
        internal int OptionalDeferred;
        internal int PeakConcurrent;
        internal ContentSourceDescriptor Selected;

        internal ContentActivationAcquisitionResult ToResult(
            ContentActivationAcquisitionStatus status,
            ContentCacheError cacheError,
            ContentSourceFetchError sourceError) => new(
                status, cacheError, sourceError, Required, PackagesReused, PackagesFetched,
                FilesReused, FilesFetched, BytesFetched, SourceAttempts, SourcesRejected,
                OptionalDeferred, PeakConcurrent, Selected);
    }
}
