namespace UltraSim.UI;

public sealed record UiTooltipLineDescriptor(
    UiTooltipLineKind Kind,
    string? Label,
    string? Text,
    string? SlotId,
    UiTooltipVisualRole Role = UiTooltipVisualRole.Normal)
{
    public UiValidationResult Validate(UiSchemaCompatibilityContext? context = null)
    {
        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Tooltip line kind is invalid.");
        }

        if (!UiValidation.IsDefined(Role))
        {
            return UiValidationResult.Invalid("Tooltip visual role is invalid.");
        }

        if (!string.IsNullOrEmpty(Label) && Label.Length > UiValidation.MaxDisplayTextLength)
        {
            return UiValidationResult.Invalid($"Tooltip line label cannot exceed {UiValidation.MaxDisplayTextLength} characters.");
        }

        if (!string.IsNullOrEmpty(Text) && Text.Length > UiValidation.MaxDisplayTextLength)
        {
            return UiValidationResult.Invalid($"Tooltip line text cannot exceed {UiValidation.MaxDisplayTextLength} characters.");
        }

        bool hasText = !string.IsNullOrWhiteSpace(Text);
        bool hasSlot = !string.IsNullOrWhiteSpace(SlotId);
        if (!hasText && !hasSlot)
        {
            return UiValidationResult.Invalid("Tooltip line requires text or a slot id.");
        }

        if (hasSlot)
        {
            UiValidationResult slotResult = UiValidation.ValidateStableId(SlotId, nameof(SlotId), UiValidation.MaxSlotIdLength);
            if (!slotResult.IsValid)
            {
                return slotResult;
            }

            if (context is not null &&
                !context.TryGetPayloadSlot(SlotId!, out _) &&
                !context.HasStateBindingSlot(SlotId!))
            {
                return UiValidationResult.Invalid($"Tooltip line references unknown slot '{SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }
}
