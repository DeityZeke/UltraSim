namespace UltraSim.Inventory;

public enum InventoryRequesterKind : byte
{
    PlayerSession = 1,
    Character = 2,
    EntityAi = 3,
    ServerSystem = 4,
    Admin = 5,
    Script = 6,
}
