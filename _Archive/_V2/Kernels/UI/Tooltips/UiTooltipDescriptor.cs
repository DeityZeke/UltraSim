namespace UltraSim.UI;

public sealed record UiTooltipDescriptor(
    string TooltipId,
    string? Title,
    string? IconId,
    UiTooltipVisualRole QualityRole,
    IReadOnlyList<UiTooltipLineDescriptor> Lines)
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

        return UiValidationResult.Valid;
    }
}
