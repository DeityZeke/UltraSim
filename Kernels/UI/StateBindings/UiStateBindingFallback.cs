namespace UltraSim.UI;

public sealed record UiStateBindingFallback(
    UiBindingFallbackBehavior WhenMissing,
    UiBindingFallbackBehavior WhenUnauthorized,
    UiBindingFallbackBehavior WhenDead,
    int StaleAfterMilliseconds)
{
    public UiValidationResult Validate()
    {
        if (!UiValidation.IsDefined(WhenMissing) ||
            !UiValidation.IsDefined(WhenUnauthorized) ||
            !UiValidation.IsDefined(WhenDead))
        {
            return UiValidationResult.Invalid("Binding fallback behavior is invalid.");
        }

        if (StaleAfterMilliseconds < 0)
        {
            return UiValidationResult.Invalid("Binding stale timeout cannot be negative.");
        }

        return UiValidationResult.Valid;
    }
}
