using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed record InventoryStoreDeltaDispatch(
    InventoryContainerDelta Delta,
    IReadOnlyList<string> RecipientViewIds);
