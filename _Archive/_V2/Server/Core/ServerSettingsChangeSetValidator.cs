namespace UltraSim.Server;

public static class ServerSettingsChangeSetValidator
{
    public static ServerSettingsChangeSetValidationResult Validate(
        ServerSettingsRegistry registry,
        IEnumerable<ServerSettingProposedValue> proposedValues)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(proposedValues);

        var changes = new List<ServerSettingValidatedChange>();
        var errors = new List<ServerSettingChangeError>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (ServerSettingProposedValue proposed in proposedValues)
        {
            if (!seenKeys.Add(proposed.Key))
            {
                errors.Add(new ServerSettingChangeError(proposed.Key, "Setting was edited more than once."));
                continue;
            }

            if (!registry.TryGetSetting(proposed.Key, out ServerSettingDescriptor descriptor))
            {
                errors.Add(new ServerSettingChangeError(proposed.Key, "Unknown setting."));
                continue;
            }

            ServerSettingValidationResult validation = ServerSettingValueValidator.Validate(descriptor, proposed.Value);
            if (!validation.IsValid)
            {
                errors.Add(new ServerSettingChangeError(proposed.Key, validation.Error));
                continue;
            }

            changes.Add(new ServerSettingValidatedChange(
                descriptor.Key,
                descriptor.DisplayName,
                descriptor.Value,
                validation.NormalizedValue,
                descriptor.ApplyMode));
        }

        return new ServerSettingsChangeSetValidationResult(changes, errors);
    }
}
