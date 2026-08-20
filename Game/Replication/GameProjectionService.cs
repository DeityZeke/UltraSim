using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Network;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Replication;

/// <summary>
/// Single-owner canonical game projection captured after complete authoritative steps.
/// </summary>
/// <remarks>
/// This service owns derived state only. Expected capacity/history failures are
/// returned without acknowledging ECS cursors. Unexpected projection failures are
/// contained and fault this service rather than escaping the trusted Runtime callback.
/// </remarks>
public sealed class GameProjectionService : IDisposable
{
    private readonly ServerGameFacet _runtime;
    private readonly AdmissionService _admission;
    private readonly GameReplicationLimits _limits;
    private readonly int _ownerThreadId;
    private readonly uint[] _runtimeGenerations;
    private readonly uint[] _replicationGenerations;
    private readonly GameProjectedEntity[] _entities;
    private readonly GameProjectedEntity[] _workingEntities;
    private readonly uint[] _workingRuntimeGenerations;
    private readonly uint[] _workingReplicationGenerations;
    private readonly uint[] _workingStamps;
    private readonly uint[] _touchedIndices;
    private readonly Entity[] _baselineEntities;
    private readonly GameSimulationState[] _baselineSimulation;
    private readonly bool[] _baselineHasSimulation;
    private readonly LifecycleRecord[] _lifecycleScratch;
    private readonly ChangedValue[] _changedScratch;
    private readonly GameProjectionDelta[] _stagedDeltas;
    private readonly bool[] _stagedIsLifecycle;
    private readonly ulong[] _stagedSourceSequences;
    private readonly LifecycleKind[] _stagedLifecycleKinds;
    private readonly GameProjectionDelta[] _lastDeltas;
    private readonly GameProjectionLifecycleFact[] _lifecycleHistory;
    private readonly GameApplicationEvent[] _applicationEvents;
    private readonly ulong[] _applicationEventNextParticipantSequence;
    private readonly ParticipantId[] _applicationEventConsumers;
    private readonly ulong[] _applicationEventConsumerCursors;
    private readonly ulong[] _applicationEventConsumerNextRelevantSequences;
    private readonly Dictionary<ParticipantId, int> _applicationEventConsumerMap;
    private readonly GameAuthoritativeInput[] _inputs;
    private readonly GameSimulationCommand[] _commandScratch;
    private readonly Dictionary<ParticipantId, ulong> _inputSequenceScratch;
    private readonly ParticipantId[] _participantScratch;
    private readonly Dictionary<ParticipantId, ParticipantState> _participants;
    private readonly int _participantMapCapacity;
    private readonly int _inputSequenceScratchCapacity;
    private readonly int _applicationEventConsumerMapCapacity;
    private readonly ServerGenerationStateOperation<GameGenerationState, GameProjectionCaptureResult> _captureOperation;
    private readonly ServerGenerationStateOperation<GameGenerationState, GameProjectionCaptureResult> _rebuildOperation;
    private readonly ServerGenerationStateOperation<GameGenerationState, IngressCommit> _ingressPublisher;

    private LifecycleCursor _lifecycleCursor;
    private ChangeCursor _changeCursor;
    private StateEpoch _stateEpoch;
    private ulong _nextStateEpoch = 1;
    private ulong _generationId;
    private ulong _baselineRevision;
    private ulong _frameSequence;
    private GameProjectionFrame _lastFrame;
    private ulong _nextDeltaSequence;
    private int _entityCount;
    private int _lastDeltaCount;
    private uint _workingStamp;
    private int _touchedCount;
    private int _lifecycleHead;
    private int _lifecycleCount;
    private ulong _lifecycleNextSequence;
    private ulong _lifecycleOverwritten;
    private int _applicationHead;
    private int _applicationCount;
    private int _applicationHighWater;
    private ulong _applicationNextSequence;
    private int _applicationEventConsumerCount;
    private ulong _applicationMinimumCursorScanEntries;
    private ulong _applicationMinimumCursorFullRescans;
    private ulong _applicationMinimumCursorFastPathUpdates;
    private ulong _applicationEventConsumerLookupAttempts;
    private ulong _applicationEventParticipantIndexHits;
    private ulong _applicationEventNoRelevantFastForwards;
    private ulong _applicationMinimumCursor;
    private int _applicationConsumersAtMinimum;
    private int _inputCount;
    private bool _rebuildRequired;
    private Exception? _fault;
    private bool _disposed;
    private ulong _ingressExpectedGeneration;
    private int _ingressAcceptedCount;

    internal Action<GameProjectionFaultPoint>? FaultInjector { get; set; }

    internal void SetSequenceStateForTest(
        ulong frameSequence,
        ulong nextDeltaSequence,
        ulong lifecycleNextSequence,
        ulong lifecycleOverwritten)
    {
        EnsureOperational();
        _frameSequence = frameSequence;
        _nextDeltaSequence = nextDeltaSequence;
        _lifecycleNextSequence = lifecycleNextSequence;
        _lifecycleOverwritten = lifecycleOverwritten;
    }

    internal void SetWorkingStampForTest(uint workingStamp)
    {
        EnsureOperational();
        _workingStamp = workingStamp;
    }

