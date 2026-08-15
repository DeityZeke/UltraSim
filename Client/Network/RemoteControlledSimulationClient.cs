using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public readonly record struct RemoteControlledSimulationClientSnapshot(
    StateEpoch StateEpoch,
    uint ClockRevision,
    ulong EffectiveFromSimulationTick,
    int SimulationFrequencyHz,
    long WorldTimeMicroseconds,
    int WorldTimeFractionQuanta,
    bool OwnerPresent,
    ReplicationEntityId OwnerEntity,
    ClientEntitySlot OwnerSlot,
    ulong SimulationTick,
    ulong FrameSequence,
    ulong LastProcessedInputSequence,
    int AuthoritativeTickMarker,
    int AuthoritativeInputVelocity,
    int PredictedTickMarker,
    int PredictedInputVelocity,
    int PendingInputs,
    ulong RejectedMessages);

/// <summary>
/// Client projection for the first one-owner N0.6 state stream.
/// </summary>
/// <remarks>
/// Input bundles repeat up to three still-unacknowledged samples. Owner state is
/// a complete superseding sample, never a per-entity delta queue. A state that
/// references a future clock revision is staged until its reliable anchor arrives.
/// </remarks>
public sealed class RemoteControlledSimulationClient : IRemoteGameProtocolHandler
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly uint[] _replicationGenerations;
    private readonly uint[] _clientGenerations;
    private readonly bool[] _present;
    private readonly RemoteAuthoritativeInputSample[] _pendingInputs;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private StateEpoch _stateEpoch;
    private uint _clockRevision;
    private ulong _effectiveTick;
    private int _frequency;
    private long _worldTime;
    private int _worldFraction;
    private ulong _nextInputSequence = 1;
    private int _pendingInputCount;
    private ReplicationEntityId _ownerEntity;
    private ClientEntitySlot _ownerSlot;
    private ulong _simulationTick;
    private ulong _frameSequence;
    private ulong _lastAdmittedInput;
    private int _authoritativeMarker;
    private int _authoritativeVelocity;
    private int _predictedMarker;
    private int _predictedVelocity;
    private RemoteOwnerStateMessage _pendingOwnerState;
    private bool _hasPendingOwnerState;
    private ulong _rejected;

    public RemoteControlledSimulationClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        int maximumEntities = 4_096,
        int maximumPendingInputs = 64)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (applicationSessionEpoch == 0) throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (maximumEntities <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntities));
        if (maximumPendingInputs <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPendingInputs));
        _applicationSessionEpoch = applicationSessionEpoch;
        _replicationGenerations = new uint[maximumEntities];
        _clientGenerations = new uint[maximumEntities];
        _present = new bool[maximumEntities];
        _pendingInputs = new RemoteAuthoritativeInputSample[maximumPendingInputs];
    }

    public bool TryQueueInput(
        ulong targetSimulationTick,
        long expiresAtWorldTimeMicroseconds,
        RemoteAuthoritativeInputKind kind,
        int value)
    {
        EnsureOwnerThread();
        if (!_stateEpoch.IsValid || _nextInputSequence == 0 ||
            _pendingInputCount == _pendingInputs.Length ||
            targetSimulationTick == ulong.MaxValue || expiresAtWorldTimeMicroseconds < 0 ||
            (uint)kind > (uint)RemoteAuthoritativeInputKind.SetInputVelocity) return false;
        var candidate = new RemoteAuthoritativeInputSample(
            _nextInputSequence, targetSimulationTick,
            expiresAtWorldTimeMicroseconds, kind, value);
        _pendingInputs[_pendingInputCount++] = candidate;
        bool queued = TryQueuePendingBundle();
        if (!queued)
        {
            _pendingInputs[--_pendingInputCount] = default;
            return false;
        }
        _nextInputSequence = _nextInputSequence == ulong.MaxValue ? 0 : _nextInputSequence + 1;
        ApplyPredicted(in candidate);
        return true;
    }

    /// <summary>Requeues the newest complete redundancy bundle without creating new intent.</summary>
    public bool TryRepeatPendingInputBundle()
    {
        EnsureOwnerThread();
        return _pendingInputCount != 0 && TryQueuePendingBundle();
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwnerThread();
        if (message.MessageId == RemoteClockAnchorMessageCodec.Descriptor.Id)
        {
            ProcessClockAnchor(payload);
            return true;
        }
        if (message.MessageId == RemoteOwnerStateMessageCodec.Descriptor.Id)
        {
            ProcessOwnerState(payload);
            return true;
        }
        return false;
    }

    public RemoteControlledSimulationClientSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_stateEpoch, _clockRevision, _effectiveTick, _frequency,
            _worldTime, _worldFraction, _ownerEntity.IsValid,
            _ownerEntity, _ownerSlot, _simulationTick, _frameSequence,
            _lastAdmittedInput, _authoritativeMarker, _authoritativeVelocity,
            _predictedMarker, _predictedVelocity, _pendingInputCount, _rejected);
    }

    private bool TryQueuePendingBundle()
    {
        int count = Math.Min(UltraSimGameProtocol.MaximumInputRedundancy, _pendingInputCount);
        ReadOnlySpan<RemoteAuthoritativeInputSample> samples =
            _pendingInputs.AsSpan(_pendingInputCount - count, count);
        if (!UltraSimGameProtocol.TryWriteAuthoritativeInputBundle(
                _messageScratch, _applicationSessionEpoch, _stateEpoch,
                samples, out int written)) return false;
        return _peer.TryQueue(RemoteAuthoritativeInputBundleHeaderCodec.Descriptor.Id,
            _messageScratch.AsSpan(0, written)) == NetworkQueueStatus.Accepted;
    }

    private void ProcessClockAnchor(ReadOnlySpan<byte> payload)
    {
        if (!RemoteClockAnchorMessageCodec.TryReadExact(payload, out RemoteClockAnchorMessage anchor) ||
            anchor.ApplicationSessionEpoch != _applicationSessionEpoch || anchor.StateEpoch == 0 ||
            anchor.ClockRevision == 0 || anchor.SimulationFrequencyHz is <= 0 or > 1_000 ||
            anchor.WorldTimeMicroseconds < 0 || anchor.WorldTimeFractionQuanta is < 0 or >= 3_600_000)
        {
            _rejected++;
            return;
        }
        var epoch = new StateEpoch(anchor.StateEpoch);
        if (!_stateEpoch.IsValid)
            _stateEpoch = epoch;
        else if (_stateEpoch != epoch)
        {
            _rejected++;
            return;
        }
        if (_clockRevision == uint.MaxValue)
        {
            _rejected++;
            return;
        }
        uint expected = _clockRevision == 0 ? 1U : _clockRevision + 1U;
        if (anchor.ClockRevision != expected)
        {
            _rejected++;
            return;
        }
        _clockRevision = anchor.ClockRevision;
        _effectiveTick = anchor.EffectiveFromSimulationTick;
        _frequency = anchor.SimulationFrequencyHz;
        _worldTime = anchor.WorldTimeMicroseconds;
        _worldFraction = anchor.WorldTimeFractionQuanta;
        if (_hasPendingOwnerState && _pendingOwnerState.ClockRevision == _clockRevision)
        {
            RemoteOwnerStateMessage pending = _pendingOwnerState;
            _pendingOwnerState = default;
            _hasPendingOwnerState = false;
            ApplyOwnerState(in pending);
        }
    }

    private void ProcessOwnerState(ReadOnlySpan<byte> payload)
    {
        if (!RemoteOwnerStateMessageCodec.TryReadExact(payload, out RemoteOwnerStateMessage state) ||
            state.ApplicationSessionEpoch != _applicationSessionEpoch || state.StateEpoch == 0 ||
            state.EntityGeneration == 0 || state.ClockRevision == 0 ||
            !_stateEpoch.IsValid || state.StateEpoch != _stateEpoch.Value)
        {
            _rejected++;
            return;
        }
        if (state.ClockRevision > _clockRevision)
        {
            if (!_hasPendingOwnerState || state.FrameSequence > _pendingOwnerState.FrameSequence)
            {
                _pendingOwnerState = state;
                _hasPendingOwnerState = true;
            }
            return;
        }
        if (state.ClockRevision != _clockRevision || !ApplyOwnerState(in state))
            _rejected++;
    }

    private bool ApplyOwnerState(in RemoteOwnerStateMessage state)
    {
        if (state.FrameSequence <= _frameSequence ||
            state.LastProcessedInputSequence >= _nextInputSequence ||
            state.EntityIndex >= _replicationGenerations.Length) return false;
        var entity = new ReplicationEntityId(state.EntityIndex, state.EntityGeneration);
        uint knownGeneration = _replicationGenerations[state.EntityIndex];
        if (state.EntityGeneration < knownGeneration) return false;

        if (!state.IsPresent)
        {
            if (_ownerEntity != entity || !_present[state.EntityIndex]) return false;
            _present[state.EntityIndex] = false;
            _ownerEntity = default;
            _ownerSlot = default;
        }
        else
        {
            if (state.EntityGeneration > knownGeneration)
            {
                if (_clientGenerations[state.EntityIndex] == uint.MaxValue) return false;
                _replicationGenerations[state.EntityIndex] = state.EntityGeneration;
                _clientGenerations[state.EntityIndex]++;
                if (_clientGenerations[state.EntityIndex] == 0) return false;
                _present[state.EntityIndex] = true;
            }
            else if (!_present[state.EntityIndex])
            {
                return false; // tombstoned identities cannot resurrect
            }
            _ownerEntity = entity;
            _ownerSlot = new ClientEntitySlot(state.EntityIndex, _clientGenerations[state.EntityIndex]);
        }

        _simulationTick = state.SimulationTick;
        _frameSequence = state.FrameSequence;
        _lastAdmittedInput = state.LastProcessedInputSequence;
        _authoritativeMarker = state.TickMarker;
        _authoritativeVelocity = state.InputVelocity;
        DiscardAcknowledgedInputs(state.LastProcessedInputSequence);
        _predictedMarker = _authoritativeMarker;
        _predictedVelocity = _authoritativeVelocity;
        for (int index = 0; index < _pendingInputCount; index++)
            ApplyPredicted(in _pendingInputs[index]);
        return true;
    }

    private void DiscardAcknowledgedInputs(ulong acknowledged)
    {
        int remove = 0;
        while (remove < _pendingInputCount && _pendingInputs[remove].Sequence <= acknowledged) remove++;
        if (remove == 0) return;
        _pendingInputs.AsSpan(remove, _pendingInputCount - remove).CopyTo(_pendingInputs);
        _pendingInputs.AsSpan(_pendingInputCount - remove, remove).Clear();
        _pendingInputCount -= remove;
    }

    private void ApplyPredicted(in RemoteAuthoritativeInputSample sample)
    {
        switch (sample.Kind)
        {
            case RemoteAuthoritativeInputKind.ApplyTickMarkerDelta:
                _predictedMarker = unchecked(_predictedMarker + sample.Value);
                break;
            case RemoteAuthoritativeInputKind.SetInputVelocity:
                _predictedVelocity = sample.Value;
                break;
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The controlled simulation client is owner-thread-bound.");
    }
}
