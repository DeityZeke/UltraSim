using UltraSim.Server.Persistence;

namespace UltraSim.Server.Console;

public sealed record ConsoleServerShellOptions(
    string ProfileName,
    int TickMilliseconds = 16,
    int MaxCatchUpTicksPerPump = 8,
    bool AutoStart = true,
    bool SaveOnStop = false,
    bool EnableInput = true,
    TimeSpan? RunFor = null,
    TimeSpan IdleDelay = default,
    bool EnableMovementWorld = true,
    int NetworkPort = 42042,
    int MaxClients = 0,
    double SessionAuthTimeoutSeconds = 5.0,
    double SessionKeepAliveTimeoutSeconds = 10.0,
    string CommandPrefix = "/",
    int MaxAutoCreatedAccountsPerRemoteAddress = 1,
    string? RuntimeRootPath = null,
    ServerPersistenceBackupStorageMode BackupStorageMode = ServerPersistenceBackupStorageMode.Directory,
    int BackupCount = 3,
    int BackupZstdCompressionLevel = 3)
{
    public static ConsoleServerShellOptions Default { get; } =
        new("console", IdleDelay: TimeSpan.FromMilliseconds(1));
}
