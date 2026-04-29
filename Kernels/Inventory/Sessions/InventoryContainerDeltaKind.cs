namespace UltraSim.Inventory;

public enum InventoryContainerDeltaKind : byte
{
    SnapshotReplaced = 1,
    ItemAdded = 2,
    ItemRemoved = 3,
    ItemUpdated = 4,
    ItemMoved = 5,
    QuantityChanged = 6,
    ItemReserved = 7,
    ItemReleased = 8,
    ContainerMetadataChanged = 9,
    ViewerOpened = 10,
    ViewerClosed = 11,
    TransactionStateChanged = 12,
}
