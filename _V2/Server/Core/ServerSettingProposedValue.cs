namespace UltraSim.Server;

public sealed record ServerSettingProposedValue
{
    public ServerSettingProposedValue(string key, string value)
    {
        Key = ServerSettingDescriptor.NormalizeKey(key);
        Value = value?.Trim() ?? string.Empty;
    }

    public string Key { get; }

    public string Value { get; }
}
