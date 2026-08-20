namespace UltraSim.Inventory;

public sealed record InventoryFreeformProjectionRow(
    int DisplayOrder,
    InventoryItemSnapshot Item)
{
    public InventoryValidationResult Validate()
    {
        if (DisplayOrder < 0)
        {
            return InventoryValidationResult.Invalid("Inventory freeform projection display order must be non-negative.");
        }

        return Item.Validate(InventoryPlacementMode.Freeform);
    }
}
