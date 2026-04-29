using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryOwnerLifetimePolicy
{
    InventoryOwnerInvalidationDecision Evaluate(
        InventoryContainerSnapshot container,
        InventoryRuntimeRefResolution ownerResolution);
}
