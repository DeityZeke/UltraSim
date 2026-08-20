namespace UltraSim.UI;

public readonly record struct UiValidationResult(bool IsValid, string Error)
{
    public static UiValidationResult Valid { get; } = new(true, string.Empty);

    public static UiValidationResult Invalid(string error)
    {
        return new UiValidationResult(false, string.IsNullOrWhiteSpace(error) ? "Validation failed." : error);
    }
}
