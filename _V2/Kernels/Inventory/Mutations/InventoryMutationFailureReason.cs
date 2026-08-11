namespace UltraSim.Inventory;

public enum InventoryMutationFailureReason : byte
{
    None = 0,
    InvalidRequester = 1,
    InvalidContainer = 2,
    InvalidItem = 3,
    InvalidPlacement = 4,
    InvalidQuantity = 5,
    Unauthorized = 6,
    OutOfRange = 7,
    Locked = 8,
    NoTrade = 9,
    NoDrop = 10,
    Conflict = 11,
    StaleItem = 12,
    StaleContainer = 13,
    TransactionRequired = 14,
    TransactionConflict = 15,
    RuleRejected = 16,
    ServerError = 17,
}
