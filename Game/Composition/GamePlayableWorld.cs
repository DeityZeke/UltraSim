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
    GameWorldActionStatus Movement,
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
    public const int FullInputSpeedMillimetersPerSecond = 6_000;
    public const int InteractionRangeMillimeters = 2_500;

    private readonly WorldId _worldId;
    private readonly ComponentLookup<GameCharacter> _character;
    private readonly ComponentLookup<GameCabinDoorState> _door;
    private readonly ulong[] _lastSequences;
    private readonly uint[] _sequenceGenerations;
    private readonly long[] _movementRemainderX;
    private readonly long[] _movementRemainderZ;

    internal GameWorldActions(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _worldId = world.Id;
        _character = world.PrepareLookup<GameCharacter>();
        _door = world.PrepareLookup<GameCabinDoorState>();
        _lastSequences = new ulong[world.Options.EntityCapacity.MaximumEntities];
        _sequenceGenerations = new uint[_lastSequences.Length];
        _movementRemainderX = new long[_lastSequences.Length];
        _movementRemainderZ = new long[_lastSequences.Length];
    }

    /// <summary>Known fixed backing reserved for actor sequence and movement-integration state.</summary>
    public long ReservedBytes => checked((long)_lastSequences.Length *
        (sizeof(ulong) + sizeof(uint) + sizeof(long) + sizeof(long)));

    /// <summary>
    /// Admits one actor command exactly once, then composes its movement and interaction effects.
    /// Movement axes are desired velocity and integrate against the authoritative fixed cadence.
    /// </summary>
    public GameWorldActionStatus ApplyPlayerCommand(
        EcsWorld world,
        GameWorldState worldState,
        Entity actor,
        in GamePlayerCommand command,
        int authoritativeSimulationFrequencyHz,
        Entity interactionTarget,
        out GamePlayerCommandResult result)
    {
        EnsureWorld(world, worldState);
        result = default;
        if (!command.IsValid ||
            !WorldClockOptions.IsSupportedSimulationFrequency(authoritativeSimulationFrequencyHz))
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

        if (!SequenceIsFresh(actor, command.Sequence))
        {
            result = result with { Admission = GameWorldActionStatus.StaleCommand, Position = current };
            return GameWorldActionStatus.StaleCommand;
        }

        // Admission is the replay boundary. A fresh command is consumed before
        // any effect is interpreted, including effects that later reject.
        CommitSequence(actor, command.Sequence);

        GameWorldActionStatus movement = GameWorldActionStatus.NotRequested;
        WorldPosition position = current;
        if (command.MoveX != 0 || command.MoveZ != 0)
        {
            int index = checked((int)actor.Index);
            int deltaX = IntegrateAxis(
                command.MoveX,
                authoritativeSimulationFrequencyHz,
                ref _movementRemainderX[index]);
            int deltaZ = IntegrateAxis(
                command.MoveZ,
                authoritativeSimulationFrequencyHz,
                ref _movementRemainderZ[index]);
            long nextX = checked(current.GlobalXMillimeters + deltaX);
            long nextZ = checked(current.GlobalZMillimeters + deltaZ);
            if (!WorldPosition.TryFromGlobalMillimeters(
                    nextX, current.YMillimeters, nextZ, out WorldPosition candidate) ||
                !worldState.TrySampleBaseTerrainHeight(candidate, out int terrainY) ||
                !WorldPosition.TryFromGlobalMillimeters(nextX, terrainY, nextZ, out position))
            {
                movement = GameWorldActionStatus.OutsidePreparedWorld;
                position = current;
            }
            else if (!worldState.TrySetPosition(world, actor, position))
            {
                movement = GameWorldActionStatus.InvariantViolation;
                position = current;
            }
            else
            {
                movement = GameWorldActionStatus.Accepted;
            }
        }

        GameWorldActionStatus interaction = GameWorldActionStatus.NotRequested;
        bool doorIsOpen = false;
        if ((command.PressedActions & GamePlayerActions.Interact) != 0)
        {
            interaction = ToggleCabinDoorCore(
                world, worldState, actor, interactionTarget, out doorIsOpen);
        }

        result = new GamePlayerCommandResult(
            GameWorldActionStatus.Accepted,
            movement,
            interaction,
            position,
            doorIsOpen);
        return GameWorldActionStatus.Accepted;
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

    private static int IntegrateAxis(short value, int frequencyHz, ref long remainder)
    {
        long stepQuanta = WorldClockOptions.WorldTimeQuantaPerSecond / frequencyHz;
        long denominator = checked(
            (long)GamePlayerCommand.MaximumAxisMagnitude * WorldClockOptions.WorldTimeQuantaPerSecond);
        Int128 numerator = (Int128)value * FullInputSpeedMillimetersPerSecond * stepQuanta + remainder;
        Int128 delta = numerator / denominator;
        remainder = checked((long)(numerator % denominator));
        return checked((int)delta);
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
        return _sequenceGenerations[index] != actor.Generation || sequence > _lastSequences[index];
    }

    private void CommitSequence(Entity actor, ulong sequence)
    {
        int index = checked((int)actor.Index);
        if (_sequenceGenerations[index] != actor.Generation)
        {
            _movementRemainderX[index] = 0;
            _movementRemainderZ[index] = 0;
        }
        _sequenceGenerations[index] = actor.Generation;
        _lastSequences[index] = sequence;
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
