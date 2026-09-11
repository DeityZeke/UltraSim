using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.Physics;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

namespace UltraSim.Game.Composition;

/// <summary>Stable runtime identities created for one T0.10 woods/cabin fixture.</summary>
public readonly record struct GamePlayableWorldEntities(
    Entity Player,
    Entity Backpack,
    Entity CabinDoor,
    Entity CabinChest,
    Entity WoodlandAnimal,
    Entity Bread,
    PersistentEntityId PlayerId,
    PersistentEntityId BackpackId,
    PersistentEntityId CabinDoorId,
    PersistentEntityId CabinChestId,
    PersistentEntityId WoodlandAnimalId,
    PersistentEntityId BreadId,
    Entity SkyTelepad,
    PersistentEntityId SkyTelepadId,
    Entity CentralFarGate,
    PersistentEntityId CentralFarGateId,
    Entity FarReturnGate,
    PersistentEntityId FarReturnGateId,
    Entity WelcomeSign,
    PersistentEntityId WelcomeSignId,
    Entity FallbackLabSign,
    PersistentEntityId FallbackLabSignId,
    Entity IronSwordSpawner,
    PersistentEntityId IronSwordSpawnerId);

/// <summary>
/// Shared authoritative Concept Demo structure created once per Game generation. These identities
/// belong to the world, not to any participant/application session.
/// </summary>
public readonly record struct GamePlayableWorldScene(
    Entity CabinDoor,
    Entity CabinChest,
    Entity WoodlandAnimal,
    Entity SkyTelepad,
    Entity CentralFarGate,
    Entity FarReturnGate,
    Entity WelcomeSign,
    Entity FallbackLabSign,
    Entity IronSwordSpawner,
    PersistentEntityId CabinDoorId,
    PersistentEntityId CabinChestId,
    PersistentEntityId WoodlandAnimalId,
    PersistentEntityId SkyTelepadId,
    PersistentEntityId CentralFarGateId,
    PersistentEntityId FarReturnGateId,
    PersistentEntityId WelcomeSignId,
    PersistentEntityId FallbackLabSignId,
    PersistentEntityId IronSwordSpawnerId)
{
    public bool IsValid => CabinDoorId.IsValid && CabinChestId.IsValid &&
        WoodlandAnimalId.IsValid && SkyTelepadId.IsValid && CentralFarGateId.IsValid &&
        FarReturnGateId.IsValid && WelcomeSignId.IsValid && FallbackLabSignId.IsValid &&
        IronSwordSpawnerId.IsValid;
}

/// <summary>Participant-owned playable/inventory identities inside one shared Game generation.</summary>
public readonly record struct GamePlayableParticipantEntities(
    Entity Player,
    Entity Backpack,
    Entity Bread,
    PersistentEntityId PlayerId,
    PersistentEntityId BackpackId,
    PersistentEntityId BreadId)
{
    public bool IsValid => PlayerId.IsValid && BackpackId.IsValid && BreadId.IsValid;
}

/// <summary>Concrete authoritative action outcomes for the first playable vertical.</summary>
public enum GameWorldActionStatus : byte
{
    NotRequested = 0,
    Accepted,
    InvalidCommand,
    StaleCommand,
    StaleControlSession,
    MissingActor,
    MissingTarget,
    UnauthorizedContainer,
    OutOfRange,
    OutsidePreparedWorld,
    InvariantViolation,
    Blocked,
    Unsupported,
    NotGrounded,
}

/// <summary>Admission and independently composed effects of one player command.</summary>
public readonly record struct GamePlayerCommandResult(
    GameWorldActionStatus Admission,
    GameWorldActionStatus Locomotion,
    GameWorldActionStatus Jump,
    GameWorldActionStatus Primary,
    GameWorldActionStatus Interaction,
    WorldPosition Position,
    bool DoorIsOpen)
{
    public bool WasAdmitted => Admission == GameWorldActionStatus.Accepted;
}

/// <summary>Generation-local authoritative vertical lifecycle; it is not durable save state.</summary>
public enum GameVerticalMotionKind : byte
{
    None = 0,
    Grounded,
    Airborne,
}

/// <summary>Concrete Game-owned jump/gravity/recovery policy for the current playable body.</summary>
public readonly record struct GameVerticalMotionOptions(
    int JumpSpeedMillimetersPerSecond,
    int GravityMillimetersPerSecondSquared,
    int RecoveryMinimumYMillimeters,
    WorldPosition RecoveryPosition)
{
    public static GameVerticalMotionOptions PlayableDefault => new(
        JumpSpeedMillimetersPerSecond: 11_000,
        GravityMillimetersPerSecondSquared: -18_000,
        RecoveryMinimumYMillimeters: -50_000,
        RecoveryPosition: GamePlayableWorld.PlayerStart);

    public bool IsValid => JumpSpeedMillimetersPerSecond > 0 &&
        GravityMillimetersPerSecondSquared < 0 &&
        RecoveryMinimumYMillimeters < RecoveryPosition.YMillimeters;
}

/// <summary>Read-only diagnostics for one actor's current generation-local vertical state.</summary>
public readonly record struct GameVerticalMotionSnapshot(
    GameVerticalMotionKind Kind,
    int VelocityMillimetersPerSecond,
    long CumulativeJumpsAccepted,
    long CumulativeLandings,
    long CumulativeRecoveries)
{
    public bool IsGrounded => Kind == GameVerticalMotionKind.Grounded;
    public bool IsAirborne => Kind == GameVerticalMotionKind.Airborne;
}

/// <summary>
/// Generation-owned concrete movement/interaction capability. It consumes the common input command,
/// validates ECS/world truth, and mutates only authoritative persisted components.
/// </summary>
public sealed class GameWorldActions
{
    private const int ActorCollisionRadiusMillimeters = 300;
    private const int ActorSupportRadiusMillimeters = 150;
    private const int ActorCollisionHeightMillimeters = 1_800;
    private const int ActorMaximumStepUpMillimeters = 500;
    private const int ActorMaximumStepDownMillimeters = 500;
    private static readonly SimplePhysicsBody s_actorBody = new(
        ActorCollisionRadiusMillimeters,
        ActorCollisionRadiusMillimeters,
        ActorSupportRadiusMillimeters,
        ActorSupportRadiusMillimeters,
        ActorCollisionHeightMillimeters,
        ActorMaximumStepUpMillimeters,
        ActorMaximumStepDownMillimeters);

    private enum RetainedLocomotionKind : byte
    {
        None = 0,
        Direct,
        Destination,
    }

    public const int FullInputSpeedMillimetersPerSecond = 6_000;
    public const int InteractionRangeMillimeters = 2_500;

    private readonly WorldId _worldId;
    private readonly GameWorldState _worldState;
    private readonly ComponentLookup<GameCharacter> _character;
    private readonly ReadJobPlan<GameCharacter> _characterBaseline;
    private readonly ComponentLookup<GameCabinDoorState> _door;
    private readonly UltraSimSimplePhysics _physics;
    private readonly GamePlayableCollisionProjection _collisionProjection;
    private readonly GameVerticalMotionOptions _verticalOptions;
    private readonly ulong[] _controlEpochs;
    private readonly ulong[] _lastSequences;
    private readonly uint[] _controlGenerations;
    private readonly RetainedLocomotionKind[] _locomotionKinds;
    private readonly short[] _directX;
    private readonly short[] _directZ;
    private readonly long[] _destinationX;
    private readonly long[] _destinationZ;
    private readonly long[] _destinationStartX;
    private readonly long[] _destinationStartZ;
    private readonly long[] _destinationRouteLength;
    private readonly long[] _destinationProgress;
    private readonly long[] _movementRemainderX;
    private readonly long[] _movementRemainderZ;
    private readonly short[] _airborneDirectX;
    private readonly short[] _airborneDirectZ;
    private readonly long[] _airborneMovementRemainderX;
    private readonly long[] _airborneMovementRemainderZ;
    private readonly uint[] _verticalGenerations;
    private readonly GameVerticalMotionKind[] _verticalKinds;
    private readonly long[] _verticalVelocityNumerators;
    private readonly Int128[] _verticalDisplacementRemainders;
    private readonly Entity[] _activeActors;
    private readonly int[] _activeActorPositions;
    private readonly uint[] _characterScratch;
    private int _activeActorCount;
    private int _activeLocomotionCount;
    private int _activeAirborneCount;
    private long _jumpsAccepted;
    private long _landings;
    private long _recoveries;

    internal GameWorldActions(
        EcsWorld world,
        GameWorldState worldState,
        UltraSimSimplePhysics physics,
        GamePlayableCollisionProjection collisionProjection,
        GameVerticalMotionOptions? verticalOptions = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _collisionProjection = collisionProjection ??
            throw new ArgumentNullException(nameof(collisionProjection));
        _verticalOptions = verticalOptions ?? GameVerticalMotionOptions.PlayableDefault;
        if (!_verticalOptions.IsValid)
            throw new ArgumentException("The Game vertical-motion policy is invalid.", nameof(verticalOptions));
        _worldId = world.Id;
        _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
        if (_worldState.WorldId != _worldId)
            throw new ArgumentException(
                "World actions and current-world state belong to different generations.",
                nameof(worldState));
        _character = world.PrepareLookup<GameCharacter>();
        _characterBaseline = world.PrepareReadJob<GameCharacter>();
        _door = world.PrepareLookup<GameCabinDoorState>();
        _controlEpochs = new ulong[world.Options.EntityCapacity.MaximumEntities];
        _lastSequences = new ulong[world.Options.EntityCapacity.MaximumEntities];
        _controlGenerations = new uint[_lastSequences.Length];
        _locomotionKinds = new RetainedLocomotionKind[_lastSequences.Length];
        _directX = new short[_lastSequences.Length];
        _directZ = new short[_lastSequences.Length];
        _destinationX = new long[_lastSequences.Length];
        _destinationZ = new long[_lastSequences.Length];
        _destinationStartX = new long[_lastSequences.Length];
        _destinationStartZ = new long[_lastSequences.Length];
        _destinationRouteLength = new long[_lastSequences.Length];
        _destinationProgress = new long[_lastSequences.Length];
        _movementRemainderX = new long[_lastSequences.Length];
        _movementRemainderZ = new long[_lastSequences.Length];
        _airborneDirectX = new short[_lastSequences.Length];
        _airborneDirectZ = new short[_lastSequences.Length];
        _airborneMovementRemainderX = new long[_lastSequences.Length];
        _airborneMovementRemainderZ = new long[_lastSequences.Length];
        _verticalGenerations = new uint[_lastSequences.Length];
        _verticalKinds = new GameVerticalMotionKind[_lastSequences.Length];
        _verticalVelocityNumerators = new long[_lastSequences.Length];
        _verticalDisplacementRemainders = new Int128[_lastSequences.Length];
        _activeActors = new Entity[_lastSequences.Length];
        _activeActorPositions = new int[_lastSequences.Length];
        _characterScratch = new uint[_lastSequences.Length];
    }

    /// <summary>Known fixed backing reserved for control, locomotion, and vertical state.</summary>
    public long ReservedBytes => checked((long)_lastSequences.Length *
        ((sizeof(ulong) * 2) + sizeof(uint) + (sizeof(short) * 4) +
         (sizeof(long) * 11) + (sizeof(uint) * 3) + (sizeof(byte) * 2) +
         Unsafe.SizeOf<Int128>() + Unsafe.SizeOf<Entity>() + sizeof(int)) +
        _physics.ReservedBytes + _collisionProjection.ReservedBytes);

