using System.Runtime.CompilerServices;
using UltraSim.ECS;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
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
    PersistentEntityId BreadId);

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
}

/// <summary>Admission and independently composed effects of one player command.</summary>
public readonly record struct GamePlayerCommandResult(
    GameWorldActionStatus Admission,
    GameWorldActionStatus Locomotion,
    GameWorldActionStatus Interaction,
    WorldPosition Position,
    bool DoorIsOpen)
{
    public bool WasAdmitted => Admission == GameWorldActionStatus.Accepted;
}

/// <summary>
/// Generation-owned concrete movement/interaction capability. It consumes the common input command,
/// validates ECS/world truth, and mutates only authoritative persisted components.
/// </summary>
public sealed class GameWorldActions
{
    private enum RetainedLocomotionKind : byte
    {
        None = 0,
        Direct,
        Destination,
    }

    public const int FullInputSpeedMillimetersPerSecond = 6_000;
    public const int InteractionRangeMillimeters = 2_500;

    private readonly WorldId _worldId;
    private readonly ComponentLookup<GameCharacter> _character;
    private readonly ComponentLookup<GameCabinDoorState> _door;
    private readonly ulong[] _controlEpochs;
    private readonly ulong[] _lastSequences;
    private readonly uint[] _controlGenerations;
    private readonly RetainedLocomotionKind[] _locomotionKinds;
    private readonly short[] _directX;
    private readonly short[] _directZ;
    private readonly long[] _destinationX;
    private readonly long[] _destinationZ;
    private readonly long[] _movementRemainderX;
    private readonly long[] _movementRemainderZ;
    private readonly Entity[] _activeLocomotion;
    private readonly int[] _activeLocomotionPositions;
    private int _activeLocomotionCount;

    internal GameWorldActions(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _worldId = world.Id;
        _character = world.PrepareLookup<GameCharacter>();
        _door = world.PrepareLookup<GameCabinDoorState>();
        _controlEpochs = new ulong[world.Options.EntityCapacity.MaximumEntities];
        _lastSequences = new ulong[world.Options.EntityCapacity.MaximumEntities];
        _controlGenerations = new uint[_lastSequences.Length];
        _locomotionKinds = new RetainedLocomotionKind[_lastSequences.Length];
        _directX = new short[_lastSequences.Length];
        _directZ = new short[_lastSequences.Length];
        _destinationX = new long[_lastSequences.Length];
        _destinationZ = new long[_lastSequences.Length];
        _movementRemainderX = new long[_lastSequences.Length];
        _movementRemainderZ = new long[_lastSequences.Length];
        _activeLocomotion = new Entity[_lastSequences.Length];
        _activeLocomotionPositions = new int[_lastSequences.Length];
    }

    /// <summary>Known fixed backing reserved for control lineage and retained locomotion.</summary>
    public long ReservedBytes => checked((long)_lastSequences.Length *
        ((sizeof(ulong) * 2) + sizeof(uint) + (sizeof(short) * 2) +
         (sizeof(long) * 4) + sizeof(byte) + Unsafe.SizeOf<Entity>() + sizeof(int)));

    public int ActiveLocomotionCount => _activeLocomotionCount;

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
        _controlGenerations[index] = actor.Generation;
        _controlEpochs[index] = epoch.Value;
        _lastSequences[index] = 0;
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

        GameWorldActionStatus interaction = GameWorldActionStatus.NotRequested;
        bool doorIsOpen = false;
        if ((command.PressedActions & GamePlayerActions.Interact) != 0)
        {
            interaction = ToggleCabinDoorCore(
                world, worldState, actor, interactionTarget, out doorIsOpen);
        }

