using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public readonly record struct InventoryMutationRuleDecision(
    InventoryMutationResultKind ResultKind,
    InventoryMutationFailureReason FailureReason,
    string? Message)
{
    public bool IsAccepted => ResultKind == InventoryMutationResultKind.Accepted;

    public static InventoryMutationRuleDecision Accepted()
    {
        return new InventoryMutationRuleDecision(
            InventoryMutationResultKind.Accepted,
            InventoryMutationFailureReason.None,
            Message: null);
    }

    public static InventoryMutationRuleDecision Pending(string? message = null)
    {
        return new InventoryMutationRuleDecision(
            InventoryMutationResultKind.Pending,
            InventoryMutationFailureReason.None,
            message);
    }

    public static InventoryMutationRuleDecision NoOp(string? message = null)
    {
        return new InventoryMutationRuleDecision(
            InventoryMutationResultKind.NoOp,
            InventoryMutationFailureReason.None,
            message);
    }

    public static InventoryMutationRuleDecision Rejected(
        InventoryMutationFailureReason reason,
        string? message = null)
    {
        if (reason == InventoryMutationFailureReason.None)
        {
            throw new ArgumentException("Rejected mutation decisions require a failure reason.", nameof(reason));
        }

        return new InventoryMutationRuleDecision(InventoryMutationResultKind.Rejected, reason, message);
    }

    public InventoryMutationResult ToResult(InventoryMutationIntent intent, IReadOnlyList<string> affectedContainerIds)
    {
        return new InventoryMutationResult(
            intent.CorrelationId,
            intent.ClientSequence,
            intent.Kind,
            ResultKind,
            FailureReason,
            Message,
            affectedContainerIds);
    }
}
