namespace UltraSim.Server.Inventory;

public interface IInventoryStackMergePolicy
{
    InventoryStackMergeDecision Evaluate(in InventoryStackMergeRequest request);
}