        result = new GamePlayerCommandResult(
            GameWorldActionStatus.Accepted,
            locomotion,
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
                    !worldState.TrySampleBaseTerrainHeight(candidate, out _))
                {
                    return GameWorldActionStatus.OutsidePreparedWorld;
                }
                SetDestination(
                    actor,
                    update.DestinationXMillimeters,
                    update.DestinationZMillimeters);
                return GameWorldActionStatus.Accepted;
            default:
                throw new InvalidOperationException("A validated locomotion update has an unknown kind.");
        }
    }

    /// <summary>
    /// Integrates every retained Direct or Destination intent once for the authoritative fixed step.
    /// This runs after the ECS graph/barrier and before canonical Spatial/publication maintenance.
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
        while (active < _activeLocomotionCount)
        {
            Entity actor = _activeLocomotion[active];
            int index = checked((int)actor.Index);
            if (!world.IsAlive(actor) || !world.Has(actor, _character) ||
                _controlGenerations[index] != actor.Generation ||
                _controlEpochs[index] == 0 ||
                !worldState.TryReadPosition(world, actor, out WorldPosition current))
            {
                ClearLocomotion(index);
                continue;
            }

            bool retained = _locomotionKinds[index] switch
            {
                RetainedLocomotionKind.Direct =>
                    IntegrateDirect(world, worldState, actor, index, current, delta.Quanta),
                RetainedLocomotionKind.Destination =>
                    IntegrateDestination(world, worldState, actor, index, current, delta.Quanta),
                _ => throw new InvalidOperationException(
                    "An active locomotion slot has no retained locomotion kind."),
            };
            if (retained)
                active++;
        }
    }

    private bool IntegrateDirect(
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
            return true;

        long nextX = checked(current.GlobalXMillimeters + deltaX);
        long nextZ = checked(current.GlobalZMillimeters + deltaZ);
        if (!TryResolveTerrainPosition(worldState, current.YMillimeters, nextX, nextZ, out WorldPosition position))
        {
            // Direct remains current while this step is blocked by prepared-world bounds.
            return true;
        }
        SetAuthoritativePosition(world, worldState, actor, position);
        return true;
    }

    private bool IntegrateDestination(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        int index,
        in WorldPosition current,
        long stepQuanta)
    {
        long dx = checked(_destinationX[index] - current.GlobalXMillimeters);
        long dz = checked(_destinationZ[index] - current.GlobalZMillimeters);
        if (dx == 0 && dz == 0)
        {
            ClearLocomotion(index);
            return false;
        }

        int distance = IntegrateDistance(stepQuanta, ref _movementRemainderX[index]);
        if (distance == 0)
            return true;

        Int128 squaredRemaining = (Int128)dx * dx + (Int128)dz * dz;
        long nextX;
        long nextZ;
        bool reached = squaredRemaining <= (Int128)distance * distance;
        if (reached)
        {
            nextX = _destinationX[index];
            nextZ = _destinationZ[index];
        }
        else
        {
            long denominator = CeilingSquareRoot(squaredRemaining, dx, dz);
            long deltaX = checked((long)((Int128)dx * distance / denominator));
            long deltaZ = checked((long)((Int128)dz * distance / denominator));
            if (deltaX == 0 && deltaZ == 0)
            {
                if (Int128.Abs((Int128)dx) >= Int128.Abs((Int128)dz))
                    deltaX = Math.Sign(dx);
                else
                    deltaZ = Math.Sign(dz);
            }
            nextX = checked(current.GlobalXMillimeters + deltaX);
            nextZ = checked(current.GlobalZMillimeters + deltaZ);
        }

        if (!TryResolveTerrainPosition(worldState, current.YMillimeters, nextX, nextZ, out WorldPosition position))
        {
            // A previously admitted straight-line destination became impossible in prepared truth.
            ClearLocomotion(index);
            return false;
        }
        SetAuthoritativePosition(world, worldState, actor, position);
        if (reached)
        {
            ClearLocomotion(index);
            return false;
        }
        return true;
    }

    private static bool TryResolveTerrainPosition(
        GameWorldState worldState,
        int currentY,
        long globalX,
        long globalZ,
        out WorldPosition position)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(
                globalX, currentY, globalZ, out WorldPosition candidate) ||
            !worldState.TrySampleBaseTerrainHeight(candidate, out int terrainY))
        {
            position = default;
            return false;
        }
        return WorldPosition.TryFromGlobalMillimeters(globalX, terrainY, globalZ, out position);
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

    internal void ValidateRestoredDoorConsistency(EcsWorld world, GameWorldState worldState)
    {
        EnsureWorld(world, worldState);
        for (uint index = 0; index < (uint)_lastSequences.Length; index++)
        {
            if (!world.TryGetEntity(index, out Entity entity) ||
                !world.TryRead(entity, _door, out GameCabinDoorState state))
                continue;
            if (state.IsOpen is not (0 or 1) ||
                !worldState.TryReadPublic(world, entity, out WorldPublicDescriptor descriptor) ||
                descriptor.PrefabId != GameWorldPublicPrefabIds.CabinDoor ||
                descriptor.Orientation != DoorOrientation(state.IsOpen != 0))
            {
                throw new InvalidOperationException(
                    $"Cabin door {entity} has contradictory persisted/public state.");
            }
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
            ClearLocomotion(index);
        _locomotionKinds[index] = RetainedLocomotionKind.Direct;
        _directX[index] = x;
        _directZ[index] = z;
        EnsureActive(actor, index);
    }

    private void SetDestination(Entity actor, long globalX, long globalZ)
    {
        int index = checked((int)actor.Index);
        if (_locomotionKinds[index] == RetainedLocomotionKind.Destination &&
            _destinationX[index] == globalX && _destinationZ[index] == globalZ)
            return;
        ClearLocomotion(index);
        _locomotionKinds[index] = RetainedLocomotionKind.Destination;
        _destinationX[index] = globalX;
        _destinationZ[index] = globalZ;
        EnsureActive(actor, index);
    }

    private void EnsureActive(Entity actor, int index)
    {
        if (_activeLocomotionPositions[index] != 0)
            return;
        if (_activeLocomotionCount >= _activeLocomotion.Length)
            throw new InvalidOperationException("The bounded locomotion set exhausted unexpectedly.");
        _activeLocomotion[_activeLocomotionCount] = actor;
        _activeLocomotionPositions[index] = checked(_activeLocomotionCount + 1);
        _activeLocomotionCount++;
    }

    private void ClearLocomotion(int index)
    {
        int encoded = _activeLocomotionPositions[index];
        if (encoded != 0)
        {
            int slot = encoded - 1;
            int last = --_activeLocomotionCount;
            if (slot != last)
            {
                Entity donor = _activeLocomotion[last];
                _activeLocomotion[slot] = donor;
                _activeLocomotionPositions[checked((int)donor.Index)] = checked(slot + 1);
            }
            _activeLocomotion[last] = Entity.Null;
            _activeLocomotionPositions[index] = 0;
        }

        _locomotionKinds[index] = RetainedLocomotionKind.None;
        _directX[index] = 0;
        _directZ[index] = 0;
        _destinationX[index] = 0;
        _destinationZ[index] = 0;
        _movementRemainderX[index] = 0;
        _movementRemainderZ[index] = 0;
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
}

/// <summary>Deterministic authored woods/cabin content and setup for T0.10 acceptance.</summary>
public static class GamePlayableWorld
{
    public const int MinimumChunk = -2;
    public const int MaximumChunk = 2;
    public const int ChunkCount = 25;

    public static WorldPosition PlayerStart => FromGlobal(13_500, 12_000);
    public static WorldPosition DoorPosition => FromGlobal(16_000, 12_000);
    public static WorldPosition ChestPosition => FromGlobal(18_000, 12_000);
    public static WorldPosition AnimalPosition => FromGlobal(8_000, 4_000);

    public static WorldPack CreateWorldPack()
    {
        var limits = new WorldPackLimits(
            2 * 1024 * 1024,
            ChunkCount,
            8,
            maximumFrozenStatics: 128,
            maximumMultiInstances: 8,
            maximumMultiFragments: 32);
        var builder = new WorldPackBuilder(
            GameWorldState.DefaultSpace,
            GameContent.WorldFingerprint,
            limits);
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        var heights = new int[TerrainChunkFormat.HeightVertexCount];
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        for (int chunkZ = MinimumChunk; chunkZ <= MaximumChunk; chunkZ++)
        {
            for (int chunkX = MinimumChunk; chunkX <= MaximumChunk; chunkX++)
            {
                FillMaterials(chunkX, chunkZ, materials);
                FillHeights(chunkX, chunkZ, heights);
                if (!TerrainHeightEncoding.TryEncodePlane(
                        heights, offsets, out TerrainHeightPlaneBase heightBase, out _, out _))
                    throw new InvalidOperationException("Authored woods terrain did not fit q5 encoding.");
                var key = new SpatialChunkKey(GameWorldState.DefaultSpace, chunkX, chunkZ);
                if (!builder.TryAddBaseTerrain(key, materials, heightBase, offsets, out WorldPackBuildError error))
                    throw new InvalidOperationException($"Authored terrain chunk {key} failed: {error}.");
                AddWoodsStatics(builder, chunkX, chunkZ);
            }
        }
        AddCabin(builder);
        if (!builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError))
            throw new InvalidOperationException($"Authored woods/cabin WorldPack failed: {buildError}.");
        return artifact!.Open();
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
            world.GetOrAssignPersistentId(bread));
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
        if (globalVertexX is >= 24 and <= 44 && globalVertexZ is >= 16 and <= 32)
            return 1_000;
        int noise = Math.Abs(unchecked(globalVertexX * 73 + globalVertexZ * 151 +
            globalVertexX * globalVertexZ * 3)) % 81;
        return 800 + noise * TerrainChunkFormat.HeightQuantumMillimeters;
    }

    private static void AddWoodsStatics(WorldPackBuilder builder, int chunkX, int chunkZ)
    {
        int ordinal = checked((chunkZ - MinimumChunk) * 5 + (chunkX - MinimumChunk));
        int[][] locations = [[3_000, 3_000], [12_000, 5_000], [5_000, 13_000]];
        for (int index = 0; index < locations.Length; index++)
        {
            int globalX = checked(chunkX * GameWorldSpatialProfile.WorldChunkEdgeUnits + locations[index][0]);
            int globalZ = checked(chunkZ * GameWorldSpatialProfile.WorldChunkEdgeUnits + locations[index][1]);
            if (globalX is >= 11_500 and <= 21_000 && globalZ is >= 7_000 and <= 17_000)
                continue;
            WorldPosition position = FromGlobal(globalX, globalZ);
            var source = new WorldFrozenStaticSource(
                new WorldStaticId(checked((ulong)ordinal * 10 + (ulong)index + 1)),
                new WorldStaticDefinitionId(index == 2
                    ? GameWorldStaticDefinitionIds.Rock
                    : GameWorldStaticDefinitionIds.Tree),
                position,
                new WorldYaw(checked((ushort)(ordinal * 997 + index * 8_192))),
                0,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                Bounds(position, index == 2 ? 500 : 650, index == 2 ? 900 : 4_500));
            if (!builder.TryAddFrozenStatic(source, out WorldPackBuildError error))
                throw new InvalidOperationException($"Authored woods static failed: {error}.");
        }
    }

    private static void AddCabin(WorldPackBuilder builder)
    {
        WorldPosition root = FromGlobal(16_000, 12_000);
        WorldMultiPieceSource[] pieces =
        [
            CabinPiece(1, GameWorldStaticDefinitionIds.CabinWall, 13_000, 9_000, 3_000, 250),
            CabinPiece(2, GameWorldStaticDefinitionIds.CabinWall, 19_000, 9_000, 3_000, 250),
            CabinPiece(3, GameWorldStaticDefinitionIds.CabinWall, 13_000, 15_000, 3_000, 250),
            CabinPiece(4, GameWorldStaticDefinitionIds.CabinWall, 19_000, 15_000, 3_000, 250),
            CabinPiece(5, GameWorldStaticDefinitionIds.CabinRoof, 15_000, 12_000, 1_500, 3_500),
            CabinPiece(6, GameWorldStaticDefinitionIds.CabinRoof, 17_000, 12_000, 1_500, 3_500),
        ];
        if (!builder.TryAddStaticMulti(new MultiInstanceId(1), root, WorldYaw.Zero, pieces,
                out WorldPackBuildError multiError))
            throw new InvalidOperationException($"Authored cabin Multi failed: {multiError}.");

        WorldPosition shelf = FromGlobal(18_500, 14_000);
        WorldPosition book = FromGlobal(18_500, 13_500);
        if (!builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(10_001), new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Shelf),
                shelf, WorldYaw.QuarterTurn, 0,
                WorldStaticFlags.Renderable | WorldStaticFlags.Collidable, Bounds(shelf, 450, 1_800)), out _) ||
            !builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(10_002), new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Book),
                book, WorldYaw.Zero, 0, WorldStaticFlags.Renderable, Bounds(book, 120, 180)), out _))
            throw new InvalidOperationException("Authored cabin decor failed.");
    }

    private static WorldMultiPieceSource CabinPiece(
        uint pieceId,
        uint definitionId,
        long globalX,
        long globalZ,
        int halfWidth,
        int height)
    {
        WorldPosition position = FromGlobal(globalX, globalZ);
        return new WorldMultiPieceSource(
            new MultiPieceId(pieceId),
            new WorldStaticDefinitionId(definitionId),
            position,
            WorldYaw.Zero,
            0,
            WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
            Bounds(position, halfWidth, height));
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

    private static WorldStaticBounds Bounds(in WorldPosition position, int halfWidth, int height) => new(
        position.GlobalXMillimeters - halfWidth,
        position.YMillimeters,
        position.GlobalZMillimeters - halfWidth,
        position.GlobalXMillimeters + halfWidth + 1L,
        position.YMillimeters + height,
        position.GlobalZMillimeters + halfWidth + 1L);
}
