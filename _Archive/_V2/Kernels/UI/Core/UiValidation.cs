using System.Globalization;

namespace UltraSim.UI;

public static class UiValidation
{
    public const int Sha256HexLength = 64;
    public const int MaxIdentityLength = 160;
    public const int MaxPanelTypeLength = 160;
    public const int MaxCatalogTypeLength = 160;
    public const int MaxSlotIdLength = 96;
    public const int MaxActionIdLength = 96;
    public const int MaxDisplayTextLength = 512;

    public static UiValidationResult ValidateStableId(
        string? value,
        string fieldName,
        int maxLength = MaxIdentityLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiValidationResult.Invalid($"{fieldName} is required.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            return UiValidationResult.Invalid($"{fieldName} cannot exceed {maxLength} characters.");
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool valid =
                c is >= 'a' and <= 'z' ||
                c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c is '.' or '_' or '-' or ':' or '/';
            if (!valid)
            {
                return UiValidationResult.Invalid($"{fieldName} contains invalid character '{c}'.");
            }
        }

        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            return UiValidationResult.Invalid($"{fieldName} cannot contain traversal-like path segments.");
        }

        return UiValidationResult.Valid;
    }

    public static UiValidationResult ValidateSha256Hex(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiValidationResult.Invalid($"{fieldName} is required.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length != Sha256HexLength)
        {
            return UiValidationResult.Invalid($"{fieldName} must be {Sha256HexLength} hexadecimal characters.");
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (!Uri.IsHexDigit(trimmed[i]))
            {
                return UiValidationResult.Invalid($"{fieldName} must be hexadecimal.");
            }
        }

        return UiValidationResult.Valid;
    }

    public static string NormalizeStableId(string value)
    {
        return value.Trim();
    }

    public static string NormalizeHash(string value)
    {
        return value.Trim().ToLower(CultureInfo.InvariantCulture);
    }

    public static bool IsDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(value);
    }
}
