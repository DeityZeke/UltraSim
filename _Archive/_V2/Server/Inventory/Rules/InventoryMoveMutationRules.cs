using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class InventoryMoveMutationRules : IInventoryMutationRules
{
    private readonly IInventoryContainerAccessPolicy _accessPolicy;
    private readonly IInventoryContainerCapacityPolicy _capacityPolicy;
    private readonly IInventoryInteractionRangePolicy _rangePolicy;

    public InventoryMoveMutationRules(
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

        if (context.Intent.Kind != InventoryMutationKind.MoveItem)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Only MoveItem is supported by InventoryMoveMutationRules.");
        }

        if (!context.Requester.IsResolved)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidRequester,
                "Requester did not resolve.");
        }

        if (context.SourceContainer is null || context.DestinationContainer is null || context.SourceItem is null)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidContainer,
                "Move requires source container, destination container, and source item.");
        }

        if (!string.Equals(context.SourceItem.ItemInstanceId, context.Intent.SourceItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.StaleItem,
                "Source item does not match the move intent.");
        }

        if (context.Intent.Quantity != context.SourceItem.Quantity)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "The first move rule only supports whole-item moves.");
        }

        InventoryPlacementDescriptor destinationPlacement =
            context.Intent.ProposedPlacement ?? context.SourceItem.Placement;
        InventoryValidationResult placementResult = destinationPlacement.ValidateForContainerMode(
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

        InventoryMutationRuleDecision capacityDecision = _capacityPolicy.CanAccept(
            context.DestinationContainer,
            context.SourceItem,
            destinationPlacement);
        if (!capacityDecision.IsAccepted)
        {
            return capacityDecision;
        }

        return InventoryMutationRuleDecision.Accepted();
    }
}
