namespace UltraSim.Server.Scripting;

public sealed record UltraSimRuntimeContentGroupDescriptor
{
    public UltraSimRuntimeContentGroupDescriptor(
        string key,
        UltraSimRuntimeContentGroupKind kind,
        string displayName,
        string description = "",
        string originPath = "",
        string version = "")
    {
        ScriptDefinitionValidation.ValidateStableKey(key, nameof(key));
        Key = ScriptDefinitionValidation.NormalizeKey(key);
        Kind = kind;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Key : displayName.Trim();
        Description = description?.Trim() ?? string.Empty;
        OriginPath = originPath?.Trim() ?? string.Empty;
        Version = version?.Trim() ?? string.Empty;
    }

    public string Key { get; }

    public UltraSimRuntimeContentGroupKind Kind { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string OriginPath { get; }

    public string Version { get; }
}
