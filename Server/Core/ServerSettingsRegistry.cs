namespace UltraSim.Server;

public sealed class ServerSettingsRegistry
{
    private readonly List<ServerSettingDescriptor> _settings = new();

    public int Count => _settings.Count;

    public bool TryRegister(ServerSettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (IndexOf(descriptor.Key) >= 0)
            return false;

        _settings.Add(descriptor);
        return true;
    }

    public void RegisterOrReplace(ServerSettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        int index = IndexOf(descriptor.Key);
        if (index >= 0)
        {
            _settings[index] = descriptor;
            return;
        }

        _settings.Add(descriptor);
    }

    public void RegisterApplyWithoutRestart(
        string key,
        string displayName,
        string value,
        string description = "",
        ServerSettingEditorKind editorKind = ServerSettingEditorKind.Text,
        IEnumerable<ServerSettingChoice>? choices = null)
    {
        RegisterOrReplace(new ServerSettingDescriptor(
            key,
            displayName,
            value,
            ServerSettingApplyMode.ApplyWithoutRestart,
            description,
            editorKind,
            choices));
    }

    public void RegisterRestartRequired(
        string key,
        string displayName,
        string value,
        string description = "",
        ServerSettingEditorKind editorKind = ServerSettingEditorKind.Text,
        IEnumerable<ServerSettingChoice>? choices = null)
    {
        RegisterOrReplace(new ServerSettingDescriptor(
            key,
            displayName,
            value,
            ServerSettingApplyMode.RestartRequired,
            description,
            editorKind,
            choices));
    }

    public bool Remove(string key)
    {
        string normalized = ServerSettingDescriptor.NormalizeKey(key);
        int index = IndexOf(normalized);
        if (index < 0)
            return false;

        _settings.RemoveAt(index);
        return true;
    }

    public bool TryGetSetting(string key, out ServerSettingDescriptor descriptor)
    {
        string normalized = ServerSettingDescriptor.NormalizeKey(key);
        int index = IndexOf(normalized);
        if (index < 0)
        {
            descriptor = null!;
            return false;
        }

        descriptor = _settings[index];
        return true;
    }

    public IReadOnlyList<ServerSettingDescriptor> Snapshot()
    {
        return _settings.ToArray();
    }

    private int IndexOf(string normalizedKey)
    {
        for (int i = 0; i < _settings.Count; i++)
        {
            if (string.Equals(_settings[i].Key, normalizedKey, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
