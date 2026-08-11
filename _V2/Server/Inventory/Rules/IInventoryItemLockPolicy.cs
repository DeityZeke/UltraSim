using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryItemLockPolicy
{
    InventoryMutationRuleDecision CanChangeLockState(
        InventoryRequesterRef requester,
        InventoryItemSnapshot item,
        InventoryContainerSnapshot container,
        bool locked);
}
