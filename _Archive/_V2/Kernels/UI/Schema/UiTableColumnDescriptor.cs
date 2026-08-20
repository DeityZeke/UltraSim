namespace UltraSim.UI;

public sealed record UiTableColumnDescriptor(
    string TableSlotId,
    string CellSlotId,
    string Label,
    UiModelValueKind ValueKind,
    bool Sortable = false,
    string? SortActionId = null,
    string? IconId = null)
{
    public UiValidationResult Validate(UiSchemaValidationLimits limits)
    {
        UiValidationResult limitResult = limits.Validate();
        if (!limitResult.IsValid)
        {
            return limitResult;
        }

        UiValidationResult tableSlotResult = UiValidation.ValidateStableId(
            TableSlotId,
            nameof(TableSlotId),
            UiValidation.MaxSlotIdLength);
        if (!tableSlotResult.IsValid)
        {
            return tableSlotResult;
        }

        UiValidationResult cellSlotResult = UiValidation.ValidateStableId(
            CellSlotId,
            nameof(CellSlotId),
            UiValidation.MaxSlotIdLength);
        if (!cellSlotResult.IsValid)
        {
            return cellSlotResult;
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            return UiValidationResult.Invalid("Label is required.");
        }

        if (Label.Length > limits.MaxTextLength)
        {
            return UiValidationResult.Invalid($"Label cannot exceed {limits.MaxTextLength} characters.");
        }

        if (!UiValidation.IsDefined(ValueKind))
        {
            return UiValidationResult.Invalid("Table column value kind is invalid.");
        }

        if (Sortable && string.IsNullOrWhiteSpace(SortActionId))
        {
            return UiValidationResult.Invalid("Sortable table columns require a sort action id.");
        }

        if (!Sortable && !string.IsNullOrWhiteSpace(SortActionId))
        {
            return UiValidationResult.Invalid("Table columns cannot declare a sort action id unless sortable.");
        }

        if (!string.IsNullOrWhiteSpace(SortActionId))
        {
            UiValidationResult actionResult = UiValidation.ValidateStableId(
                SortActionId,
                nameof(SortActionId),
                UiValidation.MaxActionIdLength);
            if (!actionResult.IsValid)
            {
                return actionResult;
            }
        }

        if (!string.IsNullOrWhiteSpace(IconId))
        {
            UiValidationResult iconResult = UiValidation.ValidateStableId(
                IconId,
                nameof(IconId),
                UiValidation.MaxIdentityLength);
            if (!iconResult.IsValid)
            {
                return iconResult;
            }
        }

        return UiValidationResult.Valid;
    }
}
