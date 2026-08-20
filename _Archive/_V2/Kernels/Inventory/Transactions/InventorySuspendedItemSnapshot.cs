namespace UltraSim.Inventory;

public sealed record InventorySuspendedItemSnapshot(
    string ItemInstanceId,
    string SourceContainerId,
    string? DestinationContainerId,
    long Quantity)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult idResult = InventoryValidation.ValidateStableId(ItemInstanceId, nameof(ItemInstanceId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        idResult = InventoryValidation.ValidateStableId(SourceContainerId, nameof(SourceContainerId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        if (!string.IsNullOrWhiteSpace(DestinationContainerId))
        {
            idResult = InventoryValidation.ValidateStableId(DestinationContainerId, nameof(DestinationContainerId));
            if (!idResult.IsValid)
            {
                return idResult;
            }
        }

        return Quantity > 0
            ? InventoryValidationResult.Valid
            : InventoryValidationResult.Invalid("Suspended item quantity must be positive.");
    }
}