    public int ActiveLocomotionCount => _activeLocomotionCount;

    public int ActiveAirborneCount => _activeAirborneCount;

    /// <summary>The concrete generation-owned Simple Physics resolver.</summary>
    public UltraSimSimplePhysics SimplePhysics => _physics;

    /// <summary>
    /// Establishes or replaces the trusted operational control lineage for one actor. Replacing a
    /// lineage clears retained locomotion before a command from the new session can be admitted.
    /// </summary>
    public GameWorldActionStatus BeginControlSession(
        EcsWorld world,
        Entity actor,
        GameControlSessionEpoch epoch)
    {
        EnsureWorld(world);
        if (!epoch.IsValid)
            return GameWorldActionStatus.InvalidCommand;
        if (!world.IsAlive(actor) || !world.Has(actor, _character))
            return GameWorldActionStatus.MissingActor;

        int index = checked((int)actor.Index);
        if (_controlGenerations[index] == actor.Generation &&
            _controlEpochs[index] == epoch.Value)
            return GameWorldActionStatus.Accepted;

        ClearLocomotion(index);
        if (_verticalGenerations[index] != actor.Generation ||
            _verticalKinds[index] == GameVerticalMotionKind.None)
            ReconcileVerticalState(world, actor);
        _controlGenerations[index] = actor.Generation;
        _controlEpochs[index] = epoch.Value;
        _lastSequences[index] = 0;
        return GameWorldActionStatus.Accepted;
    }

    /// <summary>Reads generation-local vertical state without exposing solver-owned caches.</summary>
    public bool TryReadVerticalMotion(
        EcsWorld world,
        Entity actor,
        out GameVerticalMotionSnapshot snapshot)
    {
        EnsureWorld(world);
        if (!world.IsAlive(actor) || !world.Has(actor, _character))
        {
            snapshot = default;
            return false;
        }
        int index = checked((int)actor.Index);
        if (_verticalGenerations[index] != actor.Generation ||
            _verticalKinds[index] == GameVerticalMotionKind.None)
        {
            snapshot = default;
            return false;
        }
        snapshot = new GameVerticalMotionSnapshot(
            _verticalKinds[index],
            checked((int)(_verticalVelocityNumerators[index] /
                WorldClockOptions.WorldTimeQuantaPerSecond)),
            _jumpsAccepted,
            _landings,
            _recoveries);
        return true;
    }

    private GameWorldActionStatus TryBeginJump(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in WorldPosition current)
    {
        int index = checked((int)actor.Index);
        if (_verticalGenerations[index] != actor.Generation ||
            _verticalKinds[index] == GameVerticalMotionKind.None)
            ReconcileVerticalState(world, actor);
        if (_verticalKinds[index] != GameVerticalMotionKind.Grounded)
            return GameWorldActionStatus.NotGrounded;

        bool complete = _collisionProjection.TryCapture(
            world, worldState, actor, out ReadOnlySpan<WorldStaticBounds> dynamicBounds);
        if (current.YMillimeters == int.MaxValue)
            return GameWorldActionStatus.OutsidePreparedWorld;
        SimpleAirborneMotionResult clearance = _physics.ResolveAirborneMotion(
            new SimpleAirborneMotionRequest(
                current,
                current.GlobalXMillimeters,
                current.YMillimeters + 1,
                current.GlobalZMillimeters,
                s_actorBody),
            dynamicBounds,
            complete);
        if (clearance.Status == SimpleAirborneMotionStatus.OutsidePreparedWorld)
            return GameWorldActionStatus.OutsidePreparedWorld;
        if (clearance.Status != SimpleAirborneMotionStatus.Airborne)
            return GameWorldActionStatus.Blocked;

        BeginAirborne(actor, index, _verticalOptions.JumpSpeedMillimetersPerSecond);
        _jumpsAccepted++;
        return GameWorldActionStatus.Accepted;
    }

    /// <summary>Ends only the matching control lineage and immediately clears retained locomotion.</summary>
    public GameWorldActionStatus EndControlSession(
        EcsWorld world,
        Entity actor,
        GameControlSessionEpoch epoch)
    {
        EnsureWorld(world);
        if (!epoch.IsValid)
            return GameWorldActionStatus.InvalidCommand;
        if (!world.IsAlive(actor) || !world.Has(actor, _character))
            return GameWorldActionStatus.MissingActor;

        int index = checked((int)actor.Index);
        if (_controlGenerations[index] != actor.Generation ||
            _controlEpochs[index] != epoch.Value)
            return GameWorldActionStatus.StaleControlSession;

        ClearLocomotion(index);
        _controlEpochs[index] = 0;
        _lastSequences[index] = 0;
        return GameWorldActionStatus.Accepted;
    }

