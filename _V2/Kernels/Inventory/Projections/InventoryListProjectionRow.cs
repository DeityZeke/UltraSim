namespace UltraSim.Inventory;

public sealed record InventoryListProjectionRow(
    int DisplayIndex,
    InventoryItemSnapshot Item)
{
    public InventoryValidationResult Validate(InventoryPlacementMode sourcePlacementMode)
    {
        if (DisplayIndex < 0)
        {
            return InventoryValidationResult.Invalid("Inventory list projection display index must be non-negative.");
        }

        return Item.Validate(sourcePlacementMode);
    }
}
