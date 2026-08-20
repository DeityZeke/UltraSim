namespace UltraSim.Server;

public sealed record ServerSettingsChangeSetValidationResult
{
    public ServerSettingsChangeSetValidationResult(
        IEnumerable<ServerSettingValidatedChange>? changes = null,
        IEnumerable<ServerSettingChangeError>? errors = null)
    {
        Changes = changes?.ToArray() ?? [];
        Errors = errors?.ToArray() ?? [];
    }

    public IReadOnlyList<ServerSettingValidatedChange> Changes { get; }

    public IReadOnlyList<ServerSettingChangeError> Errors { get; }

    public bool IsValid => Errors.Count == 0;
}
