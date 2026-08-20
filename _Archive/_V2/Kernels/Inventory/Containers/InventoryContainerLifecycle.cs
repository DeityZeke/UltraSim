namespace UltraSim.Inventory;

public enum InventoryContainerLifecycle : byte
{
    Persistent = 1,
    WorldPersistent = 2,
    Runtime = 3,
    Ephemeral = 4,
    Transactional = 5,
    Destructive = 6,
}
