namespace UltraSim.Server;

public sealed record ServerSettingDescriptor
{
    public ServerSettingDescriptor(
        string key,
        string displayName,
        string value,
        ServerSettingApplyMode applyMode,
        string description = "",
        ServerSettingEditorKind editorKind = ServerSettingEditorKind.Text,
        IEnumerable<ServerSettingChoice>? choices = null)
    {
        Key = NormalizeKey(key);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Key : displayName.Trim();
        Value = value?.Trim() ?? string.Empty;
        ApplyMode = applyMode;
        Description = description?.Trim() ?? string.Empty;
        EditorKind = editorKind;
        Choices = choices?.ToArray() ?? [];

        if (EditorKind == ServerSettingEditorKind.Choice && Choices.Count == 0)
            throw new ArgumentException("Choice settings must declare at least one choice.", nameof(choices));

        if (EditorKind != ServerSettingEditorKind.Choice && Choices.Count != 0)
            throw new ArgumentException("Choices are only valid for choice settings.", nameof(choices));

        if (EditorKind == ServerSettingEditorKind.Choice
            && !Choices.Any(choice => string.Equals(choice.Value, Value, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Choice setting value must match one of the declared choices.", nameof(value));
        }
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string Value { get; }

    public ServerSettingApplyMode ApplyMode { get; }

    public string Description { get; }

    public ServerSettingEditorKind EditorKind { get; }

    public IReadOnlyList<ServerSettingChoice> Choices { get; }

    internal static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim().ToLowerInvariant();
    }
}
