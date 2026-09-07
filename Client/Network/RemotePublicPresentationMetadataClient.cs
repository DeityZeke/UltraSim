using System.Runtime.CompilerServices;
using UltraSim.Client.Core;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public readonly record struct RemotePublicPresentationMetadataClientSnapshot(
    ulong ApplicationSessionEpoch,
    int PendingProofs,
    int PendingProofCapacity,
    ulong Applied,
    ulong Duplicates,
    ulong Rejected,
    ulong CapacityFailures,
    ulong ProofsQueued,
    ClientPublicPresentationMetadataSnapshot Metadata,
    long ReservedBytes);

/// <summary>
/// Remote adapter for relatively cold public actor metadata. It applies only exact bounded facts
/// to Client Core and retains semantic proof independently from hot chunk movement delivery.
/// </summary>
public sealed class RemotePublicPresentationMetadataClient :
    IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly ClientPublicPresentationMetadataState _state;
    private readonly RemotePublicPresentationAppliedMessage[] _proofs;
    private readonly byte[] _proofScratch =
        new byte[RemotePublicPresentationAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private int _proofHead;
    private int _proofCount;
    private ulong _applied;
    private ulong _duplicates;
    private ulong _rejected;
    private ulong _capacityFailures;
    private ulong _proofsQueued;

    public RemotePublicPresentationMetadataClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ClientPublicPresentationMetadataState state,
        int maximumPendingProofs = 64)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (maximumPendingProofs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingProofs));
        _applicationSessionEpoch = applicationSessionEpoch;
        _proofs = new RemotePublicPresentationAppliedMessage[maximumPendingProofs];
    }

    public ClientPublicPresentationMetadataState State => _state;

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId != RemotePublicPresentationMetadataHeaderCodec.Descriptor.Id)
            return false;
        if (_proofCount == _proofs.Length)
        {
            SaturatingIncrement(ref _capacityFailures);
            return true;
        }
        if (!UltraSimGameProtocol.TryReadPublicPresentationMetadata(
                payload, out RemotePublicPresentationMetadataHeader header,
                out string displayLabel) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch)
        {
            SaturatingIncrement(ref _rejected);
            return true;
        }

        ClientPublicPresentationMetadataSnapshot current = _state.GetSnapshot();
        if (current.ServerGenerationId == 0 || header.ServerGenerationId > current.ServerGenerationId)
            _state.ResetServerGeneration(header.ServerGenerationId);
        ClientPublicPresentationApplyStatus result;
        if (header.ServerGenerationId != _state.GetSnapshot().ServerGenerationId)
        {
            result = ClientPublicPresentationApplyStatus.Rejected;
        }
        else
        {
            var metadata = new ClientPublicPresentationMetadata(
                header.ServerGenerationId,
                new WorldPublicEntityId(
                    header.PublicEntityIndex, header.PublicEntityGeneration),
                header.MetadataRevision,
                (ClientPublicPresentationFlags)header.Flags,
                displayLabel);
            result = _state.TryApply(
                (ClientPublicPresentationOperation)header.Operation, in metadata);
        }

        RemotePublicPresentationApplyStatus proofStatus = result switch
        {
            ClientPublicPresentationApplyStatus.Applied =>
                RemotePublicPresentationApplyStatus.Applied,
            ClientPublicPresentationApplyStatus.Duplicate =>
                RemotePublicPresentationApplyStatus.Applied,
            ClientPublicPresentationApplyStatus.CapacityExceeded =>
                RemotePublicPresentationApplyStatus.CapacityExceeded,
            _ => RemotePublicPresentationApplyStatus.Rejected,
        };
        switch (result)
        {
            case ClientPublicPresentationApplyStatus.Applied:
                SaturatingIncrement(ref _applied);
                break;
            case ClientPublicPresentationApplyStatus.Duplicate:
                SaturatingIncrement(ref _duplicates);
                break;
            case ClientPublicPresentationApplyStatus.CapacityExceeded:
                SaturatingIncrement(ref _capacityFailures);
                break;
            default:
                SaturatingIncrement(ref _rejected);
                break;
        }
        EnqueueProof(new RemotePublicPresentationAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ServerGenerationId = header.ServerGenerationId,
            DeliveryId = header.DeliveryId,
            Status = proofStatus,
        });
        _ = FlushProofs();
        return true;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        return FlushProofs();
    }

    public RemotePublicPresentationMetadataClientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new RemotePublicPresentationMetadataClientSnapshot(
            _applicationSessionEpoch, _proofCount, _proofs.Length,
            _applied, _duplicates, _rejected, _capacityFailures, _proofsQueued,
            _state.GetSnapshot(),
            checked((long)_proofs.Length *
                Unsafe.SizeOf<RemotePublicPresentationAppliedMessage>() + _proofScratch.Length));
    }

    private void EnqueueProof(in RemotePublicPresentationAppliedMessage proof)
    {
        int tail = (_proofHead + _proofCount) % _proofs.Length;
        _proofs[tail] = proof;
        _proofCount++;
    }

    private bool FlushProofs()
    {
        while (_proofCount != 0)
        {
            ref readonly RemotePublicPresentationAppliedMessage proof = ref _proofs[_proofHead];
            if (!RemotePublicPresentationAppliedMessageCodec.TryWriteExact(
                    _proofScratch, in proof))
                throw new InvalidOperationException("Retained public metadata proof became invalid.");
            if (_peer.TryQueue(
                    RemotePublicPresentationAppliedMessageCodec.Descriptor.Id,
                    _proofScratch) != NetworkQueueStatus.Accepted)
                return false;
            _proofs[_proofHead] = default;
            _proofHead = (_proofHead + 1) % _proofs.Length;
            _proofCount--;
            SaturatingIncrement(ref _proofsQueued);
        }
        return true;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Remote public metadata is owner-thread-bound.");
    }
}
