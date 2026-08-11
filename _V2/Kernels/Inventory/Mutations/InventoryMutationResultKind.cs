namespace UltraSim.Inventory;

public enum InventoryMutationResultKind : byte
{
    Accepted = 1,
    Rejected = 2,
    Pending = 3,
    NoOp = 4,
}
