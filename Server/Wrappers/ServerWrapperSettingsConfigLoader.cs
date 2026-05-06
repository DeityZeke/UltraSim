using System.Globalization;
using UltraSim.Server.Persistence;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Wrappers;

internal sealed record ServerWrapperSettingsConfigValues(
    string ProfileName,
    bool SaveOnStop,
    int NetworkPort,
    int MaxClients,
    double SessionAuthTimeoutSeconds,
    double SessionKeepAliveTimeoutSeconds,
    string CommandPrefix,
    int MaxAutoCreatedAccountsPerRemoteAddress,
    ServerPersistenceBackupStorageMode BackupStorageMode,
    int BackupCount,
    int BackupZstdCompressionLevel);

internal static class ServerWrapperSettingsConfigLoader
{
    public static string? ReadRuntimeRootPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            int separator = arg.IndexOf('=');
            if (separator <= 0 || separator == arg.Length - 1)
                continue;

            string key = arg[..separator];
            if (!string.Equals(key, "--runtime-root", StringComparison.OrdinalIgnoreCase))
                continue;

            string value = arg[(separator + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    public static ServerWrapperSettingsConfigValues Load(
        string? runtimeRootPath,
        ServerWrapperSettingsConfigValues defaults)
    {
        try
        {
            UltraSimRuntimeRootLayout runtimeRoot = UltraSimRuntimeRootResolver.Resolve(runtimeRootPath);
            ServerSettingsConfigDocument document = new ServerSettingsConfigStore()
                .LoadOrEmpty(ServerSettingsConfigPaths.GetPath(runtimeRoot.RootPath));
            ServerSettingsRegistry registry = BuildSettingsRegistry(defaults);

            var proposed = new List<ServerSettingProposedValue>();
            foreach (ServerSettingProposedValue value in document.ToProposedValues())
            {
                if (registry.TryGetSetting(value.Key, out _))
                    proposed.Add(value);
            }

            ServerSettingsChangeSetValidationResult validation = registry.ValidateChanges(proposed);
            return ApplyValidatedChanges(defaults, validation.Changes);
        }
        catch
        {
            return defaults;
        }
    }

    private static ServerSettingsRegistry BuildSettingsRegistry(ServerWrapperSettingsConfigValues values)
    {
        var registry = new ServerSettingsRegistry();
        registry.RegisterRestartRequired("server.name", "Server name", values.ProfileName);
        registry.RegisterRestartRequired(
            "server.network.port",
            "Network port",
            values.NetworkPort.ToString(CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Integer);
        registry.RegisterRestartRequired(
            "server.network.max_clients",
            "Max clients",
            values.MaxClients.ToString(CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Integer);
        registry.RegisterRestartRequired(
            "server.save.on_stop",
            "Save on stop",
            values.SaveOnStop ? "on" : "off",
            editorKind: ServerSettingEditorKind.Boolean);
        registry.RegisterRestartRequired(
            "server.session.auth_timeout_seconds",
            "Session auth timeout seconds",
            values.SessionAuthTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Decimal);
        registry.RegisterRestartRequired(
            "server.session.keepalive_timeout_seconds",
            "Session keepalive timeout seconds",
            values.SessionKeepAliveTimeoutSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Decimal);
        registry.RegisterRestartRequired(
            "server.accounts.max_auto_created_per_ip",
            "Max auto-created accounts per IP",
            values.MaxAutoCreatedAccountsPerRemoteAddress.ToString(CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Integer);
        registry.RegisterRestartRequired(
            "server.persistence.backup_storage",
            "Backup compression",
            FormatBackupStorageMode(values.BackupStorageMode),
            editorKind: ServerSettingEditorKind.Choice,
            choices:
            [
                new ServerSettingChoice("off", "off"),
                new ServerSettingChoice("zip", "zip"),
                new ServerSettingChoice("zstd", "zstd")
            ]);
        registry.RegisterRestartRequired(
            "server.persistence.backup_count",
            "Backup count",
            values.BackupCount.ToString(CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Choice,
            choices:
            [
                new ServerSettingChoice("0", "0"),
                new ServerSettingChoice("1", "1"),
                new ServerSettingChoice("2", "2"),
                new ServerSettingChoice("3", "3")
            ]);
        registry.RegisterRestartRequired(
            "server.persistence.backup_zstd_level",
            "Backup ZSTD level",
            values.BackupZstdCompressionLevel.ToString(CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Integer);
        registry.RegisterRestartRequired("server.chat.command_prefix", "Command prefix", values.CommandPrefix);
        return registry;
    }

    private static ServerWrapperSettingsConfigValues ApplyValidatedChanges(
        ServerWrapperSettingsConfigValues values,
        IReadOnlyList<ServerSettingValidatedChange> changes)
    {
        string profileName = values.ProfileName;
        bool saveOnStop = values.SaveOnStop;
        int networkPort = values.NetworkPort;
        int maxClients = values.MaxClients;
        double sessionAuthTimeoutSeconds = values.SessionAuthTimeoutSeconds;
        double sessionKeepAliveTimeoutSeconds = values.SessionKeepAliveTimeoutSeconds;
        string commandPrefix = values.CommandPrefix;
        int maxAutoCreatedAccountsPerRemoteAddress = values.MaxAutoCreatedAccountsPerRemoteAddress;
        ServerPersistenceBackupStorageMode backupStorageMode = values.BackupStorageMode;
        int backupCount = values.BackupCount;
        int backupZstdCompressionLevel = values.BackupZstdCompressionLevel;

        for (int i = 0; i < changes.Count; i++)
        {
            ServerSettingValidatedChange change = changes[i];
            switch (change.Key)
            {
                case "server.name":
                    if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
                        profileName = change.NormalizedValue;
                    break;
                case "server.network.port":
                    if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
                        && parsedPort >= 0
                        && parsedPort <= ushort.MaxValue)
                    {
                        networkPort = parsedPort;
                    }
                    break;
                case "server.network.max_clients":
                    if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMaxClients))
                        maxClients = Math.Max(0, parsedMaxClients);
                    break;
                case "server.save.on_stop":
                    saveOnStop = string.Equals(change.NormalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "server.session.auth_timeout_seconds":
                    if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAuthTimeout)
                        && parsedAuthTimeout >= 0d)
                    {
                        sessionAuthTimeoutSeconds = parsedAuthTimeout;
                    }
                    break;
                case "server.session.keepalive_timeout_seconds":
                    if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedKeepAliveTimeout)
                        && parsedKeepAliveTimeout >= 0d)
                    {
                        sessionKeepAliveTimeoutSeconds = parsedKeepAliveTimeout;
                    }
                    break;
                case "server.chat.command_prefix":
                    if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
                        commandPrefix = change.NormalizedValue;
                    break;
                case "server.accounts.max_auto_created_per_ip":
                    if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAccountLimit)
                        && parsedAccountLimit >= 0)
                    {
                        maxAutoCreatedAccountsPerRemoteAddress = parsedAccountLimit;
                    }
                    break;
                case "server.persistence.backup_storage":
                    if (ServerPersistenceBackupCompressorFactory.TryParseMode(change.NormalizedValue, out ServerPersistenceBackupStorageMode parsedBackupStorageMode))
                        backupStorageMode = parsedBackupStorageMode;
                    break;
                case "server.persistence.backup_count":
                    if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBackupCount))
                        backupCount = Math.Clamp(parsedBackupCount, 0, 3);
                    break;
                case "server.persistence.backup_zstd_level":
                    if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBackupZstdLevel))
                        backupZstdCompressionLevel = parsedBackupZstdLevel;
                    break;
            }
        }

        return new ServerWrapperSettingsConfigValues(
            profileName,
            saveOnStop,
            networkPort,
            maxClients,
            sessionAuthTimeoutSeconds,
            sessionKeepAliveTimeoutSeconds,
            commandPrefix,
            maxAutoCreatedAccountsPerRemoteAddress,
            backupStorageMode,
            backupCount,
            backupZstdCompressionLevel);
    }

    private static string FormatBackupStorageMode(ServerPersistenceBackupStorageMode mode)
    {
        return mode switch
        {
            ServerPersistenceBackupStorageMode.Directory => "off",
            ServerPersistenceBackupStorageMode.Zip => "zip",
            ServerPersistenceBackupStorageMode.Zstd => "zstd",
            _ => mode.ToString()
        };
    }
}
