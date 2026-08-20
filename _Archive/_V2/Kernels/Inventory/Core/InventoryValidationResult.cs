namespace UltraSim.Inventory;

public readonly record struct InventoryValidationResult(bool IsValid, string Error)
{
    public static InventoryValidationResult Valid { get; } = new(true, string.Empty);

    public static InventoryValidationResult Invalid(string error)
    {
        return new InventoryValidationResult(false, error);
    }
}
