using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public readonly record struct InventoryContainerAccessDecision(
    bool IsAllowed,
    InventoryAccessFlags GrantedAccess,
    InventoryMutationFailureReason FailureReason,
    string? Message)
{
    public static InventoryContainerAccessDecision Allowed(InventoryAccessFlags grantedAccess)
    {
        return new InventoryContainerAccessDecision(
            IsAllowed: true,
            grantedAccess,
            InventoryMutationFailureReason.None,
            Message: null);
    }

    public static InventoryContainerAccessDecision Denied(
        InventoryMutationFailureReason reason,
        string? message = null)
    {
        if (reason == InventoryMutationFailureReason.None)
        {
            throw new ArgumentException("Denied access decisions require a failure reason.", nameof(reason));
        }

        return new InventoryContainerAccessDecision(false, InventoryAccessFlags.None, reason, message);
    }
}
