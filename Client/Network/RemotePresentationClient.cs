using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public readonly record struct RemotePresentationClientSnapshot(
    ulong ApplicationSessionEpoch,
    ulong AdvertisementSequence,
    ContentHash PlanHash,
    bool Acquiring,
    bool RealmCatalogPublished,
    PresentationGenerationSnapshot Generation,
    ContentActivationAcquisitionResult Acquisition,
    ulong RejectedAdvertisements,
    ulong StaleCompletions,
    string? Detail);

/// <summary>
/// Authenticated but non-gating Realm presentation acquisition. Required semantic application
/// owns its own plan/proof; a missing or corrupt actor layer only changes presentation fallback.
/// </summary>
public sealed class RemotePresentationClient : IRemoteGameProtocolHandler,
    IRemoteGameProtocolProgressor, IActorPresentationGenerationSource, IWorldPresentationGenerationSource, IDisposable
{
    private readonly ulong _epoch;
    private readonly ApplicationRealmId _realm;
    private readonly ContentCache _cache;
    private readonly IContentPackageSource _source;
    private readonly RemoteContentClient _semantic;
    private readonly ContentScopeActivationKey? _localKey;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private PresentationGeneration _generation;
    private ContentScopeActivationKey? _realmPresentationKey;
    private ulong _userSelectionRevision;
    private ContentActivationPlan? _pendingPlan;
    private ContentPackageAcquisition[]? _pendingSources;
    private CancellationTokenSource? _cancellation;
    private Task<Completion>? _task;
    private ulong _advertisementSequence;
    private ContentHash _planHash;
    private ContentHash _expectedFingerprint;
    private ulong _publicationRevision;
    private ulong _rejected;
    private ulong _stale;
    private bool _realmPublished;
    private ContentActivationAcquisitionResult _acquisition;
    private string? _detail;
    private bool _disposed;

    public RemotePresentationClient(
        ulong applicationSessionEpoch,
        ApplicationRealmId realm,
        ContentCache cache,
        IContentPackageSource source,
        RemoteContentClient semantic,
        ContentScopeActivationKey? localPresentationKey = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        if (localPresentationKey is { } key && (!key.IsValid || !key.Identity.Scope.IsLocal))
            throw new ArgumentException("The fallback catalog must belong to world.local.",
                nameof(localPresentationKey));
        _epoch = applicationSessionEpoch;
        _realm = realm;
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _semantic = semantic ?? throw new ArgumentNullException(nameof(semantic));
        _localKey = localPresentationKey;
        _generation = CreateInitialGeneration();
        _userSelectionRevision = _cache.UserResourcePackSelectionRevision;
    }

    public PresentationGenerationId CurrentPresentationGeneration
    {
        get { EnsureOperational(); return _generation.Generation; }
    }

    public PresentationGenerationSnapshot CurrentGeneration
    {
        get { EnsureOperational(); return _generation.Snapshot; }
    }

    public bool TryResolveActorPresentation(
        LogicalContentId definitionId,
        ActorPresentationResolutionRequirements requirements,
        out ActorPresentationContentLease? lease,
        out ContentActorPresentationResolutionError error,
        ActorPresentationArtifactLimits limits = default)
    {
        EnsureOperational();
        return _generation.TryResolveActorPresentation(
            definitionId, requirements, out lease, out error, limits);
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (message.MessageId != RemotePresentationAdvertisementHeaderCodec.Descriptor.Id)
            return false;
        if (!UltraSimGameProtocol.TryReadPresentationAdvertisement(
                payload, out RemotePresentationAdvertisement advertisement) ||
            advertisement.ApplicationSessionEpoch != _epoch ||
            advertisement.Authorization.RealmHigh != _realm.High ||
            advertisement.Authorization.RealmLow != _realm.Low ||
            !RemoteContentAuthorizationProjection.TryProject(
                advertisement.Authorization, out ContentActivationPlan? plan,
                out ContentPackageAcquisition[] sources) ||
            plan!.RequiredBaseCatalogFingerprint != ContentBaseCatalog.Empty.Fingerprint)
        {
            Increment(ref _rejected);
            return true;
        }
        if (_advertisementSequence != 0)
        {
            if (_advertisementSequence != advertisement.AdvertisementSequence ||
                _planHash != plan.PlanHash ||
                _expectedFingerprint != RemoteContentAuthorizationProjection.ToContent(
                    advertisement.Authorization.ExpectedResolvedCatalogFingerprint))
                Increment(ref _rejected);
            return true;
        }
        _advertisementSequence = advertisement.AdvertisementSequence;
        _planHash = plan.PlanHash;
        _expectedFingerprint = RemoteContentAuthorizationProjection.ToContent(
            advertisement.Authorization.ExpectedResolvedCatalogFingerprint);
        _pendingPlan = plan;
        _pendingSources = sources;
        _detail = "Realm actor presentation is pending; semantic application remains independent.";
        return true;
    }

    public bool TryProgress()
    {
        EnsureOperational();
        CompleteAcquisition();
        RefreshUserSelectionIfChanged();
        if (_task is null && _pendingPlan is not null && _semantic.Applied)
        {
            ContentActivationPlan plan = _pendingPlan;
            ContentPackageAcquisition[] sources = _pendingSources!;
            _pendingPlan = null;
            _pendingSources = null;
            _cancellation = new CancellationTokenSource();
            CancellationToken cancellation = _cancellation.Token;
            ulong revision = _publicationRevision;
            ContentHash planHash = _planHash;
            _task = Task.Run(() =>
            {
                bool published = ContentActivationAcquirer.TryAcquireAndPublish(
                    _cache, ContentBaseCatalog.Empty, plan, sources, _source,
                    persistentlyPinned: false, out ContentCatalogLease? catalog,
                    out ContentActivationAcquisitionResult result,
                    cancellationToken: cancellation);
                return new Completion(_epoch, revision, planHash, published, catalog, result);
            });
        }
        return true;
    }

    /// <summary>Invalidates an in-flight candidate without changing semantic application truth.</summary>
    public void ResetToLocalFallback()
    {
        EnsureOperational();
        _publicationRevision++;
        _cancellation?.Cancel();
        _realmPresentationKey = null;
        Publish(null);
    }

    public RemotePresentationClientSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new RemotePresentationClientSnapshot(
            _epoch, _advertisementSequence, _planHash, _task is not null,
            _realmPublished, _generation.Snapshot, _acquisition, _rejected, _stale, _detail);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _cancellation?.Cancel();
        if (_task is not null)
        {
            try { _task.GetAwaiter().GetResult().Catalog?.Dispose(); }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // No application state can be published after this owner is retired.
            }
        }
        _task = null;
        _cancellation?.Dispose();
        _generation.Dispose();
    }

    private ContentCatalogLease? OpenLocal()
    {
        if (_localKey is not { } key) return null;
        return _cache.TryOpenCatalog(ContentBaseCatalog.Empty, key, out ContentCatalogLease? lease,
            out _) ? lease : null;
    }

    private ContentCatalogLease? OpenRealm()
    {
        if (_realmPresentationKey is not { } key) return null;
        return _cache.TryOpenCatalog(
            ContentBaseCatalog.Empty, key, out ContentCatalogLease? lease, out _) ? lease : null;
    }

    private UserResourcePackStackLease? OpenUser() =>
        _cache.TryOpenEnabledUserResourcePacks(
            out UserResourcePackStackLease? lease, out _) ? lease : null;

    public PresentationSourceKind GetSource(LogicalContentId id)
    {
        EnsureOperational();
        return _generation.GetSource(id);
    }

    public bool TryResolveMaterialPresentation(LogicalContentId id,
        out ResolvedMaterialPresentation? presentation, out PresentationResourceResolutionError error)
    {
        EnsureOperational();
        return _generation.TryResolveMaterialPresentation(id, out presentation, out error);
    }

    private ContentCatalogLease? OpenLocalResources() =>
        _cache.TryOpenCatalog(ContentBaseCatalog.Empty, ClientWorldPresentationProfile.LocalDefault,
            out ContentCatalogLease? catalog, out _) ? catalog : null;

    private PresentationGeneration CreateInitialGeneration()
    {
        UserResourcePackStackLease? user = OpenUser();
        ContentCatalogLease? local = OpenLocal();
        try { return PresentationGeneration.CreateWithResources(user, null, local, null, OpenLocalResources()); }
        catch (InvalidDataException)
        {
            // A corrupt/incompatible selected presentation must not make semantic entry fail.
            return CreateFallbackGeneration(includeRealm: false);
        }
    }

    private void Publish(ContentCatalogLease? realm)
    {
        PresentationGeneration next;
        try { next = PresentationGeneration.CreateWithResources(OpenUser(), realm, OpenLocal(),
            OpenRealm(), OpenLocalResources()); }
        catch (InvalidDataException)
        {
            // The candidate is atomic. Reopen the lower layers after the failed constructor
            // retired their leases, and publish the valid authenticated fallback composition.
            next = CreateFallbackGeneration(includeRealm: true);
        }
        catch
        {
            realm?.Dispose();
            throw;
        }
        PresentationGeneration previous = _generation;
        _generation = next;
        _realmPublished = realm is not null;
        previous.Dispose();
        _userSelectionRevision = _cache.UserResourcePackSelectionRevision;
    }

    private PresentationGeneration CreateFallbackGeneration(bool includeRealm)
    {
        try
        {
            return PresentationGeneration.CreateWithResources(null,
                includeRealm ? OpenRealm() : null, OpenLocal(),
                includeRealm ? OpenRealm() : null, OpenLocalResources());
        }
        catch (InvalidDataException)
        {
            // Invalid optional world resources cannot prevent the existing actor fallback.
            return PresentationGeneration.Create(null, includeRealm ? OpenRealm() : null, OpenLocal());
        }
    }

    private void RefreshUserSelectionIfChanged()
    {
        ulong revision = _cache.UserResourcePackSelectionRevision;
        if (revision == _userSelectionRevision) return;
        _userSelectionRevision = revision;
        PresentationGeneration? candidate = null;
        try
        {
            candidate = PresentationGeneration.CreateWithResources(OpenUser(), OpenRealm(), OpenLocal(),
                OpenRealm(), OpenLocalResources());
            if (candidate.StableFingerprint == _generation.StableFingerprint)
            {
                candidate.Dispose();
                return;
            }
            PresentationGeneration previous = _generation;
            _generation = candidate;
            candidate = null;
            previous.Dispose();
        }
        catch (InvalidDataException)
        {
            // Keep the previously published generation intact.
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    private void CompleteAcquisition()
    {
        Task<Completion>? task = _task;
        if (task is null || !task.IsCompleted) return;
        _task = null;
        _cancellation?.Dispose();
        _cancellation = null;
        Completion completion;
        try { completion = task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _detail = $"Optional Realm presentation failed: {exception.GetType().Name}.";
            return;
        }
        if (completion.Epoch != _epoch || completion.Revision != _publicationRevision ||
            completion.PlanHash != _planHash)
        {
            completion.Catalog?.Dispose();
            Increment(ref _stale);
            return;
        }
        _acquisition = completion.Result;
        if (!completion.Published || completion.Catalog is null ||
            completion.Catalog.Catalog.Fingerprint != _expectedFingerprint ||
            completion.Catalog.Catalog.Entries.Any(static entry =>
                entry.Kind is not (ContentDefinitionKind.ActorPresentation or ContentDefinitionKind.Texture or
                    ContentDefinitionKind.Material)))
        {
            completion.Catalog?.Dispose();
            _detail = "Optional Realm presentation was unavailable or incompatible.";
            return;
        }
        _realmPresentationKey = new ContentScopeActivationKey(
            new ContentScopeActivationId(
                completion.Catalog.Catalog.Scope, completion.Catalog.Catalog.Activation),
            completion.Catalog.Catalog.PlanHash);
        Publish(completion.Catalog);
        _detail = null;
    }

    private static void Increment(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Presentation publication is owner-thread-bound.");
    }

    private readonly record struct Completion(
        ulong Epoch,
        ulong Revision,
        ContentHash PlanHash,
        bool Published,
        ContentCatalogLease? Catalog,
        ContentActivationAcquisitionResult Result);
}
