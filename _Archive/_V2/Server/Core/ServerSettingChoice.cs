namespace UltraSim.Server;

public sealed record ServerSettingChoice
{
    public ServerSettingChoice(string value, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Value : displayName.Trim();
    }

    public string Value { get; }

    public string DisplayName { get; }
}
