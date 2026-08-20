namespace UltraSim.Server;

public sealed record ServerSettingValidatedChange(
    string Key,
    string DisplayName,
    string PreviousValue,
    string NormalizedValue,
    ServerSettingApplyMode ApplyMode);
