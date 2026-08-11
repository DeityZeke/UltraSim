namespace UltraSim.Server.Inventory;

public interface IInventoryContainerAccessPolicy
{
    InventoryContainerAccessDecision Evaluate(in InventoryContainerAccessRequest request);
}
