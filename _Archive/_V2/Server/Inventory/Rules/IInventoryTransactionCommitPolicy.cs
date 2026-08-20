using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryTransactionCommitPolicy
{
    InventoryMutationRuleDecision CanCommitSuspendedItem(
        InventoryTransactionSnapshot transaction,
        InventorySuspendedItemSnapshot suspendedItem,
        InventoryItemSnapshot item,
        InventoryContainerSnapshot destinationContainer);
}
