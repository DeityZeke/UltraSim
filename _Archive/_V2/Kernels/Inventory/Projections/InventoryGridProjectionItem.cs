namespace UltraSim.Inventory;

public sealed record InventoryGridProjectionItem(InventoryItemSnapshot Item)
{
    public InventoryValidationResult Validate(int gridWidth, int gridHeight)
    {
        InventoryValidationResult itemResult = Item.Validate(InventoryPlacementMode.Grid);
        if (!itemResult.IsValid)
        {
            return itemResult;
        }

        InventoryPlacementDescriptor placement = Item.Placement;
        if (placement.X + placement.Width > gridWidth ||
            placement.Y + placement.Height > gridHeight)
        {
            return InventoryValidationResult.Invalid("Inventory grid projection item exceeds grid bounds.");
        }

        return InventoryValidationResult.Valid;
    }
}