    /// <summary>
    /// Admits one actor command exactly once, then independently composes its locomotion mutation
    /// and action effects. Position changes occur during fixed-step maintenance, not on arrival.
    /// </summary>
    public GameWorldActionStatus ApplyPlayerCommand(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in GamePlayerCommand command,
        Entity interactionTarget,
        out GamePlayerCommandResult result)
    {
        EnsureWorld(world, worldState);
        result = default;
        if (!command.IsValid)
        {
            result = result with { Admission = GameWorldActionStatus.InvalidCommand };
            return GameWorldActionStatus.InvalidCommand;
        }
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition current))
        {
            result = result with { Admission = GameWorldActionStatus.MissingActor };
            return GameWorldActionStatus.MissingActor;
        }

        int index = checked((int)actor.Index);
        if (_controlGenerations[index] != actor.Generation ||
            _controlEpochs[index] != command.ControlSession.Value)
        {
            result = result with
            {
                Admission = GameWorldActionStatus.StaleControlSession,
                Position = current,
            };
            return GameWorldActionStatus.StaleControlSession;
        }

        if (!SequenceIsFresh(actor, command.Sequence))
        {
            result = result with { Admission = GameWorldActionStatus.StaleCommand, Position = current };
            return GameWorldActionStatus.StaleCommand;
        }

        // Admission is the replay boundary. A fresh command is consumed before
        // any effect is interpreted, including effects that later reject.
        CommitSequence(actor, command.Sequence);

        GameWorldActionStatus locomotion = ApplyLocomotionUpdate(
            worldState, actor, current, command.Locomotion);

        GameWorldActionStatus jump = GameWorldActionStatus.NotRequested;
        if ((command.PressedActions & GamePlayerActions.Jump) != 0)
            jump = TryBeginJump(world, worldState, actor, current);

        GameWorldActionStatus interaction = GameWorldActionStatus.NotRequested;
        bool doorIsOpen = false;
        if ((command.PressedActions & GamePlayerActions.Interact) != 0)
        {
            interaction = InteractWithWorldTarget(
                world, worldState, actor, interactionTarget, out doorIsOpen);
        }

        result = new GamePlayerCommandResult(
            GameWorldActionStatus.Accepted,
            locomotion,
            jump,
            GameWorldActionStatus.NotRequested,
            interaction,
            current,
            doorIsOpen);
        return GameWorldActionStatus.Accepted;
    }

    private GameWorldActionStatus ApplyLocomotionUpdate(
        GameWorldState worldState,
        Entity actor,
        in WorldPosition current,
        in GameLocomotionUpdate update)
    {
        int index = checked((int)actor.Index);
        switch (update.Kind)
        {
            case GameLocomotionUpdateKind.Unchanged:
                return GameWorldActionStatus.NotRequested;
            case GameLocomotionUpdateKind.Stop:
                ClearLocomotion(index);
                return GameWorldActionStatus.Accepted;
            case GameLocomotionUpdateKind.Direct:
                SetDirect(actor, update.DirectX, update.DirectZ);
                return GameWorldActionStatus.Accepted;
            case GameLocomotionUpdateKind.Destination:
                if (!WorldPosition.TryFromGlobalMillimeters(
                        update.DestinationXMillimeters,
                        current.YMillimeters,
                        update.DestinationZMillimeters,
                        out WorldPosition candidate) ||
                    worldState.PreparedWorld is null)
                {
                    return GameWorldActionStatus.OutsidePreparedWorld;
                }
                WorldTerrainColumnQueryStatus terrain =
                    worldState.PreparedWorld.TryFindNearestSupport(
                        candidate, current.YMillimeters, out WorldTerrainColumnSurface support);
                if (terrain == WorldTerrainColumnQueryStatus.Unavailable)
                    return GameWorldActionStatus.OutsidePreparedWorld;
                if (terrain != WorldTerrainColumnQueryStatus.Complete || !support.IsValid)
                    return GameWorldActionStatus.Unsupported;
                SetDestination(
                    actor,
                    current,
                    update.DestinationXMillimeters,
                    update.DestinationZMillimeters);
                return GameWorldActionStatus.Accepted;
            default:
                throw new InvalidOperationException("A validated locomotion update has an unknown kind.");
        }
    }

    /// <summary>
    /// Integrates every retained horizontal intent and airborne actor once for the authoritative
    /// fixed step. This runs after the ECS graph/barrier and before Spatial/publication maintenance.
    /// </summary>
    internal void CommitLocomotionAfterEcsStep(
        EcsWorld world,
        GameWorldState worldState,
        in SimulationStepDelta delta)
    {
        EnsureWorld(world, worldState);
        if (!delta.IsValid)
            throw new ArgumentException("A valid authoritative fixed-step delta is required.", nameof(delta));

        int active = 0;
        while (active < _activeActorCount)
        {
            Entity actor = _activeActors[active];
            int index = checked((int)actor.Index);
            if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
                _verticalGenerations[index] != actor.Generation ||
                !worldState.TryReadPosition(world, actor, out WorldPosition current))
            {
                ClearActorState(index);
                continue;
            }

            if (_locomotionKinds[index] != RetainedLocomotionKind.None &&
                (_controlGenerations[index] != actor.Generation || _controlEpochs[index] == 0))
                ClearLocomotion(index);

            if (_verticalKinds[index] == GameVerticalMotionKind.Airborne)
                IntegrateAirborne(world, worldState, actor, index, current, delta.Quanta);
            else
            {
                switch (_locomotionKinds[index])
                {
                    case RetainedLocomotionKind.Direct:
                        IntegrateGroundedDirect(world, worldState, actor, index, current, delta.Quanta);
                        break;
                    case RetainedLocomotionKind.Destination:
                        IntegrateGroundedDestination(
                            world, worldState, actor, index, current, delta.Quanta);
                        break;
                    case RetainedLocomotionKind.None:
                        RemoveActive(index);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "An active actor has an unknown retained locomotion kind.");
                }
            }

            if (active < _activeActorCount && _activeActors[active] == actor)
                active++;
        }
    }

    private void IntegrateGroundedDirect(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        int index,
        in WorldPosition current,
        long stepQuanta)
    {
        int deltaX = IntegrateAxis(_directX[index], stepQuanta, ref _movementRemainderX[index]);
        int deltaZ = IntegrateAxis(_directZ[index], stepQuanta, ref _movementRemainderZ[index]);
        if (deltaX == 0 && deltaZ == 0)
            return;

        long nextX = checked(current.GlobalXMillimeters + deltaX);
        long nextZ = checked(current.GlobalZMillimeters + deltaZ);
        SimpleMotionResult physical = ResolveMotion(world, worldState, actor, current, nextX, nextZ);
        if (physical.WasResolved)
        {
            SetAuthoritativePosition(world, worldState, actor, physical.Position);
            return;
        }
        if (physical.Status == SimpleMotionStatus.Unsupported)
        {
            BeginAirborne(actor, index, initialVelocityMillimetersPerSecond: 0);
            IntegrateAirborneToward(
                world, worldState, actor, index, current, nextX, nextZ, stepQuanta,
                destinationProgress: null, destinationReached: false);
        }
        // Direct remains retained at prepared-world limits, query pressure, or geometry.
    }

    private void IntegrateGroundedDestination(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        int index,
        in WorldPosition current,
        long stepQuanta)
    {
        int distance = IntegrateDistance(stepQuanta, ref _movementRemainderX[index]);
        if (distance == 0)
            return;

        // Derive each published point from the original route and cumulative
        // authoritative progress. Never compound per-tick X/Z rounding.
        long routeLength = _destinationRouteLength[index];
        long progress = checked(_destinationProgress[index] + distance);
        bool reached = progress >= routeLength;
        if (reached)
        {
            progress = routeLength;
        }

        long routeX = checked(_destinationX[index] - _destinationStartX[index]);
        long routeZ = checked(_destinationZ[index] - _destinationStartZ[index]);
        long nextX = checked(_destinationStartX[index] +
            (long)((Int128)routeX * progress / routeLength));
        long nextZ = checked(_destinationStartZ[index] +
            (long)((Int128)routeZ * progress / routeLength));

        SimpleMotionResult physical = ResolveMotion(world, worldState, actor, current, nextX, nextZ);
        if (physical.WasResolved)
        {
            SetAuthoritativePosition(world, worldState, actor, physical.Position);
            _destinationProgress[index] = progress;
            if (reached)
                ClearLocomotion(index);
            return;
        }
        if (physical.Status == SimpleMotionStatus.Unsupported)
        {
            BeginAirborne(actor, index, initialVelocityMillimetersPerSecond: 0);
            IntegrateAirborneToward(
                world, worldState, actor, index, current, nextX, nextZ, stepQuanta,
                progress, reached);
            return;
        }
        // Destination remains one straight physical route. A blocker/unavailable route retires it.
        ClearLocomotion(index);
    }

    private void IntegrateAirborne(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        int index,
        in WorldPosition current,
        long stepQuanta)
    {
        long nextX = current.GlobalXMillimeters;
        long nextZ = current.GlobalZMillimeters;
        long? progress = null;
        bool reached = false;
        // Direct-profile horizontal momentum is captured once at takeoff/walk-off. Live input may
        // still update the retained locomotion that will resume after landing, but it cannot steer
        // the current flight or cancel its already-committed horizontal velocity.
        if (_airborneDirectX[index] != 0 || _airborneDirectZ[index] != 0)
        {
            nextX = checked(nextX + IntegrateAxis(
                _airborneDirectX[index],
                stepQuanta,
                ref _airborneMovementRemainderX[index]));
            nextZ = checked(nextZ + IntegrateAxis(
                _airborneDirectZ[index],
                stepQuanta,
                ref _airborneMovementRemainderZ[index]));
        }
        else switch (_locomotionKinds[index])
        {
            case RetainedLocomotionKind.Direct:
                // A Direct command first received after takeoff is post-landing intent, not
                // mid-air acceleration.
                break;
            case RetainedLocomotionKind.Destination:
                int distance = IntegrateDistance(stepQuanta, ref _movementRemainderX[index]);
                long candidateProgress = checked(_destinationProgress[index] + distance);
                long routeLength = _destinationRouteLength[index];
                reached = candidateProgress >= routeLength;
                progress = reached ? routeLength : candidateProgress;
                long routeX = checked(_destinationX[index] - _destinationStartX[index]);
                long routeZ = checked(_destinationZ[index] - _destinationStartZ[index]);
                nextX = checked(_destinationStartX[index] +
                    (long)((Int128)routeX * progress.Value / routeLength));
                nextZ = checked(_destinationStartZ[index] +
                    (long)((Int128)routeZ * progress.Value / routeLength));
                break;
        }
        IntegrateAirborneToward(
            world, worldState, actor, index, current, nextX, nextZ, stepQuanta,
            progress, reached);
    }

    private void IntegrateAirborneToward(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        int index,
        in WorldPosition current,
        long nextX,
        long nextZ,
        long stepQuanta,
        long? destinationProgress,
        bool destinationReached)
    {
        int deltaY = IntegrateVertical(index, stepQuanta);
        long targetY = checked((long)current.YMillimeters + deltaY);
        if (targetY < int.MinValue || targetY > int.MaxValue ||
            targetY <= _verticalOptions.RecoveryMinimumYMillimeters)
        {
            _ = TryRecoverActor(world, worldState, actor, index);
            return;
        }

        bool complete = _collisionProjection.TryCapture(
            world, worldState, actor, out ReadOnlySpan<WorldStaticBounds> dynamicBounds);
        var request = new SimpleAirborneMotionRequest(
            current, nextX, checked((int)targetY), nextZ, s_actorBody);
        SimpleAirborneMotionResult physical = _physics.ResolveAirborneMotion(
            request, dynamicBounds, complete);
        // A captured takeoff trajectory may point beyond the prepared structural boundary while
        // gravity remains valid at the actor's current X/Z. Preserve the vertical component just
        // as wall contact does. Otherwise an outside horizontal probe can hold the position fixed
        // while gravity accumulates indefinitely and eventually overflows its fixed-point state.
        if (!physical.WasResolved &&
            physical.Status == SimpleAirborneMotionStatus.OutsidePreparedWorld &&
            (nextX != current.GlobalXMillimeters || nextZ != current.GlobalZMillimeters))
        {
            physical = _physics.ResolveAirborneMotion(
                request with
                {
                    TargetXMillimeters = current.GlobalXMillimeters,
                    TargetZMillimeters = current.GlobalZMillimeters,
                },
                dynamicBounds,
                complete);
        }
        if (!physical.WasResolved)
        {
            if (physical.Status == SimpleAirborneMotionStatus.OutsidePreparedWorld)
            {
                _ = TryRecoverActor(world, worldState, actor, index);
                return;
            }
            if (_locomotionKinds[index] == RetainedLocomotionKind.Destination)
                ClearLocomotion(index);
            if (physical.Status == SimpleAirborneMotionStatus.Blocked &&
                _verticalVelocityNumerators[index] > 0)
            {
                _verticalVelocityNumerators[index] = 0;
                _verticalDisplacementRemainders[index] = 0;
            }
            return;
        }

        SetAuthoritativePosition(world, worldState, actor, physical.Position);
        if (destinationProgress.HasValue)
        {
            if (physical.Position.GlobalXMillimeters == nextX &&
                physical.Position.GlobalZMillimeters == nextZ)
            {
                _destinationProgress[index] = destinationProgress.Value;
                if (destinationReached)
                    ClearLocomotion(index);
            }
            else
            {
                ClearLocomotion(index);
            }
        }

        if (physical.Status == SimpleAirborneMotionStatus.Landed)
        {
            SetGrounded(actor, index);
            _landings++;
        }
        else if (physical.Status == SimpleAirborneMotionStatus.CeilingBlocked)
        {
            _verticalVelocityNumerators[index] = 0;
            _verticalDisplacementRemainders[index] = 0;
        }
    }

    private SimpleMotionResult ResolveMotion(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in WorldPosition current,
        long nextX,
        long nextZ)
    {
        bool complete = _collisionProjection.TryCapture(
            world, worldState, actor, out ReadOnlySpan<WorldStaticBounds> dynamicBounds);
        var request = new SimpleMotionRequest(current, nextX, nextZ, s_actorBody);
        return _physics.ResolveMotion(request, dynamicBounds, complete);
    }

    private static void SetAuthoritativePosition(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in WorldPosition position)
    {
        if (!worldState.TrySetPosition(world, actor, position))
        {
            throw new InvalidOperationException(
                "A validated controlled actor lost its authoritative Position during locomotion.");
        }
    }

    internal void RegisterPlayableDynamicBlocker(
        EcsWorld world,
        GameWorldState worldState,
        Entity blocker)
    {
        _collisionProjection.Register(world, worldState, blocker);
    }

    private GameWorldActionStatus ToggleCabinDoorCore(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        Entity door,
        out bool isOpen)
    {
        isOpen = false;
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition actorPosition))
            return GameWorldActionStatus.MissingActor;
        if (!world.IsAlive(door) || !world.TryRead(door, _door, out GameCabinDoorState state) ||
            !worldState.TryReadPosition(world, door, out WorldPosition doorPosition) ||
            !worldState.TryReadPublic(world, door, out WorldPublicDescriptor descriptor))
            return GameWorldActionStatus.MissingTarget;
        if (state.IsOpen is not (0 or 1) ||
            descriptor.PrefabId != GameWorldPublicPrefabIds.CabinDoor ||
            descriptor.Orientation != DoorOrientation(state.IsOpen != 0))
            return GameWorldActionStatus.InvariantViolation;
        if (!WithinInteractionRange(actorPosition, doorPosition))
            return GameWorldActionStatus.OutOfRange;

        isOpen = state.IsOpen == 0;
        var nextState = new GameCabinDoorState { IsOpen = isOpen ? 1 : 0 };
        var nextDescriptor = descriptor with
        {
            Orientation = isOpen ? WorldYaw.QuarterTurn : WorldYaw.Zero,
        };
        if (!world.TryWrite(door, _door, nextState))
            return GameWorldActionStatus.InvariantViolation;
        if (!worldState.TrySetPublic(world, door, nextDescriptor))
            throw new InvalidOperationException("A validated cabin door lost its public component.");
        return GameWorldActionStatus.Accepted;
    }

    private GameWorldActionStatus InteractWithWorldTarget(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        Entity target,
        out bool doorIsOpen)
    {
        doorIsOpen = false;
        if (!world.IsAlive(target) ||
            !worldState.TryReadPublic(world, target, out WorldPublicDescriptor descriptor))
            return GameWorldActionStatus.MissingTarget;
        return descriptor.PrefabId switch
        {
            GameWorldPublicPrefabIds.CabinDoor =>
                ToggleCabinDoorCore(world, worldState, actor, target, out doorIsOpen),
            GameWorldPublicPrefabIds.SkyTelepad =>
                ActivateSkyTelepad(world, worldState, actor, target),
            GameWorldPublicPrefabIds.FarGate =>
                ActivateFarGate(world, worldState, actor, target),
            GameWorldPublicPrefabIds.InformationalSign =>
                ValidateInformationalSign(world, worldState, actor, target),
            _ => GameWorldActionStatus.MissingTarget,
        };
    }

    private GameWorldActionStatus ValidateInformationalSign(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        Entity sign)
    {
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition actorPosition))
            return GameWorldActionStatus.MissingActor;
        if (!world.IsAlive(sign) ||
            !worldState.TryReadPosition(world, sign, out WorldPosition signPosition) ||
            !worldState.TryReadPublic(world, sign, out WorldPublicDescriptor descriptor) ||
            descriptor.PrefabId != GameWorldPublicPrefabIds.InformationalSign)
            return GameWorldActionStatus.MissingTarget;
        return WithinInteractionRange(actorPosition, signPosition)
            ? GameWorldActionStatus.Accepted
            : GameWorldActionStatus.OutOfRange;
    }

    private GameWorldActionStatus ActivateSkyTelepad(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        Entity telepad)
    {
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition actorPosition))
            return GameWorldActionStatus.MissingActor;
        if (!world.IsAlive(telepad) ||
            !worldState.TryReadPosition(world, telepad, out WorldPosition telepadPosition) ||
            !worldState.TryReadPublic(world, telepad, out WorldPublicDescriptor descriptor) ||
            descriptor.PrefabId != GameWorldPublicPrefabIds.SkyTelepad)
            return GameWorldActionStatus.MissingTarget;
        if (!WithinInteractionRange(actorPosition, telepadPosition))
            return GameWorldActionStatus.OutOfRange;

        // This destination is concrete authored Game content. The interaction caller nominates
        // only the public telepad entity and never supplies relocation coordinates.
        return RelocateActor(
            world,
            worldState,
            actor,
            GamePlayableWorld.SkyTelepadDestination,
            out _);
    }

    private GameWorldActionStatus ActivateFarGate(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        Entity gate)
    {
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition actorPosition))
            return GameWorldActionStatus.MissingActor;
        if (!world.IsAlive(gate) ||
            !worldState.TryReadPosition(world, gate, out WorldPosition gatePosition) ||
            !worldState.TryReadPublic(world, gate, out WorldPublicDescriptor descriptor) ||
            descriptor.PrefabId != GameWorldPublicPrefabIds.FarGate)
            return GameWorldActionStatus.MissingTarget;
        if (!WithinInteractionRange(actorPosition, gatePosition))
            return GameWorldActionStatus.OutOfRange;

        WorldPosition destination;
        if (gatePosition == GamePlayableWorld.CentralFarGatePosition)
            destination = GamePlayableWorld.FarGateDestination;
        else if (gatePosition == GamePlayableWorld.FarReturnGatePosition)
            destination = GamePlayableWorld.CentralGateDestination;
        else
            return GameWorldActionStatus.InvariantViolation;

        return RelocateActor(world, worldState, actor, destination, out _);
    }

    public bool TryReadCabinDoor(EcsWorld world, Entity door, out bool isOpen)
    {
        EnsureWorld(world);
        if (world.TryRead(door, _door, out GameCabinDoorState state) && state.IsOpen is 0 or 1)
        {
            isOpen = state.IsOpen != 0;
            return true;
        }
        isOpen = false;
        return false;
    }

    /// <summary>
    /// Applies an engine-neutral yaw toward one nominated world X/Z point. This is a concrete
    /// public-facing update for the playable character; locomotion direction remains independent.
    /// </summary>
    public GameWorldActionStatus FaceActorToward(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        long targetXMillimeters,
        long targetZMillimeters,
        out WorldYaw orientation)
    {
        EnsureWorld(world, worldState);
        orientation = default;
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition position) ||
            !worldState.TryReadPublic(world, actor, out WorldPublicDescriptor descriptor))
            return GameWorldActionStatus.MissingActor;

        long deltaX = checked(targetXMillimeters - position.GlobalXMillimeters);
        long deltaZ = checked(targetZMillimeters - position.GlobalZMillimeters);
        if (deltaX == 0 && deltaZ == 0)
        {
            orientation = descriptor.Orientation;
            return GameWorldActionStatus.Accepted;
        }

        double turn = Math.Atan2(deltaX, deltaZ) / Math.Tau;
        if (turn < 0d) turn += 1d;
        long rounded = (long)Math.Round(
            turn * WorldYaw.UnitsPerTurn,
            MidpointRounding.AwayFromZero);
        orientation = new WorldYaw(unchecked((ushort)rounded));
        if (descriptor.Orientation == orientation)
            return GameWorldActionStatus.Accepted;
        return worldState.TrySetPublic(
                world,
                actor,
                descriptor with { Orientation = orientation })
            ? GameWorldActionStatus.Accepted
            : GameWorldActionStatus.InvariantViolation;
    }

    /// <summary>
    /// Performs one explicit authoritative positional discontinuity. A successful relocation
    /// resolves physical endpoint occupancy and terminates operational Direct/Destination intent,
    /// so the next locomotion step cannot pull the actor back onto an obsolete route. Rejection
    /// leaves both canonical position and retained locomotion unchanged.
    /// </summary>
    public GameWorldActionStatus RelocateActor(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in WorldPosition requested,
        out WorldPosition relocated)
    {
        EnsureWorld(world, worldState);
        relocated = default;
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out _))
            return GameWorldActionStatus.MissingActor;

        bool projectionComplete = _collisionProjection.TryCapture(
            world, worldState, actor, out ReadOnlySpan<WorldStaticBounds> dynamicBounds);
        SimpleOccupancyResult occupancy = _physics.ProbeOccupancy(
            new SimpleOccupancyRequest(requested, s_actorBody),
            dynamicBounds,
            projectionComplete);
        if (occupancy.Status == SimpleOccupancyStatus.OutsidePreparedWorld)
            return GameWorldActionStatus.OutsidePreparedWorld;
        if (occupancy.Status == SimpleOccupancyStatus.Unsupported)
            return GameWorldActionStatus.Unsupported;
        if (!occupancy.CanOccupy)
            return GameWorldActionStatus.Blocked;

        relocated = occupancy.Position;
        if (!worldState.TrySetPosition(world, actor, relocated))
            return GameWorldActionStatus.InvariantViolation;

        int index = checked((int)actor.Index);
        ClearLocomotion(index);
        SetGrounded(actor, index);
        return GameWorldActionStatus.Accepted;
    }

    public GameWorldActionStatus MoveItemToNearbyContainer(
        EcsWorld world,
        GameWorldState worldState,
        InventoryGenerationState inventory,
        Entity actor,
        Entity item,
        Entity source,
        Entity destination,
        out InventoryMutationResult mutation)
    {
        EnsureWorld(world, worldState);
        ArgumentNullException.ThrowIfNull(inventory);
        mutation = default;
        if (inventory.WorldId != _worldId)
            throw new ArgumentException("Inventory belongs to another ECS world.", nameof(inventory));
        if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
            !worldState.TryReadPosition(world, actor, out WorldPosition actorPosition))
            return GameWorldActionStatus.MissingActor;
        if (!world.IsAlive(destination) ||
            !inventory.TryGetContainerDefinition(world, source, out _) ||
            !inventory.TryGetContainerDefinition(world, destination, out _))
            return GameWorldActionStatus.MissingTarget;
        if (!IsAuthorizedContainer(world, worldState, inventory, actor, actorPosition, source) ||
            !IsAuthorizedContainer(world, worldState, inventory, actor, actorPosition, destination))
            return GameWorldActionStatus.UnauthorizedContainer;
        mutation = inventory.Move(world, item, source, destination);
        return mutation.Accepted
            ? GameWorldActionStatus.Accepted
            : GameWorldActionStatus.InvariantViolation;
    }

    internal void InitializePhysicalState(EcsWorld world, GameWorldState worldState)
    {
        EnsureWorld(world, worldState);
        _collisionProjection.RebuildAndValidate(world, worldState);
        var visitor = new CharacterBaselineVisitor(_characterScratch);
        _ = world.ExecuteJob(_characterBaseline, ref visitor);
        Array.Sort(_characterScratch, 0, visitor.Count);
        for (int item = 0; item < visitor.Count; item++)
        {
            if (world.TryGetEntity(_characterScratch[item], out Entity actor))
                ReconcileVerticalState(world, actor);
        }
    }

    private static int IntegrateAxis(short value, long stepQuanta, ref long remainder)
    {
        long denominator = checked(
            (long)GamePlayerCommand.MaximumAxisMagnitude * WorldClockOptions.WorldTimeQuantaPerSecond);
        Int128 numerator = (Int128)value * FullInputSpeedMillimetersPerSecond * stepQuanta + remainder;
        Int128 delta = numerator / denominator;
        remainder = checked((long)(numerator % denominator));
        return checked((int)delta);
    }

    private static int IntegrateDistance(long stepQuanta, ref long remainder)
    {
        long denominator = WorldClockOptions.WorldTimeQuantaPerSecond;
        Int128 numerator =
            (Int128)FullInputSpeedMillimetersPerSecond * stepQuanta + remainder;
        Int128 distance = numerator / denominator;
        remainder = checked((long)(numerator % denominator));
        return checked((int)distance);
    }

    private static long CeilingSquareRoot(Int128 squared, long dx, long dz)
    {
        long lower = checked((long)Int128.Max(
            Int128.Abs((Int128)dx), Int128.Abs((Int128)dz)));
        long upper = checked(lower + (lower / 2) + 1);
        while (lower < upper)
        {
            long midpoint = lower + ((upper - lower) / 2);
            if ((Int128)midpoint * midpoint >= squared)
                upper = midpoint;
            else
                lower = midpoint + 1;
        }
        return lower;
    }

    private static bool WithinInteractionRange(in WorldPosition first, in WorldPosition second)
    {
        Int128 dx = (Int128)first.GlobalXMillimeters - second.GlobalXMillimeters;
        Int128 dy = (Int128)first.YMillimeters - second.YMillimeters;
        Int128 dz = (Int128)first.GlobalZMillimeters - second.GlobalZMillimeters;
        Int128 maximum = InteractionRangeMillimeters;
        return dx * dx + dy * dy + dz * dz <= maximum * maximum;
    }

    private bool SequenceIsFresh(Entity actor, ulong sequence)
    {
        int index = checked((int)actor.Index);
        return sequence > _lastSequences[index];
    }

    private void CommitSequence(Entity actor, ulong sequence)
    {
        int index = checked((int)actor.Index);
        _lastSequences[index] = sequence;
    }

    private void SetDirect(Entity actor, short x, short z)
    {
        int index = checked((int)actor.Index);
        if (_locomotionKinds[index] != RetainedLocomotionKind.Direct)
        {
            ClearLocomotion(index);
            _locomotionKinds[index] = RetainedLocomotionKind.Direct;
            _activeLocomotionCount++;
        }
        _directX[index] = x;
        _directZ[index] = z;
        EnsureActive(actor, index);
    }

    private void SetDestination(
        Entity actor,
        in WorldPosition start,
        long globalX,
        long globalZ)
    {
        int index = checked((int)actor.Index);
        if (_locomotionKinds[index] == RetainedLocomotionKind.Destination &&
            _destinationX[index] == globalX && _destinationZ[index] == globalZ)
            return;
        ClearLocomotion(index);

        long routeX = checked(globalX - start.GlobalXMillimeters);
        long routeZ = checked(globalZ - start.GlobalZMillimeters);
        if (routeX == 0 && routeZ == 0)
            return;

        _locomotionKinds[index] = RetainedLocomotionKind.Destination;
        _activeLocomotionCount++;
        _destinationX[index] = globalX;
        _destinationZ[index] = globalZ;
        _destinationStartX[index] = start.GlobalXMillimeters;
        _destinationStartZ[index] = start.GlobalZMillimeters;
        // Ceiling keeps the integer route no shorter than the exact Euclidean
        // route while final-progress clamping still publishes the exact target.
        _destinationRouteLength[index] = CeilingSquareRoot(
            (Int128)routeX * routeX + (Int128)routeZ * routeZ,
            routeX,
            routeZ);
        EnsureActive(actor, index);
    }

    private void EnsureActive(Entity actor, int index)
    {
        if (_activeActorPositions[index] != 0)
            return;
        if (_activeActorCount >= _activeActors.Length)
            throw new InvalidOperationException("The bounded physical actor set exhausted unexpectedly.");
        _activeActors[_activeActorCount] = actor;
        _activeActorPositions[index] = checked(_activeActorCount + 1);
        _activeActorCount++;
    }

    private void ClearLocomotion(int index)
    {
        if (_locomotionKinds[index] != RetainedLocomotionKind.None)
            _activeLocomotionCount--;

        _locomotionKinds[index] = RetainedLocomotionKind.None;
        _directX[index] = 0;
        _directZ[index] = 0;
        _destinationX[index] = 0;
        _destinationZ[index] = 0;
        _destinationStartX[index] = 0;
        _destinationStartZ[index] = 0;
        _destinationRouteLength[index] = 0;
        _destinationProgress[index] = 0;
        _movementRemainderX[index] = 0;
        _movementRemainderZ[index] = 0;
        if (_verticalKinds[index] != GameVerticalMotionKind.Airborne)
            RemoveActive(index);
    }

    private void BeginAirborne(Entity actor, int index, int initialVelocityMillimetersPerSecond)
    {
        if (_verticalKinds[index] != GameVerticalMotionKind.Airborne)
        {
            _activeAirborneCount++;
            bool direct = _locomotionKinds[index] == RetainedLocomotionKind.Direct;
            _airborneDirectX[index] = direct ? _directX[index] : (short)0;
            _airborneDirectZ[index] = direct ? _directZ[index] : (short)0;
            _airborneMovementRemainderX[index] = direct ? _movementRemainderX[index] : 0;
            _airborneMovementRemainderZ[index] = direct ? _movementRemainderZ[index] : 0;
        }
        _verticalGenerations[index] = actor.Generation;
        _verticalKinds[index] = GameVerticalMotionKind.Airborne;
        _verticalVelocityNumerators[index] = checked(
            (long)initialVelocityMillimetersPerSecond *
            WorldClockOptions.WorldTimeQuantaPerSecond);
        _verticalDisplacementRemainders[index] = 0;
        EnsureActive(actor, index);
    }

    private void SetGrounded(Entity actor, int index)
    {
        if (_verticalKinds[index] == GameVerticalMotionKind.Airborne)
            _activeAirborneCount--;
        _verticalGenerations[index] = actor.Generation;
        _verticalKinds[index] = GameVerticalMotionKind.Grounded;
        _verticalVelocityNumerators[index] = 0;
        _verticalDisplacementRemainders[index] = 0;
        ClearAirborneHorizontal(index);
        if (_locomotionKinds[index] == RetainedLocomotionKind.None)
            RemoveActive(index);
    }

    private void ClearActorState(int index)
    {
        if (_locomotionKinds[index] != RetainedLocomotionKind.None)
            _activeLocomotionCount--;
        if (_verticalKinds[index] == GameVerticalMotionKind.Airborne)
            _activeAirborneCount--;
        _locomotionKinds[index] = RetainedLocomotionKind.None;
        _verticalKinds[index] = GameVerticalMotionKind.None;
        _verticalGenerations[index] = 0;
        _verticalVelocityNumerators[index] = 0;
        _verticalDisplacementRemainders[index] = 0;
        ClearAirborneHorizontal(index);
        _directX[index] = 0;
        _directZ[index] = 0;
        _destinationX[index] = 0;
        _destinationZ[index] = 0;
        _destinationStartX[index] = 0;
        _destinationStartZ[index] = 0;
        _destinationRouteLength[index] = 0;
        _destinationProgress[index] = 0;
        _movementRemainderX[index] = 0;
        _movementRemainderZ[index] = 0;
        RemoveActive(index);
    }

    private void ClearAirborneHorizontal(int index)
    {
        _airborneDirectX[index] = 0;
        _airborneDirectZ[index] = 0;
        _airborneMovementRemainderX[index] = 0;
        _airborneMovementRemainderZ[index] = 0;
    }

    private void RemoveActive(int index)
    {
        int encoded = _activeActorPositions[index];
        if (encoded == 0)
            return;
        int slot = encoded - 1;
        int last = --_activeActorCount;
        if (slot != last)
        {
            Entity donor = _activeActors[last];
            _activeActors[slot] = donor;
            _activeActorPositions[checked((int)donor.Index)] = checked(slot + 1);
        }
        _activeActors[last] = Entity.Null;
        _activeActorPositions[index] = 0;
    }

    private int IntegrateVertical(int index, long stepQuanta)
    {
        long oldVelocity = _verticalVelocityNumerators[index];
        long nextVelocity = checked(oldVelocity +
            (long)_verticalOptions.GravityMillimetersPerSecondSquared * stepQuanta);
        Int128 denominator = 2 * (Int128)WorldClockOptions.WorldTimeQuantaPerSecond *
            WorldClockOptions.WorldTimeQuantaPerSecond;
        Int128 numerator = (Int128)(oldVelocity + nextVelocity) * stepQuanta +
            _verticalDisplacementRemainders[index];
        int displacement = checked((int)(numerator / denominator));
        _verticalDisplacementRemainders[index] = numerator % denominator;
        _verticalVelocityNumerators[index] = nextVelocity;
        return displacement;
    }

    private void ReconcileVerticalState(EcsWorld world, Entity actor)
    {
        int index = checked((int)actor.Index);
        if (_verticalKinds[index] == GameVerticalMotionKind.Airborne)
            _activeAirborneCount--;
        // A recycled ECS slot can still be represented by an active entry carrying the retired
        // generation. Remove that identity before unsupported reconstruction can append/reuse it.
        RemoveActive(index);
        _verticalKinds[index] = GameVerticalMotionKind.None;
        _verticalGenerations[index] = actor.Generation;
        _verticalVelocityNumerators[index] = 0;
        _verticalDisplacementRemainders[index] = 0;
        if (!_worldState.TryReadPosition(world, actor, out WorldPosition current))
            return;
        bool complete = _collisionProjection.TryCapture(
            world, _worldState, actor, out ReadOnlySpan<WorldStaticBounds> dynamicBounds);
        SimpleMotionResult grounded = _physics.ResolveMotion(
            new SimpleMotionRequest(
                current, current.GlobalXMillimeters, current.GlobalZMillimeters, s_actorBody),
            dynamicBounds,
            complete);
        if (grounded.WasResolved)
        {
            SetGrounded(actor, index);
        }
        else if (grounded.Status == SimpleMotionStatus.Unsupported)
        {
            BeginAirborne(actor, index, initialVelocityMillimetersPerSecond: 0);
        }
        else
        {
            if (!TryRecoverActor(world, _worldState, actor, index) &&
                _locomotionKinds[index] == RetainedLocomotionKind.None)
                RemoveActive(index);
        }
    }

    private bool TryRecoverActor(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        int index)
    {
        bool complete = _collisionProjection.TryCapture(
            world, worldState, actor, out ReadOnlySpan<WorldStaticBounds> dynamicBounds);
        SimpleOccupancyResult recovery = _physics.ProbeOccupancy(
            new SimpleOccupancyRequest(_verticalOptions.RecoveryPosition, s_actorBody),
            dynamicBounds,
            complete);
        if (!recovery.CanOccupy ||
            !worldState.TrySetPosition(world, actor, recovery.Position))
            return false;
        ClearLocomotion(index);
        SetGrounded(actor, index);
        _recoveries++;
        return true;
    }

    private static WorldYaw DoorOrientation(bool isOpen) =>
        isOpen ? WorldYaw.QuarterTurn : WorldYaw.Zero;

    private static bool IsAuthorizedContainer(
        EcsWorld world,
        GameWorldState worldState,
        InventoryGenerationState inventory,
        Entity actor,
        in WorldPosition actorPosition,
        Entity container)
    {
        if (inventory.TryGetContainerOwner(world, container, out Entity owner))
            return owner == actor;
        return worldState.TryReadPosition(world, container, out WorldPosition position) &&
            WithinInteractionRange(actorPosition, position);
    }

    private void EnsureWorld(EcsWorld world, GameWorldState worldState)
    {
        EnsureWorld(world);
        ArgumentNullException.ThrowIfNull(worldState);
        if (worldState.WorldId != _worldId)
            throw new ArgumentException("World actions and current-world state belong to different generations.",
                nameof(worldState));
    }

    private void EnsureWorld(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Id != _worldId)
            throw new ArgumentException("World actions belong to another ECS world.", nameof(world));
    }

    private struct CharacterBaselineVisitor : IReadChunkJob<GameCharacter>
    {
        private readonly uint[] _indices;

        internal CharacterBaselineVisitor(uint[] indices)
        {
            _indices = indices;
            Count = 0;
        }

        internal int Count;

        public void Execute(in ReadComponentChunk<GameCharacter> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                if (Count >= _indices.Length)
                    throw new InvalidOperationException(
                        "The bounded character baseline exceeded entity capacity.");
                _indices[Count++] = chunk.EntityIndices[row];
            }
        }
    }
}

