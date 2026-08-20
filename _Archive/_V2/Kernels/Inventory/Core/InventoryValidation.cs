using System.Globalization;

namespace UltraSim.Inventory;

public static class InventoryValidation
{
    public const int MaxStableIdLength = 160;
    public const int MaxTypeIdLength = 160;
    public const int MaxRuleIdLength = 160;
    public const int MaxTagCount = 64;

    public static InventoryValidationResult ValidateStableId(
        string? value,
        string fieldName,
        int maxLength = MaxStableIdLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return InventoryValidationResult.Invalid($"{fieldName} is required.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            return InventoryValidationResult.Invalid($"{fieldName} cannot exceed {maxLength} characters.");
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
                return InventoryValidationResult.Invalid($"{fieldName} contains invalid character '{c}'.");
            }
        }

        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            return InventoryValidationResult.Invalid($"{fieldName} cannot contain traversal-like path segments.");
        }

        return InventoryValidationResult.Valid;
    }

    public static string NormalizeStableId(string value)
    {
        return value.Trim();
    }

    public static string NormalizeInvariant(string value)
    {
        return value.Trim().ToLower(CultureInfo.InvariantCulture);
    }

    public static bool IsDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(value);
    }
}
