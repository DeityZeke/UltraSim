using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class InventoryEquipItemMutationRules : IInventoryMutationRules
{
    private readonly IInventoryContainerAccessPolicy _accessPolicy;
    private readonly IInventoryContainerCapacityPolicy _capacityPolicy;
    private readonly IInventoryEquipmentSlotPolicy _equipmentSlotPolicy;
    private readonly IInventoryInteractionRangePolicy _rangePolicy;

    public InventoryEquipItemMutationRules(
        IInventoryContainerAccessPolicy accessPolicy,
        IInventoryContainerCapacityPolicy capacityPolicy,
        IInventoryEquipmentSlotPolicy equipmentSlotPolicy,
        IInventoryInteractionRangePolicy rangePolicy)
    {
        _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _capacityPolicy = capacityPolicy ?? throw new ArgumentNullException(nameof(capacityPolicy));
        _equipmentSlotPolicy = equipmentSlotPolicy ?? throw new ArgumentNullException(nameof(equipmentSlotPolicy));
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

        if (context.Intent.Kind != InventoryMutationKind.EquipItem)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Only EquipItem is supported by InventoryEquipItemMutationRules.");
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
            context.Intent.ProposedPlacement is null)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidContainer,
                "Equip requires source/destination containers, source item, and slot placement.");
        }

        if (!string.Equals(context.SourceItem.ItemInstanceId, context.Intent.SourceItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.StaleItem,
                "Source item does not match the equip intent.");
        }

        if (context.Intent.Quantity != context.SourceItem.Quantity)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "The first equip rule only supports whole-item equips.");
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

        InventoryMutationRuleDecision slotDecision = _equipmentSlotPolicy.CanEquip(
            context.Intent.Requester,
            context.SourceItem,
            context.DestinationContainer,
            context.Intent.ProposedPlacement);
        if (!slotDecision.IsAccepted)
        {
            return slotDecision;
        }

        InventoryMutationRuleDecision capacityDecision = _capacityPolicy.CanAccept(
            context.DestinationContainer,
            context.SourceItem,
            context.Intent.ProposedPlacement);
        if (!capacityDecision.IsAccepted)
        {
            return capacityDecision;
        }

        return InventoryMutationRuleDecision.Accepted();
    }
}
