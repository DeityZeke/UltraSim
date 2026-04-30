namespace UltraSim.UI;

public sealed record UiActionMenuDescriptor(
    string MenuKey,
    UiActionMenuPresentationKind PresentationKind,
    UiActionContextKind ContextKind,
    string? ContextId,
    IReadOnlyList<UiActionDescriptor> Actions,
    bool CloseOnFocusLost = true)
{
    public const int MaxMenuActions = 64;

    public UiValidationResult Validate(UiRendererCapabilities? rendererCapabilities = null)
    {
        UiValidationResult menuResult = UiValidation.ValidateStableId(MenuKey, nameof(MenuKey), 96);
        if (!menuResult.IsValid)
        {
            return menuResult;
        }

        if (!UiValidation.IsDefined(PresentationKind))
        {
            return UiValidationResult.Invalid("Action menu presentation kind is invalid.");
        }

        if (!UiValidation.IsDefined(ContextKind))
        {
            return UiValidationResult.Invalid("Action menu context kind is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(ContextId))
        {
            UiValidationResult contextResult = UiValidation.ValidateStableId(ContextId, nameof(ContextId));
            if (!contextResult.IsValid)
            {
                return contextResult;
            }
        }

        if (Actions.Count == 0)
        {
            return UiValidationResult.Invalid("Action menu requires at least one action.");
        }

        if (Actions.Count > MaxMenuActions)
        {
            return UiValidationResult.Invalid($"Action menu cannot contain more than {MaxMenuActions} actions.");
        }

        if (rendererCapabilities is not null)
        {
            UiCapabilityFlags required = PresentationKind == UiActionMenuPresentationKind.RadialMenu
                ? UiCapabilityFlags.RadialMenusV1
                : UiCapabilityFlags.ContextMenusV1;
            if ((rendererCapabilities.CapabilityFlags & required) != required)
            {
                return UiValidationResult.Invalid($"Renderer capabilities do not include {required}.");
            }
        }

        for (int i = 0; i < Actions.Count; i++)
        {
            UiActionDescriptor action = Actions[i];
            UiValidationResult actionResult = action.Validate();
            if (!actionResult.IsValid)
            {
                return actionResult;
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(Actions[j].ActionId, action.ActionId, StringComparison.Ordinal))
                {
                    return UiValidationResult.Invalid($"Action menu contains duplicate action id '{action.ActionId}'.");
                }
            }
        }

        return UiValidationResult.Valid;
    }
}
