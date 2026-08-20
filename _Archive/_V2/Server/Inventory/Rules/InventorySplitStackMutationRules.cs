using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class InventorySplitStackMutationRules : IInventoryMutationRules
{
    private readonly IInventoryContainerAccessPolicy _accessPolicy;
    private readonly IInventoryContainerCapacityPolicy _capacityPolicy;
    private readonly IInventoryInteractionRangePolicy _rangePolicy;

    public InventorySplitStackMutationRules(
        IInventoryContainerAccessPolicy accessPolicy,
        IInventoryContainerCapacityPolicy capacityPolicy,
        IInventoryInteractionRangePolicy rangePolicy)
    {
        _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _capacityPolicy = capacityPolicy ?? throw new ArgumentNullException(nameof(capacityPolicy));
        _rangePolicy = rangePolicy ?? throw new ArgumentNullException(nameof(rangePolicy));
    }

    public InventoryMutationRuleDecision Evaluate(in InventoryMutationRuleContext context)
    {
        InventoryValidationResult intentResult = context.Intent.Validate();
        if (!intentResult.IsValid)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                intentResult.Error);
        }

        if (context.Intent.Kind != InventoryMutationKind.SplitStack)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Only SplitStack is supported by InventorySplitStackMutationRules.");
        }

        if (!context.Requester.IsResolved)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidRequester,
                "Requester did not resolve.");
        }

        if (context.SourceContainer is null ||
            context.DestinationContainer is null ||
            context.SourceItem is null ||
            context.Intent.ProposedPlacement is null ||
            string.IsNullOrWhiteSpace(context.Intent.DestinationItemInstanceId))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidContainer,
                "Split requires source/destination containers, source item, new item id, and proposed placement.");
        }

        if (context.DestinationItem is not null)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.StaleItem,
                "Split destination item id already exists.");
        }

        if (!string.Equals(context.SourceItem.ItemInstanceId, context.Intent.SourceItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.StaleItem,
                "Source item does not match the split intent.");
        }

        if (context.SourceItem.MaxStackQuantity <= 1)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "Non-stackable items cannot be split.");
        }

        if (context.Intent.Quantity <= 0 || context.Intent.Quantity >= context.SourceItem.Quantity)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "Split quantity must be positive and less than source quantity.");
        }

        InventoryValidationResult placementResult = context.Intent.ProposedPlacement.ValidateForContainerMode(
            context.DestinationContainer.PlacementMode);
        if (!placementResult.IsValid)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidPlacement,
                placementResult.Error);
        }

        InventoryContainerAccessDecision sourceAccess = _accessPolicy.Evaluate(new InventoryContainerAccessRequest(
            context.Intent.Requester,
            context.Requester,
            context.SourceContainer,
            InventoryAccessFlags.Take));
        if (!sourceAccess.IsAllowed)
        {
            return InventoryMutationRuleDecision.Rejected(sourceAccess.FailureReason, sourceAccess.Message);
        }

        InventoryContainerAccessDecision destinationAccess = _accessPolicy.Evaluate(new InventoryContainerAccessRequest(
            context.Intent.Requester,
            context.Requester,
            context.DestinationContainer,
            InventoryAccessFlags.Put));
        if (!destinationAccess.IsAllowed)
        {
            return InventoryMutationRuleDecision.Rejected(destinationAccess.FailureReason, destinationAccess.Message);
        }

        InventoryMutationRuleDecision rangeDecision = _rangePolicy.IsInRange(
            context.Intent.Requester,
            context.SourceContainer,
            context.DestinationContainer);
        if (!rangeDecision.IsAccepted)
        {
            return rangeDecision;
        }

        InventoryItemSnapshot splitItem = CreateSplitItem(context.Intent, context.SourceItem);
        InventoryMutationRuleDecision capacityDecision = _capacityPolicy.CanAccept(
            context.DestinationContainer,
            splitItem,
            splitItem.Placement);
        if (!capacityDecision.IsAccepted)
        {
            return capacityDecision;
        }

        return InventoryMutationRuleDecision.Accepted();
    }

    public static InventoryItemSnapshot CreateSplitItem(
        InventoryMutationIntent intent,
        InventoryItemSnapshot sourceItem)
    {
        return new InventoryItemSnapshot(
            intent.DestinationItemInstanceId!,
            sourceItem.ItemDefinitionId,
            intent.Quantity,
            sourceItem.MaxStackQuantity,
            intent.ProposedPlacement!,
            sourceItem.Flags);
    }
}
