namespace UltraSim.Server.Inventory;

public enum InventoryOwnerInvalidationAction : byte
{
    KeepOpen = 1,
    CloseViewers = 2,
    CloseViewersAndReleaseTransactions = 3,
    CloseViewersAndDestroyContainer = 4,
}
