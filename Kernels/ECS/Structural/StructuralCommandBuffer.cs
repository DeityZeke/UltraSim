using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS;

/// <summary>
/// Fixed-capacity, single-writer arena for one deterministic structural barrier.
/// </summary>
/// <remarks>
/// Recording never mutates the world and is permitted inside ECS jobs. Playback
/// is explicit and consumes commands in destroy, create, remove, then add phases.
/// Expected stale-handle and capacity failures are counted rather than thrown.
/// Unexpected playback exceptions consume the arena and fault the owning world.
/// </remarks>
public sealed class StructuralCommandBuffer
{
    private readonly StructuralCommandRecord[] _commands;
    private readonly Entity[] _createdEntities;
    private readonly int[] _nextCommand;
    private readonly WorldId _worldId;
    private uint _bufferId;
    private readonly int _ownerThreadId;
    private int _count;
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

    internal StructuralCommandBuffer(WorldId worldId, int ownerThreadId, int capacity)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (ownerThreadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(ownerThreadId));
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _worldId = worldId;
        _bufferId = BufferIdSource.Allocate();
        _ownerThreadId = ownerThreadId;
        _commands = new StructuralCommandRecord[capacity];
        _createdEntities = new Entity[capacity];
        _nextCommand = new int[capacity];
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
         sizeof(int)));

    public DeferredEntity EnqueueCreateDefault(ArchetypeHandle archetype)
    {
        if (!TryEnqueueCreateDefault(archetype, out DeferredEntity entity))
            throw new StructuralCommandCapacityExceededException(Capacity);
        return entity;
    }

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
            _commands[token.CommandIndex].Kind != StructuralCommandKind.CreateDefault)
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
        PlaybackCore(world, usePhaseIndex: true);

    /// <summary>
    /// Retained reference executor for differential tests and same-process
    /// benchmarks. Production playback must use the phase-indexed path.
    /// </summary>
    internal StructuralPlaybackResult PlaybackScalarOracle(EcsWorld<NativeChunkArena> world) =>
        PlaybackCore(world, usePhaseIndex: false);

    private StructuralPlaybackResult PlaybackCore(
        EcsWorld<NativeChunkArena> world,
        bool usePhaseIndex)
    {
        ArgumentNullException.ThrowIfNull(world);
        EnsureOwnerAndActive();
        if (_isPlaying)
            throw new InvalidOperationException("Structural command playback cannot be nested or re-entered.");
        if (_count == 0)
            return default;

        int commandCount = _count;
        var counts = new PlaybackCounts();
        bool completed = false;
        _isPlaying = true;
        try
        {
            if (usePhaseIndex)
                ExecutePhaseIndexed(world, commandCount, ref counts);
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
                counts.RejectedTransition);
            _lastPlaybackCommandCount = commandCount;
            _hasCompletedPlayback = true;
            completed = true;
            return result;
        }
        finally
        {
            _count = 0;
            ResetPhaseChains();
            _isPlaying = false;
            if (!completed)
            {
                _createdEntities.AsSpan(0, commandCount).Clear();
                _lastPlaybackCommandCount = 0;
                _hasCompletedPlayback = false;
            }
        }
    }

    private void ExecutePhaseIndexed(
        EcsWorld<NativeChunkArena> world,
        int commandCount,
        ref PlaybackCounts counts)
    {
        ExecuteIndexedDestroyChain(
            world,
            commandCount,
            _liveDestroys,
            ref counts);
        ExecuteIndexedCreateChain(
            world,
            _creates,
            ref counts);
        ExecuteIndexedDestroyChain(
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
            ref counts.RejectedTransition);
        ExecuteIndexedTransitionChain(
            world,
            commandCount,
            _adds,
            StructuralTransitionPhase.Add,
            ref counts.Added,
            ref counts.RejectedTransition);
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
            if (command.Kind == StructuralCommandKind.CreateDefault)
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
        ResetPhaseChains();
        _lastPlaybackCommandCount = 0;
        _hasCompletedPlayback = false;
        _isPlaying = false;
        _isActive = false;
    }

    private void ExecuteIndexedCreateChain(
        EcsWorld<NativeChunkArena> world,
        in PhaseChain chain,
        ref PlaybackCounts counts)
    {
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            ExecuteCreate(_commands[commandIndex], commandIndex, world, ref counts);
        }
    }

    private void ExecuteCreate(
        in StructuralCommandRecord command,
        int commandIndex,
        EcsWorld<NativeChunkArena> world,
        ref PlaybackCounts counts)
    {
        if (world.TryCreateDefault(new ArchetypeId(command.Argument), out Entity entity))
        {
            _createdEntities[commandIndex] = entity;
            counts.Created++;
        }
        else
        {
            _createdEntities[commandIndex] = Entity.Null;
            counts.RejectedCreate++;
        }
    }

    private void ExecuteIndexedDestroyChain(
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
        ref int rejected)
    {
        for (int link = chain.HeadLink; link != 0;)
        {
            int commandIndex = link - 1;
            link = _nextCommand[commandIndex];
            StructuralCommandRecord command = _commands[commandIndex];
            if (command.TransitionPhase != expectedPhase)
                ThrowCommandInvariant("A phase-indexed transition was routed to the wrong phase.");
            ExecuteTransition(command, commandCount, world, ref applied, ref rejected);
        }
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
            if (command.Kind != StructuralCommandKind.Transition ||
                command.TransitionPhase != phase)
            {
                continue;
            }

            ExecuteTransition(command, commandCount, world, ref applied, ref rejected);
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
            if (_commands[index].Kind == StructuralCommandKind.CreateDefault)
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
        ref PhaseChain chain = ref GetPhaseChain(GetPlaybackPhase(command));
        int link = commandIndex + 1;
        if (chain.HeadLink == 0)
            chain.HeadLink = link;
        else
            _nextCommand[chain.TailIndex] = link;
        chain.TailIndex = commandIndex;
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

    private void BeginRecordingIfNeeded()
    {
        if (_isPlaying)
            throw new InvalidOperationException("Commands cannot be emitted during structural playback.");
        if (!_hasCompletedPlayback)
            return;

        _createdEntities.AsSpan(0, _lastPlaybackCommandCount).Clear();
        _lastPlaybackCommandCount = 0;
        _hasCompletedPlayback = false;
        AdvanceEpoch();
    }

    private void ResetForNewEpoch()
    {
        int clearCount = Math.Max(_count, _lastPlaybackCommandCount);
        _createdEntities.AsSpan(0, clearCount).Clear();
        _count = 0;
        ResetPhaseChains();
        _lastPlaybackCommandCount = 0;
        _hasCompletedPlayback = false;
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
            _commands[entity.CommandIndex].Kind != StructuralCommandKind.CreateDefault)
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
    private static StructuralPlaybackPhase GetPlaybackPhase(
        in StructuralCommandRecord command)
    {
        if (command.Kind == StructuralCommandKind.CreateDefault)
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
        if (command.Kind == StructuralCommandKind.Transition)
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
    private void EnsureOwnerAndActive()
    {
        ObjectDisposedException.ThrowIf(!_isActive, this);
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "This structural command arena has one recording owner.");
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

    private struct PlaybackCounts
    {
        internal int Created;
        internal int Destroyed;
        internal int Removed;
        internal int Added;
        internal int RejectedCreate;
        internal int RejectedDestroy;
        internal int RejectedTransition;
    }

    private struct PhaseChain
    {
        internal int HeadLink;
        internal int TailIndex;
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
