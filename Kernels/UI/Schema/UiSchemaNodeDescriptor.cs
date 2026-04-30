namespace UltraSim.UI;

public sealed record UiSchemaNodeDescriptor(
    int NodeId,
    UiNodeKind Kind,
    string? SlotId,
    string? Text,
    IReadOnlyList<int> Children,
    UiCapabilityFlags RequiredCapabilities = UiCapabilityFlags.None,
    string? ActionId = null)
{
    public UiValidationResult Validate(UiSchemaValidationLimits limits)
    {
        UiValidationResult limitResult = limits.Validate();
        if (!limitResult.IsValid)
        {
            return limitResult;
        }

        if (NodeId <= 0)
        {
            return UiValidationResult.Invalid("Node id must be positive.");
        }

        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Node kind is invalid.");
        }

        if ((RequiredCapabilities & ~UiCapabilitySet.AllKnown) != 0)
        {
            return UiValidationResult.Invalid("Node required capabilities contain unknown values.");
        }

        if (!string.IsNullOrWhiteSpace(SlotId))
        {
            UiValidationResult slotResult = UiValidation.ValidateStableId(SlotId, nameof(SlotId), UiValidation.MaxSlotIdLength);
            if (!slotResult.IsValid)
            {
                return slotResult;
            }
        }

        if (!string.IsNullOrWhiteSpace(ActionId))
        {
            UiValidationResult actionResult = UiValidation.ValidateStableId(ActionId, nameof(ActionId), UiValidation.MaxActionIdLength);
            if (!actionResult.IsValid)
            {
                return actionResult;
            }
        }

        if (Text is not null && Text.Length > limits.MaxTextLength)
        {
            return UiValidationResult.Invalid($"Node text cannot exceed {limits.MaxTextLength} characters.");
        }

        if (Children.Count > limits.MaxChildrenPerNode)
        {
            return UiValidationResult.Invalid($"Node cannot have more than {limits.MaxChildrenPerNode} children.");
        }

        for (int i = 0; i < Children.Count; i++)
        {
            if (Children[i] <= 0)
            {
                return UiValidationResult.Invalid("Child node ids must be positive.");
            }
        }

        return UiValidationResult.Valid;
    }
}
