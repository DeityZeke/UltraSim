using UltraSim.Client.Core;
using UltraSim.Game.Admission;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public enum RemotePlayableClientState : byte
{
    AwaitingAdmission = 1,
    AdmissionPending = 2,
    Active = 3,
    Rejected = 4,
    Faulted = 5,
}

public readonly record struct RemotePlayableClientSnapshot(
    RemotePlayableClientState State,
    ulong ApplicationSessionEpoch,
    ulong AdmissionAttemptSequence,
    ParticipantId Participant,
    GameGuestIdentity GuestIdentity,
    GameControlSessionEpoch ControlSession,
    WorldPublicEntityId PublicActor,
    ulong ServerGenerationId,
    ulong StateSequence,
    ulong SimulationTick,
    ulong LastProcessedCommandSequence,
    WorldPosition Position,
    WorldYaw Orientation,
    RemotePlayableActionStatus Admission,
    RemotePlayableActionStatus Locomotion,
    RemotePlayableActionStatus Jump,
    RemotePlayableActionStatus Primary,
    RemotePlayableActionStatus Interaction,
    bool CommandRetainedLocally,
    ulong CommandsRetained,
    ulong CommandsQueued,
    ulong DuplicateOwnerStates,
    ulong RejectedMessages,
    string? Detail);

