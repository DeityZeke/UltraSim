using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class InventoryMergeStackMutationRules : IInventoryMutationRules
{
    private readonly IInventoryContainerAccessPolicy _accessPolicy;
    private readonly IInventoryInteractionRangePolicy _rangePolicy;
    private readonly IInventoryStackMergePolicy _mergePolicy;

    public InventoryMergeStackMutationRules(
        IInventoryContainerAccessPolicy accessPolicy,
        IInventoryInteractionRangePolicy rangePolicy,
        IInventoryStackMergePolicy mergePolicy)
    {
        _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _rangePolicy = rangePolicy ?? throw new ArgumentNullException(nameof(rangePolicy));
        _mergePolicy = mergePolicy ?? throw new ArgumentNullException(nameof(mergePolicy));
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

        if (context.Intent.Kind != InventoryMutationKind.MergeStack)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Only MergeStack is supported by InventoryMergeStackMutationRules.");
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
            context.DestinationItem is null)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidContainer,
                "Merge requires source/destination containers and source/destination items.");
        }

        if (!string.Equals(context.SourceItem.ItemInstanceId, context.Intent.SourceItemInstanceId, StringComparison.Ordinal) ||
            !string.Equals(context.DestinationItem.ItemInstanceId, context.Intent.DestinationItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.StaleItem,
                "Merge items do not match the merge intent.");
        }

        if (string.Equals(context.SourceItem.ItemInstanceId, context.DestinationItem.ItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.Conflict,
                "Cannot merge an item stack into itself.");
        }

        if (context.Intent.Quantity <= 0 || context.Intent.Quantity > context.SourceItem.Quantity)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "Merge quantity must be positive and cannot exceed source quantity.");
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

        InventoryStackMergeDecision merge = _mergePolicy.Evaluate(new InventoryStackMergeRequest(
            context.Intent.Requester,
            context.SourceItem,
            context.DestinationItem,
            context.Intent.Quantity,
            context.SourceContainer,
            context.DestinationContainer));
        if (!merge.CanMerge)
        {
            return InventoryMutationRuleDecision.Rejected(merge.FailureReason, merge.Message);
        }

        if (merge.QuantityToMove != context.Intent.Quantity)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "Merge policy returned a transfer quantity different from the merge intent.");
        }

        return InventoryMutationRuleDecision.Accepted();
    }
}