    public GameProjectionService(
        ServerGameFacet runtime,
        AdmissionService admission,
        GameReplicationLimits limits = default)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        if (limits == default) limits = GameReplicationLimits.Default;
        limits.Validate();
        _limits = limits;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _runtimeGenerations = new uint[limits.MaximumEntities];
        _replicationGenerations = new uint[limits.MaximumEntities];
        _entities = new GameProjectedEntity[limits.MaximumEntities];
        _workingEntities = new GameProjectedEntity[limits.MaximumEntities];
        _workingRuntimeGenerations = new uint[limits.MaximumEntities];
        _workingReplicationGenerations = new uint[limits.MaximumEntities];
        _workingStamps = new uint[limits.MaximumEntities];
        _touchedIndices = new uint[Math.Min(limits.MaximumEntities, limits.MaximumDeltasPerCapture)];
        _baselineEntities = new Entity[limits.MaximumEntities];
        _baselineSimulation = new GameSimulationState[limits.MaximumEntities];
        _baselineHasSimulation = new bool[limits.MaximumEntities];
        _lifecycleScratch = new LifecycleRecord[limits.MaximumLifecycleFactsPerCapture];
        _changedScratch = new ChangedValue[limits.MaximumDeltasPerCapture];
        _stagedDeltas = new GameProjectionDelta[limits.MaximumDeltasPerCapture];
        _stagedIsLifecycle = new bool[limits.MaximumDeltasPerCapture];
        _stagedSourceSequences = new ulong[limits.MaximumDeltasPerCapture];
        _stagedLifecycleKinds = new LifecycleKind[limits.MaximumDeltasPerCapture];
        _lastDeltas = new GameProjectionDelta[limits.MaximumDeltasPerCapture];
        _lifecycleHistory = new GameProjectionLifecycleFact[limits.LifecycleHistoryCapacity];
        _applicationEvents = new GameApplicationEvent[limits.ApplicationEventCapacity];
        _applicationEventNextParticipantSequence = new ulong[limits.ApplicationEventCapacity];
        Array.Fill(_applicationEventNextParticipantSequence, ulong.MaxValue);
        _applicationEventConsumers = new ParticipantId[limits.MaximumParticipants];
        _applicationEventConsumerCursors = new ulong[limits.MaximumParticipants];
        _applicationEventConsumerNextRelevantSequences = new ulong[limits.MaximumParticipants];
        Array.Fill(_applicationEventConsumerNextRelevantSequences, ulong.MaxValue);
        _applicationEventConsumerMap = new Dictionary<ParticipantId, int>(limits.MaximumParticipants);
        _applicationEventConsumerMapCapacity = _applicationEventConsumerMap.EnsureCapacity(limits.MaximumParticipants);
        _inputs = new GameAuthoritativeInput[limits.AuthoritativeInputCapacity];
        _commandScratch = new GameSimulationCommand[limits.AuthoritativeInputCapacity];
        _inputSequenceScratch = new Dictionary<ParticipantId, ulong>(limits.MaximumParticipants);
        _inputSequenceScratchCapacity = _inputSequenceScratch.EnsureCapacity(limits.MaximumParticipants);
        _participantScratch = new ParticipantId[limits.MaximumParticipants];
        _participants = new Dictionary<ParticipantId, ParticipantState>(limits.MaximumParticipants);
        _participantMapCapacity = _participants.EnsureCapacity(limits.MaximumParticipants);
        _captureOperation = CaptureInsideCallback;
        _rebuildOperation = RebuildInsideCallback;
        _ingressPublisher = PublishIngressInsideCallback;
        _admission.Entered += HandleAdmissionEntered;
        _admission.Rejected += HandleAdmissionRejected;
        _runtime.GenerationPublished += HandleGenerationPublished;
    }

    public StateEpoch StateEpoch
    {
        get { EnsureOperational(); return _stateEpoch; }
    }

    public bool IsFaulted
    {
        get { EnsureOwnerThread(); return _fault is not null; }
    }

    /// <summary>Synchronizes one pre-existing or newly admitted participant into projection ownership.</summary>
    public bool SynchronizeParticipant(ParticipantId participant)
    {
        EnsureOperational();
        if (!participant.IsValid || !_admission.TryGetSession(participant, out AdmissionResult session))
            return false;
        UpdateParticipant(session);
        return session.IsEntered && _participants.ContainsKey(participant);
    }

    /// <summary>Captures committed lifecycle and changed values after a complete Runtime step.</summary>
    public GameProjectionCaptureResult CaptureAfterTick()
    {
        EnsureOperational();
        if (_fault is not null)
            return Failure(GameProjectionCaptureStatus.Faulted, _fault.Message);
        if (_rebuildRequired)
            return Failure(GameProjectionCaptureStatus.RebuildRequired, "The projection requires an explicit baseline rebuild.");

        if (!_runtime.TryUseActiveGeneration<GameGenerationState, GameProjectionCaptureResult>(
            _captureOperation,
            out GameProjectionCaptureResult result))
        {
            return Failure(GameProjectionCaptureStatus.RuntimeNotRunning, "Projection capture requires a running Runtime.");
        }
        if (result.Succeeded)
            BindParticipantsToProjection();
        return result;
    }

    /// <summary>Explicitly rebuilds canonical truth and resets generation-local histories.</summary>
    public GameProjectionCaptureResult RebuildBaseline()
    {
        EnsureOperational();
        if (_fault is not null)
            return Failure(GameProjectionCaptureStatus.Faulted, _fault.Message);
        if (!_runtime.TryUseActiveGeneration<GameGenerationState, GameProjectionCaptureResult>(
            _rebuildOperation,
            out GameProjectionCaptureResult result))
        {
            return Failure(GameProjectionCaptureStatus.RuntimeNotRunning, "Projection rebuild requires a running Runtime.");
        }
        if (result.Succeeded)
            BindParticipantsToProjection();
        return result;
    }

    /// <summary>Copies current active projected entities in canonical slot order.</summary>
    public int CopyEntities(Span<GameProjectedEntity> destination)
    {
        EnsureOperational();
        if (destination.Length < _entityCount)
            throw new ArgumentException("Destination cannot contain every projected entity.", nameof(destination));
        int written = 0;
        for (int index = 0; index < _entities.Length; index++)
            if (_entities[index].Entity.IsValid) destination[written++] = _entities[index];
        return written;
    }

    /// <summary>Copies current entered participant bindings sorted by participant identity.</summary>
    public int CopyParticipants(Span<GameProjectionParticipant> destination)
    {
        EnsureOperational();
        int count = 0;
        foreach (ParticipantState state in _participants.Values)
        {
            if (!state.Binding.ControlledEntity.IsValid) continue;
            if (count >= destination.Length)
                throw new ArgumentException("Destination cannot contain every projected participant.", nameof(destination));
            destination[count++] = state.Binding;
        }
        destination[..count].Sort(static (left, right) => left.Participant.CompareTo(right.Participant));
        return count;
    }

    /// <summary>Resolves the current canonical state for one entered participant.</summary>
    public bool TryGetOwnerState(ParticipantId participant, out GameProjectionOwnerState owner)
    {
        EnsureOperational();
        if (!_participants.TryGetValue(participant, out ParticipantState state) ||
            !state.Binding.ControlledEntity.IsValid)
        {
            owner = default;
            return false;
        }
        ReplicationEntityId id = state.Binding.ControlledEntity;
        if (id.Index >= _entities.Length || _entities[id.Index].Entity != id)
        {
            owner = default;
            return false;
        }
        owner = new(state.Binding, _entities[id.Index], state.LastAcceptedInputSequence);
        return true;
    }

    public int CopyLastFrameDeltas(Span<GameProjectionDelta> destination)
    {
        EnsureOperational();
        if (destination.Length < _lastDeltaCount)
            throw new ArgumentException("Destination cannot contain the complete frame.", nameof(destination));
        _lastDeltas.AsSpan(0, _lastDeltaCount).CopyTo(destination);
        return _lastDeltaCount;
    }

    public GameProjectionFrame GetLastFrame()
    {
        EnsureOperational();
        return _lastFrame;
    }

    /// <summary>Copies retained projection-lifecycle history without advancing hidden state.</summary>
    public GameProjectionHistoryReadResult ReadLifecycle(
        ulong nextSequence,
        Span<GameProjectionLifecycleFact> destination)
    {
        EnsureOperational();
        ulong oldest = unchecked(_lifecycleNextSequence - (ulong)_lifecycleCount);
        if (nextSequence < oldest)
            return new(GameProjectionHistoryReadStatus.HistoryLost, 0, nextSequence, oldest, _lifecycleNextSequence);
        if (nextSequence > _lifecycleNextSequence)
            return new(GameProjectionHistoryReadStatus.FutureSequence, 0, nextSequence, oldest, _lifecycleNextSequence);
        ulong pending = unchecked(_lifecycleNextSequence - nextSequence);
        if (pending == 0)
            return new(GameProjectionHistoryReadStatus.NoData, 0, nextSequence, oldest, _lifecycleNextSequence);
        int count = Math.Min(destination.Length, checked((int)pending));
        CopyRing(_lifecycleHistory, _lifecycleHead, oldest, nextSequence, destination[..count]);
        return new(GameProjectionHistoryReadStatus.Success, count, unchecked(nextSequence + (ulong)count), oldest,
            _lifecycleNextSequence);
    }

    /// <summary>Retains one explicit application event; full retention rejects without mutation.</summary>
    public bool TryPublishApplicationEvent(
        ParticipantId participant,
        ushort kind,
        ulong subject,
        ulong payloadHash,
        out GameApplicationEvent applicationEvent)
    {
        EnsureOperational();
        if (!_stateEpoch.IsValid || !participant.IsValid ||
            !_participants.TryGetValue(participant, out ParticipantState participantState) || kind == 0 ||
            _applicationCount == _applicationEvents.Length ||
            _applicationNextSequence == ulong.MaxValue)
        {
            applicationEvent = default;
            return false;
        }
        int index = (_applicationHead + _applicationCount) % _applicationEvents.Length;
        int priorParticipantTailIndex = participantState.ApplicationEventTailSequence == ulong.MaxValue
            ? -1
            : ApplicationEventRingIndex(participantState.ApplicationEventTailSequence);
        applicationEvent = new GameApplicationEvent(
            _applicationNextSequence++, _stateEpoch, participant, kind, subject, payloadHash);
        _applicationEvents[index] = applicationEvent;
        _applicationEventNextParticipantSequence[index] = ulong.MaxValue;
        if (priorParticipantTailIndex >= 0)
        {
            _applicationEventNextParticipantSequence[priorParticipantTailIndex] = applicationEvent.Sequence;
        }
        else
        {
            participantState.ApplicationEventHeadSequence = applicationEvent.Sequence;
        }
        participantState.ApplicationEventTailSequence = applicationEvent.Sequence;
        _participants[participant] = participantState;
        if (_applicationEventConsumerMap.TryGetValue(participant, out int consumer) &&
            _applicationEventConsumerNextRelevantSequences[consumer] == ulong.MaxValue &&
            applicationEvent.Sequence >= _applicationEventConsumerCursors[consumer])
        {
            _applicationEventConsumerNextRelevantSequences[consumer] = applicationEvent.Sequence;
        }
        _applicationCount++;
        _applicationHighWater = Math.Max(_applicationHighWater, _applicationCount);
        return true;
    }

    public GameProjectionHistoryReadResult ReadApplicationEvents(
        ulong nextSequence,
        Span<GameApplicationEvent> destination)
    {
        EnsureOperational();
        ulong oldest = unchecked(_applicationNextSequence - (ulong)_applicationCount);
        if (nextSequence < oldest)
            return new(GameProjectionHistoryReadStatus.HistoryLost, 0, nextSequence, oldest, _applicationNextSequence);
        if (nextSequence > _applicationNextSequence)
            return new(GameProjectionHistoryReadStatus.FutureSequence, 0, nextSequence, oldest,
                _applicationNextSequence);
        ulong pending = unchecked(_applicationNextSequence - nextSequence);
        if (pending == 0)
            return new(GameProjectionHistoryReadStatus.NoData, 0, nextSequence, oldest, _applicationNextSequence);
        int count = Math.Min(destination.Length, checked((int)pending));
        CopyRing(_applicationEvents, _applicationHead, oldest, nextSequence, destination[..count]);
        return new(GameProjectionHistoryReadStatus.Success, count, unchecked(nextSequence + (ulong)count), oldest,
            _applicationNextSequence);
    }

    /// <summary>
    /// Reads the next retained Event relevant to one registered participant.
    /// Global Event sequence remains the durable ordering identity; the
    /// participant index only removes unrelated-record traversal.
    /// </summary>
    internal GameProjectionHistoryReadResult ReadApplicationEvents(
        ParticipantId participant,
        ulong nextSequence,
        Span<GameApplicationEvent> destination)
    {
        EnsureOperational();
        ulong oldest = unchecked(_applicationNextSequence - (ulong)_applicationCount);
        if (nextSequence < oldest)
            return new(GameProjectionHistoryReadStatus.HistoryLost, 0, nextSequence, oldest, _applicationNextSequence);
        if (nextSequence > _applicationNextSequence)
            return new(GameProjectionHistoryReadStatus.FutureSequence, 0, nextSequence, oldest,
                _applicationNextSequence);
        if (!_applicationEventConsumerMap.TryGetValue(participant, out int consumer))
            return new(GameProjectionHistoryReadStatus.HistoryLost, 0, nextSequence, oldest,
                _applicationNextSequence);
        if (nextSequence != _applicationEventConsumerCursors[consumer])
            return new(nextSequence < _applicationEventConsumerCursors[consumer]
                    ? GameProjectionHistoryReadStatus.HistoryLost
                    : GameProjectionHistoryReadStatus.FutureSequence,
                0, nextSequence, oldest, _applicationNextSequence);

        ulong relevant = _applicationEventConsumerNextRelevantSequences[consumer];
        while (relevant != ulong.MaxValue && relevant < nextSequence)
            relevant = _applicationEventNextParticipantSequence[ApplicationEventRingIndex(relevant)];
        _applicationEventConsumerNextRelevantSequences[consumer] = relevant;
        if (relevant == ulong.MaxValue)
        {
            if (nextSequence != _applicationNextSequence)
                _applicationEventNoRelevantFastForwards = SaturatingAdd(
                    _applicationEventNoRelevantFastForwards, 1);
            return new(GameProjectionHistoryReadStatus.NoData, 0, _applicationNextSequence, oldest,
                _applicationNextSequence);
        }
        if (destination.IsEmpty)
            return new(GameProjectionHistoryReadStatus.NoData, 0, relevant, oldest, _applicationNextSequence);
        destination[0] = _applicationEvents[ApplicationEventRingIndex(relevant)];
        _applicationEventParticipantIndexHits = SaturatingAdd(_applicationEventParticipantIndexHits, 1);
        return new(GameProjectionHistoryReadStatus.Success, 1, unchecked(relevant + 1), oldest,
            _applicationNextSequence);
    }

    /// <summary>
    /// Registers one admitted participant as an independent application-event
    /// consumer. The floor must still be retained; duplicate active consumers
    /// for one participant are rejected.
    /// </summary>
    internal bool TryRegisterApplicationEventConsumer(ParticipantId participant, ulong nextSequence)
    {
        EnsureOperational();
        if (!participant.IsValid || !_participants.TryGetValue(participant, out ParticipantState state) ||
            _applicationEventConsumerCount == _applicationEventConsumers.Length ||
            _applicationEventConsumerMap.ContainsKey(participant))
            return false;
        ulong oldest = unchecked(_applicationNextSequence - (ulong)_applicationCount);
        if (nextSequence < oldest || nextSequence > _applicationNextSequence)
            return false;
        int consumer = _applicationEventConsumerCount;
        ulong relevant = state.ApplicationEventHeadSequence;
        while (relevant != ulong.MaxValue && relevant < nextSequence)
            relevant = _applicationEventNextParticipantSequence[ApplicationEventRingIndex(relevant)];
        _applicationEventConsumers[consumer] = participant;
        _applicationEventConsumerCursors[consumer] = nextSequence;
        _applicationEventConsumerNextRelevantSequences[consumer] = relevant;
        _applicationEventConsumerMap.Add(participant, consumer);
        _applicationEventConsumerCount++;
        AddApplicationCursorToMinimum(nextSequence);
        return true;
    }

    /// <summary>
    /// Advances one participant's independent application cursor and releases
    /// only the prefix proven consumed or skipped by every registered consumer.
    /// </summary>
    internal bool TryAdvanceApplicationEventConsumer(ParticipantId participant, ulong nextSequence)
    {
        EnsureOperational();
        int consumer = FindApplicationEventConsumer(participant);
        if (consumer < 0 || nextSequence < _applicationEventConsumerCursors[consumer] ||
            nextSequence > _applicationNextSequence)
            return false;
        ulong prior = _applicationEventConsumerCursors[consumer];
        if (prior == nextSequence) return true;
        _applicationEventConsumerCursors[consumer] = nextSequence;
        ulong relevant = _applicationEventConsumerNextRelevantSequences[consumer];
        while (relevant != ulong.MaxValue && relevant < nextSequence)
            relevant = _applicationEventNextParticipantSequence[ApplicationEventRingIndex(relevant)];
        _applicationEventConsumerNextRelevantSequences[consumer] = relevant;
        AdvanceApplicationCursorMinimum(prior);
        return true;
    }

    /// <summary>Rebinds an existing consumer after this projection publishes a strictly newer epoch.</summary>
    internal bool TryRebindApplicationEventConsumer(
        ParticipantId participant,
        StateEpoch priorEpoch,
        ulong nextSequence)
    {
        EnsureOperational();
        int consumer = FindApplicationEventConsumer(participant);
        ulong oldest = unchecked(_applicationNextSequence - (ulong)_applicationCount);
        if (consumer < 0 || !priorEpoch.IsValid || _stateEpoch.Value <= priorEpoch.Value ||
            nextSequence < oldest || nextSequence > _applicationNextSequence)
            return false;
        ulong prior = _applicationEventConsumerCursors[consumer];
        _applicationEventConsumerCursors[consumer] = nextSequence;
        _applicationEventConsumerNextRelevantSequences[consumer] =
            FindFirstParticipantEventAtOrAfter(participant, nextSequence);
        RebuildApplicationCursorMinimum();
        if (prior == nextSequence)
            _applicationMinimumCursorFastPathUpdates = SaturatingAdd(
                _applicationMinimumCursorFastPathUpdates, 1);
        return true;
    }

    /// <summary>Removes a connection-owned consumer without advancing its cursor.</summary>
    internal bool UnregisterApplicationEventConsumer(ParticipantId participant)
    {
        EnsureOperational();
        int consumer = FindApplicationEventConsumer(participant);
        if (consumer < 0) return false;
        ulong removedCursor = _applicationEventConsumerCursors[consumer];
        int last = --_applicationEventConsumerCount;
        _applicationEventConsumers[consumer] = _applicationEventConsumers[last];
        _applicationEventConsumerCursors[consumer] = _applicationEventConsumerCursors[last];
        _applicationEventConsumerNextRelevantSequences[consumer] =
            _applicationEventConsumerNextRelevantSequences[last];
        _applicationEventConsumerMap.Remove(participant);
        if (consumer != last)
            _applicationEventConsumerMap[_applicationEventConsumers[consumer]] = consumer;
        _applicationEventConsumers[last] = default;
        _applicationEventConsumerCursors[last] = 0;
        _applicationEventConsumerNextRelevantSequences[last] = ulong.MaxValue;
        if (_applicationEventConsumerCount == 0)
        {
            _applicationMinimumCursor = 0;
            _applicationConsumersAtMinimum = 0;
        }
        else if (removedCursor == _applicationMinimumCursor && --_applicationConsumersAtMinimum == 0)
        {
            RebuildApplicationCursorMinimum();
        }
        return true;
    }

    /// <summary>Discards application events proven safe by every admitted consumer.</summary>
    public void DiscardApplicationEventsThrough(ulong exclusiveSequence)
    {
        EnsureOperational();
        if (_applicationEventConsumerCount != 0)
            throw new InvalidOperationException(
                "Registered consumers require per-consumer acknowledgement; a caller cannot discard their history globally.");
        DiscardApplicationEventsThroughCore(exclusiveSequence);
    }

    private void DiscardApplicationEventsThroughCore(ulong exclusiveSequence)
    {
        ulong oldest = unchecked(_applicationNextSequence - (ulong)_applicationCount);
        if (exclusiveSequence < oldest || exclusiveSequence > _applicationNextSequence)
            throw new ArgumentOutOfRangeException(nameof(exclusiveSequence));
        int remove = checked((int)unchecked(exclusiveSequence - oldest));
        if (remove == 0) return;
        for (int offset = 0; offset < remove; offset++)
        {
            int index = (_applicationHead + offset) % _applicationEvents.Length;
            GameApplicationEvent value = _applicationEvents[index];
            ulong nextRelevant = _applicationEventNextParticipantSequence[index];
            if (_participants.TryGetValue(value.Participant, out ParticipantState participantState) &&
                participantState.ApplicationEventHeadSequence == value.Sequence)
            {
                participantState.ApplicationEventHeadSequence = nextRelevant;
                if (nextRelevant == ulong.MaxValue)
                    participantState.ApplicationEventTailSequence = ulong.MaxValue;
                _participants[value.Participant] = participantState;
            }
            if (_applicationEventConsumerMap.TryGetValue(value.Participant, out int consumer) &&
                _applicationEventConsumerNextRelevantSequences[consumer] == value.Sequence)
            {
                _applicationEventConsumerNextRelevantSequences[consumer] = nextRelevant;
            }
            _applicationEvents[index] = default;
            _applicationEventNextParticipantSequence[index] = ulong.MaxValue;
        }
        _applicationHead = (_applicationHead + remove) % _applicationEvents.Length;
        _applicationCount -= remove;
    }

    private int FindApplicationEventConsumer(ParticipantId participant)
    {
        _applicationEventConsumerLookupAttempts = SaturatingAdd(
            _applicationEventConsumerLookupAttempts, 1);
        return _applicationEventConsumerMap.TryGetValue(participant, out int consumer) ? consumer : -1;
    }

    private void AddApplicationCursorToMinimum(ulong cursor)
    {
        if (_applicationEventConsumerCount == 1 || cursor < _applicationMinimumCursor)
        {
            _applicationMinimumCursor = cursor;
            _applicationConsumersAtMinimum = 1;
        }
        else if (cursor == _applicationMinimumCursor)
        {
            _applicationConsumersAtMinimum++;
        }
    }

    private void AdvanceApplicationCursorMinimum(ulong prior)
    {
        if (prior != _applicationMinimumCursor)
        {
            _applicationMinimumCursorFastPathUpdates = SaturatingAdd(
                _applicationMinimumCursorFastPathUpdates, 1);
            return;
        }
        _applicationConsumersAtMinimum--;
        if (_applicationConsumersAtMinimum != 0)
        {
            _applicationMinimumCursorFastPathUpdates = SaturatingAdd(
                _applicationMinimumCursorFastPathUpdates, 1);
            return;
        }
        RebuildApplicationCursorMinimum();
    }

    private void RebuildApplicationCursorMinimum()
    {
        if (_applicationEventConsumerCount == 0)
        {
            _applicationMinimumCursor = 0;
            _applicationConsumersAtMinimum = 0;
            return;
        }
        _applicationMinimumCursorFullRescans = SaturatingAdd(
            _applicationMinimumCursorFullRescans, 1);
        _applicationMinimumCursorScanEntries = SaturatingAdd(
            _applicationMinimumCursorScanEntries,
            checked((ulong)_applicationEventConsumerCount));
        ulong minimum = _applicationEventConsumerCursors[0];
        int ties = 1;
        for (int index = 1; index < _applicationEventConsumerCount; index++)
        {
            ulong cursor = _applicationEventConsumerCursors[index];
            if (cursor < minimum)
            {
                minimum = cursor;
                ties = 1;
            }
            else if (cursor == minimum)
            {
                ties++;
            }
        }
        _applicationMinimumCursor = minimum;
        _applicationConsumersAtMinimum = ties;
        DiscardApplicationEventsThroughCore(minimum);
    }

    private ulong FindFirstParticipantEventAtOrAfter(ParticipantId participant, ulong sequence)
    {
        if (!_participants.TryGetValue(participant, out ParticipantState state)) return ulong.MaxValue;
        ulong relevant = state.ApplicationEventHeadSequence;
        while (relevant != ulong.MaxValue && relevant < sequence)
            relevant = _applicationEventNextParticipantSequence[ApplicationEventRingIndex(relevant)];
        return relevant;
    }

    private void FindParticipantEventBounds(
        ParticipantId participant,
        out ulong head,
        out ulong tail)
    {
        head = ulong.MaxValue;
        tail = ulong.MaxValue;
        for (int offset = 0; offset < _applicationCount; offset++)
        {
            int index = (_applicationHead + offset) % _applicationEvents.Length;
            GameApplicationEvent value = _applicationEvents[index];
            if (value.Participant != participant) continue;
            if (head == ulong.MaxValue) head = value.Sequence;
            tail = value.Sequence;
        }
    }

    private int ApplicationEventRingIndex(ulong sequence)
    {
        ulong oldest = unchecked(_applicationNextSequence - (ulong)_applicationCount);
        if (sequence < oldest || sequence >= _applicationNextSequence)
            throw new InvalidOperationException("The participant Event index referenced unretained history.");
        return (_applicationHead + checked((int)unchecked(sequence - oldest))) % _applicationEvents.Length;
    }

    public bool TryEnqueueAuthoritativeInput(in GameAuthoritativeInput input)
    {
        EnsureOperational();
        if (!input.Participant.IsValid || input.ClientSequence == 0 ||
            (uint)input.Kind > (uint)GameSimulationCommandKind.SetInputVelocity ||
            _inputCount == _inputs.Length)
            return false;
        _inputs[_inputCount++] = input;
        return true;
    }

    /// <summary>Publishes validated preselected inputs into the generation's bounded RangeJob arena.</summary>
    public GameAuthoritativeIngressResult PublishAuthoritativeIngress()
    {
        EnsureOperational();
        if (_inputCount == 0)
            return new(GameAuthoritativeIngressStatus.NoData, 0, 0);
        int accepted = 0;
        int rejected = 0;
        ulong expectedGeneration = 0;
        _inputSequenceScratch.Clear();
        for (int index = 0; index < _inputCount; index++)
        {
            ref readonly GameAuthoritativeInput input = ref _inputs[index];
            if (!_participants.TryGetValue(input.Participant, out ParticipantState state) ||
                !state.Session.IsEntered || !state.Session.Entity.IsValid)
            {
                rejected++;
                continue;
            }
            ulong sequenceFloor = _inputSequenceScratch.TryGetValue(input.Participant, out ulong staged)
                ? staged
                : state.LastAcceptedInputSequence;
            if (input.ClientSequence <= sequenceFloor ||
                (expectedGeneration != 0 && state.Session.GenerationId != expectedGeneration))
            {
                rejected++;
                continue;
            }
            if (expectedGeneration == 0)
                expectedGeneration = state.Session.GenerationId;
            _commandScratch[accepted++] = new GameSimulationCommand(
                input.Participant,
                state.Session.Entity,
                input.ClientSequence,
                input.Kind,
                input.TickMarkerDelta);
            _inputSequenceScratch[input.Participant] = input.ClientSequence;
        }

        IngressCommit commit = default;
        if (accepted != 0)
        {
            _ingressExpectedGeneration = expectedGeneration;
            _ingressAcceptedCount = accepted;
            if (!_runtime.TryUseActiveGeneration<GameGenerationState, IngressCommit>(
                _ingressPublisher,
                out commit))
            {
                return new(GameAuthoritativeIngressStatus.RuntimeNotRunning, 0, 0);
            }
            if (!commit.Committed)
                return new(commit.Status, 0, rejected);
        }

        for (int index = 0; index < accepted; index++)
        {
            GameSimulationCommand command = _commandScratch[index];
            ParticipantState state = _participants[command.Participant];
            state.LastAcceptedInputSequence = command.ClientSequence;
            _participants[command.Participant] = state;
        }
        _inputs.AsSpan(0, _inputCount).Clear();
        _commandScratch.AsSpan(0, accepted).Clear();
        _inputCount = 0;
        return new(GameAuthoritativeIngressStatus.Success, accepted, rejected);
    }

    public GameProjectionSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        int bound = 0;
        foreach (ParticipantState state in _participants.Values)
            if (state.Binding.ControlledEntity.IsValid) bound++;
        return new(
            _stateEpoch,
            _generationId,
            _baselineRevision,
            _frameSequence,
            _entityCount,
            bound,
            _lastDeltaCount,
            _rebuildRequired,
            _fault is not null,
            _lifecycleOverwritten,
            _applicationCount,
            _applicationEventConsumerCount,
            _applicationHighWater,
            _applicationMinimumCursorScanEntries,
            _applicationMinimumCursorFullRescans,
            _applicationMinimumCursorFastPathUpdates,
            0,
            _applicationEventConsumerLookupAttempts,
            _applicationEventParticipantIndexHits,
            _applicationEventNoRelevantFastForwards,
            _inputCount,
            CalculateReservedBytes());
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        _admission.Entered -= HandleAdmissionEntered;
        _admission.Rejected -= HandleAdmissionRejected;
        _runtime.GenerationPublished -= HandleGenerationPublished;
        _participants.Clear();
        _applicationEventConsumerMap.Clear();
        _inputSequenceScratch.Clear();
        _fault = null;
        FaultInjector = null;
    }

    private GameProjectionCaptureResult CaptureInsideCallback(
        EcsWorld world,
        ulong generationId,
        GameGenerationState state)
    {
        try
        {
            if (state.Replication is null || state.Replication.WorldId != world.Id)
                return Failure(GameProjectionCaptureStatus.ReplicationNotConfigured,
                    "The active game generation was not created with the network profile.");
            if (generationId != _generationId)
                return RebuildInsideCallback(world, generationId, state);

            LifecycleCursor stagedLifecycleCursor = _lifecycleCursor;
            LifecycleReadResult lifecycle = world.ReadLifecycle(ref stagedLifecycleCursor, _lifecycleScratch);
            if (lifecycle.Status == LifecycleReadStatus.ResyncRequired)
            {
                _rebuildRequired = true;
                return Failure(GameProjectionCaptureStatus.LifecycleResyncRequired,
                    "ECS lifecycle history was overwritten before canonical capture.");
            }
            if (lifecycle.RemainingCount != 0)
                return CaptureResyncFailure(
                    "One capture interval exceeded its lifecycle scratch capacity.");

            ChangeCursor stagedChangeCursor = _changeCursor;
            var visitor = new ChangedVisitor(_changedScratch);
            int changedRows = state.Replication.ExecuteChanges(world, ref stagedChangeCursor, ref visitor);
            if (visitor.Overflowed)
                return CaptureResyncFailure(
                    "One capture interval exceeded its changed-row scratch capacity.");

            BeginWorkingSet();
            int deltaCount = 0;
            for (int index = 0; index < lifecycle.Count; index++)
            {
                if (!TryStageLifecycle(world, state, _lifecycleScratch[index], ref deltaCount, out string? error))
                    return CaptureResyncFailure(error!);
            }
            for (int index = 0; index < visitor.Count; index++)
            {
                if (!TryStageValue(world, visitor.Values[index], ref deltaCount, out string? error))
                    return CaptureResyncFailure(error!);
            }

            if (!TryPreflightCaptureCommit(deltaCount, out string? sequenceError))
                return Failure(GameProjectionCaptureStatus.CapacityExceeded, sequenceError!);

            FaultInjector?.Invoke(GameProjectionFaultPoint.BeforeCaptureCommit);
            CommitWorkingSet(deltaCount);
            _lifecycleCursor = stagedLifecycleCursor;
            _changeCursor = stagedChangeCursor;
            ulong previous = _frameSequence;
            _frameSequence = checked(_frameSequence + 1);
            GameProjectionFrame frame = CreateFrame(previous, _frameSequence, deltaCount, world.TickCount);
            _lastFrame = frame;
            return new(GameProjectionCaptureStatus.Success, frame, lifecycle.Count, changedRows, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _fault = exception;
            return Failure(GameProjectionCaptureStatus.Faulted,
                $"The derived projection faulted: {exception.Message}");
        }
    }

    private GameProjectionCaptureResult RebuildInsideCallback(
        EcsWorld world,
        ulong generationId,
        GameGenerationState state)
    {
        try
        {
            if (state.Replication is null || state.Replication.WorldId != world.Id)
                return Failure(GameProjectionCaptureStatus.ReplicationNotConfigured,
                    "The active game generation was not created with the network profile.");
            if (world.Options.EntityCapacity.MaximumEntities > _limits.MaximumEntities)
                return Failure(GameProjectionCaptureStatus.CapacityExceeded,
                    "The projection entity capacity is smaller than the active ECS capacity.");
            if (world.GetLifecycleHistorySnapshot().Capacity == 0)
                return Failure(GameProjectionCaptureStatus.ReplicationNotConfigured,
                    "The network profile requires ECS lifecycle history.");
            bool generationChanged = !_stateEpoch.IsValid || generationId != _generationId;
            if (generationChanged && _nextStateEpoch == ulong.MaxValue)
                return Failure(GameProjectionCaptureStatus.CapacityExceeded,
                    "The projection StateEpoch identity space is exhausted.");
            if (!generationChanged && _baselineRevision == ulong.MaxValue)
                return Failure(GameProjectionCaptureStatus.CapacityExceeded,
                    "The projection baseline revision space is exhausted.");

            Array.Clear(_baselineHasSimulation);
            var baseline = new BaselineVisitor(_baselineSimulation, _baselineHasSimulation);
            _ = state.Replication.ExecuteBaseline(world, ref baseline);
            int count = 0;
            for (uint index = 0; index < world.Options.EntityCapacity.MaximumEntities; index++)
            {
                if (!world.TryGetEntity(index, out Entity entity)) continue;
                if (count == _baselineEntities.Length)
                    return Failure(GameProjectionCaptureStatus.CapacityExceeded,
                        "The projection cannot contain the active entity population.");
                _baselineEntities[count++] = entity;
            }
            LifecycleCursor lifecycleCursor = world.CaptureLifecycleCursorFromNow();
            ChangeCursor changeCursor = world.CreateChangeCursorFromNow();

            Array.Clear(_workingRuntimeGenerations);
            Array.Clear(_workingEntities);
            if (generationChanged) Array.Clear(_workingReplicationGenerations);
            else _replicationGenerations.CopyTo(_workingReplicationGenerations, 0);
            for (int index = 0; index < count; index++)
            {
                Entity entity = _baselineEntities[index];
                uint slot = entity.Index;
                uint replicationGeneration;
                if (generationChanged)
                {
                    replicationGeneration = 1;
                }
                else if (_runtimeGenerations[slot] == entity.Generation && _entities[slot].Entity.IsValid)
                {
                    replicationGeneration = _replicationGenerations[slot];
                }
                else
                {
                    uint previous = _replicationGenerations[slot];
                    if (previous == uint.MaxValue)
                        return Failure(GameProjectionCaptureStatus.CapacityExceeded,
                            "A canonical replication identity generation was exhausted.");
                    replicationGeneration = previous + 1;
                }
                _workingRuntimeGenerations[slot] = entity.Generation;
                _workingReplicationGenerations[slot] = replicationGeneration;
                _workingEntities[slot] = new GameProjectedEntity(
                    new ReplicationEntityId(slot, replicationGeneration),
                    _baselineHasSimulation[slot],
                    _baselineSimulation[slot]);
            }
            FaultInjector?.Invoke(GameProjectionFaultPoint.BeforeBaselineCommit);

            _workingRuntimeGenerations.CopyTo(_runtimeGenerations, 0);
            _workingReplicationGenerations.CopyTo(_replicationGenerations, 0);
            _workingEntities.CopyTo(_entities, 0);
            _entityCount = count;
            _baselineEntities.AsSpan(0, count).Clear();
            _generationId = generationId;
            if (generationChanged)
            {
                _stateEpoch = new StateEpoch(_nextStateEpoch++);
                _baselineRevision = 1;
                _nextDeltaSequence = 0;
                ClearApplicationEvents();
            }
            else
            {
                _baselineRevision++;
            }
            _lifecycleCursor = lifecycleCursor;
            _changeCursor = changeCursor;
            _frameSequence = 0;
            _lastDeltaCount = 0;
            ClearLifecycleHistory();
            _rebuildRequired = false;
            _fault = null;
            GameProjectionFrame frame = CreateFrame(0, 0, 0, world.TickCount);
            _lastFrame = frame;
            return new(GameProjectionCaptureStatus.BaselineRebuilt, frame, 0, 0, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _fault = exception;
            return Failure(GameProjectionCaptureStatus.Faulted,
                $"The derived projection rebuild faulted: {exception.Message}");
        }
    }

    private bool TryStageLifecycle(
        EcsWorld world,
        GameGenerationState gameState,
        in LifecycleRecord record,
        ref int deltaCount,
        out string? error)
    {
        if (deltaCount == _stagedDeltas.Length)
        {
            error = "The capture delta capacity was exceeded.";
            return false;
        }
        Entity entity = record.Entity;
        if (entity.Index >= _entities.Length)
        {
            error = "A lifecycle entity index exceeded projection capacity.";
            return false;
        }
        ref GameProjectedEntity projected = ref GetWorkingEntity(entity.Index);
        ref uint runtimeGeneration = ref _workingRuntimeGenerations[entity.Index];
        ref uint replicationGeneration = ref _workingReplicationGenerations[entity.Index];
        GameProjectionDelta delta;
        switch (record.Kind)
        {
            case LifecycleKind.Created:
                if (projected.Entity.IsValid || runtimeGeneration != 0 || replicationGeneration == uint.MaxValue)
                {
                    error = "Created lifecycle identity conflicts with canonical projection state.";
                    return false;
                }
                replicationGeneration++;
                runtimeGeneration = entity.Generation;
                bool hasSimulation = gameState.TryReadSimulationState(world, entity, out GameSimulationState simulation);
                projected = new GameProjectedEntity(
                    new ReplicationEntityId(entity.Index, replicationGeneration),
                    hasSimulation,
                    simulation);
                delta = new(0, GameProjectionDeltaKind.Created, projected.Entity,
                    ComponentId.Invalid, projected.HasSimulationState, projected.SimulationState);
                break;
            case LifecycleKind.Destroyed:
                if (!Matches(projected, runtimeGeneration, entity))
                {
                    error = "Destroyed lifecycle identity was absent from canonical projection state.";
                    return false;
                }
                delta = new(0, GameProjectionDeltaKind.Destroyed, projected.Entity,
                    ComponentId.Invalid, projected.HasSimulationState, projected.SimulationState);
                projected = default;
                runtimeGeneration = 0;
                break;
            case LifecycleKind.ComponentAdded:
            case LifecycleKind.ComponentRemoved:
                if (!Matches(projected, runtimeGeneration, entity))
                {
                    error = "Membership lifecycle identity was absent from canonical projection state.";
                    return false;
                }
                if (record.ComponentId == GameSchema.GetComponentId<GameSimulationState>())
                {
                    bool added = record.Kind == LifecycleKind.ComponentAdded;
                    GameSimulationState simulationValue = default;
                    bool present = added && gameState.TryReadSimulationState(world, entity, out simulationValue);
                    projected = projected with
                    {
                        HasSimulationState = present,
                        SimulationState = simulationValue,
                    };
                }
                delta = new(
                    0,
                    record.Kind == LifecycleKind.ComponentAdded
                        ? GameProjectionDeltaKind.ComponentAdded
                        : GameProjectionDeltaKind.ComponentRemoved,
                    projected.Entity,
                    record.ComponentId,
                    projected.HasSimulationState,
                    projected.SimulationState);
                break;
            default:
                error = "Unknown lifecycle kind.";
                return false;
        }
        _stagedDeltas[deltaCount] = delta;
        _stagedIsLifecycle[deltaCount] = true;
        _stagedSourceSequences[deltaCount] = record.Sequence;
        _stagedLifecycleKinds[deltaCount] = record.Kind;
        deltaCount++;
        error = null;
        return true;
    }

    private bool TryStageValue(
        EcsWorld world,
        in ChangedValue change,
        ref int deltaCount,
        out string? error)
    {
        if (deltaCount == _stagedDeltas.Length || change.EntityIndex >= _entities.Length ||
            !world.TryGetEntity(change.EntityIndex, out Entity entity))
        {
            error = "A changed row could not fit or resolve in canonical projection state.";
            return false;
        }
        ref GameProjectedEntity projected = ref GetWorkingEntity(change.EntityIndex);
        ref uint runtimeGeneration = ref _workingRuntimeGenerations[change.EntityIndex];
        if (!Matches(projected, runtimeGeneration, entity))
        {
            error = "A changed row was absent from canonical projection state.";
            return false;
        }
        projected = projected with
        {
            HasSimulationState = true,
            SimulationState = change.Value,
        };
        _stagedDeltas[deltaCount] = new(
            0,
            GameProjectionDeltaKind.SimulationStateChanged,
            projected.Entity,
            GameSchema.GetComponentId<GameSimulationState>(),
            true,
            change.Value);
        _stagedIsLifecycle[deltaCount] = false;
        _stagedSourceSequences[deltaCount] = 0;
        _stagedLifecycleKinds[deltaCount] = default;
        deltaCount++;
        error = null;
        return true;
    }

    private void BeginWorkingSet()
    {
        _touchedCount = 0;
        if (_workingStamp == uint.MaxValue)
        {
            Array.Clear(_workingStamps);
            _workingStamp = 1;
        }
        else
        {
            _workingStamp++;
        }
    }

    private ref GameProjectedEntity GetWorkingEntity(uint index)
    {
        if (_workingStamps[index] != _workingStamp)
        {
            if (_touchedCount == _touchedIndices.Length)
                throw new InvalidOperationException("Touched projection capacity was exceeded.");
            _workingStamps[index] = _workingStamp;
            _workingEntities[index] = _entities[index];
            _workingRuntimeGenerations[index] = _runtimeGenerations[index];
            _workingReplicationGenerations[index] = _replicationGenerations[index];
            _touchedIndices[_touchedCount++] = index;
        }
        return ref _workingEntities[index];
    }

    private void CommitWorkingSet(int deltaCount)
    {
        for (int index = 0; index < _touchedCount; index++)
        {
            uint slot = _touchedIndices[index];
            bool wasActive = _entities[slot].Entity.IsValid;
            bool isActive = _workingEntities[slot].Entity.IsValid;
            _entities[slot] = _workingEntities[slot];
            _runtimeGenerations[slot] = _workingRuntimeGenerations[slot];
            _replicationGenerations[slot] = _workingReplicationGenerations[slot];
            if (!wasActive && isActive) _entityCount++;
            else if (wasActive && !isActive) _entityCount--;
        }
        for (int index = 0; index < deltaCount; index++)
        {
            GameProjectionDelta staged = _stagedDeltas[index];
            GameProjectionDelta committed = staged with { Sequence = _nextDeltaSequence++ };
            _lastDeltas[index] = committed;
            if (_stagedIsLifecycle[index])
            {
                AppendLifecycle(new GameProjectionLifecycleFact(
                    _lifecycleNextSequence,
                    _stagedSourceSequences[index],
                    _stagedLifecycleKinds[index],
                    committed.Entity,
                    committed.Component));
            }
        }
        if (_lastDeltaCount > deltaCount)
            _lastDeltas.AsSpan(deltaCount, _lastDeltaCount - deltaCount).Clear();
        _lastDeltaCount = deltaCount;
    }

    private bool TryPreflightCaptureCommit(int deltaCount, out string? error)
    {
        if (_frameSequence == ulong.MaxValue)
        {
            error = "The projection frame sequence space is exhausted.";
            return false;
        }
        if ((ulong)deltaCount > ulong.MaxValue - _nextDeltaSequence)
        {
            error = "The projection delta sequence space is exhausted.";
            return false;
        }

        int lifecycleFacts = 0;
        for (int index = 0; index < deltaCount; index++)
            if (_stagedIsLifecycle[index]) lifecycleFacts++;
        if ((ulong)lifecycleFacts > ulong.MaxValue - _lifecycleNextSequence)
        {
            error = "The projection lifecycle sequence space is exhausted.";
            return false;
        }
        long overwritten = Math.Max(0L, (long)_lifecycleCount + lifecycleFacts - _lifecycleHistory.Length);
        if ((ulong)overwritten > ulong.MaxValue - _lifecycleOverwritten)
        {
            error = "The projection lifecycle overwrite counter is exhausted.";
            return false;
        }
        error = null;
        return true;
    }

    private void AppendLifecycle(in GameProjectionLifecycleFact fact)
    {
        int index;
        if (_lifecycleCount == _lifecycleHistory.Length)
        {
            index = _lifecycleHead;
            _lifecycleHead = (_lifecycleHead + 1) % _lifecycleHistory.Length;
            _lifecycleOverwritten++;
        }
        else
        {
            index = (_lifecycleHead + _lifecycleCount) % _lifecycleHistory.Length;
            _lifecycleCount++;
        }
        _lifecycleHistory[index] = fact;
        _lifecycleNextSequence++;
    }

    private void RefreshParticipants()
    {
        int count = 0;
        foreach (ParticipantId participant in _participants.Keys)
            _participantScratch[count++] = participant;
        for (int index = 0; index < count; index++)
        {
            ParticipantId participant = _participantScratch[index];
            if (_admission.TryGetSession(participant, out AdmissionResult session)) UpdateParticipant(session);
            else
            {
                _participants.Remove(participant);
                _ = UnregisterApplicationEventConsumer(participant);
            }
        }
    }

    private void BindParticipantsToProjection()
    {
        int count = 0;
        foreach (ParticipantId participant in _participants.Keys)
            _participantScratch[count++] = participant;
        for (int index = 0; index < count; index++)
        {
            ParticipantId participant = _participantScratch[index];
            ParticipantState state = _participants[participant];
            ReplicationEntityId id = default;
            if (state.Session.IsEntered && state.Session.GenerationId == _generationId &&
                state.Session.Entity.Index < _entities.Length &&
                _runtimeGenerations[state.Session.Entity.Index] == state.Session.Entity.Generation)
            {
                id = _entities[state.Session.Entity.Index].Entity;
            }
            state.Binding = new GameProjectionParticipant(participant, id, state.Session.GenerationId);
            _participants[participant] = state;
        }
    }

    private void HandleAdmissionEntered(AdmissionResult result)
    {
        if (_disposed) return;
        try { UpdateParticipant(result); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _fault ??= exception; }
    }

    private void HandleAdmissionRejected(AdmissionResult result)
    {
        if (_disposed) return;
        _participants.Remove(result.Participant);
        _ = UnregisterApplicationEventConsumer(result.Participant);
    }

    private void HandleGenerationPublished(ulong generationId)
    {
        if (_disposed) return;
        try { RefreshParticipants(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { _fault ??= exception; }
    }

    private IngressCommit PublishIngressInsideCallback(
        EcsWorld world,
        ulong generationId,
        GameGenerationState state)
    {
        if (generationId != _ingressExpectedGeneration)
            return new IngressCommit(GameAuthoritativeIngressStatus.GenerationChanged, false);
        if (state.Replication is null)
            return new IngressCommit(GameAuthoritativeIngressStatus.ReplicationNotConfigured, false);
        return state.Replication.TryEnqueueCommands(_commandScratch.AsSpan(0, _ingressAcceptedCount))
            ? new IngressCommit(GameAuthoritativeIngressStatus.Success, true)
            : new IngressCommit(GameAuthoritativeIngressStatus.SchedulerCapacityExceeded, false);
    }

    private void UpdateParticipant(AdmissionResult result)
    {
        if (!result.Participant.IsValid) return;
        if (!result.IsEntered)
        {
            _participants.Remove(result.Participant);
            _ = UnregisterApplicationEventConsumer(result.Participant);
            return;
        }
        bool exists = _participants.TryGetValue(result.Participant, out ParticipantState state);
        if (!exists && _participants.Count == _limits.MaximumParticipants)
        {
            return;
        }
        if (!exists)
        {
            FindParticipantEventBounds(result.Participant,
                out state.ApplicationEventHeadSequence,
                out state.ApplicationEventTailSequence);
        }
        state.Session = result;
        _participants[result.Participant] = state;
    }

    private GameProjectionCaptureResult Failure(GameProjectionCaptureStatus status, string message) =>
        new(status, CreateFrame(_frameSequence, _frameSequence, _lastDeltaCount, 0), 0, 0, message);

    private GameProjectionCaptureResult CaptureResyncFailure(string message)
    {
        _rebuildRequired = true;
        return Failure(GameProjectionCaptureStatus.CaptureResyncRequired, message);
    }

    private GameProjectionFrame CreateFrame(ulong previous, ulong current, int deltas, ulong tick) => new(
        _stateEpoch,
        _generationId,
        tick,
        _baselineRevision,
        previous,
        current,
        _nextDeltaSequence,
        deltas,
        unchecked(_lifecycleNextSequence - (ulong)_lifecycleCount),
        _lifecycleNextSequence,
        unchecked(_applicationNextSequence - (ulong)_applicationCount),
        _applicationNextSequence);

    private static bool Matches(in GameProjectedEntity projected, uint runtimeGeneration, Entity entity) =>
        projected.Entity.IsValid && runtimeGeneration == entity.Generation;

    private void ClearLifecycleHistory()
    {
        Array.Clear(_lifecycleHistory);
        _lifecycleHead = 0;
        _lifecycleCount = 0;
        _lifecycleNextSequence = 0;
        _lifecycleOverwritten = 0;
    }

    private void ClearApplicationEvents()
    {
        Array.Clear(_applicationEvents);
        Array.Fill(_applicationEventNextParticipantSequence, ulong.MaxValue);
        _applicationHead = 0;
        _applicationCount = 0;
        _applicationNextSequence = 0;
        Array.Clear(_applicationEventConsumerCursors, 0, _applicationEventConsumerCount);
        Array.Fill(_applicationEventConsumerNextRelevantSequences, ulong.MaxValue);
        _applicationMinimumCursor = 0;
        _applicationConsumersAtMinimum = _applicationEventConsumerCount;
        int participantCount = 0;
        foreach (ParticipantId participant in _participants.Keys)
            _participantScratch[participantCount++] = participant;
        for (int index = 0; index < participantCount; index++)
        {
            ParticipantId participant = _participantScratch[index];
            ParticipantState state = _participants[participant];
            state.ApplicationEventHeadSequence = ulong.MaxValue;
            state.ApplicationEventTailSequence = ulong.MaxValue;
            _participants[participant] = state;
        }
    }

    private long CalculateReservedBytes() => checked(
        (long)_runtimeGenerations.Length * sizeof(uint) +
        (long)_replicationGenerations.Length * sizeof(uint) +
        (long)_entities.Length * Unsafe.SizeOf<GameProjectedEntity>() +
        (long)_workingEntities.Length * Unsafe.SizeOf<GameProjectedEntity>() +
        (long)_workingRuntimeGenerations.Length * sizeof(uint) +
        (long)_workingReplicationGenerations.Length * sizeof(uint) +
        (long)_workingStamps.Length * sizeof(uint) +
        (long)_touchedIndices.Length * sizeof(uint) +
        (long)_baselineEntities.Length * Unsafe.SizeOf<Entity>() +
        (long)_baselineSimulation.Length * Unsafe.SizeOf<GameSimulationState>() +
        _baselineHasSimulation.Length +
        (long)_lifecycleScratch.Length * Unsafe.SizeOf<LifecycleRecord>() +
        (long)_changedScratch.Length * Unsafe.SizeOf<ChangedValue>() +
        (long)_stagedDeltas.Length * Unsafe.SizeOf<GameProjectionDelta>() +
        _stagedIsLifecycle.Length +
        (long)_stagedSourceSequences.Length * sizeof(ulong) +
        (long)_stagedLifecycleKinds.Length * Unsafe.SizeOf<LifecycleKind>() +
        (long)_lastDeltas.Length * Unsafe.SizeOf<GameProjectionDelta>() +
        (long)_lifecycleHistory.Length * Unsafe.SizeOf<GameProjectionLifecycleFact>() +
        (long)_applicationEvents.Length * Unsafe.SizeOf<GameApplicationEvent>() +
        (long)_applicationEventNextParticipantSequence.Length * sizeof(ulong) +
        (long)_applicationEventConsumers.Length * Unsafe.SizeOf<ParticipantId>() +
        (long)_applicationEventConsumerCursors.Length * sizeof(ulong) +
        (long)_applicationEventConsumerNextRelevantSequences.Length * sizeof(ulong) +
        (long)_inputs.Length * Unsafe.SizeOf<GameAuthoritativeInput>() +
        (long)_commandScratch.Length * Unsafe.SizeOf<GameSimulationCommand>() +
        (long)_participantScratch.Length * Unsafe.SizeOf<ParticipantId>() +
        (long)_participantMapCapacity * (sizeof(int) + Unsafe.SizeOf<ParticipantMapEntryEstimate>()) +
        (long)_inputSequenceScratchCapacity *
        (sizeof(int) + Unsafe.SizeOf<InputSequenceMapEntryEstimate>()) +
        (long)_applicationEventConsumerMapCapacity *
        (sizeof(int) + Unsafe.SizeOf<ApplicationEventConsumerMapEntryEstimate>()));

    private static void CopyRing<T>(
        T[] source,
        int head,
        ulong oldest,
        ulong requested,
        Span<T> destination)
    {
        int offset = checked((int)unchecked(requested - oldest));
        int start = (head + offset) % source.Length;
        int first = Math.Min(destination.Length, source.Length - start);
        source.AsSpan(start, first).CopyTo(destination);
        if (destination.Length != first)
            source.AsSpan(0, destination.Length - first).CopyTo(destination[first..]);
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(GameProjectionService));
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Game projection is bound to its creating owner thread.");
    }

    private readonly record struct ChangedValue(uint EntityIndex, GameSimulationState Value);
    private readonly record struct IngressCommit(GameAuthoritativeIngressStatus Status, bool Committed);

    private struct ParticipantState
    {
        internal AdmissionResult Session;
        internal GameProjectionParticipant Binding;
        internal ulong LastAcceptedInputSequence;
        internal ulong ApplicationEventHeadSequence;
        internal ulong ApplicationEventTailSequence;
    }

    // Mirrors the current Dictionary<TKey,TValue> entry payload for reserved-byte
    // diagnostics. CLR object/array headers remain intentionally outside the
    // payload reservation contract.
    private readonly record struct ParticipantMapEntryEstimate(
        uint HashCode,
        int Next,
        ParticipantId Key,
        ParticipantState Value);

    private readonly record struct InputSequenceMapEntryEstimate(
        uint HashCode,
        int Next,
        ParticipantId Key,
        ulong Value);

    private readonly record struct ApplicationEventConsumerMapEntryEstimate(
        uint HashCode,
        int Next,
        ParticipantId Key,
        int Value);

    private struct BaselineVisitor : IReadChunkJob<GameSimulationState>
    {
        private readonly GameSimulationState[] _values;
        private readonly bool[] _present;

        internal BaselineVisitor(GameSimulationState[] values, bool[] present) =>
            (_values, _present) = (values, present);

        public void Execute(in ReadComponentChunk<GameSimulationState> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                uint index = chunk.EntityIndices[row];
                _values[index] = chunk.ReadComponents[row];
                _present[index] = true;
            }
        }
    }

    private struct ChangedVisitor : IReadOnlyChunkJob<GameSimulationState>
    {
        private readonly ChangedValue[] _values;

        internal ChangedVisitor(ChangedValue[] values)
        {
            _values = values;
            Count = 0;
            Overflowed = false;
        }

        internal int Count;
        internal bool Overflowed;
        internal readonly ReadOnlySpan<ChangedValue> Values => _values.AsSpan(0, Count);

        public void Execute(in ReadOnlyComponentChunk<GameSimulationState> chunk)
        {
            if (chunk.Count > _values.Length - Count)
            {
                Overflowed = true;
                return;
            }
            for (int row = 0; row < chunk.Count; row++)
                _values[Count++] = new ChangedValue(chunk.EntityIndices[row], chunk.Components[row]);
        }
    }
}

internal enum GameProjectionFaultPoint : byte
{
    BeforeBaselineCommit = 1,
    BeforeCaptureCommit = 2,
}