/// <summary>Deterministic authored woods/cabin content and setup for T0.10 acceptance.</summary>
public static class GamePlayableWorld
{
    public const int MinimumChunk = -2;
    public const int MaximumChunk = 2;
    public const int ChunkCount = 25;
    public const int ConceptMinimumChunk = -4;
    public const int ConceptMaximumChunk = 4;
    public const int FarIslandCenterChunkX = 128;
    public const int FarIslandMinimumChunkX = FarIslandCenterChunkX - 4;
    public const int FarIslandMaximumChunkX = FarIslandCenterChunkX + 4;
    public const int ConceptClusterChunkCount = 81;
    public const int ConceptChunkCount = ConceptClusterChunkCount * 2;
    public const int FlatTestMinimumChunkX = -2;
    public const int FlatTestMaximumChunkX = 0;
    public const int FlatTestMinimumChunkZ = -1;
    public const int FlatTestMaximumChunkZ = 1;
    public const int FlatTestHeightMillimeters = 1_000;

    public static WorldSurfaceId MountainSurface => new(0xD0_001);
    public static WorldSurfaceId CaveLedgeSurface => new(0xD0_002);
    public static WorldSurfaceId SkyIslandSurface => new(0xD0_003);

    public static WorldPosition PlayerStart => FromGlobal(15_500, 7_000);
    public static WorldPosition ClearCrossChunkPosition => FromGlobal(18_000, 7_000);
    public static WorldPosition DoorPosition => FromGlobal(16_000, 9_000);
    public static WorldPosition ChestPosition => FromGlobal(18_000, 12_000);
    public static WorldPosition AnimalPosition => FromGlobal(8_000, 4_000);
    public static WorldPosition SkyTelepadPosition => FromGlobal(21_000, 6_500);
    public static WorldPosition SkyTelepadDestination => AtHeight(28_000, 3_500, 9_000);
    public static WorldPosition SkyJumpStart => AtHeight(22_000, 1_000, 9_000);
    public static WorldPosition CaveEntranceApproach => FromGlobal(-6_000, 2_500);
    public static WorldPosition CaveFloorPoint => FromGlobal(-6_000, 7_000);
    public static WorldPosition CaveLedgePoint => AtHeight(-6_000, 4_000, 11_000);
    public static long FarIslandCenterXMillimeters =>
        checked((long)FarIslandCenterChunkX * GameWorldSpatialProfile.WorldChunkEdgeUnits + 8_000);
    public static WorldPosition CentralFarGatePosition => FromGlobal(0, 20_000);
    public static WorldPosition CentralGateDestination => FromGlobal(0, 16_000);
    public static WorldPosition FarReturnGatePosition => FromGlobal(FarIslandCenterXMillimeters, 20_000);
    public static WorldPosition FarGateDestination => FromGlobal(FarIslandCenterXMillimeters, 16_000);
    public static WorldPosition WelcomeSignPosition => FromGlobal(14_000, 6_000);
    public static WorldPosition IronSwordSpawnerPosition => FromGlobal(14_000, 7_000);
    public static WorldPosition FallbackLabSignPosition => FromGlobal(7_500, -1_000);
    public static WorldPosition FallbackLabApproachPosition => FromGlobal(7_500, 1_000);
    public static WorldPosition FallbackLabCorrectPosition => FromGlobal(3_000, -4_000);
    public static WorldPosition FallbackLabModelMissingPosition => FromGlobal(6_000, -4_000);
    public static WorldPosition FallbackLabTextureMissingPosition => FromGlobal(9_000, -4_000);
    public static WorldPosition FallbackLabBothMissingPosition => FromGlobal(12_000, -4_000);

