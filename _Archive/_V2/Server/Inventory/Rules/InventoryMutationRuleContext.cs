using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed record InventoryMutationRuleContext(
    InventoryMutationIntent Intent,
    InventoryRuntimeRefResolution Requester,
    InventoryContainerSnapshot? SourceContainer,
    InventoryContainerSnapshot? DestinationContainer,
    InventoryItemSnapshot? SourceItem,
    InventoryItemSnapshot? DestinationItem);
