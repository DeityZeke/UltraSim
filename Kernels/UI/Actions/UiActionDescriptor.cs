namespace UltraSim.UI;

public sealed record UiActionDescriptor(
    string ActionId,
    string Label,
    string? IconId,
    UiActionRole Role,
    UiActionContextKind ContextKind,
    bool RequiresConfirm,
    string? ChildMenuKey,
    UiActionAvailability Availability = UiActionAvailability.Available,
    string? AvailabilityReason = null)
{
    public UiValidationResult Validate()
    {
        UiValidationResult actionResult = UiValidation.ValidateStableId(ActionId, nameof(ActionId), UiValidation.MaxActionIdLength);
        if (!actionResult.IsValid)
        {
            return actionResult;
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            return UiValidationResult.Invalid("Action label is required.");
        }

        if (Label.Length > UiValidation.MaxDisplayTextLength)
        {
            return UiValidationResult.Invalid($"Action label cannot exceed {UiValidation.MaxDisplayTextLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(IconId))
        {
            UiValidationResult iconResult = UiValidation.ValidateStableId(IconId, nameof(IconId), 128);
            if (!iconResult.IsValid)
            {
                return iconResult;
            }
        }

        if (!UiValidation.IsDefined(Role))
        {
            return UiValidationResult.Invalid("Action role is invalid.");
        }

        if (!UiValidation.IsDefined(ContextKind))
        {
            return UiValidationResult.Invalid("Action context kind is invalid.");
        }

        if (!UiValidation.IsDefined(Availability))
        {
            return UiValidationResult.Invalid("Action availability is invalid.");
        }

        if (!string.IsNullOrEmpty(AvailabilityReason) && AvailabilityReason.Length > UiValidation.MaxDisplayTextLength)
        {
            return UiValidationResult.Invalid($"Action availability reason cannot exceed {UiValidation.MaxDisplayTextLength} characters.");
        }

        if (Availability == UiActionAvailability.Available && !string.IsNullOrWhiteSpace(AvailabilityReason))
        {
            return UiValidationResult.Invalid("Available actions cannot carry an availability reason.");
        }

        if (!string.IsNullOrWhiteSpace(ChildMenuKey))
        {
            UiValidationResult childResult = UiValidation.ValidateStableId(ChildMenuKey, nameof(ChildMenuKey), 96);
            if (!childResult.IsValid)
            {
                return childResult;
            }
        }

        return UiValidationResult.Valid;
    }
}
