namespace UltraSim.UI;

public sealed record UiTooltipDescriptor(
    string TooltipId,
    string? Title,
    string? IconId,
    UiTooltipVisualRole QualityRole,
    IReadOnlyList<UiTooltipLineDescriptor> Lines,
    string? DetailActionId = null)
{
    public const int MaxTooltipLines = 32;

    public UiValidationResult Validate(UiSchemaCompatibilityContext? context = null)
    {
        UiValidationResult idResult = UiValidation.ValidateStableId(TooltipId, nameof(TooltipId), UiValidation.MaxIdentityLength);
        if (!idResult.IsValid)
        {
            return idResult;
        }

        if (!string.IsNullOrEmpty(Title) && Title.Length > UiValidation.MaxDisplayTextLength)
        {
            return UiValidationResult.Invalid($"Tooltip title cannot exceed {UiValidation.MaxDisplayTextLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(IconId))
        {
            UiValidationResult iconResult = UiValidation.ValidateStableId(IconId, nameof(IconId), 128);
            if (!iconResult.IsValid)
            {
                return iconResult;
            }
        }

        if (!UiValidation.IsDefined(QualityRole))
        {
            return UiValidationResult.Invalid("Tooltip quality role is invalid.");
        }

        if (Lines.Count == 0)
        {
            return UiValidationResult.Invalid("Tooltip requires at least one line.");
        }

        if (Lines.Count > MaxTooltipLines)
        {
            return UiValidationResult.Invalid($"Tooltip cannot contain more than {MaxTooltipLines} lines.");
        }

        for (int i = 0; i < Lines.Count; i++)
        {
            UiValidationResult lineResult = Lines[i].Validate(context);
            if (!lineResult.IsValid)
            {
                return lineResult;
            }
        }

        if (!string.IsNullOrWhiteSpace(DetailActionId))
        {
            UiValidationResult detailResult = UiValidation.ValidateStableId(
                DetailActionId,
                nameof(DetailActionId),
                UiValidation.MaxActionIdLength);
            if (!detailResult.IsValid)
            {
                return detailResult;
            }

            if (context is not null && !context.TryGetAction(DetailActionId!, out _))
            {
                return UiValidationResult.Invalid($"Tooltip detail action '{DetailActionId}' does not exist.");
            }
        }

        return UiValidationResult.Valid;
    }
}
