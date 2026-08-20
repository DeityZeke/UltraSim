namespace UltraSim.Inventory;

[Flags]
public enum InventoryAccessFlags : ushort
{
    None = 0,
    Read = 1 << 0,
    Take = 1 << 1,
    Put = 1 << 2,
    Move = 1 << 3,
    Mutate = 1 << 4,
    Admin = 1 << 5,
}
