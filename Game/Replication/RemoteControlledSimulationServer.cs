using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Network;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Replication;

/// <summary>Fixed semantic bounds for the first one-owner N0.6 vertical.</summary>
public readonly record struct RemoteControlledSimulationLimits(
    ulong MaximumFutureInputTicks = 2,
    ulong MaximumPastInputTicks = 2)
{
    public static RemoteControlledSimulationLimits Default => new(2, 2);

    internal void Validate()
    {
        if (MaximumFutureInputTicks > 1_024)
            throw new ArgumentOutOfRangeException(nameof(MaximumFutureInputTicks));
        if (MaximumPastInputTicks > 1_024)
            throw new ArgumentOutOfRangeException(nameof(MaximumPastInputTicks));
    }
}

public readonly record struct RemoteControlledSimulationServerSnapshot(
    StateEpoch StateEpoch,
    uint ClockRevision,
    ulong LastQueuedInputSequence,
    ulong LastProcessedInputSequence,
    ulong AcceptedInputSamples,
    ulong RejectedInputSamples,
    ulong OwnerStatesQueued,
    ulong ClockAnchorsQueued,
    ReplicationEntityId LastOwnerEntity);

/// <summary>
/// Server adapter for one admitted participant's bounded input and latest owner state.
/// </summary>
/// <remarks>
/// Network transports bytes; this game-owned adapter validates input against the
/// active projection/time domain, publishes it through the existing bounded
/// authoritative ingress, and emits one complete owner-state sample. The message
/// family therefore has one logical latest-state stream; AOI/fanout remain N0.7.
/// </remarks>
public sealed class RemoteControlledSimulationServer : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ServerGameFacet _runtime;
    private readonly GameProjectionService _projection;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly RemoteControlledSimulationLimits _limits;
    private readonly RemoteAuthoritativeInputSample[] _inputScratch =
        new RemoteAuthoritativeInputSample[UltraSimGameProtocol.MaximumInputRedundancy];
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _highestQueuedInput;
    private ulong _lastProcessedInput;
    private ulong _acceptedInputs;
    private ulong _rejectedInputs;
    private ulong _ownerStatesQueued;
    private ulong _clockAnchorsQueued;
    private uint _clockRevision;
    private int _lastFrequency;
    private ReplicationEntityId _lastOwnerEntity;
    private RemoteOwnerStateMessage _lastOwnerState;

    public RemoteControlledSimulationServer(
        NetworkProtocolPeer peer,
        ServerGameFacet runtime,
        GameProjectionService projection,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        RemoteControlledSimulationLimits limits = default)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        if (!participant.IsValid) throw new ArgumentException("A valid participant is required.", nameof(participant));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (limits == default) limits = RemoteControlledSimulationLimits.Default;
        limits.Validate();
        _participant = participant;
        _applicationSessionEpoch = applicationSessionEpoch;
        _limits = limits;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId != RemoteAuthoritativeInputBundleHeaderCodec.Descriptor.Id)
            return false;
        if (!UltraSimGameProtocol.TryReadAuthoritativeInputBundle(
                payload, out RemoteAuthoritativeInputBundleHeader header,
                _inputScratch, out int count) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            !_projection.StateEpoch.IsValid || header.StateEpoch != _projection.StateEpoch.Value ||
            !_runtime.TryCaptureClockSnapshot(out WorldClockSnapshot clock))
        {
            _rejectedInputs++;
            return true;
        }

        for (int index = 0; index < count; index++)
        {
            ref readonly RemoteAuthoritativeInputSample sample = ref _inputScratch[index];
            if (sample.Sequence <= _lastProcessedInput)
            {
                _rejectedInputs++;
                continue;
            }
            _lastProcessedInput = sample.Sequence;
            if (
                sample.ExpiresAtWorldTimeMicroseconds < clock.WorldTimeMicroseconds ||
                IsTooFarFuture(sample.TargetSimulationTick, clock.SimulationTick) ||
                IsTooFarPast(sample.TargetSimulationTick, clock.SimulationTick))
            {
                _rejectedInputs++;
                continue;
            }
            GameSimulationCommandKind kind = sample.Kind switch
            {
                RemoteAuthoritativeInputKind.ApplyTickMarkerDelta =>
                    GameSimulationCommandKind.ApplyTickMarkerDelta,
                RemoteAuthoritativeInputKind.SetInputVelocity =>
                    GameSimulationCommandKind.SetInputVelocity,
                _ => throw new InvalidOperationException("A decoded input kind escaped canonical validation."),
            };
            var input = new GameAuthoritativeInput(
                _participant, sample.Sequence, kind, sample.Value);
            if (!_projection.TryEnqueueAuthoritativeInput(in input))
            {
                _rejectedInputs++;
                continue;
            }
            _highestQueuedInput = sample.Sequence;
            _acceptedInputs++;
        }
        _inputScratch.AsSpan(0, count).Clear();
        return true;
    }

    /// <summary>Publishes validated decoded intent into the next authoritative Input stage.</summary>
    public GameAuthoritativeIngressResult PublishInputs()
    {
        EnsureOwnerThread();
        return _projection.PublishAuthoritativeIngress();
    }

    /// <summary>
    /// Captures after a complete authoritative tick and queues clock-before-state
    /// interpretation plus one complete latest owner sample.
    /// </summary>
    public bool CaptureAndQueueOwnerState(out GameProjectionCaptureResult capture)
    {
        EnsureOwnerThread();
        capture = _projection.CaptureAfterTick();
        if (!capture.Succeeded || !_runtime.TryCaptureClockSnapshot(out WorldClockSnapshot clock))
            return false;

        uint revision = _clockRevision;
        bool cadenceChanged = revision == 0 || clock.SimulationFrequencyHz != _lastFrequency;
        if (cadenceChanged)
        {
            if (revision == uint.MaxValue) return false;
            uint candidate = revision + 1;
            var anchor = new RemoteClockAnchorMessage
            {
                ApplicationSessionEpoch = _applicationSessionEpoch,
                StateEpoch = capture.Frame.StateEpoch.Value,
                ClockRevision = candidate,
                EffectiveFromSimulationTick = clock.SimulationTick,
                SimulationFrequencyHz = clock.SimulationFrequencyHz,
                WorldTimeMicroseconds = clock.WorldTimeMicroseconds,
                WorldTimeFractionQuanta = clock.WorldTimeFractionQuanta,
            };
            int anchorBytes = RemoteClockAnchorMessageCodec.EncodedSize;
            if (!RemoteClockAnchorMessageCodec.TryWriteExact(
                    _messageScratch.AsSpan(0, anchorBytes), in anchor) ||
                _peer.TryQueue(RemoteClockAnchorMessageCodec.Descriptor.Id,
                    _messageScratch.AsSpan(0, anchorBytes)) != NetworkQueueStatus.Accepted)
                return false;
            revision = candidate;
            _clockRevision = candidate;
            _lastFrequency = clock.SimulationFrequencyHz;
            _clockAnchorsQueued++;
        }

        bool present = _projection.TryGetOwnerState(_participant, out GameProjectionOwnerState owner);
        if (present) _lastOwnerEntity = owner.Entity.Entity;
        if (!_lastOwnerEntity.IsValid) return false;
        GameSimulationState simulation = present ? owner.Entity.SimulationState : default;
        var state = new RemoteOwnerStateMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            StateEpoch = capture.Frame.StateEpoch.Value,
            EntityIndex = _lastOwnerEntity.Index,
            EntityGeneration = _lastOwnerEntity.Generation,
            SimulationTick = capture.Frame.SimulationTick,
            FrameSequence = capture.Frame.FrameSequence,
            LastProcessedInputSequence = _lastProcessedInput,
            TickMarker = simulation.TickMarker,
            InputVelocity = simulation.InputVelocity,
            ClockRevision = revision,
            IsPresent = present,
        };
        int stateBytes = RemoteOwnerStateMessageCodec.EncodedSize;
        if (!RemoteOwnerStateMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, stateBytes), in state) ||
            _peer.TryQueue(RemoteOwnerStateMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, stateBytes)) != NetworkQueueStatus.Accepted)
            return false;
        _lastOwnerState = state;
        _ownerStatesQueued++;
        return true;
    }

    /// <summary>Requeues the last complete owner sample after unreliable loss.</summary>
    public bool TryRepeatOwnerState()
    {
        EnsureOwnerThread();
        if (_lastOwnerState.EntityGeneration == 0) return false;
        int bytes = RemoteOwnerStateMessageCodec.EncodedSize;
        return RemoteOwnerStateMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, bytes), in _lastOwnerState) &&
            _peer.TryQueue(RemoteOwnerStateMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes)) == NetworkQueueStatus.Accepted;
    }

    public RemoteControlledSimulationServerSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_projection.StateEpoch, _clockRevision, _highestQueuedInput, _lastProcessedInput,
            _acceptedInputs, _rejectedInputs, _ownerStatesQueued,
            _clockAnchorsQueued, _lastOwnerEntity);
    }

    private bool IsTooFarFuture(ulong target, ulong current) =>
        target > current && target - current > _limits.MaximumFutureInputTicks;

    private bool IsTooFarPast(ulong target, ulong current) =>
        current > target && current - target > _limits.MaximumPastInputTicks;

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The controlled simulation server is owner-thread-bound.");
    }
}
