namespace UltraSim.Server.Console;

public static class ConsoleServerArgumentParser
{
    public static bool TryParse(
        string[] args,
        out ConsoleServerShellOptions options,
        out string? error,
        out bool showHelp)
    {
        string profileName = ConsoleServerShellOptions.Default.ProfileName;
        int tickMilliseconds = ConsoleServerShellOptions.Default.TickMilliseconds;
        int maxCatchUpTicksPerPump = ConsoleServerShellOptions.Default.MaxCatchUpTicksPerPump;
        bool autoStart = ConsoleServerShellOptions.Default.AutoStart;
        bool saveOnStop = ConsoleServerShellOptions.Default.SaveOnStop;
        bool enableInput = ConsoleServerShellOptions.Default.EnableInput;
        TimeSpan? runFor = ConsoleServerShellOptions.Default.RunFor;
        TimeSpan idleDelay = ConsoleServerShellOptions.Default.IdleDelay;
        bool enableMovementWorld = ConsoleServerShellOptions.Default.EnableMovementWorld;
        int networkPort = ConsoleServerShellOptions.Default.NetworkPort;
        double sessionAuthTimeoutSeconds = ConsoleServerShellOptions.Default.SessionAuthTimeoutSeconds;
        double sessionKeepAliveTimeoutSeconds = ConsoleServerShellOptions.Default.SessionKeepAliveTimeoutSeconds;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                options = ConsoleServerShellOptions.Default;
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

            if (string.Equals(arg, "--no-input", StringComparison.OrdinalIgnoreCase))
            {
                enableInput = false;
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
                options = ConsoleServerShellOptions.Default;
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

            if (string.Equals(key, "--tick-ms", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out tickMilliseconds) || tickMilliseconds <= 0)
                {
                    options = ConsoleServerShellOptions.Default;
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
                    options = ConsoleServerShellOptions.Default;
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
                    options = ConsoleServerShellOptions.Default;
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
                    options = ConsoleServerShellOptions.Default;
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
                    options = ConsoleServerShellOptions.Default;
                    error = $"Invalid session keepalive timeout '{value}'.";
                    showHelp = false;
                    return false;
                }

                continue;
            }

            if (string.Equals(key, "--run-for-seconds", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(value, out double seconds) || seconds < 0d)
                {
                    options = ConsoleServerShellOptions.Default;
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
                    options = ConsoleServerShellOptions.Default;
                    error = $"Invalid idle delay '{value}'.";
                    showHelp = false;
                    return false;
                }

                idleDelay = TimeSpan.FromMilliseconds(idleDelayMs);
                continue;
            }

            options = ConsoleServerShellOptions.Default;
            error = $"Unknown argument '{arg}'.";
            showHelp = false;
            return false;
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            options = ConsoleServerShellOptions.Default;
            error = "Profile name cannot be empty.";
            showHelp = false;
            return false;
        }

        options = new ConsoleServerShellOptions(
            profileName.Trim(),
            TickMilliseconds: tickMilliseconds,
            MaxCatchUpTicksPerPump: maxCatchUpTicksPerPump,
            AutoStart: autoStart,
            SaveOnStop: saveOnStop,
            EnableInput: enableInput,
            RunFor: runFor,
            IdleDelay: idleDelay,
            EnableMovementWorld: enableMovementWorld,
            NetworkPort: networkPort,
            SessionAuthTimeoutSeconds: sessionAuthTimeoutSeconds,
            SessionKeepAliveTimeoutSeconds: sessionKeepAliveTimeoutSeconds);
        error = null;
        showHelp = false;
        return true;
    }

    public static string GetUsage() =>
        """
        UltraSim.Server.Console options:
          --profile=<name>
          --tick-ms=<n>
          --max-catchup=<n>
          --port=<0-65535>
          --session-auth-timeout-seconds=<seconds>
          --session-keepalive-timeout-seconds=<seconds>
          --run-for-seconds=<seconds>
          --idle-delay-ms=<n>
          --no-movement-world
          --no-auto-start
          --save-on-stop
          --no-input
          --help

        Console commands:
          start
          status
          save
          restart
          stop
          shutdown
          help
        """;
}