/// <summary>
/// Client-owned F0.3b guest/control bridge. It retains at most one real
/// <see cref="GamePlayerCommand"/> across transport backpressure and applies complete owner-state
/// samples without creating a second movement authority.
/// </summary>
public sealed class RemotePlayableClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumGuestAdmissionMessageBytes];
    private readonly byte[] _proofScratch = new byte[RemoteGuestAdmissionAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RemotePlayableClientState _state = RemotePlayableClientState.AwaitingAdmission;
    private ulong _admissionAttempt;
    private ParticipantId _participant;
    private GameGuestIdentity _guestIdentity;
    private GameControlSessionEpoch _controlSession;
    private WorldPublicEntityId _publicActor;
    private ClientWorldInputSequencer? _sequencer;
    private RemoteGuestAdmissionAppliedMessage _pendingAdmissionProof;
    private bool _hasAdmissionProof;
    private RemotePlayableCommandMessage _pendingCommand;
    private bool _hasPendingCommand;
    private ulong _serverGenerationId;
    private ulong _stateSequence;
    private ulong _simulationTick;
    private ulong _lastProcessedCommand;
    private ulong _lastSubmittedCommand;
    private WorldPosition _position;
    private WorldYaw _orientation;
    private RemotePlayableActionStatus _admission;
    private RemotePlayableActionStatus _locomotion;
    private RemotePlayableActionStatus _jump;
    private RemotePlayableActionStatus _primary;
    private RemotePlayableActionStatus _interaction;
    private ulong _commandsRetained;
    private ulong _commandsQueued;
    private ulong _duplicateOwnerStates;
    private ulong _rejectedMessages;
    private string? _detail;

    public RemotePlayableClient(NetworkProtocolPeer peer, ulong applicationSessionEpoch)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        _applicationSessionEpoch = applicationSessionEpoch;
    }

    public ClientWorldInputSequencer Input => _sequencer ??
        throw new InvalidOperationException("Remote guest admission has not established control.");

    public bool TrySubmitGuest(in GameGuestAdmissionRequest request)
    {
        EnsureOwner();
        if (_state != RemotePlayableClientState.AwaitingAdmission ||
            _admissionAttempt == ulong.MaxValue)
            return false;
        ulong attempt = _admissionAttempt + 1;
        if (!UltraSimGameProtocol.TryWriteGuestAdmissionRequest(
                _messageScratch, _applicationSessionEpoch, attempt, request, out int written) ||
            _peer.TryQueue(RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _admissionAttempt = attempt;
        _state = RemotePlayableClientState.AdmissionPending;
        _detail = null;
        return true;
    }

    /// <summary>
    /// Sequences and retains one semantic input until the reliable Event lane accepts it.
    /// Later intent cannot bypass an earlier locally retained command.
    /// </summary>
    public bool TrySubmitCommand(
        in ClientWorldCommandIntent intent,
        WorldPublicEntityId target = default,
        RemotePlayableCommandFlags flags = RemotePlayableCommandFlags.None,
        long faceXMillimeters = 0,
        long faceZMillimeters = 0)
    {
        EnsureOwner();
        if (_state != RemotePlayableClientState.Active || _sequencer is null || _hasPendingCommand ||
            !intent.IsValid ||
            (flags & ~(RemotePlayableCommandFlags.FaceRequested |
                RemotePlayableCommandFlags.FacingRequiresAcceptedLocomotion |
                RemotePlayableCommandFlags.OpenInventoryRequested)) != 0 ||
            (flags & RemotePlayableCommandFlags.FacingRequiresAcceptedLocomotion) != 0 &&
                (flags & RemotePlayableCommandFlags.FaceRequested) == 0 ||
            (flags & RemotePlayableCommandFlags.FaceRequested) == 0 &&
                (faceXMillimeters != 0 || faceZMillimeters != 0) ||
            !_sequencer.TryCreateSemanticCommand(intent, out GamePlayerCommand command))
            return false;
        _pendingCommand = UltraSimGameProtocol.CreatePlayableCommand(
            _applicationSessionEpoch,
            in command,
            target.IsValid ? new RemotePublicEntityTarget(target.Index, target.Generation) : default,
            flags,
            faceXMillimeters,
            faceZMillimeters);
        if (!UltraSimGameProtocol.TryCreatePlayableCommand(
                in _pendingCommand, out _, out _))
            throw new InvalidOperationException(
                "A prevalidated remote playable command became non-canonical.");
        _lastSubmittedCommand = command.Sequence;
        _hasPendingCommand = true;
        SaturatingIncrement(ref _commandsRetained);
        _ = FlushCommand();
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId == RemoteGuestAdmissionResultHeaderCodec.Descriptor.Id)
        {
            ProcessAdmissionResult(payload);
            return true;
        }
        if (message.MessageId == RemotePlayableOwnerStateMessageCodec.Descriptor.Id)
        {
            ProcessOwnerState(payload);
            return true;
        }
        return false;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        bool complete = FlushAdmissionProof();
        return FlushCommand() && complete;
    }

    public RemotePlayableClientSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new RemotePlayableClientSnapshot(
            _state, _applicationSessionEpoch, _admissionAttempt, _participant, _guestIdentity,
            _controlSession, _publicActor, _serverGenerationId, _stateSequence, _simulationTick,
            _lastProcessedCommand, _position, _orientation, _admission, _locomotion, _jump,
            _primary, _interaction, _hasPendingCommand, _commandsRetained, _commandsQueued,
            _duplicateOwnerStates, _rejectedMessages, _detail);
    }

    private void ProcessAdmissionResult(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadGuestAdmissionResult(
                payload, out RemoteGuestAdmissionResultHeader header, out string text) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            header.AttemptSequence != _admissionAttempt ||
            _state is not (RemotePlayableClientState.AdmissionPending or
                RemotePlayableClientState.Active or RemotePlayableClientState.Rejected))
        {
            SaturatingIncrement(ref _rejectedMessages);
            return;
        }

        if (_state is RemotePlayableClientState.Active or RemotePlayableClientState.Rejected)
        {
            QueueAdmissionProof(header.AttemptSequence, RemoteGuestAdmissionApplyStatus.Applied);
            return;
        }

        if (header.Status == RemoteGuestAdmissionStatus.Rejected)
        {
            _state = RemotePlayableClientState.Rejected;
            _detail = text;
            QueueAdmissionProof(header.AttemptSequence, RemoteGuestAdmissionApplyStatus.Applied);
            return;
        }

        try
        {
            _participant = new ParticipantId(header.ParticipantId);
            _controlSession = new GameControlSessionEpoch(header.ControlSessionEpoch);
            _publicActor = new WorldPublicEntityId(
                header.PublicEntityIndex, header.PublicEntityGeneration);
        }
        catch (ArgumentOutOfRangeException)
        {
            RejectAdmissionApplication(header.AttemptSequence,
                "The remote authority returned invalid guest/control identity.");
            return;
        }
        _guestIdentity = new GameGuestIdentity((GameGuestBody)header.Body, text);
        if (!_guestIdentity.IsValid)
        {
            RejectAdmissionApplication(header.AttemptSequence,
                "The remote authority returned an invalid canonical guest identity.");
            return;
        }
        _sequencer = new ClientWorldInputSequencer(_controlSession);
        _serverGenerationId = header.ServerGenerationId;
        _state = RemotePlayableClientState.Active;
        _detail = null;
        QueueAdmissionProof(header.AttemptSequence, RemoteGuestAdmissionApplyStatus.Applied);
    }

    private void ProcessOwnerState(ReadOnlySpan<byte> payload)
    {
        if (_state != RemotePlayableClientState.Active ||
            !RemotePlayableOwnerStateMessageCodec.TryReadExact(
                payload, out RemotePlayableOwnerStateMessage state) ||
            state.ApplicationSessionEpoch != _applicationSessionEpoch ||
            state.ControlSessionEpoch != _controlSession.Value ||
            state.ServerGenerationId < _serverGenerationId || state.StateSequence == 0 ||
            state.PublicEntityGeneration == 0 ||
            state.LastProcessedCommandSequence > _lastSubmittedCommand ||
            state.Body != (byte)_guestIdentity.Body ||
            !IsActionStatus(state.Admission) || !IsActionStatus(state.Locomotion) ||
            !IsActionStatus(state.Jump) || !IsActionStatus(state.Primary) ||
            !IsActionStatus(state.Interaction) ||
            !WorldPosition.TryFromGlobalMillimeters(
                state.GlobalXMillimeters, state.YMillimeters,
                state.GlobalZMillimeters, out WorldPosition position))
        {
            SaturatingIncrement(ref _rejectedMessages);
            return;
        }
        if (state.StateSequence <= _stateSequence)
        {
            SaturatingIncrement(ref _duplicateOwnerStates);
            return;
        }
        _serverGenerationId = state.ServerGenerationId;
        _stateSequence = state.StateSequence;
        _simulationTick = state.SimulationTick;
        _lastProcessedCommand = state.LastProcessedCommandSequence;
        _publicActor = new WorldPublicEntityId(
            state.PublicEntityIndex, state.PublicEntityGeneration);
        _position = position;
        _orientation = new WorldYaw(state.OrientationUnits);
        _admission = state.Admission;
        _locomotion = state.Locomotion;
        _jump = state.Jump;
        _primary = state.Primary;
        _interaction = state.Interaction;
    }

    private void RejectAdmissionApplication(ulong attempt, string detail)
    {
        _state = RemotePlayableClientState.Faulted;
        _detail = detail;
        SaturatingIncrement(ref _rejectedMessages);
        QueueAdmissionProof(attempt, RemoteGuestAdmissionApplyStatus.Rejected);
    }

    private void QueueAdmissionProof(ulong attempt, RemoteGuestAdmissionApplyStatus status)
    {
        var proof = new RemoteGuestAdmissionAppliedMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            AttemptSequence = attempt,
            Status = status,
        };
        if (_hasAdmissionProof &&
            (_pendingAdmissionProof.ApplicationSessionEpoch != proof.ApplicationSessionEpoch ||
             _pendingAdmissionProof.AttemptSequence != proof.AttemptSequence ||
             _pendingAdmissionProof.Status != proof.Status))
        {
            _state = RemotePlayableClientState.Faulted;
            _detail = "Conflicting guest-admission proof was retained.";
            return;
        }
        _pendingAdmissionProof = proof;
        _hasAdmissionProof = true;
        _ = FlushAdmissionProof();
    }

    private bool FlushAdmissionProof()
    {
        if (!_hasAdmissionProof) return true;
        if (!RemoteGuestAdmissionAppliedMessageCodec.TryWriteExact(
                _proofScratch, in _pendingAdmissionProof))
            throw new InvalidOperationException("A retained guest-admission proof became invalid.");
        if (_peer.TryQueue(RemoteGuestAdmissionAppliedMessageCodec.Descriptor.Id, _proofScratch) !=
            NetworkQueueStatus.Accepted)
            return false;
        _pendingAdmissionProof = default;
        _hasAdmissionProof = false;
        return true;
    }

    private bool FlushCommand()
    {
        if (!_hasPendingCommand) return true;
        int bytes = RemotePlayableCommandMessageCodec.EncodedSize;
        if (!RemotePlayableCommandMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, bytes), in _pendingCommand))
            throw new InvalidOperationException("A retained player command became invalid.");
        if (_peer.TryQueue(RemotePlayableCommandMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
            return false;
        _pendingCommand = default;
        _hasPendingCommand = false;
        SaturatingIncrement(ref _commandsQueued);
        return true;
    }

    private static bool IsActionStatus(RemotePlayableActionStatus status) =>
        status is >= RemotePlayableActionStatus.NotRequested and <= RemotePlayableActionStatus.NotGrounded;

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Remote playable client is owner-thread-bound.");
    }
}
