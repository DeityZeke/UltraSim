using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Stable content keys for the minimal S0 inventory fixture.</summary>
public static class InventoryPrefabKeys
{
    public static readonly PrefabKey Character = new("game.character.inventory");
    public static readonly PrefabKey Backpack = new("game.inventory.backpack");
    public static readonly PrefabKey Bread = new("game.item.bread");
    public static readonly PrefabKey IronSword = new("game.item.iron-sword");
}

/// <summary>World-bound handles for the compiled inventory content set.</summary>
public readonly record struct InventoryPrefabHandles(
    PrefabHandle Character,
    PrefabHandle Backpack,
    PrefabHandle Bread,
    PrefabHandle IronSword)
{
    public static InventoryPrefabHandles Bind(CompiledPrefabCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return new InventoryPrefabHandles(
            catalog.GetHandle(InventoryPrefabKeys.Character),
            catalog.GetHandle(InventoryPrefabKeys.Backpack),
            catalog.GetHandle(InventoryPrefabKeys.Bread),
            catalog.GetHandle(InventoryPrefabKeys.IronSword));
    }
}

/// <summary>
/// Cold game-owned inventory prefab authoring. Callers may add additional
/// game components to the returned builders before compiling the catalog.
/// </summary>
public static class InventoryContent
{
    public static PrefabBuilder DefineCharacter(ContentCatalogBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        PrefabBuilder prefab = content.Define(InventoryPrefabKeys.Character);
        prefab.With(new InventoryCharacter { Marker = 1 });
        prefab.DisplayName("Character");
        return prefab;
    }

    public static PrefabBuilder DefineBackpack(ContentCatalogBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        PrefabBuilder prefab = content.Define(InventoryPrefabKeys.Backpack);
        prefab.With(new InventoryContainer { Owner = Entity.Null, Capacity = 8 });
        prefab.DisplayName("Backpack");
        return prefab;
    }

    public static PrefabBuilder DefineBread(ContentCatalogBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        PrefabBuilder prefab = content.Define(InventoryPrefabKeys.Bread);
        prefab.With(new InventoryItem
        {
            Parent = Entity.Null,
            Kind = InventoryItemKind.Bread,
            EquipSlot = InventoryEquipSlot.None,
            Quantity = 1,
        });
        prefab.DisplayName("Bread");
        return prefab;
    }

    public static PrefabBuilder DefineIronSword(ContentCatalogBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        PrefabBuilder prefab = content.Define(InventoryPrefabKeys.IronSword);
        prefab.With(new InventoryItem
        {
            Parent = Entity.Null,
            Kind = InventoryItemKind.IronSword,
            EquipSlot = InventoryEquipSlot.None,
            Quantity = 1,
        });
        prefab.DisplayName("Iron Sword");
        return prefab;
    }

    public static InventoryPrefabHandles CompileDefaults(
        EcsWorld world,
        ContentCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        var content = new ContentCatalogBuilder(world.Schema);
        _ = DefineCharacter(content);
        _ = DefineBackpack(content);
        _ = DefineBread(content);
        _ = DefineIronSword(content);
        return InventoryPrefabHandles.Bind(content.Compile(world, options));
    }
}