    public static WorldPack CreateWorldPack() => CreateWorldPackCore(conceptDemo: false);

    /// <summary>
    /// Builds the first local Concept Demo world while preserving the historical Cabin fixture.
    /// Its BaseTerrain contains separate finite Central/Far islands; sparse immutable shells form
    /// the central mountain, cave ledge, and detached Sky Island.
    /// </summary>
    public static WorldPack CreateConceptDemoWorldPack() => CreateWorldPackCore(conceptDemo: true);

    private static WorldPack CreateWorldPackCore(bool conceptDemo)
    {
        int chunkCount = conceptDemo ? ConceptChunkCount : ChunkCount;
        var limits = new WorldPackLimits(
            (conceptDemo ? 8 : 2) * 1024 * 1024,
            chunkCount,
            8,
            maximumFrozenStatics: conceptDemo ? 256 : 128,
            maximumMultiInstances: 8,
            maximumMultiFragments: 32);
        var builder = new WorldPackBuilder(
            GameWorldState.DefaultSpace,
            GameContent.WorldFingerprint,
            limits);
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        var heights = new int[TerrainChunkFormat.HeightVertexCount];
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        var presence = new ulong[TerrainChunkFormat.SurfacePresenceWordCount];
        int authoredOrdinal = 0;
        if (conceptDemo)
        {
            AddCluster(ConceptMinimumChunk, ConceptMaximumChunk, ConceptMinimumChunk, ConceptMaximumChunk);
            AddCluster(FarIslandMinimumChunkX, FarIslandMaximumChunkX,
                ConceptMinimumChunk, ConceptMaximumChunk);
        }
        else
            AddCluster(MinimumChunk, MaximumChunk, MinimumChunk, MaximumChunk);
        AddCabin(builder);
        if (conceptDemo)
        {
            AddConceptTerrainShells(builder);
            AddConceptMountainWalls(builder);
            AddFallbackLab(builder);
        }
        if (!builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError))
            throw new InvalidOperationException($"Authored playable WorldPack failed: {buildError}.");
        return artifact!.Open();

