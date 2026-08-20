namespace UltraSim.Server.Inventory;

public sealed record InventoryStoreEvent(
    InventoryStoreEventKind Kind,
    string ContainerId,
    string? ViewId = null,
    string? TransactionId = null,
    string? ItemInstanceId = null,
    string? Message = null);
