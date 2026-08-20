namespace UltraSim.Server;

public sealed record ServerSettingValidationResult
{
    private ServerSettingValidationResult(bool isValid, string normalizedValue, string error)
    {
        IsValid = isValid;
        NormalizedValue = normalizedValue;
        Error = error;
    }

    public bool IsValid { get; }

    public string NormalizedValue { get; }

    public string Error { get; }

    public static ServerSettingValidationResult Valid(string normalizedValue)
    {
        return new ServerSettingValidationResult(true, normalizedValue, string.Empty);
    }

    public static ServerSettingValidationResult Invalid(string error)
    {
        return new ServerSettingValidationResult(false, string.Empty, error);
    }
}
