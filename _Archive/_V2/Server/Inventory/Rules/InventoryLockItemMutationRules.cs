using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class InventoryLockItemMutationRules : IInventoryMutationRules
{
    private readonly IInventoryContainerAccessPolicy _accessPolicy;
    private readonly IInventoryItemLockPolicy _lockPolicy;
    private readonly IInventoryInteractionRangePolicy _rangePolicy;

    public InventoryLockItemMutationRules(
        IInventoryContainerAccessPolicy accessPolicy,
        IInventoryItemLockPolicy lockPolicy,
        IInventoryInteractionRangePolicy rangePolicy)
    {
        _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _lockPolicy = lockPolicy ?? throw new ArgumentNullException(nameof(lockPolicy));
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

        if (context.Intent.Kind is not (InventoryMutationKind.LockItem or InventoryMutationKind.UnlockItem))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.RuleRejected,
                "Only LockItem and UnlockItem are supported by InventoryLockItemMutationRules.");
        }

        if (!context.Requester.IsResolved)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidRequester,
                "Requester did not resolve.");
        }

        if (context.SourceContainer is null || context.SourceItem is null)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidContainer,
                "Lock state changes require a source container and source item.");
        }

        if (!string.Equals(context.SourceItem.ItemInstanceId, context.Intent.SourceItemInstanceId, StringComparison.Ordinal))
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.StaleItem,
                "Source item does not match the lock-state intent.");
        }

        if (context.Intent.Quantity != 1)
        {
            return InventoryMutationRuleDecision.Rejected(
                InventoryMutationFailureReason.InvalidQuantity,
                "The first lock-state rule expects quantity 1 and mutates the whole item stack.");
        }

        InventoryContainerAccessDecision access = _accessPolicy.Evaluate(new InventoryContainerAccessRequest(
            context.Intent.Requester,
            context.Requester,
            context.SourceContainer,
            InventoryAccessFlags.Mutate));
        if (!access.IsAllowed)
        {
            return InventoryMutationRuleDecision.Rejected(access.FailureReason, access.Message);
        }

        InventoryMutationRuleDecision rangeDecision = _rangePolicy.IsInRange(
            context.Intent.Requester,
            context.SourceContainer,
            destination: null);
        if (!rangeDecision.IsAccepted)
        {
            return rangeDecision;
        }

        bool targetLocked = context.Intent.Kind == InventoryMutationKind.LockItem;
        bool alreadyTargetState = ((context.SourceItem.Flags & InventoryItemFlags.Locked) != 0) == targetLocked;
        if (alreadyTargetState)
        {
            return InventoryMutationRuleDecision.NoOp(targetLocked
                ? "Item is already locked."
                : "Item is already unlocked.");
        }

        return _lockPolicy.CanChangeLockState(
            context.Intent.Requester,
            context.SourceItem,
            context.SourceContainer,
            targetLocked);
    }
}
