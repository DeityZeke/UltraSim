namespace UltraSim.Inventory;

public sealed record InventoryOwnerRef(
    InventoryOwnerKind Kind,
    string Id)
{
    public InventoryValidationResult Validate()
    {
        if (!InventoryValidation.IsDefined(Kind))
        {
            return InventoryValidationResult.Invalid("Inventory owner kind is invalid.");
        }

        return InventoryValidation.ValidateStableId(Id, nameof(Id));
    }
}
