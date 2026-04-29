namespace UltraSim.Inventory;

public sealed record InventoryItemSnapshot(
    string ItemInstanceId,
    string ItemDefinitionId,
    long Quantity,
    long MaxStackQuantity,
    InventoryPlacementDescriptor Placement,
    InventoryItemFlags Flags = InventoryItemFlags.None)
{
    public InventoryValidationResult Validate(InventoryPlacementMode containerPlacementMode)
    {
        InventoryValidationResult instanceResult = InventoryValidation.ValidateStableId(ItemInstanceId, nameof(ItemInstanceId));
        if (!instanceResult.IsValid)
        {
            return instanceResult;
        }

        InventoryValidationResult definitionResult = InventoryValidation.ValidateStableId(
            ItemDefinitionId,
            nameof(ItemDefinitionId),
            InventoryValidation.MaxTypeIdLength);
        if (!definitionResult.IsValid)
        {
            return definitionResult;
        }

        if (Quantity <= 0)
        {
            return InventoryValidationResult.Invalid("Item quantity must be positive.");
        }

        if (MaxStackQuantity <= 0)
        {
            return InventoryValidationResult.Invalid("Item max stack quantity must be positive.");
        }

        if (Quantity > MaxStackQuantity)
        {
            return InventoryValidationResult.Invalid("Item quantity cannot exceed max stack quantity.");
        }

        const InventoryItemFlags allKnown =
            InventoryItemFlags.Locked |
            InventoryItemFlags.NoTrade |
            InventoryItemFlags.NoDrop |
            InventoryItemFlags.QuestItem |
            InventoryItemFlags.TransactionReserved |
            InventoryItemFlags.NonPersistent;

        if ((Flags & ~allKnown) != 0)
        {
            return InventoryValidationResult.Invalid("Item flags contain unknown values.");
        }

        return Placement.ValidateForContainerMode(containerPlacementMode);
    }
}
