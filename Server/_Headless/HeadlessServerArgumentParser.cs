using UltraSim.Server.Persistence;
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
        double sessionAuthTimeoutSeconds = HeadlessServerShellOptions.Default.SessionAuthTimeoutSeconds;
        double sessionKeepAliveTimeoutSeconds = HeadlessServerShellOptions.Default.SessionKeepAliveTimeoutSeconds;
        string commandPrefix = HeadlessServerShellOptions.Default.CommandPrefix;
        int maxAutoCreatedAccountsPerRemoteAddress = HeadlessServerShellOptions.Default.MaxAutoCreatedAccountsPerRemoteAddress;
        string? runtimeRootPath = ServerWrapperSettingsConfigLoader.ReadRuntimeRootPath(args) ?? HeadlessServerShellOptions.Default.RuntimeRootPath;
        ServerPersistenceBackupStorageMode backupStorageMode = HeadlessServerShellOptions.Default.BackupStorageMode;
        int backupZstdCompressionLevel = HeadlessServerShellOptions.Default.BackupZstdCompressionLevel;

        ServerWrapperSettingsConfigValues configValues = ServerWrapperSettingsConfigLoader.Load(
            runtimeRootPath,
            new ServerWrapperSettingsConfigValues(
                profileName,
                saveOnStop,
                networkPort,
                sessionAuthTimeoutSeconds,
                sessionKeepAliveTimeoutSeconds,
                commandPrefix,
                maxAutoCreatedAccountsPerRemoteAddress,
                backupStorageMode,
                backupZstdCompressionLevel));
        profileName = configValues.ProfileName;
        saveOnStop = configValues.SaveOnStop;
        networkPort = configValues.NetworkPort;
        sessionAuthTimeoutSeconds = configValues.SessionAuthTimeoutSeconds;
        sessionKeepAliveTimeoutSeconds = configValues.SessionKeepAliveTimeoutSeconds;
        commandPrefix = configValues.CommandPrefix;
        maxAutoCreatedAccountsPerRemoteAddress = configValues.MaxAutoCreatedAccountsPerRemoteAddress;
        backupStorageMode = configValues.BackupStorageMode;
        backupZstdCompressionLevel = configValues.BackupZstdCompressionLevel;

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
            SessionAuthTimeoutSeconds: sessionAuthTimeoutSeconds,
            SessionKeepAliveTimeoutSeconds: sessionKeepAliveTimeoutSeconds,
            CommandPrefix: commandPrefix,
            MaxAutoCreatedAccountsPerRemoteAddress: maxAutoCreatedAccountsPerRemoteAddress,
            RuntimeRootPath: runtimeRootPath,
            BackupStorageMode: backupStorageMode,
            BackupZstdCompressionLevel: backupZstdCompressionLevel);
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
          --backup-zstd-level=<n>
          --tick-ms=<n>
          --max-catchup=<n>
          --port=<0-65535>
          --session-auth-timeout-seconds=<seconds>
          --session-keepalive-timeout-seconds=<seconds>
          --command-prefix=<prefix>
          --max-auto-created-accounts-per-ip=<n>
          --run-for-seconds=<seconds>
          --idle-delay-ms=<n>
          --no-movement-world
          --no-auto-start
          --save-on-stop
          --quiet
          --help
        """;

}
