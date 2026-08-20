namespace UltraSim.Inventory;

[Flags]
public enum InventoryItemFlags : uint
{
    None = 0,
    Locked = 1 << 0,
    NoTrade = 1 << 1,
    NoDrop = 1 << 2,
    QuestItem = 1 << 3,
    TransactionReserved = 1 << 4,
    NonPersistent = 1 << 5,
}
