namespace UltraSim.Inventory;

public sealed record InventoryProjectionSortDescriptor(
    string SortKey,
    InventoryProjectionSortDirection Direction)
{
    public static InventoryProjectionSortDescriptor ByPlacementIndex { get; } =
        new("placement.index", InventoryProjectionSortDirection.Ascending);

    public static InventoryProjectionSortDescriptor ByDefinitionId { get; } =
        new("item.definition", InventoryProjectionSortDirection.Ascending);

    public InventoryValidationResult Validate()
    {
        InventoryValidationResult keyResult = InventoryValidation.ValidateStableId(
            SortKey,
            nameof(SortKey),
            InventoryValidation.MaxTypeIdLength);
        if (!keyResult.IsValid)
        {
            return keyResult;
        }

        return InventoryValidation.IsDefined(Direction)
            ? InventoryValidationResult.Valid
            : InventoryValidationResult.Invalid("Inventory projection sort direction is invalid.");
    }
}
