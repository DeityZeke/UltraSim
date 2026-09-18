using UltraSim.ECS;
using UltraSim.Game.Inspection;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;

namespace UltraSim.Game.Composition;

/// <summary>Cold bounded ranges used by the first deterministic weapon spawn recipe.</summary>
public readonly record struct GameWeaponSpawnRecipe(
    ushort DamageMinimumLow,
    ushort DamageMinimumHigh,
    ushort DamageMaximumLow,
    ushort DamageMaximumHigh,
    ushort DurabilityLow,
    ushort DurabilityHigh)
{
    public static GameWeaponSpawnRecipe IronSword => new(2, 4, 6, 9, 35, 60);

    public bool IsValid => DamageMinimumLow != 0 && DamageMinimumHigh >= DamageMinimumLow &&
        DamageMaximumLow >= DamageMinimumHigh && DamageMaximumHigh >= DamageMaximumLow &&
        DurabilityLow != 0 && DurabilityHigh >= DurabilityLow;
}

/// <summary>
/// One bounded cold public item-property fact. It joins an already-admitted public lifetime; it
/// is not part of the hot movement/publication record and carries no client interaction authority.
/// </summary>
public readonly record struct GamePublicWeaponPresentation(
    WorldPublicEntityId Id,
    WorldPosition Position,
    WorldPublicStateFlags StateFlags,
    GameWeaponInstance Weapon,
    string DisplayName,
    InspectionPropertySet InspectionProperties)
{
    public bool IsValid => Id.IsValid &&
        (StateFlags & (WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem)) ==
            (WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem) &&
        Weapon.IsCanonical() &&
        !string.IsNullOrWhiteSpace(DisplayName) && InspectionProperties is not null;
}

/// <summary>
/// Concrete Game-owned item-instance lifecycle. Compiled prefabs remain constant; this capability
/// performs deterministic one-time rolls, owns respawn lineage, and coordinates public/private
/// representation without making Inventory aware of weapon schemas.
/// </summary>
public sealed class GameItemGenerationState : IInventoryItemInspectionProvider
{
    public const int PickupRangeMillimeters = 3_000;

    private enum SpawnResult : byte
    {
        Spawned = 0,
        CapacityUnavailable = 1,
        InvariantViolation = 2,
    }

    public const int RespawnDelayTicks = 120;
    public const ulong ConceptDemoWorldSeed = 0x554C_5452_4153_494DUL;

    private readonly WorldId _worldId;
    private readonly GameContent _content;
    private readonly ulong _worldSeed;
    private readonly ComponentLookup<GameWeaponInstance> _weapon;
    private readonly ComponentLookup<GameSpawnedItem> _origin;
    private readonly ComponentLookup<GameItemSpawnerState> _spawner;
    private readonly ComponentLookup<InventoryItem> _inventoryItem;
    private readonly Entity[] _items;
    private readonly Entity[] _spawners;
    private readonly GameWeaponInstance[] _describedWeapons;
    private readonly InspectionPropertySet?[] _inspectionProperties;
    private int _itemCount;
    private int _spawnerCount;

    public GameItemGenerationState(EcsWorld world, GameContent content, ulong worldSeed = ConceptDemoWorldSeed)
    {
        ArgumentNullException.ThrowIfNull(world);
        _content = content ?? throw new ArgumentNullException(nameof(content));
        if (content.Prefabs.WorldId != world.Id)
            throw new ArgumentException("Item content belongs to another ECS world.", nameof(content));
        _worldId = world.Id;
        _worldSeed = worldSeed;
        _weapon = world.PrepareLookup<GameWeaponInstance>();
        _origin = world.PrepareLookup<GameSpawnedItem>();
        _spawner = world.PrepareLookup<GameItemSpawnerState>();
        _inventoryItem = world.PrepareLookup<InventoryItem>();
        _items = new Entity[world.Options.EntityCapacity.MaximumEntities];
        _spawners = new Entity[world.Options.EntityCapacity.MaximumEntities];
        _describedWeapons = new GameWeaponInstance[_items.Length];
        _inspectionProperties = new InspectionPropertySet?[_items.Length];
    }

