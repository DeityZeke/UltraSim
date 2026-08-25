using UltraSim.ECS;

namespace UltraSim.Game.Inventory;

/// <summary>Concrete equipment placement selected for one item relationship.</summary>
public enum InventoryEquipSlot : byte
{
    None = 0,
    MainHand = 1,
}

/// <summary>Content-authored equipment capabilities.</summary>
[Flags]
public enum InventoryEquipSlots : byte
{
    None = 0,
    MainHand = 1 << 0,
}

/// <summary>
/// Persistent effective item-count capacity. Other capacity dimensions belong
/// to the compiled rule profile; ownership remains an independent fact.
/// </summary>
[EcsComponent("game.inventory.container", PersistenceId = 700701)]
public struct InventoryContainer : IComponent<InventoryContainer>
{
    public int Capacity;
}

/// <summary>
/// Optional persistent attachment/ownership relationship for a container.
/// Containers without this component are valid world storage.
/// </summary>
[EcsComponent("game.inventory.container-owner", PersistenceId = 700702)]
public struct InventoryContainerOwner : IComponent<InventoryContainerOwner>
{
    [PersistenceEntityReference]
    public Entity Owner;
}

/// <summary>
/// Persistent item relationship and quantity. Item identity and presentation
/// come from prefab provenance, not an Inventory-owned item-kind enum.
/// </summary>
[EcsComponent("game.inventory.item", PersistenceId = 700703)]
public struct InventoryItem : IComponent<InventoryItem>
{
    [PersistenceEntityReference]
    public Entity Parent;
    public InventoryEquipSlot EquipSlot;
    public int Quantity;
}

/// <summary>
/// Optional persistent placement of one contained item in a Grid container.
/// Footprint is immutable content policy and is deliberately not duplicated here.
/// </summary>
[EcsComponent("game.inventory.grid-placement", PersistenceId = 700706)]
public struct InventoryGridPlacement : IComponent<InventoryGridPlacement>
{
    public ushort X;
    public ushort Y;
}

/// <summary>
/// Optional immutable content footprint. Absence canonically means one cell;
/// presence makes larger item shapes part of prefab/persistence identity.
/// </summary>
[EcsComponent("game.inventory.grid-footprint", PersistenceId = 700707)]
public struct InventoryGridFootprint : IComponent<InventoryGridFootprint>
{
    public ushort Width;
    public ushort Height;
}

/// <summary>Optional content-authored stack limit for an item prefab.</summary>
[EcsComponent("game.inventory.stack-policy", PersistenceId = 700704)]
public struct InventoryStackPolicy : IComponent<InventoryStackPolicy>
{
    public int MaximumQuantity;
}

/// <summary>Optional content-authored equipment slots for an item prefab.</summary>
[EcsComponent("game.inventory.equip-capability", PersistenceId = 700705)]
public struct InventoryEquipCapability : IComponent<InventoryEquipCapability>
{
    public InventoryEquipSlots AllowedSlots;
}

internal static class InventoryEquipSlotExtensions
{
    internal static InventoryEquipSlots ToCapability(this InventoryEquipSlot slot) => slot switch
    {
        InventoryEquipSlot.MainHand => InventoryEquipSlots.MainHand,
        _ => InventoryEquipSlots.None,
    };
}
