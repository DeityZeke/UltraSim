namespace UltraSim.UI;

public sealed record UiRepeaterRowDelta(
    string RepeaterSlotId,
    string RowKey,
    UiRepeaterRowUpdateKind Kind,
    IReadOnlyList<UiModelSlotValue> Values)
{
    public UiValidationResult Validate()
    {
        UiValidationResult repeaterSlotResult = UiValidation.ValidateStableId(
            RepeaterSlotId,
            nameof(RepeaterSlotId),
            UiValidation.MaxSlotIdLength);
        if (!repeaterSlotResult.IsValid)
        {
            return repeaterSlotResult;
        }

        UiValidationResult rowKeyResult = UiValidation.ValidateStableId(
            RowKey,
            nameof(RowKey),
            UiValidation.MaxSlotIdLength);
        if (!rowKeyResult.IsValid)
        {
            return rowKeyResult;
        }

        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Repeater row update kind is invalid.");
        }

        if (Kind == UiRepeaterRowUpdateKind.Remove && Values.Count != 0)
        {
            return UiValidationResult.Invalid("Repeater row remove updates cannot contain values.");
        }

        if (Kind != UiRepeaterRowUpdateKind.Remove && Values.Count == 0)
        {
            return UiValidationResult.Invalid("Repeater row add/update/replace updates require values.");
        }

        var slotIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Values.Count; i++)
        {
            UiModelSlotValue value = Values[i];
            UiValidationResult valueResult = value.Validate();
            if (!valueResult.IsValid)
            {
                return valueResult;
            }

            if (!slotIds.Add(value.SlotId))
            {
                return UiValidationResult.Invalid($"Repeater row '{RowKey}' contains duplicate slot '{value.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }
}