    public int ItemCount => _itemCount;
    public int SpawnerCount => _spawnerCount;
    public long ReservedBytes
    {
        get
        {
            long propertyBytes = 0;
            for (int index = 0; index < _inspectionProperties.Length; index++)
                if (_inspectionProperties[index] is { } value)
                    propertyBytes = checked(propertyBytes + 24L + value.Count * 40L);
            return checked(
                (long)(_items.Length + _spawners.Length) * sizeof(ulong) +
                (long)_describedWeapons.Length * 16 +
                (long)_inspectionProperties.Length * IntPtr.Size +
                propertyBytes);
        }
    }

    public void Rebuild(EcsWorld world)
    {
        EnsureWorld(world);
        _itemCount = 0;
        _spawnerCount = 0;
        Array.Clear(_describedWeapons);
        Array.Clear(_inspectionProperties);
        int maximum = world.Options.EntityCapacity.MaximumEntities;
        for (uint index = 0; index < unchecked((uint)maximum); index++)
        {
            if (!world.TryGetEntity(index, out Entity entity))
                continue;
            if (world.TryRead(entity, _weapon, out GameWeaponInstance weapon))
            {
                if (!weapon.IsCanonical() ||
                    !world.TryRead(entity, _origin, out GameSpawnedItem origin) ||
                    origin.Spawner.IsValid && !world.IsAlive(origin.Spawner) ||
                    !world.TryRead(entity, _inventoryItem, out _))
                    throw new InvalidOperationException("A persisted weapon instance is noncanonical.");
                TrackItem(entity, in weapon);
            }
            if (world.TryRead(entity, _spawner, out GameItemSpawnerState spawner))
            {
                if (spawner.RemainingRespawnTicks < 0 ||
                    spawner.RemainingRespawnTicks > RespawnDelayTicks ||
                    spawner.ActiveItem.IsValid && !world.IsAlive(spawner.ActiveItem))
                    throw new InvalidOperationException("A persisted item spawner is noncanonical.");
                _spawners[_spawnerCount++] = entity;
            }
        }

        for (int index = 0; index < _spawnerCount; index++)
        {
            Entity entity = _spawners[index];
            if (!world.TryRead(entity, _spawner, out GameItemSpawnerState state) ||
                !state.ActiveItem.IsValid)
                continue;
            if (!world.TryRead(state.ActiveItem, _origin, out GameSpawnedItem origin) ||
                origin.Spawner != entity ||
                !world.TryRead(state.ActiveItem, _weapon, out GameWeaponInstance activeWeapon) ||
                !activeWeapon.IsCanonical())
                throw new InvalidOperationException(
                    "A persisted item spawner does not own its declared active item.");
        }
    }

    public bool TryCreateIronSwordSpawner(
        EcsWorld world,
        GameWorldState worldState,
        in WorldPosition position,
        out Entity spawner,
        out Entity sword)
    {
        EnsureWorld(world);
        ArgumentNullException.ThrowIfNull(worldState);
        spawner = Entity.Null;
        sword = Entity.Null;
        if (!world.TryInstantiate(_content.IronSwordSpawner, out spawner))
            return false;
        if (!worldState.TrySetPosition(world, spawner, position))
        {
            DestroyOrThrow(world, spawner, "spawner position initialization");
            spawner = Entity.Null;
            return false;
        }
        _ = world.GetOrAssignPersistentId(spawner);
        Track(_spawners, ref _spawnerCount, spawner);
        if (TrySpawn(world, worldState, spawner, out sword) == SpawnResult.Spawned)
            return true;
        DestroyOrThrow(world, spawner, "initial sword spawn rollback");
        _spawnerCount--;
        spawner = Entity.Null;
        return false;
    }

    public GameWorldActionStatus TryPickup(
        EcsWorld world,
        GameWorldState worldState,
        InventoryGenerationState inventory,
        Entity actor,
        Entity item,
        Entity destination)
    {
        EnsureWorld(world);
        ArgumentNullException.ThrowIfNull(worldState);
        ArgumentNullException.ThrowIfNull(inventory);
        if (!worldState.TryReadPosition(world, actor, out WorldPosition actorPosition))
            return GameWorldActionStatus.MissingActor;
        if (!worldState.TryReadPosition(world, item, out WorldPosition itemPosition) ||
            !world.TryRead(item, _weapon, out GameWeaponInstance weapon) || !weapon.IsCanonical() ||
            !worldState.TryReadPublic(world, item, out WorldPublicDescriptor descriptor) ||
            descriptor.PrefabId != GameWorldPublicPrefabIds.IronSword ||
            (descriptor.Flags & (WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem)) !=
                (WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem))
            return GameWorldActionStatus.MissingTarget;
        if (!WithinInteractionRange(actorPosition, itemPosition))
            return GameWorldActionStatus.OutOfRange;

        InventoryMutationResult taken = inventory.TakeWorldItem(world, item, destination);
        if (!taken.Accepted)
            return MapInventoryFailure(taken.Status);
        if (!worldState.TrySetPublic(world, item, descriptor with { Flags = WorldPublicStateFlags.None }))
            throw new InvalidOperationException("A picked-up item lost its public projection component.");
        MarkSpawnerVacant(world, item);
        return GameWorldActionStatus.Accepted;
    }

