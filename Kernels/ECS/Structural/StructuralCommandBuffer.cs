using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity, single-writer arena for one deterministic structural barrier.
/// </summary>
/// <remarks>
/// Recording never mutates the world. This canonical arena is owner-thread-bound
/// and must not be retained as a way around scheduler capabilities; scheduled
/// jobs use <see cref="SystemJobContext.Commands"/> instead. Playback is explicit
/// and consumes commands in destroy, create, remove, then add phases.
/// Expected stale-handle and capacity failures are counted rather than thrown.
/// Unexpected playback exceptions consume the arena and fault the owning world.
/// Structural changes are invisible until playback, even to systems ordered later
/// in the same scheduler graph.
/// </remarks>
public sealed class StructuralCommandBuffer
{
    private const int MinimumGroupedDestroyRun = 32;
    private const int MinimumGroupedTransitionRun = 8;
    // The production benchmark shows narrow (12-byte) churn losing ~2-3% while
    // 88+ byte rows win. Keep the crossover conservative and benchmark-owned.
    private const int MinimumTemporaryHoleFixedRowBytes = 64;
    private const int TemporaryHoleUntracked = 0;
    private const int TemporaryHoleCountingBias = -1;
    private const int TemporaryHoleEligible = int.MinValue;

    private readonly StructuralCommandRecord[] _commands;
    private readonly Entity[] _createdEntities;
    private readonly int[] _nextCommand;
    private readonly int[] _temporaryHoleHeads;
    private readonly int[] _createDemand;
    private readonly int[] _temporaryHoleState;
    private readonly int[] _activeTemporaryArchetypes;
    private readonly byte[] _payload;
    private readonly WorldId _worldId;
    private readonly StructuralRecordingGate? _recordingGate;
    private uint _bufferId;
    private readonly int _ownerThreadId;
    private readonly bool _schedulerLane;
    private int _count;
    private int _payloadCount;
    private int _lastPlaybackCommandCount;
    private uint _epoch;
    private bool _hasCompletedPlayback;
    private bool _isPlaying;
    private bool _isActive;
    private PhaseChain _liveDestroys;
    private PhaseChain _creates;
    private PhaseChain _deferredDestroys;
    private PhaseChain _removes;
    private PhaseChain _adds;
    private int _activeTemporaryArchetypeCount;
    private bool _hasTemporaryHoleEligibleArchetype;
    private StructuralPlaybackDiagnostics _lastPlaybackDiagnostics;

