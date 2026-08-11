using System.Globalization;

namespace UltraSim.Server;

public static class ServerSettingValueValidator
{
    public static ServerSettingValidationResult Validate(ServerSettingDescriptor descriptor, string? value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        string trimmed = value?.Trim() ?? string.Empty;
        return descriptor.EditorKind switch
        {
            ServerSettingEditorKind.Text => ServerSettingValidationResult.Valid(trimmed),
            ServerSettingEditorKind.Integer => ValidateInteger(trimmed),
            ServerSettingEditorKind.Decimal => ValidateDecimal(trimmed),
            ServerSettingEditorKind.Boolean => ValidateBoolean(trimmed),
            ServerSettingEditorKind.Choice => ValidateChoice(descriptor, trimmed),
            ServerSettingEditorKind.Path => ValidatePath(trimmed),
            _ => ServerSettingValidationResult.Invalid($"Unsupported setting editor kind '{descriptor.EditorKind}'.")
        };
    }

    private static ServerSettingValidationResult ValidateInteger(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? ServerSettingValidationResult.Valid(parsed.ToString(CultureInfo.InvariantCulture))
            : ServerSettingValidationResult.Invalid("Enter a whole number.");
    }

    private static ServerSettingValidationResult ValidateDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
            ? ServerSettingValidationResult.Valid(parsed.ToString(CultureInfo.InvariantCulture))
            : ServerSettingValidationResult.Invalid("Enter a number.");
    }

    private static ServerSettingValidationResult ValidateBoolean(string value)
    {
        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal))
        {
            return ServerSettingValidationResult.Valid("on");
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "0", StringComparison.Ordinal))
        {
            return ServerSettingValidationResult.Valid("off");
        }

        return ServerSettingValidationResult.Invalid("Enter on or off.");
    }

    private static ServerSettingValidationResult ValidateChoice(ServerSettingDescriptor descriptor, string value)
    {
        for (int i = 0; i < descriptor.Choices.Count; i++)
        {
            ServerSettingChoice choice = descriptor.Choices[i];
            if (string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
                return ServerSettingValidationResult.Valid(choice.Value);
        }

        return ServerSettingValidationResult.Invalid("Select one of the available choices.");
    }

    private static ServerSettingValidationResult ValidatePath(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ServerSettingValidationResult.Invalid("Enter a path.")
            : ServerSettingValidationResult.Valid(value);
    }
}
