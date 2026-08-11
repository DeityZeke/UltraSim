using UltraSim.Server.Persistence;
using UltraSim.Server.Discovery;
using UltraSim.Server.Wrappers;

namespace UltraSim.Server.Headless;

public static class HeadlessServerArgumentParser
{
    public static bool TryParse(
        string[] args,
        out HeadlessServerShellOptions options,
        out string? error,
        out bool showHelp)
    {
        string profileName = HeadlessServerShellOptions.Default.ProfileName;
        int tickMilliseconds = HeadlessServerShellOptions.Default.TickMilliseconds;
        int maxCatchUpTicksPerPump = HeadlessServerShellOptions.Default.MaxCatchUpTicksPerPump;
        bool autoStart = HeadlessServerShellOptions.Default.AutoStart;
        bool saveOnStop = HeadlessServerShellOptions.Default.SaveOnStop;
        bool quiet = HeadlessServerShellOptions.Default.Quiet;
        TimeSpan? runFor = HeadlessServerShellOptions.Default.RunFor;
        TimeSpan idleDelay = HeadlessServerShellOptions.Default.IdleDelay;
        bool enableMovementWorld = HeadlessServerShellOptions.Default.EnableMovementWorld;
        int networkPort = HeadlessServerShellOptions.Default.NetworkPort;
        string bindAddress = HeadlessServerShellOptions.Default.BindAddress;
        int maxClients = HeadlessServerShellOptions.Default.MaxClients;
        double autoSaveIntervalSeconds = HeadlessServerShellOptions.Default.AutoSaveIntervalSeconds;
        double sessionAuthTimeoutSeconds = HeadlessServerShellOptions.Default.SessionAuthTimeoutSeconds;
        double sessionKeepAliveTimeoutSeconds = HeadlessServerShellOptions.Default.SessionKeepAliveTimeoutSeconds;
        string commandPrefix = HeadlessServerShellOptions.Default.CommandPrefix;
        int maxAutoCreatedAccountsPerRemoteAddress = HeadlessServerShellOptions.Default.MaxAutoCreatedAccountsPerRemoteAddress;
        string? runtimeRootPath = ServerWrapperSettingsConfigLoader.ReadRuntimeRootPath(args) ?? HeadlessServerShellOptions.Default.RuntimeRootPath;
        ServerPersistenceBackupStorageMode backupStorageMode = HeadlessServerShellOptions.Default.BackupStorageMode;
        int backupCount = HeadlessServerShellOptions.Default.BackupCount;
        int backupZstdCompressionLevel = HeadlessServerShellOptions.Default.BackupZstdCompressionLevel;
        bool discoveryEnabled = HeadlessServerShellOptions.Default.DiscoveryEnabled;
        string discoveryRegistryUrl = HeadlessServerShellOptions.Default.DiscoveryRegistryUrl;
        string discoveryPublishMode = HeadlessServerShellOptions.Default.DiscoveryPublishMode;
        string discoveryAdvertisedHost = HeadlessServerShellOptions.Default.DiscoveryAdvertisedHost;
        string discoveryServerId = HeadlessServerShellOptions.Default.DiscoveryServerId;
        string discoveryDescription = HeadlessServerShellOptions.Default.DiscoveryDescription;
        string discoveryRegion = HeadlessServerShellOptions.Default.DiscoveryRegion;
        string discoveryLanguage = HeadlessServerShellOptions.Default.DiscoveryLanguage;
        string discoveryTags = HeadlessServerShellOptions.Default.DiscoveryTags;
        double discoveryHeartbeatIntervalSeconds = HeadlessServerShellOptions.Default.DiscoveryHeartbeatIntervalSeconds;
        bool discoveryUnpublishOnStop = HeadlessServerShellOptions.Default.DiscoveryUnpublishOnStop;
        string discoveryStatusEndpointUrl = HeadlessServerShellOptions.Default.DiscoveryStatusEndpointUrl;
        bool discoveryLanEnabled = HeadlessServerShellOptions.Default.DiscoveryLanEnabled;
        int discoveryLanPort = HeadlessServerShellOptions.Default.DiscoveryLanPort;
        string discoveryLanBindAddress = HeadlessServerShellOptions.Default.DiscoveryLanBindAddress;

        ServerWrapperSettingsConfigValues configValues = ServerWrapperSettingsConfigLoader.Load(
            runtimeRootPath,
            new ServerWrapperSettingsConfigValues(
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
                discoveryStatusEndpointUrl,
                discoveryLanEnabled,
                discoveryLanPort,
                discoveryLanBindAddress));
        profileName = configValues.ProfileName;
        saveOnStop = configValues.SaveOnStop;
        networkPort = configValues.NetworkPort;
        bindAddress = configValues.BindAddress;
        maxClients = configValues.MaxClients;
        autoSaveIntervalSeconds = configValues.AutoSaveIntervalSeconds;
        sessionAuthTimeoutSeconds = configValues.SessionAuthTimeoutSeconds;
        sessionKeepAliveTimeoutSeconds = configValues.SessionKeepAliveTimeoutSeconds;
        commandPrefix = configValues.CommandPrefix;
        maxAutoCreatedAccountsPerRemoteAddress = configValues.MaxAutoCreatedAccountsPerRemoteAddress;
        backupStorageMode = configValues.BackupStorageMode;
        backupCount = configValues.BackupCount;
        backupZstdCompressionLevel = configValues.BackupZstdCompressionLevel;
        discoveryEnabled = configValues.DiscoveryEnabled;
        discoveryRegistryUrl = configValues.DiscoveryRegistryUrl;
        discoveryPublishMode = configValues.DiscoveryPublishMode;
        discoveryAdvertisedHost = configValues.DiscoveryAdvertisedHost;
        discoveryServerId = configValues.DiscoveryServerId;
        discoveryDescription = configValues.DiscoveryDescription;
        discoveryRegion = configValues.DiscoveryRegion;
        discoveryLanguage = configValues.DiscoveryLanguage;
        discoveryTags = configValues.DiscoveryTags;
        discoveryHeartbeatIntervalSeconds = configValues.DiscoveryHeartbeatIntervalSeconds;
        discoveryUnpublishOnStop = configValues.DiscoveryUnpublishOnStop;
        discoveryStatusEndpointUrl = configValues.DiscoveryStatusEndpointUrl;
        discoveryLanEnabled = configValues.DiscoveryLanEnabled;
        discoveryLanPort = configValues.DiscoveryLanPort;
        discoveryLanBindAddress = configValues.DiscoveryLanBindAddress;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                options = HeadlessServerShellOptions.Default;
                error = null;
                showHelp = true;
                return true;
            }

