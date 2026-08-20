namespace UltraSim.UI;

public sealed record UiStateBindingDescriptor(
    string SlotId,
    string SourceId,
    string TargetId,
    string FieldId,
    UiStateBindingFallback Fallback)
{
    public UiValidationResult Validate()
    {
        UiValidationResult slotResult = UiValidation.ValidateStableId(SlotId, nameof(SlotId), UiValidation.MaxSlotIdLength);
        if (!slotResult.IsValid)
        {
            return slotResult;
        }

        UiValidationResult sourceResult = UiValidation.ValidateStableId(SourceId, nameof(SourceId), 96);
        if (!sourceResult.IsValid)
        {
            return sourceResult;
        }

        UiValidationResult targetResult = UiValidation.ValidateStableId(TargetId, nameof(TargetId), 128);
        if (!targetResult.IsValid)
        {
            return targetResult;
        }

        UiValidationResult fieldResult = UiValidation.ValidateStableId(FieldId, nameof(FieldId), 128);
        if (!fieldResult.IsValid)
        {
            return fieldResult;
        }

        return Fallback.Validate();
    }
}
