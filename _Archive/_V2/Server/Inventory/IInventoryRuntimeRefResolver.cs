using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public interface IInventoryRuntimeRefResolver
{
    InventoryRuntimeRefResolution ResolveOwner(InventoryOwnerRef owner);
    InventoryRuntimeRefResolution ResolveRequester(InventoryRequesterRef requester);
}
