namespace UltraSim.Inventory;

public sealed record InventoryContainerDelta(
    string DeltaId,
    string ContainerId,
    long ContainerRevision,
    InventoryContainerDeltaKind Kind,
    string? ItemInstanceId,
    InventoryItemSnapshot? Item,
    InventoryPlacementDescriptor? PreviousPlacement,
    InventoryPlacementDescriptor? CurrentPlacement,
    long QuantityDelta,
    InventoryAccessFlags RequiredAccess,
    string? VisibilityScopeId = null,
    string? TransactionId = null)
{
    public InventoryValidationResult Validate(InventoryPlacementMode containerPlacementMode)
    {
        InventoryValidationResult idResult = InventoryValidation.ValidateStableId(DeltaId, nameof(DeltaId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        idResult = InventoryValidation.ValidateStableId(ContainerId, nameof(ContainerId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        if (ContainerRevision < 0)
        {
            return InventoryValidationResult.Invalid("Container delta revision cannot be negative.");
        }

        if (!InventoryValidation.IsDefined(Kind))
        {
            return InventoryValidationResult.Invalid("Container delta kind is invalid.");
        }

        InventoryValidationResult accessResult = ValidateAccessFlags(RequiredAccess, nameof(RequiredAccess), allowNone: false);
        if (!accessResult.IsValid)
        {
            return accessResult;
        }

        InventoryValidationResult optionalResult = ValidateOptionalId(ItemInstanceId, nameof(ItemInstanceId));
        if (!optionalResult.IsValid)
        {
            return optionalResult;
        }

        optionalResult = ValidateOptionalId(VisibilityScopeId, nameof(VisibilityScopeId));
        if (!optionalResult.IsValid)
        {
            return optionalResult;
        }

        optionalResult = ValidateOptionalId(TransactionId, nameof(TransactionId));
        if (!optionalResult.IsValid)
        {
            return optionalResult;
        }

        if (Item is not null)
        {
            InventoryValidationResult itemResult = Item.Validate(containerPlacementMode);
            if (!itemResult.IsValid)
            {
                return itemResult;
            }
        }

        InventoryValidationResult placementResult = ValidatePlacement(PreviousPlacement, nameof(PreviousPlacement), containerPlacementMode);
        if (!placementResult.IsValid)
        {
            return placementResult;
        }

        placementResult = ValidatePlacement(CurrentPlacement, nameof(CurrentPlacement), containerPlacementMode);
        if (!placementResult.IsValid)
        {
            return placementResult;
        }

        return Kind switch
        {
            InventoryContainerDeltaKind.SnapshotReplaced => ValidateSnapshotReplaced(),
            InventoryContainerDeltaKind.ItemAdded => RequireItem(),
            InventoryContainerDeltaKind.ItemRemoved => RequireItemIdWithoutItem(),
            InventoryContainerDeltaKind.ItemUpdated => RequireItem(),
            InventoryContainerDeltaKind.ItemMoved => ValidateMoved(),
            InventoryContainerDeltaKind.QuantityChanged => ValidateQuantityChanged(),
            InventoryContainerDeltaKind.ItemReserved => ValidateTransactionItemDelta(),
            InventoryContainerDeltaKind.ItemReleased => ValidateTransactionItemDelta(),
            InventoryContainerDeltaKind.ContainerMetadataChanged => ValidateNoItemPayload(),
            InventoryContainerDeltaKind.ViewerOpened => ValidateNoItemPayload(),
            InventoryContainerDeltaKind.ViewerClosed => ValidateNoItemPayload(),
            InventoryContainerDeltaKind.TransactionStateChanged => ValidateTransactionStateChanged(),
            _ => InventoryValidationResult.Invalid("Container delta kind is invalid."),
        };
    }

    internal static InventoryValidationResult ValidateAccessFlags(
        InventoryAccessFlags access,
        string fieldName,
        bool allowNone)
    {
        const InventoryAccessFlags allKnown =
            InventoryAccessFlags.Read |
            InventoryAccessFlags.Take |
            InventoryAccessFlags.Put |
            InventoryAccessFlags.Move |
            InventoryAccessFlags.Mutate |
            InventoryAccessFlags.Admin;

        if ((access & ~allKnown) != 0)
        {
            return InventoryValidationResult.Invalid($"{fieldName} contains unknown access flags.");
        }

        if (!allowNone && access == InventoryAccessFlags.None)
        {
            return InventoryValidationResult.Invalid($"{fieldName} cannot be None.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateSnapshotReplaced()
    {
        if (Item is not null ||
            ItemInstanceId is not null ||
            PreviousPlacement is not null ||
            CurrentPlacement is not null ||
            QuantityDelta != 0)
        {
            return InventoryValidationResult.Invalid("Snapshot replacement deltas cannot carry item payloads.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult RequireItem()
    {
        if (Item is null)
        {
            return InventoryValidationResult.Invalid($"{Kind} requires an item snapshot.");
        }

        if (ItemInstanceId is not null && !string.Equals(ItemInstanceId, Item.ItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryValidationResult.Invalid($"{Kind} item id must match the item snapshot.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult RequireItemIdWithoutItem()
    {
        InventoryValidationResult requiredResult = Require(ItemInstanceId, nameof(ItemInstanceId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (Item is not null)
        {
            return InventoryValidationResult.Invalid($"{Kind} cannot carry an item snapshot.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateMoved()
    {
        InventoryValidationResult requiredResult = Require(ItemInstanceId, nameof(ItemInstanceId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (PreviousPlacement is null || CurrentPlacement is null)
        {
            return InventoryValidationResult.Invalid("ItemMoved requires previous and current placement.");
        }

        if (Item is not null || QuantityDelta != 0)
        {
            return InventoryValidationResult.Invalid("ItemMoved cannot carry item snapshot or quantity delta.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateQuantityChanged()
    {
        InventoryValidationResult requiredResult = Require(ItemInstanceId, nameof(ItemInstanceId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (QuantityDelta == 0)
        {
            return InventoryValidationResult.Invalid("QuantityChanged requires a non-zero quantity delta.");
        }

        if (Item is not null || PreviousPlacement is not null || CurrentPlacement is not null)
        {
            return InventoryValidationResult.Invalid("QuantityChanged cannot carry item snapshot or placement.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateTransactionItemDelta()
    {
        InventoryValidationResult requiredResult = Require(ItemInstanceId, nameof(ItemInstanceId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        requiredResult = Require(TransactionId, nameof(TransactionId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (Item is not null || QuantityDelta < 0)
        {
            return InventoryValidationResult.Invalid($"{Kind} cannot carry item snapshots or negative quantity deltas.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateNoItemPayload()
    {
        if (Item is not null ||
            ItemInstanceId is not null ||
            PreviousPlacement is not null ||
            CurrentPlacement is not null ||
            QuantityDelta != 0)
        {
            return InventoryValidationResult.Invalid($"{Kind} cannot carry item payloads.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateTransactionStateChanged()
    {
        InventoryValidationResult requiredResult = Require(TransactionId, nameof(TransactionId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        return ValidateNoItemPayload();
    }

    private static InventoryValidationResult ValidateOptionalId(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? InventoryValidationResult.Valid
            : InventoryValidation.ValidateStableId(value, fieldName);
    }

    private static InventoryValidationResult ValidatePlacement(
        InventoryPlacementDescriptor? placement,
        string fieldName,
        InventoryPlacementMode containerPlacementMode)
    {
        if (placement is null)
        {
            return InventoryValidationResult.Valid;
        }

        InventoryValidationResult result = placement.ValidateForContainerMode(containerPlacementMode);
        if (!result.IsValid)
        {
            return InventoryValidationResult.Invalid($"{fieldName}: {result.Error}");
        }

        return InventoryValidationResult.Valid;
    }

    private static InventoryValidationResult Require(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? InventoryValidationResult.Invalid($"{fieldName} is required.")
            : InventoryValidationResult.Valid;
    }
}
