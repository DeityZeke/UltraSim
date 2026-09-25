using System.Buffers.Binary;
using UltraSim.Client.Content;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;

namespace UltraSim.Client.Network;

public enum RemoteStructuralManifestClientState : byte
{
    AwaitingAdvertisement = 0,
    AwaitingContent = 1,
    ProofPending = 2,
    Applied = 3,
    Rejected = 4,
}

public readonly record struct RemoteStructuralManifestClientSnapshot(
    RemoteStructuralManifestClientState State,
    ulong ApplicationSessionEpoch,
    ulong StructuralSequence,
    WorldStructuralManifestFingerprint ManifestFingerprint,
    WorldCompositionRevision Revision,
    int ManifestEntryCount,
    int PreparedPackCount,
    WorldPackCoord InitialPack,
    ulong Advertisements,
    ulong DuplicateAdvertisements,
    ulong RejectedAdvertisements,
    ulong ProofsQueued,
    ContentWorldStructuralManifestResolutionStatus Resolution)
{
    public SpatialChunkKey InitialCenter { get; init; }
}

/// <summary>
/// World-level validation of an authenticated structural advertisement. Content authorization
/// grants access to manifest bytes; this owner independently validates exact World identity and
/// retains a zero-Pack composition. It never treats catalog application as structural proof.
/// </summary>
public sealed class RemoteStructuralManifestClient :
    IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly RemoteContentClient _content;
    private readonly ulong _applicationSessionEpoch;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RemoteStructuralWorldAdvertisement _advertisement;
    private RemoteStructuralManifestAppliedMessage _proof;
    private PreparedWorldComposition? _composition;
    private WorldPackCoord _initialPack;
    private SpatialChunkKey _initialCenter;
    private ContentWorldStructuralManifestResolutionStatus _resolution;
    private RemoteStructuralManifestClientState _state;
    private ulong _advertisements;
    private ulong _duplicates;
    private ulong _rejected;
    private ulong _proofsQueued;

    public RemoteStructuralManifestClient(
        NetworkProtocolPeer peer, RemoteContentClient content, ulong applicationSessionEpoch)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        _applicationSessionEpoch = applicationSessionEpoch;
    }

    public bool Applied
    {
        get { EnsureOwner(); return _state == RemoteStructuralManifestClientState.Applied; }
    }

    public PreparedWorldComposition? Composition
    {
        get { EnsureOwner(); return _composition; }
    }

    public WorldPackCoord InitialPack
    {
        get { EnsureOwner(); return _initialPack; }
    }

    public SpatialChunkKey InitialCenter
    {
        get { EnsureOwner(); return _initialCenter; }
    }

    public RemoteStructuralManifestClientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new RemoteStructuralManifestClientSnapshot(_state, _applicationSessionEpoch,
            _advertisement.Header.StructuralSequence,
            _composition?.ManifestFingerprint ?? default,
            _composition?.Revision ?? default,
            _composition?.ManifestEntryCount ?? 0,
            _composition?.PreparedPackCount ?? 0,
            _initialPack, _advertisements, _duplicates, _rejected, _proofsQueued,
            _resolution) { InitialCenter = _initialCenter };
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId != RemoteStructuralWorldAdvertisementHeaderCodec.Descriptor.Id)
            return false;
        if (!UltraSimGameProtocol.TryReadStructuralAdvertisement(payload,
                out RemoteStructuralWorldAdvertisement incoming) ||
            incoming.Header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            !LogicalContentId.TryCreate(incoming.ManifestDefinitionId, out _))
        {
            Increment(ref _rejected);
            return true;
        }
        Increment(ref _advertisements);
        if (_state != RemoteStructuralManifestClientState.AwaitingAdvertisement)
        {
            if (incoming.Equals(_advertisement))
            {
                Increment(ref _duplicates);
                if (_state == RemoteStructuralManifestClientState.Applied)
                    _state = RemoteStructuralManifestClientState.ProofPending;
            }
            else
            {
                Increment(ref _rejected);
                _state = RemoteStructuralManifestClientState.Rejected;
            }
            return true;
        }
        _advertisement = incoming;
        _initialPack = new WorldPackCoord(
            incoming.Header.AnchorPackX, incoming.Header.AnchorPackZ);
        _initialCenter = new SpatialChunkKey(
            new SpatialSpaceId(incoming.Header.SpatialSpace),
            incoming.Header.AnchorChunkX, incoming.Header.AnchorChunkZ);
        _state = RemoteStructuralManifestClientState.AwaitingContent;
        return true;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        if (_state == RemoteStructuralManifestClientState.AwaitingContent && _content.Applied)
            ResolveManifest();
        if (_state != RemoteStructuralManifestClientState.ProofPending)
            return _state != RemoteStructuralManifestClientState.Rejected;
        Span<byte> bytes = stackalloc byte[RemoteStructuralManifestAppliedMessageCodec.EncodedSize];
        if (!RemoteStructuralManifestAppliedMessageCodec.TryWriteExact(bytes, in _proof))
            throw new InvalidOperationException("The retained structural proof did not encode.");
        if (_peer.TryQueue(RemoteStructuralManifestAppliedMessageCodec.Descriptor.Id, bytes) !=
            NetworkQueueStatus.Accepted)
            return false;
        Increment(ref _proofsQueued);
        _state = _proof.Status == RemoteStructuralApplyStatus.Applied
            ? RemoteStructuralManifestClientState.Applied
            : RemoteStructuralManifestClientState.Rejected;
        return _state == RemoteStructuralManifestClientState.Applied;
    }

    private void ResolveManifest()
    {
        RemoteStructuralWorldAdvertisementHeader header = _advertisement.Header;
        Span<byte> digest = stackalloc byte[WorldStructuralManifestFormat.FingerprintBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(digest, header.ManifestHash0);
        BinaryPrimitives.WriteUInt64LittleEndian(digest[8..], header.ManifestHash1);
        BinaryPrimitives.WriteUInt64LittleEndian(digest[16..], header.ManifestHash2);
        BinaryPrimitives.WriteUInt64LittleEndian(digest[24..], header.ManifestHash3);
        if (!WorldStructuralManifestFingerprint.TryRead(digest, out var fingerprint) ||
            !LogicalContentId.TryCreate(_advertisement.ManifestDefinitionId, out var definition) ||
            !_content.TryResolveStructuralManifest(definition, fingerprint,
                out WorldStructuralManifest? manifest, out _resolution) ||
            !manifest!.TryGetPack(_initialPack, out _) ||
            !WorldPackAddressing.Contains(_initialPack, _initialCenter.Coord) ||
            !PreparedWorldComposition.TryCreate(
                new SpatialSpaceId(header.SpatialSpace),
                new WorldCompositionRevision(header.WorldCompositionRevision),
                manifest, ReadOnlySpan<PreparedWorldPack>.Empty,
                out _composition, out _))
        {
            _composition = null;
            PrepareProof(RemoteStructuralApplyStatus.Rejected);
            return;
        }
        PrepareProof(RemoteStructuralApplyStatus.Applied);
    }

    private void PrepareProof(RemoteStructuralApplyStatus status)
    {
        RemoteStructuralWorldAdvertisementHeader header = _advertisement.Header;
        _proof = new RemoteStructuralManifestAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StructuralSequence = header.StructuralSequence,
            ManifestHash0 = header.ManifestHash0,
            ManifestHash1 = header.ManifestHash1,
            ManifestHash2 = header.ManifestHash2,
            ManifestHash3 = header.ManifestHash3,
            Status = status,
        };
        _state = RemoteStructuralManifestClientState.ProofPending;
    }

    private static void Increment(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Structural manifest client is owner-thread-bound.");
    }
}
