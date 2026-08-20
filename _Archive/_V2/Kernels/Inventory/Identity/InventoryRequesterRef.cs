namespace UltraSim.Inventory;

public sealed record InventoryRequesterRef(
    InventoryRequesterKind Kind,
    string Id)
{
    public InventoryValidationResult Validate()
    {
        if (!InventoryValidation.IsDefined(Kind))
        {
            return InventoryValidationResult.Invalid("Inventory requester kind is invalid.");
        }

        return InventoryValidation.ValidateStableId(Id, nameof(Id));
    }
}
