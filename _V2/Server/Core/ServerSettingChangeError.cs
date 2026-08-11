namespace UltraSim.Server;

public sealed record ServerSettingChangeError(
    string Key,
    string Error);
