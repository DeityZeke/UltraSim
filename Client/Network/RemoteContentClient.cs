using System.Buffers.Binary;
using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client.Network;

public enum RemoteContentClientState : byte
{
    AwaitingRequirement = 1,
    Applied = 2,
    Rejected = 3,
}

public readonly record struct RemoteContentClientSnapshot(
    RemoteContentClientState State,
    ulong ApplicationSessionEpoch,
    ulong RequirementSequence,
    ContentActivationId Activation,
    ContentHash PlanHash,
    ContentHash ResolvedCatalogFingerprint,
    bool ProofRetained,
    ulong ProofsQueued,
    ulong DuplicateRequirements,
    ulong RejectedMessages,
    ContentActivationAcquisitionResult Acquisition,
    string? Detail);

public sealed class RemoteRequiredContentBinding
{
    public RemoteRequiredContentBinding(
        ApplicationRealmId realm,
        ContentCache cache,
        ContentBaseCatalog baseCatalog,
        IContentPackageSource source)
    {
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        Realm = realm;
        Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        BaseCatalog = baseCatalog ?? throw new ArgumentNullException(nameof(baseCatalog));
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ApplicationRealmId Realm { get; }
    public ContentCache Cache { get; }
    public ContentBaseCatalog BaseCatalog { get; }
    public IContentPackageSource Source { get; }
}

/// <summary>Maps engine-neutral content identities to the Game-owned authenticated wire projection.</summary>
public static class RemoteContentAuthorizationProjection
{
    public static RemoteContentAuthorization Create(
        ContentActivationPlan plan,
        ContentHash expectedResolvedCatalogFingerprint,
        IEnumerable<ContentPackageAcquisition> acquisitions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(acquisitions);
        ContentPackageAcquisition[] sourcePlans = acquisitions
            .Take(plan.Packages.Count + 1).ToArray();
        if (sourcePlans.Length != plan.Packages.Count)
            throw new ArgumentException("Every authorized package must have one acquisition descriptor.", nameof(acquisitions));
        var packages = new RemoteContentPackageRequirement[plan.Packages.Count];
        for (int index = 0; index < packages.Length; index++)
        {
            ContentActivationPackage requirement = plan.Packages[index];
            ContentPackageAcquisition? acquisition = sourcePlans.FirstOrDefault(value =>
                value.Package == requirement.PackageId && value.ManifestHash == requirement.ManifestHash);
            if (acquisition is null)
                throw new ArgumentException("An acquisition descriptor does not match the authorized plan.",
                    nameof(acquisitions));
            packages[index] = new RemoteContentPackageRequirement(
                requirement.PackageId.Value!, ToRemote(requirement.ManifestHash),
                (RemoteContentRequirementPolicy)requirement.Requirement,
                acquisition.Sources.Select(static value => new RemoteContentSource(
                    (RemoteContentSourceKind)value.Kind, value.Locator)));
        }
        return new RemoteContentAuthorization(
            plan.Realm.High, plan.Realm.Low, plan.Activation.High, plan.Activation.Low,
            ToRemote(plan.RequiredBaseCatalogFingerprint), ToRemote(plan.PlanHash),
            ToRemote(expectedResolvedCatalogFingerprint), packages);
    }

    public static RemoteContentHash ToRemote(ContentHash hash) =>
        new(hash.Part0, hash.Part1, hash.Part2, hash.Part3);

