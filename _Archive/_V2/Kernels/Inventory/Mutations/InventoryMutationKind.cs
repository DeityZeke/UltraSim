namespace UltraSim.Inventory;

public enum InventoryMutationKind : byte
{
    MoveItem = 1,
    SplitStack = 2,
    MergeStack = 3,
    SwapItems = 4,
    TakeItem = 5,
    DropItem = 6,
    EquipItem = 7,
    UnequipItem = 8,
    UseItem = 9,
    DestroyItem = 10,
    LockItem = 11,
    UnlockItem = 12,
    OpenContainer = 13,
    CloseContainerView = 14,
    BeginTransaction = 15,
    CommitTransaction = 16,
    CancelTransaction = 17,
}
