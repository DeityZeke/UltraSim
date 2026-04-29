namespace UltraSim.Server.Inventory;

public enum InventoryStoreEventKind : byte
{
    ViewerOpened = 1,
    ViewerClosed = 2,
    OwnerInvalidated = 3,
    TransactionCancelled = 4,
    SuspendedItemReleased = 5,
    ContainerDestroyed = 6,
}
