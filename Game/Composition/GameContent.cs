using UltraSim.ECS;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;

namespace UltraSim.Game.Composition;

/// <summary>Stable canonical identities of the concrete game's compiled content fixtures.</summary>
public static class GamePrefabKeys
{
    public static readonly PrefabKey Character = new("game.character.default");
    public static readonly PrefabKey CharacterMale = new("game.character.human-male");
    public static readonly PrefabKey CharacterDormant = new("game.character.dormant");
    public static readonly PrefabKey Backpack = new("game.container.backpack");
    public static readonly PrefabKey GridBackpack = new("game.container.grid-backpack");
    public static readonly PrefabKey WorldChest = new("game.container.world-chest");
    public static readonly PrefabKey Bread = new("game.item.bread");
    public static readonly PrefabKey IronSword = new("game.item.iron-sword");
    public static readonly PrefabKey Apple = new("game.item.apple");
    public static readonly PrefabKey CabinDoor = new("game.world.cabin-door");
    public static readonly PrefabKey SkyTelepad = new("game.world.sky-telepad");
    public static readonly PrefabKey FarGate = new("game.world.far-gate");
    public static readonly PrefabKey InformationalSign = new("game.world.informational-sign");
    public static readonly PrefabKey WoodlandAnimal = new("game.world.woodland-animal");
}

/// <summary>Stable compiled inventory policy identities owned by this game content set.</summary>
public static class GameInventoryRuleProfileIds
{
    public static readonly InventoryRuleProfileId Backpack = new(1);
    public static readonly InventoryRuleProfileId WorldStorage = new(2);
    public static readonly InventoryRuleProfileId GridBackpack = new(3);
}

/// <summary>World-bound compiled content owned by the concrete game composition.</summary>
public sealed class GameContent : IInventoryContentCatalog
{
    private readonly Dictionary<PrefabHandle, InventoryItemDefinition> _itemsByHandle;
    private readonly Dictionary<PrefabKey, InventoryItemDefinition> _itemsByKey;
    private readonly Dictionary<PrefabHandle, InventoryContainerDefinition> _containersByHandle;
    private readonly Dictionary<PrefabKey, InventoryContainerDefinition> _containersByKey;
    private readonly Dictionary<ArchetypeHandle, GridPlacementTransitions> _gridPlacementTransitions;

    private GameContent(
        CompiledPrefabCatalog prefabs,
        InventoryRuleCatalog inventoryRules,
        PrefabHandle character,
        PrefabHandle characterMale,
        InventoryContainerDefinition backpack,
        InventoryContainerDefinition gridBackpack,
        InventoryContainerDefinition worldChest,
        InventoryItemDefinition bread,
        InventoryItemDefinition ironSword,
        InventoryItemDefinition apple,
        PrefabHandle cabinDoor,
        PrefabHandle skyTelepad,
        PrefabHandle farGate,
        PrefabHandle informationalSign,
        PrefabHandle woodlandAnimal,
        WorldContentBinding world,
        Dictionary<ArchetypeHandle, GridPlacementTransitions> gridPlacementTransitions)
    {
        ValidateContainerRules(inventoryRules, backpack);
        ValidateContainerRules(inventoryRules, gridBackpack);
        ValidateContainerRules(inventoryRules, worldChest);
        Prefabs = prefabs;
        InventoryRules = inventoryRules;
        Character = character;
        CharacterMale = characterMale;
        Backpack = backpack.Handle;
        GridBackpack = gridBackpack.Handle;
        WorldChest = worldChest.Handle;
        Bread = bread;
        IronSword = ironSword;
        Apple = apple;
        CabinDoor = cabinDoor;
        SkyTelepad = skyTelepad;
        FarGate = farGate;
        InformationalSign = informationalSign;
        WoodlandAnimal = woodlandAnimal;
        World = world;
        _gridPlacementTransitions = gridPlacementTransitions;
        _itemsByHandle = new Dictionary<PrefabHandle, InventoryItemDefinition>(3)
        {
            [bread.Handle] = bread,
            [ironSword.Handle] = ironSword,
            [apple.Handle] = apple,
        };
        _itemsByKey = new Dictionary<PrefabKey, InventoryItemDefinition>(3)
        {
            [bread.Key] = bread,
            [ironSword.Key] = ironSword,
            [apple.Key] = apple,
        };
        _containersByHandle = new Dictionary<PrefabHandle, InventoryContainerDefinition>(3)
        {
            [backpack.Handle] = backpack,
            [gridBackpack.Handle] = gridBackpack,
            [worldChest.Handle] = worldChest,
        };
        _containersByKey = new Dictionary<PrefabKey, InventoryContainerDefinition>(3)
        {
            [backpack.Key] = backpack,
            [gridBackpack.Key] = gridBackpack,
            [worldChest.Key] = worldChest,
        };
    }