    public GameWorldActionStatus TryDrop(
        EcsWorld world,
        GameWorldState worldState,
        InventoryGenerationState inventory,
        Entity actor,
        Entity item)
    {
        EnsureWorld(world);
        ArgumentNullException.ThrowIfNull(worldState);
        ArgumentNullException.ThrowIfNull(inventory);
        if (!worldState.TryReadPosition(world, actor, out WorldPosition actorPosition) ||
            !worldState.TryReadPublic(world, actor, out WorldPublicDescriptor actorPublic))
            return GameWorldActionStatus.MissingActor;
        if (!world.TryRead(item, _weapon, out GameWeaponInstance weapon) || !weapon.IsCanonical() ||
            !worldState.TryReadPublic(world, item, out WorldPublicDescriptor itemPublic))
            return GameWorldActionStatus.MissingTarget;

        InventoryMutationResult dropped = inventory.DropToWorld(world, item, actor);
        if (!dropped.Accepted)
            return MapInventoryFailure(dropped.Status);

        WorldPosition dropPosition = ResolveDropPosition(worldState, actorPosition);
        if (!worldState.TrySetPosition(world, item, dropPosition) ||
            !worldState.TrySetPublic(world, item, itemPublic with
            {
                Orientation = actorPublic.Orientation,
                Flags = WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem,
            }))
            throw new InvalidOperationException("A dropped item could not publish its world state.");
        return GameWorldActionStatus.Accepted;
    }

    public GameWorldActionStatus TryUsePrimary(
        EcsWorld world,
        InventoryGenerationState inventory,
        Entity actor)
    {
        EnsureWorld(world);
        ArgumentNullException.ThrowIfNull(inventory);
        return world.IsAlive(actor) &&
            inventory.Index.TryGetEquipped(actor, InventoryEquipSlot.MainHand, out Entity item) &&
            world.TryRead(item, _weapon, out GameWeaponInstance weapon) && weapon.IsCanonical()
                ? GameWorldActionStatus.Accepted
                : GameWorldActionStatus.MissingTarget;
    }

    public bool TryReadWeapon(EcsWorld world, Entity item, out GameWeaponInstance weapon)
    {
        EnsureWorld(world);
        return world.TryRead(item, _weapon, out weapon) && weapon.IsCanonical();
    }

    public bool TryReadSpawner(EcsWorld world, Entity spawner, out GameItemSpawnerState state)
    {
        EnsureWorld(world);
        return world.TryRead(spawner, _spawner, out state) &&
            state.RemainingRespawnTicks is >= 0 and <= RespawnDelayTicks &&
            (!state.ActiveItem.IsValid || world.IsAlive(state.ActiveItem));
    }

    public bool TryGetInspectionProperties(
        EcsWorld world,
        Entity item,
        out InspectionPropertySet? properties)
    {
        if (TryReadWeapon(world, item, out GameWeaponInstance weapon))
        {
            int slot = FindItem(item);
            if (slot >= 0)
            {
                if (_inspectionProperties[slot] is null ||
                    !WeaponsEqual(in _describedWeapons[slot], in weapon))
                {
                    _describedWeapons[slot] = weapon;
                    _inspectionProperties[slot] = BuildInspectionProperties(in weapon);
                }
                properties = _inspectionProperties[slot]!;
                return true;
            }
            properties = BuildInspectionProperties(in weapon);
            return true;
        }
        properties = null;
        return false;
    }

