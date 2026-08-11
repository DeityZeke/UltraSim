namespace UltraSim.Inventory;

public enum InventoryTransactionState : byte
{
    Open = 1,
    CommitPending = 2,
    Committed = 3,
    Cancelled = 4,
    Failed = 5,
}
