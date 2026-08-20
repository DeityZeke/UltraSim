namespace UltraSim.Server.Inventory;

public enum InventoryRuntimeRefResolutionKind : byte
{
    Resolved = 1,
    InvalidRef = 2,
    UnsupportedKind = 3,
    InvalidEntityFormat = 4,
    EntityNotAlive = 5,
    NotFound = 6,
}
