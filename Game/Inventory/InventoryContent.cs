using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Immutable content footprint consumed only by Grid placement policy.</summary>
public readonly record struct InventoryItemFootprint(ushort Width, ushort Height)
{
    public static InventoryItemFootprint OneCell => new(1, 1);
    public bool IsValid => Width != 0 && Height != 0;
}

/// <summary>Cold content descriptor for a compiled container prefab.</summary>
public readonly record struct InventoryContainerDefinition
{
    public InventoryContainerDefinition(
        PrefabHandle handle,
        PrefabKey key,
        string displayName,
        int defaultCapacity,
        InventoryRuleProfileId ruleProfile,
        bool hasOwner)
    {
        if (!handle.IsValid)
            throw new ArgumentException("An inventory container definition requires a compiled prefab handle.", nameof(handle));
        if (!key.IsValid)
            throw new ArgumentException("An inventory container definition requires a stable prefab key.", nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (defaultCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultCapacity));
        if (!ruleProfile.IsValid)
            throw new ArgumentException("A container definition requires an inventory rule profile.", nameof(ruleProfile));
        Handle = handle;
        Key = key;
        DisplayName = displayName;
        DefaultCapacity = defaultCapacity;
        RuleProfile = ruleProfile;
        HasOwner = hasOwner;
    }

    public PrefabHandle Handle { get; }
    public PrefabKey Key { get; }
    public string DisplayName { get; }
    public int DefaultCapacity { get; }
    public InventoryRuleProfileId RuleProfile { get; }
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
        InventoryEquipSlots allowedEquipSlots,
        InventoryItemFootprint footprint)
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
        if (!footprint.IsValid)
            throw new ArgumentException("An inventory item requires a valid Grid footprint.", nameof(footprint));

        Handle = handle;
        Key = key;
        DisplayName = displayName;
        MaximumQuantity = maximumQuantity;
        AllowedEquipSlots = allowedEquipSlots;
        Footprint = footprint;
    }

    public PrefabHandle Handle { get; }
    public PrefabKey Key { get; }
    public string DisplayName { get; }
    public int MaximumQuantity { get; }
    public InventoryEquipSlots AllowedEquipSlots { get; }
    public InventoryItemFootprint Footprint { get; }
    public bool IsStackable => MaximumQuantity > 1;
}

/// <summary>
/// Game-content capability consumed by Inventory. Adding a new item prefab is
/// a content-layer operation and does not require Inventory rule changes.
/// </summary>
public interface IInventoryContentCatalog
{
    CompiledPrefabCatalog Prefabs { get; }

    InventoryRuleCatalog InventoryRules { get; }

    bool TryGetContainer(PrefabHandle handle, out InventoryContainerDefinition definition);

    bool TryGetContainer(PrefabKey key, out InventoryContainerDefinition definition);

    bool TryGetItem(PrefabHandle handle, out InventoryItemDefinition definition);

    bool TryGetItem(PrefabKey key, out InventoryItemDefinition definition);

    bool TryGetGridPlacementTransitions(
        PrefabHandle item,
        out ArchetypeTransition addPlacement,
        out ArchetypeTransition removePlacement);
}

/// <summary>Single cold source of item policy used for prefab data and bound metadata.</summary>
public readonly record struct InventoryItemAuthoring(
    PrefabKey Key,
    string DisplayName,
    int MaximumQuantity,
    InventoryEquipSlots AllowedEquipSlots,
    InventoryItemFootprint Footprint);

/// <summary>Single cold source of container policy used for prefab data and bound metadata.</summary>
public readonly record struct InventoryContainerAuthoring(
    PrefabKey Key,
    string DisplayName,
    int DefaultCapacity,
    InventoryRuleProfileId RuleProfile,
    bool HasOwner);

/// <summary>Reusable Inventory component authoring for game-owned content.</summary>
public static class InventoryContent
{
    public static InventoryContainerAuthoring DefineContainer(
        ContentCatalogBuilder content,
        PrefabKey key,
        string displayName,
        int defaultCapacity,
        InventoryRuleProfileId ruleProfile,
        bool hasOwner)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (defaultCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultCapacity));
        if (!ruleProfile.IsValid)
            throw new ArgumentException("A container requires an inventory rule profile.", nameof(ruleProfile));
        var prefab = content.Define(key);
        prefab.With(new InventoryContainer { Capacity = defaultCapacity });
        if (hasOwner)
            prefab.With(new InventoryContainerOwner { Owner = Entity.Null });
        prefab.DisplayName(displayName);
        return new InventoryContainerAuthoring(key, displayName, defaultCapacity, ruleProfile, hasOwner);
    }

    public static InventoryItemAuthoring DefineItem(
        ContentCatalogBuilder content,
        PrefabKey key,
        string displayName,
        int maximumQuantity,
        InventoryEquipSlots allowedEquipSlots = InventoryEquipSlots.None,
        InventoryItemFootprint footprint = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maximumQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumQuantity));
        if ((allowedEquipSlots & ~InventoryEquipSlots.MainHand) != 0)
            throw new ArgumentOutOfRangeException(nameof(allowedEquipSlots));
        if (allowedEquipSlots != InventoryEquipSlots.None && maximumQuantity != 1)
            throw new ArgumentException("Equippable inventory items must be non-stackable.", nameof(maximumQuantity));
        if (footprint == default)
            footprint = InventoryItemFootprint.OneCell;
        if (!footprint.IsValid)
            throw new ArgumentException("An inventory item requires a valid Grid footprint.", nameof(footprint));

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
        if (footprint != InventoryItemFootprint.OneCell)
        {
            prefab.With(new InventoryGridFootprint
            {
                Width = footprint.Width,
                Height = footprint.Height,
            });
        }
        prefab.DisplayName(displayName);
        return new InventoryItemAuthoring(key, displayName, maximumQuantity, allowedEquipSlots, footprint);
    }
}
