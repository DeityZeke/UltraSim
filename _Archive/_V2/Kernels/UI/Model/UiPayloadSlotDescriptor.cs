namespace UltraSim.UI;

public sealed record UiPayloadSlotDescriptor(
    string SlotId,
    UiModelValueKind ValueKind,
    bool Required,
    int MaxStringLength = UiValidation.MaxDisplayTextLength,
    double? MinNumber = null,
    double? MaxNumber = null)
{
    public UiValidationResult Validate()
    {
        UiValidationResult slotResult = UiValidation.ValidateStableId(SlotId, nameof(SlotId), UiValidation.MaxSlotIdLength);
        if (!slotResult.IsValid)
        {
            return slotResult;
        }

        if (!UiValidation.IsDefined(ValueKind))
        {
            return UiValidationResult.Invalid("Payload slot value kind is invalid.");
        }

        if (ValueKind == UiModelValueKind.Null)
        {
            return UiValidationResult.Invalid("Payload slot descriptor cannot require Null value kind.");
        }

        if (MaxStringLength <= 0)
        {
            return UiValidationResult.Invalid("Payload slot max string length must be positive.");
        }

        if (MinNumber.HasValue &&
            MaxNumber.HasValue &&
            MinNumber.Value > MaxNumber.Value)
        {
            return UiValidationResult.Invalid("Payload slot min number cannot exceed max number.");
        }

        return UiValidationResult.Valid;
    }
}
