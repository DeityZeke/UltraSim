using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Cold content descriptor for a compiled container prefab.</summary>
public readonly record struct InventoryContainerDefinition
{
    public InventoryContainerDefinition(
        PrefabHandle handle,
        PrefabKey key,
        string displayName,
        bool hasOwner)
    {
        if (!handle.IsValid)
            throw new ArgumentException("An inventory container definition requires a compiled prefab handle.", nameof(handle));
        if (!key.IsValid)
            throw new ArgumentException("An inventory container definition requires a stable prefab key.", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Handle = handle;
        Key = key;
        DisplayName = displayName;
        HasOwner = hasOwner;
    }

    public PrefabHandle Handle { get; }
    public PrefabKey Key { get; }
    public string DisplayName { get; }
    public bool HasOwner { get; }
}

/// <summary>
/// Cold content descriptor used to validate a requested item before runtime
/// instantiation. The actual item facts are also compiled into ECS components.
/// </summary>
public readonly record struct InventoryItemDefinition
{
    public InventoryItemDefinition(
        PrefabHandle handle,
        PrefabKey key,
        string displayName,
        int maximumQuantity,
        InventoryEquipSlots allowedEquipSlots)
    {
        if (!handle.IsValid)
            throw new ArgumentException("An inventory item definition requires a compiled prefab handle.", nameof(handle));
        if (!key.IsValid)
            throw new ArgumentException("An inventory item definition requires a stable prefab key.", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (maximumQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
        if ((allowedEquipSlots & ~InventoryEquipSlots.MainHand) != 0)
            throw new ArgumentOutOfRangeException(nameof(allowedEquipSlots));
        if (allowedEquipSlots != InventoryEquipSlots.None && maximumQuantity != 1)
            throw new ArgumentException("Equippable inventory items must be non-stackable.", nameof(maximumQuantity));

        Handle = handle;
        Key = key;
        DisplayName = displayName;
        MaximumQuantity = maximumQuantity;
        AllowedEquipSlots = allowedEquipSlots;
    }

    public PrefabHandle Handle { get; }
    public PrefabKey Key { get; }
    public string DisplayName { get; }
    public int MaximumQuantity { get; }
    public InventoryEquipSlots AllowedEquipSlots { get; }
    public bool IsStackable => MaximumQuantity > 1;
}

/// <summary>
/// Game-content capability consumed by Inventory. Adding a new item prefab is
/// a content-layer operation and does not require Inventory rule changes.
/// </summary>
public interface IInventoryContentCatalog
{
    CompiledPrefabCatalog Prefabs { get; }

    bool TryGetContainer(PrefabHandle handle, out InventoryContainerDefinition definition);

    bool TryGetContainer(PrefabKey key, out InventoryContainerDefinition definition);

    bool TryGetItem(PrefabHandle handle, out InventoryItemDefinition definition);

    bool TryGetItem(PrefabKey key, out InventoryItemDefinition definition);
}

/// <summary>Single cold source of item policy used for prefab data and bound metadata.</summary>
public readonly record struct InventoryItemAuthoring(
    PrefabKey Key,
    string DisplayName,
    int MaximumQuantity,
    InventoryEquipSlots AllowedEquipSlots);

/// <summary>Single cold source of container policy used for prefab data and bound metadata.</summary>
public readonly record struct InventoryContainerAuthoring(
    PrefabKey Key,
    string DisplayName,
    int DefaultCapacity,
    bool HasOwner);

/// <summary>Reusable Inventory component authoring for game-owned content.</summary>
public static class InventoryContent
{
    public static InventoryContainerAuthoring DefineContainer(
        ContentCatalogBuilder content,
        PrefabKey key,
        string displayName,
        int defaultCapacity,
        bool hasOwner)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (defaultCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultCapacity));
        var prefab = content.Define(key);
        prefab.With(new InventoryContainer { Capacity = defaultCapacity });
        if (hasOwner)
            prefab.With(new InventoryContainerOwner { Owner = Entity.Null });
        prefab.DisplayName(displayName);
        return new InventoryContainerAuthoring(key, displayName, defaultCapacity, hasOwner);
    }

    public static InventoryItemAuthoring DefineItem(
        ContentCatalogBuilder content,
        PrefabKey key,
        string displayName,
        int maximumQuantity,
        InventoryEquipSlots allowedEquipSlots = InventoryEquipSlots.None)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maximumQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
        if ((allowedEquipSlots & ~InventoryEquipSlots.MainHand) != 0)
            throw new ArgumentOutOfRangeException(nameof(allowedEquipSlots));
        if (allowedEquipSlots != InventoryEquipSlots.None && maximumQuantity != 1)
            throw new ArgumentException("Equippable inventory items must be non-stackable.", nameof(maximumQuantity));

        var prefab = content.Define(key);
        prefab.With(new InventoryItem
        {
            Parent = Entity.Null,
            EquipSlot = InventoryEquipSlot.None,
            Quantity = 1,
        });
        if (maximumQuantity > 1)
            prefab.With(new InventoryStackPolicy { MaximumQuantity = maximumQuantity });
        if (allowedEquipSlots != InventoryEquipSlots.None)
            prefab.With(new InventoryEquipCapability { AllowedSlots = allowedEquipSlots });
        prefab.DisplayName(displayName);
        return new InventoryItemAuthoring(key, displayName, maximumQuantity, allowedEquipSlots);
    }
}