            if (string.Equals(arg, "--no-auto-start", StringComparison.OrdinalIgnoreCase))
            {
                autoStart = false;
                continue;
            }

            if (string.Equals(arg, "--save-on-stop", StringComparison.OrdinalIgnoreCase))
            {
                saveOnStop = true;
                continue;
            }

            if (string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
                continue;
            }

            if (string.Equals(arg, "--no-movement-world", StringComparison.OrdinalIgnoreCase))
            {
                enableMovementWorld = false;
                continue;
            }

            int separator = arg.IndexOf('=');
            if (separator <= 0 || separator == arg.Length - 1)
            {
                options = HeadlessServerShellOptions.Default;
                error = $"Unknown or malformed argument '{arg}'.";
                showHelp = false;
                return false;
            }

            string key = arg[..separator];
            string value = arg[(separator + 1)..];

            if (string.Equals(key, "--profile", StringComparison.OrdinalIgnoreCase))
            {
                profileName = value.Trim();
                continue;
            }

            if (string.Equals(key, "--runtime-root", StringComparison.OrdinalIgnoreCase))
            {
                runtimeRootPath = value.Trim();
                continue;
            }

            if (string.Equals(key, "--backup-storage", StringComparison.OrdinalIgnoreCase))
            {
                if (!ServerPersistenceBackupCompressorFactory.TryParseMode(value, out backupStorageMode))
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid backup storage mode '{value}'. Expected directory, zip, or zstd.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--backup-zstd-level", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out backupZstdCompressionLevel))
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid ZSTD compression level '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--backup-count", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out backupCount) || backupCount is < 0 or > 3)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid backup count '{value}'. Expected 0, 1, 2, or 3.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--command-prefix", StringComparison.OrdinalIgnoreCase))
            {
                commandPrefix = value.Trim();
                continue;
            }

            if (string.Equals(key, "--tick-ms", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out tickMilliseconds) || tickMilliseconds <= 0)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid tick milliseconds '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--max-catchup", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out maxCatchUpTicksPerPump) || maxCatchUpTicksPerPump <= 0)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid max catch-up ticks '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--port", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out networkPort) || (uint)networkPort > ushort.MaxValue)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid network port '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--bind-address", StringComparison.OrdinalIgnoreCase))
            {
                bindAddress = value.Trim();
                continue;
            }

            if (string.Equals(key, "--max-clients", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out maxClients) || maxClients < 0)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid max clients '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--autosave-interval-seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out autoSaveIntervalSeconds) || autoSaveIntervalSeconds < 0d)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid autosave interval '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--session-auth-timeout-seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out sessionAuthTimeoutSeconds) || sessionAuthTimeoutSeconds < 0d)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid session auth timeout '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--session-keepalive-timeout-seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out sessionKeepAliveTimeoutSeconds) || sessionKeepAliveTimeoutSeconds < 0d)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid session keepalive timeout '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--max-auto-created-accounts-per-ip", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out maxAutoCreatedAccountsPerRemoteAddress) || maxAutoCreatedAccountsPerRemoteAddress < 0)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid max auto-created accounts per IP '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--run-for-seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out double seconds) || seconds < 0d)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid run duration '{value}'.";
                    showHelp = false;
                    return false;
                }

                runFor = TimeSpan.FromSeconds(seconds);
                continue;
            }

            if (string.Equals(key, "--idle-delay-ms", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out int idleDelayMs) || idleDelayMs < 0)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid idle delay '{value}'.";
                    showHelp = false;
                    return false;
                }

                idleDelay = TimeSpan.FromMilliseconds(idleDelayMs);
                continue;
            }

            if (string.Equals(key, "--discovery-enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSwitch(value, out discoveryEnabled))
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid discovery enabled value '{value}'. Expected on/off.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--discovery-registry-url", StringComparison.OrdinalIgnoreCase))
            {
                discoveryRegistryUrl = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-publish-mode", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _ = ServerDiscoveryPublicationFactory.ParsePublishMode(value);
                }
                catch (ArgumentException)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid discovery publish mode '{value}'. Expected disabled, unverified, or verified.";
                    showHelp = false;
                    return false;
                }

                discoveryPublishMode = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-advertised-host", StringComparison.OrdinalIgnoreCase))
            {
                discoveryAdvertisedHost = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-server-id", StringComparison.OrdinalIgnoreCase))
            {
                discoveryServerId = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-description", StringComparison.OrdinalIgnoreCase))
            {
                discoveryDescription = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-region", StringComparison.OrdinalIgnoreCase))
            {
                discoveryRegion = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-language", StringComparison.OrdinalIgnoreCase))
            {
                discoveryLanguage = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-tags", StringComparison.OrdinalIgnoreCase))
            {
                discoveryTags = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-heartbeat-interval-seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out discoveryHeartbeatIntervalSeconds) || discoveryHeartbeatIntervalSeconds < 1d)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid discovery heartbeat interval '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--discovery-unpublish-on-stop", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSwitch(value, out discoveryUnpublishOnStop))
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid discovery unpublish value '{value}'. Expected on/off.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--discovery-status-endpoint-url", StringComparison.OrdinalIgnoreCase))
            {
                discoveryStatusEndpointUrl = value.Trim();
                continue;
            }

            if (string.Equals(key, "--discovery-lan-enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseSwitch(value, out discoveryLanEnabled))
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid LAN discovery enabled value '{value}'. Expected on/off.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--discovery-lan-port", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out discoveryLanPort) || (uint)discoveryLanPort > ushort.MaxValue)
                {
                    options = HeadlessServerShellOptions.Default;
                    error = $"Invalid LAN discovery port '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--discovery-lan-bind-address", StringComparison.OrdinalIgnoreCase))
            {
                discoveryLanBindAddress = value.Trim();
                continue;
            }

            options = HeadlessServerShellOptions.Default;
            error = $"Unknown argument '{arg}'.";
            showHelp = false;
            return false;
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            options = HeadlessServerShellOptions.Default;
            error = "Profile name cannot be empty.";
            showHelp = false;
            return false;
        }

        options = new HeadlessServerShellOptions(
            profileName.Trim(),
            TickMilliseconds: tickMilliseconds,
            MaxCatchUpTicksPerPump: maxCatchUpTicksPerPump,
            AutoStart: autoStart,
            SaveOnStop: saveOnStop,
            Quiet: quiet,
            RunFor: runFor,
            IdleDelay: idleDelay,
            EnableMovementWorld: enableMovementWorld,
            NetworkPort: networkPort,
            BindAddress: bindAddress,
            MaxClients: maxClients,
            AutoSaveIntervalSeconds: autoSaveIntervalSeconds,
            SessionAuthTimeoutSeconds: sessionAuthTimeoutSeconds,
            SessionKeepAliveTimeoutSeconds: sessionKeepAliveTimeoutSeconds,
            CommandPrefix: commandPrefix,
            MaxAutoCreatedAccountsPerRemoteAddress: maxAutoCreatedAccountsPerRemoteAddress,
            RuntimeRootPath: runtimeRootPath,
            BackupStorageMode: backupStorageMode,
            BackupCount: backupCount,
            BackupZstdCompressionLevel: backupZstdCompressionLevel,
            DiscoveryEnabled: discoveryEnabled,
            DiscoveryRegistryUrl: discoveryRegistryUrl,
            DiscoveryPublishMode: discoveryPublishMode,
            DiscoveryAdvertisedHost: discoveryAdvertisedHost,
            DiscoveryServerId: discoveryServerId,
            DiscoveryDescription: discoveryDescription,
            DiscoveryRegion: discoveryRegion,
            DiscoveryLanguage: discoveryLanguage,
            DiscoveryTags: discoveryTags,
            DiscoveryHeartbeatIntervalSeconds: discoveryHeartbeatIntervalSeconds,
            DiscoveryUnpublishOnStop: discoveryUnpublishOnStop,
            DiscoveryStatusEndpointUrl: discoveryStatusEndpointUrl,
            DiscoveryLanEnabled: discoveryLanEnabled,
            DiscoveryLanPort: discoveryLanPort,
            DiscoveryLanBindAddress: discoveryLanBindAddress);
        error = null;
        showHelp = false;
        return true;
    }

    public static string GetUsage() =>
        """
        UltraSim.Server.Headless options:
          --profile=<name>
          --runtime-root=<path>
          --backup-storage=<directory|zip|zstd>
          --backup-count=<0-3>
          --backup-zstd-level=<n>
          --tick-ms=<n>
          --max-catchup=<n>
          --port=<0-65535>
          --bind-address=<ip|any|localhost>
          --max-clients=<n>
          --autosave-interval-seconds=<seconds>
          --session-auth-timeout-seconds=<seconds>
          --session-keepalive-timeout-seconds=<seconds>
          --command-prefix=<prefix>
          --max-auto-created-accounts-per-ip=<n>
          --run-for-seconds=<seconds>
          --idle-delay-ms=<n>
          --discovery-enabled=<on|off>
          --discovery-registry-url=<url>
          --discovery-publish-mode=<disabled|unverified|verified>
          --discovery-advertised-host=<host>
          --discovery-server-id=<id>
          --discovery-description=<text>
          --discovery-region=<region>
          --discovery-language=<language>
          --discovery-tags=<comma-separated-tags>
          --discovery-heartbeat-interval-seconds=<seconds>
          --discovery-unpublish-on-stop=<on|off>
          --discovery-status-endpoint-url=<url>
          --discovery-lan-enabled=<on|off>
          --discovery-lan-port=<0-65535>
          --discovery-lan-bind-address=<ip|any|localhost>
          --no-movement-world
          --no-auto-start
          --save-on-stop
          --quiet
          --help
        """;

    private static bool TryParseSwitch(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "on":
            case "true":
            case "yes":
            case "1":
                result = true;
                return true;
            case "off":
            case "false":
            case "no":
            case "0":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
