namespace UltraSim.UI;

public sealed record UiSelectOptionDescriptor(
    string SlotId,
    string Value,
    string Label,
    bool Disabled = false,
    string? IconId = null)
{
    public UiValidationResult Validate(UiSchemaValidationLimits limits)
    {
        UiValidationResult limitResult = limits.Validate();
        if (!limitResult.IsValid)
        {
            return limitResult;
        }

        UiValidationResult slotResult = UiValidation.ValidateStableId(SlotId, nameof(SlotId), UiValidation.MaxSlotIdLength);
        if (!slotResult.IsValid)
        {
            return slotResult;
        }

        UiValidationResult valueResult = UiValidation.ValidateStableId(Value, nameof(Value), UiValidation.MaxIdentityLength);
        if (!valueResult.IsValid)
        {
            return valueResult;
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            return UiValidationResult.Invalid("Select option label is required.");
        }

        if (Label.Length > limits.MaxTextLength)
        {
            return UiValidationResult.Invalid($"Select option label cannot exceed {limits.MaxTextLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(IconId))
        {
            UiValidationResult iconResult = UiValidation.ValidateStableId(IconId, nameof(IconId), UiValidation.MaxIdentityLength);
            if (!iconResult.IsValid)
            {
                return iconResult;
            }
        }

        return UiValidationResult.Valid;
    }
}
