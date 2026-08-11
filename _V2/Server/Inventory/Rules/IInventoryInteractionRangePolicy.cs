using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryInteractionRangePolicy
{
    InventoryMutationRuleDecision IsInRange(
        InventoryRequesterRef requester,
        InventoryContainerSnapshot source,
        InventoryContainerSnapshot? destination);
}