    internal static ContentHash ToContent(RemoteContentHash hash)
    {
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        BinaryPrimitives.WriteUInt64BigEndian(digest, hash.Part0);
        BinaryPrimitives.WriteUInt64BigEndian(digest[8..], hash.Part1);
        BinaryPrimitives.WriteUInt64BigEndian(digest[16..], hash.Part2);
        BinaryPrimitives.WriteUInt64BigEndian(digest[24..], hash.Part3);
        return ContentHash.FromDigest(digest);
    }
}

/// <summary>
/// One application-session content consumer. Cache/source success is private client state until
/// the exact immutable catalog publishes and its retained semantic proof crosses Control.
/// </summary>
public sealed class RemoteContentClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor, IDisposable
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly ApplicationRealmId _realm;
    private readonly ContentCache _cache;
    private readonly ContentBaseCatalog _baseCatalog;
    private readonly IContentPackageSource _source;
    private readonly byte[] _proofScratch = new byte[RemoteContentSetAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RemoteContentClientState _state = RemoteContentClientState.AwaitingRequirement;
    private ulong _requirementSequence;
    private ContentActivationId _activation;
    private ContentHash _planHash;
    private ContentHash _resolvedFingerprint;
    private RemoteContentSetAppliedMessage _pendingProof;
    private bool _hasPendingProof;
    private ulong _proofsQueued;
    private ulong _duplicateRequirements;
    private ulong _rejectedMessages;
    private ContentActivationAcquisitionResult _acquisition;
    private ContentCatalogLease? _catalog;
    private string? _detail;
    private bool _disposed;

    public RemoteContentClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ApplicationRealmId realm,
        ContentCache cache,
        ContentBaseCatalog baseCatalog,
        IContentPackageSource source)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _baseCatalog = baseCatalog ?? throw new ArgumentNullException(nameof(baseCatalog));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _applicationSessionEpoch = applicationSessionEpoch;
        _realm = realm;
    }

    public bool Applied
    {
        get { EnsureOperational(); return _state == RemoteContentClientState.Applied; }
    }

    public ResolvedContentCatalog Catalog
    {
        get
        {
            EnsureOperational();
            return _catalog?.Catalog ??
                throw new InvalidOperationException("The required remote content catalog is not applied.");
        }
    }

    /// <summary>
    /// Resolves an exact passive UI definition from this application session's applied realm
    /// catalog. Possessing a blob outside that selected catalog is deliberately insufficient.
    /// </summary>
    public bool TryResolveUiDefinition(
        UiDefinitionCacheKey cacheKey,
        out CompiledUiDefinition? definition,
        out ContentUiDefinitionResolutionError error)
    {
        EnsureOperational();
        definition = null;
        error = ContentUiDefinitionResolutionError.None;
        if (_state != RemoteContentClientState.Applied || _catalog is null)
        {
            error = ContentUiDefinitionResolutionError.InvalidRequest;
            return false;
        }
        return ContentUiDefinitionResolver.TryResolve(
            _catalog,
            new UiDefinitionIdentity(
                cacheKey.DefinitionKey,
                cacheKey.DefinitionVersion,
                cacheKey.DefinitionHash),
            out definition,
            out error);
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (message.MessageId != RemoteContentRequirementHeaderCodec.Descriptor.Id) return false;
        ProcessRequirement(payload);
        return true;
    }

    public bool TryProgress()
    {
        EnsureOperational();
        return FlushProof();
    }

    public RemoteContentClientSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new RemoteContentClientSnapshot(
            _state, _applicationSessionEpoch, _requirementSequence, _activation, _planHash,
            _resolvedFingerprint, _hasPendingProof, _proofsQueued, _duplicateRequirements,
            _rejectedMessages, _acquisition, _detail);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _catalog?.Dispose();
        _catalog = null;
        Array.Clear(_proofScratch);
    }

    private void ProcessRequirement(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadContentRequirement(payload, out RemoteContentRequirement remote) ||
            remote.ApplicationSessionEpoch != _applicationSessionEpoch ||
            remote.Authorization.RealmHigh != _realm.High || remote.Authorization.RealmLow != _realm.Low ||
            !TryProject(remote.Authorization, out ContentActivationPlan? plan,
                out ContentPackageAcquisition[] acquisitions))
        {
            SaturatingIncrement(ref _rejectedMessages);
            return;
        }

        if (_requirementSequence != 0)
        {
            if (remote.RequirementSequence != _requirementSequence ||
                plan!.Activation != _activation || plan.PlanHash != _planHash)
            {
                SaturatingIncrement(ref _rejectedMessages);
                return;
            }
            SaturatingIncrement(ref _duplicateRequirements);
            QueueProof(_state == RemoteContentClientState.Applied
                ? RemoteContentApplyStatus.Applied
                : RemoteContentApplyStatus.Rejected);
            return;
        }

        _requirementSequence = remote.RequirementSequence;
        _activation = plan!.Activation;
        _planHash = plan.PlanHash;
        if (!ContentActivationAcquirer.TryAcquireAndPublish(
                _cache, _baseCatalog, plan, acquisitions, _source,
                persistentlyPinned: false, out ContentCatalogLease? catalog, out _acquisition))
        {
            _state = RemoteContentClientState.Rejected;
            _detail = $"Required content could not be applied: {_acquisition.Status} " +
                $"(cache={_acquisition.CacheError}, source={_acquisition.SourceError}).";
            QueueProof(RemoteContentApplyStatus.Rejected);
            return;
        }
        _catalog = catalog;
        _resolvedFingerprint = catalog!.Catalog.Fingerprint;
        _state = RemoteContentClientState.Applied;
        _detail = null;
        QueueProof(RemoteContentApplyStatus.Applied);
    }

    private bool TryProject(
        RemoteContentAuthorization remote,
        out ContentActivationPlan? plan,
        out ContentPackageAcquisition[] acquisitions)
    {
        plan = null;
        acquisitions = [];
        try
        {
            var realm = new ApplicationRealmId(remote.RealmHigh, remote.RealmLow);
            var activation = new ContentActivationId(remote.ActivationHigh, remote.ActivationLow);
            ContentHash baseHash = RemoteContentAuthorizationProjection.ToContent(
                remote.RequiredBaseCatalogFingerprint);
            ContentHash advertisedPlanHash = RemoteContentAuthorizationProjection.ToContent(remote.PlanHash);
            var packages = new ContentActivationPackage[remote.Packages.Count];
            acquisitions = new ContentPackageAcquisition[remote.Packages.Count];
            for (int index = 0; index < packages.Length; index++)
            {
                RemoteContentPackageRequirement package = remote.Packages[index];
                if (!ContentPackageId.TryCreate(package.PackageId, out ContentPackageId packageId))
                    return false;
                ContentHash manifestHash = RemoteContentAuthorizationProjection.ToContent(package.ManifestHash);
                packages[index] = new ContentActivationPackage(
                    packageId, manifestHash, (ContentRequirementPolicy)package.Policy);
                acquisitions[index] = new ContentPackageAcquisition(
                    packageId, manifestHash,
                    package.Sources.Select(static value => new ContentSourceDescriptor(
                        (ContentSourceKind)value.Kind, value.Locator)));
            }
            if (!ContentActivationPlan.TryCreate(
                    realm, activation, baseHash, packages, out plan, out _) ||
                plan!.PlanHash != advertisedPlanHash)
            {
                plan = null;
                acquisitions = [];
                return false;
            }
            return true;
        }
        catch (ArgumentException)
        {
            plan = null;
            acquisitions = [];
            return false;
        }
    }

    private void QueueProof(RemoteContentApplyStatus status)
    {
        RemoteContentHash plan = RemoteContentAuthorizationProjection.ToRemote(_planHash);
        RemoteContentHash resolved = _resolvedFingerprint.IsValid
            ? RemoteContentAuthorizationProjection.ToRemote(_resolvedFingerprint)
            : default;
        _pendingProof = new RemoteContentSetAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            RequirementSequence = _requirementSequence,
            ApplicationRealmHigh = _realm.High,
            ApplicationRealmLow = _realm.Low,
            ActivationHigh = _activation.High,
            ActivationLow = _activation.Low,
            PlanHash0 = plan.Part0,
            PlanHash1 = plan.Part1,
            PlanHash2 = plan.Part2,
            PlanHash3 = plan.Part3,
            ResolvedHash0 = resolved.Part0,
            ResolvedHash1 = resolved.Part1,
            ResolvedHash2 = resolved.Part2,
            ResolvedHash3 = resolved.Part3,
            Status = status,
        };
        if (!UltraSimGameProtocol.IsValidContentApplied(in _pendingProof))
            throw new InvalidOperationException("The content application proof became invalid.");
        _hasPendingProof = true;
        _ = FlushProof();
    }

    private bool FlushProof()
    {
        if (!_hasPendingProof) return true;
        if (!RemoteContentSetAppliedMessageCodec.TryWriteExact(_proofScratch, in _pendingProof))
            throw new InvalidOperationException("The content application proof did not encode.");
        if (_peer.TryQueue(RemoteContentSetAppliedMessageCodec.Descriptor.Id, _proofScratch) !=
            NetworkQueueStatus.Accepted)
            return false;
        _hasPendingProof = false;
        SaturatingIncrement(ref _proofsQueued);
        return true;
    }

    private static void SaturatingIncrement(ref ulong value)
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
            throw new InvalidOperationException("Remote content application is owner-thread-bound.");
    }
}
