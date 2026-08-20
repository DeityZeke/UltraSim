namespace UltraSim.Server;

public sealed record ServerAccessRuleDescriptor
{
    public ServerAccessRuleDescriptor(
        string key,
        string displayName,
        string description = "",
        IEnumerable<string>? grants = null,
        IEnumerable<string>? denies = null)
    {
        Key = ServerAccessKey.NormalizeRuleKey(key);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Key : displayName.Trim();
        Description = description?.Trim() ?? string.Empty;
        Grants = ServerAccessKey.NormalizePermissionSet(grants);
        Denies = ServerAccessKey.NormalizePermissionSet(denies);
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public IReadOnlyList<string> Grants { get; }

    public IReadOnlyList<string> Denies { get; }
}
