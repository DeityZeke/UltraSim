using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public readonly record struct InventoryStackMergeDecision(
    bool CanMerge,
    long QuantityToMove,
    InventoryMutationFailureReason FailureReason,
    string? Message)
{
    public static InventoryStackMergeDecision Allowed(long quantityToMove)
    {
        if (quantityToMove <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityToMove), "Merge quantity must be positive.");
        }

        return new InventoryStackMergeDecision(true, quantityToMove, InventoryMutationFailureReason.None, Message: null);
    }

    public static InventoryStackMergeDecision Denied(
        InventoryMutationFailureReason reason,
        string? message = null)
    {
        if (reason == InventoryMutationFailureReason.None)
        {
            throw new ArgumentException("Denied stack merge decisions require a failure reason.", nameof(reason));
        }

        return new InventoryStackMergeDecision(false, QuantityToMove: 0, reason, message);
    }
}
