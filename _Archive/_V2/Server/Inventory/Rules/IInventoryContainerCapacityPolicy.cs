using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryContainerCapacityPolicy
{
    InventoryMutationRuleDecision CanAccept(
        InventoryContainerSnapshot destination,
        InventoryItemSnapshot item,
        InventoryPlacementDescriptor? proposedPlacement);
}
