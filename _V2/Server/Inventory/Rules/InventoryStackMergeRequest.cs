using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed record InventoryStackMergeRequest(
    InventoryRequesterRef Requester,
    InventoryItemSnapshot SourceItem,
    InventoryItemSnapshot DestinationItem,
    long RequestedQuantity,
    InventoryContainerSnapshot? SourceContainer = null,
    InventoryContainerSnapshot? DestinationContainer = null);