    public static InspectionPropertySet BuildInspectionProperties(in GameWeaponInstance weapon)
    {
        if (!weapon.IsCanonical())
            throw new ArgumentException("Weapon properties must be canonical.", nameof(weapon));
        if (!InspectionPropertyValue.TryToken(
                weapon.Element switch
                {
                    GameWeaponElement.None => "none",
                    GameWeaponElement.Fire => "fire",
                    GameWeaponElement.Cold => "cold",
                    GameWeaponElement.Lightning => "lightning",
                    _ => null,
                }, out InspectionPropertyValue element) ||
            !InspectionPropertySet.TryCreate(
                [
                    // Contributor order is deliberately unrelated to presentation order.
                    new(InspectionPropertyIds.Durability,
                        InspectionPropertyValue.Fraction(
                            weapon.CurrentDurability, weapon.MaximumDurability)),
                    new(InspectionPropertyIds.Quality,
                        InspectionPropertyValue.Integer(weapon.Quality)),
                    new(InspectionPropertyIds.Damage,
                        InspectionPropertyValue.IntegerRange(
                            weapon.DamageMinimum, weapon.DamageMaximum)),
                    new(InspectionPropertyIds.Element, element),
                ],
                out InspectionPropertySet? properties))
            throw new InvalidOperationException("A canonical weapon produced invalid inspection properties.");
        return properties!;
    }

    public int CopyPublicWeaponPresentations(
        EcsWorld world,
        GameWorldState worldState,
        Span<GamePublicWeaponPresentation> destination)
    {
        EnsureWorld(world);
        ArgumentNullException.ThrowIfNull(worldState);
        int count = 0;
        for (int slot = 0; slot < _itemCount; slot++)
        {
            Entity item = _items[slot];
            if (!world.IsAlive(item) ||
                !worldState.TryReadPublic(world, item, out WorldPublicDescriptor descriptor) ||
                (descriptor.Flags &
                    (WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem)) !=
                    (WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem) ||
                !worldState.TryReadPosition(world, item, out WorldPosition position) ||
                !worldState.Publications.TryGetPublicEntityId(item, out WorldPublicEntityId id) ||
                !TryReadWeapon(world, item, out GameWeaponInstance weapon))
                continue;
            if (count == destination.Length) return -1;
            if (_inspectionProperties[slot] is null ||
                !WeaponsEqual(in _describedWeapons[slot], in weapon))
            {
                _describedWeapons[slot] = weapon;
                _inspectionProperties[slot] = BuildInspectionProperties(in weapon);
            }
            destination[count++] = new GamePublicWeaponPresentation(
                id,
                position,
                descriptor.Flags,
                weapon,
                "Iron Sword",
                _inspectionProperties[slot]!);
        }
        return count;
    }

    public void CommitAfterEcsStep(
        EcsWorld world,
        GameWorldState worldState,
        InventoryGenerationState inventory)
    {
        EnsureWorld(world);
        for (int index = 0; index < _spawnerCount; index++)
        {
            Entity entity = _spawners[index];
            if (!world.TryRead(entity, _spawner, out GameItemSpawnerState state))
                continue;
            if (state.ActiveItem.IsValid)
                continue;
            if (state.RemainingRespawnTicks > 0)
            {
                state.RemainingRespawnTicks--;
                if (!world.TryWrite(entity, _spawner, state))
                    throw new InvalidOperationException("A live item spawner rejected its countdown update.");
            }
            if (state.RemainingRespawnTicks == 0 && state.NextSpawnOrdinal != uint.MaxValue &&
                TrySpawn(world, worldState, entity, out _) == SpawnResult.InvariantViolation)
                throw new InvalidOperationException("A due item spawner entered an invalid state.");
        }

        for (int index = 0; index < _itemCount; index++)
        {
            Entity item = _items[index];
            if (!world.TryRead(item, _inventoryItem, out InventoryItem location) ||
                !worldState.TryReadPublic(world, item, out WorldPublicDescriptor descriptor))
                continue;
            bool equipped = location.EquipSlot == InventoryEquipSlot.MainHand;
            bool worldItem = !location.Parent.IsValid;
            WorldPublicStateFlags flags = worldItem
                ? WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem
                : equipped
                    ? WorldPublicStateFlags.Visible
                    : WorldPublicStateFlags.None;
            if (equipped && worldState.TryReadPosition(world, location.Parent, out WorldPosition ownerPosition) &&
                worldState.TryReadPublic(world, location.Parent, out WorldPublicDescriptor ownerPublic))
            {
                if ((!worldState.TryReadPosition(world, item, out WorldPosition itemPosition) ||
                        itemPosition != ownerPosition) &&
                    !worldState.TrySetPosition(world, item, ownerPosition))
                    throw new InvalidOperationException("An equipped item could not follow its authoritative owner.");
                descriptor = descriptor with { Orientation = ownerPublic.Orientation };
            }
            WorldPublicDescriptor desired = descriptor with { Flags = flags };
            if (worldState.TryReadPublic(world, item, out WorldPublicDescriptor current) &&
                current != desired && !worldState.TrySetPublic(world, item, desired))
                throw new InvalidOperationException("An item could not synchronize its public projection.");
        }
    }