    internal StructuralCommandBuffer(
        WorldId worldId,
        int ownerThreadId,
        int capacity,
        int maximumArchetypes,
        bool schedulerLane = false,
        int payloadCapacityBytes = 0,
        StructuralRecordingGate? recordingGate = null)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (ownerThreadId <= 0 && !schedulerLane)
            throw new ArgumentOutOfRangeException(nameof(ownerThreadId));
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumArchetypes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumArchetypes));
        if (payloadCapacityBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadCapacityBytes));

        _worldId = worldId;
        _bufferId = BufferIdSource.Allocate();
        _ownerThreadId = ownerThreadId;
        _schedulerLane = schedulerLane;
        _recordingGate = recordingGate;
        _commands = new StructuralCommandRecord[capacity];
        _createdEntities = new Entity[capacity];
        _nextCommand = new int[capacity];
        _temporaryHoleHeads = new int[checked(maximumArchetypes + 1)];
        _createDemand = new int[checked(maximumArchetypes + 1)];
        _temporaryHoleState = new int[checked(maximumArchetypes + 1)];
        _activeTemporaryArchetypes = new int[maximumArchetypes];
        _payload = new byte[payloadCapacityBytes];
        _epoch = 1;
        _isActive = true;
    }

    public bool IsValid => _isActive;

    public int Capacity => _commands.Length;

    public int Count
    {
        get
        {
            EnsureOwnerAndActive();
            return _count;
        }
    }

    /// <summary>
    /// Payload bytes reserved by command, create-result, and stable phase-link
    /// arrays. Managed array headers and the buffer object are deliberately excluded.
    /// </summary>
    public long ReservedBytes => checked(
        (long)_commands.Length *
        (Unsafe.SizeOf<StructuralCommandRecord>() +
         Unsafe.SizeOf<Entity>() +
         sizeof(int)) +
        (long)(_temporaryHoleHeads.Length + _createDemand.Length + _temporaryHoleState.Length +
            _activeTemporaryArchetypes.Length) * sizeof(int) +
        _payload.Length);

    internal int PayloadCapacityBytes => _payload.Length;
    internal int SchedulerPayloadCount => _payloadCount;

    /// <summary>Records one default create or throws when command capacity is exhausted.</summary>
    /// <remarks>The returned token belongs to this recording epoch and is not a persistent ID.</remarks>
    public DeferredEntity EnqueueCreateDefault(ArchetypeHandle archetype)
    {
        if (!TryEnqueueCreateDefault(archetype, out DeferredEntity entity))
            throw new StructuralCommandCapacityExceededException(Capacity);
        return entity;
    }

    /// <summary>Records one compiled prefab instantiation as one structural command.</summary>
    /// <remarks>
    /// Playback allocates directly in the prefab's final archetype and copies its
    /// immutable defaults before publishing one ordinary Created lifecycle fact.
    /// </remarks>
    public DeferredEntity EnqueueInstantiate(PrefabHandle prefab)
    {
        if (!TryEnqueueInstantiate(prefab, out DeferredEntity entity))
            throw new StructuralCommandCapacityExceededException(Capacity);
        return entity;
    }

    public bool TryEnqueueInstantiate(PrefabHandle prefab, out DeferredEntity entity)
    {
        EnsureOwnerAndActive();
        ValidatePrefab(prefab, nameof(prefab));
        BeginRecordingIfNeeded();
        if (_count == _commands.Length)
        {
            entity = default;
            return false;
        }

        int commandIndex = _count;
        AppendTrusted(StructuralCommandRecord.InstantiatePrefab(
            prefab.PrefabId,
            prefab.ArchetypeId));
        entity = new DeferredEntity(
            _worldId,
            _bufferId,
            _epoch,
            unchecked((uint)commandIndex));
        return true;
    }

    internal StructuralPlaybackDiagnostics LastPlaybackDiagnostics
    {
        get
        {
            EnsureOwnerAndActive();
            return _lastPlaybackDiagnostics;
        }
    }

    internal int SchedulerCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            System.Diagnostics.Debug.Assert(_schedulerLane);
            return _count;
        }
    }

    internal bool OwnsSchedulerDeferredInSegment(DeferredEntity entity, int segmentStart)
    {
        System.Diagnostics.Debug.Assert(_schedulerLane);
        return entity.IsValid && entity.World == _worldId &&
            entity.BufferId == _bufferId && entity.Epoch == _epoch &&
            entity.CommandIndex >= unchecked((uint)segmentStart) &&
            entity.CommandIndex < unchecked((uint)_count);
    }

    internal void ClearSchedulerLane()
    {
        System.Diagnostics.Debug.Assert(_schedulerLane);
        if (_isPlaying)
            throw new InvalidOperationException("A scheduler structural lane cannot clear during playback.");
        ResetForNewEpoch();
    }

    internal bool CanAppendSchedulerCommands(int count)
    {
        if (_schedulerLane)
            throw new InvalidOperationException("A scheduler lane cannot be the canonical merge destination.");
        EnsureOwnerAndActive();
        return count >= 0 && count <= _commands.Length - _count;
    }

    /// <summary>Opens the canonical arena epoch before scheduler payload preflight and copy.</summary>
    internal void BeginSchedulerMerge()
    {
        if (_schedulerLane)
            throw new InvalidOperationException("A scheduler lane cannot be the canonical merge destination.");
        EnsureOwnerAndActive();
        BeginRecordingIfNeeded();
    }

    internal int GetSchedulerMergedPayloadEnd(
        EcsWorld<NativeChunkArena> world,
        int start,
        int count,
        int destinationOffset)
    {
        System.Diagnostics.Debug.Assert(_schedulerLane);
        if (start < 0 || count < 0 || start > _count - count)
            throw new ArgumentOutOfRangeException(nameof(start));
        int offset = destinationOffset;
        for (int index = start; index < start + count; index++)
        {
            StructuralCommandRecord command = _commands[index];
            if (command.Kind != StructuralCommandKind.AddComponent)
                continue;
            world.GetStructuralComponentPayloadLayout(
                command.Argument,
                out int size,
                out int alignment);
            offset = AlignUp(offset, alignment);
            offset = checked(offset + size);
        }
        return offset;
    }

    internal bool CanAppendSchedulerPayloadEnd(int finalOffset)
    {
        if (_schedulerLane)
            throw new InvalidOperationException("A scheduler lane cannot be the canonical merge destination.");
        EnsureOwnerAndActive();
        return finalOffset >= _payloadCount && finalOffset <= _payload.Length;
    }

    /// <summary>
    /// Appends one producer segment into the canonical owner arena. Existing
    /// phase chains are rebuilt by AppendTrusted; producer-local deferred targets
    /// are remapped through the source lane's already-reserved link array.
    /// </summary>
    internal int CopySchedulerSegmentTo(
        StructuralCommandBuffer destination,
        EcsWorld<NativeChunkArena> world,
        int start,
        int count)
    {
        System.Diagnostics.Debug.Assert(_schedulerLane);
        ArgumentNullException.ThrowIfNull(destination);
        destination.EnsureOwnerAndActive();
        if (destination._schedulerLane)
            throw new InvalidOperationException("Scheduler segments require the canonical owner arena.");
        if (start < 0 || count < 0 || start > _count - count)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (!destination.CanAppendSchedulerCommands(count))
            throw new StructuralCommandCapacityExceededException(destination.Capacity);

        int destinationStart = destination._count;
        for (int offset = 0; offset < count; offset++)
        {
            int sourceIndex = start + offset;
            StructuralCommandRecord command = _commands[sourceIndex];
            if (command.Kind == StructuralCommandKind.AddComponent)
            {
                world.GetStructuralComponentPayloadLayout(
                    command.Argument,
                    out int payloadSize,
                    out int payloadAlignment);
                int destinationPayloadOffset = AlignUp(destination._payloadCount, payloadAlignment);
                if (destinationPayloadOffset > destination._payload.Length - payloadSize)
                    throw new InvalidOperationException("Preflighted scheduler structural payload capacity changed during merge.");
                _payload.AsSpan(unchecked((int)command.PayloadOffset), payloadSize).CopyTo(
                    destination._payload.AsSpan(destinationPayloadOffset, payloadSize));
                destination._payloadCount = destinationPayloadOffset + payloadSize;
                command = command.WithPayloadOffset(unchecked((uint)destinationPayloadOffset));
            }
            if (command.TargetKind == StructuralCommandTargetKind.Deferred)
            {
                uint sourceTarget = command.DeferredCommandIndex;
                if (sourceTarget >= unchecked((uint)sourceIndex))
                    ThrowCommandInvariant("A scheduler deferred target must refer to an earlier producer command.");
                int mappedLink = _nextCommand[sourceTarget];
                if (mappedLink == 0)
                    ThrowCommandInvariant("A scheduler deferred target crossed producer or merge order.");
                command = command.RemapDeferredTarget(
                    destination._bufferId,
                    destination._epoch,
                    unchecked((uint)(mappedLink - 1)));
            }

            int destinationIndex = destination._count;
            destination.BeginRecordingIfNeeded();
            destination.AppendTrusted(command);
            _nextCommand[sourceIndex] = destinationIndex + 1;
        }
        return destination._count - destinationStart;
    }

    internal bool TryEnqueueAddComponent(
        Entity entity,
        uint componentRouteId,
        ReadOnlySpan<byte> payload,
        int alignment)
    {
        EnsureOwnerAndActive();
        ValidateEntity(entity, nameof(entity));
        BeginRecordingIfNeeded();
        if (_count == _commands.Length)
            return false;
        int offset = ReservePayload(payload, alignment);
        if (offset < 0)
            return false;
        AppendTrusted(StructuralCommandRecord.AddComponent(
            entity,
            componentRouteId,
            unchecked((uint)offset)));
        return true;
    }

    internal bool TryEnqueueAddComponent(
        DeferredEntity entity,
        uint componentRouteId,
        ReadOnlySpan<byte> payload,
        int alignment)
    {
        EnsureOwnerAndActive();
        ValidateDeferredEntity(entity, nameof(entity));
        BeginRecordingIfNeeded();
        if (_count == _commands.Length)
            return false;
        int offset = ReservePayload(payload, alignment);
        if (offset < 0)
            return false;
        AppendTrusted(StructuralCommandRecord.AddComponent(
            entity,
            componentRouteId,
            unchecked((uint)offset)));
        return true;
    }

    internal bool TryEnqueueRemoveComponent(Entity entity, uint componentRouteId)
    {
        EnsureOwnerAndActive();
        ValidateEntity(entity, nameof(entity));
        BeginRecordingIfNeeded();
        return TryAppend(StructuralCommandRecord.RemoveComponent(entity, componentRouteId));
    }

    internal bool TryEnqueueRemoveComponent(DeferredEntity entity, uint componentRouteId)
    {
        EnsureOwnerAndActive();
        ValidateDeferredEntity(entity, nameof(entity));
        BeginRecordingIfNeeded();
        return TryAppend(StructuralCommandRecord.RemoveComponent(entity, componentRouteId));
    }

    /// <summary>Attempts to record one default create without mutating world topology.</summary>
    public bool TryEnqueueCreateDefault(
        ArchetypeHandle archetype,
        out DeferredEntity entity)
    {
        EnsureOwnerAndActive();
        ValidateArchetype(archetype, nameof(archetype));
        BeginRecordingIfNeeded();
        if (_count == _commands.Length)
        {
            entity = default;
            return false;
        }

        int commandIndex = _count;
        AppendTrusted(StructuralCommandRecord.CreateDefault(archetype.Id));
        entity = new DeferredEntity(
            _worldId,
            _bufferId,
            _epoch,
            unchecked((uint)commandIndex));
        return true;
    }

    public void EnqueueCreateDefault(
        ReadOnlySpan<ArchetypeHandle> archetypes,
        Span<DeferredEntity> entities)
    {
        if (!TryEnqueueCreateDefault(archetypes, entities))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    /// <summary>
    /// Atomically appends a batch or returns false without recording any member.
    /// </summary>
    public bool TryEnqueueCreateDefault(
        ReadOnlySpan<ArchetypeHandle> archetypes,
        Span<DeferredEntity> entities)
    {
        EnsureOwnerAndActive();
        if (entities.Length < archetypes.Length)
            throw new ArgumentException("The entity output span is shorter than the archetype input.", nameof(entities));
        if (archetypes.IsEmpty)
            return true;
        for (int index = 0; index < archetypes.Length; index++)
            ValidateArchetype(archetypes[index], nameof(archetypes));

        BeginRecordingIfNeeded();
        if (archetypes.Length > _commands.Length - _count)
        {
            entities[..archetypes.Length].Clear();
            return false;
        }

        for (int index = 0; index < archetypes.Length; index++)
        {
            int commandIndex = _count;
            AppendTrusted(StructuralCommandRecord.CreateDefault(archetypes[index].Id));
            entities[index] = new DeferredEntity(
                _worldId,
                _bufferId,
                _epoch,
                unchecked((uint)commandIndex));
        }
        return true;
    }

    public void EnqueueDestroy(Entity entity)
    {
        if (!TryEnqueueDestroy(entity))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    /// <summary>Attempts to record destruction for the next structural playback.</summary>
    public bool TryEnqueueDestroy(Entity entity)
    {
        EnsureOwnerAndActive();
        ValidateEntity(entity, nameof(entity));
        BeginRecordingIfNeeded();
        return TryAppend(StructuralCommandRecord.Destroy(entity));
    }

    public void EnqueueDestroy(ReadOnlySpan<Entity> entities)
    {
        if (!TryEnqueueDestroy(entities))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    /// <summary>Atomically appends a live-entity destroy batch.</summary>
    public bool TryEnqueueDestroy(ReadOnlySpan<Entity> entities)
    {
        EnsureOwnerAndActive();
        if (entities.IsEmpty)
            return true;
        for (int index = 0; index < entities.Length; index++)
            ValidateEntity(entities[index], nameof(entities));

        BeginRecordingIfNeeded();
        if (entities.Length > _commands.Length - _count)
            return false;
        for (int index = 0; index < entities.Length; index++)
            AppendTrusted(StructuralCommandRecord.Destroy(entities[index]));
        return true;
    }

    public void EnqueueDestroy(DeferredEntity entity)
    {
        if (!TryEnqueueDestroy(entity))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    public bool TryEnqueueDestroy(DeferredEntity entity)
    {
        EnsureOwnerAndActive();
        ValidateDeferredEntity(entity, nameof(entity));
        BeginRecordingIfNeeded();
        return TryAppend(StructuralCommandRecord.Destroy(entity));
    }

    public void EnqueueTransition(Entity entity, in ArchetypeTransition transition)
    {
        if (!TryEnqueueTransition(entity, transition))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    /// <summary>Attempts to record one prepared membership transition for playback.</summary>
    public bool TryEnqueueTransition(Entity entity, in ArchetypeTransition transition)
    {
        EnsureOwnerAndActive();
        ValidateEntity(entity, nameof(entity));
        ValidateTransition(transition, nameof(transition));
        BeginRecordingIfNeeded();
        return TryAppend(StructuralCommandRecord.Transition(
            entity,
            transition.TransitionId,
            transition.Phase));
    }

    public void EnqueueTransition(
        ReadOnlySpan<Entity> entities,
        in ArchetypeTransition transition)
    {
        if (!TryEnqueueTransition(entities, transition))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    /// <summary>Atomically appends one prepared transition for a live-entity batch.</summary>
    public bool TryEnqueueTransition(
        ReadOnlySpan<Entity> entities,
        in ArchetypeTransition transition)
    {
        EnsureOwnerAndActive();
        ValidateTransition(transition, nameof(transition));
        if (entities.IsEmpty)
            return true;
        for (int index = 0; index < entities.Length; index++)
            ValidateEntity(entities[index], nameof(entities));

        BeginRecordingIfNeeded();
        if (entities.Length > _commands.Length - _count)
            return false;
        for (int index = 0; index < entities.Length; index++)
        {
            AppendTrusted(StructuralCommandRecord.Transition(
                entities[index],
                transition.TransitionId,
                transition.Phase));
        }
        return true;
    }

    public void EnqueueTransition(DeferredEntity entity, in ArchetypeTransition transition)
    {
        if (!TryEnqueueTransition(entity, transition))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    public bool TryEnqueueTransition(
        DeferredEntity entity,
        in ArchetypeTransition transition)
    {
        EnsureOwnerAndActive();
        ValidateDeferredEntity(entity, nameof(entity));
        ValidateTransition(transition, nameof(transition));
        BeginRecordingIfNeeded();
        return TryAppend(StructuralCommandRecord.Transition(
            entity,
            transition.TransitionId,
            transition.Phase));
    }

    public void EnqueueTransition(
        ReadOnlySpan<DeferredEntity> entities,
        in ArchetypeTransition transition)
    {
        if (!TryEnqueueTransition(entities, transition))
            throw new StructuralCommandCapacityExceededException(Capacity);
    }

    /// <summary>Atomically appends one prepared transition for a deferred-create batch.</summary>
    public bool TryEnqueueTransition(
        ReadOnlySpan<DeferredEntity> entities,
        in ArchetypeTransition transition)
    {
        EnsureOwnerAndActive();
        ValidateTransition(transition, nameof(transition));
        if (entities.IsEmpty)
            return true;
        for (int index = 0; index < entities.Length; index++)
            ValidateDeferredEntity(entities[index], nameof(entities));

        BeginRecordingIfNeeded();
        if (entities.Length > _commands.Length - _count)
            return false;
        for (int index = 0; index < entities.Length; index++)
        {
            AppendTrusted(StructuralCommandRecord.Transition(
                entities[index],
                transition.TransitionId,
                transition.Phase));
        }
        return true;
    }

    /// <summary>
    /// Returns the entity produced for a create token by the most recent completed
    /// playback. This reports creation outcome, not current liveness.
    /// </summary>
    public bool TryResolve(DeferredEntity token, out Entity entity)
    {
        EnsureOwnerAndActive();
        if (!_hasCompletedPlayback ||
            token.World != _worldId ||
            token.BufferId != _bufferId ||
            token.Epoch != _epoch ||
            token.CommandIndex >= unchecked((uint)_lastPlaybackCommandCount) ||
            !IsCreateCommand(_commands[token.CommandIndex].Kind))
        {
            entity = Entity.Null;
            return false;
        }

        entity = _createdEntities[token.CommandIndex];
        return entity.IsValid;
    }

    /// <summary>Discards pending commands and invalidates every outstanding token.</summary>
    public void Clear()
    {
        EnsureOwnerAndActive();
        if (_isPlaying)
            throw new InvalidOperationException("A structural command buffer cannot be cleared during playback.");
        ResetForNewEpoch();
    }

    internal StructuralPlaybackResult Playback(EcsWorld<NativeChunkArena> world) =>
        PlaybackCore(world, StructuralPlaybackExecutor.Production);

    /// <summary>
    /// Phase-indexed packed reference retained to isolate barrier-local row
    /// reuse from the otherwise identical production executor.
    /// </summary>
    internal StructuralPlaybackResult PlaybackPhaseIndexedPackedOracle(
        EcsWorld<NativeChunkArena> world) =>
        PlaybackCore(world, StructuralPlaybackExecutor.PackedOracle);

    /// <summary>
    /// Retained reference executor for differential tests and same-process
    /// benchmarks. Production playback must use the phase-indexed path.
    /// </summary>
    internal StructuralPlaybackResult PlaybackScalarOracle(EcsWorld<NativeChunkArena> world) =>
        PlaybackCore(world, StructuralPlaybackExecutor.ScalarOracle);

    private StructuralPlaybackResult PlaybackCore(
        EcsWorld<NativeChunkArena> world,
        StructuralPlaybackExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(world);
        EnsureOwnerAndActive();
        if (_isPlaying)
            throw new InvalidOperationException("Structural command playback cannot be nested or re-entered.");
        _lastPlaybackDiagnostics = default;
        if (_count == 0)
            return default;

        int commandCount = _count;
        var counts = new PlaybackCounts();
        var diagnostics = new PlaybackDiagnostics();
        bool completed = false;
        _isPlaying = true;
        try
        {
            if (executor != StructuralPlaybackExecutor.ScalarOracle)
                ExecutePhaseIndexed(
                    world,
                    commandCount,
                    ref counts,
                    ref diagnostics,
                    useTemporaryHoles: executor == StructuralPlaybackExecutor.Production);
            else
                ExecuteScalarOracle(world, commandCount, ref counts);

            var result = new StructuralPlaybackResult(
                _epoch,
                commandCount,
                counts.Created,
                counts.Destroyed,
                counts.Removed,
                counts.Added,
                counts.RejectedCreate,
                counts.RejectedDestroy,
                counts.RejectedTransition,
                counts.TemporaryHoles,
                counts.ReusedTemporaryHoles,
                counts.CompactedTemporaryHoles,
                counts.TemporaryHoleDonorRelocations,
                counts.ReusedTemporaryHoleFixedRowBytes);
            _lastPlaybackCommandCount = commandCount;
            _hasCompletedPlayback = true;
            _lastPlaybackDiagnostics = new StructuralPlaybackDiagnostics(
                diagnostics.ExactWholeSourceResets,
                diagnostics.ExactSuffixShrinks,
                diagnostics.ExactSuffixCommands,
                diagnostics.ExactSuffixBlocksStreamed,
                diagnostics.ExactSuffixRowsBulkShrunk,
                ExactSuffixDonorRelocations: 0,
                FailedExactSuffixCandidates: diagnostics.FailedExactSuffixCandidates);
            completed = true;
            return result;
        }
        finally
        {
            _count = 0;
            _payloadCount = 0;
            ResetPhaseChains();
            ResetTemporaryHoleState();
            _isPlaying = false;
            if (!completed)
            {
                _createdEntities.AsSpan(0, commandCount).Clear();
                _lastPlaybackCommandCount = 0;
                _hasCompletedPlayback = false;
                _lastPlaybackDiagnostics = default;
            }
        }
    }

    private void ExecutePhaseIndexed(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        ref PlaybackCounts counts,
        ref PlaybackDiagnostics diagnostics,
        bool useTemporaryHoles)
    {
        LiveDestroyPlan liveDestroyPlan = AnalyzeLiveDestroyPlan(world, _liveDestroys);
        bool liveDestroysCompleted = TryExecuteExactLiveDestroyPlan(
            world,
            _liveDestroys,
            liveDestroyPlan,
            ref counts);
        if (!liveDestroysCompleted)
        {
            if (useTemporaryHoles)
                PrepareTemporaryHoleDemand(world);
            ExecuteGeneralLiveDestroyChain(world, _liveDestroys, ref counts);
        }
        bool hasTemporaryHoles = useTemporaryHoles && counts.TemporaryHoles != 0;
        if (hasTemporaryHoles)
        {
            try
            {
                ExecuteIndexedCreateChain(
                    world,
                    _creates,
                    ref counts,
                    reuseTemporaryHoles: true);
            }
            finally
            {
                FinalizeTemporaryHoles(world, ref counts);
            }
        }
        else
        {
            ExecuteIndexedCreateChain(
                world,
                _creates,
                ref counts,
                reuseTemporaryHoles: false);
        }
        ExecuteIndexedDeferredDestroyChain(
            world,
            commandCount,
            _deferredDestroys,
            ref counts);
        ExecuteIndexedTransitionChain(
            world,
            commandCount,
            _removes,
            StructuralTransitionPhase.Remove,
            ref counts.Removed,
            ref counts.RejectedTransition,
            ref diagnostics);
        ExecuteIndexedTransitionChain(
            world,
            commandCount,
            _adds,
            StructuralTransitionPhase.Add,
            ref counts.Added,
            ref counts.RejectedTransition,
            ref diagnostics);
    }

    private void ExecuteScalarOracle(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        ref PlaybackCounts counts)
    {
        ExecuteScalarDestroyPass(
            world,
            commandCount,
            StructuralCommandTargetKind.Entity,
            ref counts.Destroyed,
            ref counts.RejectedDestroy);

        for (int index = 0; index < commandCount; index++)
        {
            StructuralCommandRecord command = _commands[index];
            if (IsCreateCommand(command.Kind))
                ExecuteCreate(command, index, world, ref counts);
        }

        // A deferred destroy cannot resolve before its create phase. It still
        // precedes every remove/add transition and therefore wins conflicts.
        ExecuteScalarDestroyPass(
            world,
            commandCount,
            StructuralCommandTargetKind.Deferred,
            ref counts.Destroyed,
            ref counts.RejectedDestroy);

        ExecuteScalarTransitionPass(
            world,
            commandCount,
            StructuralTransitionPhase.Remove,
            ref counts.Removed,
            ref counts.RejectedTransition);
        ExecuteScalarTransitionPass(
            world,
            commandCount,
            StructuralTransitionPhase.Add,
            ref counts.Added,
            ref counts.RejectedTransition);
    }

    internal void Invalidate()
    {
        if (!_isActive)
            return;

        int clearCount = Math.Max(_count, _lastPlaybackCommandCount);
        _createdEntities.AsSpan(0, clearCount).Clear();
        _count = 0;
        _payloadCount = 0;
        ResetPhaseChains();
        ResetTemporaryHoleState();
        _lastPlaybackCommandCount = 0;
        _hasCompletedPlayback = false;
        _isPlaying = false;
        _isActive = false;
    }

    private void ExecuteIndexedCreateChain(
        EcsWorld<NativeChunkArena> world,
        in PhaseChain chain,
        ref PlaybackCounts counts,
        bool reuseTemporaryHoles)
    {
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            ExecuteCreate(
                _commands[commandIndex],
                commandIndex,
                world,
                ref counts,
                reuseTemporaryHoles);
        }
    }

    private void ExecuteCreate(
        in StructuralCommandRecord command,
        int commandIndex,
        EcsWorld<NativeChunkArena> world,
        ref PlaybackCounts counts,
        bool reuseTemporaryHoles = false)
    {
        ArchetypeId archetypeId = command.CreateArchetypeId;
        int archetypeIndex = checked((int)archetypeId.Value);
        int holeLink = reuseTemporaryHoles
            ? _temporaryHoleHeads[archetypeIndex]
            : 0;
        bool reusedHole = false;
        StructuralCommandRecord hole = default;
        bool created;
        Entity entity;
        if (holeLink != 0)
        {
            hole = _commands[holeLink - 1];
            if (hole.Kind != StructuralCommandKind.TemporaryHole ||
                hole.TemporaryHoleArchetypeId != archetypeId)
            {
                ThrowCommandInvariant("A temporary-hole chain contains invalid metadata.");
            }
            created = command.Kind == StructuralCommandKind.InstantiatePrefab
                ? world.TryInstantiatePrefabInTemporaryHole(
                    command.Argument,
                    archetypeId,
                    hole.TemporaryHoleRow,
                    out entity)
                : world.TryCreateDefaultInTemporaryHole(
                    archetypeId,
                    hole.TemporaryHoleRow,
                    out entity);
            if (created)
            {
                _temporaryHoleHeads[archetypeIndex] = _nextCommand[holeLink - 1];
                reusedHole = true;
            }
        }
        else
        {
            created = command.Kind == StructuralCommandKind.InstantiatePrefab
                ? world.TryInstantiatePrefab(command.Argument, archetypeId, out entity)
                : world.TryCreateDefault(archetypeId, out entity);
        }

        if (created)
        {
            _createdEntities[commandIndex] = entity;
            counts.Created++;
            if (reusedHole)
            {
                counts.ReusedTemporaryHoles++;
                counts.ReusedTemporaryHoleFixedRowBytes = checked(
                    counts.ReusedTemporaryHoleFixedRowBytes +
                    hole.TemporaryHoleFixedRowBytes);
            }
        }
        else
        {
            _createdEntities[commandIndex] = Entity.Null;
            counts.RejectedCreate++;
        }
    }

    private void ExecuteIndexedDeferredDestroyChain(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        in PhaseChain chain,
        ref PlaybackCounts counts)
    {
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            StructuralCommandRecord command = _commands[commandIndex];
            ExecuteDestroy(
                command,
                commandCount,
                world,
                ref counts.Destroyed,
                ref counts.RejectedDestroy);
        }
    }

    private LiveDestroyPlan AnalyzeLiveDestroyPlan(
        EcsWorld<NativeChunkArena> world,
        in PhaseChain chain)
    {
        if (chain.HeadLink == 0)
            return default;
        if (chain.Count < MinimumGroupedDestroyRun)
            return new LiveDestroyPlan(LiveDestroyStrategy.General);

        Entity firstEntity = _commands[chain.HeadLink - 1].GetEntityTarget();
        Entity lastEntity = _commands[chain.TailIndex].GetEntityTarget();
        if (world.IsWholeArchetypeDestroyCandidate(firstEntity, chain.Count))
            return new LiveDestroyPlan(LiveDestroyStrategy.ExactWholeArchetype);
        if (world.IsArchetypeTailDestroyCandidate(firstEntity, lastEntity, chain.Count))
            return new LiveDestroyPlan(LiveDestroyStrategy.ExactArchetypeTail);
        return new LiveDestroyPlan(LiveDestroyStrategy.General);
    }

    private bool TryExecuteExactLiveDestroyPlan(
        EcsWorld<NativeChunkArena> world,
        in PhaseChain chain,
        in LiveDestroyPlan plan,
        ref PlaybackCounts counts)
    {
        if (!plan.IsExact)
            return false;

        int candidateCount = 0;
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            StructuralCommandRecord command = _commands[commandIndex];
            if (command.TargetKind != StructuralCommandTargetKind.Entity)
                ThrowCommandInvariant("A live-destroy chain contains a non-live target.");
            System.Diagnostics.Debug.Assert(candidateCount < _createdEntities.Length);
            _createdEntities[candidateCount++] = command.GetEntityTarget();
        }
        ReadOnlySpan<Entity> candidates = _createdEntities.AsSpan(0, candidateCount);
        bool destroyed = plan.Strategy == LiveDestroyStrategy.ExactWholeArchetype
            ? world.TryDestroyWholeArchetype(candidates)
            : world.TryDestroyArchetypeTail(candidates);

        if (!destroyed)
            return false;
        counts.Destroyed = checked(counts.Destroyed + chain.Count);
        return true;
    }

    private void ExecuteGeneralLiveDestroyChain(
        EcsWorld<NativeChunkArena> world,
        in PhaseChain chain,
        ref PlaybackCounts counts)
    {
        if (!_hasTemporaryHoleEligibleArchetype)
        {
            for (int link = chain.HeadLink; link != 0;)
            {
                int commandIndex = link - 1;
                link = _nextCommand[commandIndex];
                Entity entity = _commands[commandIndex].GetEntityTarget();
                if (world.TryDestroy(entity))
                    counts.Destroyed++;
                else
                    counts.RejectedDestroy++;
            }
            return;
        }

        // Every non-exact population retains scalar record-order behavior,
        // including stale and duplicate rejection. Only destroys with matched
        // same-archetype create demand become temporary holes.
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            Entity entity = _commands[commandIndex].GetEntityTarget();
            if (!world.TryGetLocation(entity, out EntityLocation location))
            {
                counts.RejectedDestroy++;
                continue;
            }

            int archetypeIndex = checked((int)location.ArchetypeId.Value);
            if (_temporaryHoleState[archetypeIndex] == TemporaryHoleEligible)
            {
                if (!world.TryDestroyIntoTemporaryHoleValidated(
                        entity,
                        location,
                        out int fixedRowBytes))
                {
                    counts.RejectedDestroy++;
                    continue;
                }

                _commands[commandIndex] = StructuralCommandRecord.TemporaryHole(
                    location.ArchetypeId,
                    location.ChunkRow,
                    fixedRowBytes);
                _nextCommand[commandIndex] = _temporaryHoleHeads[archetypeIndex];
                _temporaryHoleHeads[archetypeIndex] = commandIndex + 1;
                counts.TemporaryHoles++;
                counts.Destroyed++;
                continue;
            }

            if (world.TryDestroy(entity))
                counts.Destroyed++;
            else
                counts.RejectedDestroy++;
        }
    }

    private void PrepareTemporaryHoleDemand(EcsWorld<NativeChunkArena> world)
    {
        // A packed store cannot safely execute ordinary swap-removes while it
        // contains temporary markers. Enable the market only when recorded
        // same-archetype creates cover every currently resolvable destroy in
        // that store. This is deliberately conservative for unbalanced churn.
        for (int index = 0; index < _activeTemporaryArchetypeCount; index++)
        {
            int archetypeIndex = _activeTemporaryArchetypes[index];
            if (world.GetArchetypeFixedRowBytesForPlayback(
                    new ArchetypeId(unchecked((uint)archetypeIndex))) <
                MinimumTemporaryHoleFixedRowBytes)
            {
                continue;
            }
            _temporaryHoleState[archetypeIndex] = TemporaryHoleCountingBias;
            _hasTemporaryHoleEligibleArchetype = true;
        }
        if (!_hasTemporaryHoleEligibleArchetype)
            return;

        for (int link = _liveDestroys.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            Entity entity = _commands[commandIndex].GetEntityTarget();
            if (!world.TryGetLocation(entity, out EntityLocation location))
                continue;
            int archetypeIndex = checked((int)location.ArchetypeId.Value);
            int state = _temporaryHoleState[archetypeIndex];
            if (state <= TemporaryHoleCountingBias && state != TemporaryHoleEligible)
                _temporaryHoleState[archetypeIndex]--;
        }
        _hasTemporaryHoleEligibleArchetype = false;
        for (int index = 0; index < _activeTemporaryArchetypeCount; index++)
        {
            int archetypeIndex = _activeTemporaryArchetypes[index];
            int encodedSupply = _temporaryHoleState[archetypeIndex];
            if (encodedSupply >= 0)
                continue;
            int supply = checked(-encodedSupply - 1);
            _temporaryHoleState[archetypeIndex] = supply != 0 &&
                _createDemand[archetypeIndex] >= supply
                ? TemporaryHoleEligible
                : TemporaryHoleUntracked;
            _hasTemporaryHoleEligibleArchetype |=
                _temporaryHoleState[archetypeIndex] == TemporaryHoleEligible;
        }
    }

    private void FinalizeTemporaryHoles(
        EcsWorld<NativeChunkArena> world,
        ref PlaybackCounts counts)
    {
        for (int activeIndex = 0;
             activeIndex < _activeTemporaryArchetypeCount;
             activeIndex++)
        {
            int archetypeIndex = _activeTemporaryArchetypes[activeIndex];
            if (_temporaryHoleHeads[archetypeIndex] == 0)
                continue;
            var archetypeId = new ArchetypeId(unchecked((uint)archetypeIndex));
            while (world.TryCompactTemporaryHoleTail(archetypeId))
                counts.CompactedTemporaryHoles++;

            for (int link = _temporaryHoleHeads[archetypeIndex]; link != 0;)
            {
                int holeIndex = link - 1;
                link = _nextCommand[holeIndex];
                StructuralCommandRecord hole = _commands[holeIndex];
                if (hole.Kind != StructuralCommandKind.TemporaryHole ||
                    hole.TemporaryHoleArchetypeId != archetypeId)
                {
                    ThrowCommandInvariant("Temporary-hole finalization metadata is invalid.");
                }

                while (world.TryCompactTemporaryHoleTail(archetypeId))
                    counts.CompactedTemporaryHoles++;
                if (!world.TryCompactTemporaryHole(
                        archetypeId,
                        hole.TemporaryHoleRow,
                        out bool relocatedDonor))
                {
                    continue;
                }

                counts.CompactedTemporaryHoles++;
                if (relocatedDonor)
                    counts.TemporaryHoleDonorRelocations++;
            }

            while (world.TryCompactTemporaryHoleTail(archetypeId))
                counts.CompactedTemporaryHoles++;
        }
    }

    private void ExecuteScalarDestroyPass(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        StructuralCommandTargetKind targetKind,
        ref int destroyed,
        ref int rejected)
    {
        for (int index = 0; index < commandCount; index++)
        {
            StructuralCommandRecord command = _commands[index];
            if (command.Kind != StructuralCommandKind.Destroy || command.TargetKind != targetKind)
                continue;

            ExecuteDestroy(command, commandCount, world, ref destroyed, ref rejected);
        }
    }

    private void ExecuteDestroy(
        in StructuralCommandRecord command,
        int commandCount,
        EcsWorld<NativeChunkArena> world,
        ref int destroyed,
        ref int rejected)
    {
        if (!TryResolveTarget(command, commandCount, out Entity entity, out int createIndex) ||
            !world.TryDestroy(entity))
        {
            rejected++;
            return;
        }

        if (createIndex >= 0)
            _createdEntities[createIndex] = Entity.Null;
        destroyed++;
    }

    private void ExecuteIndexedTransitionChain(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        in PhaseChain chain,
        StructuralTransitionPhase expectedPhase,
        ref int applied,
        ref int rejected,
        ref PlaybackDiagnostics diagnostics)
    {
        Span<Entity> batch = stackalloc Entity[ArchetypeMigrator.MaximumBatchSize];
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            StructuralCommandRecord first = _commands[commandIndex];
            if (first.TransitionPhase != expectedPhase)
                ThrowCommandInvariant("A phase-indexed transition was routed to the wrong phase.");
            if (first.Kind is StructuralCommandKind.AddComponent or StructuralCommandKind.RemoveComponent)
            {
                link = _nextCommand[commandIndex];
                ExecuteComponentCommand(first, commandCount, world, ref applied, ref rejected);
                continue;
            }
            if (first.Kind != StructuralCommandKind.Transition)
                ThrowCommandInvariant("A transition phase chain contains an invalid command kind.");
            var runKey = new StructuralRunKey(first);
            uint transitionId = runKey.Argument;
            TransitionRunPlan runPlan = AnalyzeTransitionRun(
                link,
                runKey);
            if (!runPlan.UseGroupedExecution)
            {
                for (int index = 0; index < runPlan.PrefixCount; index++)
                {
                    commandIndex = link - 1;
                    link = _nextCommand[commandIndex];
                    StructuralCommandRecord command = _commands[commandIndex];
                    ExecuteTransition(
                        command,
                        commandCount,
                        world,
                        ref applied,
                        ref rejected);
                }
                continue;
            }

            PreparedArchetypeTransition transition =
                world.GetPublicTransitionForPlayback(transitionId, expectedPhase);
            if (TryExecuteExactWholeSourceTransitionRun(
                    world,
                    commandCount,
                    runKey,
                    transition,
                    ref link,
                    ref applied,
                    ref diagnostics))
            {
                continue;
            }
            if (TryExecuteExactPackedSuffixTransitionRun(
                    world,
                    commandCount,
                    runKey,
                    transition,
                    ref link,
                    ref applied,
                    ref diagnostics))
            {
                continue;
            }

            int batchCount = 0;
            bool destinationExhausted = false;
            while (link != 0)
            {
                commandIndex = link - 1;
                StructuralCommandRecord command = _commands[commandIndex];
                if (!runKey.Matches(command))
                    break;
                link = _nextCommand[commandIndex];

                if (destinationExhausted)
                {
                    rejected++;
                    continue;
                }
                if (!TryResolveTarget(command, commandCount, out Entity entity, out _))
                {
                    rejected++;
                    continue;
                }

                batch[batchCount++] = entity;
                if (batchCount != batch.Length)
                    continue;

                ExecuteTransitionBatch(
                    world,
                    transition,
                    batch,
                    batchCount,
                    ref destinationExhausted,
                    ref applied,
                    ref rejected);
                batchCount = 0;
            }

            if (!destinationExhausted && batchCount != 0)
            {
                ExecuteTransitionBatch(
                    world,
                    transition,
                    batch,
                    batchCount,
                    ref destinationExhausted,
                    ref applied,
                    ref rejected);
            }
        }
    }

    /// <summary>
    /// Removes the fixed-block planning horizon when one homogeneous route is
    /// the complete, ordered source population. Validation and capacity proof
    /// finish before mutation; destination publication remains record ordered
    /// in bounded blocks and source occupancy is reset exactly once.
    /// </summary>
    private bool TryExecuteExactWholeSourceTransitionRun(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        StructuralRunKey runKey,
        PreparedArchetypeTransition transition,
        ref int link,
        ref int applied,
        ref PlaybackDiagnostics diagnostics)
    {
        int runStartLink = link;
        int firstCommandIndex = link - 1;
        StructuralCommandRecord first = _commands[firstCommandIndex];
        if (!TryResolveTarget(first, commandCount, out Entity firstEntity, out _) ||
            !world.TryGetWholeSourceTransitionPopulation(
                firstEntity,
                transition,
                out int population))
        {
            return false;
        }

        int scanLink = link;
        ChunkRow previous = ChunkRow.Invalid;
        int chunkDirection = 0;
        for (int index = 0; index < population; index++)
        {
            if (scanLink == 0)
                return false;
            int commandIndex = scanLink - 1;
            StructuralCommandRecord command = _commands[commandIndex];
            if (!runKey.Matches(command) ||
                !TryResolveTarget(command, commandCount, out Entity entity, out _) ||
                !world.TryGetTransitionSourceRow(entity, transition, out ChunkRow row) ||
                !IsStrictOrderedTransitionRow(
                    previous,
                    row,
                    ref chunkDirection,
                    index == 0))
            {
                return false;
            }

            // Create results and transition commands occupy disjoint record
            // slots, so the existing per-command entity array doubles as
            // allocation-free route scratch without widening the arena.
            _createdEntities[commandIndex] = entity;
            previous = row;
            scanLink = _nextCommand[commandIndex];
        }

        if (scanLink != 0 && runKey.Matches(_commands[scanLink - 1]))
        {
            return false;
        }

        Span<Entity> batch = stackalloc Entity[ArchetypeMigrator.MaximumBatchSize];
        int executeLink = link;
        int remaining = population;
        while (remaining != 0)
        {
            int batchCount = Math.Min(remaining, batch.Length);
            for (int index = 0; index < batchCount; index++)
            {
                int commandIndex = executeLink - 1;
                batch[index] = _createdEntities[commandIndex];
                executeLink = _nextCommand[commandIndex];
            }

            world.TransitionPrevalidatedRouteBlock(
                batch[..batchCount],
                transition);
            remaining -= batchCount;
        }

        world.CompleteWholeSourceTransition(transition, population);
        if (world.IsLifecycleEnabled)
        {
            int publishLink = runStartLink;
            int publishRemaining = population;
            while (publishRemaining != 0)
            {
                int publishCount = Math.Min(publishRemaining, batch.Length);
                for (int index = 0; index < publishCount; index++)
                {
                    int commandIndex = publishLink - 1;
                    batch[index] = _createdEntities[commandIndex];
                    publishLink = _nextCommand[commandIndex];
                }
                world.PublishTransitionLifecycle(batch[..publishCount], transition);
                publishRemaining -= publishCount;
            }
        }
        link = scanLink;
        applied = checked(applied + population);
        diagnostics.ExactWholeSourceResets++;
        return true;
    }

    /// <summary>
    /// Removes the fixed-block planning horizon when one homogeneous route is
    /// exactly the source store's authoritative packed suffix. Validation and
    /// whole-route destination capacity proof finish before mutation. Blocks
    /// publish forward in command order without source removal; finalization
    /// performs one bulk tail shrink and relocates no survivor.
    /// </summary>
    private bool TryExecuteExactPackedSuffixTransitionRun(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        StructuralRunKey runKey,
        PreparedArchetypeTransition transition,
        ref int link,
        ref int applied,
        ref PlaybackDiagnostics diagnostics)
    {
        int runStartLink = link;
        int scanLink = link;
        int runCount = 0;
        while (scanLink != 0)
        {
            int commandIndex = scanLink - 1;
            if (!runKey.Matches(_commands[commandIndex]))
                break;
            runCount++;
            scanLink = _nextCommand[commandIndex];
        }
        if (runCount <= ArchetypeMigrator.MaximumBatchSize)
            return false;

        StructuralCommandRecord first = _commands[link - 1];
        if (!TryResolveTarget(first, commandCount, out Entity firstEntity, out _) ||
            !world.TryCreateExactPackedSuffixTransitionCursor(
                firstEntity,
                transition,
                runCount,
                out int sourcePopulation,
                out int expectedChunkIndex,
                out int expectedRow))
        {
            diagnostics.FailedExactSuffixCandidates++;
            return false;
        }

        int validateLink = link;
        for (int index = 0; index < runCount; index++)
        {
            int commandIndex = validateLink - 1;
            StructuralCommandRecord command = _commands[commandIndex];
            if (!runKey.Matches(command) ||
                !TryResolveTarget(command, commandCount, out Entity entity, out _) ||
                !world.MatchAndAdvanceExactPackedSuffixTransition(
                    entity,
                    transition,
                    ref expectedChunkIndex,
                    ref expectedRow))
            {
                diagnostics.FailedExactSuffixCandidates++;
                return false;
            }

            // Create result slots and transition command slots are disjoint.
            // Preserve command-index addressing so deferred target results in
            // earlier phases remain available to later commands.
            _createdEntities[commandIndex] = entity;
            validateLink = _nextCommand[commandIndex];
        }

        if (expectedChunkIndex != -1 || expectedRow != -1)
            ThrowCommandInvariant("An exact packed-suffix proof did not terminate at the donor tail.");

        Span<Entity> batch = stackalloc Entity[ArchetypeMigrator.MaximumBatchSize];
        int executeLink = link;
        int remaining = runCount;
        while (remaining != 0)
        {
            int batchCount = Math.Min(remaining, batch.Length);
            for (int index = 0; index < batchCount; index++)
            {
                int commandIndex = executeLink - 1;
                batch[index] = _createdEntities[commandIndex];
                executeLink = _nextCommand[commandIndex];
            }

            world.TransitionPrevalidatedRouteBlock(
                batch[..batchCount],
                transition);
            diagnostics.ExactSuffixBlocksStreamed++;
            remaining -= batchCount;
        }

        world.CompleteExactPackedSuffixTransition(
            transition,
            sourcePopulation,
            runCount);
        if (world.IsLifecycleEnabled)
        {
            int publishLink = runStartLink;
            int publishRemaining = runCount;
            while (publishRemaining != 0)
            {
                int publishCount = Math.Min(publishRemaining, batch.Length);
                for (int index = 0; index < publishCount; index++)
                {
                    int commandIndex = publishLink - 1;
                    batch[index] = _createdEntities[commandIndex];
                    publishLink = _nextCommand[commandIndex];
                }
                world.PublishTransitionLifecycle(batch[..publishCount], transition);
                publishRemaining -= publishCount;
            }
        }
        diagnostics.ExactSuffixShrinks++;
        diagnostics.ExactSuffixCommands = checked(diagnostics.ExactSuffixCommands + runCount);
        diagnostics.ExactSuffixRowsBulkShrunk = checked(
            diagnostics.ExactSuffixRowsBulkShrunk + runCount);
        link = scanLink;
        applied = checked(applied + runCount);
        return true;
    }

    private TransitionRunPlan AnalyzeTransitionRun(int link, StructuralRunKey runKey)
    {
        int count = 0;
        while (link != 0 && count < MinimumGroupedTransitionRun)
        {
            int commandIndex = link - 1;
            if (!runKey.Matches(_commands[commandIndex]))
                break;
            count++;
            link = _nextCommand[commandIndex];
        }
        return new TransitionRunPlan(count);
    }

    private static void ExecuteTransitionBatch(
        EcsWorld<NativeChunkArena> world,
        PreparedArchetypeTransition transition,
        Span<Entity> batch,
        int batchCount,
        ref bool destinationExhausted,
        ref int applied,
        ref int rejected)
    {
        int migrated = world.TransitionBatch(
            batch[..batchCount],
            transition,
            out bool batchCapacityExhausted);
        applied = checked(applied + migrated);
        rejected = checked(rejected + batchCount - migrated);
        destinationExhausted = batchCapacityExhausted;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStrictOrderedTransitionRow(
        ChunkRow previous,
        ChunkRow current,
        ref int chunkDirection,
        bool first)
    {
        if (first)
            return current.IsValid;
        if (current.ChunkIndex == previous.ChunkIndex)
            return current.Row == previous.Row + 1;
        if (current.Row != 0)
            return false;

        int direction = current.ChunkIndex > previous.ChunkIndex ? 1 : -1;
        if (chunkDirection == 0)
            chunkDirection = direction;
        return direction == chunkDirection;
    }

    private void ExecuteScalarTransitionPass(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        StructuralTransitionPhase phase,
        ref int applied,
        ref int rejected)
    {
        for (int index = 0; index < commandCount; index++)
        {
            StructuralCommandRecord command = _commands[index];
            if (command.TransitionPhase != phase ||
                command.Kind is not (StructuralCommandKind.Transition or
                    StructuralCommandKind.AddComponent or
                    StructuralCommandKind.RemoveComponent))
            {
                continue;
            }
            if (command.Kind == StructuralCommandKind.Transition)
                ExecuteTransition(command, commandCount, world, ref applied, ref rejected);
            else
                ExecuteComponentCommand(command, commandCount, world, ref applied, ref rejected);
        }
    }

    private void ExecuteTransition(
        in StructuralCommandRecord command,
        int commandCount,
        EcsWorld<NativeChunkArena> world,
        ref int applied,
        ref int rejected)
    {
        if (!TryResolveTarget(command, commandCount, out Entity entity, out _) ||
            !world.TryTransition(entity, command.Argument, out _))
        {
            rejected++;
            return;
        }

        applied++;
    }

    private void ExecuteComponentCommand(
        in StructuralCommandRecord command,
        int commandCount,
        EcsWorld<NativeChunkArena> world,
        ref int applied,
        ref int rejected)
    {
        if (!TryResolveTarget(command, commandCount, out Entity entity, out _))
        {
            rejected++;
            return;
        }

        bool success;
        if (command.Kind == StructuralCommandKind.AddComponent)
        {
            world.GetStructuralComponentPayloadLayout(command.Argument, out int size, out _);
            int payloadOffset = unchecked((int)command.PayloadOffset);
            if (payloadOffset < 0 || payloadOffset > _payloadCount - size)
                ThrowCommandInvariant("A typed structural command references payload outside its arena.");
            success = world.TryApplyStructuralAdd(
                entity,
                command.Argument,
                _payload.AsSpan(payloadOffset, size));
        }
        else if (command.Kind == StructuralCommandKind.RemoveComponent)
        {
            success = world.TryApplyStructuralRemove(entity, command.Argument);
        }
        else
        {
            ThrowCommandInvariant("A typed structural phase contains an invalid command kind.");
            return;
        }

        if (success)
            applied++;
        else
            rejected++;
    }

    private bool TryResolveTarget(
        in StructuralCommandRecord command,
        int commandCount,
        out Entity entity,
        out int createIndex)
    {
        if (command.TargetKind == StructuralCommandTargetKind.Entity)
        {
            entity = command.GetEntityTarget();
            createIndex = -1;
            return true;
        }

        if (command.TargetKind == StructuralCommandTargetKind.Deferred &&
            command.MatchesDeferredTarget(_bufferId, _epoch) &&
            command.DeferredCommandIndex < unchecked((uint)commandCount))
        {
            int index = unchecked((int)command.DeferredCommandIndex);
            if (IsCreateCommand(_commands[index].Kind))
            {
                entity = _createdEntities[index];
                createIndex = index;
                return entity.IsValid;
            }
        }

        entity = Entity.Null;
        createIndex = -1;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAppend(in StructuralCommandRecord command)
    {
        if (_count == _commands.Length)
            return false;
        AppendTrusted(command);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendTrusted(in StructuralCommandRecord command)
    {
        int commandIndex = _count++;
        _commands[commandIndex] = command;
        _nextCommand[commandIndex] = 0;
        if (IsCreateCommand(command.Kind))
        {
            int archetypeIndex = checked((int)command.CreateArchetypeId.Value);
            if (_createDemand[archetypeIndex]++ == 0)
            {
                _activeTemporaryArchetypes[_activeTemporaryArchetypeCount++] =
                    archetypeIndex;
            }
        }
        ref PhaseChain chain = ref GetPhaseChain(GetPlaybackPhase(command));
        int link = commandIndex + 1;
        if (chain.HeadLink == 0)
            chain.HeadLink = link;
        else
            _nextCommand[chain.TailIndex] = link;
        chain.TailIndex = commandIndex;
        chain.Count++;
    }

    private ref PhaseChain GetPhaseChain(StructuralPlaybackPhase phase)
    {
        switch (phase)
        {
            case StructuralPlaybackPhase.LiveDestroy:
                return ref _liveDestroys;
            case StructuralPlaybackPhase.Create:
                return ref _creates;
            case StructuralPlaybackPhase.DeferredDestroy:
                return ref _deferredDestroys;
            case StructuralPlaybackPhase.Remove:
                return ref _removes;
            case StructuralPlaybackPhase.Add:
                return ref _adds;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetPhaseChains()
    {
        _liveDestroys = default;
        _creates = default;
        _deferredDestroys = default;
        _removes = default;
        _adds = default;
    }

    private void ResetTemporaryHoleState()
    {
        for (int index = 0; index < _activeTemporaryArchetypeCount; index++)
        {
            int archetypeIndex = _activeTemporaryArchetypes[index];
            _temporaryHoleHeads[archetypeIndex] = 0;
            _createDemand[archetypeIndex] = 0;
            _temporaryHoleState[archetypeIndex] = TemporaryHoleUntracked;
            _activeTemporaryArchetypes[index] = 0;
        }
        _activeTemporaryArchetypeCount = 0;
        _hasTemporaryHoleEligibleArchetype = false;
    }

    private void BeginRecordingIfNeeded()
    {
        if (_isPlaying)
            throw new InvalidOperationException("Commands cannot be emitted during structural playback.");
        if (!_hasCompletedPlayback)
            return;

        _createdEntities.AsSpan(0, _lastPlaybackCommandCount).Clear();
        _lastPlaybackCommandCount = 0;
        _hasCompletedPlayback = false;
        _payloadCount = 0;
        AdvanceEpoch();
    }

    private void ResetForNewEpoch()
    {
        int clearCount = Math.Max(_count, _lastPlaybackCommandCount);
        _createdEntities.AsSpan(0, clearCount).Clear();
        _count = 0;
        ResetPhaseChains();
        ResetTemporaryHoleState();
        _lastPlaybackCommandCount = 0;
        _hasCompletedPlayback = false;
        _payloadCount = 0;
        AdvanceEpoch();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateArchetype(ArchetypeHandle archetype, string parameterName)
    {
        if (!archetype.IsValid || archetype.World != _worldId)
            throw new ArgumentException("The archetype belongs to a different world.", parameterName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateEntity(Entity entity, string parameterName)
    {
        if (!entity.IsValid || entity.World != _worldId)
            throw new ArgumentException("The entity belongs to a different world.", parameterName);
    }

    private void ValidateDeferredEntity(DeferredEntity entity, string parameterName)
    {
        if (_hasCompletedPlayback ||
            !entity.IsValid ||
            entity.World != _worldId ||
            entity.BufferId != _bufferId ||
            entity.Epoch != _epoch ||
            entity.CommandIndex >= unchecked((uint)_count) ||
            !IsCreateCommand(_commands[entity.CommandIndex].Kind))
        {
            throw new ArgumentException(
                "The deferred entity does not identify a create command in this recording epoch.",
                parameterName);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateTransition(in ArchetypeTransition transition, string parameterName)
    {
        if (!transition.IsValid || transition.WorldId != _worldId)
            throw new ArgumentException("The transition belongs to a different world.", parameterName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidatePrefab(PrefabHandle prefab, string parameterName)
    {
        if (!prefab.IsValid || prefab.World != _worldId)
            throw new ArgumentException("The prefab belongs to a different world.", parameterName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StructuralPlaybackPhase GetPlaybackPhase(
        in StructuralCommandRecord command)
    {
        if (IsCreateCommand(command.Kind))
            return StructuralPlaybackPhase.Create;
        if (command.Kind == StructuralCommandKind.Destroy)
        {
            return command.TargetKind switch
            {
                StructuralCommandTargetKind.Entity => StructuralPlaybackPhase.LiveDestroy,
                StructuralCommandTargetKind.Deferred => StructuralPlaybackPhase.DeferredDestroy,
                _ => throw new InvalidOperationException(
                    "A destroy command has an invalid target kind."),
            };
        }
        if (command.Kind is StructuralCommandKind.Transition or
            StructuralCommandKind.AddComponent or
            StructuralCommandKind.RemoveComponent)
        {
            return command.TransitionPhase switch
            {
                StructuralTransitionPhase.Remove => StructuralPlaybackPhase.Remove,
                StructuralTransitionPhase.Add => StructuralPlaybackPhase.Add,
                _ => throw new InvalidOperationException(
                    "A transition command has an invalid structural phase."),
            };
        }

        throw new InvalidOperationException("The structural command kind is invalid.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCreateCommand(StructuralCommandKind kind) =>
        kind is StructuralCommandKind.CreateDefault or StructuralCommandKind.InstantiatePrefab;

    private int ReservePayload(ReadOnlySpan<byte> payload, int alignment)
    {
        if (_count == _commands.Length)
            return -1;
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        int offset = AlignUp(_payloadCount, alignment);
        if (payload.IsEmpty)
            return offset;
        if (offset > _payload.Length - payload.Length)
            return -1;
        payload.CopyTo(_payload.AsSpan(offset, payload.Length));
        _payloadCount = offset + payload.Length;
        return offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment) =>
        checked((value + alignment - 1) & -alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureOwnerAndActive()
    {
        ObjectDisposedException.ThrowIf(!_isActive, this);
        if (!_schedulerLane && Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "This structural command arena has one recording owner.");
        }
        if (!_schedulerLane && _recordingGate?.IsSystemGraphActive == true)
        {
            throw new InvalidOperationException(
                "The canonical structural command arena is unavailable during scheduler graph execution; use the issued SystemStructuralWriter capability.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceEpoch()
    {
        if (_epoch != uint.MaxValue)
        {
            _epoch++;
            return;
        }

        // Rotating the buffer identity prevents a token from becoming valid again
        // after 2^32 recording epochs without widening its 16-byte layout.
        _bufferId = BufferIdSource.Allocate();
        _epoch = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCommandInvariant(string message) =>
        throw new InvalidOperationException(message);

    private enum StructuralPlaybackPhase : byte
    {
        LiveDestroy = 0,
        Create = 1,
        DeferredDestroy = 2,
        Remove = 3,
        Add = 4,
    }

    private enum LiveDestroyStrategy : byte
    {
        None = 0,
        General = 1,
        ExactWholeArchetype = 2,
        ExactArchetypeTail = 3,
    }

    private readonly struct LiveDestroyPlan
    {
        internal LiveDestroyPlan(LiveDestroyStrategy strategy) => Strategy = strategy;

        internal LiveDestroyStrategy Strategy { get; }

        internal bool IsExact =>
            Strategy is LiveDestroyStrategy.ExactWholeArchetype or
                LiveDestroyStrategy.ExactArchetypeTail;
    }

    private readonly struct TransitionRunPlan
    {
        internal TransitionRunPlan(int prefixCount) => PrefixCount = prefixCount;

        internal int PrefixCount { get; }

        internal bool UseGroupedExecution =>
            PrefixCount >= MinimumGroupedTransitionRun;
    }

    private struct PlaybackCounts
    {
        internal int Created;
        internal int Destroyed;
        internal int Removed;
        internal int Added;
        internal int RejectedCreate;
        internal int RejectedDestroy;
        internal int RejectedTransition;
        internal int TemporaryHoles;
        internal int ReusedTemporaryHoles;
        internal int CompactedTemporaryHoles;
        internal int TemporaryHoleDonorRelocations;
        internal long ReusedTemporaryHoleFixedRowBytes;
    }

    private struct PlaybackDiagnostics
    {
        internal int ExactWholeSourceResets;
        internal int ExactSuffixShrinks;
        internal int ExactSuffixCommands;
        internal int ExactSuffixBlocksStreamed;
        internal int ExactSuffixRowsBulkShrunk;
        internal int FailedExactSuffixCandidates;
    }

    private struct PhaseChain
    {
        internal int HeadLink;
        internal int TailIndex;
        internal int Count;
    }

    private static class BufferIdSource
    {
        private static long _lastIssued;

        internal static uint Allocate()
        {
            long value = Interlocked.Increment(ref _lastIssued);
            if (unchecked((ulong)value) > uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "The process-lifetime structural-buffer identity space is exhausted.");
            }

            return unchecked((uint)value);
        }
    }
}

internal readonly record struct StructuralPlaybackDiagnostics(
    int ExactWholeSourceResets,
    int ExactSuffixShrinks,
    int ExactSuffixCommands,
    int ExactSuffixBlocksStreamed,
    int ExactSuffixRowsBulkShrunk,
    int ExactSuffixDonorRelocations,
    int FailedExactSuffixCandidates);
