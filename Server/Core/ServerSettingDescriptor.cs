namespace UltraSim.Server;

public sealed record ServerSettingDescriptor
{
    public ServerSettingDescriptor(
        string key,
        string displayName,
        string value,
        ServerSettingApplyMode applyMode,
        string description = "")
    {
        Key = NormalizeKey(key);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Key : displayName.Trim();
        Value = value?.Trim() ?? string.Empty;
        ApplyMode = applyMode;
        Description = description?.Trim() ?? string.Empty;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string Value { get; }

    public ServerSettingApplyMode ApplyMode { get; }

    public string Description { get; }

    internal static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim().ToLowerInvariant();
    }
}
