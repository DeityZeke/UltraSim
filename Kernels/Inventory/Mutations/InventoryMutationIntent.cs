namespace UltraSim.Inventory;

public sealed record InventoryMutationIntent(
    string CorrelationId,
    InventoryRequesterRef Requester,
    long ClientSequence,
    InventoryMutationKind Kind,
    string? SourceContainerId,
    string? DestinationContainerId,
    string? SourceItemInstanceId,
    string? DestinationItemInstanceId,
    long Quantity,
    InventoryPlacementDescriptor? ProposedPlacement,
    string? RuleContextId = null)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult correlationResult = InventoryValidation.ValidateStableId(
            CorrelationId,
            nameof(CorrelationId),
            InventoryValidation.MaxStableIdLength);
        if (!correlationResult.IsValid)
        {
            return correlationResult;
        }

        InventoryValidationResult requesterResult = Requester.Validate();
        if (!requesterResult.IsValid)
        {
            return requesterResult;
        }

        if (ClientSequence < -1)
        {
            return InventoryValidationResult.Invalid("Inventory mutation client sequence cannot be less than -1.");
        }

        if (!InventoryValidation.IsDefined(Kind))
        {
            return InventoryValidationResult.Invalid("Inventory mutation kind is invalid.");
        }

        InventoryValidationResult idsResult = ValidateIds();
        if (!idsResult.IsValid)
        {
            return idsResult;
        }

        if (ProposedPlacement is not null)
        {
            InventoryValidationResult placementResult = ProposedPlacement.Validate();
            if (!placementResult.IsValid)
            {
                return placementResult;
            }
        }

        return Kind switch
        {
            InventoryMutationKind.MoveItem => ValidateMoveLike(requirePlacement: false),
            InventoryMutationKind.SplitStack => ValidateSplit(),
            InventoryMutationKind.MergeStack => ValidateMerge(),
            InventoryMutationKind.SwapItems => ValidateSwap(),
            InventoryMutationKind.TakeItem => ValidateMoveLike(requirePlacement: false),
            InventoryMutationKind.DropItem => ValidateMoveLike(requirePlacement: false),
            InventoryMutationKind.EquipItem => ValidateMoveLike(requirePlacement: false),
            InventoryMutationKind.UnequipItem => ValidateMoveLike(requirePlacement: false),
            InventoryMutationKind.UseItem => ValidateSingleItemMutation(),
            InventoryMutationKind.DestroyItem => ValidateSingleItemMutation(),
            InventoryMutationKind.LockItem => ValidateSingleItemMutation(),
            InventoryMutationKind.UnlockItem => ValidateSingleItemMutation(),
            InventoryMutationKind.OpenContainer => ValidateContainerViewIntent(),
            InventoryMutationKind.CloseContainerView => ValidateContainerViewIntent(),
            InventoryMutationKind.BeginTransaction => ValidateTransactionIntent(),
            InventoryMutationKind.CommitTransaction => ValidateTransactionIntent(),
            InventoryMutationKind.CancelTransaction => ValidateTransactionIntent(),
            _ => InventoryValidationResult.Invalid("Inventory mutation kind is invalid."),
        };
    }

    private InventoryValidationResult ValidateIds()
    {
        InventoryValidationResult result = ValidateOptionalId(SourceContainerId, nameof(SourceContainerId));
        if (!result.IsValid)
        {
            return result;
        }

        result = ValidateOptionalId(DestinationContainerId, nameof(DestinationContainerId));
        if (!result.IsValid)
        {
            return result;
        }

        result = ValidateOptionalId(SourceItemInstanceId, nameof(SourceItemInstanceId));
        if (!result.IsValid)
        {
            return result;
        }

        result = ValidateOptionalId(DestinationItemInstanceId, nameof(DestinationItemInstanceId));
        if (!result.IsValid)
        {
            return result;
        }

        return ValidateOptionalId(RuleContextId, nameof(RuleContextId));
    }

    private static InventoryValidationResult ValidateOptionalId(string? value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? InventoryValidationResult.Valid
            : InventoryValidation.ValidateStableId(value, fieldName);
    }

    private InventoryValidationResult ValidateMoveLike(bool requirePlacement)
    {
        InventoryValidationResult requiredResult = Require(SourceContainerId, nameof(SourceContainerId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        requiredResult = Require(DestinationContainerId, nameof(DestinationContainerId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        requiredResult = Require(SourceItemInstanceId, nameof(SourceItemInstanceId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (Quantity <= 0)
        {
            return InventoryValidationResult.Invalid($"{Kind} quantity must be positive.");
        }

        if (requirePlacement && ProposedPlacement is null)
        {
            return InventoryValidationResult.Invalid($"{Kind} requires proposed placement.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateMerge()
    {
        InventoryValidationResult moveResult = ValidateMoveLike(requirePlacement: false);
        if (!moveResult.IsValid)
        {
            return moveResult;
        }

        return Require(DestinationItemInstanceId, nameof(DestinationItemInstanceId));
    }

    private InventoryValidationResult ValidateSplit()
    {
        InventoryValidationResult moveResult = ValidateMoveLike(requirePlacement: true);
        if (!moveResult.IsValid)
        {
            return moveResult;
        }

        return Require(DestinationItemInstanceId, nameof(DestinationItemInstanceId));
    }

    private InventoryValidationResult ValidateSwap()
    {
        InventoryValidationResult moveResult = ValidateMoveLike(requirePlacement: false);
        if (!moveResult.IsValid)
        {
            return moveResult;
        }

        return Require(DestinationItemInstanceId, nameof(DestinationItemInstanceId));
    }

    private InventoryValidationResult ValidateSingleItemMutation()
    {
        InventoryValidationResult requiredResult = Require(SourceContainerId, nameof(SourceContainerId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        requiredResult = Require(SourceItemInstanceId, nameof(SourceItemInstanceId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (Quantity <= 0)
        {
            return InventoryValidationResult.Invalid($"{Kind} quantity must be positive.");
        }

        if (DestinationContainerId is not null ||
            DestinationItemInstanceId is not null ||
            ProposedPlacement is not null)
        {
            return InventoryValidationResult.Invalid($"{Kind} cannot carry destination ids or placement.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateContainerViewIntent()
    {
        InventoryValidationResult requiredResult = Require(SourceContainerId, nameof(SourceContainerId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (SourceItemInstanceId is not null ||
            DestinationContainerId is not null ||
            DestinationItemInstanceId is not null ||
            ProposedPlacement is not null ||
            Quantity != 0)
        {
            return InventoryValidationResult.Invalid($"{Kind} can only carry a source container id.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateTransactionIntent()
    {
        InventoryValidationResult requiredResult = Require(RuleContextId, nameof(RuleContextId));
        if (!requiredResult.IsValid)
        {
            return requiredResult;
        }

        if (Quantity != 0 ||
            ProposedPlacement is not null ||
            SourceItemInstanceId is not null ||
            DestinationItemInstanceId is not null)
        {
            return InventoryValidationResult.Invalid($"{Kind} can only carry transaction context and optional container ids.");
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
