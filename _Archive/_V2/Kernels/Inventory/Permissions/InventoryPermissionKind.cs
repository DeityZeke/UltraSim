namespace UltraSim.Inventory;

public enum InventoryPermissionKind : byte
{
    Public = 1,
    Owner = 2,
    Account = 3,
    Party = 4,
    Guild = 5,
    HouseAccessList = 6,
    VendorManaged = 7,
    Admin = 8,
    ScriptedRule = 9,
}
