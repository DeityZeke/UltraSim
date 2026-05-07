using System.Globalization;
using UltraSim.Server.Persistence;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Wrappers;

internal sealed record ServerWrapperSettingsConfigValues(
    string ProfileName,
    bool SaveOnStop,
    int NetworkPort,
    string BindAddress,
    int MaxClients,
    double AutoSaveIntervalSeconds,
    double SessionAuthTimeoutSeconds,
    double SessionKeepAliveTimeoutSeconds,
    string CommandPrefix,
    int MaxAutoCreatedAccountsPerRemoteAddress,
    ServerPersistenceBackupStorageMode BackupStorageMode,
    int BackupCount,
    int BackupZstdCompressionLevel,
    bool DiscoveryEnabled,
    string DiscoveryRegistryUrl,
    string DiscoveryPublishMode,
    string DiscoveryAdvertisedHost,
    string DiscoveryServerId,
    string DiscoveryDescription,
    string DiscoveryRegion,
    string DiscoveryLanguage,
    string DiscoveryTags,
    double DiscoveryHeartbeatIntervalSeconds,
    bool DiscoveryUnpublishOnStop,
    string DiscoveryStatusEndpointUrl);

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
        registry.RegisterRestartRequired("server.network.bind_address", "Bind address", values.BindAddress);
        registry.RegisterRestartRequired(
            "server.network.max_clients",
            "Max clients",
            values.MaxClients.ToString(CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Integer);
        registry.RegisterRestartRequired(
            "server.save.autosave_interval_seconds",
            "Autosave interval",
            values.AutoSaveIntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Choice,
            choices: CreateAutoSaveIntervalChoices());
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
        registry.RegisterRestartRequired(
            "server.discovery.enabled",
            "Discovery enabled",
            values.DiscoveryEnabled ? "on" : "off",
            editorKind: ServerSettingEditorKind.Boolean);
        registry.RegisterRestartRequired(
            "server.discovery.registry_url",
            "Discovery registry URL",
            values.DiscoveryRegistryUrl);
        registry.RegisterRestartRequired(
            "server.discovery.publish_mode",
            "Discovery publish mode",
            values.DiscoveryPublishMode,
            editorKind: ServerSettingEditorKind.Choice,
            choices:
            [
                new ServerSettingChoice("unverified", "unverified"),
                new ServerSettingChoice("verified", "verified")
            ]);
        registry.RegisterRestartRequired(
            "server.discovery.advertised_host",
            "Discovery advertised host",
            values.DiscoveryAdvertisedHost);
        registry.RegisterRestartRequired(
            "server.discovery.server_id",
            "Discovery server ID",
            values.DiscoveryServerId);
        registry.RegisterRestartRequired(
            "server.discovery.description",
            "Discovery description",
            values.DiscoveryDescription);
        registry.RegisterRestartRequired(
            "server.discovery.region",
            "Discovery region",
            values.DiscoveryRegion);
        registry.RegisterRestartRequired(
            "server.discovery.language",
            "Discovery language",
            values.DiscoveryLanguage);
        registry.RegisterRestartRequired(
            "server.discovery.tags",
            "Discovery tags",
            values.DiscoveryTags);
        registry.RegisterRestartRequired(
            "server.discovery.heartbeat_interval_seconds",
            "Discovery heartbeat interval",
            values.DiscoveryHeartbeatIntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            editorKind: ServerSettingEditorKind.Decimal);
        registry.RegisterRestartRequired(
            "server.discovery.unpublish_on_stop",
            "Discovery unpublish on stop",
            values.DiscoveryUnpublishOnStop ? "on" : "off",
            editorKind: ServerSettingEditorKind.Boolean);
        registry.RegisterRestartRequired(
            "server.discovery.status_endpoint_url",
            "Discovery status endpoint URL",
            values.DiscoveryStatusEndpointUrl);
        return registry;
    }

    private static ServerWrapperSettingsConfigValues ApplyValidatedChanges(
        ServerWrapperSettingsConfigValues values,
        IReadOnlyList<ServerSettingValidatedChange> changes)
    {
        string profileName = values.ProfileName;
        bool saveOnStop = values.SaveOnStop;
        int networkPort = values.NetworkPort;
        string bindAddress = values.BindAddress;
        int maxClients = values.MaxClients;
        double autoSaveIntervalSeconds = values.AutoSaveIntervalSeconds;
        double sessionAuthTimeoutSeconds = values.SessionAuthTimeoutSeconds;
        double sessionKeepAliveTimeoutSeconds = values.SessionKeepAliveTimeoutSeconds;
        string commandPrefix = values.CommandPrefix;
        int maxAutoCreatedAccountsPerRemoteAddress = values.MaxAutoCreatedAccountsPerRemoteAddress;
        ServerPersistenceBackupStorageMode backupStorageMode = values.BackupStorageMode;
        int backupCount = values.BackupCount;
        int backupZstdCompressionLevel = values.BackupZstdCompressionLevel;
        bool discoveryEnabled = values.DiscoveryEnabled;
        string discoveryRegistryUrl = values.DiscoveryRegistryUrl;
        string discoveryPublishMode = values.DiscoveryPublishMode;
        string discoveryAdvertisedHost = values.DiscoveryAdvertisedHost;
        string discoveryServerId = values.DiscoveryServerId;
        string discoveryDescription = values.DiscoveryDescription;
        string discoveryRegion = values.DiscoveryRegion;
        string discoveryLanguage = values.DiscoveryLanguage;
        string discoveryTags = values.DiscoveryTags;
        double discoveryHeartbeatIntervalSeconds = values.DiscoveryHeartbeatIntervalSeconds;
        bool discoveryUnpublishOnStop = values.DiscoveryUnpublishOnStop;
        string discoveryStatusEndpointUrl = values.DiscoveryStatusEndpointUrl;

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
                case "server.network.bind_address":
                    if (!string.IsNullOrWhiteSpace(change.NormalizedValue))
                        bindAddress = change.NormalizedValue;
                    break;
                case "server.network.max_clients":
                    if (int.TryParse(change.NormalizedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMaxClients))
                        maxClients = Math.Max(0, parsedMaxClients);
                    break;
                case "server.save.autosave_interval_seconds":
                    if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedAutoSaveIntervalSeconds))
                        autoSaveIntervalSeconds = Math.Max(0.0, parsedAutoSaveIntervalSeconds);
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
                case "server.discovery.enabled":
                    discoveryEnabled = string.Equals(change.NormalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "server.discovery.registry_url":
                    discoveryRegistryUrl = change.NormalizedValue;
                    break;
                case "server.discovery.publish_mode":
                    discoveryPublishMode = change.NormalizedValue;
                    break;
                case "server.discovery.advertised_host":
                    discoveryAdvertisedHost = change.NormalizedValue;
                    break;
                case "server.discovery.server_id":
                    discoveryServerId = change.NormalizedValue;
                    break;
                case "server.discovery.description":
                    discoveryDescription = change.NormalizedValue;
                    break;
                case "server.discovery.region":
                    discoveryRegion = change.NormalizedValue;
                    break;
                case "server.discovery.language":
                    discoveryLanguage = change.NormalizedValue;
                    break;
                case "server.discovery.tags":
                    discoveryTags = change.NormalizedValue;
                    break;
                case "server.discovery.heartbeat_interval_seconds":
                    if (double.TryParse(change.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDiscoveryHeartbeat)
                        && parsedDiscoveryHeartbeat >= 1d)
                    {
                        discoveryHeartbeatIntervalSeconds = parsedDiscoveryHeartbeat;
                    }
                    break;
                case "server.discovery.unpublish_on_stop":
                    discoveryUnpublishOnStop = string.Equals(change.NormalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "server.discovery.status_endpoint_url":
                    discoveryStatusEndpointUrl = change.NormalizedValue;
                    break;
            }
        }

        return new ServerWrapperSettingsConfigValues(
            profileName,
            saveOnStop,
            networkPort,
            bindAddress,
            maxClients,
            autoSaveIntervalSeconds,
            sessionAuthTimeoutSeconds,
            sessionKeepAliveTimeoutSeconds,
            commandPrefix,
            maxAutoCreatedAccountsPerRemoteAddress,
            backupStorageMode,
            backupCount,
            backupZstdCompressionLevel,
            discoveryEnabled,
            discoveryRegistryUrl,
            discoveryPublishMode,
            discoveryAdvertisedHost,
            discoveryServerId,
            discoveryDescription,
            discoveryRegion,
            discoveryLanguage,
            discoveryTags,
            discoveryHeartbeatIntervalSeconds,
            discoveryUnpublishOnStop,
            discoveryStatusEndpointUrl);
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

    private static ServerSettingChoice[] CreateAutoSaveIntervalChoices() =>
    [
        new ServerSettingChoice("0", "off"),
        new ServerSettingChoice("60", "1m"),
        new ServerSettingChoice("300", "5m"),
        new ServerSettingChoice("900", "15m"),
        new ServerSettingChoice("1800", "30m"),
        new ServerSettingChoice("3600", "60m")
    ];
}
