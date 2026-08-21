using UltraSim.ECS;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;

namespace UltraSim.Game.Composition;

/// <summary>Stable canonical identities of the concrete game's compiled content fixtures.</summary>
public static class GamePrefabKeys
{
    public static readonly PrefabKey Character = new("game.character.default");
    public static readonly PrefabKey CharacterDormant = new("game.character.dormant");
    public static readonly PrefabKey Backpack = new("game.container.backpack");
    public static readonly PrefabKey WorldChest = new("game.container.world-chest");
    public static readonly PrefabKey Bread = new("game.item.bread");
    public static readonly PrefabKey IronSword = new("game.item.iron-sword");
    public static readonly PrefabKey Apple = new("game.item.apple");
}

/// <summary>Compact structural terrain identities admitted by the first game world content profile.</summary>
public static class GameTerrainMaterialIds
{
    public const ushort Water = 1;
    public const ushort Sand = 2;
    public const ushort Grass = 3;
    public const ushort Rock = 4;
    public const ushort Snow = 5;
}

/// <summary>Compact immutable-world definition identities admitted by the first game world profile.</summary>
public static class GameWorldStaticDefinitionIds
{
    public const uint Tree = 1;
    public const uint Rock = 2;
    public const uint CabinWall = 3;
    public const uint CabinRoof = 4;
    public const uint Shelf = 5;
    public const uint Book = 6;
}

/// <summary>Compact public-projection prefab identities admitted by the concrete game.</summary>
public static class GameWorldPublicPrefabIds
{
    public const uint Character = 1;
}

/// <summary>World-bound compiled content owned by the concrete game composition.</summary>
public sealed class GameContent : IInventoryContentCatalog
{
    private readonly Dictionary<PrefabHandle, InventoryItemDefinition> _itemsByHandle;
    private readonly Dictionary<PrefabKey, InventoryItemDefinition> _itemsByKey;
    private readonly Dictionary<PrefabHandle, InventoryContainerDefinition> _containersByHandle;
    private readonly Dictionary<PrefabKey, InventoryContainerDefinition> _containersByKey;

    private GameContent(
        CompiledPrefabCatalog prefabs,
        PrefabHandle character,
        InventoryContainerDefinition backpack,
        InventoryContainerDefinition worldChest,
        InventoryItemDefinition bread,
        InventoryItemDefinition ironSword,
        InventoryItemDefinition apple,
        WorldContentBinding world)
    {
        Prefabs = prefabs;
        Character = character;
        Backpack = backpack.Handle;
        WorldChest = worldChest.Handle;
        Bread = bread;
        IronSword = ironSword;
        Apple = apple;
        World = world;
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
        _containersByHandle = new Dictionary<PrefabHandle, InventoryContainerDefinition>(2)
        {
            [backpack.Handle] = backpack,
            [worldChest.Handle] = worldChest,
        };
        _containersByKey = new Dictionary<PrefabKey, InventoryContainerDefinition>(2)
        {
            [backpack.Key] = backpack,
            [worldChest.Key] = worldChest,
        };
    }

    public CompiledPrefabCatalog Prefabs { get; }
    public PrefabHandle Character { get; }
    public PrefabHandle Backpack { get; }
    public PrefabHandle WorldChest { get; }
    public InventoryItemDefinition Bread { get; }
    public InventoryItemDefinition IronSword { get; }
    public InventoryItemDefinition Apple { get; }
    public WorldContentBinding World { get; }

    // Governance: bump this identity whenever the semantic meaning of any packed numeric world-content
    // ID changes incompatibly, even when the numeric value itself remains admitted and unchanged.
    public static WorldContentFingerprint WorldFingerprint { get; } =
        new(0x3B8D_14CB_E652_EE21UL, 0xD241_8739_B74F_10A5UL);

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

    public static GameContent Compile(EcsWorld world, ContentCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        var content = new ContentCatalogBuilder(world.Schema);
        PrefabBuilder character = content.Define(GamePrefabKeys.Character);
        character.With(new GameCharacter { Marker = 1 });
        character.With(new GameSimulationState { TickMarker = 0 });
        character.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        character.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        character.DisplayName("Character");
        // The paired no-simulation shape is a real content route used when a
        // character's scheduled simulation membership is explicitly suspended.
        PrefabBuilder dormantCharacter = content.Define(GamePrefabKeys.CharacterDormant);
        dormantCharacter.With(new GameCharacter { Marker = 1 });
        dormantCharacter.With(new WorldPositionComponent(new WorldPosition(0, 0, 0, 0, 0)));
        dormantCharacter.With(new WorldPublicComponent(new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible)));
        dormantCharacter.DisplayName("Dormant Character");
        InventoryContainerAuthoring backpackSource = InventoryContent.DefineContainer(
            content, GamePrefabKeys.Backpack, "Backpack", 8, hasOwner: true);
        InventoryContainerAuthoring chestSource = InventoryContent.DefineContainer(
            content, GamePrefabKeys.WorldChest, "World Chest", 64, hasOwner: false);
        InventoryItemAuthoring breadSource = InventoryContent.DefineItem(
            content, GamePrefabKeys.Bread, "Bread", maximumQuantity: 20);
        InventoryItemAuthoring swordSource = InventoryContent.DefineItem(
            content,
            GamePrefabKeys.IronSword,
            "Iron Sword",
            maximumQuantity: 1,
            InventoryEquipSlots.MainHand);
        // Third item deliberately proves content expansion without changing
        // InventoryGenerationState or InventoryIndex.
        InventoryItemAuthoring appleSource = InventoryContent.DefineItem(
            content, GamePrefabKeys.Apple, "Apple", maximumQuantity: 10);

        CompiledPrefabCatalog compiled = content.Compile(world, options);
        InventoryItemDefinition bread = BindItem(compiled, breadSource);
        InventoryItemDefinition sword = BindItem(compiled, swordSource);
        InventoryItemDefinition apple = BindItem(compiled, appleSource);
        return new GameContent(
            compiled,
            compiled.GetHandle(GamePrefabKeys.Character),
            BindContainer(compiled, backpackSource),
            BindContainer(compiled, chestSource),
            bread,
            sword,
            apple,
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
            ]));
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
            source.AllowedEquipSlots);
    }
}
