namespace UltraSim.Server;

public sealed class ServerSettingsConfigDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    public static ServerSettingsConfigDocument Empty()
    {
        return new ServerSettingsConfigDocument();
    }

    public bool TryGetValue(string key, out string value)
    {
        string normalized = ServerSettingDescriptor.NormalizeKey(key);
        return Values.TryGetValue(normalized, out value!);
    }

    public void SetValue(string key, string value)
    {
        string normalized = ServerSettingDescriptor.NormalizeKey(key);
        Values[normalized] = value?.Trim() ?? string.Empty;
    }

    public void ApplyChanges(IEnumerable<ServerSettingValidatedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (ServerSettingValidatedChange change in changes)
        {
            SetValue(change.Key, change.NormalizedValue);
        }
    }

    public IReadOnlyList<ServerSettingProposedValue> ToProposedValues()
    {
        var values = new List<ServerSettingProposedValue>(Values.Count);
        foreach (KeyValuePair<string, string> pair in Values)
        {
            values.Add(new ServerSettingProposedValue(pair.Key, pair.Value));
        }

        return values;
    }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in Values)
        {
            string key = ServerSettingDescriptor.NormalizeKey(pair.Key);
            normalized[key] = pair.Value?.Trim() ?? string.Empty;
        }

        Values = normalized;
    }
}
