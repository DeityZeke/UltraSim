namespace UltraSim.Server.Inventory;

public readonly record struct InventoryOwnerInvalidationDecision(
    InventoryOwnerInvalidationAction Action,
    string? Reason)
{
    public bool ShouldCloseViewers => Action != InventoryOwnerInvalidationAction.KeepOpen;
    public bool ShouldReleaseTransactions => Action == InventoryOwnerInvalidationAction.CloseViewersAndReleaseTransactions;
    public bool ShouldDestroyContainer => Action == InventoryOwnerInvalidationAction.CloseViewersAndDestroyContainer;

    public static InventoryOwnerInvalidationDecision KeepOpen()
    {
        return new InventoryOwnerInvalidationDecision(InventoryOwnerInvalidationAction.KeepOpen, Reason: null);
    }

    public static InventoryOwnerInvalidationDecision CloseViewers(string reason)
    {
        return new InventoryOwnerInvalidationDecision(InventoryOwnerInvalidationAction.CloseViewers, reason);
    }

    public static InventoryOwnerInvalidationDecision CloseViewersAndReleaseTransactions(string reason)
    {
        return new InventoryOwnerInvalidationDecision(
            InventoryOwnerInvalidationAction.CloseViewersAndReleaseTransactions,
            reason);
    }

    public static InventoryOwnerInvalidationDecision CloseViewersAndDestroyContainer(string reason)
    {
        return new InventoryOwnerInvalidationDecision(
            InventoryOwnerInvalidationAction.CloseViewersAndDestroyContainer,
            reason);
    }
}
