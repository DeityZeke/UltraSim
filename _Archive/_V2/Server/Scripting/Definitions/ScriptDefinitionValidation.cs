using UltraSim.ECS;

namespace UltraSim.Server.Scripting;

internal static class ScriptDefinitionValidation
{
    public static void ValidateStableKey(string key, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"{fieldName} cannot be empty.", fieldName);

        string trimmed = key.Trim();
        if (trimmed.Length > 160)
            throw new ArgumentException($"{fieldName} cannot exceed 160 characters.", fieldName);

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool valid =
                c is >= 'a' and <= 'z' ||
                c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c is '.' or '_' or '-' or ':' or '/';
            if (!valid)
                throw new ArgumentException($"{fieldName} contains invalid character '{c}'.", fieldName);
        }

        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.StartsWith("/", StringComparison.Ordinal) ||
            trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{fieldName} cannot contain traversal-like path segments.", fieldName);
        }
    }

    public static void ValidateComponentType(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        if (!componentType.IsValueType)
            throw new ArgumentException($"Component type {componentType.FullName} must be a struct.", nameof(componentType));

        if (!ImplementsIComponentOfSelf(componentType))
        {
            throw new ArgumentException(
                $"Component type {componentType.FullName} must implement IComponent<{componentType.Name}>.",
                nameof(componentType));
        }
    }

    public static string NormalizeKey(string key) => key.Trim();

    private static bool ImplementsIComponentOfSelf(Type componentType)
    {
        Type interfaceType = typeof(IComponent<>);
        foreach (Type candidate in componentType.GetInterfaces())
        {
            if (!candidate.IsGenericType)
                continue;

            if (candidate.GetGenericTypeDefinition() != interfaceType)
                continue;

            Type[] args = candidate.GetGenericArguments();
            if (args.Length == 1 && args[0] == componentType)
                return true;
        }

        return false;
    }
}