    private SpawnResult TrySpawn(
        EcsWorld world,
        GameWorldState worldState,
        Entity spawner,
        out Entity item)
    {
        item = Entity.Null;
        if (!world.TryRead(spawner, _spawner, out GameItemSpawnerState state) ||
            state.ActiveItem.IsValid || state.NextSpawnOrdinal == uint.MaxValue ||
            !worldState.TryReadPosition(world, spawner, out WorldPosition position) ||
            !world.TryGetPersistentId(spawner, out PersistentEntityId spawnerId))
            return SpawnResult.InvariantViolation;
        if (_itemCount >= _items.Length ||
            !world.TryInstantiate(_content.IronSword.Handle, out item))
            return SpawnResult.CapacityUnavailable;

        uint ordinal = state.NextSpawnOrdinal;
        GameWeaponInstance rolled = Roll(spawnerId, ordinal);
        if (!world.TryWrite(item, _weapon, rolled) ||
            !world.TryWrite(item, _origin, new GameSpawnedItem { Spawner = spawner }) ||
            !worldState.TrySetPosition(world, item, position))
        {
            DestroyOrThrow(world, item, "rolled item initialization");
            item = Entity.Null;
            return SpawnResult.InvariantViolation;
        }
        _ = world.GetOrAssignPersistentId(item);
        state.ActiveItem = item;
        state.NextSpawnOrdinal++;
        state.RemainingRespawnTicks = 0;
        if (!world.TryWrite(spawner, _spawner, state))
        {
            DestroyOrThrow(world, item, "spawner publication rollback");
            item = Entity.Null;
            return SpawnResult.InvariantViolation;
        }
        TrackItem(item, in rolled);
        return SpawnResult.Spawned;
    }

    private GameWeaponInstance Roll(PersistentEntityId spawner, uint ordinal)
    {
        GameWeaponSpawnRecipe recipe = GameWeaponSpawnRecipe.IronSword;
        if (!recipe.IsValid)
            throw new InvalidOperationException("The Iron Sword spawn recipe is invalid.");
        ulong ordinalSalt = unchecked((ulong)ordinal * 0x9E37_79B9_7F4A_7C15UL);
        ulong state = Mix(_worldSeed ^ spawner.Value ^ ordinalSalt);
        ushort minimum = checked((ushort)NextRange(
            ref state, recipe.DamageMinimumLow, recipe.DamageMinimumHigh));
        ushort maximum = checked((ushort)NextRange(
            ref state, Math.Max(minimum, recipe.DamageMaximumLow), recipe.DamageMaximumHigh));
        ushort durability = checked((ushort)NextRange(
            ref state, recipe.DurabilityLow, recipe.DurabilityHigh));
        return new GameWeaponInstance
        {
            DamageMinimum = minimum,
            DamageMaximum = maximum,
            MaximumDurability = durability,
            CurrentDurability = durability,
            Quality = checked((byte)NextRange(ref state, 1, 100)),
            Element = (GameWeaponElement)NextRange(ref state, 0, 3),
            SpawnOrdinal = ordinal,
        };
    }

    private void MarkSpawnerVacant(EcsWorld world, Entity item)
    {
        if (!world.TryRead(item, _origin, out GameSpawnedItem origin) ||
            !world.TryRead(origin.Spawner, _spawner, out GameItemSpawnerState state) ||
            state.ActiveItem != item)
            return;
        state.ActiveItem = Entity.Null;
        state.RemainingRespawnTicks = RespawnDelayTicks;
        if (!world.TryWrite(origin.Spawner, _spawner, state))
            throw new InvalidOperationException("A picked-up spawned item could not advance its spawner lineage.");
    }

