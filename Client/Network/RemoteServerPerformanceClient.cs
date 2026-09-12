using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public enum RemoteServerPerformanceClientState : byte
{
    AwaitingAdvertisement = 1,
    Applied = 2,
    Faulted = 3,
}

public readonly record struct RemoteServerPerformanceClientSnapshot(
    RemoteServerPerformanceClientState State,
    ulong ApplicationSessionEpoch,
    ulong ProfileRevision,
    RemoteServerPerformanceProfileHash ProfileHash,
    ulong AdvertisementsReceived,
    ulong ProofsQueued,
    ulong RejectedMessages,
    string? Detail);

/// <summary>
/// Automatic authenticated application of one finite server execution/delivery policy. Unlike a
/// control recommendation this policy is not consent state: a compatible stock client applies the
/// exact session-scoped limit and proves that identity before later application traffic proceeds.
/// </summary>
public sealed class RemoteServerPerformanceClient :
    IRemoteGameProtocolHandler,
    IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _proof =
        new byte[RemoteServerPerformanceProfileAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RemoteServerPerformanceProfile? _profile;
    private RemoteServerPerformanceProfileAppliedMessage _pendingProof;
    private bool _hasPendingProof;
    private ulong _advertisements;
    private ulong _proofsQueued;
    private ulong _rejectedMessages;
    private string? _detail;
    private RemoteServerPerformanceClientState _state =
        RemoteServerPerformanceClientState.AwaitingAdvertisement;

    public RemoteServerPerformanceClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        _applicationSessionEpoch = applicationSessionEpoch;
    }

    public RemoteServerPerformanceProfile Profile => _profile ??
        throw new InvalidOperationException("The server performance profile is not applied.");

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId != RemoteServerPerformanceProfileMessageCodec.Descriptor.Id)
            return false;
        if (!RemoteServerPerformanceProfileMessageCodec.TryReadExact(
                payload, out RemoteServerPerformanceProfileMessage wire) ||
            wire.ApplicationSessionEpoch != _applicationSessionEpoch ||
            !UltraSimGameProtocol.TryReadServerPerformanceProfile(in wire, out var profile))
        {
            SaturatingIncrement(ref _rejectedMessages);
            Fault("The authenticated server performance profile was invalid or unsupported.");
            return true;
        }
        if (_profile is not null)
        {
            if (_profile.Revision != profile!.Revision || _profile.Hash != profile.Hash)
            {
                SaturatingIncrement(ref _rejectedMessages);
                Fault("The server changed its performance profile within one application session.");
            }
            else
            {
                SaturatingIncrement(ref _advertisements);
                QueueProof();
            }
            return true;
        }
        _profile = profile;
        _state = RemoteServerPerformanceClientState.Applied;
        _detail = null;
        SaturatingIncrement(ref _advertisements);
        QueueProof();
        return true;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        if (!_hasPendingProof) return true;
        if (!RemoteServerPerformanceProfileAppliedMessageCodec.TryWriteExact(
                _proof, in _pendingProof))
            throw new InvalidOperationException(
                "The retained server-performance proof became invalid.");
        if (_peer.TryQueue(
                RemoteServerPerformanceProfileAppliedMessageCodec.Descriptor.Id, _proof) !=
            NetworkQueueStatus.Accepted)
            return false;
        _hasPendingProof = false;
        SaturatingIncrement(ref _proofsQueued);
        return true;
    }

    public RemoteServerPerformanceClientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new(
            _state,
            _applicationSessionEpoch,
            _profile?.Revision ?? 0,
            _profile?.Hash ?? default,
            _advertisements,
            _proofsQueued,
            _rejectedMessages,
            _detail);
    }

    private void QueueProof()
    {
        if (_profile is null) return;
        _pendingProof = UltraSimGameProtocol.CreateServerPerformanceProfileApplied(
            _applicationSessionEpoch,
            _profile,
            RemoteServerPerformanceApplyStatus.Applied);
        _hasPendingProof = true;
        _ = TryProgress();
    }

    private void Fault(string detail)
    {
        _state = RemoteServerPerformanceClientState.Faulted;
        _detail = detail;
        _hasPendingProof = false;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "The remote server-performance gate is owner-thread-bound.");
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }
}
