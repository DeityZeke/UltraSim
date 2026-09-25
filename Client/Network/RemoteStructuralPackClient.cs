using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Client.Network;

public enum RemoteStructuralPackClientState : byte
{
    None = 0,
    RequestPending = 1,
    AwaitingGrant = 2,
    Acquiring = 3,
    ProofPending = 4,
    Applied = 5,
    Rejected = 6,
    Retired = 7,
}

public readonly record struct RemoteStructuralPackClientSnapshot(
    ulong RequestsQueued,
    ulong GrantsAccepted,
    ulong ProofsQueued,
    ulong RejectedGrants,
    ulong Acquisitions,
    long BytesFetched,
    int PreparedPackCount,
    int SemanticResidentChunks,
    bool InitialReady,
    int PendingRequests,
    ContentWorldPackResolutionResult LastResolution)
{
    public ulong RetirementsQueued { get; init; }
}


/// <summary>
/// Session-local structural Pack orchestration. Authenticated Control grants authorize exact
/// passive bytes, Content installs them without catalog activation, and World publishes only a
/// validated prepared-subset candidate. Every prepared Pack keeps its own recipient proof.
/// </summary>
public sealed class RemoteStructuralPackClient :
    IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor, IDisposable
{
    private const int MaximumPendingPacks = 8;
    private readonly NetworkProtocolPeer _peer;
    private readonly RemoteStructuralManifestClient _manifest;
    private readonly ClientWorldSemanticCache _cache;
    private readonly ContentCache _contentCache;
    private readonly ContentScopeKey _scope;
    private readonly IContentPackageSource _source;
    private readonly WorldContentBinding _binding;
    private readonly ulong _applicationSessionEpoch;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly PendingPack?[] _pending = new PendingPack[MaximumPendingPacks];
    private ClientStructuralResidency? _residency;
    private ulong _nextRequestSequence = 1;
    private ulong _requestsQueued;
    private ulong _grantsAccepted;
    private ulong _proofsQueued;
    private ulong _windowProofsQueued;
    private ulong _retirementsQueued;
    private ulong _rejectedGrants;
    private ulong _acquisitions;
    private long _bytesFetched;
    private ContentWorldPackResolutionResult _lastResolution;
    private bool _initialReady;
    private bool _initialProofQueued;
    private bool _initialWindowProofQueued;
    private bool _windowProofPending;
    private SpatialChunkKey _residentCenter;
    private SpatialChunkKey _lastProvenWindowCenter;
    private ulong _lastProvenWindowMask;
    private ulong _nextWindowSequence = 1;
    private PendingPack? _pendingRetirement;
    private bool _disposed;

    public RemoteStructuralPackClient(
        NetworkProtocolPeer peer,
        RemoteStructuralManifestClient manifest,
        ClientWorldSemanticCache cache,
        ContentCache contentCache,
        ContentScopeKey scope,
        IContentPackageSource source,
        WorldContentBinding binding,
        ulong applicationSessionEpoch)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _contentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        if (!scope.IsRealm || applicationSessionEpoch == 0 || !_manifest.Applied ||
            _manifest.Composition is null ||
            !ReferenceEquals(cache.PreparedComposition, _manifest.Composition))
            throw new ArgumentException("An applied realm manifest and its zero-Pack cache are required.");
        _scope = scope;
        _applicationSessionEpoch = applicationSessionEpoch;
    }

    public bool InitialReady
    {
        get { EnsureOperational(); return _initialReady; }
    }

    public bool InitialProofQueued
    {
        get { EnsureOperational(); return _initialProofQueued; }
    }

    public bool InitialWindowProofQueued
    {
        get { EnsureOperational(); return _initialWindowProofQueued; }
    }

    public ClientStructuralResidency? Residency
    {
        get { EnsureOperational(); return _residency; }
    }

    public ulong WindowProofsQueued
    {
        get { EnsureOperational(); return _windowProofsQueued; }
    }

    /// <summary>Stages a bounded semantic neighborhood; Pack acquisition remains an outer concern.</summary>
    public bool TryPreparePresentation(
        in WorldPosition player,
        out SpatialChunkKey center,
        out ClientStructuralResidencyPlan plan)
    {
        EnsureOperational();
        center = default;
        plan = default;
        if (!_initialReady || _residency is null) return false;
        RequestMissingPacksAround(player.ToChunkKey(_cache.StructuralSpace));
        center = _residency.GetPresentationCenter(player);
        return _residency.TryPrepare(center, reset: false, out plan, out _, out _);
    }

    public void CommitPresentation(in ClientStructuralResidencyPlan plan, SpatialChunkKey center)
    {
        EnsureOperational();
        if (_residency is null) throw new InvalidOperationException("No structural residency exists.");
        _residency.Commit(plan);
        _residentCenter = center;
        ulong mask = CreateResidentMask(center);
        if (center != _lastProvenWindowCenter || mask != _lastProvenWindowMask)
            _windowProofPending = true;
    }

    public void CancelPresentation(in ClientStructuralResidencyPlan plan)
    {
        EnsureOperational();
        _residency?.Cancel(plan);
    }

    public bool TryRequest(WorldPackCoord coord)
    {
        EnsureOperational();
        PreparedWorldComposition composition = _cache.PreparedComposition!;
        if (_pendingRetirement?.Coord == coord) return false;
        if (!composition.Manifest.TryGetPack(coord, out _) ||
            composition.TryGetPreparedPack(coord, out _) ||
            _nextRequestSequence == 0)
            return false;
        for (int index = 0; index < _pending.Length; index++)
            if (_pending[index] is { State: not (RemoteStructuralPackClientState.Applied or
                    RemoteStructuralPackClientState.Rejected or
                    RemoteStructuralPackClientState.Retired) } existing && existing.Coord == coord)
                return true;
        int slot = -1;
        for (int index = 0; index < _pending.Length; index++)
            if ((_pendingRetirement is null ||
                 !ReferenceEquals(_pending[index], _pendingRetirement)) &&
                (_pending[index] is null ||
                _pending[index]!.State is RemoteStructuralPackClientState.Applied or
                    RemoteStructuralPackClientState.Rejected or
                    RemoteStructuralPackClientState.Retired))
            {
                slot = index;
                break;
            }
        if (slot < 0) return false;
        _pending[slot]?.Dispose();
        _pending[slot] = new PendingPack(coord, _nextRequestSequence++);
        return true;
    }

    /// <summary>Unprepares only after the semantic-cache borrower guard accepts removal.</summary>
    public bool TryRetirePack(WorldPackCoord coord)
    {
        EnsureOperational();
        if (coord == _manifest.InitialPack || _pendingRetirement is not null ||
            _windowProofPending) return false;
        PreparedWorldComposition previous = _cache.PreparedComposition!;
        if (!previous.TryGetPreparedPack(coord, out _)) return false;
        PendingPack? obligation = null;
        foreach (PendingPack? pending in _pending)
            if (pending is { State: RemoteStructuralPackClientState.Applied } &&
                pending.Coord == coord &&
                (obligation is null || pending.Sequence > obligation.Sequence))
                obligation = pending;
        if (obligation is null) return false;
        var retained = new PreparedWorldPack[previous.PreparedPackCount - 1];
        int count = 0;
        for (int index = 0; index < previous.PreparedPackCount; index++)
        {
            if (!previous.TryGetPreparedPackAt(index, out PreparedWorldPack pack))
                throw new InvalidOperationException("A retained prepared Pack disappeared.");
            if (pack.Coord != coord) retained[count++] = pack;
        }
        if (count != retained.Length ||
            !PreparedWorldComposition.TryCreate(previous.Space, previous.Revision,
                previous.Manifest, retained, out PreparedWorldComposition? candidate, out _) ||
            !candidate!.TryValidatePreparedSubset(out _) ||
            !_cache.TryPublishPreparedComposition(candidate)) return false;
        if (_residency is not null && !_residency.TryRefreshPreparedSource())
        {
            RollBackPreparedCandidate(previous);
            return false;
        }
        obligation.State = RemoteStructuralPackClientState.Retired;
        _pendingRetirement = obligation;
        return true;
    }

    /// <summary>
    /// Retires at most one noninitial Pack outside the current bounded presentation region.
    /// The semantic-cache borrower guard remains the final authority; no Pack is removed while
    /// structural/public handles or a pending window still depend on it.
    /// </summary>
    public bool TryRetireOutsidePresentation(SpatialChunkKey center)
    {
        EnsureOperational();
        if (_pendingRetirement is not null || _windowProofPending ||
            center.Space != _cache.StructuralSpace) return false;
        PreparedWorldComposition composition = _cache.PreparedComposition!;
        for (int index = 0; index < composition.PreparedPackCount; index++)
        {
            if (!composition.TryGetPreparedPackAt(index, out PreparedWorldPack pack) ||
                pack.Coord == _manifest.InitialPack) continue;
            bool near = false;
            for (int dx = -2; dx <= 2 && !near; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                long x = (long)center.Coord.X + dx;
                long z = (long)center.Coord.Z + dz;
                if (x is < int.MinValue or > int.MaxValue ||
                    z is < int.MinValue or > int.MaxValue) continue;
                WorldPackAddressing.ToPackLocal(new SpatialChunkCoord((int)x, (int)z),
                    out WorldPackCoord owner, out _);
                if (owner == pack.Coord)
                {
                    near = true;
                    break;
                }
            }
            if (!near && TryRetirePack(pack.Coord)) return true;
        }
        return false;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (message.MessageId != RemoteStructuralPackGrantHeaderCodec.Descriptor.Id)
            return false;
        if (!UltraSimGameProtocol.TryReadStructuralPackGrant(payload,
                out RemoteStructuralPackGrant? grant) ||
            grant!.Header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            grant.Header.StructuralSequence != _manifest.GetSnapshot().StructuralSequence)
        {
            Increment(ref _rejectedGrants);
            return true;
        }
        RemoteStructuralPackGrantHeader header = grant.Header;
        for (int index = 0; index < _pending.Length; index++)
        {
            PendingPack? pending = _pending[index];
            if (pending is null || pending.Sequence != header.PackRequestSequence) continue;
            if (pending.Coord.X != header.PackX || pending.Coord.Z != header.PackZ ||
                !_cache.PreparedComposition!.Manifest.TryGetPack(pending.Coord,
                    out WorldPackFingerprint expected) ||
                header.PackHash0 != expected.Part0 ||
                header.PackHash1 != expected.Part1 ||
                header.PackHash2 != expected.Part2 ||
                header.PackHash3 != expected.Part3 ||
                !ContentPackageId.TryCreate(grant.PackageId, out _) ||
                pending.State == RemoteStructuralPackClientState.RequestPending)
            {
                Increment(ref _rejectedGrants);
                return true;
            }
            if (pending.Grant is not null)
            {
                if (!GrantsEqual(pending.Grant, grant))
                {
                    // A contradictory duplicate is not authority to replace an already
                    // accepted grant or poison its publication/proof/retirement lifecycle.
                    // The original grant must still finish exact package and World validation.
                    Increment(ref _rejectedGrants);
                }
                return true;
            }
            if (pending.State != RemoteStructuralPackClientState.AwaitingGrant)
            {
                Increment(ref _rejectedGrants);
                return true;
            }
            pending.Grant = grant;
            pending.State = RemoteStructuralPackClientState.Acquiring;
            pending.StartAcquisition(_contentCache, _scope, _source);
            Increment(ref _grantsAccepted);
            return true;
        }
        Increment(ref _rejectedGrants);
        return true;
    }

    public bool TryProgress()
    {
        EnsureOperational();
        bool complete = true;
        if (_pendingRetirement is not null)
            complete &= QueueRetirement();
        for (int index = 0; index < _pending.Length; index++)
        {
            PendingPack? pending = _pending[index];
            if (pending is null) continue;
            if (pending.State == RemoteStructuralPackClientState.RequestPending)
                complete &= QueueRequest(pending);
            if (pending.State == RemoteStructuralPackClientState.Acquiring &&
                pending.Acquisition?.IsCompleted == true)
                CompleteAcquisition(pending);
            if (pending.State == RemoteStructuralPackClientState.ProofPending)
                complete &= QueueProof(pending);
        }
        if (_initialProofQueued && _windowProofPending)
            complete &= QueueWindowProof();
        return complete;
    }

    public RemoteStructuralPackClientSnapshot GetSnapshot()
    {
        EnsureOperational();
        int pending = 0;
        foreach (PendingPack? value in _pending)
            if (value is { State: not (RemoteStructuralPackClientState.Applied or
                    RemoteStructuralPackClientState.Rejected or
                    RemoteStructuralPackClientState.Retired) }) pending++;
        return new(_requestsQueued, _grantsAccepted, _proofsQueued, _rejectedGrants,
            _acquisitions, _bytesFetched, _cache.PreparedComposition!.PreparedPackCount,
            _cache.GetSnapshot().ResidentChunks, _initialReady, pending, _lastResolution)
        { RetirementsQueued = _retirementsQueued };
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        foreach (PendingPack? pending in _pending) pending?.Dispose();
    }

    private bool QueueRequest(PendingPack pending)
    {
        var request = new RemoteStructuralPackRequestMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StructuralSequence = _manifest.GetSnapshot().StructuralSequence,
            PackRequestSequence = pending.Sequence,
            PackX = pending.Coord.X,
            PackZ = pending.Coord.Z,
        };
        Span<byte> bytes = stackalloc byte[RemoteStructuralPackRequestMessageCodec.EncodedSize];
        if (!RemoteStructuralPackRequestMessageCodec.TryWriteExact(bytes, in request))
            throw new InvalidOperationException("The structural Pack request did not encode.");
        if (_peer.TryQueue(RemoteStructuralPackRequestMessageCodec.Descriptor.Id, bytes) !=
            NetworkQueueStatus.Accepted) return false;
        pending.State = RemoteStructuralPackClientState.AwaitingGrant;
        Increment(ref _requestsQueued);
        return true;
    }

    private void CompleteAcquisition(PendingPack pending)
    {
        Task<AcquisitionResult> task = pending.Acquisition!;
        pending.Acquisition = null;
        AcquisitionResult acquired;
        try { acquired = task.GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            pending.Status = RemoteStructuralApplyStatus.Rejected;
            pending.State = RemoteStructuralPackClientState.ProofPending;
            pending.Cancellation?.Dispose();
            pending.Cancellation = null;
            return;
        }
        pending.Cancellation?.Dispose();
        pending.Cancellation = null;
        Increment(ref _acquisitions);
        _bytesFetched = checked(_bytesFetched + acquired.Result.BytesFetched);
        if (!acquired.Success || pending.Grant is null ||
            !ContentPackageId.TryCreate(pending.Grant.PackageId, out ContentPackageId packageId))
        {
            pending.Status = RemoteStructuralApplyStatus.Rejected;
            pending.State = RemoteStructuralPackClientState.ProofPending;
            return;
        }
        RemoteStructuralPackGrantHeader header = pending.Grant.Header;
        var hash = new RemoteContentHash(
            header.PackageManifestHash0, header.PackageManifestHash1,
            header.PackageManifestHash2, header.PackageManifestHash3);
        ContentHash manifestHash = RemoteContentAuthorizationProjection.ToContent(hash);
        PreparedWorldComposition previous = _cache.PreparedComposition!;
        if (!previous.Manifest.TryGetPack(pending.Coord,
                out WorldPackFingerprint expectedPack) ||
            !ContentWorldPackResolver.TryResolveExactPackage(
                _contentCache, _scope, packageId, manifestHash, expectedPack,
                pending.Coord, previous.Space, _binding, out BoundWorldPack? bound,
                out _lastResolution))
        {
            pending.Status = RemoteStructuralApplyStatus.Rejected;
            pending.State = RemoteStructuralPackClientState.ProofPending;
            return;
        }
        var packs = new PreparedWorldPack[previous.PreparedPackCount + 1];
        for (int index = 0; index < previous.PreparedPackCount; index++)
            if (!previous.TryGetPreparedPackAt(index, out packs[index]))
                throw new InvalidOperationException("A retained prepared Pack disappeared.");
        packs[^1] = new PreparedWorldPack(pending.Coord, new WorldPreparedView(bound!));
        if (!PreparedWorldComposition.TryCreate(previous.Space, previous.Revision,
                previous.Manifest, packs, out PreparedWorldComposition? candidate, out _) ||
            !candidate!.TryValidatePreparedSubset(out _) ||
            !_cache.TryPublishPreparedComposition(candidate))
        {
            pending.Status = RemoteStructuralApplyStatus.Rejected;
            pending.State = RemoteStructuralPackClientState.ProofPending;
            return;
        }
        if (_residency is not null && !_residency.TryRefreshPreparedSource())
        {
            RollBackPreparedCandidate(previous);
            pending.Status = RemoteStructuralApplyStatus.Rejected;
            pending.State = RemoteStructuralPackClientState.ProofPending;
            return;
        }
        if (pending.Coord == _manifest.InitialPack && !_initialReady)
        {
            ClientStructuralResidency initial;
            try { initial = new ClientStructuralResidency(_cache, renderRadiusChunks: 1); }
            catch (ArgumentException)
            {
                RollBackPreparedCandidate(previous);
                pending.Status = RemoteStructuralApplyStatus.Rejected;
                pending.State = RemoteStructuralPackClientState.ProofPending;
                return;
            }
            if (!initial.TryPrepare(_manifest.InitialCenter, reset: false,
                    out ClientStructuralResidencyPlan plan, out _, out _))
            {
                RollBackPreparedCandidate(previous);
                pending.Status = RemoteStructuralApplyStatus.Rejected;
                pending.State = RemoteStructuralPackClientState.ProofPending;
                return;
            }
            initial.Commit(plan);
            _residency = initial;
            _initialReady = true;
            _residentCenter = _manifest.InitialCenter;
            _windowProofPending = true;
        }
        pending.Status = RemoteStructuralApplyStatus.Applied;
        pending.State = RemoteStructuralPackClientState.ProofPending;
    }

    private void RollBackPreparedCandidate(PreparedWorldComposition previous)
    {
        if (!_cache.TryPublishPreparedComposition(previous) ||
            (_residency is not null && !_residency.TryRefreshPreparedSource()))
            throw new InvalidOperationException("A failed Pack candidate could not be rolled back.");
    }

    private bool QueueProof(PendingPack pending)
    {
        RemoteStructuralPackGrantHeader header = pending.Grant!.Header;
        var proof = new RemoteStructuralPackAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StructuralSequence = header.StructuralSequence,
            PackRequestSequence = header.PackRequestSequence,
            PackX = header.PackX,
            PackZ = header.PackZ,
            PackHash0 = header.PackHash0,
            PackHash1 = header.PackHash1,
            PackHash2 = header.PackHash2,
            PackHash3 = header.PackHash3,
            Status = pending.Status,
        };
        Span<byte> bytes = stackalloc byte[RemoteStructuralPackAppliedMessageCodec.EncodedSize];
        if (!RemoteStructuralPackAppliedMessageCodec.TryWriteExact(bytes, in proof))
            throw new InvalidOperationException("The structural Pack proof did not encode.");
        if (_peer.TryQueue(RemoteStructuralPackAppliedMessageCodec.Descriptor.Id, bytes) !=
            NetworkQueueStatus.Accepted) return false;
        pending.State = pending.Status == RemoteStructuralApplyStatus.Applied
            ? RemoteStructuralPackClientState.Applied : RemoteStructuralPackClientState.Rejected;
        if (pending.Coord == _manifest.InitialPack &&
            pending.Status == RemoteStructuralApplyStatus.Applied)
            _initialProofQueued = true;
        Increment(ref _proofsQueued);
        return true;
    }

    private bool QueueWindowProof()
    {
        ClientStructuralResidency residency = _residency!;
        SpatialChunkKey center = _residentCenter;
        ulong mask = CreateResidentMask(center);
        if (_nextWindowSequence == 0 || (mask & (1UL << 12)) == 0)
            return false;
        var proof = new RemoteStructuralWindowAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StructuralSequence = _manifest.GetSnapshot().StructuralSequence,
            WindowSequence = _nextWindowSequence,
            CenterChunkX = center.Coord.X,
            CenterChunkZ = center.Coord.Z,
            ResidentRadiusChunks = checked(residency.ConfiguredRenderRadiusChunks +
                residency.StructuralFringeChunks),
            ResidentMask = mask,
        };
        Span<byte> bytes = stackalloc byte[RemoteStructuralWindowAppliedMessageCodec.EncodedSize];
        if (!RemoteStructuralWindowAppliedMessageCodec.TryWriteExact(bytes, in proof))
            throw new InvalidOperationException("The structural window proof did not encode.");
        if (_peer.TryQueue(RemoteStructuralWindowAppliedMessageCodec.Descriptor.Id, bytes) !=
            NetworkQueueStatus.Accepted) return false;
        _nextWindowSequence++;
        _lastProvenWindowCenter = center;
        _lastProvenWindowMask = mask;
        _windowProofPending = false;
        _initialWindowProofQueued = true;
        Increment(ref _windowProofsQueued);
        return true;
    }

    private bool QueueRetirement()
    {
        RemoteStructuralPackGrantHeader header = _pendingRetirement!.Grant!.Header;
        var retired = new RemoteStructuralPackRetiredMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StructuralSequence = header.StructuralSequence,
            PackRequestSequence = header.PackRequestSequence,
            PackX = header.PackX,
            PackZ = header.PackZ,
            PackHash0 = header.PackHash0,
            PackHash1 = header.PackHash1,
            PackHash2 = header.PackHash2,
            PackHash3 = header.PackHash3,
        };
        Span<byte> bytes = stackalloc byte[RemoteStructuralPackRetiredMessageCodec.EncodedSize];
        if (!RemoteStructuralPackRetiredMessageCodec.TryWriteExact(bytes, in retired))
            throw new InvalidOperationException("The structural Pack retirement did not encode.");
        if (_peer.TryQueue(RemoteStructuralPackRetiredMessageCodec.Descriptor.Id, bytes) !=
            NetworkQueueStatus.Accepted) return false;
        _pendingRetirement = null;
        Increment(ref _retirementsQueued);
        return true;
    }

    private void RequestMissingPacksAround(SpatialChunkKey center)
    {
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            long x = (long)center.Coord.X + dx;
            long z = (long)center.Coord.Z + dz;
            if (x is < int.MinValue or > int.MaxValue ||
                z is < int.MinValue or > int.MaxValue) continue;
            var key = new SpatialChunkKey(center.Space, (int)x, (int)z);
            if (_cache.TryProbeStructuralChunk(key, out WorldStructuralChunkProbe probe) &&
                probe.Status == WorldStructuralChunkStatus.KnownPackButNotPrepared)
            {
                bool rejected = false;
                foreach (PendingPack? pending in _pending)
                    if (pending is { State: RemoteStructuralPackClientState.Rejected } &&
                        pending.Coord == probe.OwnerPack)
                    {
                        rejected = true;
                        break;
                    }
                if (!rejected) _ = TryRequest(probe.OwnerPack);
            }
        }
    }

    private ulong CreateResidentMask(SpatialChunkKey center)
    {
        ulong mask = 0;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            var key = new SpatialChunkKey(center.Space,
                checked(center.Coord.X + dx), checked(center.Coord.Z + dz));
            if (_cache.TryGetResidentHandle(key, out _) &&
                _cache.TryProbeStructuralChunk(key, out WorldStructuralChunkProbe probe) &&
                probe.Status == WorldStructuralChunkStatus.PreparedChunk)
                mask |= 1UL << ((dx + 2) * 5 + dz + 2);
        }
        return mask;
    }

    private static bool GrantsEqual(RemoteStructuralPackGrant left, RemoteStructuralPackGrant right) =>
        left.Header.Equals(right.Header) && left.PackageId == right.PackageId &&
        left.Sources.SequenceEqual(right.Sources);

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
            throw new InvalidOperationException("Structural Pack client is owner-thread-bound.");
    }

    private readonly record struct AcquisitionResult(
        bool Success, ContentActivationAcquisitionResult Result);

    private sealed class PendingPack : IDisposable
    {
        public PendingPack(WorldPackCoord coord, ulong sequence)
        {
            Coord = coord;
            Sequence = sequence;
            State = RemoteStructuralPackClientState.RequestPending;
        }

        public WorldPackCoord Coord { get; }
        public ulong Sequence { get; }
        public RemoteStructuralPackClientState State { get; set; }
        public RemoteStructuralPackGrant? Grant { get; set; }
        public RemoteStructuralApplyStatus Status { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task<AcquisitionResult>? Acquisition { get; set; }

        public void StartAcquisition(ContentCache cache, ContentScopeKey scope,
            IContentPackageSource source)
        {
            RemoteStructuralPackGrant grant = Grant!;
            if (!ContentPackageId.TryCreate(grant.PackageId, out ContentPackageId packageId))
                throw new InvalidOperationException("A validated Pack grant lost its package identity.");
            RemoteStructuralPackGrantHeader header = grant.Header;
            ContentHash manifestHash = RemoteContentAuthorizationProjection.ToContent(
                new RemoteContentHash(header.PackageManifestHash0,
                    header.PackageManifestHash1, header.PackageManifestHash2,
                    header.PackageManifestHash3));
            var acquisition = new ContentPackageAcquisition(packageId, manifestHash,
                grant.Sources.Select(static value => new ContentSourceDescriptor(
                    (ContentSourceKind)value.Kind, value.Locator)));
            Cancellation = new CancellationTokenSource();
            CancellationToken token = Cancellation.Token;
            Acquisition = Task.Run(() =>
            {
                bool acquired = ContentActivationAcquirer.TryAcquireExactPackage(
                    cache, scope, acquisition, source, out _,
                    out ContentActivationAcquisitionResult result,
                    cancellationToken: token);
                return new AcquisitionResult(acquired, result);
            }, token);
        }

        public void Dispose()
        {
            Cancellation?.Cancel();
            if (Acquisition is not null)
            {
                try { _ = Acquisition.GetAwaiter().GetResult(); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { }
            }
            Cancellation?.Dispose();
            Cancellation = null;
            Acquisition = null;
        }
    }
}
