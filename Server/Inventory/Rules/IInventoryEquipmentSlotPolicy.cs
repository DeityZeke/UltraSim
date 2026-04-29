using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryEquipmentSlotPolicy
{
    InventoryMutationRuleDecision CanEquip(
        InventoryRequesterRef requester,
        InventoryItemSnapshot item,
        InventoryContainerSnapshot equipmentContainer,
        InventoryPlacementDescriptor slotPlacement);
}
