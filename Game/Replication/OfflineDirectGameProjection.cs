using System.Runtime.CompilerServices;
using UltraSim.Game.Composition;
using UltraSim.Network;

namespace UltraSim.Game.Replication;

/// <summary>
/// Packet-free client-semantic projection used as the deterministic Network oracle.
/// </summary>
/// <remarks>
/// Baselines and frames stage into private fixed arrays and publish only after
/// complete validation. This type never receives ECS entities or world authority.
/// </remarks>
public sealed class OfflineDirectGameProjection
{
    private readonly int _ownerThreadId;
    private GameProjectedEntity[] _entities;
    private GameProjectedEntity[] _stagingEntities;
    private readonly GameProjectedEntity[] _packedEntities;
    private GameProjectionParticipant[] _participants;
    private GameProjectionParticipant[] _stagingParticipants;
    private readonly NetworkSemanticTrace _trace;
    private StateEpoch _stateEpoch;
    private ulong _generationId;
    private ulong _frameSequence;
    private ulong _nextDeltaSequence;
    private ulong _nextApplicationEventSequence;
    private int _entityCount;
    private int _participantCount;

    public OfflineDirectGameProjection(
        int maximumEntities,
        int maximumParticipants,
        int semanticTraceCapacity = 65_536)
    {
        if (maximumEntities <= 0) throw new ArgumentOutOfRangeException(nameof(maximumEntities));
        if (maximumParticipants <= 0) throw new ArgumentOutOfRangeException(nameof(maximumParticipants));
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _entities = new GameProjectedEntity[maximumEntities];
        _stagingEntities = new GameProjectedEntity[maximumEntities];
        _packedEntities = new GameProjectedEntity[maximumEntities];
        _participants = new GameProjectionParticipant[maximumParticipants];
        _stagingParticipants = new GameProjectionParticipant[maximumParticipants];
        _trace = new NetworkSemanticTrace(semanticTraceCapacity);
    }

    /// <summary>Atomically publishes one whole-epoch baseline from canonical projection truth.</summary>
    public OfflineProjectionApplyStatus ApplyBaseline(
        GameProjectionService source,
        in GameProjectionFrame frame)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(source);
        if (!frame.StateEpoch.IsValid || frame.DeltaCount != 0)
            return OfflineProjectionApplyStatus.InvalidDelta;
        GameProjectionSnapshot sourceSnapshot = source.GetSnapshot();
        if (sourceSnapshot.EntityCount > _stagingEntities.Length ||
            sourceSnapshot.ParticipantCount > _stagingParticipants.Length)
        {
            return OfflineProjectionApplyStatus.CapacityExceeded;
        }
        if (sourceSnapshot.StateEpoch != frame.StateEpoch ||
            sourceSnapshot.ServerGenerationId != frame.ServerGenerationId ||
            sourceSnapshot.FrameSequence != frame.FrameSequence)
        {
            return OfflineProjectionApplyStatus.InvalidDelta;
        }

        Array.Clear(_packedEntities);
        int entityCount = source.CopyEntities(_packedEntities);
        Array.Clear(_stagingEntities);
        for (int index = 0; index < entityCount; index++)
        {
            GameProjectedEntity entity = _packedEntities[index];
            if (!entity.Entity.IsValid || entity.Entity.Index >= _entities.Length)
                return OfflineProjectionApplyStatus.InvalidDelta;
            if (_stagingEntities[entity.Entity.Index].Entity.IsValid)
                return OfflineProjectionApplyStatus.InvalidDelta;
            _stagingEntities[entity.Entity.Index] = entity;
        }

