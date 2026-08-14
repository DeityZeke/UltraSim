using UltraSim.ECS;
using UltraSim.Game.Inventory;

namespace UltraSim.Game.Composition;

public static class GamePrefabKeys
{
    public static readonly PrefabKey Character = new("game.character.default");
    public static readonly PrefabKey Backpack = new("game.container.backpack");
    public static readonly PrefabKey WorldChest = new("game.container.world-chest");
    public static readonly PrefabKey Bread = new("game.item.bread");
    public static readonly PrefabKey IronSword = new("game.item.iron-sword");
    public static readonly PrefabKey Apple = new("game.item.apple");
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
        InventoryItemDefinition apple)
    {
        Prefabs = prefabs;
        Character = character;
        Backpack = backpack.Handle;
        WorldChest = worldChest.Handle;
        Bread = bread;
        IronSword = ironSword;
        Apple = apple;
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
        character.DisplayName("Character");
        _ = InventoryContent.DefineContainer(content, GamePrefabKeys.Backpack, "Backpack", 8, hasOwner: true);
        _ = InventoryContent.DefineContainer(content, GamePrefabKeys.WorldChest, "World Chest", 64, hasOwner: false);
        _ = InventoryContent.DefineItem(content, GamePrefabKeys.Bread, "Bread", maximumQuantity: 20);
        _ = InventoryContent.DefineItem(
            content,
            GamePrefabKeys.IronSword,
            "Iron Sword",
            maximumQuantity: 1,
            InventoryEquipSlots.MainHand);
        // Third item deliberately proves content expansion without changing
        // InventoryGenerationState or InventoryIndex.
        _ = InventoryContent.DefineItem(content, GamePrefabKeys.Apple, "Apple", maximumQuantity: 10);

        CompiledPrefabCatalog compiled = content.Compile(world, options);
        InventoryItemDefinition bread = BindItem(compiled, GamePrefabKeys.Bread, 20, InventoryEquipSlots.None);
        InventoryItemDefinition sword = BindItem(compiled, GamePrefabKeys.IronSword, 1, InventoryEquipSlots.MainHand);
        InventoryItemDefinition apple = BindItem(compiled, GamePrefabKeys.Apple, 10, InventoryEquipSlots.None);
        return new GameContent(
            compiled,
            compiled.GetHandle(GamePrefabKeys.Character),
            BindContainer(compiled, GamePrefabKeys.Backpack, hasOwner: true),
            BindContainer(compiled, GamePrefabKeys.WorldChest, hasOwner: false),
            bread,
            sword,
            apple);
    }

    private static InventoryContainerDefinition BindContainer(
        CompiledPrefabCatalog catalog,
        PrefabKey key,
        bool hasOwner)
    {
        PrefabHandle handle = catalog.GetHandle(key);
        CompiledPrefabInfo info = catalog.GetInfo(handle);
        return new InventoryContainerDefinition(
            handle,
            key,
            info.DisplayName ?? key.Value,
            hasOwner);
    }

    private static InventoryItemDefinition BindItem(
        CompiledPrefabCatalog catalog,
        PrefabKey key,
        int maximumQuantity,
        InventoryEquipSlots slots)
    {
        PrefabHandle handle = catalog.GetHandle(key);
        CompiledPrefabInfo info = catalog.GetInfo(handle);
        return new InventoryItemDefinition(
            handle,
            key,
            info.DisplayName ?? key.Value,
            maximumQuantity,
            slots);
    }
}