    public CompiledPrefabCatalog Prefabs { get; }
    public InventoryRuleCatalog InventoryRules { get; }
    public PrefabHandle Character { get; }
    public PrefabHandle CharacterMale { get; }
    public PrefabHandle Backpack { get; }
    public PrefabHandle GridBackpack { get; }
    public PrefabHandle WorldChest { get; }
    public InventoryItemDefinition Bread { get; }
    public InventoryItemDefinition IronSword { get; }
    public InventoryItemDefinition Apple { get; }
    public PrefabHandle CabinDoor { get; }
    public PrefabHandle SkyTelepad { get; }
    public PrefabHandle FarGate { get; }
    public PrefabHandle InformationalSign { get; }
    public PrefabHandle WoodlandAnimal { get; }
    public WorldContentBinding World { get; }

    // Governance: bump this identity whenever the semantic meaning of any packed numeric world-content
    // ID changes incompatibly, even when the numeric value itself remains admitted and unchanged.
    public static WorldContentFingerprint WorldFingerprint { get; } =
        new(GameWorldContentProfile.ContentFingerprintLow,
            GameWorldContentProfile.ContentFingerprintHigh);

    public bool TryGetContainer(PrefabHandle handle, out InventoryContainerDefinition definition)
    {
        if (!handle.IsValid || handle.World != Prefabs.WorldId)
        {
            definition = default;
            return false;
        }
        return _containersByHandle.TryGetValue(handle, out definition);
    }

    public bool TryGetContainer(PrefabKey key, out InventoryContainerDefinition definition) =>
        _containersByKey.TryGetValue(key, out definition);

    public bool TryGetItem(PrefabHandle handle, out InventoryItemDefinition definition)
    {
        if (!handle.IsValid || handle.World != Prefabs.WorldId)
        {
            definition = default;
            return false;
        }
        return _itemsByHandle.TryGetValue(handle, out definition);
    }

    public bool TryGetItem(PrefabKey key, out InventoryItemDefinition definition) =>
        _itemsByKey.TryGetValue(key, out definition);

    public bool TryGetGridPlacementTransitions(
        PrefabHandle item,
        out ArchetypeTransition addPlacement,
        out ArchetypeTransition removePlacement)
    {
        if (item.IsValid && item.World == Prefabs.WorldId &&
            _gridPlacementTransitions.TryGetValue(item.TargetArchetype, out GridPlacementTransitions route))
        {
            addPlacement = route.Add;
            removePlacement = route.Remove;
            return true;
        }
        addPlacement = default;
        removePlacement = default;
        return false;
    }

