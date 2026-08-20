namespace UltraSim.Server;

public static class ServerAccessKey
{
    public static string NormalizeRuleKey(string key)
        => NormalizeKey(key, "Rule key");

    public static string NormalizePermissionKey(string key)
        => NormalizeKey(key, "Permission key");

    public static string[] NormalizePermissionSet(IEnumerable<string>? keys)
    {
        if (keys is null)
            return [];

        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
            normalized.Add(NormalizePermissionKey(key));

        return normalized.ToArray();
    }

    public static string[] NormalizeRuleSet(IEnumerable<string>? keys)
    {
        if (keys is null)
            return [];

        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
            normalized.Add(NormalizeRuleKey(key));

        return normalized.ToArray();
    }

    public static bool MatchesPermission(string pattern, string permissionKey)
    {
        if (string.Equals(pattern, permissionKey, StringComparison.Ordinal))
            return true;

        if (string.Equals(pattern, "*", StringComparison.Ordinal))
            return true;

        return pattern.EndsWith(".*", StringComparison.Ordinal)
            && permissionKey.StartsWith(pattern[..^1], StringComparison.Ordinal);
    }

    private static string NormalizeKey(string key, string label)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"{label} cannot be empty.", nameof(key));

        string normalized = key.Trim().ToLowerInvariant();
        for (int i = 0; i < normalized.Length; i++)
        {
            if (char.IsWhiteSpace(normalized[i]))
                throw new ArgumentException($"{label} cannot contain whitespace.", nameof(key));
        }

        return normalized;
    }
}
