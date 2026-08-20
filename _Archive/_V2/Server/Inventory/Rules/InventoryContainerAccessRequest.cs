using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed record InventoryContainerAccessRequest(
    InventoryRequesterRef Requester,
    InventoryRuntimeRefResolution RequesterResolution,
    InventoryContainerSnapshot Container,
    InventoryAccessFlags RequestedAccess);