        int participantCount = source.CopyParticipants(_stagingParticipants);
        (_entities, _stagingEntities) = (_stagingEntities, _entities);
        Array.Clear(_stagingEntities);
        (_participants, _stagingParticipants) = (_stagingParticipants, _participants);
        Array.Clear(_stagingParticipants);
        _participantCount = participantCount;
        _entityCount = entityCount;
        _stateEpoch = frame.StateEpoch;
        _generationId = frame.ServerGenerationId;
        _frameSequence = frame.FrameSequence;
        _nextDeltaSequence = 0;
        _nextApplicationEventSequence = frame.ApplicationEventOldestSequence;
        _trace.Clear();
        _trace.TryAppend(new NetworkSemanticTraceEntry(
            NetworkSemanticTraceKind.BaselineCommitted,
            _stateEpoch,
            frame.SimulationTick,
            subject: unchecked((ulong)entityCount),
            version: frame.FrameSequence,
            valueHash: ComputeCanonicalHash()));
        return OfflineProjectionApplyStatus.Success;
    }

    /// <summary>Atomically applies one complete immediately-following OfflineDirect frame.</summary>
    public OfflineProjectionApplyStatus ApplyFrame(
        GameProjectionService source,
        in GameProjectionFrame frame,
        ReadOnlySpan<GameProjectionDelta> deltas)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(source);
        if (frame.StateEpoch != _stateEpoch ||
            frame.ServerGenerationId != _generationId ||
            frame.PreviousFrameSequence != _frameSequence ||
            _frameSequence == ulong.MaxValue ||
            frame.FrameSequence != _frameSequence + 1)
            return OfflineProjectionApplyStatus.BaselineRequired;
        if (frame.DeltaCount != deltas.Length)
            return OfflineProjectionApplyStatus.InvalidDelta;
        GameProjectionSnapshot sourceSnapshot = source.GetSnapshot();
        if (sourceSnapshot.StateEpoch != frame.StateEpoch ||
            sourceSnapshot.ServerGenerationId != frame.ServerGenerationId ||
            sourceSnapshot.FrameSequence != frame.FrameSequence)
        {
            return OfflineProjectionApplyStatus.InvalidDelta;
        }
        if (sourceSnapshot.ParticipantCount > _stagingParticipants.Length ||
            _trace.Capacity - _trace.Count < deltas.Length)
        {
            return OfflineProjectionApplyStatus.CapacityExceeded;
        }

        _entities.CopyTo(_stagingEntities, 0);
        int entityCount = _entityCount;
        ulong expectedSequence = _nextDeltaSequence;
        for (int index = 0; index < deltas.Length; index++)
        {
            ref readonly GameProjectionDelta delta = ref deltas[index];
            if (!delta.Entity.IsValid || delta.Entity.Index >= _stagingEntities.Length ||
                delta.Sequence != expectedSequence++)
            {
                return OfflineProjectionApplyStatus.InvalidDelta;
            }
            ref GameProjectedEntity target = ref _stagingEntities[delta.Entity.Index];
            switch (delta.Kind)
            {
                case GameProjectionDeltaKind.Created:
                    if (target.Entity.IsValid) return OfflineProjectionApplyStatus.InvalidDelta;
                    target = new GameProjectedEntity(
                        delta.Entity, delta.HasSimulationState, delta.SimulationState);
                    entityCount++;
                    break;
                case GameProjectionDeltaKind.Destroyed:
                    if (target.Entity != delta.Entity) return OfflineProjectionApplyStatus.InvalidDelta;
                    target = default;
                    entityCount--;
                    break;
                case GameProjectionDeltaKind.ComponentAdded:
                case GameProjectionDeltaKind.ComponentRemoved:
                    if (target.Entity != delta.Entity) return OfflineProjectionApplyStatus.InvalidDelta;
                    if (delta.Component == GameSchema.GetComponentId<GameSimulationState>())
                    {
                        target = target with
                        {
                            HasSimulationState = delta.HasSimulationState,
                            SimulationState = delta.SimulationState,
                        };
                    }
                    break;
                case GameProjectionDeltaKind.SimulationStateChanged:
                    if (target.Entity != delta.Entity) return OfflineProjectionApplyStatus.InvalidDelta;
                    target = target with
                    {
                        HasSimulationState = true,
                        SimulationState = delta.SimulationState,
                    };
                    break;
                default:
                    return OfflineProjectionApplyStatus.InvalidDelta;
            }
        }

        int participantCount = source.CopyParticipants(_stagingParticipants);
        if (entityCount != sourceSnapshot.EntityCount || participantCount != sourceSnapshot.ParticipantCount)
            return OfflineProjectionApplyStatus.InvalidDelta;
        (_entities, _stagingEntities) = (_stagingEntities, _entities);
        (_participants, _stagingParticipants) = (_stagingParticipants, _participants);
        Array.Clear(_stagingParticipants);
        _entityCount = entityCount;
        _participantCount = participantCount;
        _generationId = frame.ServerGenerationId;
        _frameSequence = frame.FrameSequence;
        _nextDeltaSequence = expectedSequence;
        for (int index = 0; index < deltas.Length; index++)
        {
            GameProjectionDelta delta = deltas[index];
            _trace.TryAppend(new NetworkSemanticTraceEntry(
                delta.Kind is GameProjectionDeltaKind.Created or GameProjectionDeltaKind.Destroyed or
                    GameProjectionDeltaKind.ComponentAdded or GameProjectionDeltaKind.ComponentRemoved
                    ? NetworkSemanticTraceKind.ProjectionLifecyclePublished
                    : NetworkSemanticTraceKind.ProjectionStatePublished,
                _stateEpoch,
                frame.SimulationTick,
                Pack(delta.Entity),
                delta.Sequence,
                unchecked((ulong)(uint)delta.SimulationState.TickMarker)));
        }
        return OfflineProjectionApplyStatus.Success;
    }

    /// <summary>Applies a complete ordered prefix of independently retained application facts.</summary>
    public OfflineProjectionApplyStatus ApplyApplicationEvents(ReadOnlySpan<GameApplicationEvent> events)
    {
        EnsureOwnerThread();
        if (_trace.Capacity - _trace.Count < events.Length)
            return OfflineProjectionApplyStatus.CapacityExceeded;
        ulong expected = _nextApplicationEventSequence;
        for (int index = 0; index < events.Length; index++)
        {
            ref readonly GameApplicationEvent applicationEvent = ref events[index];
            if (applicationEvent.StateEpoch != _stateEpoch || applicationEvent.Sequence != expected++)
                return OfflineProjectionApplyStatus.InvalidDelta;
        }
        for (int index = 0; index < events.Length; index++)
        {
            GameApplicationEvent applicationEvent = events[index];
            _trace.TryAppend(new NetworkSemanticTraceEntry(
                NetworkSemanticTraceKind.ApplicationEventPublished,
                _stateEpoch,
                simulationTick: 0,
                applicationEvent.Subject,
                applicationEvent.Sequence,
                applicationEvent.PayloadHash));
        }
        _nextApplicationEventSequence = expected;
        return OfflineProjectionApplyStatus.Success;
    }

    public int CopyEntities(Span<GameProjectedEntity> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _entityCount)
            throw new ArgumentException("Destination cannot contain every client entity.", nameof(destination));
        int written = 0;
        for (int index = 0; index < _entities.Length; index++)
            if (_entities[index].Entity.IsValid) destination[written++] = _entities[index];
        return written;
    }

    public int CopyParticipants(Span<GameProjectionParticipant> destination)
    {
        EnsureOwnerThread();
        if (destination.Length < _participantCount)
            throw new ArgumentException("Destination cannot contain every client participant.", nameof(destination));
        _participants.AsSpan(0, _participantCount).CopyTo(destination);
        return _participantCount;
    }

    public OfflineProjectionSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(
            _stateEpoch,
            _generationId,
            _frameSequence,
            _entityCount,
            _participantCount,
            _nextApplicationEventSequence,
            _trace.ComputeDigest(),
            ComputeCanonicalHash(),
            checked(
                (long)(_entities.Length + _stagingEntities.Length + _packedEntities.Length) *
                    Unsafe.SizeOf<GameProjectedEntity>() +
                (long)(_participants.Length + _stagingParticipants.Length) *
                    Unsafe.SizeOf<GameProjectionParticipant>() +
                _trace.ReservedBytes));
    }

    private ulong ComputeCanonicalHash()
    {
        ulong hash = 14695981039346656037UL;
        Mix(ref hash, _stateEpoch.Value);
        Mix(ref hash, _generationId);
        for (int index = 0; index < _entities.Length; index++)
        {
            GameProjectedEntity entity = _entities[index];
            if (!entity.Entity.IsValid) continue;
            Mix(ref hash, Pack(entity.Entity));
            Mix(ref hash, entity.HasSimulationState ? 1UL : 0UL);
            Mix(ref hash, unchecked((ulong)(uint)entity.SimulationState.TickMarker));
        }
        for (int index = 0; index < _participantCount; index++)
        {
            GameProjectionParticipant participant = _participants[index];
            Mix(ref hash, participant.Participant.Value);
            Mix(ref hash, Pack(participant.ControlledEntity));
        }
        return hash;
    }

    private static ulong Pack(ReplicationEntityId id) =>
        ((ulong)id.Generation << 32) | id.Index;

    private static void Mix(ref ulong hash, ulong value)
    {
        hash = unchecked((hash ^ value) * 1099511628211UL);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Offline projection is bound to its creating owner thread.");
    }
}
