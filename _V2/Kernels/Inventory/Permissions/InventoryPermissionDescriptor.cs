namespace UltraSim.Inventory;

public sealed record InventoryPermissionDescriptor(
    InventoryPermissionKind Kind,
    InventoryAccessFlags Access,
    string? RuleId = null)
{
    public static InventoryPermissionDescriptor PublicReadTake { get; } = new(
        InventoryPermissionKind.Public,
        InventoryAccessFlags.Read | InventoryAccessFlags.Take);

    public InventoryValidationResult Validate()
    {
        if (!InventoryValidation.IsDefined(Kind))
        {
            return InventoryValidationResult.Invalid("Inventory permission kind is invalid.");
        }

        const InventoryAccessFlags allKnown =
            InventoryAccessFlags.Read |
            InventoryAccessFlags.Take |
            InventoryAccessFlags.Put |
            InventoryAccessFlags.Move |
            InventoryAccessFlags.Mutate |
            InventoryAccessFlags.Admin;

        if ((Access & ~allKnown) != 0)
        {
            return InventoryValidationResult.Invalid("Inventory access flags contain unknown values.");
        }

        if (Access == InventoryAccessFlags.None)
        {
            return InventoryValidationResult.Invalid("Inventory permission access cannot be None.");
        }

        if (Kind == InventoryPermissionKind.ScriptedRule)
        {
            return InventoryValidation.ValidateStableId(RuleId, nameof(RuleId), InventoryValidation.MaxRuleIdLength);
        }

        if (!string.IsNullOrWhiteSpace(RuleId))
        {
            return InventoryValidationResult.Invalid("Only scripted inventory permissions can carry a rule id.");
        }

        return InventoryValidationResult.Valid;
    }
}
