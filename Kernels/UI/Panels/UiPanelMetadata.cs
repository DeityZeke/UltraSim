namespace UltraSim.UI;

public sealed record UiPanelMetadata(
    string PanelType,
    UiSurfaceTarget SurfaceTarget,
    UiFocusMode FocusMode,
    string? ExclusiveGroup,
    int ZOrderClass,
    UiOpenPolicy OpenPolicy,
    UiBackPolicy BackPolicy,
    UiTooltipPolicy TooltipPolicy,
    bool BlocksGameplayInput,
    string? PersistenceKey)
{
    public UiValidationResult Validate()
    {
        UiValidationResult panelResult = UiValidation.ValidateStableId(PanelType, nameof(PanelType), UiValidation.MaxPanelTypeLength);
        if (!panelResult.IsValid)
        {
            return panelResult;
        }

        if (!UiValidation.IsDefined(SurfaceTarget))
        {
            return UiValidationResult.Invalid("Surface target is invalid.");
        }

        if (!UiValidation.IsDefined(FocusMode))
        {
            return UiValidationResult.Invalid("Focus mode is invalid.");
        }

        if (!UiValidation.IsDefined(OpenPolicy))
        {
            return UiValidationResult.Invalid("Open policy is invalid.");
        }

        if (!UiValidation.IsDefined(BackPolicy))
        {
            return UiValidationResult.Invalid("Back policy is invalid.");
        }

        if (!UiValidation.IsDefined(TooltipPolicy))
        {
            return UiValidationResult.Invalid("Tooltip policy is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(ExclusiveGroup))
        {
            UiValidationResult groupResult = UiValidation.ValidateStableId(ExclusiveGroup, nameof(ExclusiveGroup), 96);
            if (!groupResult.IsValid)
            {
                return groupResult;
            }
        }

        if (!string.IsNullOrWhiteSpace(PersistenceKey))
        {
            UiValidationResult persistenceResult = UiValidation.ValidateStableId(PersistenceKey, nameof(PersistenceKey), 128);
            if (!persistenceResult.IsValid)
            {
                return persistenceResult;
            }
        }

        return UiValidationResult.Valid;
    }
}
