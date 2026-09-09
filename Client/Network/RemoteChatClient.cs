using UltraSim.Client.Core;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public readonly record struct RemoteChatClientSnapshot(
    ulong ApplicationSessionEpoch,
    ulong NextSubmissionSequence,
    bool HasPendingProof,
    ulong SubmissionsQueued,
    ulong OccurrencesApplied,
    ulong DuplicateOccurrences,
    ulong RejectedOccurrences,
    ulong ProofsQueued,
    long KnownArrayReservedBytes);

/// <summary>Authenticated Chat submit/delivery bridge over the existing Game protocol.</summary>
public sealed class RemoteChatClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ClientChatState _state;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumChatMessageBytes];
    private readonly byte[] _proofScratch = new byte[RemoteChatAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _nextSubmissionSequence = 1;
    private RemoteChatAppliedMessage _pendingProof;
    private bool _hasPendingProof;
    private ulong _submissionsQueued;
    private ulong _applied;
    private ulong _duplicates;
    private ulong _rejected;
    private ulong _proofsQueued;

    public RemoteChatClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ClientChatState state)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (applicationSessionEpoch == 0 ||
            state.GetSnapshot().ApplicationSessionEpoch != applicationSessionEpoch)
            throw new ArgumentException("Client Chat state belongs to another application session.");
        _applicationSessionEpoch = applicationSessionEpoch;
    }

    public ClientChatState State => _state;

    public bool TrySubmit(string text, ClientChatSpeechMode mode = ClientChatSpeechMode.Say)
    {
        EnsureOwnerThread();
        if (_nextSubmissionSequence == 0 ||
            mode is < ClientChatSpeechMode.Whisper or > ClientChatSpeechMode.Global ||
            !UltraSimGameProtocol.TryWriteChatSubmission(
                _messageScratch, _applicationSessionEpoch, _nextSubmissionSequence,
                (byte)mode, text, out int written) ||
            _peer.TryQueue(RemoteChatSubmitHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _nextSubmissionSequence = _nextSubmissionSequence == ulong.MaxValue
            ? 0
            : _nextSubmissionSequence + 1;
        SaturatingIncrement(ref _submissionsQueued);
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId != RemoteChatOccurrenceHeaderCodec.Descriptor.Id) return false;
        if (!UltraSimGameProtocol.TryReadChatOccurrence(
                payload, out RemoteChatOccurrenceHeader header,
                out string displayLabel, out string text) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            !TryCreateAnchor(header.PublicEntityIndex, header.PublicEntityGeneration, out WorldPublicEntityId anchor))
        {
            SaturatingIncrement(ref _rejected);
            return true;
        }
        var occurrence = new ClientChatOccurrence(
            header.ServerGenerationId,
            header.DeliveryId,
            header.RecipientSequence,
            header.SpeechFactId,
            header.SimulationTick,
            (ClientChatOccurrenceKind)header.Kind,
            (ClientChatSpeechMode)header.SpeechMode,
            anchor,
            displayLabel,
            text);
        ClientChatApplyStatus applied = _state.TryApply(in occurrence);
        switch (applied)
        {
            case ClientChatApplyStatus.Applied:
                SaturatingIncrement(ref _applied);
                QueueProof(in header, RemoteChatApplyStatus.Applied);
                break;
            case ClientChatApplyStatus.Duplicate:
                SaturatingIncrement(ref _duplicates);
                QueueProof(in header, RemoteChatApplyStatus.Applied);
                break;
            default:
                SaturatingIncrement(ref _rejected);
                QueueProof(in header, RemoteChatApplyStatus.Rejected);
                break;
        }
        return true;
    }

    public bool TryProgress()
    {
        EnsureOwnerThread();
        if (!_hasPendingProof) return true;
        if (!TryQueueProof(in _pendingProof)) return false;
        _pendingProof = default;
        _hasPendingProof = false;
        return true;
    }

    bool IRemoteGameProtocolProgressor.TryProgress() => TryProgress();

    public RemoteChatClientSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new RemoteChatClientSnapshot(
            _applicationSessionEpoch, _nextSubmissionSequence, _hasPendingProof,
            _submissionsQueued, _applied, _duplicates, _rejected, _proofsQueued,
            _messageScratch.Length + _proofScratch.Length);
    }

    private void QueueProof(in RemoteChatOccurrenceHeader occurrence, RemoteChatApplyStatus status)
    {
        var proof = new RemoteChatAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ServerGenerationId = occurrence.ServerGenerationId,
            DeliveryId = occurrence.DeliveryId,
            RecipientSequence = occurrence.RecipientSequence,
            Status = status,
        };
        if (_hasPendingProof)
        {
            if (!ProofsEqual(in _pendingProof, in proof))
            {
                SaturatingIncrement(ref _rejected);
                return;
            }
            if (TryQueueProof(in _pendingProof))
            {
                _pendingProof = default;
                _hasPendingProof = false;
            }
            return;
        }
        if (TryQueueProof(in proof)) return;
        _pendingProof = proof;
        _hasPendingProof = true;
    }

    private bool TryQueueProof(in RemoteChatAppliedMessage proof)
    {
        if (!RemoteChatAppliedMessageCodec.TryWriteExact(_proofScratch, in proof))
            throw new InvalidOperationException("A canonical Chat application proof did not encode.");
        if (_peer.TryQueue(RemoteChatAppliedMessageCodec.Descriptor.Id, _proofScratch) !=
            NetworkQueueStatus.Accepted)
            return false;
        SaturatingIncrement(ref _proofsQueued);
        return true;
    }

    private static bool TryCreateAnchor(uint index, uint generation, out WorldPublicEntityId anchor)
    {
        if (generation == 0)
        {
            anchor = default;
            return index == 0;
        }
        anchor = new WorldPublicEntityId(index, generation);
        return true;
    }

    private static bool ProofsEqual(
        in RemoteChatAppliedMessage left,
        in RemoteChatAppliedMessage right) =>
        left.ApplicationSessionEpoch == right.ApplicationSessionEpoch &&
        left.ServerGenerationId == right.ServerGenerationId &&
        left.DeliveryId == right.DeliveryId &&
        left.RecipientSequence == right.RecipientSequence &&
        left.Status == right.Status;

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Remote Chat client is owner-thread-bound.");
    }
}