        void AddCluster(int minimumX, int maximumX, int minimumZ, int maximumZ)
        {
            for (int chunkZ = minimumZ; chunkZ <= maximumZ; chunkZ++)
            for (int chunkX = minimumX; chunkX <= maximumX; chunkX++)
            {
                FillMaterials(chunkX, chunkZ, materials);
                FillHeights(chunkX, chunkZ, heights);
                if (!TerrainHeightEncoding.TryEncodePlane(
                        heights, offsets, out TerrainHeightPlaneBase heightBase, out _, out _))
                    throw new InvalidOperationException("Authored woods terrain did not fit q5 encoding.");
                var key = new SpatialChunkKey(GameWorldState.DefaultSpace, chunkX, chunkZ);
                bool added;
                WorldPackBuildError error;
                if (conceptDemo)
                {
                    FillConceptBasePresence(chunkX, chunkZ, presence);
                    added = builder.TryAddBaseTerrain(
                        key, materials, heightBase, offsets, presence, out error);
                }
                else
                {
                    added = builder.TryAddBaseTerrain(
                        key, materials, heightBase, offsets, out error);
                }
                if (!added)
                    throw new InvalidOperationException($"Authored terrain chunk {key} failed: {error}.");
                AddWoodsStatics(builder, chunkX, chunkZ, conceptDemo, authoredOrdinal++);
            }
        }
    }

    /// <summary>Creates the shared Concept Demo objects exactly once for a multi-participant world.</summary>
    public static GamePlayableWorldScene PopulateSharedConceptDemoScene(
        EcsWorld world,
        IGameGenerationStateProvider game)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(game);
        if (!world.TryInstantiate(game.Content.CabinDoor, out Entity door) ||
            !game.WorldState.TrySetPosition(world, door, DoorPosition))
            throw new InvalidOperationException("The shared cabin door could not be created.");
        int chestCapacity = Math.Min(64, game.Inventory.Limits.MaximumItemsPerContainer);
        if (!game.Inventory.TryCreateContainer(world, game.Content.WorldChest, chestCapacity, out Entity chest) ||
            !game.WorldState.TrySetPosition(world, chest, ChestPosition))
            throw new InvalidOperationException("The shared cabin chest could not be created.");
        if (!world.TryInstantiate(game.Content.WoodlandAnimal, out Entity animal) ||
            !game.WorldState.TrySetPosition(world, animal, AnimalPosition))
            throw new InvalidOperationException("The shared woodland actor could not be created.");
        if (!world.TryInstantiate(game.Content.SkyTelepad, out Entity telepad) ||
            !game.WorldState.TrySetPosition(world, telepad, SkyTelepadPosition))
            throw new InvalidOperationException("The shared Sky Island telepad could not be created.");
        if (!world.TryInstantiate(game.Content.FarGate, out Entity centralGate) ||
            !game.WorldState.TrySetPosition(world, centralGate, CentralFarGatePosition) ||
            !world.TryInstantiate(game.Content.FarGate, out Entity returnGate) ||
            !game.WorldState.TrySetPosition(world, returnGate, FarReturnGatePosition))
            throw new InvalidOperationException("The shared Far Gate pair could not be created.");
        if (!world.TryInstantiate(game.Content.InformationalSign, out Entity welcomeSign) ||
            !game.WorldState.TrySetPosition(world, welcomeSign, WelcomeSignPosition) ||
            !world.TryInstantiate(game.Content.InformationalSign, out Entity fallbackLabSign) ||
            !game.WorldState.TrySetPosition(world, fallbackLabSign, FallbackLabSignPosition))
            throw new InvalidOperationException("The shared informational signs could not be created.");
        if (!game.Items.TryCreateIronSwordSpawner(
                world, game.WorldState, IronSwordSpawnerPosition, out Entity swordSpawner, out _))
            throw new InvalidOperationException("The shared Iron Sword spawner could not be created.");

        game.WorldActions.RegisterPlayableDynamicBlocker(world, game.WorldState, door);
        game.WorldActions.RegisterPlayableDynamicBlocker(world, game.WorldState, chest);
        return new GamePlayableWorldScene(
            door, chest, animal, telepad, centralGate, returnGate, welcomeSign, fallbackLabSign,
            swordSpawner,
            world.GetOrAssignPersistentId(door),
            world.GetOrAssignPersistentId(chest),
            world.GetOrAssignPersistentId(animal),
            world.GetOrAssignPersistentId(telepad),
            world.GetOrAssignPersistentId(centralGate),
            world.GetOrAssignPersistentId(returnGate),
            world.GetOrAssignPersistentId(welcomeSign),
            world.GetOrAssignPersistentId(fallbackLabSign),
            world.GetOrAssignPersistentId(swordSpawner));
    }

    /// <summary>Adds one participant-owned playable and inventory to an existing shared scene.</summary>
    public static GamePlayableParticipantEntities PopulateSharedParticipant(
        EcsWorld world,
        IGameGenerationStateProvider game,
        Entity player,
        in WorldPosition start)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(game);
        if (!world.IsAlive(player) || !game.WorldState.TrySetPosition(world, player, start))
            throw new InvalidOperationException("The shared playable could not enter the world.");
        if (!game.Inventory.TryCreateOwnedContainer(
                world, game.Content.Backpack, player, 8, out Entity backpack) ||
            !game.Inventory.TryCreateItem(world, backpack, game.Content.Bread, 3, out Entity bread))
            throw new InvalidOperationException("The shared playable inventory could not be created.");
        return new GamePlayableParticipantEntities(
            player,
            backpack,
            bread,
            world.GetOrAssignPersistentId(player),
            world.GetOrAssignPersistentId(backpack),
            world.GetOrAssignPersistentId(bread));
    }

    public static GamePlayableWorldEntities Populate(
        EcsWorld world,
        IGameGenerationStateProvider game,
        Entity player)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(game);
        if (!world.IsAlive(player) || !game.WorldState.TrySetPosition(world, player, PlayerStart))
            throw new InvalidOperationException("The playable character could not enter the woods fixture.");
        if (!world.TryInstantiate(game.Content.CabinDoor, out Entity door) ||
            !game.WorldState.TrySetPosition(world, door, DoorPosition))
            throw new InvalidOperationException("The cabin door could not be created.");
        int chestCapacity = Math.Min(64, game.Inventory.Limits.MaximumItemsPerContainer);
        if (!game.Inventory.TryCreateContainer(world, game.Content.WorldChest, chestCapacity, out Entity chest) ||
            !game.WorldState.TrySetPosition(world, chest, ChestPosition))
            throw new InvalidOperationException("The cabin chest could not be created.");
        if (!world.TryInstantiate(game.Content.WoodlandAnimal, out Entity animal) ||
            !game.WorldState.TrySetPosition(world, animal, AnimalPosition))
            throw new InvalidOperationException("The woodland actor could not be created.");
        if (!game.Inventory.TryCreateOwnedContainer(
                world, game.Content.Backpack, player, 8, out Entity backpack) ||
            !game.Inventory.TryCreateItem(world, backpack, game.Content.Bread, 3, out Entity bread))
            throw new InvalidOperationException("The playable inventory could not be created.");

        game.WorldActions.RegisterPlayableDynamicBlocker(world, game.WorldState, door);
        game.WorldActions.RegisterPlayableDynamicBlocker(world, game.WorldState, chest);

        return new GamePlayableWorldEntities(
            player,
            backpack,
            door,
            chest,
            animal,
            bread,
            world.GetOrAssignPersistentId(player),
            world.GetOrAssignPersistentId(backpack),
            world.GetOrAssignPersistentId(door),
            world.GetOrAssignPersistentId(chest),
            world.GetOrAssignPersistentId(animal),
            world.GetOrAssignPersistentId(bread),
            Entity.Null,
            default,
            Entity.Null,
            default,
            Entity.Null,
            default,
            Entity.Null,
            default,
            Entity.Null,
            default,
            Entity.Null,
            default);
    }

    /// <summary>Extends the accepted Cabin population with the concrete Sky Island telepad.</summary>
    public static GamePlayableWorldEntities PopulateConceptDemo(
        EcsWorld world,
        IGameGenerationStateProvider game,
        Entity player)
    {
        GamePlayableWorldEntities entities = Populate(world, game, player);
        if (!world.TryInstantiate(game.Content.SkyTelepad, out Entity telepad) ||
            !game.WorldState.TrySetPosition(world, telepad, SkyTelepadPosition))
            throw new InvalidOperationException("The Concept Demo Sky Island telepad could not be created.");
        if (!world.TryInstantiate(game.Content.FarGate, out Entity centralGate) ||
            !game.WorldState.TrySetPosition(world, centralGate, CentralFarGatePosition) ||
            !world.TryInstantiate(game.Content.FarGate, out Entity returnGate) ||
            !game.WorldState.TrySetPosition(world, returnGate, FarReturnGatePosition))
            throw new InvalidOperationException("The Concept Demo Far Gate pair could not be created.");
        if (!world.TryInstantiate(game.Content.InformationalSign, out Entity welcomeSign) ||
            !game.WorldState.TrySetPosition(world, welcomeSign, WelcomeSignPosition) ||
            !world.TryInstantiate(game.Content.InformationalSign, out Entity fallbackLabSign) ||
            !game.WorldState.TrySetPosition(world, fallbackLabSign, FallbackLabSignPosition))
            throw new InvalidOperationException("The Concept Demo informational signs could not be created.");
        if (!game.Items.TryCreateIronSwordSpawner(
                world, game.WorldState, IronSwordSpawnerPosition, out Entity swordSpawner, out _))
            throw new InvalidOperationException("The Concept Demo Iron Sword spawner could not be created.");
        return entities with
        {
            SkyTelepad = telepad,
            SkyTelepadId = world.GetOrAssignPersistentId(telepad),
            CentralFarGate = centralGate,
            CentralFarGateId = world.GetOrAssignPersistentId(centralGate),
            FarReturnGate = returnGate,
            FarReturnGateId = world.GetOrAssignPersistentId(returnGate),
            WelcomeSign = welcomeSign,
            WelcomeSignId = world.GetOrAssignPersistentId(welcomeSign),
            FallbackLabSign = fallbackLabSign,
            FallbackLabSignId = world.GetOrAssignPersistentId(fallbackLabSign),
            IronSwordSpawner = swordSpawner,
            IronSwordSpawnerId = world.GetOrAssignPersistentId(swordSpawner),
        };
    }

    private static void FillConceptBasePresence(
        int chunkX,
        int chunkZ,
        Span<ulong> destination)
    {
        destination.Clear();
        for (int z = 0; z < TerrainChunkFormat.MaterialCellEdge; z++)
        for (int x = 0; x < TerrainChunkFormat.MaterialCellEdge; x++)
        {
            int globalCellX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
            int globalCellZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
            long centerX = checked((long)globalCellX * GameWorldSpatialProfile.TerrainCellEdgeUnits +
                GameWorldSpatialProfile.TerrainCellEdgeUnits / 2);
            long centerZ = checked((long)globalCellZ * GameWorldSpatialProfile.TerrainCellEdgeUnits +
                GameWorldSpatialProfile.TerrainCellEdgeUnits / 2);
            if (!IsConceptBasePresent(centerX, centerZ)) continue;
            TerrainSurfacePresence.SetPresent(
                destination,
                new TerrainCellCoord(checked((byte)x), checked((byte)z)),
                true);
        }
    }

    private static bool IsConceptBasePresent(long xMillimeters, long zMillimeters)
    {
        // Two independently authored islands share one sparse WorldPack key set. No base chunks
        // are synthesized across the roughly two-kilometre distance between the clusters.
        bool withinCentral = WithinEllipse(
            xMillimeters, zMillimeters, 8_000, 8_000, 54_000, 50_000);
        bool withinFar = WithinEllipse(
            xMillimeters, zMillimeters, FarIslandCenterXMillimeters, 8_000, 26_000, 24_000);
        bool skyCutout = xMillimeters >= 23_000 && xMillimeters < 32_000 &&
            zMillimeters >= 5_500 && zMillimeters < 12_500;
        return withinFar || withinCentral && !skyCutout;

        static bool WithinEllipse(
            long x,
            long z,
            long centerX,
            long centerZ,
            long radiusX,
            long radiusZ)
        {
            long dx = x - centerX;
            long dz = z - centerZ;
            return (Int128)dx * dx * radiusZ * radiusZ +
                (Int128)dz * dz * radiusX * radiusX <=
                (Int128)radiusX * radiusX * radiusZ * radiusZ;
        }
    }

    private static void FillMaterials(int chunkX, int chunkZ, Span<ushort> destination)
    {
        for (int z = 0; z < TerrainChunkFormat.MaterialCellEdge; z++)
        {
            for (int x = 0; x < TerrainChunkFormat.MaterialCellEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                bool clearing = globalX is >= 24 and <= 44 && globalZ is >= 16 and <= 32;
                ushort material = clearing
                    ? GameTerrainMaterialIds.Grass
                    : ((globalX * 17 ^ globalZ * 31) & 15) == 0
                        ? GameTerrainMaterialIds.Rock
                        : GameTerrainMaterialIds.Grass;
                destination[z * TerrainChunkFormat.MaterialCellEdge + x] = material;
            }
        }
    }

    private static void FillHeights(int chunkX, int chunkZ, Span<int> destination)
    {
        for (int z = 0; z < TerrainChunkFormat.HeightVertexEdge; z++)
        {
            for (int x = 0; x < TerrainChunkFormat.HeightVertexEdge; x++)
            {
                int globalX = checked(chunkX * TerrainChunkFormat.MaterialCellEdge + x);
                int globalZ = checked(chunkZ * TerrainChunkFormat.MaterialCellEdge + z);
                destination[z * TerrainChunkFormat.HeightVertexEdge + x] = AuthoredHeight(globalX, globalZ);
            }
        }
    }

    private static int AuthoredHeight(int globalVertexX, int globalVertexZ)
    {
        int minimumFlatVertexX = FlatTestMinimumChunkX * TerrainChunkFormat.MaterialCellEdge;
        int maximumFlatVertexX = (FlatTestMaximumChunkX + 1) *
            TerrainChunkFormat.MaterialCellEdge;
        int minimumFlatVertexZ = FlatTestMinimumChunkZ * TerrainChunkFormat.MaterialCellEdge;
        int maximumFlatVertexZ = (FlatTestMaximumChunkZ + 1) *
            TerrainChunkFormat.MaterialCellEdge;
        if (globalVertexX >= minimumFlatVertexX && globalVertexX <= maximumFlatVertexX &&
            globalVertexZ >= minimumFlatVertexZ && globalVertexZ <= maximumFlatVertexZ)
            return FlatTestHeightMillimeters;
        if (globalVertexX is >= 24 and <= 44 && globalVertexZ is >= 16 and <= 32)
            return 1_000;
        int noise = Math.Abs(unchecked(globalVertexX * 73 + globalVertexZ * 151 +
            globalVertexX * globalVertexZ * 3)) % 81;
        return 800 + noise * TerrainChunkFormat.HeightQuantumMillimeters;
    }

    private static void AddWoodsStatics(
        WorldPackBuilder builder,
        int chunkX,
        int chunkZ,
        bool conceptDemo,
        int ordinal)
    {
        int[][] locations = [[3_000, 3_000], [12_000, 5_000], [5_000, 13_000]];
        for (int index = 0; index < locations.Length; index++)
        {
            int globalX = checked(chunkX * GameWorldSpatialProfile.WorldChunkEdgeUnits + locations[index][0]);
            int globalZ = checked(chunkZ * GameWorldSpatialProfile.WorldChunkEdgeUnits + locations[index][1]);
            if (globalX is >= 11_500 and <= 21_000 && globalZ is >= 7_000 and <= 17_000)
                continue;
            if (conceptDemo && !IsConceptBasePresent(globalX, globalZ))
                continue;
            WorldPosition position = FromGlobal(globalX, globalZ);
            var source = new WorldFrozenStaticSource(
                new WorldStaticId(checked((ulong)ordinal * 10 + (ulong)index + 1)),
                new WorldStaticDefinitionId(index == 2
                    ? GameWorldStaticDefinitionIds.Rock
                    : GameWorldStaticDefinitionIds.Tree),
                position,
                new WorldYaw(unchecked((ushort)(ordinal * 997 + index * 8_192))),
                0,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                Bounds(position, index == 2 ? 500 : 650, index == 2 ? 900 : 4_500));
            if (!builder.TryAddFrozenStatic(source, out WorldPackBuildError error))
                throw new InvalidOperationException($"Authored woods static failed: {error}.");
        }
    }

    private static void AddConceptTerrainShells(WorldPackBuilder builder)
    {
        AddFlatShell(
            builder,
            new SpatialChunkKey(GameWorldState.DefaultSpace, -1, 0),
            MountainSurface,
            new TerrainSurfacePatchBounds(12, 8, 16, 20),
            topMillimeters: 10_000,
            bottomMillimeters: 8_000,
            topMaterial: GameTerrainMaterialIds.Rock,
            bottomMaterial: GameTerrainMaterialIds.Rock,
            sideMaterial: GameTerrainMaterialIds.Rock);
        AddFlatShell(
            builder,
            new SpatialChunkKey(GameWorldState.DefaultSpace, -1, 0),
            CaveLedgeSurface,
            new TerrainSurfacePatchBounds(16, 20, 8, 6),
            topMillimeters: 4_000,
            bottomMillimeters: 3_500,
            topMaterial: GameTerrainMaterialIds.Rock,
            bottomMaterial: GameTerrainMaterialIds.Rock,
            sideMaterial: GameTerrainMaterialIds.Rock);
        AddFlatShell(
            builder,
            new SpatialChunkKey(GameWorldState.DefaultSpace, 1, 0),
            SkyIslandSurface,
            new TerrainSurfacePatchBounds(18, 12, 12, 12),
            topMillimeters: 3_500,
            bottomMillimeters: 3_000,
            topMaterial: GameTerrainMaterialIds.Grass,
            bottomMaterial: GameTerrainMaterialIds.Rock,
            sideMaterial: GameTerrainMaterialIds.Rock);
    }

    private static void AddFlatShell(
        WorldPackBuilder builder,
        SpatialChunkKey key,
        WorldSurfaceId id,
        TerrainSurfacePatchBounds bounds,
        int? topMillimeters,
        int? bottomMillimeters,
        ushort topMaterial,
        ushort bottomMaterial,
        ushort sideMaterial)
    {
        WorldTerrainSurfaceFaces faces =
            (topMillimeters.HasValue ? WorldTerrainSurfaceFaces.Top : WorldTerrainSurfaceFaces.None) |
            (bottomMillimeters.HasValue ? WorldTerrainSurfaceFaces.Bottom : WorldTerrainSurfaceFaces.None);
        var presence = new ulong[bounds.PresenceWordCount];
        Array.Fill(presence, ulong.MaxValue);
        int remainder = bounds.CellCount & 63;
        if (remainder != 0)
            presence[^1] = (1UL << remainder) - 1;
        var connections = new TerrainSurfaceEdgeConnection[bounds.CellCount];
        ushort[] topMaterials = topMillimeters.HasValue
            ? Enumerable.Repeat(topMaterial, bounds.CellCount).ToArray()
            : [];
        ushort[] bottomMaterials = bottomMillimeters.HasValue
            ? Enumerable.Repeat(bottomMaterial, bounds.CellCount).ToArray()
            : [];
        TerrainHeightOffset[] topHeights = topMillimeters.HasValue
            ? new TerrainHeightOffset[bounds.VertexCount]
            : [];
        TerrainHeightOffset[] bottomHeights = bottomMillimeters.HasValue
            ? new TerrainHeightOffset[bounds.VertexCount]
            : [];
        TerrainHeightPlaneBase topBase = default;
        TerrainHeightPlaneBase bottomBase = default;
        if (topMillimeters.HasValue &&
            !TerrainHeightPlaneBase.TryCreate(topMillimeters.Value, out topBase) ||
            bottomMillimeters.HasValue &&
            !TerrainHeightPlaneBase.TryCreate(bottomMillimeters.Value, out bottomBase))
            throw new InvalidOperationException($"Concept Demo shell {id} is not q5 canonical.");
        if (!builder.TryAddAdditionalTerrainSurfaceBand(
                key,
                id,
                bounds,
                faces,
                presence,
                connections,
                topMaterials,
                topBase,
                topHeights,
                bottomMaterials,
                bottomBase,
                bottomHeights,
                sideMaterial,
                out WorldPackBuildError error))
            throw new InvalidOperationException($"Concept Demo shell {id} failed: {error}.");
    }

    private static void AddConceptMountainWalls(WorldPackBuilder builder)
    {
        WorldPosition root = FromGlobal(-6_000, 9_000);
        WorldMultiPieceSource[] pieces =
        [
            CabinPiece(1, GameWorldStaticDefinitionIds.Rock,
                -10_000, 9_000, 200, 5_000, 7_200,
                WorldYaw.QuarterTurn, GameWorldStaticVariants.MountainWallTenMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            CabinPiece(2, GameWorldStaticDefinitionIds.Rock,
                -2_000, 9_000, 200, 5_000, 7_200,
                WorldYaw.QuarterTurn, GameWorldStaticVariants.MountainWallTenMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            CabinPiece(3, GameWorldStaticDefinitionIds.Rock,
                -6_000, 14_000, 4_000, 200, 7_200,
                WorldYaw.Zero, GameWorldStaticVariants.MountainWallEightMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
        ];
        if (!builder.TryAddStaticMulti(
                new MultiInstanceId(2), root, WorldYaw.Zero, pieces,
                out WorldPackBuildError error))
            throw new InvalidOperationException($"Concept Demo mountain walls failed: {error}.");
    }

    private static void AddFallbackLab(WorldPackBuilder builder)
    {
        const int pedestalHalfExtent = 1_000;
        const int pedestalHeight = 400;
        const int canaryHalfExtent = 500;
        const int canaryHeight = 1_600;
        WorldStaticFlags physical = WorldStaticFlags.Renderable | WorldStaticFlags.Collidable;
        WorldPosition root = FallbackLabModelMissingPosition;
        WorldMultiPieceSource[] pieces =
        [
            LabPiece(1, FallbackLabCorrectPosition, GameWorldStaticVariants.FallbackLabPedestal,
                pedestalHalfExtent, pedestalHeight),
            LabPiece(2, FallbackLabCorrectPosition, GameWorldStaticVariants.FallbackLabCorrect,
                canaryHalfExtent, canaryHeight, pedestalHeight),
            LabPiece(3, FallbackLabModelMissingPosition, GameWorldStaticVariants.FallbackLabPedestal,
                pedestalHalfExtent, pedestalHeight),
            LabPiece(4, FallbackLabModelMissingPosition,
                GameWorldStaticVariants.FallbackLabModelMissing,
                canaryHalfExtent, canaryHeight, pedestalHeight),
            LabPiece(5, FallbackLabTextureMissingPosition,
                GameWorldStaticVariants.FallbackLabPedestal,
                pedestalHalfExtent, pedestalHeight),
            LabPiece(6, FallbackLabTextureMissingPosition,
                GameWorldStaticVariants.FallbackLabTextureMissing,
                canaryHalfExtent, canaryHeight, pedestalHeight),
            LabPiece(7, FallbackLabBothMissingPosition,
                GameWorldStaticVariants.FallbackLabPedestal,
                pedestalHalfExtent, pedestalHeight),
            LabPiece(8, FallbackLabBothMissingPosition,
                GameWorldStaticVariants.FallbackLabModelAndTextureMissing,
                canaryHalfExtent, canaryHeight, pedestalHeight),
        ];
        if (!builder.TryAddStaticMulti(
                new MultiInstanceId(3), root, WorldYaw.Zero, pieces,
                out WorldPackBuildError error))
            throw new InvalidOperationException($"Concept Demo Fallback Lab failed: {error}.");

        WorldMultiPieceSource LabPiece(
            uint pieceId,
            WorldPosition ground,
            ushort variant,
            int halfExtent,
            int height,
            int verticalOffset = 0)
        {
            WorldPosition position = verticalOffset == 0
                ? ground
                : WithVerticalOffset(ground, verticalOffset);
            return new WorldMultiPieceSource(
                new MultiPieceId(pieceId),
                new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Rock),
                position,
                WorldYaw.Zero,
                variant,
                physical,
                Bounds(position, halfExtent, halfExtent, height));
        }
    }

    private static void AddCabin(WorldPackBuilder builder)
    {
        WorldPosition root = FromGlobal(16_000, 12_000);
        WorldMultiPieceSource[] pieces =
        [
            // North wall and two side walls form a true 8 m x 6 m shell.
            CabinPiece(1, GameWorldStaticDefinitionIds.CabinWall,
                16_000, 15_000, 4_000, 150, 2_500,
                WorldYaw.Zero, GameWorldStaticVariants.CabinWallEightMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            CabinPiece(2, GameWorldStaticDefinitionIds.CabinWall,
                12_000, 12_000, 150, 3_000, 2_500,
                WorldYaw.QuarterTurn, GameWorldStaticVariants.CabinWallSixMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            CabinPiece(3, GameWorldStaticDefinitionIds.CabinWall,
                20_000, 12_000, 150, 3_000, 2_500,
                WorldYaw.QuarterTurn, GameWorldStaticVariants.CabinWallSixMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            // The south wall is split around a real 2 m doorway centered on the public door.
            CabinPiece(4, GameWorldStaticDefinitionIds.CabinWall,
                13_500, 9_000, 1_500, 150, 2_500,
                WorldYaw.Zero, GameWorldStaticVariants.CabinWallThreeMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            CabinPiece(5, GameWorldStaticDefinitionIds.CabinWall,
                18_500, 9_000, 1_500, 150, 2_500,
                WorldYaw.Zero, GameWorldStaticVariants.CabinWallThreeMeters,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable),
            // A translucent presentation roof proves the structural layer but sits above the
            // actor collision volume, so standing beneath it is not a horizontal obstruction.
            CabinPiece(6, GameWorldStaticDefinitionIds.CabinRoof,
                16_000, 12_000, 4_150, 3_150, 250,
                WorldYaw.Zero, GameWorldStaticVariants.CabinRoofEightBySixMeters,
                WorldStaticFlags.Renderable, verticalOffset: 2_500),
        ];
        if (!builder.TryAddStaticMulti(new MultiInstanceId(1), root, WorldYaw.Zero, pieces,
                out WorldPackBuildError multiError))
            throw new InvalidOperationException($"Authored cabin Multi failed: {multiError}.");

        WorldPosition shelf = FromGlobal(18_500, 14_000);
        WorldPosition book = WithVerticalOffset(FromGlobal(18_500, 14_000), 1_800);
        if (!builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(10_001), new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Shelf),
                shelf, WorldYaw.QuarterTurn, 0,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                Bounds(shelf, 175, 450, 1_800)), out _) ||
            !builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(10_002), new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Book),
                book, WorldYaw.Zero, 0, WorldStaticFlags.Renderable,
                Bounds(book, 120, 90, 180)), out _))
            throw new InvalidOperationException("Authored cabin decor failed.");
    }

    private static WorldMultiPieceSource CabinPiece(
        uint pieceId,
        uint definitionId,
        long globalX,
        long globalZ,
        int halfExtentX,
        int halfExtentZ,
        int height,
        WorldYaw orientation,
        ushort variant,
        WorldStaticFlags flags,
        int verticalOffset = 0)
    {
        WorldPosition ground = FromGlobal(globalX, globalZ);
        WorldPosition position = verticalOffset == 0
            ? ground
            : WithVerticalOffset(ground, verticalOffset);
        return new WorldMultiPieceSource(
            new MultiPieceId(pieceId),
            new WorldStaticDefinitionId(definitionId),
            position,
            orientation,
            variant,
            flags,
            Bounds(position, halfExtentX, halfExtentZ, height));
    }

    private static WorldPosition FromGlobal(long xMillimeters, long zMillimeters)
    {
        int vertexX = checked((int)Math.DivRem(xMillimeters, 500, out long remainderX));
        int vertexZ = checked((int)Math.DivRem(zMillimeters, 500, out long remainderZ));
        if (remainderX != 0 || remainderZ != 0)
            throw new InvalidOperationException("Authored fixture positions must align to terrain vertices.");
        int y = AuthoredHeight(vertexX, vertexZ);
        if (!WorldPosition.TryFromGlobalMillimeters(xMillimeters, y, zMillimeters, out WorldPosition position))
            throw new InvalidOperationException("Authored fixture position is outside the world profile.");
        return position;
    }

    private static WorldPosition WithVerticalOffset(in WorldPosition position, int offset)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(
                position.GlobalXMillimeters,
                checked(position.YMillimeters + offset),
                position.GlobalZMillimeters,
                out WorldPosition elevated))
            throw new InvalidOperationException("Authored fixture height is outside the world profile.");
        return elevated;
    }

    private static WorldPosition AtHeight(long xMillimeters, int yMillimeters, long zMillimeters)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(
                xMillimeters, yMillimeters, zMillimeters, out WorldPosition position))
            throw new InvalidOperationException("Authored fixture position is outside the world profile.");
        return position;
    }

    private static WorldStaticBounds Bounds(in WorldPosition position, int halfWidth, int height) => new(
        position.GlobalXMillimeters - halfWidth,
        position.YMillimeters,
        position.GlobalZMillimeters - halfWidth,
        position.GlobalXMillimeters + halfWidth + 1L,
        position.YMillimeters + height,
        position.GlobalZMillimeters + halfWidth + 1L);

    private static WorldStaticBounds Bounds(
        in WorldPosition position,
        int halfExtentX,
        int halfExtentZ,
        int height) => new(
        position.GlobalXMillimeters - halfExtentX,
        position.YMillimeters,
        position.GlobalZMillimeters - halfExtentZ,
        position.GlobalXMillimeters + halfExtentX + 1L,
        position.YMillimeters + height,
        position.GlobalZMillimeters + halfExtentZ + 1L);
}