    public static GameContent Compile(
        EcsWorld world,
        ContentCompilationOptions options,
        InventoryRuleSelection? inventoryRuleSelection = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var inventoryRules = new InventoryRuleCatalog(
            inventoryRuleSelection ?? InventoryRuleSelection.PerContainer,
            [
                new InventoryRuleProfile(
                    GameInventoryRuleProfileIds.Backpack,
                    InventoryPlacementMode.Unplaced,
                    new InventoryCapacityDimensions(MaximumItemCount: 256)),
                new InventoryRuleProfile(
                    GameInventoryRuleProfileIds.WorldStorage,
                    InventoryPlacementMode.Unplaced,
                    new InventoryCapacityDimensions(MaximumItemCount: 4_096)),
                new InventoryRuleProfile(
                    GameInventoryRuleProfileIds.GridBackpack,
                    InventoryPlacementMode.Grid,
                    new InventoryCapacityDimensions(
                        MaximumItemCount: 4_096,
                        MaximumGridWidth: 10,
                        MaximumGridHeight: 10),
                    InitialGridWidth: 5,
                    InitialGridHeight: 5),
            ]);
        var content = new ContentCatalogBuilder(world.Schema);
        PrefabBuilder character = content.Define(GamePrefabKeys.Character);
        character.With(new GameCharacter { Marker = 1 });
        character.With(new GameSimulationState { TickMarker = 0 });
        character.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        character.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        character.DisplayName("Character");
        PrefabBuilder characterMale = content.Define(GamePrefabKeys.CharacterMale);
        characterMale.With(new GameCharacter { Marker = 1 });
        characterMale.With(new GameSimulationState { TickMarker = 0 });
        characterMale.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        characterMale.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.CharacterMale, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        characterMale.DisplayName("Human Male");
        // The paired no-simulation shape is a real content route used when a
        // character's scheduled simulation membership is explicitly suspended.
        PrefabBuilder dormantCharacter = content.Define(GamePrefabKeys.CharacterDormant);
        dormantCharacter.With(new GameCharacter { Marker = 1 });
        dormantCharacter.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        dormantCharacter.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        dormantCharacter.DisplayName("Dormant Character");
        InventoryContainerAuthoring backpackSource = InventoryContent.DefineContainer(
            content,
            GamePrefabKeys.Backpack,
            "Backpack",
            8,
            GameInventoryRuleProfileIds.Backpack,
            hasOwner: true);
        InventoryContainerAuthoring gridBackpackSource = InventoryContent.DefineContainer(
            content,
            GamePrefabKeys.GridBackpack,
            "Grid Backpack",
            20,
            GameInventoryRuleProfileIds.GridBackpack,
            hasOwner: true);
        PrefabBuilder worldChest = content.Define(GamePrefabKeys.WorldChest);
        worldChest.With(new InventoryContainer { Capacity = 64 });
        worldChest.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        worldChest.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.WorldChest, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        worldChest.DisplayName("World Chest");
        var chestSource = new InventoryContainerAuthoring(
            GamePrefabKeys.WorldChest,
            "World Chest",
            64,
            GameInventoryRuleProfileIds.WorldStorage,
            HasOwner: false);
        InventoryItemAuthoring breadSource = InventoryContent.DefineItem(
            content, GamePrefabKeys.Bread, "Bread", maximumQuantity: 20);
        InventoryItemAuthoring swordSource = InventoryContent.DefineItem(
            content,
            GamePrefabKeys.IronSword,
            "Iron Sword",
            maximumQuantity: 1,
            InventoryEquipSlots.MainHand,
            new InventoryItemFootprint(1, 3));
        // Third item deliberately proves content expansion without changing
        // InventoryGenerationState or InventoryIndex.
        InventoryItemAuthoring appleSource = InventoryContent.DefineItem(
            content, GamePrefabKeys.Apple, "Apple", maximumQuantity: 10);

        PrefabBuilder cabinDoor = content.Define(GamePrefabKeys.CabinDoor);
        cabinDoor.With(new GameCabinDoorState { IsOpen = 0 });
        cabinDoor.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        cabinDoor.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.CabinDoor, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        cabinDoor.DisplayName("Cabin Door");

        // The destination is selected by concrete Game world content when this public marker is
        // used. The client may nominate the marker, but never supplies relocation coordinates.
        PrefabBuilder skyTelepad = content.Define(GamePrefabKeys.SkyTelepad);
        skyTelepad.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        skyTelepad.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.SkyTelepad, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        skyTelepad.DisplayName("Sky Island Telepad");

        // The two Concept Demo instances share one public kind. Concrete Game world content maps
        // each admitted marker position to its server-owned reciprocal destination.
        PrefabBuilder farGate = content.Define(GamePrefabKeys.FarGate);
        farGate.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        farGate.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.FarGate, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        farGate.DisplayName("Far Gate");

        // Sign copy remains Game-owned content. The public descriptor exposes only the finite
        // interactable kind; selecting the object never discloses its informational body.
        PrefabBuilder informationalSign = content.Define(GamePrefabKeys.InformationalSign);
        informationalSign.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        informationalSign.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.InformationalSign,
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible)));
        informationalSign.DisplayName("Sign");

        PrefabBuilder woodlandAnimal = content.Define(GamePrefabKeys.WoodlandAnimal);
        woodlandAnimal.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        woodlandAnimal.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.WoodlandAnimal, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        woodlandAnimal.DisplayName("Woodland Animal");

        CompiledPrefabCatalog compiled = content.Compile(world, options);
        InventoryItemDefinition bread = BindItem(compiled, breadSource);
        InventoryItemDefinition sword = BindItem(compiled, swordSource);
        InventoryItemDefinition apple = BindItem(compiled, appleSource);
        Dictionary<ArchetypeHandle, GridPlacementTransitions> gridPlacementTransitions =
            PrepareGridPlacementTransitions(
                world,
                options,
                [bread, sword, apple]);
        return new GameContent(
            compiled,
            inventoryRules,
            compiled.GetHandle(GamePrefabKeys.Character),
            compiled.GetHandle(GamePrefabKeys.CharacterMale),
            BindContainer(compiled, backpackSource),
            BindContainer(compiled, gridBackpackSource),
            BindContainer(compiled, chestSource),
            bread,
            sword,
            apple,
            compiled.GetHandle(GamePrefabKeys.CabinDoor),
            compiled.GetHandle(GamePrefabKeys.SkyTelepad),
            compiled.GetHandle(GamePrefabKeys.FarGate),
            compiled.GetHandle(GamePrefabKeys.InformationalSign),
            compiled.GetHandle(GamePrefabKeys.WoodlandAnimal),
            new WorldContentBinding(WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]),
            gridPlacementTransitions);
    }

    private static InventoryContainerDefinition BindContainer(
        CompiledPrefabCatalog catalog,
        InventoryContainerAuthoring source)
    {
        PrefabHandle handle = catalog.GetHandle(source.Key);
        CompiledPrefabInfo info = catalog.GetInfo(handle);
        return new InventoryContainerDefinition(
            handle,
            source.Key,
            info.DisplayName ?? source.DisplayName,
            source.DefaultCapacity,
            source.RuleProfile,
            source.HasOwner);
    }

    private static InventoryItemDefinition BindItem(
        CompiledPrefabCatalog catalog,
        InventoryItemAuthoring source)
    {
        PrefabHandle handle = catalog.GetHandle(source.Key);
        CompiledPrefabInfo info = catalog.GetInfo(handle);
        return new InventoryItemDefinition(
            handle,
            source.Key,
            info.DisplayName ?? source.DisplayName,
            source.MaximumQuantity,
            source.AllowedEquipSlots,
            source.Footprint);
    }

    private static Dictionary<ArchetypeHandle, GridPlacementTransitions> PrepareGridPlacementTransitions(
        EcsWorld world,
        ContentCompilationOptions options,
        IReadOnlyList<InventoryItemDefinition> items)
    {
        var result = new Dictionary<ArchetypeHandle, GridPlacementTransitions>();
        ComponentId placementId = GameSchema.GetComponentId<InventoryGridPlacement>();
        Span<ComponentId> sourceSignature = stackalloc ComponentId[world.Schema.ComponentCount];
        foreach (InventoryItemDefinition item in items)
        {
            ArchetypeHandle source = item.Handle.TargetArchetype;
            if (result.ContainsKey(source))
                continue;
            int sourceCount = world.CopyArchetypeSignature(source, sourceSignature);
            var signature = new ComponentId[sourceCount + 1];
            sourceSignature[..sourceCount].CopyTo(signature);
            signature[sourceCount] = placementId;
            Array.Sort(signature);
            ArchetypeHandle destination = world.RegisterArchetype(
                signature, options.Layout, options.MaximumChunksPerNewArchetype);
            result.Add(source, new GridPlacementTransitions(
                world.PrepareTransition(source, destination),
                world.PrepareTransition(destination, source)));
        }
        return result;
    }

    private readonly record struct GridPlacementTransitions(
        ArchetypeTransition Add,
        ArchetypeTransition Remove);

    private static void ValidateContainerRules(
        InventoryRuleCatalog rules,
        in InventoryContainerDefinition definition)
    {
        if (!rules.TryResolve(
                definition.RuleProfile,
                definition.DefaultCapacity,
                out InventoryEffectiveRules effective) ||
            !effective.IsOperational)
        {
            throw new InvalidOperationException(
                $"Container content '{definition.Key.Value}' selects unsupported or invalid inventory rules.");
        }
    }
}