    private static WorldPosition ResolveDropPosition(
        GameWorldState worldState,
        in WorldPosition actorPosition)
    {
        if (!WorldPosition.TryFromGlobalMillimeters(
                actorPosition.GlobalXMillimeters + 1_000,
                actorPosition.YMillimeters,
                actorPosition.GlobalZMillimeters,
                out WorldPosition candidate) || worldState.PreparedWorld is null)
            return actorPosition;
        WorldTerrainColumnQueryStatus query = worldState.PreparedWorld.TryFindNearestSupport(
            candidate, actorPosition.YMillimeters, out WorldTerrainColumnSurface support);
        return query == WorldTerrainColumnQueryStatus.Complete && support.IsSupport &&
            WorldPosition.TryFromGlobalMillimeters(
                candidate.GlobalXMillimeters,
                support.HeightMillimeters,
                candidate.GlobalZMillimeters,
                out WorldPosition grounded)
            ? grounded
            : actorPosition;
    }

    private static GameWorldActionStatus MapInventoryFailure(InventoryMutationStatus status) => status switch
    {
        InventoryMutationStatus.MissingContainer or InventoryMutationStatus.CapacityExceeded =>
            GameWorldActionStatus.UnauthorizedContainer,
        InventoryMutationStatus.UnauthorizedContainer => GameWorldActionStatus.UnauthorizedContainer,
        InventoryMutationStatus.MissingItem => GameWorldActionStatus.MissingTarget,
        _ => GameWorldActionStatus.InvariantViolation,
    };

    private static bool WithinInteractionRange(in WorldPosition first, in WorldPosition second)
    {
        Int128 dx = (Int128)first.GlobalXMillimeters - second.GlobalXMillimeters;
        Int128 dy = (Int128)first.YMillimeters - second.YMillimeters;
        Int128 dz = (Int128)first.GlobalZMillimeters - second.GlobalZMillimeters;
        Int128 range = PickupRangeMillimeters;
        return dx * dx + dy * dy + dz * dz <= range * range;
    }

    private static void Track(Entity[] destination, ref int count, Entity entity)
    {
        if (count >= destination.Length)
            throw new InvalidOperationException("Item lifecycle tracking exceeded ECS entity capacity.");
        destination[count++] = entity;
    }

    private void TrackItem(Entity entity, in GameWeaponInstance weapon)
    {
        if (_itemCount >= _items.Length)
            throw new InvalidOperationException("Item lifecycle tracking exceeded ECS entity capacity.");
        int slot = _itemCount++;
        _items[slot] = entity;
        _describedWeapons[slot] = weapon;
        _inspectionProperties[slot] = BuildInspectionProperties(in weapon);
    }

    private int FindItem(Entity item)
    {
        for (int slot = 0; slot < _itemCount; slot++)
            if (_items[slot] == item) return slot;
        return -1;
    }

    private static bool WeaponsEqual(
        in GameWeaponInstance left,
        in GameWeaponInstance right) =>
        left.DamageMinimum == right.DamageMinimum &&
        left.DamageMaximum == right.DamageMaximum &&
        left.MaximumDurability == right.MaximumDurability &&
        left.CurrentDurability == right.CurrentDurability &&
        left.Quality == right.Quality &&
        left.Element == right.Element &&
        left.SpawnOrdinal == right.SpawnOrdinal;

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += 0x9E37_79B9_7F4A_7C15UL;
            value = (value ^ value >> 30) * 0xBF58_476D_1CE4_E5B9UL;
            value = (value ^ value >> 27) * 0x94D0_49BB_1331_11EBUL;
            return value ^ value >> 31;
        }
    }

    private static int NextRange(ref ulong state, int minimum, int maximum)
    {
        state = Mix(state);
        uint width = checked((uint)(maximum - minimum + 1));
        return checked(minimum + (int)(state % width));
    }

    private static void DestroyOrThrow(EcsWorld world, Entity entity, string operation)
    {
        if (!world.TryDestroy(entity))
            throw new InvalidOperationException($"A failed {operation} could not roll back its entity.");
    }

    private void EnsureWorld(EcsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Id != _worldId)
            throw new ArgumentException("Item instance state belongs to another ECS world.", nameof(world));
    }
}
